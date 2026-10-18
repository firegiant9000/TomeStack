// @vitest-environment jsdom
// The D&D Beyond import steps against a mocked client (the real flow runs in e2e/flow.e2e.tsx). Every value is invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../../api/client';
import { TomeStackError } from '../../api/transport';
import type { DdbPreview, DdbReadResult, MatchRow, RulesFamilyPolicy } from '../../api/types';
import { DdbImportPanel } from './DdbImportPanel';
import { DdbSummaryStep } from './DdbSummaryStep';

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

it('says on the first step that TomeStack is not affiliated with D&D Beyond (release checklist item 1, 0.5.0)', async () => {
  renderPanel();
  expect(await screen.findByText('TomeStack is not affiliated with D&D Beyond or Wizards of the Coast.')).toBeTruthy();
});

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

it('keeps open-choice answers while their choice is still offered and drops one whose choice went away', async () => {
  const choice = { source: ref(3), sourceName: 'Fixture Wanderer', choiceId: 'fixture-skill', count: 1, options: [ref(4), ref(5)], selected: [], resolved: false };
  // Leaving the species out takes away the choice it offered; picking a spell's caster does not touch it.
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => ({
    ...preview((request.resolutions ?? []).some((r) => r.rowId === 'spell:0')),
    openChoices: (request.resolutions ?? []).some((r) => r.rowId === 'species' && r.leaveOut) ? [] : [choice],
  }));
  const user = userEvent.setup();
  renderPanel();
  await toMatches(user);

  const [option] = await screen.findAllByRole('checkbox', { name: 'Option' });
  await user.click(option as HTMLElement);
  await waitFor(() => expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ answers: [expect.objectContaining({ choiceId: 'fixture-skill' })] })));

  await user.selectOptions(screen.getByRole('combobox', { name: 'Match for Fixture Veil' }), '1');
  await waitFor(() =>
    expect(client.ddbPreview).toHaveBeenLastCalledWith(
      expect.objectContaining({ resolutions: [expect.objectContaining({ rowId: 'spell:0' })], answers: [expect.objectContaining({ choiceId: 'fixture-skill' })] }),
    ),
  );

  await user.click(screen.getByRole('checkbox', { name: /Leave out\s+Fixture Quickfoot/ }));
  await waitFor(() => expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ answers: [] })));
});

it('drops an answer that holds the option of a row the user then leaves out', async () => {
  const choice = { source: ref(3), sourceName: 'Fixture Wanderer', choiceId: 'fixture-skill', count: 1, options: [ref(4), ref(5)], selected: [], resolved: false };
  const skillRow: MatchRow = { rowId: 'skill:arcana', kind: 'skill', label: 'Arcana', status: 'matched', candidates: [candidate(4, 'Fixture Arcana Lesson')], chosen: candidate(4, 'Fixture Arcana Lesson') };
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => {
    const base = preview((request.resolutions ?? []).some((r) => r.rowId === 'spell:0'));
    return { ...base, matches: [...base.matches, skillRow], openChoices: [choice] };
  });
  const user = userEvent.setup();
  renderPanel();
  await toMatches(user);

  const [option] = await screen.findAllByRole('checkbox', { name: 'Option' });
  await user.click(option as HTMLElement); // the first option is ref(4), the Arcana row's
  await waitFor(() => expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ answers: [expect.objectContaining({ choiceId: 'fixture-skill' })] })));

  await user.click(screen.getByRole('checkbox', { name: /Leave out\s+Arcana/ }));

  await waitFor(() =>
    expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ resolutions: [expect.objectContaining({ rowId: 'skill:arcana', leaveOut: true })], answers: [] })),
  );
});

