// @vitest-environment jsdom
// A late reply for one character must not switch the screen once the user has moved on (checkpoint A review, R26).
// The client is mocked; names and values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from './App';
import { client } from './api/client';
import type { Character, CharacterSummary, CharacterView, ChoiceStatus } from './api/types';
import { diffLevelUp, writeChecklist } from './levelUpChecklist';
import { noticeText } from './replaceable';

// The real module, except that diffLevelUp is a spy (it throws once in the A3 test).
vi.mock('./levelUpChecklist', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./levelUpChecklist')>();
  return { ...actual, diffLevelUp: vi.fn(actual.diffLevelUp) };
});

// The real module, except that noticeText is a spy (it throws once in the R39 notice test).
vi.mock('./replaceable', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./replaceable')>();
  return { ...actual, noticeText: vi.fn(actual.noticeText) };
});

vi.mock('./api/client', () => ({
  client: {
    info: vi.fn(),
    listCharacters: vi.fn(),
    getCharacter: vi.fn(),
    attachment: vi.fn(),
    listGapNotes: vi.fn(),
    availableUpdates: vi.fn(),
    listContent: vi.fn(),
    draftCounts: vi.fn(),
    listCampaigns: vi.fn(),
    preview: vi.fn(),
    createCharacter: vi.fn(),
    saveCharacter: vi.fn(),
    snapshots: vi.fn(),
    previewExport: vi.fn(),
    play: vi.fn(),
    roll: vi.fn(),
    restPreview: vi.fn(),
    rest: vi.fn(),
    listSources: vi.fn(),
    dailyCopies: vi.fn(),
    libraryBackupPreview: vi.fn(),
    listAllGapNotes: vi.fn(),
    listExtensions: vi.fn(),
  },
}));

const summary = (id: string, name: string): CharacterSummary => ({ id, name, rulesFamily: 'srd-5.1', updatedAt: '2026-10-03T00:00:00Z', level: 1 });
function viewOf(id: string, name: string): CharacterView {
  const character: Character = {
    id,
    schemaVersion: 7,
    name,
    rulesFamily: 'srd-5.1',
    level: 1,
    classes: [],
    choices: [],
    crossFamilyExceptions: [],
    baseAbilities: { str: 10, dex: 12, con: 10, int: 14, wis: 10, cha: 10 },
    pins: [],
    overrides: [],
    updatedAt: '2026-10-03T00:00:00Z',
  };
  return { character, sheet: { characterId: id, rulesFamily: 'srd-5.1', diagnostics: [], fields: [], hitPoints: { maximum: 8, current: 8, temporary: 0 } } };
}
const a = viewOf('fixture-a', 'Fixture Avery');
const b = viewOf('fixture-b', 'Fixture Blake');

beforeEach(() => {
  vi.mocked(client.info).mockResolvedValue({ version: 'test', schemaVersion: 1, rulesFamilies: [], warnings: [], fields: [] });
  vi.mocked(client.listCharacters).mockResolvedValue([summary('fixture-a', 'Fixture Avery'), summary('fixture-b', 'Fixture Blake')]);
  vi.mocked(client.getCharacter).mockImplementation(async (id) => (id === 'fixture-a' ? a : b));
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'shortRest', changes: [], manual: [], basis: 'fixture' });
  vi.mocked(client.listGapNotes).mockResolvedValue([]);
  vi.mocked(client.availableUpdates).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.draftCounts).mockResolvedValue({});
  vi.mocked(client.listCampaigns).mockResolvedValue([]);
  vi.mocked(client.listSources).mockResolvedValue([]);
  vi.mocked(client.dailyCopies).mockResolvedValue({ on: false, copies: [], bytes: 0 });
  vi.mocked(client.libraryBackupPreview).mockReturnValue(new Promise(() => {})); // the Backups heading does not wait for it
  vi.mocked(client.listAllGapNotes).mockResolvedValue([]);
  vi.mocked(client.listExtensions).mockResolvedValue([]); // the Settings screen lists installed extensions
  vi.mocked(client.snapshots).mockResolvedValue({ items: [], hasMore: false });
});
afterEach(() => {
  cleanup();
  localStorage.clear();
});

