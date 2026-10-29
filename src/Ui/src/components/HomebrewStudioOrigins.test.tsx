// @vitest-environment jsdom
// Item 12: "New species", "New background" and "New spell" in the studio, and what saving and publishing do for each.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, ContentKind, ContentRevision, SourceRecord, StudioEntry } from '../api/types';
import { increaseOptions, isIncreaseChoice } from '../entryBasics';
import { HomebrewStudio } from './HomebrewStudio';
import { fill } from '../test/fill';

vi.mock('../api/client', () => ({
  client: {
    listSources: vi.fn(),
    contentBySource: vi.fn(),
    saveDraft: vi.fn(),
    publish: vi.fn(),
    validateRevision: vi.fn(),
    listContent: vi.fn(),
    spellListKeys: vi.fn(),
    publishManyPreview: vi.fn(),
    listCharacters: vi.fn(),
    publishMany: vi.fn(),
  },
}));

const info = {
  rulesFamilies: [
    { id: 'srd-5.1', displayName: 'Fixture Older Rules', abilityIncreaseSource: 'species', backgroundGrantsFeat: false },
    { id: 'srd-5.2.1', displayName: 'Fixture Newer Rules', abilityIncreaseSource: 'background', backgroundGrantsFeat: true },
  ],
  fields: [{ id: 'skill.athletics', label: 'Athletics' }],
} as unknown as AppInfo;

let source: SourceRecord;
let saved: ContentRevision[];
let n: number;

/** The studio's entries as the service would list them after the drafts that were saved. */
function entriesNow(): StudioEntry[] {
  const byContent = new Map<string, ContentRevision[]>();
  for (const r of saved) byContent.set(r.contentId, [...(byContent.get(r.contentId) ?? []), r]);
  return [...byContent.values()].map((revisions) => ({
    contentId: revisions[0]!.contentId,
    name: revisions.at(-1)!.name,
    kind: revisions[0]!.kind as ContentKind,
    revisions,
    latest: revisions.at(-1)!,
    latestPublished: revisions.filter((r) => r.status === 'published').at(-1),
  }));
}

function render_(onStatus: (text: string) => void = () => {}) {
  return render(<HomebrewStudio info={info} onError={(e) => { throw e as Error; }} onStatus={onStatus} />);
}

beforeEach(() => {
  n = 0;
  saved = [];
  source = { id: 'src', title: 'Fixture Book', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;
  vi.mocked(client.listSources).mockImplementation(() => Promise.resolve([source]));
  vi.mocked(client.contentBySource).mockImplementation(() => Promise.resolve(entriesNow()));
  vi.mocked(client.saveDraft).mockReset();
  vi.mocked(client.saveDraft).mockImplementation((revision) => {
    const id = ++n;
    const stored = { ...revision, revisionId: `rev-${id}`, status: 'draft' } as ContentRevision;
    saved.push(stored);
    return Promise.resolve({ contentId: stored.contentId, revisionId: stored.revisionId });
  });
  vi.mocked(client.publish).mockReset();
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.spellListKeys).mockResolvedValue([]);
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'c', revisionId: 'r' }, errors: [], warnings: [], canPublish: true });
  vi.mocked(client.publishManyPreview).mockResolvedValue({ entries: [], dependents: [], canPublish: true });
});
afterEach(cleanup);

it('offers New species, New background and New spell beside the others', async () => {
  render_();
  for (const name of ['New species', 'New background', 'New spell', 'New class', 'New feat']) {
    expect(await screen.findByRole('button', { name })).toBeTruthy();
  }
});

it('a new species: increases and skills are saved as effects, and the rule list does not repeat them', async () => {
  const user = userEvent.setup();
  render_();
  await user.click(await screen.findByRole('button', { name: 'New species' }));
  expect(screen.getByRole('heading', { name: 'New species' })).toBeTruthy();
  await fill(user, screen.getByRole('textbox', { name: 'Name' }), 'Fixture Folk');
  await user.type(screen.getByRole('textbox', { name: 'Strength' }), '2');
  await user.click(screen.getByRole('checkbox', { name: 'Athletics' }));
  expect(screen.queryByRole('group', { name: /^Rule \d/ })).toBeNull(); // nothing listed as a loose rule
  await user.click(screen.getByRole('button', { name: 'Save draft' }));

  await waitFor(() => expect(client.saveDraft).toHaveBeenCalledTimes(1));
  const revision = vi.mocked(client.saveDraft).mock.calls[0]![0];
  expect(revision).toMatchObject({ kind: 'species', name: 'Fixture Folk', rulesFamilies: ['srd-5.1'], status: 'draft' });
  expect(revision.effects).toEqual([
    { type: 'modifier', id: 'str-increase', operation: 'bonus', target: 'ability.str.score', value: '2' },
    { type: 'grant', id: 'skill-athletics', grant: 'proficiency', target: 'skill.athletics' },
  ]);
});