it('falls back to a file input when the host has no dialog', async () => {
  vi.mocked(client.ddbRead).mockRejectedValue(new TomeStackError({ code: 'unsupported', message: 'Fixture: no dialog.' }));
  const user = userEvent.setup();
  renderPanel();

  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.upload(screen.getByLabelText('Character sheet PDF file'), new File([new Uint8Array([37, 80, 68, 70])], 'fixture.pdf', { type: 'application/pdf' }));

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

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

const fixtureCampaign = { id: 'fixture-campaign', name: 'Fixture Campaign', rulesFamily: 'srd-5.1' as const, allowedSources: [] };

it('clears the campaign when the rules family changes, so later previews do not send it', async () => {
  vi.mocked(client.listCampaigns).mockResolvedValue([fixtureCampaign]);
  const user = userEvent.setup();
  renderPanel();
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.click(await screen.findByRole('button', { name: 'Next: rules' }));
  await user.selectOptions(await screen.findByRole('combobox', { name: /Campaign/ }), 'fixture-campaign');
  await user.click(screen.getByRole('radio', { name: /SRD 5\.2\.1/ }));
  expect(screen.getByRole<HTMLSelectElement>('combobox', { name: /Campaign/ }).value).toBe('');
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));

  await screen.findByRole('heading', { name: 'Matches' });
  await waitFor(() => expect(client.ddbPreview).toHaveBeenCalled());
  const last = vi.mocked(client.ddbPreview).mock.calls.at(-1)![0];
  expect(last.rulesFamily).toBe('srd-5.2.1');
  expect(last.campaignId).toBeUndefined();
});

it('drops the shown preview when the family changes and disables Next while the new one loads', async () => {
  const user = userEvent.setup();
  renderPanel();
  await toMatches(user);
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));

  const pending = deferred<DdbPreview>();
  vi.mocked(client.ddbPreview).mockReturnValueOnce(pending.promise);
  await user.click(screen.getByRole('button', { name: 'Back' }));
  await user.click(screen.getByRole('radio', { name: /SRD 5\.2\.1/ }));
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));
  await screen.findByRole('heading', { name: 'Matches' });

  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(true);
  pending.resolve(preview(false));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
});

it('shows a failed preview with Try again, which asks again', async () => {
  vi.mocked(client.ddbPreview).mockRejectedValueOnce(new TomeStackError({ code: 'fixture.failed', message: 'Fixture preview failed.' }));
  const user = userEvent.setup();
  renderPanel();
  await toMatches(user);

  const alert = await screen.findByRole('alert');
  expect(alert.textContent).toContain('Fixture preview failed.');
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(true);
  expect(screen.queryByText('Working…')).toBeNull();

  await user.click(within(alert).getByRole('button', { name: 'Try again' }));
  await waitFor(() => expect(client.ddbPreview).toHaveBeenCalledTimes(2));
  await waitFor(() => expect(screen.queryByRole('alert')).toBeNull());
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
});

it('sends the user back to choose the sheet, with the error, when the token is no longer valid', async () => {
  vi.mocked(client.ddbPreview).mockRejectedValueOnce(new TomeStackError({ code: 'ddb.token-invalid', message: 'Fixture: this sheet is no longer open.' }));
  const user = userEvent.setup();
  renderPanel();
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.click(await screen.findByRole('button', { name: 'Next: rules' }));
  await user.click(await screen.findByRole('button', { name: 'Next: matches' }));

  await waitFor(() => expect(screen.getByRole('heading', { name: 'Choose the sheet' })).toBeTruthy());
  expect((await screen.findByRole('alert')).textContent).toContain('Fixture: this sheet is no longer open.');
  expect(screen.getByRole('button', { name: 'Choose PDF…' })).toBeTruthy();
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: rules' }).disabled).toBe(true);
});

it('awaits onCreated and keeps Create disabled for good after a successful apply', async () => {
  const user = userEvent.setup();
  const done = deferred<void>();
  const onCreated = vi.fn(() => done.promise);
  render(<DdbImportPanel rulesFamilies={families} onError={vi.fn()} onCancel={vi.fn()} onCreated={onCreated} />);
  await toMatches(user);
  await user.selectOptions(screen.getByRole('combobox', { name: 'Match for Fixture Veil' }), '1');
  await waitFor(() => expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ resolutions: [expect.anything()] })));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await user.click(await screen.findByRole('button', { name: 'Next: summary' }));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Create character' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Create character' }));
  await waitFor(() => expect(onCreated).toHaveBeenCalled());

  // Still inside onCreated: Create stays disabled.
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Create character' }).disabled).toBe(true);
  done.resolve();
  await waitFor(() => expect(screen.queryByText('Working…')).toBeNull());

  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Create character' }).disabled).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Create character' }));
  expect(client.ddbApply).toHaveBeenCalledTimes(1);
});