it('shows the rules family id before app.info arrives, then the label the service sent', async () => {
  let resolveInfo: (value: Awaited<ReturnType<typeof client.info>>) => void = () => {};
  vi.mocked(client.info).mockReturnValue(new Promise((resolve) => (resolveInfo = resolve)));
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  await within(list).findAllByText('srd-5.1');
  expect(within(list).queryByText('2014 rules')).toBeNull();
  resolveInfo({
    version: 'test',
    schemaVersion: 1,
    rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1', shortLabel: '2014 rules' } as never],
    warnings: [],
    fields: [],
  });
  expect((await within(list).findAllByText('2014 rules')).length).toBe(2);
});

it('the document title names the place: the home screen, then an opened sheet', async () => {
  const user = userEvent.setup();
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  await waitFor(() => expect(document.title).toBe('Your characters – TomeStack'));
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await waitFor(() => expect(document.title).toBe('Fixture Avery – TomeStack'));
});

it('navigating to a screen that is not a sheet updates the document title (Settings)', async () => {
  const user = userEvent.setup();
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(within(nav).getByRole('button', { name: 'Settings' }));
  await waitFor(() => expect(document.title).toBe('Settings – TomeStack'));
  await user.click(within(nav).getByRole('button', { name: 'Backups' }));
  await waitFor(() => expect(document.title).toBe('Backups – TomeStack'));
});

it('Every_screen_has_one_h1_matching_the_title', async () => {
  const user = userEvent.setup();
  vi.mocked(client.info).mockResolvedValue({
    version: 'test',
    schemaVersion: 1,
    rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1', shortLabel: '2014 rules' } as never],
    warnings: [],
    fields: [],
  });
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Main' });
  await within(nav).findByRole('button', { name: /Fixture Avery/ });
  const main = () => document.querySelector('.app') as HTMLElement;
  const expectOneH1 = async (place: string) => {
    await waitFor(() => expect(document.title).toBe(`${place} – TomeStack`));
    await waitFor(() => {
      const h1s = within(main()).getAllByRole('heading', { level: 1, hidden: true });
      expect(h1s.map((h) => h.textContent)).toEqual([place]);
    });
  };
  const sidebar = (name: string | RegExp) => user.click(within(nav).getByRole('button', { name }));

  await expectOneH1('Your characters');
  for (const [label, place] of [
    ['Books and PDFs', 'Books and PDFs'],
    ['Homebrew', 'Homebrew'],
    ['Campaigns', 'Campaigns'],
    ['Backups', 'Backups'],
    ['Gap notes', 'Gap notes'],
    ['Settings', 'Settings'],
    ['All characters', 'Your characters'],
  ] as const) {
    await sidebar(label);
    await expectOneH1(place);
  }
  await sidebar(/Fixture Avery/);
  await expectOneH1('Fixture Avery');

  await user.click(await screen.findByRole('button', { name: 'Level up' }));
  await expectOneH1('Level up Fixture Avery');
  await user.click(await screen.findByRole('button', { name: 'Next: choices' })); // the builder's choices step
  await expectOneH1('Level up Fixture Avery');

  await sidebar('+ Add a character');
  await expectOneH1('Add a character');
  await user.click(screen.getByRole('radio', { name: /^Make a new one/ }));
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  await expectOneH1('New character');

  await sidebar('+ Add a character');
  await user.click(screen.getByRole('radio', { name: /^From a character sheet/ }));
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  await expectOneH1('From a character sheet (PDF or text)');
}, 30000); // a long walk of 14 screens; each step waits on real user-event clicks

it('Cancel from a level-up shows the character as it is now, not as it was when the builder opened (carry 1)', async () => {
  const user = userEvent.setup();
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await user.click(await screen.findByRole('button', { name: 'Level up' }));
  await screen.findByRole('heading', { name: 'Level up Fixture Avery' });
  vi.mocked(client.getCharacter).mockResolvedValue(viewOf('fixture-a', 'Fixture Avery Later'));
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(await screen.findByRole('heading', { name: 'Fixture Avery Later' })).toBeTruthy();
  expect(screen.getByText('Draft discarded. Nothing was changed.')).toBeTruthy();
});