it('a new spell starts as a level 1 spell and saves trimmed text with its lists', async () => {
  const user = userEvent.setup();
  render_();
  await user.click(await screen.findByRole('button', { name: 'New spell' }));
  expect(screen.getByRole('heading', { name: 'New spell' })).toBeTruthy();
  await fill(user, screen.getByRole('textbox', { name: 'Name' }), 'Fixture Spark');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Level' }), '0');
  await user.type(screen.getByRole('textbox', { name: 'Range' }), '  30 feet ');
  await user.type(screen.getByRole('combobox', { name: 'Add a spell list' }), 'Fixture List');
  await user.click(screen.getByRole('button', { name: 'Add list' }));
  await user.click(screen.getByRole('button', { name: 'Save draft' }));

  await waitFor(() => expect(client.saveDraft).toHaveBeenCalledTimes(1));
  const revision = vi.mocked(client.saveDraft).mock.calls[0]![0];
  expect(revision.kind).toBe('spell');
  expect(revision.effects).toEqual([{ type: 'spell', id: 'spell', level: 0, concentration: false, ritual: false, lists: ['fixture-list'], range: '30 feet' }]);
});

it('a typed spell list key that was not added blocks Save draft with aria-disabled, a linked reason, and kept focus', async () => {
  const user = userEvent.setup();
  render_();
  await user.click(await screen.findByRole('button', { name: 'New spell' }));
  await user.type(screen.getByRole('combobox', { name: 'Add a spell list' }), 'wizard');
  const save = screen.getByRole('button', { name: 'Save draft' });
  expect(save.getAttribute('aria-disabled')).toBe('true');
  expect(save).toHaveProperty('disabled', false); // never native disabled: it stays focusable
  const reason = document.getElementById(save.getAttribute('aria-describedby') ?? '');
  expect(reason?.textContent).toMatch(/Spell lists: add the typed list key with Add list, or clear it\./);
  const publish = screen.getByRole('button', { name: 'Publish' });
  expect(publish.getAttribute('aria-disabled')).toBe('true');
  expect(publish).toHaveProperty('disabled', false);
  expect((publish.getAttribute('aria-describedby') ?? '').split(' ')).toContain(save.getAttribute('aria-describedby'));
  save.focus();
  await user.click(save);
  await user.click(publish);
  expect(client.saveDraft).not.toHaveBeenCalled();
  expect(client.publish).not.toHaveBeenCalled();
  expect(document.activeElement).toBe(publish); // the pressed button keeps focus
});

it('a 2014 background saves a feature grant and no increases; a 2024 one has no feature picker', async () => {
  const user = userEvent.setup();
  render_();
  await user.click(await screen.findByRole('button', { name: 'New background' }));
  expect(screen.getByRole('combobox', { name: 'Feature granted by the background' })).toBeTruthy();
  expect(screen.queryByRole('combobox', { name: 'Feat granted by the background' })).toBeNull();
  expect(screen.queryByRole('checkbox', { name: 'Strength' })).toBeNull();
});

async function newBackgroundWithIncreases(user: ReturnType<typeof userEvent.setup>) {
  source = { ...source, rulesFamilies: ['srd-5.2.1'] };
  render_(onStatus);
  await user.click(await screen.findByRole('button', { name: 'New background' }));
  await fill(user, screen.getByRole('textbox', { name: 'Name' }), 'Fixture Wanderer');
  for (const name of ['Strength', 'Dexterity', 'Constitution']) await user.click(screen.getByRole('checkbox', { name }));
}
const onStatus = vi.fn();

it('a 2024 background: saving makes the seven option drafts first, then the background pinning them', async () => {
  const user = userEvent.setup();
  onStatus.mockReset();
  await newBackgroundWithIncreases(user);
  await user.click(screen.getByRole('button', { name: 'Save draft' }));

  await waitFor(() => expect(client.saveDraft).toHaveBeenCalledTimes(8));
  const calls = vi.mocked(client.saveDraft).mock.calls.map((c) => c[0]);
  expect(calls.slice(0, 7).map((r) => r.name)).toEqual(increaseOptions('Fixture Wanderer', ['str', 'dex', 'con']).map((o) => o.name));
  expect(calls.slice(0, 7).every((r) => r.kind === 'feature' && r.status === 'draft')).toBe(true);
  const background = calls[7]!;
  expect(background.kind).toBe('background');
  const choice = background.effects.find(isIncreaseChoice)!;
  expect(choice.choiceId).toBe('fixture-wanderer-ability-scores');
  expect(choice.options.map((o) => o.revisionId)).toEqual(['rev-1', 'rev-2', 'rev-3', 'rev-4', 'rev-5', 'rev-6', 'rev-7']);
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith(expect.stringContaining('points at 7 unpublished drafts')));
  expect(onStatus.mock.calls.at(-1)![0]).toContain('Publish all drafts in this source');
});

