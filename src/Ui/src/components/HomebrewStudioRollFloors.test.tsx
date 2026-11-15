// @vitest-environment jsdom
// Content v12 (G9): the roll floor editor in the studio.
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
    name: 'Testy Floor',
    kind: 'feature',
    revisions: [],
    latest: { contentId: 'id-f', revisionId: 'r-f', name: 'Testy Floor', kind: 'feature', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Floor' }));
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

it('adds a roll floor with defaults', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, []);
  await user.click(screen.getByRole('button', { name: 'Add roll floor' }));
  const r = rule(/^Rule 1: Roll floor/);
  expect(r.getByRole('combobox', { name: 'Applies to' })).toHaveProperty('value', 'check');
  expect(r.getByRole('radio', { name: 'The d20' })).toHaveProperty('checked', true);
  expect(r.getByRole('spinbutton', { name: 'At least' })).toHaveProperty('value', '10');
  expect(r.getByRole('checkbox', { name: 'Only when proficient' })).toHaveProperty('checked', false);
  expect((await checked()).effect('rollFloor-1')).toMatchObject({ type: 'rollFloor', target: 'check', face: 10 });
});

it('offers no Automation select for a roll floor (it is always automatic)', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ type: 'rollFloor', id: 'rollFloor-1', target: 'check', face: 8 }]);
  expect(rule(/^Rule 1: Roll floor/).queryByRole('combobox', { name: 'Automation' })).toBeNull();
});

it('switching to total clears face and back', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ type: 'rollFloor', id: 'rollFloor-1', target: 'check', face: 8 }]);
  const r = rule(/^Rule 1: Roll floor/);
  await user.click(r.getByRole('radio', { name: 'The total' }));
  await user.type(r.getByRole('textbox', { name: 'At least' }), '12');
  const total = (await checked()).effect('rollFloor-1');
  expect(total.total).toBe('12');
  expect(total).not.toHaveProperty('face');
  await user.click(r.getByRole('radio', { name: 'The d20' }));
  const face = (await checked()).effect('rollFloor-1');
  expect(face).not.toHaveProperty('total');
  expect(typeof face.face).toBe('number');
});

it('edits target and proficient', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ type: 'rollFloor', id: 'rollFloor-1', target: 'check', face: 10 }]);
  const r = rule(/^Rule 1: Roll floor/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'skill.stealth');
  await user.click(r.getByRole('checkbox', { name: 'Only when proficient' }));
  expect((await checked()).effect('rollFloor-1')).toMatchObject({ target: 'skill.stealth', face: 10, proficient: true });
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'ability.wis.check');
  await user.click(r.getByRole('checkbox', { name: 'Only when proficient' }));
  const e = (await checked()).effect('rollFloor-1');
  expect(e).toMatchObject({ target: 'ability.wis.check' });
  expect(e.proficient).not.toBe(true);
});

it('keeps unknown members across an edit', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ type: 'rollFloor', id: 'rollFloor-1', target: 'check', face: 10, futureMember: 'kept' }]);
  const r = rule(/^Rule 1: Roll floor/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'ability.str.check');
  await user.click(r.getByRole('checkbox', { name: 'Only when proficient' }));
  expect((await checked()).effect('rollFloor-1')).toMatchObject({ target: 'ability.str.check', proficient: true, futureMember: 'kept' });
});
