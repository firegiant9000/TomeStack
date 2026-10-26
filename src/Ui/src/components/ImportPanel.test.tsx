// @vitest-environment jsdom
// Item 3: a two-family source asks what the book is written for before it is read. Item 2: a finished job offers the studio.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { ImportJob, SourceRecord, StoredPage } from '../api/types';
import { ImportPanel } from './ImportPanel';

vi.mock('../api/client', () => ({
  client: {
    listImports: vi.fn(),
    startImport: vi.fn(),
    resumeImport: vi.fn(),
    cancelImport: vi.fn(),
    candidates: vi.fn(),
    startMarkdownImport: vi.fn(),
    startMarkdownImportData: vi.fn(),
    importedPage: vi.fn(),
    openPage: vi.fn(),
    searchImport: vi.fn(),
  },
}));

const both = { id: 'src-2', title: 'Testy Both', rulesFamilies: ['srd-5.1', 'srd-5.2.1'] } as SourceRecord;
const single = { id: 'src-1', title: 'Testy Tome', rulesFamilies: ['srd-5.1'] } as SourceRecord;
const completed = (id: string, candidates: number) =>
  ({ id, sourceId: 'src-2', firstPage: 1, wholeDocument: true, status: 'completed', pagesDone: 6, pagesFailed: 0, pagesFromOcr: 0, pagesWithoutText: 0, candidates }) as ImportJob;

