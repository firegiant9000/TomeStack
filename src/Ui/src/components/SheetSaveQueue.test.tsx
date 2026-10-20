// @vitest-environment jsdom
// R92/R132: every sheet save goes through one queue in the sheet, each as an updater applied to the latest saved character, so
// two panels that save close together both keep their change. The client is mocked; values are invented.
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet as SheetModel, CharacterView } from '../api/types';
import { CharacterSheet } from './CharacterSheet';

vi.mock('../api/client', () => ({
  client: {
    saveCharacter: vi.fn(),
    attachment: vi.fn(),
    listGapNotes: vi.fn(),
    availableUpdates: vi.fn(),
    listContent: vi.fn(),
    snapshots: vi.fn(),
    previewExport: vi.fn(),
    info: vi.fn(),
    play: vi.fn(),
    roll: vi.fn(),
    rollDice: vi.fn(),
    restPreview: vi.fn(),
    rest: vi.fn(),
  },
}));

beforeEach(() => {
  vi.mocked(client.listGapNotes).mockResolvedValue([]);
  vi.mocked(client.availableUpdates).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.snapshots).mockResolvedValue({ items: [], hasMore: false });
});
afterEach(() => {
  cleanup();
  localStorage.clear();
  vi.mocked(client.saveCharacter).mockReset();
});

const character: Character = {
  id: 'fixture-9',
  schemaVersion: 8,
  name: 'Fixture Queue',
  rulesFamily: 'srd-5.1',
  level: 1,
  classes: [],
  choices: [],
  crossFamilyExceptions: [],
  baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
  pins: [],
  overrides: [],
  updatedAt: '2026-10-06T00:00:00Z',
};
const sheet: SheetModel = { characterId: 'fixture-9', rulesFamily: 'srd-5.1', diagnostics: [], fields: [] };

const noop = () => {};

function Harness({ onShown }: { onShown: (view: CharacterView) => void }) {
  const [shown, setShown] = useState<CharacterView>({ character, sheet });
  return (
    <CharacterSheet
      view={shown}
      onChanged={(next) => {
        onShown(next);
        setShown(next);
      }}
      onError={noop}
      onStatus={noop}
      onLevelUp={noop}
      onMakeChoices={noop}
      onArchiveChanged={noop}
    />
  );
}

it('keeps both changes when two panels save close together (R132)', async () => {
  // Each save answers when the test says so, echoing what it was sent with a later updatedAt, as the service does.
  const settle: Array<() => void> = [];
  let saves = 0;
  vi.mocked(client.saveCharacter).mockImplementation(
    (sent) =>
      new Promise<CharacterView>((resolve) => {
        saves += 1;
        const stamp = `2026-10-06T00:00:0${saves}Z`;
        settle.push(() => resolve({ character: { ...sent, updatedAt: stamp }, sheet }));
      }),
  );
  const shown: CharacterView[] = [];
  render(<Harness onShown={(v) => shown.push(v)} />);

  // The Currency panel (Inventory tab) saves 5 gp; before it answers, the Session notes panel (Notes tab) saves a note.
  fireEvent.change(screen.getByLabelText('Gold (gp)', { selector: 'input' }), { target: { value: '5' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save currency', hidden: true }));
  fireEvent.change(screen.getByLabelText('Note', { selector: 'textarea', exact: true }), { target: { value: 'Fixture note one.' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save session note', hidden: true }));

  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(1)); // the second waits its turn
  await act(async () => settle[0]!());
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(2));
  await act(async () => settle[1]!());
  await waitFor(() => expect(shown).toHaveLength(2));

  const second = vi.mocked(client.saveCharacter).mock.calls[1]![0];
  expect(second.currency?.gp).toBe(5); // the first panel's change was not overwritten by the second's props snapshot
  expect(second.notes?.map((n) => n.text)).toEqual(['Fixture note one.']);
  expect(shown[1]!.character.currency?.gp).toBe(5);
  expect(shown[1]!.character.notes).toHaveLength(1);
});

it('gives each panel its own failure and carries on with the next save (R132)', async () => {
  vi.mocked(client.saveCharacter)
    .mockRejectedValueOnce(new Error('fixture refusal'))
    .mockImplementation(async (sent) => ({ character: { ...sent, updatedAt: '2026-10-06T00:00:09Z' }, sheet }));
  const errors: unknown[] = [];
  const shown: CharacterView[] = [];
  render(
    <CharacterSheet
      view={{ character, sheet }}
      onChanged={(v) => shown.push(v)}
      onError={(e) => errors.push(e)}
      onStatus={noop}
      onLevelUp={noop}
      onMakeChoices={noop}
      onArchiveChanged={noop}
    />,
  );
  fireEvent.change(screen.getByLabelText('Gold (gp)', { selector: 'input' }), { target: { value: '5' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save currency', hidden: true }));
  fireEvent.change(screen.getByLabelText('Note', { selector: 'textarea', exact: true }), { target: { value: 'Fixture note two.' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save session note', hidden: true }));

  await waitFor(() => expect(shown).toHaveLength(1));
  expect(errors).toHaveLength(1); // the currency panel's own failure
  const second = vi.mocked(client.saveCharacter).mock.calls[1]![0];
  expect(second.currency).toBeUndefined(); // the failed save left nothing behind
  expect(second.notes).toHaveLength(1);
});
