// @vitest-environment jsdom
// Item 2: the review screen says what happens to an accepted entry and offers the way to the studio.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CandidateCheck, DraftCandidate, ImportJob, SourceRecord, StoredCandidate } from '../api/types';
import { CandidateReviewPanel } from './CandidateReviewPanel';

vi.mock('../api/client', () => ({ client: { candidates: vi.fn(), checkCandidate: vi.fn(), editCandidate: vi.fn(), acceptCandidate: vi.fn() } }));

const source = { id: 'src-1', title: 'Testy Tome', rulesFamilies: ['srd-5.1'] } as SourceRecord;
const bothSource = { id: 'src-2', title: 'Testy Both', rulesFamilies: ['srd-5.1', 'srd-5.2.1'] } as SourceRecord;
const job = { id: 'job-1', sourceId: 'src-1' } as ImportJob;

const pageOf = (items: StoredCandidate[], total = items.length) => ({ items, total });

beforeEach(() => vi.mocked(client.candidates).mockResolvedValue(pageOf([])));
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
  return { ...base, id: `cand-${n}`, candidate: { ...base.candidate, id: `cand-${n}`, proposedName: `Testy Item ${n}`, page: { start: 1 + Math.floor(n / 10) } } };
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
