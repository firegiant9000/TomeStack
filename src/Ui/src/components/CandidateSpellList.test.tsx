// @vitest-environment jsdom
// Item 10: "Add to spell list…" on the ticked spell candidates in the import review.
import { act, cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CandidateCheck, DraftCandidate, ImportJob, SourceRecord, StoredCandidate } from '../api/types';
import { CandidateReviewPanel } from './CandidateReviewPanel';

vi.mock('../api/client', () => ({
  client: {
    candidates: vi.fn(),
    checkCandidate: vi.fn(),
    editCandidate: vi.fn(),
    acceptCandidate: vi.fn(),
    acceptCandidates: vi.fn(),
    addCandidatesToSpellList: vi.fn(),
    spellListKeys: vi.fn(),
  },
}));

const source = { id: 'src-2', title: 'Testy Both', rulesFamilies: ['srd-5.1', 'srd-5.2.1'] } as SourceRecord;
const job = { id: 'job-1', sourceId: 'src-2' } as ImportJob;

const candidate = (n: number, kind: 'spell' | 'item'): StoredCandidate => ({
  id: `cand-${n}`,
  jobId: 'job-1',
  status: 'pending',
  updatedAt: '2026-10-14T00:00:00Z',
  candidate: {
    id: `cand-${n}`,
    sourceId: 'src-2',
    page: { start: 1 },
    excerpt: 'Testy excerpt',
    proposedKind: kind,
    proposedName: `Testy ${kind === 'spell' ? 'Spell' : 'Item'} ${n}`,
    rulesFamilies: ['srd-5.1'],
    proposedEffects: [],
    confidence: 0.85,
    uncertainties: [],
    unresolvedReferences: [],
    fields: {},
    lowConfidenceFields: [],
  } as DraftCandidate,
});

const check: CandidateCheck = {
  candidateId: 'cand-1',
  report: { errors: [], warnings: [], canPublish: true } as unknown as CandidateCheck['report'],
  dependencies: [],
  blockers: [],
  canAccept: true,
  canAcceptAsReference: true,
};

const held = (el: HTMLElement) => el.getAttribute('aria-disabled') === 'true';
const reasonOf = (el: HTMLElement) => document.getElementById(el.getAttribute('aria-describedby') ?? '')?.textContent;

/** cand-1 and cand-2 are spells, cand-3 an item. */
beforeEach(() => {
  vi.mocked(client.candidates).mockReset();
  vi.mocked(client.candidates).mockImplementation(() => Promise.resolve({ items: [candidate(1, 'spell'), candidate(2, 'spell'), candidate(3, 'item')], total: 3 }));
  vi.mocked(client.checkCandidate).mockResolvedValue(check);
  vi.mocked(client.spellListKeys).mockResolvedValue([{ key: 'wizard', spells: 214 }]);
  vi.mocked(client.addCandidatesToSpellList).mockReset();
});
afterEach(cleanup);

function renderPanel(onStatus: (text: string) => void = () => {}) {
  render(<CandidateReviewPanel job={job} source={source} onError={() => {}} onStatus={onStatus} onOpenPage={() => {}} onOpenStudio={() => {}} />);
}

const addButton = (name: string) => screen.getByRole('button', { name });

it('names how many ticked spells the list button will change, and holds it with a reason when there are none', async () => {
  const user = userEvent.setup();
  renderPanel();
  await screen.findByRole('button', { name: 'Testy Spell 1' });

  const none = addButton('Add to a spell list…');
  expect(held(none)).toBe(true);
  expect(none).toHaveProperty('disabled', false);
  expect(reasonOf(none)).toBe('Select at least one candidate first.');

  await user.click(screen.getByRole('checkbox', { name: 'Select Testy Item 3' }));
  expect(reasonOf(addButton('Add to a spell list…'))).toBe('Select at least one spell first.');

  await user.click(screen.getByRole('checkbox', { name: 'Select Testy Spell 1' }));
  expect(held(addButton('Add 1 spell to a spell list…'))).toBe(false);
  await user.click(screen.getByRole('checkbox', { name: 'Select Testy Spell 2' }));
  expect(held(addButton('Add 2 spells to a spell list…'))).toBe(false);
});

