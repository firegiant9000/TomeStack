// @vitest-environment jsdom
// A late reply for one character must not switch the screen once the user has moved on (checkpoint A review, R26).
// The client is mocked; names and values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from './App';
import { client } from './api/client';
import type { Character, CharacterSummary, CharacterView } from './api/types';

vi.mock('./api/client', () => ({
  client: {
    info: vi.fn(),
    listCharacters: vi.fn(),
    getCharacter: vi.fn(),
    attachment: vi.fn(),
    listGapNotes: vi.fn(),
    availableUpdates: vi.fn(),
    listContent: vi.fn(),
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
  vi.mocked(client.listCampaigns).mockResolvedValue([]);
  vi.mocked(client.snapshots).mockResolvedValue({ items: [], hasMore: false });
});
afterEach(() => {
  cleanup();
  localStorage.clear();
});

it('Cancel from a level-up shows the character as it is now, not as it was when the builder opened (carry 1)', async () => {
  const user = userEvent.setup();
  render(<App />);
  const list = await screen.findByRole('navigation', { name: 'Characters' });
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
  const list = await screen.findByRole('navigation', { name: 'Characters' });
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await user.click(within(list).getByRole('button', { name: /Fixture Blake/ }));
  replies['fixture-b']!(b);
  await waitFor(() => expect(within(list).getByRole('button', { name: /Fixture Blake/ }).getAttribute('aria-current')).toBe('page'));
  replies['fixture-a']!(a); // the older request answers last
  await new Promise((r) => setTimeout(r, 20));
  expect(within(list).getByRole('button', { name: /Fixture Blake/ }).getAttribute('aria-current')).toBe('page');
  expect(within(list).getByRole('button', { name: /Fixture Avery/ }).getAttribute('aria-current')).toBeNull();
});

/** Walks the create steps by hand (no content installed) up to "Create and save". */
async function toCreate(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'New character' }));
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
  await user.click(screen.getByRole('button', { name: 'Characters' })); // leaves the builder while the save is in flight
  await screen.findByRole('region', { name: 'Characters' });
  finish(created);
  expect(await screen.findByText('Saved Fixture New.')).toBeTruthy();
  expect(vi.mocked(client.listCharacters).mock.calls.length).toBeGreaterThan(lists);
  expect(screen.getByRole('region', { name: 'Characters' })).toBeTruthy(); // still the home screen
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
  const list = await screen.findByRole('navigation', { name: 'Characters' });
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

it('pressing New character during a save mounts a fresh empty builder; the save still reports, and the old draft is gone (R40)', async () => {
  const user = userEvent.setup();
  const created = viewOf('fixture-new', 'Fixture New');
  let finish: (view: CharacterView) => void = () => {};
  vi.mocked(client.preview).mockResolvedValue(created);
  vi.mocked(client.createCharacter).mockReturnValue(new Promise<CharacterView>((r) => (finish = r)));
  render(<App />);
  const create = await toCreate(user);
  await user.click(create);
  await user.click(screen.getByRole('button', { name: 'New character' })); // a new builder mode while the save is in flight
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
  await user.click(screen.getByRole('button', { name: 'Characters' }));
  await screen.findByRole('region', { name: 'Characters' });
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
  const list = await screen.findByRole('navigation', { name: 'Characters' });
  await user.click(within(list).getByRole('button', { name: /Fixture Avery/ }));
  await user.click(await screen.findByRole('button', { name: 'New character' }));
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
  const list = await screen.findByRole('navigation', { name: 'Characters' });
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