it('two quick opens: only the latest reply is shown (carry 4)', async () => {
  const user = userEvent.setup();
  const replies: Record<string, (view: CharacterView) => void> = {};
  vi.mocked(client.getCharacter).mockImplementation((id) => new Promise<CharacterView>((r) => (replies[id] = r)));
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await user.click(within(list).getByRole('button', { name: /Fixture Blake/ }));
  replies['fixture-b']!(b);
  await waitFor(() => expect(within(list).getByRole('button', { name: /Fixture Blake/ }).getAttribute('aria-current')).toBe('page'));
  replies['fixture-a']!(a); // the older request answers last
  await new Promise((r) => setTimeout(r, 20));
  expect(within(list).getByRole('button', { name: /Fixture Blake/ }).getAttribute('aria-current')).toBe('page');
  expect(within(list).getByRole('button', { name: /Fixture Avery/ }).getAttribute('aria-current')).toBeNull();
});

/** From the sidebar: "+ Add a character", pick "Make a new one", Continue. */
async function startNew(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: '+ Add a character' }));
  await chooseNew(user);
}
async function chooseNew(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('radio', { name: 'Make a new one' }));
  await user.click(screen.getByRole('button', { name: 'Continue' }));
}

/** Walks the create steps by hand (no content installed) up to "Create and save". */
async function toCreate(user: ReturnType<typeof userEvent.setup>) {
  await startNew(user);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  for (const next of ['Next: species', 'Next: class', 'Next: background', 'Next: choices']) await user.click(await screen.findByRole('button', { name: next }));
  return screen.findByRole('button', { name: 'Create and save' });
}

it('a save that lands after the user left the builder refreshes the list and says so, without changing the screen (R36)', async () => {
  const user = userEvent.setup();
  const created = viewOf('fixture-new', 'Fixture New');
  let finish: (view: CharacterView) => void = () => {};
  vi.mocked(client.preview).mockResolvedValue(created);
  vi.mocked(client.createCharacter).mockReturnValue(new Promise<CharacterView>((r) => (finish = r)));
  render(<App />);
  await user.click(await toCreate(user));
  const lists = vi.mocked(client.listCharacters).mock.calls.length;
  await user.click(screen.getByRole('button', { name: 'All characters' })); // leaves the builder while the save is in flight
  await screen.findByRole('region', { name: 'Your characters' });
  finish(created);
  expect(await screen.findByText('Saved Fixture New.')).toBeTruthy();
  expect(vi.mocked(client.listCharacters).mock.calls.length).toBeGreaterThan(lists);
  expect(screen.getByRole('region', { name: 'Your characters' })).toBeTruthy(); // still the home screen
  expect(screen.queryByRole('article', { name: 'Fixture New' })).toBeNull();
});

it('a level-up saved while the same character was opened again shows the saved sheet, not the older read (R38 a)', async () => {
  const user = userEvent.setup();
  const before = viewOf('fixture-a', 'Fixture Avery');
  before.character = { ...before.character, classes: [{ class: { contentId: 'fixture-class', revisionId: 'fixture-class-r1' }, level: 1 }] };
  const saved = viewOf('fixture-a', 'Fixture Avery Leveled');
  vi.mocked(client.getCharacter).mockResolvedValue(before); // the read made after the save began: still level 1
  vi.mocked(client.preview).mockResolvedValue(before);
  let finish: (view: CharacterView) => void = () => {};
  vi.mocked(client.saveCharacter).mockReturnValue(new Promise<CharacterView>((r) => (finish = r)));
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await user.click(await screen.findByRole('button', { name: 'Level up' }));
  await user.click(await screen.findByRole('button', { name: 'Next: choices' }));
  await user.click(await screen.findByRole('button', { name: 'Save level-up' }));
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ })); // opens the sheet while the save is in flight
  expect(await screen.findByRole('heading', { name: 'Fixture Avery' })).toBeTruthy();
  let release: (list: CharacterSummary[]) => void = () => {};
  vi.mocked(client.listCharacters).mockReturnValue(new Promise<CharacterSummary[]>((r) => (release = r)));
  finish(saved);
  // The view is swapped before the list refresh answers (R39), so a newer view set during the refresh is never overwritten.
  expect(await screen.findByRole('heading', { name: 'Fixture Avery Leveled' })).toBeTruthy();
  release([summary('fixture-a', 'Fixture Avery Leveled')]);
  expect(await screen.findByRole('heading', { name: 'Fixture Avery Leveled' })).toBeTruthy();
});

