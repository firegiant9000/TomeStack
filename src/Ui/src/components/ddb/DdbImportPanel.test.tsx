// @vitest-environment jsdom
// The D&D Beyond import steps against a mocked client (the real flow runs in e2e/flow.e2e.tsx). Every value is invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../../api/client';
import { TomeStackError } from '../../api/transport';
import type { DdbPreview, DdbReadResult, MatchRow, RulesFamilyPolicy } from '../../api/types';
import { DdbImportPanel } from './DdbImportPanel';

vi.mock('../../api/client', () => ({
  client: {
    ddbRead: vi.fn(),
    ddbReadData: vi.fn(),
    ddbPreview: vi.fn(),
    ddbApply: vi.fn(),
    ddbDiscard: vi.fn(),
    listCampaigns: vi.fn(),
    listContent: vi.fn(),
  },
}));

const families: RulesFamilyPolicy[] = [
  { id: 'srd-5.1', displayName: 'SRD 5.1 (2014 rules)', abilityIncreaseSource: 'species', backgroundGrantsFeat: false, longRestExhaustionNeedsFoodAndDrink: true },
  { id: 'srd-5.2.1', displayName: 'SRD 5.2.1 (2024 rules)', abilityIncreaseSource: 'background', backgroundGrantsFeat: true, longRestExhaustionNeedsFoodAndDrink: false },
];

const read: DdbReadResult = {
  chosen: true,
  token: 'fixture-token',
  layout: 'ddb-2014',
  suggestedFamily: 'srd-5.1',
  summary: { name: 'Testy McFixture', classText: 'Fixture Arcanist 3', features: 2, spells: 2, items: 1 },
};

const ref = (n: number) => ({ contentId: `00000000-0000-4000-8000-00000000000${n}`, revisionId: `00000000-0000-4000-8000-00000000001${n}` });
const candidate = (n: number, name: string, caster?: string) => ({
  reference: ref(n),
  name,
  sourceTitle: 'Fixture Notes',
  families: ['srd-5.1' as const],
  placement: caster ? { kind: 'spell' as const, caster } : { kind: 'pin' as const },
});

const rows: MatchRow[] = [
  { rowId: 'species', kind: 'species', label: 'Fixture Quickfoot', status: 'matched', candidates: [candidate(1, 'Fixture Quickfoot')], chosen: candidate(1, 'Fixture Quickfoot') },
  { rowId: 'feat:0', kind: 'feat', label: 'Fixture Lost Feat', status: 'notFound', candidates: [] },
  {
    rowId: 'spell:0',
    kind: 'spell',
    label: 'Fixture Veil',
    status: 'choose',
    candidates: [candidate(2, 'Fixture Veil', 'caster-a'), candidate(2, 'Fixture Veil', 'caster-b')],
  },
];

function preview(resolved: boolean): DdbPreview {
  return {
    character: {
      id: 'new-id', schemaVersion: 7, name: 'Testy McFixture', rulesFamily: 'srd-5.1', level: 3, classes: [], choices: [], crossFamilyExceptions: [],
      baseAbilities: { str: 10, dex: 12, con: 10, int: 15, wis: 10, cha: 10 }, pins: [], overrides: [], updatedAt: '2026-10-02T00:00:00Z',
    },
    matches: resolved
      ? rows.map((r) => (r.status === 'choose' ? { ...r, status: 'matched' as const, chosen: r.candidates[1] } : r))
      : rows,
    openChoices: [],
    comparison: [
      { field: 'initiative', label: 'Initiative', sheet: 1, calculated: 1, differs: false },
      { field: 'armorClass', label: 'Armor Class', sheet: 16, calculated: 13, differs: true },
    ],
    abilityPlan: { proposedBase: { str: 10, dex: 12, con: 10, int: 15, wis: 10, cha: 10 }, notes: [] },
    report: { matched: 1, chosen: resolved ? 1 : 0, notFound: 1, noPlace: 0, unreadable: 0, leftOut: 0, notBroughtOver: ['currency', 'speed', 'playState'], sameNameExists: false, familyMismatch: false },
    diagnostics: [],
    canApply: resolved,
  };
}