it('sends equipMatched true by default and false once the checkbox on the Numbers step is cleared (D16g)', async () => {
  const user = userEvent.setup();
  render(<DdbImportPanel rulesFamilies={families} onError={vi.fn()} onCancel={vi.fn()} onCreated={vi.fn()} />);
  await toMatches(user);
  await user.selectOptions(screen.getByRole('combobox', { name: 'Match for Fixture Veil' }), '1');
  await waitFor(() => expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ resolutions: [expect.anything()] })));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  const box = await screen.findByRole<HTMLInputElement>('checkbox', { name: 'Equip matched weapons and armour' });
  expect(box.checked).toBe(true);
  expect(vi.mocked(client.ddbPreview).mock.lastCall?.[0].equipMatched).toBe(true);
  await user.click(box);
  await waitFor(() => expect(vi.mocked(client.ddbPreview).mock.lastCall?.[0].equipMatched).toBe(false));
  // The summary step no longer offers it.
  await user.click(await screen.findByRole('button', { name: 'Next: summary' }));
  await screen.findByRole('button', { name: 'Create character' });
  expect(screen.queryByRole('checkbox', { name: 'Equip matched weapons and armour' })).toBeNull();
});

it('pre-fills every differing number from the step-2 preset and still lets the user change a row (D16h)', async () => {
  const user = userEvent.setup();
  renderPanel();
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.click(await screen.findByRole('button', { name: 'Next: rules' }));
  const presets = screen.getByRole('radiogroup', { name: "Numbers that differ from TomeStack's calculation" });
  expect(within(presets).getByRole<HTMLInputElement>('radio', { name: 'Decide each number myself' }).checked).toBe(true); // owner answer 6
  await user.click(within(presets).getByRole('radio', { name: "Use the character sheet's values" }));
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));
  await screen.findByRole('heading', { name: 'Matches' });
  await user.selectOptions(screen.getByRole('combobox', { name: 'Match for Fixture Veil' }), '1');
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await screen.findByRole('heading', { name: 'Numbers' });
  const armorClass = screen.getByRole('radiogroup', { name: 'Armor Class' });
  expect(within(armorClass).getByRole<HTMLInputElement>('radio', { name: "Keep the sheet's number" }).checked).toBe(true);
  await waitFor(() => expect(vi.mocked(client.ddbPreview).mock.lastCall?.[0].numberChoices).toEqual([{ field: 'armorClass', action: 'keepSheet' }]));
  expect(screen.getByText(/Pre-filled from your choice on step 2/)).toBeTruthy();
  await user.click(within(armorClass).getByRole('radio', { name: 'Note it' }));
  await waitFor(() => expect(vi.mocked(client.ddbPreview).mock.lastCall?.[0].numberChoices).toEqual([{ field: 'armorClass', action: 'note' }]));
});

it('sends no number choice for a row that agrees whatever the preset, and Create stays available', async () => {
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => {
    const p = preview((request.resolutions ?? []).some((r) => r.rowId === 'spell:0'));
    return { ...p, comparison: p.comparison.map((row) => ({ ...row, differs: false, sheet: row.calculated })) };
  });
  const user = userEvent.setup();
  renderPanel();
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.click(await screen.findByRole('button', { name: 'Next: rules' }));
  await user.click(screen.getByRole('radio', { name: "Use the installed sources' values" }));
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));
  await screen.findByRole('heading', { name: 'Matches' });
  await user.selectOptions(screen.getByRole('combobox', { name: 'Match for Fixture Veil' }), '1');
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await screen.findByRole('heading', { name: 'Numbers' });
  expect(screen.getAllByText('Same')).toHaveLength(2);
  await waitFor(() => expect(vi.mocked(client.ddbPreview).mock.lastCall?.[0].numberChoices).toEqual([]));
  await user.click(screen.getByRole('button', { name: 'Next: summary' }));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Create character' }).disabled).toBe(false));
});

async function toNumbersWithPreset(user: ReturnType<typeof userEvent.setup>, preset: string) {
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.click(await screen.findByRole('button', { name: 'Next: rules' }));
  await user.click(screen.getByRole('radio', { name: preset }));
  await goToNumbers(user);
}

async function goToNumbers(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));
  await screen.findByRole('heading', { name: 'Matches' });
  await user.selectOptions(screen.getByRole('combobox', { name: 'Match for Fixture Veil' }), '1');
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await screen.findByRole('heading', { name: 'Numbers' });
}