/** A level-1 character (hit point maximum 10) and the level-2 view a save returns (maximum 17). */
function levelUpViews() {
  const before = viewOf('fixture-a', 'Fixture Avery');
  before.character = { ...before.character, classes: [{ class: { contentId: 'fixture-class', revisionId: 'fixture-class-r1' }, level: 1 }] };
  before.sheet = { ...before.sheet, hitPoints: { maximum: 10, current: 10, temporary: 0 } };
  const saved = viewOf('fixture-a', 'Fixture Avery');
  saved.character = { ...before.character, level: 2, classes: [{ class: { contentId: 'fixture-class', revisionId: 'fixture-class-r1' }, level: 2 }] };
  saved.sheet = { ...saved.sheet, hitPoints: { maximum: 17, current: 17, temporary: 0 } };
  return { before, saved };
}
async function toSaveLevelUp(user: ReturnType<typeof userEvent.setup>, before: CharacterView) {
  vi.mocked(client.getCharacter).mockResolvedValue(before);
  vi.mocked(client.preview).mockResolvedValue(before);
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await user.click(await screen.findByRole('button', { name: 'Level up' }));
  await user.click(await screen.findByRole('button', { name: 'Next: choices' }));
  return { list, save: await screen.findByRole('button', { name: 'Save level-up' }) };
}

it('Save level-up opens the sheet on Features with the level-up panel focused, remembers no tab and keeps the list (D74)', async () => {
  const user = userEvent.setup();
  const { before, saved } = levelUpViews();
  vi.mocked(client.saveCharacter).mockResolvedValue(saved);
  const { save } = await toSaveLevelUp(user, before);
  await user.click(save);
  const region = await screen.findByRole('region', { name: 'What changed at level 2' });
  expect(within(region).getByText('Hit point maximum: 10 → 17')).toBeTruthy();
  expect(screen.getByRole('tab', { name: /Features/ }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).toBe(document.getElementById('level-up-heading'));
  expect(localStorage.getItem('tomestack.levelUpChecklist.fixture-a')).not.toBeNull();
  expect(localStorage.getItem('tomestack.sheetTab.fixture-a')).toBeNull(); // the deep link is not the user's choice
});

it('a throwing diff never loses the save: the sheet still opens on Features with no error (D74 A3)', async () => {
  const user = userEvent.setup();
  const { before, saved } = levelUpViews();
  vi.mocked(client.saveCharacter).mockResolvedValue(saved);
  const { save } = await toSaveLevelUp(user, before);
  writeChecklist('fixture-a', { level: 2, changes: [{ kind: 'hitPoints', before: 1, after: 2 }] }); // an older list for the same level
  vi.mocked(diffLevelUp).mockImplementationOnce(() => {
    throw new Error('fixture diff failure');
  });
  await user.click(save);
  expect(await screen.findByRole('heading', { name: 'Fixture Avery' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: /Features/ }).getAttribute('aria-selected')).toBe('true');
  expect(screen.queryByRole('alert')).toBeNull();
  expect(screen.queryByRole('region', { name: /What changed at level/ })).toBeNull();
  expect(localStorage.getItem('tomestack.levelUpChecklist.fixture-a')).toBeNull();
});

it('a level-up that lands after the user left the builder writes the list but moves no tab or focus (D74, R38)', async () => {
  const user = userEvent.setup();
  const { before, saved } = levelUpViews();
  let finish: (view: CharacterView) => void = () => {};
  vi.mocked(client.saveCharacter).mockReturnValue(new Promise<CharacterView>((r) => (finish = r)));
  const { list, save } = await toSaveLevelUp(user, before);
  await user.click(save);
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ })); // opens the sheet while the save is in flight
  await screen.findByRole('heading', { name: 'Fixture Avery' });
  const tabBefore = screen.getAllByRole('tab').find((t) => t.getAttribute('aria-selected') === 'true')?.textContent;
  finish(saved);
  await waitFor(() => expect(localStorage.getItem('tomestack.levelUpChecklist.fixture-a')).not.toBeNull());
  expect(screen.getAllByRole('tab').find((t) => t.getAttribute('aria-selected') === 'true')?.textContent).toBe(tabBefore);
  // The list is ready in the Features panel (hidden while another tab is selected) once the save lands.
  expect(await screen.findByRole('region', { name: 'What changed at level 2', hidden: true })).toBeTruthy();
  expect(screen.getByRole('tab', { name: /Play/ }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).not.toBe(document.getElementById('level-up-heading'));
});

