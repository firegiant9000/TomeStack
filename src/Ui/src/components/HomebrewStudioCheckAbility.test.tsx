// @vitest-environment jsdom
// Content v12 (G10): the check ability editor in the studio.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: { listSources: vi.fn(), contentBySource: vi.fn(), publishManyPreview: vi.fn(), createHomebrewSource: vi.fn(), previewFormula: vi.fn(), listCharacters: vi.fn(), validateRevision: vi.fn(), listContent: vi.fn(), contentRevisions: vi.fn() },
}));

const info = { rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }], fields: [{ id: 'initiative', label: 'Initiative' }] } as unknown as AppInfo;
const source = { id: 'a', title: 'Testy Alpha', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.contentRevisions).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-f', revisionId: 'x' }, errors: [], warnings: [], requiredSchemaVersion: 12, canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[]) {
  const entry = {
    contentId: 'id-f',
    name: 'Testy Swap',
    kind: 'feature',
    revisions: [],
    latest: { contentId: 'id-f', revisionId: 'r-f', name: 'Testy Swap', kind: 'feature', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Swap' }));
  const rule = (name: RegExp) => within(screen.getByRole('group', { name }));
  const checked = async () => {
    vi.mocked(client.validateRevision).mockClear();
    await user.click(screen.getByRole('button', { name: 'Check' }));
    await waitFor(() => expect(vi.mocked(client.validateRevision).mock.calls.length).toBeGreaterThan(0));
    const sent = vi.mocked(client.validateRevision).mock.calls.at(-1)![0] as unknown as { effects: Raw[] };
    return { effect: (id: string) => sent.effects.find((e) => e.id === id) as Raw };
  };
  return { rule, checked };
}

const RULE = /^Rule 1: Use another ability for a skill/;

it('adds a check ability with defaults', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, []);
  await user.click(screen.getByRole('button', { name: 'Add check ability' }));
  const r = rule(RULE);
  expect(r.getByRole('combobox', { name: 'Skill' })).toHaveProperty('value', 'skill.athletics');
  expect(r.getByRole('combobox', { name: 'Uses instead' })).toHaveProperty('value', 'dex');
  expect((await checked()).effect('checkAbility-1')).toMatchObject({ type: 'checkAbility', target: 'skill.athletics', ability: 'dex' });
});

it('edits target and ability', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ type: 'checkAbility', id: 'checkAbility-1', target: 'skill.athletics', ability: 'dex' }]);
  const r = rule(RULE);
  await user.selectOptions(r.getByRole('combobox', { name: 'Skill' }), 'ability.str.check');
  await user.selectOptions(r.getByRole('combobox', { name: 'Uses instead' }), 'con');
  expect((await checked()).effect('checkAbility-1')).toMatchObject({ target: 'ability.str.check', ability: 'con' });
  expect(r.getByRole('option', { name: 'Every Strength skill' })).toBeTruthy();
  expect(r.getByRole('option', { name: 'Sleight of Hand' })).toBeTruthy();
});

it("disables the skill's own ability", async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ type: 'checkAbility', id: 'checkAbility-1', target: 'skill.athletics', ability: 'dex' }]);
  const r = rule(RULE);
  expect(r.getByRole('option', { name: 'Strength' })).toHaveProperty('disabled', true);
  expect(r.getByRole('option', { name: 'Dexterity' })).toHaveProperty('disabled', false);
  await user.selectOptions(r.getByRole('combobox', { name: 'Skill' }), 'ability.dex.check');
  expect(r.getByRole('option', { name: 'Dexterity' })).toHaveProperty('disabled', true);
  expect(r.getByRole('option', { name: 'Strength' })).toHaveProperty('disabled', false);
});

it('hides automation and timing', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ type: 'checkAbility', id: 'checkAbility-1', target: 'skill.athletics', ability: 'dex' }]);
  const r = rule(RULE);
  expect(r.queryByRole('combobox', { name: 'Automation' })).toBeNull();
});

it('offers no Constitution skill target and shows a stored one as unknown', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ type: 'checkAbility', id: 'checkAbility-1', target: 'ability.con.check', ability: 'dex' }]);
  const r = rule(RULE);
  expect(r.queryByRole('option', { name: 'Every Constitution skill' })).toBeNull();
  expect(r.getByRole('option', { name: 'ability.con.check (unknown)' })).toBeTruthy();
  expect(r.getByRole('option', { name: 'Every Strength skill' })).toBeTruthy();
});

it('keeps unknown members across an edit', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ type: 'checkAbility', id: 'checkAbility-1', target: 'skill.athletics', ability: 'dex', futureMember: 'kept' }]);
  const r = rule(RULE);
  await user.selectOptions(r.getByRole('combobox', { name: 'Uses instead' }), 'con');
  expect((await checked()).effect('checkAbility-1')).toMatchObject({ ability: 'con', futureMember: 'kept' });
});

it('hints at SPEED only for a climb, swim or fly speed modifier', async () => {
  const user = userEvent.setup();
  await openDraft(user, [
    { type: 'modifier', id: 'modifier-1', target: 'speed.climb', operation: 'replace', value: '10' },
    { type: 'modifier', id: 'modifier-2', target: 'initiative', operation: 'bonus', value: '1' },
  ]);
  const hint = /SPEED \(your walking speed\)/;
  expect(within(screen.getByRole('group', { name: /^Rule 1:/ })).queryByText(hint)).not.toBeNull();
  expect(within(screen.getByRole('group', { name: /^Rule 2:/ })).queryByText(hint)).toBeNull();
});