it('adds the key to the ticked spells only, reports through the app status, and explains what was skipped', async () => {
  const user = userEvent.setup();
  vi.mocked(client.addCandidatesToSpellList).mockResolvedValue({
    listKey: 'wizard',
    updated: [candidate(1, 'spell')],
    skipped: [{ candidateId: 'cand-2', codes: ['candidate.already-reviewed'] }],
    notes: [],
  });
  const onStatus = vi.fn();
  renderPanel(onStatus);
  await user.click(await screen.findByRole('button', { name: 'Select the 3 shown' }));

  await user.click(addButton('Add 2 spells to a spell list…'));
  const input = screen.getByRole('combobox', { name: 'Spell list key' });
  expect(document.activeElement).toBe(input);
  await user.type(input, ' Wizard');
  expect(screen.getByText('Saved as: wizard')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Add 2 spells to this list' }));

  expect(client.addCandidatesToSpellList).toHaveBeenCalledExactlyOnceWith(['cand-1', 'cand-2'], 'wizard');
  await vi.waitFor(() => expect(onStatus).toHaveBeenCalledWith(expect.stringMatching(/^Added the spell list wizard to 1 spell; 1 skipped\./)));
  expect(onStatus.mock.calls.at(-1)![0]).toContain('Accept them to make drafts');
  await vi.waitFor(() => expect(screen.queryByRole('combobox', { name: 'Spell list key' })).toBeNull()); // the form closed
  expect(document.activeElement).toBe(addButton('Add 2 spells to a spell list…')); // focus is back where it started
  const reasons = screen.getByRole('list', { name: 'Not changed' });
  expect(within(reasons).getByText(/Testy Spell 2/).textContent).toContain('was already reviewed');
  expect(vi.mocked(client.candidates).mock.calls.length).toBeGreaterThan(1); // the list was reloaded
  expect(document.querySelectorAll('[aria-live="polite"]')).toHaveLength(1);
  expect(screen.queryByRole('status')).toBeNull();
});

it('says, with counts only, how many spells keep other proposed lists to check (R59)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.addCandidatesToSpellList).mockResolvedValue({
    listKey: 'wizard',
    updated: [candidate(1, 'spell'), candidate(2, 'spell')],
    skipped: [],
    notes: [
      { candidateId: 'cand-1', codes: ['spell-list.other-lists-unreviewed'] },
      { candidateId: 'cand-2', codes: ['spell-list.other-lists-unreviewed'] },
    ],
  });
  const onStatus = vi.fn();
  renderPanel(onStatus);
  await user.click(await screen.findByRole('button', { name: 'Select the 3 shown' }));
  await user.click(addButton('Add 2 spells to a spell list…'));
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  await user.click(screen.getByRole('button', { name: 'Add 2 spells to this list' }));

  await vi.waitFor(() => expect(onStatus).toHaveBeenCalled());
  const text = onStatus.mock.calls.at(-1)![0] as string;
  expect(text).toContain('2 spells keep other proposed lists to check.');
  expect(text).not.toContain('Testy'); // counts only, never a name
});

it('holds the list button with a linked reason while an edit is unsaved', async () => {
  const user = userEvent.setup();
  renderPanel();
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Spell 1' }));
  await user.click(screen.getByRole('button', { name: 'Testy Spell 2' }));
  await user.type(await screen.findByLabelText('Name'), ' II');
  const reason = await screen.findByText('Save or undo your edit first.');

  const button = addButton('Add 1 spell to a spell list…');
  expect(held(button)).toBe(true);
  expect(button.getAttribute('aria-describedby')).toBe(reason.id);
  await user.click(button);
  expect(screen.queryByRole('combobox', { name: 'Spell list key' })).toBeNull();
});

it('holds the form confirm if an edit starts after the form opened', async () => {
  const user = userEvent.setup();
  renderPanel();
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Spell 1' }));
  await user.click(addButton('Add 1 spell to a spell list…'));
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  await user.click(screen.getByRole('button', { name: 'Testy Spell 2' }));
  await user.type(await screen.findByLabelText('Name'), ' II');
  const reason = await screen.findByText('Save or undo your edit first.');

  const confirm = screen.getByRole('button', { name: 'Add 1 spell to this list' });
  expect(held(confirm)).toBe(true);
  expect(confirm.getAttribute('aria-describedby')).toBe(reason.id);
  await user.click(confirm);
  expect(client.addCandidatesToSpellList).not.toHaveBeenCalled();
});

it('holds paging and the filters while the add is in flight, and the accept buttons with them', async () => {
  const user = userEvent.setup();
  let finish: (value: { listKey: string; updated: StoredCandidate[]; skipped: []; notes: [] }) => void = () => {};
  vi.mocked(client.addCandidatesToSpellList).mockReturnValue(new Promise((resolve) => (finish = resolve)));
  renderPanel();
  await user.click(await screen.findByRole('button', { name: 'Select the 3 shown' }));
  await user.click(addButton('Add 2 spells to a spell list…'));
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  await user.click(screen.getByRole('button', { name: 'Add 2 spells to this list' }));

  const kind = screen.getByLabelText('Kind');
  expect(held(kind)).toBe(true);
  expect(reasonOf(kind)).toBe('Adding to the spell list…');
  expect(held(addButton('Accept selected as drafts'))).toBe(true);
  await act(async () => finish({ listKey: 'wizard', updated: [candidate(1, 'spell'), candidate(2, 'spell')], skipped: [], notes: [] }));
  await vi.waitFor(() => expect(held(screen.getByLabelText('Kind'))).toBe(false));
});

it('closes the form when the selection no longer holds a spell', async () => {
  const user = userEvent.setup();
  renderPanel();
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Spell 1' }));
  await user.click(addButton('Add 1 spell to a spell list…'));
  expect(screen.getByRole('combobox', { name: 'Spell list key' })).toBeTruthy();
  await user.click(screen.getByRole('checkbox', { name: 'Select Testy Spell 1' })); // untick
  expect(screen.queryByRole('combobox', { name: 'Spell list key' })).toBeNull();
});