it('Cancel from a level-up writes no list (D74)', async () => {
  const user = userEvent.setup();
  const { before } = levelUpViews();
  await toSaveLevelUp(user, before);
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  await screen.findByText('Draft discarded. Nothing was changed.');
  expect(localStorage.getItem('tomestack.levelUpChecklist.fixture-a')).toBeNull();
});

it('pressing New character during a save mounts a fresh empty builder; the save still reports, and the old draft is gone (R40)', async () => {
  const user = userEvent.setup();
  const created = viewOf('fixture-new', 'Fixture New');
  let finish: (view: CharacterView) => void = () => {};
  vi.mocked(client.preview).mockResolvedValue(created);
  vi.mocked(client.createCharacter).mockReturnValue(new Promise<CharacterView>((r) => (finish = r)));
  render(<App />);
  const create = await toCreate(user);
  await user.click(create);
  await user.click(screen.getByRole('button', { name: '+ Add a character' })); // a new builder mode while the save is in flight
  await chooseNew(user);
  finish(created);
  // The new builder starts empty at step 1; the saved draft is not offered again.
  expect((screen.getByRole('textbox', { name: 'Name' }) as HTMLInputElement).value).toBe('');
  expect(screen.getByText(/^Step 1 of 6/)).toBeTruthy();
  expect(await screen.findByText('Saved Fixture New.')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Create and save' })).toBeNull();
  expect((screen.getByRole('textbox', { name: 'Name' }) as HTMLInputElement).value).toBe('');
  expect(vi.mocked(client.createCharacter)).toHaveBeenCalledTimes(1);
});

it('says the save succeeded when only the list refresh fails after the user has left the builder (R40)', async () => {
  const user = userEvent.setup();
  const created = viewOf('fixture-new', 'Fixture New');
  let finish: (view: CharacterView) => void = () => {};
  vi.mocked(client.preview).mockResolvedValue(created);
  vi.mocked(client.createCharacter).mockReturnValue(new Promise<CharacterView>((r) => (finish = r)));
  render(<App />);
  await user.click(await toCreate(user));
  await user.click(screen.getByRole('button', { name: 'All characters' }));
  await screen.findByRole('region', { name: 'Your characters' });
  vi.mocked(client.listCharacters).mockRejectedValueOnce(new Error('fixture refresh failure'));
  finish(created);
  const alert = await screen.findByRole('alert');
  expect(alert.textContent).toBe('Saved Fixture New. The character list could not be refreshed: fixture refresh failure');
});

it('a failing list refresh after a save leaves the saved sheet open, reports the error and sticks nothing (R39 case 2)', async () => {
  const user = userEvent.setup();
  const created = viewOf('fixture-new', 'Fixture New');
  vi.mocked(client.preview).mockResolvedValue(created);
  vi.mocked(client.createCharacter).mockResolvedValue(created);
  render(<App />);
  const create = await toCreate(user);
  vi.mocked(client.listCharacters).mockRejectedValueOnce(new Error('fixture refresh failure'));
  await user.click(create);
  expect(await screen.findByRole('article', { name: 'Fixture New' })).toBeTruthy(); // switched before the refresh
  expect((await screen.findByRole('alert')).textContent).toMatch(/fixture refresh failure/);
});

it('a slow open never replaces a screen chosen after it (R37)', async () => {
  const user = userEvent.setup();
  let reply: (view: CharacterView) => void = () => {};
  vi.mocked(client.getCharacter).mockReturnValue(new Promise<CharacterView>((r) => (reply = r)));
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await user.click(await screen.findByRole('button', { name: '+ Add a character' }));
  await chooseNew(user);
  expect(await screen.findByRole('textbox', { name: 'Name' })).toBeTruthy();
  reply(a);
  await new Promise((r) => setTimeout(r, 20));
  expect(screen.getByRole('textbox', { name: 'Name' })).toBeTruthy(); // the builder stays
  expect(screen.queryByRole('article', { name: 'Fixture Avery' })).toBeNull();
});

it("a late rest reply for the first character leaves the second character's sheet on screen and shows no status", async () => {
  const user = userEvent.setup();
  let resolveRest: (value: CharacterView) => void = () => {};
  vi.mocked(client.rest).mockReturnValue(new Promise<CharacterView>((r) => (resolveRest = r)));
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  const avery = () => within(list).getByRole('button', { name: /Fixture Avery/ });
  const blake = () => within(list).getByRole('button', { name: /Fixture Blake/ });
  await user.click(avery());
  await user.click(await screen.findByRole('button', { name: 'Short rest…' }));
  const finish = await screen.findByRole('button', { name: 'Finish short rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);
  await user.click(blake());
  await waitFor(() => expect(blake().getAttribute('aria-current')).toBe('page'));
  resolveRest(a);
  await waitFor(() => expect(client.listCharacters).toHaveBeenCalledTimes(2)); // the list is refreshed after the reply
  expect(blake().getAttribute('aria-current')).toBe('page');
  expect(avery().getAttribute('aria-current')).toBeNull();
  expect(screen.queryByText(/rest finished/)).toBeNull();
});

it('a code-split screen arrives after a plain Loading status, and focus stays on the button that opened it (R161 d)', async () => {
  const user = userEvent.setup();
  render(<App />);
  const campaigns = await screen.findByRole('button', { name: 'Campaigns' });
  await user.click(campaigns);
  expect(await screen.findByRole('heading', { name: 'Campaigns' })).toBeTruthy();
  expect(screen.queryByText('Loading…')).toBeNull();
  expect(document.activeElement).toBe(campaigns);
});

// MECH-X item 2: the save's notice (picks replaced outside their window) is worded from the sheets and shown as the status.
function replacedViews(saveLevel: number) {
  const source = { contentId: 'fixture-src', revisionId: 'fixture-src-r1' };
  const status = (picks: string[]): ChoiceStatus => ({
    source,
    sourceName: 'Fixture Repertoire',
    choiceId: 'technique',
    count: 2,
    options: [],
    selected: picks.map((p) => ({ contentId: p, revisionId: `${p}-r1` })),
    resolved: picks.length === 2,
    replaceable: { when: 'levelUp', max: 1 },
  });
  const stored = (picks: string[]) => [{ source, choiceId: 'technique', selected: picks.map((p) => ({ contentId: p, revisionId: `${p}-r1` })) }];
  const before = viewOf('fixture-a', 'Fixture Avery');
  before.character = { ...before.character, classes: [{ class: { contentId: 'fixture-class', revisionId: 'fixture-class-r1' }, level: 1 }], choices: stored(['one', 'two']) };
  before.sheet = { ...before.sheet, choices: [status(['one', 'two'])] };
  const saved = viewOf('fixture-a', 'Fixture Avery');
  saved.character = { ...before.character, level: saveLevel, choices: stored(['three', 'four']) };
  saved.sheet = { ...saved.sheet, choices: [status(['three', 'four'])] };
  saved.notices = [{ code: 'choice.replaced-outside-window', message: "Choice 'technique' had 2 pick(s) replaced outside its window.", content: source }];
  return { before, saved };
}
const moreThanMax = 'Fixture Repertoire: you replaced 2 picks, more than the 1 it allows at once.';

it('a level-up save with the notice shows its words as the status (MECH-X item 2)', async () => {
  const user = userEvent.setup();
  const { before, saved } = replacedViews(2);
  vi.mocked(client.saveCharacter).mockResolvedValue(saved);
  const { save } = await toSaveLevelUp(user, before);
  await user.click(save);
  expect(await screen.findByText(moreThanMax)).toBeTruthy();
});

it('a level-up save that reset a companion adds that notice to the status (MECH-X R69)', async () => {
  const user = userEvent.setup();
  const { before, saved } = replacedViews(2);
  const pruned = "A companion's hit points were reset, because the content that gives it is no longer active.";
  saved.notices = [...(saved.notices ?? []), { code: 'companion.play-pruned', message: pruned }];
  vi.mocked(client.saveCharacter).mockResolvedValue(saved);
  const { save } = await toSaveLevelUp(user, before);
  await user.click(save);
  expect(await screen.findByText(`${moreThanMax} ${pruned}`)).toBeTruthy();
});

it('a choices-mode save with the notice shows its words as the status (MECH-X item 2)', async () => {
  const user = userEvent.setup();
  const { before, saved } = replacedViews(1);
  before.sheet.choices![0]!.resolved = false; // so the sheet offers Make choices
  vi.mocked(client.getCharacter).mockResolvedValue(before);
  vi.mocked(client.saveCharacter).mockResolvedValue(saved);
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await user.click(await screen.findByRole('button', { name: 'Make choices' }));
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));
  expect(await screen.findByText(moreThanMax)).toBeTruthy();
});

it('a save that lands after the user left the builder adds the notice to the Saved status (MECH-X item 2)', async () => {
  const user = userEvent.setup();
  const { before, saved } = replacedViews(2);
  let finish: (view: CharacterView) => void = () => {};
  vi.mocked(client.saveCharacter).mockReturnValue(new Promise<CharacterView>((r) => (finish = r)));
  const { list, save } = await toSaveLevelUp(user, before);
  await user.click(save);
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await screen.findByRole('heading', { name: 'Fixture Avery' });
  finish(saved);
  expect(await screen.findByText(`Saved Fixture Avery. ${moreThanMax}`)).toBeTruthy();
});

it('a save with no notice shows no notice text, and a create has none (MECH-X item 2)', async () => {
  const user = userEvent.setup();
  const { before, saved } = replacedViews(2);
  saved.notices = undefined;
  vi.mocked(client.saveCharacter).mockResolvedValue(saved);
  const { save } = await toSaveLevelUp(user, before);
  await user.click(save);
  await screen.findByRole('region', { name: 'What changed at level 2' });
  expect(screen.queryByText(/outside its window|more than the/)).toBeNull();
});

it('a throw while wording the notice never blocks the screen switch; the generic sentence stands in (MECH-X item 2, R39)', async () => {
  const user = userEvent.setup();
  const { before, saved } = replacedViews(2);
  vi.mocked(client.saveCharacter).mockResolvedValue(saved);
  const { save } = await toSaveLevelUp(user, before);
  vi.mocked(noticeText).mockImplementationOnce(() => {
    throw new Error('fixture wording failure');
  });
  await user.click(save);
  expect(await screen.findByRole('region', { name: 'What changed at level 2' })).toBeTruthy();
  expect(screen.getByText('A choice had picks replaced outside its window.')).toBeTruthy();
  expect(screen.queryByRole('alert')).toBeNull();
});

it('the sidebar has an add button, then Characters, Library and App as headed lists in that order (Task 2.1)', async () => {
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Main' });
  await within(nav).findByRole('button', { name: /Fixture Avery/ });
  const headings = within(nav).getAllByRole('heading', { level: 2 }).map((h) => h.textContent);
  expect(headings).toEqual(['Characters', 'Library', 'App']);
  const lists = within(nav).getAllByRole('list');
  const names = (list: HTMLElement) => within(list).getAllByRole('button').map((b) => b.textContent?.replace(/\s+/g, ' ').trim());
  expect(names(lists[0]!)[0]).toBe('All characters');
  expect(names(lists[1]!)).toEqual(['Books and PDFs', 'Homebrew', 'Campaigns']);
  expect(names(lists[2]!)).toEqual(['Backups', 'Gap notes', 'Settings']);
  const buttons = within(nav).getAllByRole('button');
  expect(buttons[0]!.textContent).toBe('+ Add a character');
  for (const gone of ['Extensions', 'New character', /^Import package/, /^From a TomeStack file/]) expect(within(nav).queryByRole('button', { name: gone })).toBeNull();
});

it('"All characters" appears only with two or more characters', async () => {
  vi.mocked(client.listCharacters).mockResolvedValue([summary('fixture-a', 'Fixture Avery')]);
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Main' });
  await within(nav).findByRole('button', { name: /Fixture Avery/ });
  expect(within(nav).queryByRole('button', { name: 'All characters' })).toBeNull();
});

it('aria-current follows the screen: Add, a Library entry, an App entry and a character', async () => {
  const user = userEvent.setup();
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Main' });
  const current = () => within(nav).queryAllByRole('button').filter((b) => b.getAttribute('aria-current') === 'page').map((b) => b.textContent);
  await within(nav).findByRole('button', { name: /Fixture Avery/ });
  await user.click(within(nav).getByRole('button', { name: '+ Add a character' }));
  expect(current()).toEqual(['+ Add a character']);
  await user.click(within(nav).getByRole('button', { name: 'Books and PDFs' }));
  expect(current()).toEqual(['Books and PDFs']);
  await user.click(within(nav).getByRole('button', { name: 'Settings' }));
  expect(current()).toEqual(['Settings']);
  await user.click(within(nav).getByRole('button', { name: 'All characters' }));
  expect(current()).toEqual(['All characters']);
  await user.click(within(nav).getByRole('button', { name: /Fixture Blake/ }));
  await waitFor(() => expect(current()).toHaveLength(1));
  expect(current()[0]).toMatch(/Fixture Blake/);
});

it('the add screen keeps the file import and the character-sheet import reachable', async () => {
  const user = userEvent.setup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: '+ Add a character' }));
  const picker = screen.getByLabelText('TomeStack file') as HTMLInputElement;
  const picked = vi.fn();
  picker.addEventListener('click', picked);
  await user.click(screen.getByRole('radio', { name: /^From a TomeStack file/ }));
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  expect(picked).toHaveBeenCalledTimes(1); // the chooser opened the file picker
  await user.click(screen.getByRole('radio', { name: /^From a character sheet/ }));
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  expect(await screen.findByRole('heading', { name: /From a character sheet/ })).toBeTruthy();
});

