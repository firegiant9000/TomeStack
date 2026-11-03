// @vitest-environment jsdom
// Import Lab Task 13 (D67): "Open page" from a Markdown job's candidate review shows the stored text in the app, read by the
// job's own hash, and never opens the PDF viewer.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { ImportJob, SourceRecord, StoredPage } from '../api/types';
import { ImportPanel } from './ImportPanel';

vi.mock('../api/client', () => ({
  client: { listImports: vi.fn(), importedPage: vi.fn(), openPage: vi.fn() },
}));

// The review panel is its own unit; here only the page-opening callback it is given matters.
vi.mock('./CandidateReviewPanel', () => ({
  CandidateReviewPanel: ({ onOpenPage }: { onOpenPage: (page: number) => void }) => (
    <button type="button" onClick={() => onOpenPage(3)}>
      Open page 3
    </button>
  ),
}));

const source = { id: 'src-1', title: 'Testy Tome', rulesFamilies: ['srd-5.1'] } as SourceRecord;
const job = { id: 'job-m', sourceId: 'src-1', sha256: 'abc123', format: 'markdown', firstPage: 1, wholeDocument: true, status: 'completed', pagesDone: 3, pagesFailed: 0, pagesFromOcr: 0, pagesWithoutText: 0, candidates: 2 } as ImportJob;

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

it("opens a Markdown job's candidate page in the app by the job's hash, even when the source has a PDF", async () => {
  vi.mocked(client.listImports).mockResolvedValue([job]);
  vi.mocked(client.importedPage).mockResolvedValue({ page: 3, text: 'Stored line', fromOcr: false, width: 612, height: 792, blocks: [], warnings: [] } as StoredPage);
  render(<ImportPanel source={source} hasPdf onError={() => {}} onStatus={() => {}} onOpenStudio={() => {}} />);
  const user = userEvent.setup();

  await user.click(await screen.findByRole('button', { name: 'Review 2 candidates' }));
  await user.click(await screen.findByRole('button', { name: 'Open page 3' }));

  expect(client.importedPage).toHaveBeenCalledWith('src-1', 3, 'abc123');
  expect(client.openPage).not.toHaveBeenCalled();
  const region = await screen.findByRole('region', { name: 'Stored text of page 3 of Testy Tome' });
  expect(within(region).getByText('Stored line')).toBeTruthy();
});
