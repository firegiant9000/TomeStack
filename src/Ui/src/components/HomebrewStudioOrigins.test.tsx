// @vitest-environment jsdom
// Item 12: "New species", "New background" and "New spell" in the studio, and what saving and publishing do for each.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, ContentKind, ContentRevision, SourceRecord, StudioEntry } from '../api/types';
import { increaseOptions, isIncreaseChoice } from '../entryBasics';
import { HomebrewStudio } from './HomebrewStudio';

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
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture Folk');
  await user.type(screen.getByRole('spinbutton', { name: 'Strength' }), '2');
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
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture Spark');
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

it('a typed spell list key that was not added blocks Save draft and says why', async () => {
  const user = userEvent.setup();
  render_();
  await user.click(await screen.findByRole('button', { name: 'New spell' }));
  await user.type(screen.getByRole('combobox', { name: 'Add a spell list' }), 'wizard');
  const save = screen.getByRole('button', { name: 'Save draft' });
  expect(save).toHaveProperty('disabled', true);
  expect(screen.getByText(/Spell lists: add the typed list key with Add list, or clear it\./)).toBeTruthy();
  expect(client.saveDraft).not.toHaveBeenCalled();
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
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture Wanderer');
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

it('Publish for a background that pins drafts saves it but publishes nothing, and says what to do', async () => {
  const user = userEvent.setup();
  onStatus.mockReset();
  await newBackgroundWithIncreases(user);
  await user.click(screen.getByRole('button', { name: 'Publish' }));

  await waitFor(() => expect(client.saveDraft).toHaveBeenCalledTimes(8));
  expect(client.publish).not.toHaveBeenCalled();
  await waitFor(() => expect(onStatus).toHaveBeenCalled());
  expect(onStatus.mock.calls.at(-1)![0]).toContain('not published');
});

it('once the drafts are known, Publish is held with a linked reason and focus is kept', async () => {
  const user = userEvent.setup();
  onStatus.mockReset();
  await newBackgroundWithIncreases(user);
  await user.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(onStatus).toHaveBeenCalled());

  const publish = await screen.findByRole('button', { name: 'Publish' });
  await waitFor(() => expect(publish.getAttribute('aria-disabled')).toBe('true'));
  const reason = document.getElementById(publish.getAttribute('aria-describedby') ?? '');
  expect(reason?.textContent).toContain('points at 7 unpublished drafts');
  publish.focus();
  await user.click(publish);
  expect(client.publish).not.toHaveBeenCalled();
  expect(document.activeElement).toBe(publish);
  expect(within(document.body).queryAllByRole('status')).toHaveLength(0);
});
