// @vitest-environment jsdom
// Content v12 (G5): the studio field for the highest an ability score can reach through one bonus.
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
    { id: 'ability.str.score', label: 'Strength score' },
    { id: 'ability.dex.score', label: 'Dexterity score' },
  ],
} as unknown as AppInfo;
const source = { id: 'a', title: 'Testy Alpha', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.contentRevisions).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-c', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[], kind = 'feature') {
  const entry = {
    contentId: 'id-c',
    name: 'Testy Ceiling',
    kind,
    revisions: [],
    latest: { contentId: 'id-c', revisionId: 'r-c', name: 'Testy Ceiling', kind, status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Ceiling' }));
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
const LABEL = 'Highest the score can reach';
const score: Raw = { type: 'modifier', id: 'modifier-1', operation: 'bonus', target: 'ability.str.score', value: '2' };

it('shows the cap field only for an ability-score bonus', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [score]);
  const r = rule(RULE);
  const field = r.getByRole('spinbutton', { name: LABEL });
  expect(field.getAttribute('aria-describedby')).toBeTruthy();
  expect(r.getByText('Leave empty for the usual 20; up to 30.')).toBeTruthy();
  await user.selectOptions(r.getByRole('combobox', { name: 'How' }), 'set');
  expect(r.queryByRole('spinbutton', { name: LABEL })).toBeNull();
  await user.selectOptions(r.getByRole('combobox', { name: 'How' }), 'bonus');
  expect(r.getByRole('spinbutton', { name: LABEL })).toBeTruthy();
  await user.selectOptions(r.getByRole('combobox', { name: 'Changes' }), 'initiative');
  expect(r.queryByRole('spinbutton', { name: LABEL })).toBeNull();
});

it('does not show the cap field in an item-mod modifier', async () => {
  const user = userEvent.setup();
  const mod: Raw = { type: 'itemMod', id: 'itemMod-1', appliesTo: 'weapon', modifiers: [{ ...score, id: 'itemMod-1-m1' }] };
  await openDraft(user, [mod], 'item');
  const nested = within(screen.getByRole('group', { name: /^Modifier 1 of this mod/ }));
  expect(nested.getByRole('combobox', { name: 'Changes' })).toHaveProperty('value', 'ability.str.score');
  expect(nested.queryByRole('spinbutton', { name: LABEL })).toBeNull();
});

it('sets and clears the cap', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [score]);
  const field = rule(RULE).getByRole('spinbutton', { name: LABEL });
  await user.type(field, '25');
  expect((await checked()).effect('modifier-1')).toMatchObject({ operation: 'bonus', target: 'ability.str.score', cap: 25 });
  await user.clear(field);
  const cleared = (await checked()).effect('modifier-1');
  expect('cap' in cleared).toBe(false);
  expect(cleared).toMatchObject({ operation: 'bonus', value: '2' });
});

it('removes the cap when the target changes away from a score', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...score, cap: 24 }]);
  expect(rule(RULE).getByRole('spinbutton', { name: LABEL })).toHaveProperty('value', '24');
  await user.selectOptions(rule(RULE).getByRole('combobox', { name: 'Changes' }), 'initiative');
  expect('cap' in (await checked()).effect('modifier-1')).toBe(false);
});

it('removes the cap when the operation changes away from a bonus', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...score, cap: 24 }]);
  await user.selectOptions(rule(RULE).getByRole('combobox', { name: 'How' }), 'set');
  expect('cap' in (await checked()).effect('modifier-1')).toBe(false);
});

it('keeps unknown members across an edit', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...score, cap: 24, futureMember: 'kept' }]);
  await user.selectOptions(rule(RULE).getByRole('combobox', { name: 'Changes' }), 'ability.dex.score');
  expect((await checked()).effect('modifier-1')).toMatchObject({ target: 'ability.dex.score', cap: 24, futureMember: 'kept' });
});