it('+ Add a character is enabled before app.info arrives and the file import works', async () => {
  const user = userEvent.setup();
  vi.mocked(client.info).mockReturnValue(new Promise(() => {}));
  render(<App />);
  const add = await screen.findByRole('button', { name: '+ Add a character' });
  expect((add as HTMLButtonElement).disabled).toBe(false);
  await user.click(add);
  const picker = screen.getByLabelText('TomeStack file');
  const picked = vi.fn();
  picker.addEventListener('click', picked);
  await user.click(screen.getByRole('radio', { name: /^From a TomeStack file/ }));
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  expect(picked).toHaveBeenCalledTimes(1);
});

it('the brand button returns home with one character: sheet, Settings, brand, list; nothing in the sidebar is current', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listCharacters).mockResolvedValue([summary('fixture-a', 'Fixture Avery')]);
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(await within(nav).findByRole('button', { name: /Fixture Avery/ }));
  await screen.findByRole('article', { name: 'Fixture Avery' });
  await user.click(within(nav).getByRole('button', { name: 'Settings' }));
  await user.click(screen.getByRole('button', { name: 'TomeStack, your characters' }));
  expect(await screen.findByRole('region', { name: 'Your characters' })).toBeTruthy();
  expect(within(nav).getAllByRole('button').filter((b) => b.getAttribute('aria-current') === 'page')).toHaveLength(0);
});