const lastChoices = () => vi.mocked(client.ddbPreview).mock.lastCall?.[0].numberChoices;

it('keeps an explicit row choice through Back and Next; the preset is only a default (D16h)', async () => {
  const user = userEvent.setup();
  renderPanel();
  await toNumbersWithPreset(user, "Use the character sheet's values");
  await user.click(within(screen.getByRole('radiogroup', { name: 'Armor Class' })).getByRole('radio', { name: 'Note it' }));
  await waitFor(() => expect(lastChoices()).toEqual([{ field: 'armorClass', action: 'note' }]));
  await user.click(screen.getByRole('button', { name: 'Back' }));
  await screen.findByRole('heading', { name: 'Matches' });
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await screen.findByRole('heading', { name: 'Numbers' });
  expect(within(screen.getByRole('radiogroup', { name: 'Armor Class' })).getByRole<HTMLInputElement>('radio', { name: 'Note it' }).checked).toBe(true);
  expect(lastChoices()).toEqual([{ field: 'armorClass', action: 'note' }]);
});

it('switching the preset to manual after visiting Numbers leaves nothing pre-filled, and the preview is not refetched on its own', async () => {
  const user = userEvent.setup();
  renderPanel();
  await toNumbersWithPreset(user, "Use the character sheet's values");
  await waitFor(() => expect(lastChoices()).toEqual([{ field: 'armorClass', action: 'keepSheet' }]));
  const settled = vi.mocked(client.ddbPreview).mock.calls.length;
  await new Promise((resolve) => setTimeout(resolve, 150));
  expect(vi.mocked(client.ddbPreview).mock.calls.length).toBe(settled); // no refetch loop from the derived choices
  await user.click(screen.getByRole('button', { name: 'Back' }));
  await user.click(screen.getByRole('button', { name: 'Back' }));
  await user.click(screen.getByRole('radio', { name: 'Decide each number myself' }));
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));
  await screen.findByRole('heading', { name: 'Matches' });
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await screen.findByRole('heading', { name: 'Numbers' });
  await waitFor(() => expect(lastChoices()).toEqual([]));
  expect(screen.queryByText(/Pre-filled from your choice/)).toBeNull();
});

it('gives the preset to a row that starts differing after the equip toggle', async () => {
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => {
    const p = preview((request.resolutions ?? []).some((r) => r.rowId === 'spell:0'));
    return { ...p, comparison: p.comparison.map((row) => (row.field === 'initiative' ? { ...row, differs: request.equipMatched === false } : row)) };
  });
  const user = userEvent.setup();
  renderPanel();
  await toNumbersWithPreset(user, "Use the character sheet's values");
  await waitFor(() => expect(lastChoices()).toEqual([{ field: 'armorClass', action: 'keepSheet' }]));
  await user.click(screen.getByRole('checkbox', { name: 'Equip matched weapons and armour' }));
  await waitFor(() => expect(lastChoices()).toEqual([{ field: 'armorClass', action: 'keepSheet' }, { field: 'initiative', action: 'keepSheet' }]));
  expect(within(screen.getByRole('radiogroup', { name: 'Initiative' })).getByRole<HTMLInputElement>('radio', { name: "Keep the sheet's number" }).checked).toBe(true);
});

it('keeps the sheet\'s speed by default even under the "sources" preset, and an explicit row choice still wins', async () => {
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => {
    const p = preview((request.resolutions ?? []).some((r) => r.rowId === 'spell:0'));
    return { ...p, comparison: [...p.comparison, { field: 'speed', label: 'Speed', sheet: 25, calculated: 30, differs: true }] };
  });
  const user = userEvent.setup();
  renderPanel();
  await toNumbersWithPreset(user, "Use the installed sources' values");
  await waitFor(() => expect(lastChoices()).toEqual([{ field: 'armorClass', action: 'useTomeStack' }, { field: 'speed', action: 'keepSheet' }]));
  expect(within(screen.getByRole('radiogroup', { name: 'Speed' })).getByRole<HTMLInputElement>('radio', { name: "Keep the sheet's number" }).checked).toBe(true);
  await user.click(within(screen.getByRole('radiogroup', { name: 'Speed' })).getByRole('radio', { name: 'Note it' }));
  await waitFor(() => expect(lastChoices()).toEqual([{ field: 'armorClass', action: 'useTomeStack' }, { field: 'speed', action: 'note' }]));
});

