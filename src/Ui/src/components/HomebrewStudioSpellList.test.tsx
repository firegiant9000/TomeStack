// @vitest-environment jsdom
// Item 10: "Add to a spell list…" on the studio's spell entries, and the publish panel it opens on exactly those drafts.
import { act, cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: {
    listSources: vi.fn(),
    contentBySource: vi.fn(),
    publishManyPreview: vi.fn(),
    publishMany: vi.fn(),
    addToSpellList: vi.fn(),
    spellListKeys: vi.fn(),
  },
}));

const info = { rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }] } as unknown as AppInfo;
const source = { id: 'a', title: 'Testy Alpha', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

const entry = (id: string, name: string, kind: string, status: 'draft' | 'published', published = status === 'published'): StudioEntry =>
  ({
    contentId: id,
    name,
    kind,
    revisions: [],
    latest: { contentId: id, revisionId: `rev-${id}-${status}`, status, effects: [] },
    latestPublished: published ? { contentId: id, revisionId: `rev-${id}-published` } : undefined,
  }) as unknown as StudioEntry;

const held = (el: HTMLElement) => el.getAttribute('aria-disabled') === 'true';

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.spellListKeys).mockResolvedValue([{ key: 'wizard', spells: 3 }]);
  vi.mocked(client.publishManyPreview).mockResolvedValue({ entries: [], dependents: [], canPublish: true });
  vi.mocked(client.addToSpellList).mockReset();
  vi.mocked(client.contentBySource).mockResolvedValue([
    entry('s1', 'Testy Spark', 'spell', 'published'),
    entry('s2', 'Testy Glow', 'spell', 'published'),
    entry('f1', 'Testy Feat', 'feat', 'draft'),
  ]);
});
afterEach(cleanup);

it('offers row checkboxes on spells only, and holds the list button with a reason until one is ticked', async () => {
  const user = userEvent.setup();
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await screen.findByText('Testy Spark');
  expect(screen.queryByRole('checkbox', { name: 'Select Testy Feat' })).toBeNull();

  const none = screen.getByRole('button', { name: 'Add to a spell list…' });
  expect(held(none)).toBe(true);
  expect(none).toHaveProperty('disabled', false);
  expect(document.getElementById(none.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Select at least one spell first.');

  await user.click(screen.getByRole('checkbox', { name: 'Select Testy Spark' }));
  expect(held(screen.getByRole('button', { name: 'Add 1 spell to a spell list…' }))).toBe(false);
});

it('saves drafts, says so through the app status, and opens the publish panel on exactly those drafts, all ticked', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  vi.mocked(client.addToSpellList).mockImplementation(() => {
    // The reload after the add sees a new draft of Testy Spark (it replaces a published version).
    vi.mocked(client.contentBySource).mockResolvedValue([
      entry('s1', 'Testy Spark', 'spell', 'draft', true),
      entry('s2', 'Testy Glow', 'spell', 'published'),
      entry('f1', 'Testy Feat', 'feat', 'draft'),
    ]);
    return Promise.resolve({
      listKey: 'wizard',
      drafts: [{ contentId: 's1', revisionId: 'rev-s1-draft' }],
      skipped: [{ contentId: 's2', codes: ['spell-list.already-on-list'] }],
      note: 'Characters keep the spell revisions they recorded until you review an update for each one.',
    });
  });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={onStatus} />);
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Spark' }));
  await user.click(screen.getByRole('checkbox', { name: 'Select Testy Glow' }));
  await user.click(screen.getByRole('button', { name: 'Add 2 spells to a spell list…' }));
  const input = screen.getByRole('combobox', { name: 'Spell list key' });
  expect(document.activeElement).toBe(input);
  await user.type(input, 'Wizard');
  await user.click(screen.getByRole('button', { name: 'Add 2 spells to this list' }));

  expect(client.addToSpellList).toHaveBeenCalledExactlyOnceWith(['s1', 's2'], 'wizard');
  const region = await screen.findByRole('region', { name: 'Publish drafts of Testy Alpha' });
  expect(within(region).getByRole('checkbox', { name: 'Publish Testy Spark' })).toHaveProperty('checked', true);
  expect(within(region).queryByRole('checkbox', { name: 'Publish Testy Feat' })).toBeNull(); // not one of the new drafts
  expect(document.activeElement).toBe(within(region).getByRole('heading', { name: 'Publish drafts of Testy Alpha' }));
  expect(onStatus).toHaveBeenCalledWith(expect.stringContaining('Saved 1 draft with the spell list wizard; 1 skipped.'));
  expect(onStatus.mock.calls.at(-1)![0]).toContain('Characters keep the spell revisions they recorded');
  const reasons = screen.getByRole('list', { name: 'Not changed' });
  expect(within(reasons).getByText(/Testy Glow/).textContent).toContain('is already on this list');
  expect(screen.queryByRole('combobox', { name: 'Spell list key' })).toBeNull(); // the form closed
  expect(document.querySelectorAll('[aria-live="polite"]').length).toBeLessThanOrEqual(1);
  expect(screen.queryByRole('status')).toBeNull();
});

it('opens no publish panel when nothing was saved, and keeps focus on the list button', async () => {
  const user = userEvent.setup();
  vi.mocked(client.addToSpellList).mockResolvedValue({
    listKey: 'wizard',
    drafts: [],
    skipped: [{ contentId: 's1', codes: ['spell-list.already-on-list'] }],
    note: 'x',
  });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Spark' }));
  await user.click(screen.getByRole('button', { name: 'Add 1 spell to a spell list…' }));
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  await user.click(screen.getByRole('button', { name: 'Add 1 spell to this list' }));

  await vi.waitFor(() => expect(screen.queryByRole('combobox', { name: 'Spell list key' })).toBeNull());
  expect(screen.queryByRole('region', { name: 'Publish drafts of Testy Alpha' })).toBeNull();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Add 1 spell to a spell list…' }));
});

