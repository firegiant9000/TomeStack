// @vitest-environment jsdom
// Item 2: the review screen says what happens to an accepted entry and offers the way to the studio.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CandidateCheck, DraftCandidate, ImportJob, SourceRecord, StoredCandidate } from '../api/types';
import { CandidateReviewPanel } from './CandidateReviewPanel';

vi.mock('../api/client', () => ({ client: { candidates: vi.fn(), checkCandidate: vi.fn(), editCandidate: vi.fn() } }));

const source = { id: 'src-1', title: 'Testy Tome', rulesFamilies: ['srd-5.1'] } as SourceRecord;
const bothSource = { id: 'src-2', title: 'Testy Both', rulesFamilies: ['srd-5.1', 'srd-5.2.1'] } as SourceRecord;
const job = { id: 'job-1', sourceId: 'src-1' } as ImportJob;

beforeEach(() => vi.mocked(client.candidates).mockResolvedValue([]));
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
  vi.mocked(client.candidates).mockResolvedValue([detected(rulesFamilies)]);
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
  vi.mocked(client.candidates).mockResolvedValue([detected(['srd-5.1'])]);
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
  expect(screen.getByRole('button', { name: 'Accept as a draft' })).toHaveProperty('disabled', true);
  await user.click(screen.getByRole('button', { name: 'Save edit' }));
  expect(client.editCandidate).toHaveBeenCalledWith('cand-1', expect.objectContaining({ rulesFamilies: [] }));
});