it('keeps the sheet\'s speed by default under the "Decide each number myself" preset too, as the step-2 hint says', async () => {
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => {
    const p = preview((request.resolutions ?? []).some((r) => r.rowId === 'spell:0'));
    return { ...p, comparison: [...p.comparison, { field: 'speed', label: 'Speed', sheet: 25, calculated: 30, differs: true }] };
  });
  const user = userEvent.setup();
  renderPanel();
  await toNumbersWithPreset(user, 'Decide each number myself');
  await waitFor(() => expect(lastChoices()).toEqual([{ field: 'speed', action: 'keepSheet' }]));
  expect(within(screen.getByRole('radiogroup', { name: 'Speed' })).getByRole<HTMLInputElement>('radio', { name: "Keep the sheet's number" }).checked).toBe(true);
  // Manual pre-fills nothing else: Armor Class shows TomeStack's number and sends no choice.
  expect(within(screen.getByRole('radiogroup', { name: 'Armor Class' })).getByRole<HTMLInputElement>('radio', { name: "Use TomeStack's number" }).checked).toBe(true);
});

it('discards the token of a read that resolves after Cancel', async () => {
  const pending = deferred<DdbReadResult>();
  vi.mocked(client.ddbRead).mockReturnValueOnce(pending.promise);
  const user = userEvent.setup();
  const { onCancel } = renderPanel();
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(onCancel).toHaveBeenCalled();
  expect(client.ddbDiscard).not.toHaveBeenCalled();

  pending.resolve(read);
  await waitFor(() => expect(client.ddbDiscard).toHaveBeenCalledWith('fixture-token'));
});

// ---- item 14: one entry for D&D Beyond exports and the fillable 2014 sheet ----

it('titles the import as a character sheet import and names the layout it read in words', async () => {
  vi.mocked(client.ddbRead).mockResolvedValue({ ...read, layout: 'wotc-2014', layoutName: 'the fillable 2014 sheet' });
  const user = userEvent.setup();
  renderPanel();

  expect(screen.getByRole('heading', { name: 'Import a character sheet (PDF)', hidden: true })).toBeTruthy();
  expect(screen.getByText(/Choose a character sheet PDF/)).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));

  const layout = (await screen.findByText('Layout')).nextElementSibling;
  expect(layout?.textContent).toBe('the fillable 2014 sheet');
  // The id is the fallback for a build that sends no name.
  expect(screen.queryByText('wotc-2014')).toBeNull();
});

it.each([
  ['D&D Beyond 2014', 'D&D Beyond 2014'],
  ['the fillable 2014 sheet', 'the fillable 2014 sheet'],
])('names the layout %s in the summary step', (layoutName, shown) => {
  const base = preview(true);
  render(
    <DdbSummaryStep
      family="srd-5.1"
      suggested="srd-5.1"
      includePlayState={false}
      onIncludePlayState={vi.fn()}
      publishClass={false}
      onPublishClass={vi.fn()}
      preview={{ ...base, report: { ...base.report, layoutName } }}
    />,
  );

  expect(screen.getByText('Layout read').nextElementSibling?.textContent).toBe(shown);
});

it('leaves the layout row out of the summary when the build sends no layout name', () => {
  const base = preview(true);
  render(
    <DdbSummaryStep family="srd-5.1" suggested="srd-5.1" includePlayState={false} onIncludePlayState={vi.fn()} publishClass={false} onPublishClass={vi.fn()} preview={{ ...base, report: { ...base.report, layoutName: undefined } }} />,
  );

  expect(screen.queryByText('Layout read')).toBeNull();
});

it('shows the report lines of the summary step, and nothing when the sheet gave none', () => {
  const base = preview(true);
  const props = { family: 'srd-5.1' as const, suggested: 'srd-5.1' as const, includePlayState: false, onIncludePlayState: vi.fn(), publishClass: false, onPublishClass: vi.fn() };
  const { rerender } = render(<DdbSummaryStep {...props} preview={base} />);
  expect(screen.queryByRole('heading', { name: 'From reading the sheet' })).toBeNull();

  const lines = ['Backstory was cut to 20,000 characters.', 'Weapon row 3: the attack bonus could not be read, so the row is in the import note only.'];
  rerender(<DdbSummaryStep {...props} preview={{ ...base, report: { ...base.report, layoutName: 'the fillable 2014 sheet', lines } }} />);

  const section = screen.getByRole('region', { name: 'From reading the sheet' });
  expect(within(section).getAllByRole('listitem').map((li) => li.textContent)).toEqual(lines);
});

