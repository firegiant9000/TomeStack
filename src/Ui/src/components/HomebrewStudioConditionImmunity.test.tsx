// @vitest-environment jsdom
// Content v12 (G11): the condition immunity editor in the studio.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';
import { conditionLabels } from './conditionLabels';
import { conditionKeys } from './RollModeFields';

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
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-i', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
});
afterEach(cleanup);

it('keeps the studio condition keys and the label map in step, in the same order', () => {
  expect(Object.keys(conditionLabels)).toEqual([...conditionKeys]);
});

type Raw = Record<string, unknown>;

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[]) {
  const entry = {
    contentId: 'id-i',
    name: 'Testy Ward',
    kind: 'feature',
    revisions: [],
    latest: { contentId: 'id-i', revisionId: 'r-i', name: 'Testy Ward', kind: 'feature', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Ward' }));
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

const heavy: Raw = { type: 'toggle', id: 'toggle-1', toggleId: 'heavy', label: 'Testy Heavy' };
const RULE = /^Rule 1: Condition immunity/;
const immunity: Raw = { type: 'grant', id: 'grant-1', grant: 'conditionImmunity', condition: 'charmed' };

it('adds a condition immunity with defaults', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, []);
  await user.click(screen.getByRole('button', { name: 'Add condition immunity' }));
  const r = rule(RULE);
  expect(r.getByRole('combobox', { name: 'Condition' })).toHaveProperty('value', 'charmed');
  expect((await checked()).effect('grant-1')).toMatchObject({ type: 'grant', grant: 'conditionImmunity', condition: 'charmed' });
});

it('edits the condition among the 14 labels', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [immunity]);
  const r = rule(RULE);
  const select = r.getByRole('combobox', { name: 'Condition' });
  expect(within(select).getAllByRole('option')).toHaveLength(14);
  expect(r.getByRole('option', { name: 'Frightened' })).toBeTruthy();
  await user.selectOptions(select, 'poisoned');
  expect((await checked()).effect('grant-1')).toMatchObject({ grant: 'conditionImmunity', condition: 'poisoned' });
});

it('sets and clears only while a toggle', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [heavy, { ...immunity, id: 'grant-2' }]);
  const r = rule(/^Rule 2: Condition immunity/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Only while' }), 'heavy');
  expect((await checked()).effect('grant-2')).toMatchObject({ condition: 'charmed', whileToggle: { toggleId: 'heavy' } });
  await user.selectOptions(r.getByRole('combobox', { name: 'Only while' }), '');
  const cleared = (await checked()).effect('grant-2');
  expect(cleared.whileToggle).toBeUndefined();
  expect(cleared).toMatchObject({ grant: 'conditionImmunity', condition: 'charmed' });
});

it('damage defence editor does not offer condition immunity', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ type: 'grant', id: 'grant-1', grant: 'damageResistance', damageType: 'fire' }]);
  const r = rule(/^Rule 1: Damage defence/);
  const kinds = within(r.getByRole('combobox', { name: 'Kind' })).getAllByRole('option').map((o) => o.textContent);
  expect(kinds).toEqual(['Resistance', 'Immunity', 'Vulnerability']);
  expect(r.queryByRole('combobox', { name: 'Condition' })).toBeNull();
});

it('keeps unknown members across an edit', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...immunity, futureMember: 'kept' }]);
  await user.selectOptions(rule(RULE).getByRole('combobox', { name: 'Condition' }), 'stunned');
  expect((await checked()).effect('grant-1')).toMatchObject({ condition: 'stunned', futureMember: 'kept' });
});
