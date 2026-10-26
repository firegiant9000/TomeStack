// @vitest-environment jsdom
// Item 2: the review screen says what happens to an accepted entry and offers the way to the studio.
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { CandidateCheck, DraftCandidate, ImportJob, SourceRecord, StoredCandidate } from '../api/types';
import { expectNoAxeViolations } from '../test/axe';
import { CandidateReviewPanel } from './CandidateReviewPanel';

vi.mock('../api/client', () => ({
  client: { candidates: vi.fn(), checkCandidate: vi.fn(), editCandidate: vi.fn(), acceptCandidate: vi.fn(), ignoreCandidate: vi.fn(), acceptCandidates: vi.fn(), importedPage: vi.fn(), contentDuplicates: vi.fn() },
}));

const source = { id: 'src-1', title: 'Testy Tome', rulesFamilies: ['srd-5.1'] } as SourceRecord;
const bothSource = { id: 'src-2', title: 'Testy Both', rulesFamilies: ['srd-5.1', 'srd-5.2.1'] } as SourceRecord;
const job = { id: 'job-1', sourceId: 'src-1' } as ImportJob;

const pageOf = (items: StoredCandidate[], total = items.length) => ({ items, total });

beforeEach(() => {
  vi.mocked(client.candidates).mockResolvedValue(pageOf([]));
  vi.mocked(client.contentDuplicates).mockResolvedValue([]);
});
afterEach(cleanup);

function renderPanel(onOpenStudio = vi.fn(), forSource: SourceRecord = source) {
  render(<CandidateReviewPanel job={job} source={forSource} onError={() => {}} onStatus={() => {}} onOpenPage={() => {}} onOpenStudio={onOpenStudio} />);
  return onOpenStudio;
}

it('says, always visible and not as a live region, that accepted entries are drafts in the studio', async () => {
  renderPanel();
  await screen.findByText('No candidates match.');
  const line = screen.getByText('Accepted entries are drafts in the Homebrew studio under Testy Tome. Publish them there to use them in the builder.');
  expect(line.tagName).toBe('P');
  expect(line.className).toBe('hint');
  expect(line.closest('[aria-live], [role="status"], [role="alert"]')).toBeNull();
});

it('opens the source in the studio from a named button', async () => {
  const onOpenStudio = renderPanel();
  await screen.findByText('No candidates match.');
  await userEvent.setup().click(screen.getByRole('button', { name: 'Open Testy Tome in the studio' }));
  expect(onOpenStudio).toHaveBeenCalledExactlyOnceWith('src-1');
});

// Items 3 and 4: a candidate's rules families, edited within the source's.
const detected = (rulesFamilies: string[]): StoredCandidate => ({
  id: 'cand-1',
  jobId: 'job-1',
  status: 'pending',
  etag: 'etag-1',
  updatedAt: '2026-10-14T00:00:00Z',
  candidate: {
    id: 'cand-1',
    sourceId: 'src-2',
    page: { start: 4 },
    excerpt: 'Testy Coat 11',
    proposedKind: 'item',
    proposedName: 'Testy Coat',
    rulesFamilies,
    proposedEffects: [],
    confidence: 0.85,
    uncertainties: [],
    unresolvedReferences: [],
    fields: {},
    lowConfidenceFields: [],
  } as DraftCandidate,
});

const checkOf = (over: Partial<CandidateCheck>): CandidateCheck => ({
  candidateId: 'cand-1',
  report: { errors: [], warnings: [], canPublish: true } as unknown as CandidateCheck['report'],
  dependencies: [],
  blockers: [],
  canAccept: true,
  canAcceptAsReference: true,
  ...over,
});

async function openDetail(rulesFamilies: string[], check: CandidateCheck) {
  vi.mocked(client.candidates).mockResolvedValue(pageOf([detected(rulesFamilies)]));
  vi.mocked(client.checkCandidate).mockResolvedValue(check);
  vi.mocked(client.editCandidate).mockResolvedValue(detected(rulesFamilies));
  renderPanel(vi.fn(), bothSource);
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Testy Coat' }));
  return screen.findByRole('group', { name: 'Rules families' });
}

it('offers one checkbox per family of the source, ticked as the candidate has them', async () => {
  const group = await openDetail(['srd-5.1'], checkOf({}));
  const boxes = within(group).getAllByRole('checkbox');
  expect(boxes.map((b) => (b as HTMLInputElement).labels?.[0]?.textContent?.trim())).toEqual(['2014 rules (SRD 5.1)', '2024 rules (SRD 5.2.1)']);
  expect(boxes.map((b) => (b as HTMLInputElement).checked)).toEqual([true, false]);
});

