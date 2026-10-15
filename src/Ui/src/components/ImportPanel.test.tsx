// @vitest-environment jsdom
// Item 3: a two-family source asks what the book is written for before it is read. Item 2: a finished job offers the studio.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { ImportJob, SourceRecord } from '../api/types';
import { ImportPanel } from './ImportPanel';

vi.mock('../api/client', () => ({ client: { listImports: vi.fn(), startImport: vi.fn() } }));

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

async function renderPanel(forSource: SourceRecord) {
  const onStatus = vi.fn();
  render(<ImportPanel source={forSource} onError={() => {}} onStatus={onStatus} onOpenStudio={() => {}} />);
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
      <ImportPanel source={both} onError={() => {}} onStatus={() => {}} onOpenStudio={() => {}} />
      <ImportPanel source={other} onError={() => {}} onStatus={() => {}} onOpenStudio={() => {}} />
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

it('offers the studio from a finished job that found candidates, and not from one that found none', async () => {
  vi.mocked(client.listImports).mockResolvedValue([completed('job-a', 3), completed('job-b', 0)]);
  await renderPanel(both);
  const [withCandidates, withNone] = await screen.findAllByRole('listitem');
  expect(within(withCandidates!).getByRole('button', { name: 'Open Testy Both in the studio' })).toBeTruthy();
  expect(within(withNone!).queryByRole('button', { name: 'Open Testy Both in the studio' })).toBeNull();
});
