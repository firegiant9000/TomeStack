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