beforeEach(() => {
  vi.mocked(client.ddbRead).mockResolvedValue(read);
  vi.mocked(client.ddbReadData).mockResolvedValue(read);
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => preview((request.resolutions ?? []).some((r) => r.rowId === 'spell:0')));
  vi.mocked(client.ddbApply).mockResolvedValue({ characterId: 'new-id', overrides: 0, gapNotes: 1, gapNotesNotStored: 0, report: preview(true).report });
  vi.mocked(client.ddbDiscard).mockResolvedValue({ discarded: true });
  vi.mocked(client.listCampaigns).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function renderPanel() {
  const onCancel = vi.fn();
  const onCreated = vi.fn();
  render(<DdbImportPanel rulesFamilies={families} onError={vi.fn()} onCancel={onCancel} onCreated={onCreated} />);
  return { onCancel, onCreated };
}

async function toMatches(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.click(await screen.findByRole('button', { name: 'Next: rules' }));
  await user.click(await screen.findByRole('button', { name: 'Next: matches' }));
  await screen.findByRole('heading', { name: 'Matches' });
}

it('moves focus to the step heading on every step and Cancel discards the token', async () => {
  const user = userEvent.setup();
  const { onCancel } = renderPanel();

  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Choose the sheet' })));
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  expect(await screen.findByText(/Testy McFixture/)).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Next: rules' }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Rules and campaign' })));
  expect(within(screen.getByRole('region', { name: 'Rules and campaign' })).getByRole<HTMLInputElement>('radio', { name: /SRD 5\.1/ }).checked).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Matches' })));

  await user.click(screen.getByRole('button', { name: 'Cancel' }));

  expect(client.ddbDiscard).toHaveBeenCalledWith('fixture-token');
  expect(onCancel).toHaveBeenCalled();
});

it('shows Needs a choice rows first under the filter and requires a pick before Create', async () => {
  const user = userEvent.setup();
  const { onCreated } = renderPanel();
  await toMatches(user);

  await user.click(screen.getByRole('radio', { name: 'Needs a choice' }));
  const region = screen.getByRole('region', { name: 'Matches' });
  const shown = within(region).getAllByRole('rowheader').map((h) => h.textContent);
  expect(shown).toEqual(['Fixture Veil']);

  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await user.click(await screen.findByRole('button', { name: 'Next: summary' }));
  const create = await screen.findByRole<HTMLButtonElement>('button', { name: 'Create character' });
  expect(create.disabled).toBe(true);

  await user.click(screen.getByRole('button', { name: 'Back' }));
  await user.click(screen.getByRole('button', { name: 'Back' }));
  await screen.findByRole('heading', { name: 'Matches' });
  await user.selectOptions(screen.getByRole('combobox', { name: 'Match for Fixture Veil' }), '1');
  await waitFor(() => expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ resolutions: [expect.objectContaining({ rowId: 'spell:0', caster: 'caster-b' })] })));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await user.click(await screen.findByRole('button', { name: 'Next: summary' }));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Create character' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Create character' }));

  await waitFor(() => expect(onCreated).toHaveBeenCalledWith('new-id', expect.anything()));
  expect(client.ddbDiscard).not.toHaveBeenCalled();
});

it('drops open-choice answers when a match changes, since they were made on the earlier proposal', async () => {
  const choice = { source: ref(3), sourceName: 'Fixture Wanderer', choiceId: 'fixture-skill', count: 1, options: [ref(4), ref(5)], selected: [], resolved: false };
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => ({ ...preview((request.resolutions ?? []).some((r) => r.rowId === 'spell:0')), openChoices: [choice] }));
  const user = userEvent.setup();
  renderPanel();
  await toMatches(user);

  const [option] = await screen.findAllByRole('checkbox', { name: 'Option' });
  await user.click(option as HTMLElement);
  await waitFor(() => expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ answers: [expect.objectContaining({ choiceId: 'fixture-skill' })] })));

  await user.selectOptions(screen.getByRole('combobox', { name: 'Match for Fixture Veil' }), '1');

  await waitFor(() =>
    expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ resolutions: [expect.objectContaining({ rowId: 'spell:0' })], answers: [] })),
  );
});

it('falls back to a file input when the host has no dialog', async () => {
  vi.mocked(client.ddbRead).mockRejectedValue(new TomeStackError({ code: 'unsupported', message: 'Fixture: no dialog.' }));
  const user = userEvent.setup();
  renderPanel();

  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.upload(screen.getByLabelText('D&D Beyond PDF file'), new File([new Uint8Array([37, 80, 68, 70])], 'fixture.pdf', { type: 'application/pdf' }));

  await waitFor(() => expect(client.ddbReadData).toHaveBeenCalledWith('fixture.pdf', 'JVBERg=='));
  expect(await screen.findByRole('button', { name: 'Next: rules' })).toBeTruthy();
});

it('every table has a caption and row headers', async () => {
  const user = userEvent.setup();
  renderPanel();
  await toMatches(user);
  const check = () => {
    const tables = screen.getAllByRole('table');
    expect(tables.length).toBeGreaterThan(0);
    for (const table of tables) {
      expect(table.querySelector('caption')?.textContent?.trim()).toBeTruthy();
      expect(within(table).getAllByRole('rowheader').length).toBeGreaterThan(0);
    }
  };
  check();
  // Each row's controls are grouped and named by the row.
  expect(screen.getByRole('group', { name: 'Fixture Veil' })).toBeTruthy();

  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await screen.findByRole('heading', { name: 'Numbers' });
  check();
  expect(screen.getByRole('radiogroup', { name: 'Armor Class' })).toBeTruthy();
});
