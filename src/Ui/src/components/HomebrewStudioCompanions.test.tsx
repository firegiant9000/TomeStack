// @vitest-environment jsdom
// MECH-X item 4 (T5): the studio adds and edits a companion.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: { listSources: vi.fn(), contentBySource: vi.fn(), publishManyPreview: vi.fn(), createHomebrewSource: vi.fn(), previewFormula: vi.fn(), listCharacters: vi.fn(), validateRevision: vi.fn(), spellListKeys: vi.fn() },
}));

const info = { rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }], fields: [{ id: 'initiative', label: 'Initiative' }] } as unknown as AppInfo;
const source = { id: 'a', title: 'Testy Alpha', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.spellListKeys).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-c', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[], kind = 'feature') {
  const entry = {
    contentId: 'id-c',
    name: 'Testy Feature',
    kind,
    revisions: [],
    latest: { contentId: 'id-c', revisionId: 'r-c', name: 'Testy Feature', kind, status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Feature' }));
  const rule = (name: RegExp) => within(screen.getByRole('group', { name }));
  const checked = async () => {
    vi.mocked(client.validateRevision).mockClear();
    await user.click(screen.getByRole('button', { name: 'Check' }));
    await waitFor(() => expect(vi.mocked(client.validateRevision).mock.calls.length).toBeGreaterThan(0));
    const sent = vi.mocked(client.validateRevision).mock.calls.at(-1)![0] as unknown as { effects: Raw[] };
    return { effect: (id: string) => sent.effects.find((e) => e.id === id), ofType: (type: string) => sent.effects.find((e) => e.type === type), count: sent.effects.length };
  };
  return { rule, checked };
}

const bite: Raw = { type: 'attack', id: 'attack-1', attackId: 'bite', label: 'Bite', kind: 'melee', abilities: ['str'], damage: { count: '1', die: 'd6' }, damageType: 'piercing' };

it('adds a companion to a feature with the starting values and sends it', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [bite]);
  await user.click(screen.getByRole('button', { name: 'Add companion' }));
  const c = rule(/^Rule 2: Companion/);
  expect((c.getByRole('textbox', { name: 'Armor Class formula' }) as HTMLInputElement).value).toBe('13');
  expect((c.getByRole('textbox', { name: 'Hit point maximum formula' }) as HTMLInputElement).value).toBe('5 * LEVEL');
  await user.type(c.getByRole('textbox', { name: 'Label' }), 'Hound');
  await user.click(c.getByRole('checkbox', { name: 'Bite' }));
  const sent = (await checked()).ofType('companion') as Raw;
  expect(sent).toMatchObject({ type: 'companion', label: 'Hound', armorClass: '13', hitPointMaximum: '5 * LEVEL', attacks: ['attack-1'] });
  expect(sent.companionId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
});

it('keeps the companionId of a stored companion through an edit', async () => {
  const user = userEvent.setup();
  const stored: Raw = { type: 'companion', id: 'companion-1', companionId: '3f2504e0-4f89-41d3-9a0c-0305e82c3301', label: 'Pet', armorClass: '13', hitPointMaximum: '5 * LEVEL' };
  const { rule, checked } = await openDraft(user, [stored]);
  await user.type(rule(/^Rule 1: Companion/).getByRole('textbox', { name: 'Label' }), 's');
  expect((await checked()).effect('companion-1')).toMatchObject({ label: 'Pets', companionId: '3f2504e0-4f89-41d3-9a0c-0305e82c3301' });
});

const pool: Raw = { type: 'resource', id: 'resource-1', resourceId: 'pool-a', label: 'Pool A', maximum: '2' };
const linked: Raw = { type: 'companion', id: 'companion-1', companionId: '3f2504e0-4f89-41d3-9a0c-0305e82c3301', label: 'Pet', armorClass: '13', hitPointMaximum: '5 * LEVEL', attacks: ['attack-1'], resourceIds: ['pool-a'] };

it('unticking the last attack leaves attacks out of the payload', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [bite, linked]);
  await user.click(rule(/^Rule 2: Companion/).getByRole('checkbox', { name: 'Bite' }));
  const sent = (await checked()).effect('companion-1') as Raw;
  expect('attacks' in sent).toBe(false);
  expect(sent.companionId).toBe('3f2504e0-4f89-41d3-9a0c-0305e82c3301');
});

it('removing an attack clears it from the companions', async () => {
  const user = userEvent.setup();
  const { checked } = await openDraft(user, [bite, linked]);
  await user.click(screen.getByRole('button', { name: 'Remove rule 1' }));
  const sent = (await checked()).effect('companion-1') as Raw;
  expect('attacks' in sent).toBe(false);
  expect(sent.resourceIds).toEqual(['pool-a']);
});

it('removing a pool clears it from the companions', async () => {
  const user = userEvent.setup();
  const { checked } = await openDraft(user, [pool, linked]);
  await user.click(screen.getByRole('button', { name: 'Remove rule 1' }));
  const sent = (await checked()).effect('companion-1') as Raw;
  expect('resourceIds' in sent).toBe(false);
  expect(sent.attacks).toEqual(['attack-1']);
});

it.each(['item', 'spell'])('has no companion button on a %s', async (kind) => {
  const user = userEvent.setup();
  await openDraft(user, [], kind);
  expect(screen.queryByRole('button', { name: 'Add companion' })).toBeNull();
});