it('Esc on the builder leaves the draft and the screen alone', async () => {
  const user = userEvent.setup();
  render(<App />);
  await startNew(user);
  const name = await screen.findByRole('textbox', { name: 'Name' });
  await user.type(name, 'Fixture New');
  await user.keyboard('{Escape}');
  expect((screen.getByRole('textbox', { name: 'Name' }) as HTMLInputElement).value).toBe('Fixture New');
});

it('Esc closes the open Archived disclosure in the sidebar and returns focus to its summary', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listCharacters).mockResolvedValue([
    summary('fixture-a', 'Fixture Avery'),
    { ...summary('fixture-b', 'Fixture Blake'), archivedAt: '2026-01-01T00:00:00Z' } as CharacterSummary,
  ]);
  render(<App />);
  const nav = await screen.findByRole('navigation', { name: 'Main' });
  await user.click(await within(nav).findByText(/Archived \(1\)/));
  const link = await within(nav).findByRole('button', { name: /Fixture Blake/ });
  link.focus();
  await user.keyboard('{Escape}');
  expect(nav.querySelector('details')!.open).toBe(false);
  expect(document.activeElement).toBe(nav.querySelector('summary'));
});

it('on a narrow window a sheet starts with the sidebar hidden (nothing saved) and home shows it again', async () => {
  const user = userEvent.setup();
  const original = window.matchMedia;
  window.matchMedia = ((query: string) => ({ matches: true, media: query, addEventListener() {}, removeEventListener() {} })) as never;
  try {
    render(<App />);
    const nav = await screen.findByRole('navigation', { name: 'Main' });
    await user.click(await within(nav).findByRole('button', { name: /Fixture Avery/ }));
    await screen.findByRole('article', { name: 'Fixture Avery' });
    await waitFor(() => expect(nav.hidden).toBe(true));
    expect(screen.getByRole('button', { name: /Show sidebar/ })).toBeTruthy();
    expect(localStorage.getItem('tomestack.sidebar')).toBeNull();
    await user.click(screen.getByRole('button', { name: 'TomeStack, your characters' }));
    await waitFor(() => expect(nav.hidden).toBe(false));
  } finally {
    window.matchMedia = original;
  }
});