beforeEach(() => {
  vi.mocked(client.listImports).mockResolvedValue([]);
  vi.mocked(client.startImport).mockResolvedValue({} as ImportJob);
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

async function renderPanel(forSource: SourceRecord, options: { hasPdf?: boolean; onError?: (error: unknown) => void } = {}) {
  const onStatus = vi.fn();
  render(<ImportPanel source={forSource} hasPdf={options.hasPdf ?? true} onError={options.onError ?? (() => {})} onStatus={onStatus} onOpenStudio={() => {}} />);
  await vi.waitFor(() => expect(client.listImports).toHaveBeenCalled());
  return userEvent.setup();
}

it('asks what a two-family book is written for, with no default', async () => {
  await renderPanel(both);
  const group = screen.getByRole('group', { name: 'This book is written for' });
  const radios = within(group).getAllByRole('radio');
  expect(radios.map((r) => (r as HTMLInputElement).labels?.[0]?.textContent?.trim())).toEqual(['2014 rules (SRD 5.1)', '2024 rules (SRD 5.2.1)', 'Both']);
  expect(radios.map((r) => (r as HTMLInputElement).checked)).toEqual([false, false, false]);
});

it('holds the Read buttons, with a linked reason, until it is answered', async () => {
  const user = await renderPanel(both);
  const whole = screen.getByRole('button', { name: 'Read the whole document' });
  expect(whole.getAttribute('aria-disabled')).toBe('true');
  expect(whole).toHaveProperty('disabled', false); // reachable by keyboard, never `disabled`
  const reason = document.getElementById(whole.getAttribute('aria-describedby') ?? '');
  expect(reason?.textContent).toBe('Say what the book is written for.');

  await user.click(whole);
  expect(client.startImport).not.toHaveBeenCalled();

  await user.click(screen.getByRole('radio', { name: '2014 rules (SRD 5.1)' }));
  expect(whole.getAttribute('aria-disabled')).toBeNull();
  expect(screen.queryByText('Say what the book is written for.')).toBeNull();
});

it('links each panel to its own reason when two sources are listed', async () => {
  const other = { ...both, id: 'src-3', title: 'Testy Other' } as SourceRecord;
  render(
    <>
      <ImportPanel source={both} hasPdf onError={() => {}} onStatus={() => {}} onOpenStudio={() => {}} />
      <ImportPanel source={other} hasPdf onError={() => {}} onStatus={() => {}} onOpenStudio={() => {}} />
    </>,
  );
  await vi.waitFor(() => expect(client.listImports).toHaveBeenCalledTimes(2));
  for (const title of ['Testy Both', 'Testy Other']) {
    const panel = screen.getByRole('region', { name: `Read the text of ${title}` });
    for (const name of ['Read the whole document', 'Read these pages']) {
      const id = within(panel).getByRole('button', { name }).getAttribute('aria-describedby') ?? '';
      const reasons = document.querySelectorAll(`[id="${id}"]`);
      expect(reasons).toHaveLength(1);
      expect(panel.contains(reasons[0] ?? null)).toBe(true);
      expect(reasons[0]?.textContent).toBe('Say what the book is written for.');
    }
  }
});

it('sends the answer with the read', async () => {
  const user = await renderPanel(both);
  await user.click(screen.getByRole('radio', { name: '2014 rules (SRD 5.1)' }));
  await user.click(screen.getByRole('button', { name: 'Read the whole document' }));
  expect(client.startImport).toHaveBeenLastCalledWith('src-2', { wholeDocument: true, writtenFor: ['srd-5.1'] });

  await user.click(screen.getByRole('radio', { name: 'Both' }));
  await user.click(screen.getByRole('button', { name: 'Read the whole document' }));
  expect(client.startImport).toHaveBeenLastCalledWith('src-2', { wholeDocument: true, writtenFor: ['srd-5.1', 'srd-5.2.1'] });
});

it('holds Read these pages too, and sends the answer with the range', async () => {
  const user = await renderPanel(both);
  await user.type(screen.getByLabelText('From page'), '2');
  const pages = screen.getByRole('button', { name: 'Read these pages' });
  expect(pages.getAttribute('aria-disabled')).toBe('true');
  await user.click(pages);
  expect(client.startImport).not.toHaveBeenCalled();

  await user.click(screen.getByRole('radio', { name: '2024 rules (SRD 5.2.1)' }));
  await user.click(pages);
  expect(client.startImport).toHaveBeenCalledWith('src-2', { firstPage: 2, lastPage: 2, writtenFor: ['srd-5.2.1'] });
});

it('says a one-family source is written for that family, with no question and no writtenFor', async () => {
  const user = await renderPanel(single);
  expect(screen.getByText('Written for SRD 5.1 (from the source).')).toBeTruthy();
  expect(screen.queryByRole('radio')).toBeNull();
  const whole = screen.getByRole('button', { name: 'Read the whole document' });
  expect(whole.getAttribute('aria-disabled')).toBeNull();
  await user.click(whole);
  expect(client.startImport).toHaveBeenCalledWith('src-1', { wholeDocument: true });
});

const running = (id: string) => ({ ...completed(id, 0), status: 'running' }) as ImportJob;
const cancelled = (id: string) => ({ ...completed(id, 0), status: 'cancelled' }) as ImportJob;

it('keeps focus on the pressed Read button when the job starts, and refuses a press while it runs (2.4.3)', async () => {
  const user = await renderPanel(single);
  const whole = screen.getByRole('button', { name: 'Read the whole document' });
  vi.mocked(client.listImports).mockResolvedValue([running('job-a')]);
  await user.click(whole);
  await vi.waitFor(() => expect(whole.getAttribute('aria-disabled')).toBe('true'));
  expect(whole).toHaveProperty('disabled', false);
  expect(document.activeElement).toBe(whole);
  const reason = document.getElementById(whole.getAttribute('aria-describedby') ?? '');
  expect(reason?.textContent).toBe('Reading… — cancel it first or wait.');

  await user.click(whole);
  expect(client.startImport).toHaveBeenCalledTimes(1);
});

it('holds Read these pages without a first page, with a linked reason, and never natively disables it', async () => {
  const user = await renderPanel(single);
  const pages = screen.getByRole('button', { name: 'Read these pages' });
  expect(pages.getAttribute('aria-disabled')).toBe('true');
  expect(pages).toHaveProperty('disabled', false);
  expect(document.getElementById(pages.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Enter a first page.');
  await user.click(pages);
  expect(client.startImport).not.toHaveBeenCalled();
  await user.type(screen.getByLabelText('From page'), '3');
  expect(pages.getAttribute('aria-disabled')).toBeNull();
});

it('moves focus to the job line when Resume is pressed and the button goes away (2.4.3)', async () => {
  vi.mocked(client.listImports).mockResolvedValue([cancelled('job-a')]);
  vi.mocked(client.resumeImport).mockResolvedValue({} as ImportJob);
  const user = await renderPanel(single);
  const resume = await screen.findByRole('button', { name: 'Resume' });
  vi.mocked(client.listImports).mockResolvedValue([running('job-a')]);
  await user.click(resume);
  await vi.waitFor(() => expect(screen.queryByRole('button', { name: 'Resume' })).toBeNull());
  expect(client.resumeImport).toHaveBeenCalledWith('job-a');
  expect(document.activeElement?.id).toBe('import-progress-job-a');
});

it('holds Resume while another job runs: aria-disabled, a linked reason, and the press does nothing', async () => {
  vi.mocked(client.listImports).mockResolvedValue([running('job-a'), cancelled('job-b')]);
  const user = await renderPanel(single);
  const resume = await screen.findByRole('button', { name: 'Resume' });
  expect(resume.getAttribute('aria-disabled')).toBe('true');
  expect(resume).toHaveProperty('disabled', false);
  expect(document.getElementById(resume.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Reading… — cancel it first or wait.');
  await user.click(resume);
  expect(client.resumeImport).not.toHaveBeenCalled();
});

it('shows one studio button while a job is under review: the panel\'s own, not also the job line\'s', async () => {
  vi.mocked(client.listImports).mockResolvedValue([completed('job-a', 3)]);
  vi.mocked(client.candidates).mockResolvedValue({ items: [], total: 0 });
  const user = await renderPanel(both);
  expect(await screen.findAllByRole('button', { name: 'Open Testy Both in the studio' })).toHaveLength(1);

  await user.click(screen.getByRole('button', { name: 'Review 3 candidates' }));
  const region = await screen.findByRole('region', { name: 'Candidates from Testy Both' });
  const buttons = screen.getAllByRole('button', { name: 'Open Testy Both in the studio' });
  expect(buttons).toHaveLength(1);
  expect(region.contains(buttons[0] ?? null)).toBe(true);

  await user.click(screen.getByRole('button', { name: 'Review 3 candidates' })); // closing the review brings the job line's back
  expect(screen.getAllByRole('button', { name: 'Open Testy Both in the studio' })).toHaveLength(1);
  expect(screen.queryByRole('region', { name: 'Candidates from Testy Both' })).toBeNull();
});

it('hides every job line\'s studio button while any review is open, so no unsaved edit can be left behind', async () => {
  vi.mocked(client.listImports).mockResolvedValue([completed('job-a', 3), completed('job-b', 2)]);
  vi.mocked(client.candidates).mockResolvedValue({ items: [], total: 0 });
  const user = await renderPanel(both);
  expect(await screen.findAllByRole('button', { name: 'Open Testy Both in the studio' })).toHaveLength(2);

  await user.click(screen.getByRole('button', { name: 'Review 3 candidates' }));
  const region = await screen.findByRole('region', { name: 'Candidates from Testy Both' });
  const buttons = screen.getAllByRole('button', { name: 'Open Testy Both in the studio' });
  expect(buttons).toHaveLength(1);
  expect(region.contains(buttons[0] ?? null)).toBe(true);

  await user.click(screen.getByRole('button', { name: 'Review 3 candidates' }));
  expect(screen.getAllByRole('button', { name: 'Open Testy Both in the studio' })).toHaveLength(2);
});

it.each(['0', '-2', '1.5'])('says the first page is 1 or more for From page "%s", not the last-page text', async (value) => {
  const user = await renderPanel(single);
  await user.type(screen.getByLabelText('From page'), value);
  const pages = screen.getByRole('button', { name: 'Read these pages' });
  expect(pages.getAttribute('aria-disabled')).toBe('true');
  expect(document.getElementById(pages.getAttribute('aria-describedby') ?? '')?.textContent).toBe('The first page is 1 or more.');
});

it('keeps the last-page text for a last page before a valid first page', async () => {
  const user = await renderPanel(single);
  await user.type(screen.getByLabelText('From page'), '5');
  await user.type(screen.getByLabelText('To page (optional)'), '3');
  const pages = screen.getByRole('button', { name: 'Read these pages' });
  expect(document.getElementById(pages.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Check the pages: the last page cannot be before the first.');
});

it('offers the studio from a finished job that found candidates, and not from one that found none', async () => {
  vi.mocked(client.listImports).mockResolvedValue([completed('job-a', 3), completed('job-b', 0)]);
  await renderPanel(both);
  const [withCandidates, withNone] = await screen.findAllByRole('listitem');
  expect(within(withCandidates!).getByRole('button', { name: 'Open Testy Both in the studio' })).toBeTruthy();
  expect(within(withNone!).queryByRole('button', { name: 'Open Testy Both in the studio' })).toBeNull();
});

// ---- Import Lab Task 13 (D67): Markdown import, pages only ----

const markdownJob = (id: string, status: ImportJob['status'], extra: Partial<ImportJob> = {}) =>
  ({ ...completed(id, 2), sourceId: 'src-1', sha256: 'abc123', format: 'markdown', status, ...extra }) as ImportJob;

it('offers a Markdown import with no PDF attached, and no PDF read buttons', async () => {
  const user = await renderPanel(single, { hasPdf: false });
  expect(screen.queryByRole('button', { name: 'Read the whole document' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Read these pages' })).toBeNull();
  const markdown = screen.getByRole('button', { name: 'Import a Markdown file…' });
  expect(markdown.getAttribute('aria-disabled')).toBeNull();

  vi.mocked(client.startMarkdownImport).mockResolvedValue(markdownJob('job-m', 'running'));
  await user.click(markdown);
  expect(client.startMarkdownImport).toHaveBeenCalledWith('src-1', undefined);
  await vi.waitFor(() => expect(client.listImports).toHaveBeenCalledTimes(2)); // the list is read again
});

it('holds the Markdown import until a two-family book is answered, and sends the answer with it', async () => {
  const user = await renderPanel(both, { hasPdf: false });
  const markdown = screen.getByRole('button', { name: 'Import a Markdown file…' });
  expect(markdown.getAttribute('aria-disabled')).toBe('true');
  expect(markdown).toHaveProperty('disabled', false);
  expect(document.getElementById(markdown.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Say what the book is written for.');
  await user.click(markdown);
  expect(client.startMarkdownImport).not.toHaveBeenCalled();

  vi.mocked(client.startMarkdownImport).mockResolvedValue(undefined);
  await user.click(screen.getByRole('radio', { name: 'Both' }));
  await user.click(markdown);
  expect(client.startMarkdownImport).toHaveBeenCalledWith('src-2', ['srd-5.1', 'srd-5.2.1']);
});

it('does nothing more when the Open dialog is cancelled', async () => {
  vi.mocked(client.startMarkdownImport).mockResolvedValue(undefined);
  const onStatus = vi.fn();
  render(<ImportPanel source={single} hasPdf={false} onError={() => {}} onStatus={onStatus} onOpenStudio={() => {}} />);
  await vi.waitFor(() => expect(client.listImports).toHaveBeenCalled());
  await userEvent.setup().click(screen.getByRole('button', { name: 'Import a Markdown file…' }));
  await vi.waitFor(() => expect(client.startMarkdownImport).toHaveBeenCalled());
  expect(onStatus).not.toHaveBeenCalled();
});

it('falls back to a file picker in the browser and sends the bytes, even if the file comes before the host replied', async () => {
  const user = await renderPanel(single, { hasPdf: false });
  let refuse: (error: unknown) => void = () => {};
  vi.mocked(client.startMarkdownImport).mockReturnValue(new Promise((_, reject) => (refuse = reject)));
  vi.mocked(client.startMarkdownImportData).mockResolvedValue(markdownJob('job-m', 'running'));

  await user.click(screen.getByRole('button', { name: 'Import a Markdown file…' }));
  const input = screen.getByLabelText('Markdown file') as HTMLInputElement;
  expect(input.getAttribute('accept')).toBe('.md');
  await user.upload(input, new File(['# Placeholder Class\n'], 'class.md', { type: 'text/markdown' })); // before the "unsupported" reply
  await vi.waitFor(() => expect(client.startMarkdownImportData).toHaveBeenCalledWith('src-1', 'class.md', btoa('# Placeholder Class\n'), undefined));
  refuse(new TomeStackError({ code: 'unsupported', message: 'No dialog.' }));
});

it('opens the browser picker when the host has no dialog', async () => {
  const user = await renderPanel(single, { hasPdf: false });
  vi.mocked(client.startMarkdownImport).mockRejectedValue(new TomeStackError({ code: 'unsupported', message: 'No dialog.' }));
  const input = screen.getByLabelText('Markdown file') as HTMLInputElement;
  const clicked = vi.spyOn(input, 'click');
  await user.click(screen.getByRole('button', { name: 'Import a Markdown file…' }));
  await vi.waitFor(() => expect(clicked).toHaveBeenCalled());
});

it('shows no Resume on a Markdown job in any resumable state, and says to import the file again; a PDF job still resumes', async () => {
  vi.mocked(client.listImports).mockResolvedValue([
    markdownJob('job-a', 'failed', { failureCode: 'markdown.encoding', failureMessage: 'server words' }),
    markdownJob('job-b', 'cancelled'),
    markdownJob('job-c', 'interrupted'),
    { ...completed('job-d', 0), status: 'cancelled' } as ImportJob,
  ]);
  await renderPanel(single);
  const items = await screen.findAllByRole('listitem');
  expect(screen.getAllByRole('button', { name: 'Resume' })).toHaveLength(1);
  for (const item of items.slice(0, 3)) {
    expect(within(item).queryByRole('button', { name: 'Resume' })).toBeNull();
    expect(item.textContent).toContain('Markdown file');
    expect(item.textContent).toContain('Import the Markdown file again.');
  }
  expect(within(items[3]!).getByRole('button', { name: 'Resume' })).toBeTruthy();
});

it('says a failed Markdown job in words, and falls back to the server message for a code it does not know', async () => {
  vi.mocked(client.listImports).mockResolvedValue([
    markdownJob('job-a', 'failed', { failureCode: 'markdown.encoding', failureMessage: 'server words' }),
    markdownJob('job-b', 'failed', { failureCode: 'worker.crashed', failureMessage: 'The import worker stopped unexpectedly.' }),
  ]);
  await renderPanel(single);
  const [known, unknown] = await screen.findAllByRole('listitem');
  expect(known!.textContent).toContain('The file is not valid UTF-8 text. Save it as UTF-8, then import it again.');
  expect(known!.textContent).not.toContain('server words');
  expect(unknown!.textContent).toContain('The import worker stopped unexpectedly.');
});

it('says a refused start in words, and passes an unknown refusal on as the server said it', async () => {
  const onError = vi.fn();
  const user = await renderPanel(single, { hasPdf: false, onError });
  vi.mocked(client.startMarkdownImport).mockRejectedValue(
    new TomeStackError({ code: 'validation', message: 'server words', diagnostics: [{ code: 'markdown.too-large', message: 'server words' }] }),
  );
  await user.click(screen.getByRole('button', { name: 'Import a Markdown file…' }));
  await vi.waitFor(() => expect(onError).toHaveBeenCalled());
  expect((onError.mock.calls[0]![0] as Error).message).toBe('The Markdown file is too large to import: over 4 MB, or too many lines, pages or table columns.');

  onError.mockClear();
  vi.mocked(client.startMarkdownImport).mockRejectedValue(
    new TomeStackError({ code: 'validation', message: 'Another import is running.', diagnostics: [{ code: 'import.busy', message: 'Another import is running.' }] }),
  );
  await user.click(screen.getByRole('button', { name: 'Import a Markdown file…' }));
  await vi.waitFor(() => expect(onError).toHaveBeenCalled());
  expect(onError.mock.calls[0]![0]).toBeInstanceOf(TomeStackError);
});

const storedPage = (text: string, warnings: string[] = []): StoredPage => ({ page: 2, text, fromOcr: false, width: 612, height: 792, blocks: [], warnings });

async function searchAndOpen(user: Awaited<ReturnType<typeof renderPanel>>, page: number) {
  await user.type(screen.getByLabelText('Search the text of Testy Tome'), 'snip');
  await user.click(screen.getByRole('button', { name: 'Search' }));
  await user.click(await screen.findByRole('button', { name: `Open page ${page}` }));
}

it('shows a search hit of a source with no PDF as stored text in the app, not in the PDF viewer', async () => {
  vi.mocked(client.searchImport).mockResolvedValue([{ page: 2, snippet: 'a snippet' }]);
  vi.mocked(client.importedPage).mockResolvedValue(storedPage('Line one\nLine two', ['markdown.page-order', 'page.text-truncated', 'page.no-text', 'page.odd-new-code']));
  const user = await renderPanel(single, { hasPdf: false });

  await searchAndOpen(user, 2);

  expect(client.openPage).not.toHaveBeenCalled();
  expect(client.importedPage).toHaveBeenCalledWith('src-1', 2, undefined);
  const region = await screen.findByRole('region', { name: 'Stored text of page 2 of Testy Tome' });
  expect(region.textContent).toContain('Line one\nLine two');
  expect(within(region).getByText('Page markers must go up; one was ignored.')).toBeTruthy();
  expect(within(region).getByText('This page was longer than TomeStack keeps, so the end of it was left out.')).toBeTruthy();
  expect(within(region).getByText('This page has no text.')).toBeTruthy();
  expect(within(region).getByText('page.odd-new-code')).toBeTruthy(); // an unknown warning is shown as it is
  await vi.waitFor(() => expect(document.activeElement).toBe(region));

  await userEvent.setup().click(within(region).getByRole('button', { name: 'Close' }));
  expect(screen.queryByRole('region', { name: 'Stored text of page 2 of Testy Tome' })).toBeNull();
});

it('shows stored page text as plain text: markup-looking text is shown literally and creates no element', async () => {
  const hostile = '<b>bold</b> <script>window.hacked = true</script> <img src=x onerror="window.hacked = true"> &amp;';
  vi.mocked(client.searchImport).mockResolvedValue([{ page: 2, snippet: 'x' }]);
  vi.mocked(client.importedPage).mockResolvedValue(storedPage(hostile));
  const user = await renderPanel(single, { hasPdf: false });

  await searchAndOpen(user, 2);

  const region = await screen.findByRole('region', { name: 'Stored text of page 2 of Testy Tome' });
  expect(region.textContent).toContain(hostile);
  expect(region.querySelector('b, script, img')).toBeNull();
  expect((window as unknown as { hacked?: boolean }).hacked).toBeUndefined();
});

it('still opens a search hit in the PDF viewer when the source has a PDF', async () => {
  vi.mocked(client.searchImport).mockResolvedValue([{ page: 4, snippet: 'a snippet' }]);
  vi.mocked(client.openPage).mockResolvedValue({ opened: true, page: 4, warnings: [] });
  const user = await renderPanel(single, { hasPdf: true });

  await searchAndOpen(user, 4);

  expect(client.openPage).toHaveBeenCalledWith('src-1', 4);
  expect(client.importedPage).not.toHaveBeenCalled();
});