it('reports a refused add through onError and leaves the form open', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  vi.mocked(client.addToSpellList).mockImplementation(() => Promise.reject(new Error('refused')));
  render(<HomebrewStudio info={info} onError={onError} onStatus={() => {}} />);
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Spark' }));
  await user.click(screen.getByRole('button', { name: 'Add 1 spell to a spell list…' }));
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  await user.click(screen.getByRole('button', { name: 'Add 1 spell to this list' }));
  await vi.waitFor(() => expect(onError).toHaveBeenCalled());
  expect(screen.getByRole('combobox', { name: 'Spell list key' })).toBeTruthy();
});

/** Ticks Testy Spark, opens the form and confirms "wizard". */
async function addWizardToSpark(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Spark' }));
  await user.click(screen.getByRole('button', { name: 'Add 1 spell to a spell list…' }));
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  await user.click(screen.getByRole('button', { name: 'Add 1 spell to this list' }));
}

const saved = { listKey: 'wizard', drafts: [{ contentId: 's1', revisionId: 'rev-s1-draft' }], skipped: [{ contentId: 's2', codes: ['spell-list.has-unpublished-draft'] }], note: 'Characters keep the spell revisions they recorded until you review an update for each one.' };

it('reports the committed result before the reload, so a slow or failed reload cannot hide it (review fix 9)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  vi.mocked(client.addToSpellList).mockImplementation(() => {
    vi.mocked(client.contentBySource).mockImplementation(() => new Promise(() => {})); // the reload never answers
    return Promise.resolve(saved);
  });
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={onStatus} />);
  await user.click(await screen.findByRole('checkbox', { name: 'Select Testy Glow' }));
  await user.click(screen.getByRole('checkbox', { name: 'Select Testy Spark' }));
  await user.click(screen.getByRole('button', { name: 'Add 2 spells to a spell list…' }));
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  await user.click(screen.getByRole('button', { name: 'Add 2 spells to this list' }));

  await vi.waitFor(() => expect(onStatus).toHaveBeenCalledWith(expect.stringContaining('Saved 1 draft with the spell list wizard; 1 skipped.')));
  const reasons = screen.getByRole('list', { name: 'Not changed' });
  expect(within(reasons).getByText(/Testy Glow/).textContent).toContain('has an unpublished draft; publish or discard it first');
  expect(within(reasons).getByText(/Testy Glow/).textContent).not.toContain('already on this list');
  expect(screen.queryByRole('combobox', { name: 'Spell list key' })).toBeNull(); // the form closed
});

it('ignores the result when the source was changed during the call (review fix 9)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  let finish: (value: typeof saved) => void = () => {};
  vi.mocked(client.addToSpellList).mockReturnValue(new Promise((resolve) => (finish = resolve)));
  vi.mocked(client.listSources).mockResolvedValue([source, { ...source, id: 'b', title: 'Testy Beta' }]);
  vi.mocked(client.contentBySource).mockImplementation((id) =>
    Promise.resolve(id === 'a' ? [entry('s1', 'Testy Spark', 'spell', 'published'), entry('s2', 'Testy Glow', 'spell', 'published')] : []),
  );
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={onStatus} />);
  await addWizardToSpark(user);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Homebrew source' }), 'b');
  await vi.waitFor(() => expect(screen.getByRole('heading', { name: 'Content in Testy Beta' })).toBeTruthy());

  await act(async () => finish(saved));

  expect(onStatus).not.toHaveBeenCalled();
  expect(screen.queryByRole('region', { name: /^Publish drafts of/ })).toBeNull();
  expect(screen.queryByRole('list', { name: 'Not changed' })).toBeNull();
});

it('keeps the result visible and focused after the restricted panel publishes (review fix 10)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.addToSpellList).mockImplementation(() => {
    vi.mocked(client.contentBySource).mockResolvedValue([entry('s1', 'Testy Spark', 'spell', 'draft', true), entry('f1', 'Testy Feat', 'feat', 'draft')]);
    return Promise.resolve({ ...saved, skipped: [] });
  });
  const draft = { contentId: 's1', revisionId: 'rev-s1-draft' };
  vi.mocked(client.publishManyPreview).mockResolvedValue({
    entries: [{ draft, name: 'Testy Spark', kind: 'spell', report: { revision: draft, errors: [], warnings: [], canPublish: true }, findings: [], replacesPublished: true, rePointings: [] }],
    dependents: [],
    canPublish: true,
  });
  vi.mocked(client.publishMany).mockResolvedValue({ published: [{ contentId: 's1', revisionId: 'rev-s1-new' }] } as never);
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await addWizardToSpark(user);
  const region = await screen.findByRole('region', { name: 'Publish drafts of Testy Alpha' });
  const publish = await within(region).findByRole('button', { name: 'Publish 1 draft' });
  await vi.waitFor(() => expect(publish.getAttribute('aria-disabled')).toBeNull());
  await user.click(publish);

  const result = await within(region).findByText('Published 1 draft.');
  expect(document.activeElement).toBe(result); // the panel was not remounted by clearing the restriction
  // The restriction is gone: a later "Publish all drafts" would list every pending draft.
  await user.click(await screen.findByRole('button', { name: 'Publish all drafts in this source…' }));
  expect(await screen.findByRole('checkbox', { name: 'Publish Testy Feat' })).toBeTruthy();
});