it('offers only the source families, even for a source with one', async () => {
  vi.mocked(client.candidates).mockResolvedValue(pageOf([detected(['srd-5.1'])]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  renderPanel(vi.fn(), source);
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Testy Coat' }));
  expect(within(await screen.findByRole('group', { name: 'Rules families' })).getAllByRole('checkbox')).toHaveLength(1);
});

it('saves the ticked families with the edit', async () => {
  const user = userEvent.setup();
  const group = await openDetail([], checkOf({ canAccept: false, canAcceptAsReference: false }));
  await user.click(within(group).getByRole('checkbox', { name: '2024 rules (SRD 5.2.1)' }));
  await user.click(screen.getByRole('button', { name: 'Save edit' }));
  expect(client.editCandidate).toHaveBeenCalledWith('cand-1', expect.objectContaining({ rulesFamilies: ['srd-5.2.1'] }));
});

it('still saves with no family ticked, and Accept shows the servers reason', async () => {
  const user = userEvent.setup();
  const group = await openDetail(
    [],
    checkOf({
      canAccept: false,
      canAcceptAsReference: false,
      blockers: [{ code: 'import.family-needed', message: 'Say which rules family this entry is written for.' }],
    }),
  );
  expect(within(group).getAllByRole('checkbox').map((b) => (b as HTMLInputElement).checked)).toEqual([false, false]);
  await screen.findByRole('list', { name: 'Blocks accepting' });
  expect(screen.getByText('Say which rules family this entry is written for.')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Accept as a draft' }).getAttribute('aria-disabled')).toBe('true');
  await user.click(screen.getByRole('button', { name: 'Save edit' }));
  expect(client.editCandidate).toHaveBeenCalledWith('cand-1', expect.objectContaining({ rulesFamilies: [] }));
});

it('keeps a blocked Accept focusable, refuses the press, and links the visible reason', async () => {
  vi.mocked(client.acceptCandidate).mockClear();
  const user = userEvent.setup();
  await openDetail(
    [],
    checkOf({
      canAccept: false,
      canAcceptAsReference: false,
      blockers: [{ code: 'import.family-needed', message: 'Say which rules family this entry is written for.' }],
    }),
  );
  const list = await screen.findByRole('list', { name: 'Blocks accepting' });
  expect(list.id).not.toBe('');
  for (const name of ['Accept as a draft', 'Accept as reference']) {
    const button = screen.getByRole('button', { name });
    expect(button).toHaveProperty('disabled', false);
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.getAttribute('aria-describedby')).toBe(list.id);
    button.focus();
    await user.click(button);
    expect(document.activeElement).toBe(button);
  }
  expect(client.acceptCandidate).not.toHaveBeenCalled();
});

// Ruling R26: the studio button must not silently drop an unsaved edit.
it('holds the studio button, with a linked reason, while the open candidate has an unsaved edit', async () => {
  const user = userEvent.setup();
  const onOpenStudio = vi.fn();
  vi.mocked(client.candidates).mockResolvedValue(pageOf([detected(['srd-5.1'])]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  render(<CandidateReviewPanel job={job} source={bothSource} onError={() => {}} onStatus={() => {}} onOpenPage={() => {}} onOpenStudio={onOpenStudio} />);
  const studio = await screen.findByRole('button', { name: 'Open Testy Both in the studio' });
  expect(studio.getAttribute('aria-disabled')).toBeNull();

  await user.click(screen.getByRole('button', { name: 'Testy Coat' }));
  const name = await screen.findByLabelText('Name');
  expect(studio.getAttribute('aria-disabled')).toBeNull(); // opening a candidate is not an edit

  await user.type(name, ' II');
  expect(studio.getAttribute('aria-disabled')).toBe('true');
  expect(studio).toHaveProperty('disabled', false);
  expect(document.getElementById(studio.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Save or undo your edit first.');
  await user.click(studio);
  expect(onOpenStudio).not.toHaveBeenCalled();

  // Typing it back is an undo: nothing differs from what is stored.
  await user.type(name, '{Backspace}{Backspace}{Backspace}');
  expect(studio.getAttribute('aria-disabled')).toBeNull();
  expect(screen.queryByText('Save or undo your edit first.')).toBeNull();
  await user.click(studio);
  expect(onOpenStudio).toHaveBeenCalledExactlyOnceWith('src-2');
});

it('counts a changed family, text, effects or kind as an edit, and a saved edit as none', async () => {
  const user = userEvent.setup();
  vi.mocked(client.candidates).mockResolvedValue(pageOf([detected(['srd-5.1'])]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  vi.mocked(client.editCandidate).mockResolvedValue(detected(['srd-5.1', 'srd-5.2.1']));
  render(<CandidateReviewPanel job={job} source={bothSource} onError={() => {}} onStatus={() => {}} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  const studio = await screen.findByRole('button', { name: 'Open Testy Both in the studio' });
  await user.click(screen.getByRole('button', { name: 'Testy Coat' }));
  const group = await screen.findByRole('group', { name: 'Rules families' });

  await user.click(within(group).getByRole('checkbox', { name: '2024 rules (SRD 5.2.1)' }));
  expect(studio.getAttribute('aria-disabled')).toBe('true');

  vi.mocked(client.candidates).mockResolvedValue(pageOf([{ ...detected(['srd-5.1', 'srd-5.2.1']), updatedAt: '2026-10-14T00:01:00Z' }]));
  await user.click(screen.getByRole('button', { name: 'Save edit' }));
  await vi.waitFor(() => expect(studio.getAttribute('aria-disabled')).toBeNull());
});

// Item 13: the list pages at 50, filters by a page range, and says when detection stopped early.
const numbered = (n: number): StoredCandidate => {
  const base = detected(['srd-5.1']);
  return { ...base, id: `cand-${n}`, etag: `etag-${n}`, candidate: { ...base.candidate, id: `cand-${n}`, proposedName: `Testy Item ${n}`, page: { start: 1 + Math.floor(n / 10) } } };
};

/** A job of `total` candidates; the mock answers each request from its offset and limit. */
function longList(total: number) {
  vi.mocked(client.candidates).mockReset();
  vi.mocked(client.candidates).mockImplementation((_jobId, _filter, paging) => {
    const offset = paging?.offset ?? 0;
    const limit = paging?.limit ?? 50;
    const all = Array.from({ length: total }, (_, i) => numbered(i + 1));
    return Promise.resolve(pageOf(all.slice(offset, offset + limit), total));
  });
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
}

const held = (button: HTMLElement) => button.getAttribute('aria-disabled') === 'true';

it('pages at 50 with Previous 50 and Next 50, held (not disabled) at the ends', async () => {
  const user = userEvent.setup();
  longList(120);
  renderPanel(vi.fn(), bothSource);
  const next = await screen.findByRole('button', { name: 'Next 50' });
  const previous = screen.getByRole('button', { name: 'Previous 50' });
  expect(vi.mocked(client.candidates)).toHaveBeenLastCalledWith('job-1', { status: 'pending' }, { offset: 0, limit: 50 });
  expect(within(screen.getByRole('list', { name: 'Candidates' })).getAllByRole('listitem')).toHaveLength(50);
  expect([held(previous), held(next)]).toEqual([true, false]);
  expect(previous).toHaveProperty('disabled', false);

  await user.click(previous); // held: nothing happens
  expect(vi.mocked(client.candidates)).toHaveBeenCalledTimes(1);

  await user.click(next);
  await screen.findByRole('button', { name: 'Testy Item 51' });
  expect(vi.mocked(client.candidates)).toHaveBeenLastCalledWith('job-1', { status: 'pending' }, { offset: 50, limit: 50 });
  expect([held(previous), held(next)]).toEqual([false, false]);

  await user.click(next);
  await screen.findByRole('button', { name: 'Testy Item 101' });
  expect(within(screen.getByRole('list', { name: 'Candidates' })).getAllByRole('listitem')).toHaveLength(20);
  expect([held(previous), held(next)]).toEqual([false, true]);
  expect(document.activeElement).toBe(next); // focus stays on the control that was pressed

  await user.click(previous);
  await screen.findByRole('button', { name: 'Testy Item 51' });
});

it('holds both paging buttons for a list that fits on one page', async () => {
  longList(9);
  renderPanel(vi.fn(), bothSource);
  await screen.findByRole('button', { name: 'Testy Item 9' });
  expect(held(screen.getByRole('button', { name: 'Previous 50' }))).toBe(true);
  expect(held(screen.getByRole('button', { name: 'Next 50' }))).toBe(true);
  expect(screen.getByText('1–9 of 9')).toBeTruthy();
});

it('announces "Showing 51–100 of 340" politely when the page changes, and not on load', async () => {
  const user = userEvent.setup();
  longList(340);
  renderPanel(vi.fn(), bothSource);
  await screen.findByRole('button', { name: 'Next 50' });
  await screen.findByRole('button', { name: 'Testy Item 1' });
  expect(screen.queryByText(/^Showing /)).toBeNull(); // nothing announced by opening the list
  expect(screen.getByText('1–50 of 340')).toBeTruthy();
  expect(screen.queryByRole('status')).toBeNull(); // the app has the one role=status

  await user.click(screen.getByRole('button', { name: 'Next 50' }));
  const live = await screen.findByText('Showing 51–100 of 340');
  expect(live.tagName).toBe('P');
  expect(live.getAttribute('aria-live')).toBe('polite');
  expect(screen.getByText('51–100 of 340')).toBeTruthy();
});

it('says in plain text above the list that detection kept only the first 5,000 or stopped before a page', async () => {
  longList(3);
  render(
    <CandidateReviewPanel
      job={{ ...job, truncatedCandidates: true, detectionLimitedFromPage: 812 }}
      source={bothSource}
      onError={() => {}}
      onStatus={() => {}}
      onOpenPage={() => {}}
      onOpenStudio={() => {}}
    />,
  );
  const list = await screen.findByRole('list', { name: 'Candidates' });
  for (const text of [
    'Showing the first 5,000 candidates; read fewer pages at a time to see the rest.',
    'Detection stopped before page 812; read pages 812 on as a range to detect them.',
  ]) {
    const notice = screen.getByText(text);
    expect(notice.tagName).toBe('P');
    expect(notice.closest('[aria-live], [role="status"], [role="alert"]')).toBeNull();
    expect(notice.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy(); // above the list
  }
});

it('shows no truncation notice for an ordinary job', async () => {
  longList(3);
  renderPanel(vi.fn(), bothSource);
  await screen.findByRole('list', { name: 'Candidates' });
  expect(screen.queryByText(/first 5,000/)).toBeNull();
  expect(screen.queryByText(/Detection stopped/)).toBeNull();
});

it('filters by From page and To page, and goes back to the first page when a filter changes', async () => {
  const user = userEvent.setup();
  longList(120);
  renderPanel(vi.fn(), bothSource);
  expect(screen.queryByLabelText('Page')).toBeNull();
  await user.click(await screen.findByRole('button', { name: 'Next 50' }));
  await screen.findByRole('button', { name: 'Testy Item 51' });

  await user.type(screen.getByLabelText('From page'), '7');
  await vi.waitFor(() => expect(vi.mocked(client.candidates)).toHaveBeenLastCalledWith('job-1', { status: 'pending', firstPage: 7 }, { offset: 0, limit: 50 }));
  await user.type(screen.getByLabelText('To page'), '9');
  await vi.waitFor(() =>
    expect(vi.mocked(client.candidates)).toHaveBeenLastCalledWith('job-1', { status: 'pending', firstPage: 7, lastPage: 9 }, { offset: 0, limit: 50 }),
  );
  await user.selectOptions(screen.getByLabelText('Kind'), 'spell');
  await vi.waitFor(() =>
    expect(vi.mocked(client.candidates)).toHaveBeenLastCalledWith('job-1', { status: 'pending', firstPage: 7, lastPage: 9, kind: 'spell' }, { offset: 0, limit: 50 }),
  );
});

// Ruling R35: paging, the filters and the other rows must not silently drop an unsaved edit.
async function openFirstAndEdit(user: ReturnType<typeof userEvent.setup>) {
  longList(120);
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Testy Item 1' }));
  const name = await screen.findByLabelText('Name');
  await user.type(name, ' II');
  const reason = await screen.findByText('Save or undo your edit first.');
  return { name, reason };
}

it('holds Previous 50 and Next 50 while the open candidate has an unsaved edit', async () => {
  const user = userEvent.setup();
  const { name, reason } = await openFirstAndEdit(user);
  const next = screen.getByRole('button', { name: 'Next 50' });
  const calls = vi.mocked(client.candidates).mock.calls.length;

  expect(held(next)).toBe(true);
  expect(next).toHaveProperty('disabled', false);
  expect(next.getAttribute('aria-describedby')).toBe(reason.id);
  expect(held(screen.getByRole('button', { name: 'Previous 50' }))).toBe(true);
  next.focus();
  await user.click(next);
  expect(document.activeElement).toBe(next);
  expect(vi.mocked(client.candidates)).toHaveBeenCalledTimes(calls); // refused in the handler
  expect((name as HTMLInputElement).value).toBe('Testy Item 1 II');

  // Undo: the same button works again.
  await user.type(name, '{Backspace}{Backspace}{Backspace}');
  expect(held(next)).toBe(false);
  await user.click(next);
  await screen.findByRole('button', { name: 'Testy Item 51' });
});

it('holds every filter control while the open candidate has an unsaved edit', async () => {
  const user = userEvent.setup();
  const { reason } = await openFirstAndEdit(user);
  const calls = vi.mocked(client.candidates).mock.calls.length;

  const show = within(screen.getByRole('group', { name: 'Show' })); // the detail form has its own Kind
  for (const label of ['From page', 'To page', 'Kind', 'Confidence', 'Status']) {
    const control = show.getByLabelText(label);
    expect(control.getAttribute('aria-disabled')).toBe('true');
    expect(control.getAttribute('aria-describedby')).toBe(reason.id);
    expect(control).toHaveProperty('disabled', false);
  }
  await user.type(show.getByLabelText('From page'), '3');
  await user.selectOptions(show.getByLabelText('Kind'), 'spell');
  expect((show.getByLabelText('From page') as HTMLInputElement).value).toBe('');
  expect((show.getByLabelText('Kind') as HTMLSelectElement).value).toBe('');
  expect(vi.mocked(client.candidates)).toHaveBeenCalledTimes(calls);
});

it('holds the other rows, keeps the edit when one is pressed, and still lets the open row be pressed', async () => {
  const user = userEvent.setup();
  const { name, reason } = await openFirstAndEdit(user);
  const other = screen.getByRole('button', { name: 'Testy Item 2' });

  expect(other.getAttribute('aria-disabled')).toBe('true');
  expect(other.getAttribute('aria-describedby')).toBe(reason.id);
  expect(other).toHaveProperty('disabled', false);
  other.focus();
  await user.click(other);
  expect(document.activeElement).toBe(other);
  expect(screen.getByRole('button', { name: 'Testy Item 1' }).getAttribute('aria-current')).toBe('true');
  expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Testy Item 1 II'); // not dropped
  expect(screen.getByLabelText('Name')).toBe(name); // the same form, not a remount

  const open = screen.getByRole('button', { name: 'Testy Item 1' });
  expect(open.getAttribute('aria-disabled')).toBeNull();

  await user.type(name, '{Backspace}{Backspace}{Backspace}');
  expect(other.getAttribute('aria-disabled')).toBeNull();
  await user.click(other);
  expect(screen.getByRole('button', { name: 'Testy Item 2' }).getAttribute('aria-current')).toBe('true');
});

// Fix round 1 (review of item 13).
it('keeps focus on Previous 50 after paging away from an open candidate and back', async () => {
  const user = userEvent.setup();
  longList(120);
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Testy Item 1' }));
  await screen.findByLabelText('Name');

  await user.click(screen.getByRole('button', { name: 'Next 50' }));
  await screen.findByRole('button', { name: 'Testy Item 51' });
  expect(screen.queryByLabelText('Name')).toBeNull(); // the open candidate is not on this page, so its detail closes
  const previous = screen.getByRole('button', { name: 'Previous 50' });
  await user.click(previous);
  await screen.findByRole('button', { name: 'Testy Item 1' });

  expect(document.activeElement).toBe(previous);
  expect(screen.queryByLabelText('Name')).toBeNull();
  expect(screen.getByRole('button', { name: 'Testy Item 1' }).getAttribute('aria-current')).toBeNull();
});

it('keeps focus on the filter after one that drops the open candidate is undone', async () => {
  const user = userEvent.setup();
  longList(3);
  const everything = vi.mocked(client.candidates).getMockImplementation()!;
  vi.mocked(client.candidates).mockImplementation((id, f, p) => (f?.kind === 'spell' ? Promise.resolve(pageOf([])) : everything(id, f, p)));
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Testy Item 1' }));
  await screen.findByLabelText('Name');
  const kind = within(screen.getByRole('group', { name: 'Show' })).getByLabelText('Kind');

  await user.selectOptions(kind, 'spell');
  await screen.findByText('No candidates match.');
  await user.selectOptions(kind, '');
  await screen.findByRole('button', { name: 'Testy Item 1' });

  expect(document.activeElement).toBe(kind);
  expect(screen.queryByLabelText('Name')).toBeNull();
});

it('points a paging button held at an end to the visible range, and an edit to its own reason', async () => {
  const user = userEvent.setup();
  longList(340);
  renderPanel(vi.fn(), bothSource);
  const previous = await screen.findByRole('button', { name: 'Previous 50' });
  const next = screen.getByRole('button', { name: 'Next 50' });
  await screen.findByRole('button', { name: 'Testy Item 1' });
  const range = screen.getByText('1–50 of 340');

  expect(range.id).not.toBe('');
  expect(previous.getAttribute('aria-describedby')).toBe(range.id);
  expect(next.getAttribute('aria-describedby')).toBeNull(); // not held

  await user.click(next);
  await screen.findByRole('button', { name: 'Testy Item 51' });
  expect(previous.getAttribute('aria-describedby')).toBeNull();

  // An unsaved edit is the reason that counts.
  await user.click(screen.getByRole('button', { name: 'Previous 50' }));
  await user.click(await screen.findByRole('button', { name: 'Testy Item 1' }));
  await user.type(await screen.findByLabelText('Name'), ' II');
  const reason = screen.getByText('Save or undo your edit first.');
  expect(previous.getAttribute('aria-describedby')).toBe(reason.id);
  expect(next.getAttribute('aria-describedby')).toBe(reason.id);
});

it('stays on the shown page, and announces nothing later, when a page turn fails', async () => {
  const user = userEvent.setup();
  longList(120);
  const works = vi.mocked(client.candidates).getMockImplementation()!;
  let failing = true;
  vi.mocked(client.candidates).mockImplementation((id, f, p) => (failing && p?.offset === 50 ? Promise.reject(new Error('no')) : works(id, f, p)));
  vi.mocked(client.editCandidate).mockResolvedValue(numbered(1));
  const onError = vi.fn();
  render(<CandidateReviewPanel job={job} source={bothSource} onError={onError} onStatus={() => {}} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Next 50' }));
  await vi.waitFor(() => expect(onError).toHaveBeenCalled());

  expect(screen.getByText('1–50 of 120')).toBeTruthy(); // still the page that is shown
  expect(screen.getByRole('button', { name: 'Testy Item 1' })).toBeTruthy();
  expect(screen.queryByText(/^Showing /)).toBeNull();

  // A reload after an action (a saved edit) does not announce on its own.
  const calls = vi.mocked(client.candidates).mock.calls.length;
  await user.click(screen.getByRole('button', { name: 'Testy Item 1' }));
  await user.type(await screen.findByLabelText('Name'), 'x');
  await user.click(screen.getByRole('button', { name: 'Save edit' }));
  await vi.waitFor(() => expect(vi.mocked(client.candidates).mock.calls.length).toBeGreaterThan(calls));
  expect(screen.queryByText(/^Showing /)).toBeNull();
  await user.type(screen.getByLabelText('Name'), '{Backspace}'); // the mock keeps the old row, so undo the typed letter

  // The next press goes to page 2 (it does not skip to page 3).
  failing = false;
  await user.click(screen.getByRole('button', { name: 'Next 50' }));
  await screen.findByRole('button', { name: 'Testy Item 51' });
  expect(screen.getByText('51–100 of 120')).toBeTruthy();
  expect(screen.getByText('Showing 51–100 of 120')).toBeTruthy();
});

it('moves back a page, with focus on the heading, when the only candidate on the last page is accepted', async () => {
  const user = userEvent.setup();
  let accepted = false;
  vi.mocked(client.candidates).mockReset();
  vi.mocked(client.candidates).mockImplementation((_id, _f, paging) => {
    const all = Array.from({ length: accepted ? 50 : 51 }, (_, i) => numbered(i + 1));
    const offset = paging?.offset ?? 0;
    return Promise.resolve(pageOf(all.slice(offset, offset + 50), all.length));
  });
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  vi.mocked(client.acceptCandidate).mockImplementation(() => {
    accepted = true;
    return Promise.resolve(numbered(51));
  });
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Next 50' }));
  await user.click(await screen.findByRole('button', { name: 'Testy Item 51' }));
  await user.click(await screen.findByRole('button', { name: 'Accept as a draft' }));

  await screen.findByRole('button', { name: 'Testy Item 50' });
  expect(screen.getByText('1–50 of 50')).toBeTruthy();
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Candidates from Testy Both' }));
});

it('shows the newest filter result when two requests answer out of order', async () => {
  const user = userEvent.setup();
  longList(3);
  const everything = vi.mocked(client.candidates).getMockImplementation()!;
  const answers: Record<string, (value: { items: StoredCandidate[]; total: number }) => void> = {};
  vi.mocked(client.candidates).mockImplementation((id, f, p) =>
    f?.kind === 'spell' || f?.kind === 'feat' ? new Promise((resolve) => (answers[f.kind as string] = resolve)) : everything(id, f, p),
  );
  renderPanel(vi.fn(), bothSource);
  await screen.findByRole('button', { name: 'Testy Item 1' });
  const kind = within(screen.getByRole('group', { name: 'Show' })).getByLabelText('Kind');

  await user.selectOptions(kind, 'spell');
  await user.selectOptions(kind, 'feat');
  answers.feat?.(pageOf([numbered(22)]));
  await screen.findByRole('button', { name: 'Testy Item 22' });
  answers.spell?.(pageOf([numbered(11)])); // the older request answers last
  await new Promise((resolve) => setTimeout(resolve, 50));

  expect(screen.queryByRole('button', { name: 'Testy Item 11' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Testy Item 22' })).toBeTruthy();
});

it('reloads for the page-range fields only after a pause in typing, while the field shows each keystroke', async () => {
  longList(120);
  renderPanel(vi.fn(), bothSource);
  await screen.findByRole('button', { name: 'Testy Item 1' });
  const calls = vi.mocked(client.candidates).mock.calls.length;
  const from = screen.getByLabelText('From page') as HTMLInputElement;

  vi.useFakeTimers();
  try {
    fireEvent.change(from, { target: { value: '7' } });
    fireEvent.change(from, { target: { value: '75' } });
    expect(from.value).toBe('75');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(250);
    });
    expect(vi.mocked(client.candidates)).toHaveBeenCalledTimes(calls); // still waiting
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
    });
  } finally {
    vi.useRealTimers();
  }
  expect(vi.mocked(client.candidates)).toHaveBeenCalledTimes(calls + 1);
  expect(vi.mocked(client.candidates)).toHaveBeenLastCalledWith('job-1', { status: 'pending', firstPage: 75 }, { offset: 0, limit: 50 });
});

// Item 9, D33: row checkboxes, "Select the N shown", and the two bulk accepts.
const pollite = () => screen.getByText((_, el) => el?.tagName === 'P' && el.getAttribute('aria-live') === 'polite' && el.classList.contains('selection-note'));

it('ticks rows, says how many are selected politely, and selects only the rows shown', async () => {
  const user = userEvent.setup();
  longList(120);
  renderPanel(vi.fn(), bothSource);
  await screen.findByRole('button', { name: 'Testy Item 1' });

  const boxes = screen.getAllByRole('checkbox', { name: /^Select Testy Item / });
  expect(boxes).toHaveLength(50);
  expect(boxes[0]!.className).toContain('row-select'); // 24 px target (WCAG 2.5.8) is its CSS
  await user.click(boxes[0]!);
  expect(screen.getByText('1 selected', { selector: '[aria-live="polite"]' }).tagName).toBe('P');

  await user.click(screen.getByRole('button', { name: 'Select the 50 shown' }));
  expect(boxes.every((b) => (b as HTMLInputElement).checked)).toBe(true);
  expect(screen.getByText('50 selected', { selector: '[aria-live="polite"]' })).toBeTruthy();
  expect(screen.queryByRole('status')).toBeNull(); // never a second role=status
  expect(pollite()).toBeTruthy();
});

it('says the real count on the last page, "Select the 20 shown"', async () => {
  const user = userEvent.setup();
  longList(70);
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Next 50' }));
  await screen.findByRole('button', { name: 'Testy Item 51' });
  expect(screen.getByRole('button', { name: 'Select the 20 shown' })).toBeTruthy();
});

it('clears the selection when the page or a filter changes, announcing it once', async () => {
  const user = userEvent.setup();
  longList(120);
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Select the 50 shown' }));
  expect(screen.getByText('50 selected', { selector: '[aria-live="polite"]' })).toBeTruthy();

  await user.click(screen.getByRole('button', { name: 'Next 50' }));
  await screen.findByRole('button', { name: 'Testy Item 51' });
  expect(screen.getByText('Showing 51–100 of 120. Selection cleared.', { selector: '[aria-live="polite"]' })).toBeTruthy();
  expect(screen.getAllByRole('checkbox', { name: /^Select Testy Item / }).some((b) => (b as HTMLInputElement).checked)).toBe(false);

  await user.click(screen.getByRole('button', { name: 'Select the 50 shown' }));
  await user.selectOptions(screen.getByLabelText('Kind'), 'spell');
  expect(screen.getByText('Selection cleared', { selector: '[aria-live="polite"]' })).toBeTruthy();
});

it('holds the bulk buttons with a linked reason until something is selected', async () => {
  const user = userEvent.setup();
  longList(3);
  renderPanel(vi.fn(), bothSource);
  const drafts = await screen.findByRole('button', { name: 'Accept selected as drafts' });
  const reference = screen.getByRole('button', { name: 'Accept selected as reference' });
  for (const button of [drafts, reference]) {
    expect(held(button)).toBe(true);
    expect(button).toHaveProperty('disabled', false);
    expect(document.getElementById(button.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Select at least one candidate first.');
    await user.click(button);
  }
  expect(client.acceptCandidates).not.toHaveBeenCalled();
  await user.click(screen.getByRole('checkbox', { name: 'Select Testy Item 1' }));
  expect(held(drafts)).toBe(false);
});

it('accepts the selection in bulk, reports the counts, and leaves the blocked rows selected and explained', async () => {
  const user = userEvent.setup();
  longList(3);
  const onStatus = vi.fn();
  render(<CandidateReviewPanel job={job} source={bothSource} onError={() => {}} onStatus={onStatus} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Select the 3 shown' }));
  vi.mocked(client.acceptCandidates).mockResolvedValue({
    accepted: [numbered(1), numbered(2)],
    blocked: [{ candidateId: 'cand-3', codes: ['candidate.unresolved-reference', 'import.family-needed'] }],
  });
  // After the accept the first two are no longer pending.
  const everything = vi.mocked(client.candidates).getMockImplementation()!;
  vi.mocked(client.candidates).mockImplementation((id, f, p) => everything(id, f, p).then((page) => ({ items: page.items.filter((c) => c.id === 'cand-3'), total: 1 })));

  await user.click(screen.getByRole('button', { name: 'Accept selected as drafts' }));

  expect(client.acceptCandidates).toHaveBeenCalledExactlyOnceWith(['cand-1', 'cand-2', 'cand-3'], false, { 'cand-1': 'etag-1', 'cand-2': 'etag-2', 'cand-3': 'etag-3' });
  await vi.waitFor(() => expect(onStatus).toHaveBeenCalledWith(expect.stringMatching(/^Accepted 2; 1 need review\./)));
  await screen.findByText('1 selected', { selector: '[aria-live="polite"]' });
  expect((screen.getByRole('checkbox', { name: 'Select Testy Item 3' }) as HTMLInputElement).checked).toBe(true);
  const reasons = screen.getByRole('list', { name: 'Not accepted' });
  expect(within(reasons).getByText(/Testy Item 3/).textContent).toContain('needs a rules family');
});

it('accepts as reference with the same selection', async () => {
  const user = userEvent.setup();
  longList(2);
  vi.mocked(client.acceptCandidates).mockResolvedValue({ accepted: [numbered(1)], blocked: [] });
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Item 1' }));
  await user.click(screen.getByRole('button', { name: 'Accept selected as reference' }));
  expect(client.acceptCandidates).toHaveBeenLastCalledWith(['cand-1'], true, { 'cand-1': 'etag-1' });
});

it('reloads and clears the selection when a bulk accept is refused as changed, so the next try is not stale (R139)', async () => {
  const user = userEvent.setup();
  longList(3);
  const onError = vi.fn();
  vi.mocked(client.acceptCandidates).mockRejectedValueOnce(new TomeStackError({ code: 'import.candidate-changed', message: 'Fixture: changed.' }));
  render(<CandidateReviewPanel job={job} source={bothSource} onError={onError} onStatus={() => {}} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Select the 3 shown' }));
  const calls = vi.mocked(client.candidates).mock.calls.length;
  const button = screen.getByRole('button', { name: 'Accept selected as drafts' });
  await user.click(button);

  await screen.findByText('Some selected candidates changed; the selection was cleared.', { selector: '[aria-live="polite"]' });
  expect(onError).not.toHaveBeenCalled(); // a refusal to act on, not an error to show
  expect(vi.mocked(client.candidates).mock.calls.length).toBeGreaterThan(calls); // the list was reloaded, for fresh tokens
  expect(screen.getAllByRole('checkbox', { name: /^Select Testy Item / }).some((b) => (b as HTMLInputElement).checked)).toBe(false);
  expect(document.activeElement).toBe(button); // WCAG 2.4.3: focus stays where it was
});

it('ignores a second press while a bulk accept is in flight', async () => {
  const user = userEvent.setup();
  longList(2);
  vi.mocked(client.acceptCandidates).mockReset();
  let finish: (value: { accepted: StoredCandidate[]; blocked: [] }) => void = () => {};
  vi.mocked(client.acceptCandidates).mockReturnValue(new Promise((resolve) => (finish = resolve)));
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Select the 2 shown' }));
  const button = screen.getByRole('button', { name: 'Accept selected as drafts' });
  await user.click(button);
  await user.click(button);
  await user.click(screen.getByRole('button', { name: 'Accept selected as reference' }));
  expect(client.acceptCandidates).toHaveBeenCalledTimes(1);
  await act(async () => finish({ accepted: [], blocked: [] }));
});

it('holds the checkboxes and bulk buttons while the open candidate has an unsaved edit', async () => {
  const user = userEvent.setup();
  vi.mocked(client.acceptCandidates).mockClear();
  const { reason } = await openFirstAndEdit(user);

  const box = screen.getByRole('checkbox', { name: 'Select Testy Item 2' });
  expect(box.getAttribute('aria-disabled')).toBe('true');
  expect(box.getAttribute('aria-describedby')).toBe(reason.id);
  await user.click(box);
  expect((box as HTMLInputElement).checked).toBe(false);
  for (const name of ['Select the 50 shown', 'Accept selected as drafts', 'Accept selected as reference']) {
    const button = screen.getByRole('button', { name });
    expect(held(button)).toBe(true);
    expect(button.getAttribute('aria-describedby')).toBe(reason.id);
    await user.click(button);
  }
  expect(screen.queryByText(/ selected$/)).toBeNull();
  expect(client.acceptCandidates).not.toHaveBeenCalled();
});

// Fix round 1, items 14 and 15.
it('holds paging and the filters, with a linked reason, while a bulk accept is in flight', async () => {
  const user = userEvent.setup();
  longList(120);
  vi.mocked(client.acceptCandidates).mockReset();
  let finish: (value: { accepted: StoredCandidate[]; blocked: [] }) => void = () => {};
  vi.mocked(client.acceptCandidates).mockReturnValue(new Promise((resolve) => (finish = resolve)));
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Select the 50 shown' }));
  await user.click(screen.getByRole('button', { name: 'Accept selected as drafts' }));
  const calls = vi.mocked(client.candidates).mock.calls.length;

  const next = screen.getByRole('button', { name: 'Next 50' });
  const kind = screen.getByLabelText('Kind');
  for (const control of [next, kind, screen.getByRole('button', { name: 'Select the 50 shown' }), screen.getByRole('checkbox', { name: 'Select Testy Item 1' })]) {
    expect(held(control)).toBe(true);
    expect(document.getElementById(control.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Accepting the selected candidates…');
  }
  await user.click(next);
  await user.selectOptions(kind, 'spell');
  expect(vi.mocked(client.candidates)).toHaveBeenCalledTimes(calls);
  expect(screen.getByText('1–50 of 120')).toBeTruthy();

  await act(async () => finish({ accepted: [], blocked: [] }));
  await vi.waitFor(() => expect(held(screen.getByRole('button', { name: 'Next 50' }))).toBe(false));
});

it('turning the page with ticks makes one combined announcement in the one polite paragraph', async () => {
  const user = userEvent.setup();
  longList(120);
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Select the 50 shown' }));
  await user.click(screen.getByRole('button', { name: 'Next 50' }));
  await screen.findByRole('button', { name: 'Testy Item 51' });
  expect(pollite().textContent).toBe('Showing 51–100 of 120. Selection cleared.');
  expect(document.querySelectorAll('[aria-live="polite"]')).toHaveLength(1);
  expect(screen.queryByRole('status')).toBeNull();
});

// Fix round 2, item 4.
it('a page-range pause that is still running does not fire during a bulk accept', async () => {
  const user = userEvent.setup();
  longList(120);
  vi.mocked(client.acceptCandidates).mockReset();
  let finish: (value: { accepted: StoredCandidate[]; blocked: [] }) => void = () => {};
  vi.mocked(client.acceptCandidates).mockReturnValue(new Promise((resolve) => (finish = resolve)));
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Select the 50 shown' }));
  const from = screen.getByLabelText('From page') as HTMLInputElement;
  fireEvent.change(from, { target: { value: '75' } }); // the 300 ms pause starts
  await user.click(screen.getByRole('button', { name: 'Accept selected as drafts' }));
  const calls = vi.mocked(client.candidates).mock.calls.length;

  await new Promise((resolve) => setTimeout(resolve, 450));

  expect(vi.mocked(client.candidates)).toHaveBeenCalledTimes(calls); // the filter did not change mid-flight
  expect(from.value).toBe(''); // the field shows the range that is applied
  expect((screen.getByRole('checkbox', { name: 'Select Testy Item 1' }) as HTMLInputElement).checked).toBe(true);
  await act(async () => finish({ accepted: [], blocked: [] }));
});

// Item 11 (D34, ADR-016): a detected class page ends in a usable class draft once the reviewer confirms what was read.
const lamp = (): StoredCandidate => {
  const base = detected(['srd-5.2.1']);
  return {
    ...base,
    candidate: {
      ...base.candidate,
      proposedKind: 'class',
      proposedName: 'Testy Lamplighter',
      proposedEffects: [
        { id: 'hit-die', type: 'hitDie', die: 8, automation: 'reference' },
        { id: 'spellcasting', type: 'spellcasting', automation: 'reference', slots: [[2], [3], [4, 2]], cantrips: [2, 2, 2], spellsTable: [2, 3, 4] },
      ],
    },
    grantsCandidates: [
      { candidateId: 'feat-a', level: 1 },
      { candidateId: 'feat-b', level: 3 },
      { candidateId: 'feat-b', level: 5 },
    ],
  };
};

async function openClass(check: CandidateCheck = checkOf({})) {
  vi.mocked(client.candidates).mockImplementation(() => Promise.resolve(pageOf([lamp()])));
  vi.mocked(client.checkCandidate).mockResolvedValue(check);
  vi.mocked(client.acceptCandidate).mockReset();
  vi.mocked(client.acceptCandidate).mockResolvedValue(lamp());
  renderPanel(vi.fn(), source);
  await userEvent.setup().click(await screen.findByRole('button', { name: 'Testy Lamplighter' }));
  await screen.findByRole('heading', { name: 'Candidate: Testy Lamplighter' });
}

it('shows the slot table with a caption and headers, and Open page beside it', async () => {
  await openClass();
  const table = await screen.findByRole('table', { name: 'Spell slots by class level, as read from the page' });
  expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Class level', 'Cantrips', 'Spells', '1st-level slots', '2nd-level slots']);
  expect(within(table).getAllByRole('rowheader').map((h) => h.textContent)).toEqual(['1', '2', '3']);
  const row3 = within(table).getByRole('rowheader', { name: '3' }).closest('tr')!;
  expect(within(row3).getAllByRole('cell').map((c) => c.textContent)).toEqual(['2', '4', '4', '2']);
  expect(screen.getAllByRole('button', { name: 'Open page 4' })).toHaveLength(1);
});

it('holds Accept as a draft, with a visible reason, until the hit die and slot table are ticked, and sends what was ticked', async () => {
  const user = userEvent.setup();
  await openClass();
  const accept = await screen.findByRole('button', { name: 'Accept as a draft' });
  const hit = screen.getByRole('checkbox', { name: 'Confirm the hit die, d8' }) as HTMLInputElement;
  const slots = screen.getByRole('checkbox', { name: 'Confirm the spell slot table' }) as HTMLInputElement;
  expect([hit.checked, slots.checked]).toEqual([false, false]);
  expect(accept.getAttribute('aria-disabled')).toBe('true');
  expect(accept.hasAttribute('disabled')).toBe(false);
  const reason = document.getElementById(accept.getAttribute('aria-describedby')!)!;
  expect(reason.textContent).toBe('Confirm the hit die and the spell slot table to accept the class as a draft.');

  accept.focus();
  await user.click(accept);
  expect(client.acceptCandidate).not.toHaveBeenCalled();
  expect(document.activeElement).toBe(accept);

  await user.click(hit);
  expect(document.getElementById(accept.getAttribute('aria-describedby')!)!.textContent).toBe('Confirm the spell slot table to accept the class as a draft.');
  await user.click(slots);
  expect(accept.getAttribute('aria-disabled')).toBeNull();
  await user.click(accept);
  expect(client.acceptCandidate).toHaveBeenCalledExactlyOnceWith('cand-1', false, 'etag-1', ['hit-die', 'spellcasting']);
});

it('accepts a class as reference without any confirmation, and sends none', async () => {
  const user = userEvent.setup();
  await openClass();
  await user.click(await screen.findByRole('button', { name: 'Accept as reference' }));
  expect(client.acceptCandidate).toHaveBeenCalledExactlyOnceWith('cand-1', true, 'etag-1');
});

it('adds no live region for a class', async () => {
  await openClass();
  await screen.findByRole('table');
  expect(document.querySelectorAll('[aria-live], [role="status"], [role="alert"]')).toHaveLength(1);
});

// Task 5 fix round 1. The class with every kind of proposal the detector makes.
const fullLamp = (): StoredCandidate => {
  const base = lamp();
  return {
    ...base,
    candidate: {
      ...base.candidate,
      proposedEffects: [
        ...base.candidate.proposedEffects,
        { id: 'save-wisdom', type: 'grant', grant: 'proficiency', target: 'save.wisdom', automation: 'reference' },
        { id: 'armor-light', type: 'grant', grant: 'proficiency', target: 'armor.light', automation: 'reference' },
        { id: 'armor-shield', type: 'grant', grant: 'proficiency', target: 'armor.shield', automation: 'reference' },
        { id: 'weapon-simple', type: 'grant', grant: 'proficiency', target: 'weapon.simple', automation: 'reference' },
        { id: 'lamp-subclass', type: 'choice', choiceId: 'lamp-subclass', count: 1, level: 3, automation: 'reference' },
      ],
    },
  };
};

/** Opens a class whose stored row the test can replace, as a save would. */
async function openStored(rows: { current: StoredCandidate }, check: CandidateCheck = checkOf({})) {
  vi.mocked(client.candidates).mockImplementation(() => Promise.resolve(pageOf([rows.current])));
  vi.mocked(client.checkCandidate).mockResolvedValue(check);
  vi.mocked(client.acceptCandidate).mockReset();
  vi.mocked(client.acceptCandidate).mockResolvedValue(rows.current);
  renderPanel(vi.fn(), source);
  await userEvent.setup().click(await screen.findByRole('button', { name: (rows.current.edited ?? rows.current.candidate).proposedName }));
  await screen.findByRole('heading', { name: `Candidate: ${(rows.current.edited ?? rows.current.candidate).proposedName}` });
}

// Item 8.
it('offers one unticked Confirm checkbox per proposed effect, named by it, and sends every ticked id', async () => {
  const user = userEvent.setup();
  await openStored({ current: fullLamp() });
  const group = await screen.findByRole('group', { name: 'Confirm what was read' });
  const boxes = within(group).getAllByRole('checkbox') as HTMLInputElement[];
  expect(boxes.map((b) => b.labels?.[0]?.textContent?.trim())).toEqual([
    'Confirm the hit die, d8',
    'Confirm the spell slot table',
    'Confirm the Wisdom saving throw proficiency',
    'Confirm the light armor proficiency',
    'Confirm the shield proficiency',
    'Confirm the simple weapons proficiency',
    'Confirm the subclass choice at level 3',
  ]);
  expect(boxes.every((b) => !b.checked)).toBe(true);
  expect(boxes.every((b) => b.closest('label')?.className === 'choice')).toBe(true);

  const accept = screen.getByRole('button', { name: 'Accept as a draft' });
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the Wisdom saving throw proficiency' }));
  expect(accept.getAttribute('aria-disabled')).toBe('true'); // the hold stays on the hit die and slot table
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the hit die, d8' }));
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the spell slot table' }));
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the shield proficiency' }));
  expect(accept.getAttribute('aria-disabled')).toBeNull();
  await user.click(accept);
  expect(client.acceptCandidate).toHaveBeenCalledExactlyOnceWith('cand-1', false, 'etag-1', ['hit-die', 'spellcasting', 'save-wisdom', 'armor-shield']);
});

it('has no axe violations in the detail view with its confirm ticks', async () => {
  await openStored({ current: fullLamp() });
  const group = await screen.findByRole('group', { name: 'Confirm what was read' });
  expect(within(group).getAllByRole('checkbox').length).toBeGreaterThan(0);
  await expectNoAxeViolations(document.body);
});

// Item 9.
it('treats a candidate edited into a class as a class', async () => {
  const user = userEvent.setup();
  const item = detected(['srd-5.2.1']);
  const asClass = fullLamp().candidate;
  await openStored({ current: { ...item, candidate: { ...asClass, proposedKind: 'item' }, edited: asClass } });
  expect(await screen.findByRole('group', { name: 'Confirm what was read' })).toBeTruthy();
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the hit die, d8' }));
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the spell slot table' }));
  await user.click(screen.getByRole('button', { name: 'Accept as a draft' }));
  expect(client.acceptCandidate).toHaveBeenCalledExactlyOnceWith('cand-1', false, 'etag-1', ['hit-die', 'spellcasting']);
});

it('treats a class edited into another kind as that kind', async () => {
  const user = userEvent.setup();
  const stored = fullLamp();
  await openStored({ current: { ...stored, edited: { ...stored.candidate, proposedKind: 'feature' } } });
  const accept = await screen.findByRole('button', { name: 'Accept as a draft' });
  expect(screen.queryByRole('group', { name: 'Confirm what was read' })).toBeNull();
  await vi.waitFor(() => expect(accept.getAttribute('aria-disabled')).toBeNull());
  await user.click(accept);
  expect(client.acceptCandidate).toHaveBeenCalledExactlyOnceWith('cand-1', false, 'etag-1');
});

// Item 10.
it('explains a class held back from a bulk accept in words, and never shows a code', async () => {
  const user = userEvent.setup();
  longList(2);
  render(<CandidateReviewPanel job={job} source={bothSource} onError={() => {}} onStatus={() => {}} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Select the 2 shown' }));
  vi.mocked(client.acceptCandidates).mockResolvedValue({
    accepted: [],
    blocked: [
      { candidateId: 'cand-1', codes: ['import.class-unconfirmed'] },
      { candidateId: 'cand-2', codes: ['candidate.some-future-code'] },
    ],
  });
  await user.click(screen.getByRole('button', { name: 'Accept selected as drafts' }));
  const reasons = await screen.findByRole('list', { name: 'Not accepted' });
  expect(within(reasons).getByText(/Testy Item 1/).textContent).toContain('is a class: open it, tick the hit die and slot table, and accept it on its own');
  expect(within(reasons).getByText(/Testy Item 2/).textContent).toContain('was held back by a check: open it to see why');
  expect(reasons.textContent).not.toMatch(/[a-z]+\.[a-z-]+/);
});

// Item 11.
it('lists the linked features the check returns, with installed and ignored ones said, and reads no candidate page for them', async () => {
  const linked: CandidateCheck['linked'] = [
    { candidateId: 'feat-a', name: 'Testy Glow', level: 1, status: 'pending', installed: false },
    { name: 'Testy Spark', level: 2, installed: true },
    { candidateId: 'feat-b', name: 'Testy Beacon', level: 3, status: 'accepted', installed: false },
    { candidateId: 'feat-b', name: 'Testy Beacon', level: 5, status: 'accepted', installed: false },
    { candidateId: 'feat-c', name: 'Testy Dim', level: 4, status: 'ignored', installed: false },
  ];
  await openStored({ current: lamp() }, checkOf({ linked, notes: ['1 linked feature is ignored and will not be granted.'] }));
  const list = await screen.findByRole('list', { name: 'Linked features' });
  expect(within(list).getAllByRole('listitem').map((li) => li.textContent)).toEqual([
    'Testy Glow, level 1',
    'Testy Spark, level 2, installed',
    'Testy Beacon, levels 3, 5, already accepted',
    'Testy Dim, level 4, ignored: not granted',
  ]);
  expect(screen.getByText('1 linked feature is ignored and will not be granted.')).toBeTruthy();
  expect(vi.mocked(client.candidates).mock.calls.some(([, filter]) => filter?.kind === 'feature')).toBe(false);
});

// Task 5 fix round 2.
it('writes Depends on with a space, and shows the check notes, counts only', async () => {
  await openStored(
    { current: lamp() },
    checkOf({
      dependencies: [{ kind: 'content', name: 'Testy Glow' }],
      notes: ['2 table features are not linked and will not be granted; add them in the studio.'],
    }),
  );
  expect(await screen.findByText('2 table features are not linked and will not be granted; add them in the studio.')).toBeTruthy();
  expect(screen.getByText(/^Depends on:/).textContent).toBe('Depends on: Testy Glow');
});

it('says a class is accepted granting only the linked features that are granted, not the ignored ones', async () => {
  const user = userEvent.setup();
  const linked: CandidateCheck['linked'] = [
    { candidateId: 'feat-a', name: 'Testy Glow', level: 1, status: 'pending', installed: false },
    { name: 'Testy Spark', level: 2, installed: true },
    { candidateId: 'feat-c', name: 'Testy Dim', level: 4, status: 'ignored', installed: false },
  ];
  const onStatus = vi.fn();
  vi.mocked(client.candidates).mockImplementation(() => Promise.resolve(pageOf([lamp()])));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({ linked }));
  vi.mocked(client.acceptCandidate).mockReset();
  vi.mocked(client.acceptCandidate).mockResolvedValue(lamp());
  render(<CandidateReviewPanel job={job} source={source} onError={() => {}} onStatus={onStatus} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Testy Lamplighter' }));
  await screen.findByRole('list', { name: 'Linked features' });
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the hit die, d8' }));
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the spell slot table' }));
  await user.click(screen.getByRole('button', { name: 'Accept as a draft' }));
  await vi.waitFor(() => expect(onStatus).toHaveBeenCalled());
  expect(onStatus.mock.calls[0]?.[0]).toMatch(/^Testy Lamplighter is now a draft granting 2 features in the studio\./);
});

// Item 12.
it('clears the ticks when a saved edit changes the effects, and keeps them when it does not', async () => {
  const user = userEvent.setup();
  const rows = { current: lamp() };
  await openStored(rows);
  vi.mocked(client.editCandidate).mockImplementation((_id, edit) => {
    const candidate = { ...rows.current.candidate, proposedName: edit.name ?? rows.current.candidate.proposedName, proposedEffects: (edit.effects ?? []) as DraftCandidate['proposedEffects'] };
    rows.current = { ...rows.current, edited: candidate, etag: `${rows.current.etag}x`, updatedAt: `${rows.current.updatedAt}x` };
    return Promise.resolve(rows.current);
  });
  await user.click(await screen.findByRole('checkbox', { name: 'Confirm the hit die, d8' }));
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the spell slot table' }));

  // The name changes; the effects do not: the ticks stay.
  const name = screen.getByLabelText('Name');
  await user.clear(name);
  await user.type(name, 'Testy Lantern');
  await user.click(screen.getByRole('button', { name: 'Save edit' }));
  await screen.findByRole('heading', { name: 'Candidate: Testy Lantern' });
  expect((screen.getByRole('checkbox', { name: 'Confirm the hit die, d8' }) as HTMLInputElement).checked).toBe(true);
  expect((screen.getByRole('checkbox', { name: 'Confirm the spell slot table' }) as HTMLInputElement).checked).toBe(true);

  // The hit die changes: everything is to confirm again.
  const effects = screen.getByLabelText(/^Proposed effects/) as HTMLTextAreaElement;
  fireEvent.change(effects, { target: { value: effects.value.replace('"die": 8', '"die": 10') } });
  await user.click(screen.getByRole('button', { name: 'Save edit' }));
  const hit = (await screen.findByRole('checkbox', { name: 'Confirm the hit die, d10' })) as HTMLInputElement;
  expect(hit.checked).toBe(false);
  expect((screen.getByRole('checkbox', { name: 'Confirm the spell slot table' }) as HTMLInputElement).checked).toBe(false);
});

// Item 13.
it('says on a class that the ticked effects become automatic and the others stay reference', async () => {
  await openStored({ current: lamp() });
  expect(
    await screen.findByLabelText('Proposed effects (JSON; on accepting the class as a draft, the ones you tick under Confirm what was read are automatic, the others stay reference)'),
  ).toBeTruthy();
});

it('keeps the reference-only label for other kinds', async () => {
  await openDetail([], checkOf({}));
  expect(screen.getByLabelText('Proposed effects (JSON; they stay reference-only until you change them in the studio)')).toBeTruthy();
});

// R120: an edit carries the token it was opened with; a refused one reloads the candidate and says so.
it('sends the opened candidates token with the edit', async () => {
  const user = userEvent.setup();
  vi.mocked(client.editCandidate).mockClear();
  vi.mocked(client.candidates).mockResolvedValue(pageOf([{ ...detected(['srd-5.1']), etag: 'token-1' }]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  vi.mocked(client.editCandidate).mockResolvedValue({ ...detected(['srd-5.1']), etag: 'token-2' });
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Testy Coat' }));
  await user.click(await screen.findByRole('button', { name: 'Save edit' }));
  expect(client.editCandidate).toHaveBeenCalledWith('cand-1', expect.objectContaining({ expected: 'token-1' }));
});

it('reloads the candidate and says it changed when the edit is refused, without reporting an error', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  const onStatus = vi.fn();
  vi.mocked(client.candidates).mockReset();
  vi.mocked(client.candidates).mockResolvedValueOnce(pageOf([{ ...detected(['srd-5.1']), etag: 'token-1' }]));
  vi.mocked(client.candidates).mockResolvedValue(pageOf([{ ...detected(['srd-5.1']), etag: 'token-9', updatedAt: '2026-10-15T00:00:00Z' }]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  vi.mocked(client.editCandidate).mockRejectedValueOnce(new TomeStackError({ code: 'import.candidate-changed', message: 'Fixture: changed.' }));
  render(<CandidateReviewPanel job={job} source={bothSource} onError={onError} onStatus={onStatus} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Testy Coat' }));
  await user.click(await screen.findByRole('button', { name: 'Save edit' }));
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('This candidate changed since you opened it. It has been reloaded.'));
  expect(onError).not.toHaveBeenCalled();
  expect(vi.mocked(client.candidates).mock.calls.length).toBe(2);
  // The editor is still there, on the reloaded candidate, and focus is inside it.
  const heading = await screen.findByRole('heading', { name: 'Candidate: Testy Coat' });
  await waitFor(() => expect(heading.closest('section')?.contains(document.activeElement)).toBe(true));
});

// R134: accept and ignore carry the opened candidate's token too.
it('sends the opened candidates token with Accept as reference and with Ignore', async () => {
  const user = userEvent.setup();
  vi.mocked(client.acceptCandidate).mockReset();
  vi.mocked(client.ignoreCandidate).mockReset();
  vi.mocked(client.candidates).mockResolvedValue(pageOf([{ ...detected(['srd-5.1']), etag: 'token-1' }]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  vi.mocked(client.acceptCandidate).mockResolvedValue(detected(['srd-5.1']));
  vi.mocked(client.ignoreCandidate).mockResolvedValue(detected(['srd-5.1']));
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Testy Coat' }));
  await user.click(await screen.findByRole('button', { name: 'Accept as reference' }));
  await waitFor(() => expect(client.acceptCandidate).toHaveBeenCalledExactlyOnceWith('cand-1', true, 'token-1'));
  await user.click(await screen.findByRole('button', { name: 'Ignore' }));
  await waitFor(() => expect(client.ignoreCandidate).toHaveBeenCalledExactlyOnceWith('cand-1', 'token-1'));
});

it.each([
  ['Accept as a draft', 'acceptCandidate'],
  ['Ignore', 'ignoreCandidate'],
] as const)('reloads the candidate and says it changed when %s is refused, without reporting an error', async (button, method) => {
  const user = userEvent.setup();
  const onError = vi.fn();
  const onStatus = vi.fn();
  vi.mocked(client.candidates).mockReset();
  vi.mocked(client.candidates).mockResolvedValueOnce(pageOf([{ ...detected(['srd-5.1']), etag: 'token-1' }]));
  vi.mocked(client.candidates).mockResolvedValue(pageOf([{ ...detected(['srd-5.1']), etag: 'token-9', updatedAt: '2026-10-15T00:00:00Z' }]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  vi.mocked(client[method]).mockRejectedValueOnce(new TomeStackError({ code: 'import.candidate-changed', message: 'Fixture: changed.' }));
  render(<CandidateReviewPanel job={job} source={bothSource} onError={onError} onStatus={onStatus} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Testy Coat' }));
  await user.click(await screen.findByRole('button', { name: button }));
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('This candidate changed since you opened it. It has been reloaded.'));
  expect(onError).not.toHaveBeenCalled();
  expect(vi.mocked(client.candidates).mock.calls.length).toBe(2);
  const heading = await screen.findByRole('heading', { name: 'Candidate: Testy Coat' });
  await waitFor(() => expect(heading.closest('section')?.contains(document.activeElement)).toBe(true));
});

// R134: a linked feature that moved on does not change the class's own etag, so the check is asked for again by hand.
it.each(['candidate.linked-edited', 'candidate.linked-changed'])('asks for the check again, and says so, when a linked feature moved on (%s)', async (code) => {
  const user = userEvent.setup();
  const onError = vi.fn();
  const onStatus = vi.fn();
  vi.mocked(client.candidates).mockReset();
  vi.mocked(client.candidates).mockResolvedValue(pageOf([lamp()]));
  vi.mocked(client.checkCandidate).mockReset();
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  vi.mocked(client.acceptCandidate).mockReset();
  vi.mocked(client.acceptCandidate).mockRejectedValueOnce(new TomeStackError({ code, message: 'Fixture: a feature moved on.' }));
  render(<CandidateReviewPanel job={job} source={source} onError={onError} onStatus={onStatus} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Testy Lamplighter' }));
  await user.click(await screen.findByRole('checkbox', { name: 'Confirm the hit die, d8' }));
  await user.click(screen.getByRole('checkbox', { name: 'Confirm the spell slot table' }));
  await waitFor(() => expect(vi.mocked(client.checkCandidate)).toHaveBeenCalledTimes(1));

  await user.click(screen.getByRole('button', { name: 'Accept as a draft' }));

  await waitFor(() => expect(vi.mocked(client.checkCandidate)).toHaveBeenCalledTimes(2));
  expect(onStatus).toHaveBeenCalledWith('A feature this class grants changed while it was being accepted. The check has been refreshed; look it over and accept again.');
  expect(onError).not.toHaveBeenCalled();
});

// R134: accept and ignore act on the stored candidate, so they are held, like selecting another one, while the form differs from it.
it('holds Accept as a draft, Accept as reference and Ignore, with a linked reason, while the open candidate has an unsaved edit', async () => {
  const user = userEvent.setup();
  vi.mocked(client.acceptCandidate).mockReset();
  vi.mocked(client.ignoreCandidate).mockReset();
  vi.mocked(client.acceptCandidate).mockResolvedValue(detected(['srd-5.1']));
  vi.mocked(client.ignoreCandidate).mockResolvedValue(detected(['srd-5.1']));
  vi.mocked(client.candidates).mockResolvedValue(pageOf([detected(['srd-5.1'])]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  renderPanel(vi.fn(), bothSource);
  await user.click(await screen.findByRole('button', { name: 'Testy Coat' }));
  const name = await screen.findByLabelText('Name');
  const buttons = ['Accept as a draft', 'Accept as reference', 'Ignore'].map((label) => screen.getByRole('button', { name: label }));
  await waitFor(() => expect(held(buttons[0]!)).toBe(false));
  await user.type(name, ' II');
  const reason = await screen.findByText('Save or undo your edit first.');

  for (const button of buttons) {
    expect(held(button)).toBe(true);
    expect(button).toHaveProperty('disabled', false);
    expect(button.getAttribute('aria-describedby')?.split(' ')).toContain(reason.id);
    button.focus();
    await user.click(button);
    expect(document.activeElement).toBe(button);
  }
  expect(client.acceptCandidate).not.toHaveBeenCalled();
  expect(client.ignoreCandidate).not.toHaveBeenCalled();
  expect((name as HTMLInputElement).value).toBe('Testy Coat II');

  // Undo: the same buttons work again.
  await user.type(name, '{Backspace}{Backspace}{Backspace}');
  for (const button of buttons) expect(held(button)).toBe(false);
  await user.click(buttons[2]!);
  await waitFor(() => expect(client.ignoreCandidate).toHaveBeenCalledExactlyOnceWith('cand-1', 'etag-1'));
});

// D64 (Import Lab, task 9): the selected effect's span, highlighted on the stored page text.
beforeEach(() => vi.mocked(client.importedPage).mockReset());
const span = { page: 4, blockStart: 1, blockEnd: 2, patternId: 'fixture.pattern', confidence: 0.9 };
const spanned = (over: Partial<DraftCandidate>): StoredCandidate => {
  const base = detected(['srd-5.1']);
  return {
    ...base,
    candidate: { ...base.candidate, excerpt: 'Whole excerpt of the fixture', proposedEffects: [{ id: 'eff-1', type: 'note' }], effectSpans: { 'eff-1': span }, ...over },
  };
};
const storedPage = (texts: string[]) => ({
  page: 4,
  text: texts.join('\n'),
  fromOcr: false,
  width: 600,
  height: 800,
  blocks: texts.map((text) => ({ text, x: 0, y: 0, width: 10, height: 10, fontSize: 10, bold: false })),
  warnings: [],
});
async function openSpanned(candidate: StoredCandidate) {
  vi.mocked(client.candidates).mockResolvedValue(pageOf([candidate]));
  vi.mocked(client.checkCandidate).mockResolvedValue(checkOf({}));
  renderPanel();
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'Testy Coat' }));
  return user;
}

it('selecting an effect highlights its span', async () => {
  vi.mocked(client.importedPage).mockResolvedValue(storedPage(['First block', 'Second block', 'Third block\nwith two lines', 'Fourth block']));
  const user = await openSpanned(spanned({}));
  await user.click(await screen.findByRole('button', { name: 'The effect eff-1' }));
  expect(client.importedPage).toHaveBeenCalledWith('src-2', 4, undefined);
  const region = await screen.findByRole('region', { name: 'The effect eff-1' });
  expect(within(region).getByText('Lines 2–4')).toBeTruthy();
  const marks = region.querySelectorAll('mark');
  expect(Array.from(marks).map((m) => m.textContent)).toEqual(['Second block', 'Third block\nwith two lines']);
  expect(region.textContent).toContain('First block');
  expect(region.textContent).toContain('Fourth block');
  expect(region.getAttribute('tabindex')).toBe('0'); // the scrollable text is reachable by keyboard
});

it('candidate without spans shows the whole excerpt', async () => {
  await openSpanned(spanned({ effectSpans: undefined }));
  expect(await screen.findByText('Whole excerpt of the fixture')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'The effect eff-1' })).toBeNull();
  expect(client.importedPage).not.toHaveBeenCalled();
});

async function settled() {
  await act(async () => {});
}

it('shows the whole excerpt, with no highlight, when the page cannot be fetched', async () => {
  const unhandled: unknown[] = [];
  const record = (reason: unknown) => unhandled.push(reason);
  process.on('unhandledRejection', record);
  vi.mocked(client.importedPage).mockRejectedValueOnce(new Error('no page'));
  const user = await openSpanned(spanned({}));
  await user.click(await screen.findByRole('button', { name: 'The effect eff-1' }));
  await waitFor(() => expect(client.importedPage).toHaveBeenCalledTimes(1));
  await settled();
  await new Promise((resolve) => setTimeout(resolve, 20)); // Node reports an unhandled rejection after the microtask queue drains
  process.off('unhandledRejection', record);
  expect(unhandled).toEqual([]); // the failed read is caught, not left rejected
  expect(unhandled.length).toBe(0);
  expect(screen.getByText('Whole excerpt of the fixture')).toBeTruthy();
  expect(document.querySelector('mark')).toBeNull();
  expect(screen.queryByRole('region', { name: 'The effect eff-1' })).toBeNull();
});

it('shows the whole excerpt, with no highlight, when the span does not fit the fetched page', async () => {
  vi.mocked(client.importedPage).mockResolvedValue(storedPage(['Only block']));
  const user = await openSpanned(spanned({}));
  await user.click(await screen.findByRole('button', { name: 'The effect eff-1' }));
  await waitFor(() => expect(client.importedPage).toHaveBeenCalledTimes(1));
  await settled();
  expect(screen.getByText('Whole excerpt of the fixture')).toBeTruthy();
  expect(document.querySelector('mark')).toBeNull();
  expect(screen.queryByRole('region', { name: 'The effect eff-1' })).toBeNull();
});

it('lists no effects and fetches nothing when the effect with the span was edited away', async () => {
  await openSpanned(spanned({ proposedEffects: [{ id: 'eff-2', type: 'note' }] }));
  await screen.findByText('Whole excerpt of the fixture');
  await settled();
  expect(screen.queryByRole('list', { name: /^Proposed effects/ })).toBeNull();
  expect(screen.queryByRole('button', { name: 'The effect eff-1' })).toBeNull();
  expect(client.importedPage).not.toHaveBeenCalled();
});

it('reads the page of the PDF the candidate was detected on, by its stored hash', async () => {
  vi.mocked(client.importedPage).mockResolvedValue(storedPage(['First block', 'Second block', 'Third block']));
  const stored = { ...spanned({}), attachmentSha256: 'ab'.repeat(32) };
  const user = await openSpanned(stored);
  await user.click(await screen.findByRole('button', { name: 'The effect eff-1' }));
  await screen.findByRole('region', { name: 'The effect eff-1' });
  expect(client.importedPage).toHaveBeenCalledWith('src-2', 4, 'ab'.repeat(32));
});

it('puts each confirm tick beside its effect button in one group', async () => {
  vi.mocked(client.importedPage).mockResolvedValue(storedPage(['First block', 'Second block', 'Third block']));
  const user = await openSpanned(spanned({ proposedKind: 'class', proposedEffects: [{ id: 'hd', type: 'hitDie', die: 8 }], effectSpans: { hd: span } }));
  const group = await screen.findByRole('group', { name: 'Confirm what was read' });
  expect(within(group).getByRole('checkbox', { name: 'Confirm the hit die, d8' })).toBeTruthy();
  await user.click(within(group).getByRole('button', { name: 'The hit die, d8' }));
  expect(await screen.findByRole('region', { name: 'The hit die, d8' })).toBeTruthy();
});

it('page text is rendered as text, not HTML', async () => {
  const hostile = '<img src=x onerror=alert(1)>';
  vi.mocked(client.importedPage).mockResolvedValue(storedPage(['First block', hostile, 'Third block']));
  const user = await openSpanned(spanned({ excerpt: hostile }));
  expect(document.querySelector('img')).toBeNull();
  expect(screen.getByText(hostile)).toBeTruthy();
  await user.click(await screen.findByRole('button', { name: 'The effect eff-1' }));
  const region = await screen.findByRole('region', { name: 'The effect eff-1' });
  expect(region.querySelector('img')).toBeNull();
  expect(document.querySelector('img')).toBeNull();
  expect(region.querySelector('mark')!.textContent).toContain(hostile);
});

// Import Lab L6: a pending candidate that looks like content in the library says so, in words; nothing is merged.
it('shows "Possible duplicate of {name} ({source})" beside a candidate that has a twin, and none beside one that has not', async () => {
  const twin = detected(['srd-5.1']);
  const alone = { ...twin, id: 'cand-2', candidate: { ...twin.candidate, id: 'cand-2', proposedName: 'Testy Lone Coat' } };
  vi.mocked(client.candidates).mockResolvedValue(pageOf([twin, alone]));
  vi.mocked(client.contentDuplicates).mockResolvedValue([
    {
      class: 'sameNameDifferentRules',
      family: 'srd-5.1',
      members: [
        { candidateId: 'cand-1', kind: 'item', name: 'Testy Coat', sourceId: 'src-2', sourceName: 'Testy Both' },
        { contentId: 'c-9', revisionId: 'r-9', kind: 'item', name: 'Testy Coat', sourceId: 'src-9', sourceName: 'Testy Library' },
      ],
    },
  ]);
  renderPanel(vi.fn(), bothSource);
  const rows = await screen.findAllByRole('listitem');
  const first = rows.find((r) => within(r).queryByRole('button', { name: 'Testy Coat' }))!;
  const second = rows.find((r) => within(r).queryByRole('button', { name: 'Testy Lone Coat' }))!;
  expect(await within(first).findByText('Possible duplicate of Testy Coat (Testy Library)')).toBeTruthy();
  expect(within(second).queryByText(/Possible duplicate/)).toBeNull();
  expect(client.contentDuplicates).toHaveBeenCalledWith({ sourceId: undefined, jobId: 'job-1' });
});

it('shows no chip, and no error, when the duplicates cannot be read', async () => {
  vi.mocked(client.candidates).mockResolvedValue(pageOf([detected(['srd-5.1'])]));
  vi.mocked(client.contentDuplicates).mockRejectedValue(new Error('unavailable'));
  const onError = vi.fn();
  render(<CandidateReviewPanel job={job} source={bothSource} onError={onError} onStatus={() => {}} onOpenPage={() => {}} onOpenStudio={() => {}} />);
  await screen.findByRole('button', { name: 'Testy Coat' });
  await waitFor(() => expect(client.contentDuplicates).toHaveBeenCalled());
  expect(screen.queryByText(/Possible duplicate/)).toBeNull();
  expect(onError).not.toHaveBeenCalled();
});
