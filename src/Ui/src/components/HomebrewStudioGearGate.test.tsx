// @vitest-environment jsdom
// Content v12 (G4): the studio boxes for a modifier that applies only while no armor, heavy armor or shield is worn.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: { listSources: vi.fn(), contentBySource: vi.fn(), publishManyPreview: vi.fn(), createHomebrewSource: vi.fn(), previewFormula: vi.fn(), listCharacters: vi.fn(), validateRevision: vi.fn(), listContent: vi.fn(), contentRevisions: vi.fn() },
}));

const info = {
  rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }],
  fields: [
    { id: 'initiative', label: 'Initiative' },
    { id: 'ac', label: 'Armor Class' },
  ],
} as unknown as AppInfo;
const source = { id: 'a', title: 'Testy Alpha', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.contentRevisions).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-c', revisionId: 'x' }, errors: [], warnings: [], requiredSchemaVersion: 12, canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[], kind = 'feature') {
  const entry = {
    contentId: 'id-c',
    name: 'Testy Gearless',
    kind,
    revisions: [],
    latest: { contentId: 'id-c', revisionId: 'r-c', name: 'Testy Gearless', kind, status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Gearless' }));
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

const RULE = /^Rule 1: Modifier/;
const LEGEND = 'Only while not wearing';
const mod: Raw = { type: 'modifier', id: 'modifier-1', operation: 'bonus', target: 'ac', value: '2' };

it('shows the gear boxes for a top-level modifier only', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [mod]);
  const r = rule(RULE);
  const set = r.getByRole('group', { name: LEGEND });
  expect(within(set).getByRole('checkbox', { name: 'Armor' })).toBeTruthy();
  expect(within(set).getByRole('checkbox', { name: 'Heavy armor' })).toBeTruthy();
  expect(within(set).getByRole('checkbox', { name: 'Shield' })).toBeTruthy();
  expect(set.getAttribute('aria-describedby')).toBeTruthy();
  expect(r.getByText('Leave all unticked for a rule that applies whatever is worn.')).toBeTruthy();
  cleanup();
  const user2 = userEvent.setup();
  const itemMod: Raw = { type: 'itemMod', id: 'itemMod-1', appliesTo: 'weapon', modifiers: [{ ...mod, id: 'itemMod-1-m1' }] };
  await openDraft(user2, [itemMod], 'item');
  const nested = within(screen.getByRole('group', { name: /^Modifier 1 of this mod/ }));
  expect(nested.getByRole('combobox', { name: 'Changes' })).toBeTruthy();
  expect(nested.queryByRole('group', { name: LEGEND })).toBeNull();
});

it('ticking armor and shield writes them in order', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [mod]);
  const set = within(rule(RULE).getByRole('group', { name: LEGEND }));
  await user.click(set.getByRole('checkbox', { name: 'Shield' }));
  await user.click(set.getByRole('checkbox', { name: 'Armor' }));
  expect((await checked()).effect('modifier-1')).toMatchObject({ notWearing: ['armor', 'shield'] });
});

it('ticking armor clears and disables heavy armor', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...mod, notWearing: ['heavyArmor', 'shield'] }]);
  const set = within(rule(RULE).getByRole('group', { name: LEGEND }));
  expect(set.getByRole('checkbox', { name: 'Heavy armor' })).toHaveProperty('checked', true);
  await user.click(set.getByRole('checkbox', { name: 'Armor' }));
  const heavy = set.getByRole('checkbox', { name: 'Heavy armor' }) as HTMLInputElement;
  expect(heavy.checked).toBe(false);
  expect(heavy.disabled).toBe(true);
  expect((await checked()).effect('modifier-1')).toMatchObject({ notWearing: ['armor', 'shield'] });
});

it('unticking every box removes the key', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...mod, notWearing: ['shield'] }]);
  await user.click(within(rule(RULE).getByRole('group', { name: LEGEND })).getByRole('checkbox', { name: 'Shield' }));
  const sent = (await checked()).effect('modifier-1');
  expect('notWearing' in sent).toBe(false);
  expect(sent).toMatchObject({ operation: 'bonus', value: '2' });
});

it('hides the gear boxes for a pool maximum and drops the key', async () => {
  const user = userEvent.setup();
  const resource: Raw = { type: 'resource', id: 'resource-1', resourceId: 'pool', label: 'Testy Pool', maximum: '2' };
  const { checked } = await openDraft(user, [resource,{ ...mod, notWearing: ['armor'] }]);
  const r = within(screen.getByRole('group', { name: /^Rule 2: Modifier/ }));
  expect(r.getByRole('group', { name: LEGEND })).toBeTruthy();
  await user.selectOptions(r.getByRole('combobox', { name: 'Changes' }), 'resource.pool');
  expect(r.queryByRole('group', { name: LEGEND })).toBeNull();
  expect('notWearing' in (await checked()).effect('modifier-1')).toBe(false);
});

it('a stored unknown value is dropped on the first box click', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...mod, notWearing: ['cloak'] }]);
  await user.click(within(rule(RULE).getByRole('group', { name: LEGEND })).getByRole('checkbox', { name: 'Shield' }));
  expect((await checked()).effect('modifier-1')).toMatchObject({ notWearing: ['shield'] });
});

it('stored armor and heavy armor normalise to armor on the first click', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...mod, notWearing: ['armor', 'heavyArmor'] }]);
  await user.click(within(rule(RULE).getByRole('group', { name: LEGEND })).getByRole('checkbox', { name: 'Shield' }));
  expect((await checked()).effect('modifier-1')).toMatchObject({ notWearing: ['armor', 'shield'] });
});

it('keeps unknown members across an edit', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...mod, notWearing: ['armor'], futureMember: 'kept' }]);
  await user.click(within(rule(RULE).getByRole('group', { name: LEGEND })).getByRole('checkbox', { name: 'Shield' }));
  expect((await checked()).effect('modifier-1')).toMatchObject({ notWearing: ['armor', 'shield'], futureMember: 'kept' });
});
