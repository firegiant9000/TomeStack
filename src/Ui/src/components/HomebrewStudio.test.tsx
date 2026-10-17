// @vitest-environment jsdom
// Item 2: the studio can open on a given source (from the import review), and then names itself by taking focus.
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({ client: { listSources: vi.fn(), contentBySource: vi.fn() } }));

const info = { rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }] } as unknown as AppInfo;
const homebrew = (id: string, title: string) => ({ id, title, rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' }) as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([homebrew('a', 'Testy Alpha'), homebrew('b', 'Testy Beta')]);
  vi.mocked(client.contentBySource).mockResolvedValue([]);
});
afterEach(cleanup);

it('opens on the source it was asked for, and takes focus on its heading', async () => {
  render(<HomebrewStudio info={info} initialSourceId="b" onError={() => {}} onStatus={() => {}} />);
  const select = await screen.findByRole<HTMLSelectElement>('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(select.selectedOptions[0]?.textContent).toMatch(/^Testy Beta/));
  expect(document.activeElement).toBe(screen.getByRole('heading', { level: 2, name: 'Homebrew studio' }));
});

it('falls back to the first source when the asked-for one is not there', async () => {
  render(<HomebrewStudio info={info} initialSourceId="gone" onError={() => {}} onStatus={() => {}} />);
  const select = await screen.findByRole<HTMLSelectElement>('combobox', { name: 'Homebrew source' });
  expect(select.selectedOptions[0]?.textContent).toMatch(/^Testy Alpha/);
});

it('leaves focus alone when opened from the sidebar (the sidebar button keeps it)', async () => {
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await screen.findByRole('combobox', { name: 'Homebrew source' });
  expect(document.activeElement).toBe(document.body);
});