// ---- D35: the import's stub class ----

const stubRow: MatchRow = { rowId: 'class:0', kind: 'class', label: 'Fixture Tinkerer 3', status: 'notFound', candidates: [] };
const stubReview = { rowId: 'class:0', name: 'Fixture Tinkerer', rulesFamily: 'srd-5.1' as const, hitDie: 8, saves: ['str', 'con'], sourceTitle: 'Characters imported from sheets' };

/** A preview whose only class row is not installed; `note` makes the offer unavailable (R100 or a hit die that cannot be read). */
function stubPreview(creating: boolean, note: 'stub.one-per-import' | 'stub.hit-die-unreadable' | null = null): DdbPreview {
  const base = preview(true);
  const matched: MatchRow = { ...stubRow, status: 'matched', note: 'stub.created', candidates: [candidate(7, 'Fixture Tinkerer')], chosen: candidate(7, 'Fixture Tinkerer') };
  return {
    ...base,
    matches: [creating ? matched : stubRow],
    stubOffers: [{ rowId: 'class:0', name: 'Fixture Tinkerer', hitDie: note ? null : 8, note }],
    stub: creating ? stubReview : null,
    canApply: creating,
  };
}

const wantsStub = (request: { createClassRow?: string }) => request.createClassRow === 'class:0';

async function toStubMatches(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Choose PDF…' }));
  await user.click(await screen.findByRole('button', { name: 'Next: rules' }));
  await user.click(await screen.findByRole('button', { name: 'Next: matches' }));
  await screen.findByRole('heading', { name: 'Matches' });
}

async function toStubSummary(user: ReturnType<typeof userEvent.setup>) {
  await toStubMatches(user);
  await user.click(await screen.findByRole('checkbox', { name: /Create a homebrew class/ }));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await user.click(await screen.findByRole('button', { name: 'Next: summary' }));
}

it('offers to create a homebrew class for an uninstalled class and sends the row with the preview', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => stubPreview(wantsStub(request)));
  renderPanel();
  await toStubMatches(user);

  const box = await screen.findByRole<HTMLInputElement>('checkbox', { name: /Create a homebrew class Fixture Tinkerer/ });
  expect(box.checked).toBe(false);
  await user.click(box);

  await waitFor(() => expect(client.ddbPreview).toHaveBeenLastCalledWith(expect.objectContaining({ createClassRow: 'class:0' })));
  await waitFor(() => expect(screen.getByRole<HTMLInputElement>('checkbox', { name: /Create a homebrew class/ }).checked).toBe(true));
  expect(document.activeElement).toBe(screen.getByRole('checkbox', { name: /Create a homebrew class/ }));
});

it('holds a second class: aria-disabled, still focusable, with its reason linked, and a click changes nothing (R100)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async () => stubPreview(false, 'stub.one-per-import'));
  renderPanel();
  await toStubMatches(user);

  const box = await screen.findByRole<HTMLInputElement>('checkbox', { name: /Create a homebrew class Fixture Tinkerer/ });
  expect(box.getAttribute('aria-disabled')).toBe('true');
  expect(box.disabled).toBe(false);
  const reason = document.getElementById(box.getAttribute('aria-describedby')!);
  expect(reason?.textContent).toContain('One class per import can be created here; the others become gap notes.');

  const calls = vi.mocked(client.ddbPreview).mock.calls.length;
  await user.click(box);
  expect(box.checked).toBe(false);
  expect(vi.mocked(client.ddbPreview).mock.calls.length).toBe(calls);
});

it('says why a class cannot be created when its hit die cannot be read', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async () => stubPreview(false, 'stub.hit-die-unreadable'));
  renderPanel();
  await toStubMatches(user);

  const box = await screen.findByRole('checkbox', { name: /Create a homebrew class Fixture Tinkerer/ });
  expect(box.getAttribute('aria-disabled')).toBe('true');
  expect(document.getElementById(box.getAttribute('aria-describedby')!)?.textContent).toContain('hit dice total');
});

