// @vitest-environment jsdom
// Item 9, D33: a previewed, confirmed, all-or-nothing publish of a source's pending drafts.
import { act, cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { PublishManyEntry, PublishManyPreview, SourceRecord } from '../api/types';
import { PublishManyPanel, type PendingDraft } from './PublishManyPanel';

vi.mock('../api/client', () => ({ client: { publishManyPreview: vi.fn(), publishMany: vi.fn(), compare: vi.fn() } }));

const source = { id: 'src-1', title: 'Testy Tome', rulesFamilies: ['srd-5.2.1'] } as SourceRecord;

const pendingOf = (n: number, replaces = false): PendingDraft => ({
  contentId: `c${n}`,
  name: `Testy Entry ${n}`,
  kind: 'feat',
  replacesPublished: replaces,
  draft: { contentId: `c${n}`, revisionId: `d${n}` },
  latestPublished: replaces ? { contentId: `c${n}`, revisionId: `p${n}` } : undefined,
});

const entryOf = (p: PendingDraft, over: Partial<PublishManyEntry> = {}): PublishManyEntry => ({
  draft: p.draft,
  name: p.name,
  kind: p.kind,
  report: { revision: p.draft, errors: [], warnings: [], canPublish: true },
  findings: [],
  replacesPublished: p.replacesPublished,
  rePointings: [],
  ...over,
});

const previewOf = (entries: PublishManyEntry[], over: Partial<PublishManyPreview> = {}): PublishManyPreview => ({
  entries,
  dependents: [],
  canPublish: entries.every((e) => e.report.errors.length === 0),
  ...over,
});

/** The preview echoes the ticked ids back as entries, as the service does. */
function echoPreview(all: PendingDraft[]) {
  vi.mocked(client.publishManyPreview).mockImplementation((ids) => Promise.resolve(previewOf(all.filter((p) => ids.includes(p.contentId)).map((p) => entryOf(p)))));
}

const onClose = vi.fn();
const onPublished = vi.fn();

function renderPanel(pending: PendingDraft[]) {
  return render(<PublishManyPanel source={source} pending={pending} onError={() => {}} onClose={onClose} onPublished={onPublished} />);
}

const held = (button: HTMLElement) => button.getAttribute('aria-disabled') === 'true';

beforeEach(() => {
  vi.mocked(client.publishManyPreview).mockReset();
  vi.mocked(client.publishMany).mockReset();
  vi.mocked(client.compare).mockReset();
  onClose.mockClear();
  onPublished.mockClear();
});
afterEach(cleanup);

it('opens a named region whose heading has focus, with a captioned table of the pending drafts', async () => {
  const all = [pendingOf(1), pendingOf(2, true)];
  echoPreview(all);
  renderPanel(all);
  const region = await screen.findByRole('region', { name: 'Publish drafts of Testy Tome' });
  expect(document.activeElement).toBe(within(region).getByRole('heading', { name: 'Publish drafts of Testy Tome' }));
  const table = within(region).getByRole('table', { name: 'Pending drafts in Testy Tome' });
  expect(within(table).getAllByRole('row')).toHaveLength(3);
  expect(within(table).getByRole('columnheader', { name: 'Problems' })).toBeTruthy();
});

it('ticks only never-published drafts, and previews exactly the ticked ones', async () => {
  const all = [pendingOf(1), pendingOf(2, true)];
  echoPreview(all);
  renderPanel(all);
  const first = (await screen.findByRole('checkbox', { name: 'Publish Testy Entry 1' })) as HTMLInputElement;
  const second = screen.getByRole('checkbox', { name: 'Publish Testy Entry 2' }) as HTMLInputElement;
  expect([first.checked, second.checked]).toEqual([true, false]);
  expect(screen.getByText('replaces the published version')).toBeTruthy();
  await vi.waitFor(() => expect(client.publishManyPreview).toHaveBeenLastCalledWith(['c1']));
  expect(await screen.findByRole('button', { name: 'Publish 1 draft' })).toBeTruthy();
});

it('previews again when a tick changes, and holds the button with a reason at none', async () => {
  const user = userEvent.setup();
  const all = [pendingOf(1), pendingOf(2, true)];
  echoPreview(all);
  renderPanel(all);
  await user.click(await screen.findByRole('checkbox', { name: 'Publish Testy Entry 2' }));
  await vi.waitFor(() => expect(client.publishManyPreview).toHaveBeenLastCalledWith(['c1', 'c2']));
  expect(await screen.findByRole('button', { name: 'Publish 2 drafts' })).toBeTruthy();

  await user.click(screen.getByRole('checkbox', { name: 'Publish Testy Entry 1' }));
  await user.click(screen.getByRole('checkbox', { name: 'Publish Testy Entry 2' }));
  const button = screen.getByRole('button', { name: 'Publish 0 drafts' });
  expect(held(button)).toBe(true);
  expect(document.getElementById(button.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Tick at least one draft.');
});

it('holds the publish button while the preview loads', async () => {
  const all = [pendingOf(1)];
  let finish: (p: PublishManyPreview) => void = () => {};
  vi.mocked(client.publishManyPreview).mockReturnValue(new Promise((resolve) => (finish = resolve)));
  renderPanel(all);
  const button = await screen.findByRole('button', { name: 'Publish 1 draft' });
  expect(held(button)).toBe(true);
  expect(button).toHaveProperty('disabled', false);
  expect(document.getElementById(button.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Checking the drafts…');
  await act(async () => finish(previewOf([entryOf(all[0]!)])));
  expect(held(button)).toBe(false);
});

it('refuses while any error exists, naming it, and sends nothing', async () => {
  const user = userEvent.setup();
  const all = [pendingOf(1), pendingOf(2)];
  vi.mocked(client.publishManyPreview).mockResolvedValue(
    previewOf([
      entryOf(all[0]!),
      entryOf(all[1]!, { report: { revision: all[1]!.draft, errors: [{ code: 'validate.dice-invalid', message: 'The dice are not valid.' }], warnings: [], canPublish: false } }),
    ]),
  );
  renderPanel(all);
  const button = await screen.findByRole('button', { name: 'Publish 2 drafts' });
  const list = await screen.findByRole('list', { name: 'Problems that block publishing' });
  expect(within(list).getByText('Testy Entry 2: The dice are not valid.')).toBeTruthy();
  await vi.waitFor(() => expect(held(button)).toBe(true));
  expect(button.getAttribute('aria-describedby')).toBe('publish-many-reason');
  await user.click(button);
  expect(client.publishMany).not.toHaveBeenCalled();
});

it('lists what is re-pointed and the dependents that are republished', async () => {
  const all = [pendingOf(1)];
  const dependent = entryOf(
    { ...pendingOf(9, true), name: 'Testy Class' },
    { rePointings: [{ dependent: { contentId: 'c9', revisionId: 'p9' }, dependentName: 'Testy Class', grantsMoved: 2, optionsMoved: 1 }] },
  );
  vi.mocked(client.publishManyPreview).mockResolvedValue(previewOf([entryOf(all[0]!)], { dependents: [dependent] }));
  renderPanel(all);
  const list = await screen.findByRole('list', { name: 'Re-pointed and republished' });
  expect(within(list).getByText('Testy Class: 2 grants and 1 choice option now point at the published versions.')).toBeTruthy();
});

it('publishes the ticked ids with their pending revision ids, ignores a second press, and puts focus on the result', async () => {
  const user = userEvent.setup();
  const all = [pendingOf(1), pendingOf(3)];
  echoPreview(all);
  let finish: (value: { published: never[] }) => void = () => {};
  vi.mocked(client.publishMany).mockReturnValue(new Promise((resolve) => (finish = resolve)));
  renderPanel(all);
  const button = await screen.findByRole('button', { name: 'Publish 2 drafts' });
  await vi.waitFor(() => expect(held(button)).toBe(false));

  await user.click(button);
  await user.click(button);

  expect(client.publishMany).toHaveBeenCalledExactlyOnceWith(['c1', 'c3'], ['d1', 'd3']);
  await act(async () => finish({ published: [] }));
  const result = await screen.findByText('Published 2 drafts.');
  expect(document.activeElement).toBe(result);
  expect(result.getAttribute('role')).toBeNull(); // the app has the one role=status
  expect(onPublished).toHaveBeenCalledExactlyOnceWith(2);
});

it('shows what a replacing draft changes, from content.compare', async () => {
  const user = userEvent.setup();
  const all = [pendingOf(2, true)];
  echoPreview(all);
  vi.mocked(client.compare).mockResolvedValue({
    mechanics: { properties: [], effects: [{ effectId: 'init', change: 'changed', type: 'modifier', before: '1', after: '2' }] },
    text: [],
    runs: [],
  });
  renderPanel(all);
  await user.click(await screen.findByRole('button', { name: 'Show what changes in Testy Entry 2' }));
  expect(client.compare).toHaveBeenCalledWith({ from: { contentId: 'c2', revisionId: 'p2' }, to: { contentId: 'c2', revisionId: 'd2' } });
  expect(await screen.findByText('changed init: 1 to 2')).toBeTruthy();
});
