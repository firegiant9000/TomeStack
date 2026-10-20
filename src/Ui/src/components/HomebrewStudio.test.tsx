// @vitest-environment jsdom
// Item 2: the studio can open on a given source (from the import review), and then names itself by taking focus.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: { listSources: vi.fn(), contentBySource: vi.fn(), publishManyPreview: vi.fn(), createHomebrewSource: vi.fn(), previewFormula: vi.fn(), listCharacters: vi.fn() },
}));

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

// Item 9: "Publish all drafts in this source…" opens the named region with the pending drafts of the source.
it('offers Publish all drafts only when a draft is pending, and opens the region named for the source', async () => {
  const user = userEvent.setup();
  const revision = (status: 'draft' | 'published', revisionId: string) => ({ contentId: 'c1', revisionId, status, effects: [] }) as unknown as StudioEntry['latest'];
  const entry = (name: string, status: 'draft' | 'published'): StudioEntry => ({
    contentId: `id-${name}`,
    name,
    kind: 'feat',
    revisions: [],
    latest: { ...revision(status, `r-${name}`), contentId: `id-${name}` },
  });
  vi.mocked(client.contentBySource).mockResolvedValue([entry('Testy Done', 'published')]);
  const view = render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await screen.findByText('Testy Done');
  expect(screen.queryByRole('button', { name: 'Publish all drafts in this source…' })).toBeNull();
  view.unmount();

  vi.mocked(client.contentBySource).mockResolvedValue([entry('Testy Done', 'published'), entry('Testy Waiting', 'draft')]);
  vi.mocked(client.publishManyPreview).mockResolvedValue({ entries: [], dependents: [], canPublish: true });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Publish all drafts in this source…' }));
  const region = screen.getByRole('region', { name: 'Publish drafts of Testy Alpha' });
  expect(document.activeElement).toBe(within(region).getByRole('heading', { name: 'Publish drafts of Testy Alpha' }));
  expect(within(region).getByRole('checkbox', { name: 'Publish Testy Waiting' })).toHaveProperty('checked', true);
  expect(within(region).queryByRole('checkbox', { name: 'Publish Testy Done' })).toBeNull();
});

// Fix round 1, item 13: another source never shows the open panel or the old source's rows.
it('closes the publish panel and drops the old rows when the source changes', async () => {
  const user = userEvent.setup();
  const draft = { contentId: 'id-w', name: 'Testy Waiting', kind: 'feat', revisions: [], latest: { contentId: 'id-w', revisionId: 'r-w', status: 'draft', effects: [] } } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [draft] : []));
  vi.mocked(client.publishManyPreview).mockResolvedValue({ entries: [], dependents: [], canPublish: true });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Publish all drafts in this source…' }));
  expect(screen.getByRole('region', { name: 'Publish drafts of Testy Alpha' })).toBeTruthy();

  // Hold the new source's rows back: until they arrive the old ones must not be shown.
  let release: (rows: StudioEntry[]) => void = () => {};
  vi.mocked(client.contentBySource).mockImplementation((id) => (id === 'b' ? new Promise((resolve) => (release = resolve)) : Promise.resolve([draft])));
  await user.selectOptions(screen.getByRole('combobox', { name: 'Homebrew source' }), 'b');
  expect(screen.queryByRole('region', { name: /^Publish drafts of/ })).toBeNull();
  expect(screen.queryByText('Testy Waiting')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Publish all drafts in this source…' })).toBeNull();
  release([]);
});

// Fix round 2, items 2 and 6.
const draftEntry = { contentId: 'id-w', name: 'Testy Waiting', kind: 'feat', revisions: [], latest: { contentId: 'id-w', revisionId: 'r-w', status: 'draft', effects: [] } } as unknown as StudioEntry;

it('creating a new source closes the open publish panel and drops the old rows', async () => {
  const user = userEvent.setup();
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [draftEntry] : []));
  vi.mocked(client.publishManyPreview).mockResolvedValue({ entries: [], dependents: [], canPublish: true });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Publish all drafts in this source…' }));
  expect(screen.getByRole('region', { name: 'Publish drafts of Testy Alpha' })).toBeTruthy();

  // The new source's rows are slow: until they arrive the old ones must not be shown.
  let release: (rows: StudioEntry[]) => void = () => {};
  vi.mocked(client.contentBySource).mockImplementation((id) => (id === 'c' ? new Promise((resolve) => (release = resolve)) : Promise.resolve([draftEntry])));
  vi.mocked(client.createHomebrewSource).mockResolvedValue(homebrew('c', 'Testy Gamma'));
  vi.mocked(client.listSources).mockResolvedValue([homebrew('a', 'Testy Alpha'), homebrew('b', 'Testy Beta'), homebrew('c', 'Testy Gamma')]);
  await user.type(screen.getByLabelText('Source title'), 'Testy Gamma');
  await user.click(screen.getByRole('checkbox', { name: 'SRD 5.1' }));
  await user.click(screen.getByRole('button', { name: 'Create source' }));

  await waitFor(() => expect(client.contentBySource).toHaveBeenCalledWith('c'));
  expect(screen.queryByRole('region', { name: /^Publish drafts of/ })).toBeNull();
  expect(screen.queryByText('Testy Waiting')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Publish all drafts in this source…' })).toBeNull();
  release([]);
});

it('ignores a slow reload of the old source that lands after a switch', async () => {
  const user = userEvent.setup();
  let releaseA: (rows: StudioEntry[]) => void = () => {};
  vi.mocked(client.contentBySource).mockImplementation((id) => (id === 'a' ? new Promise((resolve) => (releaseA = resolve)) : Promise.resolve([])));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await waitFor(() => expect(client.contentBySource).toHaveBeenCalledWith('a'));
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Homebrew source' }), 'b');
  await waitFor(() => expect(client.contentBySource).toHaveBeenCalledWith('b'));

  releaseA([draftEntry]); // A answers last
  await new Promise((resolve) => setTimeout(resolve, 50));

  expect(screen.queryByText('Testy Waiting')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Publish all drafts in this source…' })).toBeNull();
});

it('choosing the source that is already shown keeps its rows', async () => {
  const user = userEvent.setup();
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [draftEntry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await screen.findByText('Testy Waiting');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Homebrew source' }), 'a');
  expect(screen.getByText('Testy Waiting')).toBeTruthy();
});

// D61: every formula input shows its preview and is described by it.
it('a_resource_maximum_shows_its_preview_and_is_described_by_it', async () => {
  const user = userEvent.setup();
  const resource = { type: 'resource', id: 'resource-1', resourceId: 'resource-1', label: 'Testy Pool', maximum: '1' };
  const entry = {
    contentId: 'id-r',
    name: 'Testy Resource',
    kind: 'feat',
    revisions: [],
    latest: { contentId: 'id-r', revisionId: 'r-r', name: 'Testy Resource', kind: 'feat', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects: [resource] },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: 'PB', points: [{ level: 1, value: 2 }, { level: 20, value: 6 }] });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Resource' }));
  const uses = await screen.findByLabelText(/^Uses \(a number or a formula/);
  await user.clear(uses);
  await user.type(uses, 'PB');

  expect(await screen.findByText('Level 1: 2 · Level 20: 6')).toBeTruthy();
  expect(uses.getAttribute('aria-describedby')).toBe('rule-resource-1-maximum-preview');
  expect(document.getElementById('rule-resource-1-maximum-preview')?.textContent).toContain('Level 20: 6');
  expect(vi.mocked(client.previewFormula).mock.calls.at(-1)?.[0]).toBe('PB');
});