it('shows the stub in full on the summary, leaves Publish it unticked and says the class is a draft', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => stubPreview(wantsStub(request)));
  vi.mocked(client.ddbApply).mockResolvedValue({ characterId: 'new-id', overrides: 0, gapNotes: 0, gapNotesNotStored: 0, report: preview(true).report, createdClass: 'class-id' });
  renderPanel();
  await toStubSummary(user);

  const section = await screen.findByRole('region', { name: 'Homebrew class to create' });
  const text = section.textContent ?? '';
  for (const part of ['Fixture Tinkerer', 'srd-5.1', 'd8', 'Strength, Constitution', 'Characters imported from sheets']) expect(text).toContain(part);
  const publish = within(section).getByRole<HTMLInputElement>('checkbox', { name: 'Publish it' });
  expect(publish.checked).toBe(false);
  expect(within(section).getByRole('note').textContent).toBe('The class is a draft: the character gets nothing from it until you publish it in the studio and accept the update on the character.');

  await user.click(screen.getByRole('button', { name: 'Create character' }));
  await waitFor(() => expect(client.ddbApply).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.ddbApply).mock.calls[0]).toEqual([expect.objectContaining({ createClassRow: 'class:0' }), false]);
});

it('sends publishClass true once Publish it is ticked, and the draft line goes away', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => stubPreview(wantsStub(request)));
  renderPanel();
  await toStubSummary(user);

  await user.click(await screen.findByRole('checkbox', { name: 'Publish it' }));
  expect(screen.queryByText(/The class is a draft/)).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Create character' }));

  await waitFor(() => expect(client.ddbApply).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.ddbApply).mock.calls[0]?.[1]).toBe(true);
});

it('puts Publish it back to unticked whenever the created class is cleared or chosen again', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => stubPreview(wantsStub(request)));
  renderPanel();
  await toStubSummary(user);
  await user.click(await screen.findByRole('checkbox', { name: 'Publish it' }));
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Publish it' }).checked).toBe(true);

  await user.click(screen.getByRole('button', { name: 'Back' })); // numbers
  await user.click(screen.getByRole('button', { name: 'Back' })); // matches
  await user.click(await screen.findByRole('checkbox', { name: /Create a homebrew class/ })); // clear it
  await waitFor(() => expect(screen.getByRole<HTMLInputElement>('checkbox', { name: /Create a homebrew class/ }).checked).toBe(false));
  await user.click(screen.getByRole('checkbox', { name: /Create a homebrew class/ })); // choose it again
  await waitFor(() => expect(screen.getByRole<HTMLInputElement>('checkbox', { name: /Create a homebrew class/ }).checked).toBe(true));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await user.click(await screen.findByRole('button', { name: 'Next: summary' }));

  expect((await screen.findByRole<HTMLInputElement>('checkbox', { name: 'Publish it' })).checked).toBe(false);
});

/** A second, ambiguous class row whose choice decides the stub's hit die: the first candidate leaves d8, the second makes d10. */
function twoClassStubPreview(request: { createClassRow?: string; resolutions?: { rowId: string; chosen?: { contentId: string } }[] }): DdbPreview {
  const base = stubPreview(wantsStub(request));
  const picked = (request.resolutions ?? []).find((r) => r.rowId === 'class:1');
  const second: MatchRow = {
    rowId: 'class:1',
    kind: 'class',
    label: 'Fixture Arcanist 2',
    status: picked ? 'matched' : 'choose',
    candidates: [candidate(5, 'Fixture Arcanist'), candidate(6, 'Fixture Arcanist')],
  };
  const d10 = picked?.chosen?.contentId === ref(6).contentId;
  return { ...base, matches: [...base.matches, second], stub: base.stub ? { ...base.stub, hitDie: d10 ? 10 : 8 } : null };
}

async function backToMatches(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Back' })); // numbers
  await user.click(screen.getByRole('button', { name: 'Back' })); // matches
}

async function forwardToSummary(user: ReturnType<typeof userEvent.setup>) {
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Next: numbers' }));
  await user.click(await screen.findByRole('button', { name: 'Next: summary' }));
}