it('R61: Publish for a background that pins drafts works as the service does: it saves the options, then publishes the background', async () => {
  const user = userEvent.setup();
  onStatus.mockReset();
  vi.mocked(client.publish).mockResolvedValue({ published: { contentId: 'bg', revisionId: 'bg-pub' }, report: { revision: { contentId: 'bg', revisionId: 'bg-pub' }, errors: [], warnings: [], canPublish: true }, affected: [] } as never);
  await newBackgroundWithIncreases(user);
  await user.click(screen.getByRole('button', { name: 'Publish' }));

  await waitFor(() => expect(client.saveDraft).toHaveBeenCalledTimes(8));
  await waitFor(() => expect(client.publish).toHaveBeenCalledTimes(1));
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('Published Fixture Wanderer.'));
});

it('R61: a background that points at drafts shows a linked, non-blocking note beside Publish and Publish stays available', async () => {
  const user = userEvent.setup();
  onStatus.mockReset();
  await newBackgroundWithIncreases(user);
  await user.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(onStatus).toHaveBeenCalled());

  const publish = await screen.findByRole('button', { name: 'Publish' });
  const note = 'Points at 7 unpublished drafts. Publish all drafts publishes them together and re-points this entry.';
  await waitFor(() => {
    const described = (publish.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent);
    expect(described).toContain(note);
  });
  expect(publish.getAttribute('aria-disabled')).toBeNull();
  expect(publish).toHaveProperty('disabled', false);
  expect(within(document.body).queryAllByRole('status')).toHaveLength(0);
});

it('R61: a species that grants a draft trait shows the same note', async () => {
  const user = userEvent.setup();
  saved.push({ contentId: 'trait-c', revisionId: 'trait-r', kind: 'feature', name: 'Fixture Keen Senses', rulesFamilies: ['srd-5.1'], provenance: { sourceId: 'src' }, status: 'draft', effects: [] } as ContentRevision);
  render_();
  await user.click(await screen.findByRole('button', { name: 'New species' }));
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Add an existing feature as a trait' }), 'Fixture Keen Senses (draft)');
  await user.click(screen.getByRole('button', { name: 'Add trait' }));
  const publish = screen.getByRole('button', { name: 'Publish' });
  const described = (publish.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent);
  expect(described).toContain('Points at 1 unpublished draft. Publish all drafts publishes it together and re-points this entry.');
});

it('R64: Publish all drafts is held with a linked reason while the open editor has unsaved changes, and runs again once they are saved', async () => {
  const user = userEvent.setup();
  onStatus.mockReset();
  await newBackgroundWithIncreases(user);
  await user.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(onStatus).toHaveBeenCalled());
  const publishAll = await screen.findByRole('button', { name: 'Publish all drafts in this source…' });
  expect(publishAll.getAttribute('aria-disabled')).toBeNull(); // clean editor: it runs

  await user.type(screen.getByRole('textbox', { name: 'Name' }), ' Two');
  await waitFor(() => expect(publishAll.getAttribute('aria-disabled')).toBe('true'));
  expect(publishAll).toHaveProperty('disabled', false);
  expect(document.getElementById(publishAll.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Save or close the open editor first.');
  publishAll.focus();
  await user.click(publishAll);
  expect(screen.queryByRole('region', { name: /^Publish drafts of/ })).toBeNull();
  expect(document.activeElement).toBe(publishAll);
  expect(screen.getByRole('heading', { name: 'New background' })).toBeTruthy(); // nothing was discarded

  await user.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(publishAll.getAttribute('aria-disabled')).toBeNull());
});

it('item 1: once Publish all has published the entry that was saved in the open editor, the editor closes with a status', async () => {
  const user = userEvent.setup();
  onStatus.mockReset();
  vi.mocked(client.publishMany).mockImplementation(() => {
    // The service published every pending draft: a published revision follows each saved draft.
    const drafts = [...saved];
    for (const d of drafts) saved.push({ ...d, revisionId: `${d.revisionId}-pub`, status: 'published' } as ContentRevision);
    return Promise.resolve({ published: [] });
  });
  await newBackgroundWithIncreases(user);
  await user.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(onStatus).toHaveBeenCalled());
  expect(screen.getByRole('heading', { name: 'New background' })).toBeTruthy();

  await user.click(await screen.findByRole('button', { name: 'Publish all drafts in this source…' }));
  vi.mocked(client.publishManyPreview).mockImplementation((ids) =>
    Promise.resolve({
      entries: ids.map((id) => ({ draft: { contentId: id, revisionId: saved.find((r) => r.contentId === id)!.revisionId }, name: 'x', kind: 'feature', report: { revision: { contentId: id, revisionId: 'r' }, errors: [], warnings: [], canPublish: true }, findings: [], replacesPublished: false, rePointings: [] })),
      dependents: [],
      canPublish: true,
    } as never),
  );
  const publishAll = await screen.findByRole('button', { name: 'Publish 8 drafts' }, { timeout: 5000 });
  await waitFor(() => expect(publishAll.getAttribute('aria-disabled')).toBeNull(), { timeout: 5000 });
  await user.click(publishAll);

  await waitFor(() => expect(screen.queryByRole('heading', { name: 'New background' })).toBeNull());
  expect(onStatus.mock.calls.at(-1)![0]).toContain('Closed the editor of Fixture Wanderer');
});
