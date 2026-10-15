// @vitest-environment jsdom
// Item 2: the review screen says what happens to an accepted entry and offers the way to the studio.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { ImportJob, SourceRecord } from '../api/types';
import { CandidateReviewPanel } from './CandidateReviewPanel';

vi.mock('../api/client', () => ({ client: { candidates: vi.fn() } }));

const source = { id: 'src-1', title: 'Testy Tome', rulesFamilies: ['srd-5.1'] } as SourceRecord;
const job = { id: 'job-1', sourceId: 'src-1' } as ImportJob;

beforeEach(() => vi.mocked(client.candidates).mockResolvedValue([]));
afterEach(cleanup);

function renderPanel(onOpenStudio = vi.fn()) {
  render(<CandidateReviewPanel job={job} source={source} onError={() => {}} onStatus={() => {}} onOpenPage={() => {}} onOpenStudio={onOpenStudio} />);
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