it('puts Publish it back to unticked when another class row changes the hit die of the reviewed class', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => twoClassStubPreview(request));
  renderPanel();
  await toStubSummary(user);
  await user.click(await screen.findByRole('checkbox', { name: 'Publish it' }));
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Publish it' }).checked).toBe(true);

  await backToMatches(user);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Match for Fixture Arcanist 2' }), '1'); // d8 becomes d10
  await waitFor(() => expect(vi.mocked(client.ddbPreview)).toHaveBeenLastCalledWith(expect.objectContaining({ resolutions: [expect.objectContaining({ rowId: 'class:1' })] })));
  await forwardToSummary(user);

  const section = await screen.findByRole('region', { name: 'Homebrew class to create' });
  expect(section.textContent).toContain('d10');
  expect(within(section).getByRole<HTMLInputElement>('checkbox', { name: 'Publish it' }).checked).toBe(false);
  await user.click(screen.getByRole('button', { name: 'Create character' }));
  await waitFor(() => expect(client.ddbApply).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.ddbApply).mock.calls[0]?.[1]).toBe(false);
});

it('keeps Publish it as the user set it when a change elsewhere leaves the reviewed class identical', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => twoClassStubPreview(request));
  renderPanel();
  await toStubSummary(user);
  await user.click(await screen.findByRole('checkbox', { name: 'Publish it' }));

  await backToMatches(user);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Match for Fixture Arcanist 2' }), '0'); // still d8
  await waitFor(() => expect(vi.mocked(client.ddbPreview)).toHaveBeenLastCalledWith(expect.objectContaining({ resolutions: [expect.objectContaining({ rowId: 'class:1' })] })));
  await forwardToSummary(user);

  expect((await screen.findByRole<HTMLInputElement>('checkbox', { name: 'Publish it' })).checked).toBe(true);
});

it('holds Next while the shown preview is not for the current choices, also after a failed re-preview, and a retry frees it', async () => {
  const user = userEvent.setup();
  let failing = false;
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => {
    if (failing) throw new TomeStackError({ code: 'fixture.failed', message: 'Fixture preview failed.' });
    return twoClassStubPreview(request);
  });
  renderPanel();
  await toStubSummary(user);
  await backToMatches(user);
  failing = true;
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Match for Fixture Arcanist 2' }), '1');

  const alert = await screen.findByRole('alert');
  expect(alert.textContent).toContain('Fixture preview failed.');
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(true);

  failing = false;
  await user.click(within(alert).getByRole('button', { name: 'Try again' }));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Next: numbers' }).disabled).toBe(false));
});

it('holds Create after a failed re-preview on the summary, never applies, and a retry frees it', async () => {
  const user = userEvent.setup();
  let failing = false;
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => {
    if (failing) throw new TomeStackError({ code: 'fixture.failed', message: 'Fixture preview failed.' });
    return twoClassStubPreview(request);
  });
  renderPanel();
  await toStubSummary(user);
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Create character' }).disabled).toBe(false));

  failing = true;
  await user.click(screen.getByRole('checkbox', { name: /Bring over current hit points/ }));
  const alert = await screen.findByRole('alert');
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Create character' }).disabled).toBe(true);
  expect(client.ddbApply).not.toHaveBeenCalled();

  failing = false;
  await user.click(within(alert).getByRole('button', { name: 'Try again' }));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Create character' }).disabled).toBe(false));
});

it('says on the summary that the created class lives in a source that is never shared, and how to share one (D35)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => stubPreview(wantsStub(request)));
  renderPanel();
  await toStubSummary(user);

  const section = await screen.findByRole('region', { name: 'Homebrew class to create' });
  expect(section.textContent).toContain('It lives in a source that is never shared, so your later work on it cannot be shared or moved; to share your own version, make a new class.');
});

it('Cancel with a class chosen discards the sheet and never applies', async () => {
  const user = userEvent.setup();
  vi.mocked(client.ddbPreview).mockImplementation(async (request) => stubPreview(wantsStub(request)));
  const { onCancel } = renderPanel();
  await toStubMatches(user);
  await user.click(await screen.findByRole('checkbox', { name: /Create a homebrew class/ }));

  await user.click(screen.getByRole('button', { name: 'Cancel' }));

  expect(client.ddbDiscard).toHaveBeenCalledWith('fixture-token');
  expect(client.ddbApply).not.toHaveBeenCalled();
  expect(onCancel).toHaveBeenCalled();
});
