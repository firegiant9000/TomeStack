// @vitest-environment jsdom
// D43 slice 6 (Task 6.5b): the roll mode editor and the damage defence editor in the studio.
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
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-m', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[]) {
  const entry = {
    contentId: 'id-m',
    name: 'Testy Mode',
    kind: 'feature',
    revisions: [],
    latest: { contentId: 'id-m', revisionId: 'r-m', name: 'Testy Mode', kind: 'feature', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Mode' }));
  const rule = (name: RegExp) => within(screen.getByRole('group', { name }));
  const checked = async () => {
    vi.mocked(client.validateRevision).mockClear();
    await user.click(screen.getByRole('button', { name: 'Check' }));
    await waitFor(() => expect(vi.mocked(client.validateRevision).mock.calls.length).toBeGreaterThan(0));
    const sent = vi.mocked(client.validateRevision).mock.calls.at(-1)![0] as unknown as { effects: Raw[] };
    return { effect: (id: string) => sent.effects.find((e) => e.id === id) as Raw, count: sent.effects.length };
  };
  return { rule, checked };
}

const blocked = (name: string) => screen.getByRole('button', { name }).getAttribute('aria-disabled') === 'true';

const heavy: Raw = { type: 'toggle', id: 'toggle-1', toggleId: 'heavy', label: 'Testy Heavy' };

it('the roll mode editor writes target, mode, against and toggle', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [heavy]);
  await user.click(screen.getByRole('button', { name: 'Add roll mode' }));
  const r = rule(/^Rule 2: Roll mode/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'save.dex');
  await user.selectOptions(r.getByRole('combobox', { name: 'Mode' }), 'disadvantage');
  await user.click(r.getByRole('checkbox', { name: 'Paralyzed' }));
  await user.click(r.getByRole('checkbox', { name: 'Stunned' }));
  await user.selectOptions(r.getByRole('combobox', { name: 'Only while' }), 'heavy');
  expect((await checked()).effect('rollMode-2')).toMatchObject({ type: 'rollMode', target: 'save.dex', mode: 'disadvantage', against: ['paralyzed', 'stunned'], toggle: 'heavy' });

  // The three groups: saves, skills (labelled), ability checks.
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'skill.sleightOfHand');
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'ability.str.check');
  expect((await checked()).effect('rollMode-2')).toMatchObject({ target: 'ability.str.check' });
  expect(r.getByRole('option', { name: 'Sleight of Hand' })).toBeTruthy();
  // No Normal mode is offered.
  expect(r.queryByRole('option', { name: /normal/i })).toBeNull();
  expect(blocked('Publish')).toBe(false);
});

it('clearing every condition removes against', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'save.wis', mode: 'advantage', against: ['charmed'] };
  const { rule, checked } = await openDraft(user, [mode]);
  const r = rule(/^Rule 1: Roll mode/);
  expect(r.getByRole('checkbox', { name: 'Charmed' })).toHaveProperty('checked', true);
  await user.click(r.getByRole('checkbox', { name: 'Charmed' }));
  const sent = (await checked()).effect('rollMode-1');
  expect(sent.against).toBeUndefined();
  expect(sent).toMatchObject({ target: 'save.wis', mode: 'advantage' });
});

it('the roll mode editor blocks Publish on an unknown condition key', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'save.wis', mode: 'advantage', against: ['charmed', 'testyCondition'] };
  const { rule } = await openDraft(user, [mode]);
  const r = rule(/^Rule 1: Roll mode/);
  expect(blocked('Publish')).toBe(true);
  expect(blocked('Save draft')).toBe(true);
  expect(r.getByText(/testyCondition/)).toBeTruthy();
  // Unchecking the unknown key fixes it.
  await user.click(r.getByRole('checkbox', { name: 'testyCondition' }));
  expect(blocked('Publish')).toBe(false);
});

it('the roll mode editor blocks Publish on an unknown target', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'save.testy', mode: 'advantage' };
  const { rule } = await openDraft(user, [mode]);
  expect(blocked('Publish')).toBe(true);
  await user.selectOptions(rule(/^Rule 1: Roll mode/).getByRole('combobox', { name: 'Applies to' }), 'save.con');
  expect(blocked('Publish')).toBe(false);
});

it('a damage defence grant can apply only while a form is on', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [heavy]);
  await user.click(screen.getByRole('button', { name: 'Add damage defence' }));
  const r = rule(/^Rule 2: Damage defence/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Kind' }), 'damageImmunity');
  await user.selectOptions(r.getByRole('combobox', { name: 'Damage type' }), 'fire');
  await user.selectOptions(r.getByRole('combobox', { name: 'Only while' }), 'heavy');
  expect((await checked()).effect('grant-2')).toMatchObject({ type: 'grant', grant: 'damageImmunity', damageType: 'fire', whileToggle: { toggleId: 'heavy' } });
  await user.selectOptions(r.getByRole('combobox', { name: 'Only while' }), '');
  const cleared = (await checked()).effect('grant-2');
  expect(cleared.whileToggle).toBeUndefined();
  expect(cleared).toMatchObject({ grant: 'damageImmunity', damageType: 'fire' });
});
