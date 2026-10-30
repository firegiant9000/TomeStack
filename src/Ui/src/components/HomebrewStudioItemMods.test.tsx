// @vitest-environment jsdom
// MECH-X item 3 (T5): the studio adds and edits an item mod.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: { listSources: vi.fn(), contentBySource: vi.fn(), publishManyPreview: vi.fn(), createHomebrewSource: vi.fn(), previewFormula: vi.fn(), listCharacters: vi.fn(), validateRevision: vi.fn() },
}));

const info = { rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }], fields: [{ id: 'initiative', label: 'Initiative' }] } as unknown as AppInfo;
const source = { id: 'a', title: 'Testy Alpha', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-m', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[], kind = 'item') {
  const entry = {
    contentId: 'id-m',
    name: 'Testy Mod',
    kind,
    revisions: [],
    latest: { contentId: 'id-m', revisionId: 'r-m', name: 'Testy Mod', kind, status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Mod' }));
  const rule = (name: RegExp) => within(screen.getByRole('group', { name }));
  const checked = async () => {
    vi.mocked(client.validateRevision).mockClear();
    await user.click(screen.getByRole('button', { name: 'Check' }));
    await waitFor(() => expect(vi.mocked(client.validateRevision).mock.calls.length).toBeGreaterThan(0));
    const sent = vi.mocked(client.validateRevision).mock.calls.at(-1)![0] as unknown as { effects: Raw[] };
    return { effect: (id: string) => sent.effects.find((e) => e.id === id), count: sent.effects.length };
  };
  return { rule, checked };
}

const blocked = (name: string) => screen.getByRole('button', { name }).getAttribute('aria-disabled') === 'true';
const weapon: Raw = { type: 'weapon', id: 'weapon-1', category: 'simple', attack: 'melee', damage: '1d6', damageType: 'slashing', properties: [], weaponKey: 'blade' };
const mod: Raw = { type: 'itemMod', id: 'itemMod-1', appliesTo: 'weapon', bonus: 1 };

it('adds an item mod to an item and sends it', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, []);
  await user.click(screen.getByRole('button', { name: 'Add mod' }));
  const m = rule(/^Rule 1: Item mod/);
  expect((m.getByRole('combobox', { name: 'Applies to' }) as HTMLSelectElement).value).toBe('weapon');
  await user.selectOptions(m.getByRole('combobox', { name: 'Applies to' }), 'armor');
  await user.clear(m.getByRole('textbox', { name: /^Bonus/ }));
  await user.type(m.getByRole('textbox', { name: /^Bonus/ }), '2');
  await user.click(m.getByRole('button', { name: 'Add a modifier to this mod' }));
  expect(blocked('Save draft')).toBe(false);
  expect((await checked()).effect('itemMod-1')).toMatchObject({
    type: 'itemMod', appliesTo: 'armor', bonus: 2, modifiers: [{ type: 'modifier', id: 'itemMod-1-m1', operation: 'bonus', target: 'initiative', value: '1' }],
  });
});

it('a bad bonus blocks Save with a linked error', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [mod]);
  const bonus = rule(/^Rule 1: Item mod/).getByRole('textbox', { name: /^Bonus/ });
  await user.clear(bonus);
  await user.type(bonus, '11');
  expect(bonus.getAttribute('aria-invalid')).toBe('true');
  expect(blocked('Save draft')).toBe(true);
  await user.clear(bonus);
  await user.type(bonus, '3');
  expect(blocked('Save draft')).toBe(false);
});

it('Add mod is disabled beside a weapon, and Add weapon and Add armor beside a mod, each saying why', async () => {
  const user = userEvent.setup();
  await openDraft(user, [weapon]);
  const addMod = screen.getByRole('button', { name: 'Add mod' }) as HTMLButtonElement;
  expect(addMod.disabled).toBe(true);
  expect(addMod.title).toMatch(/weapon/i);
  cleanup();
  await openDraft(user, [mod]);
  for (const name of ['Add weapon', 'Add armor']) {
    const b = screen.getByRole('button', { name }) as HTMLButtonElement;
    expect(b.disabled).toBe(true);
    expect(b.title).toMatch(/mod/i);
  }
  expect((screen.getByRole('button', { name: 'Add mod' }) as HTMLButtonElement).disabled).toBe(true);
});

it('has no mod button on content that is not an item', async () => {
  const user = userEvent.setup();
  await openDraft(user, [], 'feature');
  expect(screen.queryByRole('button', { name: 'Add mod' })).toBeNull();
});
