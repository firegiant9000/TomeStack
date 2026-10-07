// @vitest-environment jsdom
// Session notes (D22): a dated journal saved with the character; delete behind one confirm click. Values are invented.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet, CharacterView } from '../api/types';
import { SessionNotesPanel } from './SessionNotesPanel';

vi.mock('../api/client', () => ({ client: { saveCharacter: vi.fn(), listContent: vi.fn() } }));

afterEach(cleanup);

const character: Character = {
  id: 'fixture-4',
  schemaVersion: 8,
  name: 'Fixture Journal',
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
const sheet: CharacterSheet = { characterId: 'fixture-4', rulesFamily: 'srd-5.1', diagnostics: [], fields: [] };
const fixture: CharacterView = { character, sheet };

it('keeps the typed note and reports the error when the save fails', async () => {
  vi.mocked(client.saveCharacter).mockRejectedValue(new Error('refused'));
  const user = userEvent.setup();
  const onError = vi.fn();
  render(<SessionNotesPanel view={fixture} onChanged={() => {}} onError={onError} onStatus={() => {}} />);
  const form = screen.getByRole('form', { name: 'New session note' });
  await user.type(within(form).getByRole('textbox', { name: 'Note' }), 'Fixture unsaved note.');
  await user.click(within(form).getByRole('button', { name: 'Save session note' }));
  expect(onError).toHaveBeenCalled();
  expect(within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Note' }).value).toBe('Fixture unsaved note.');
});

it('adds a dated note and lists it newest first, then deletes it after a confirm click', async () => {
  vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ ...fixture, character: c }));
  const user = userEvent.setup();
  const onChanged = vi.fn();
  const { rerender } = render(<SessionNotesPanel view={fixture} onChanged={onChanged} onError={() => {}} onStatus={() => {}} />);
  const form = screen.getByRole('form', { name: 'New session note' });
  await user.clear(within(form).getByLabelText('Session date'));
  await user.type(within(form).getByLabelText('Session date'), '2026-10-06');
  await user.type(within(form).getByRole('textbox', { name: 'Note' }), 'Fixture session one.');
  await user.click(within(form).getByRole('button', { name: 'Save session note' }));
  const saved = vi.mocked(client.saveCharacter).mock.lastCall![0];
  expect(saved.notes).toHaveLength(1);
  expect(saved.notes![0]).toMatchObject({ date: '2026-10-06', text: 'Fixture session one.' });
  rerender(<SessionNotesPanel view={{ ...fixture, character: saved }} onChanged={onChanged} onError={() => {}} onStatus={() => {}} />);
  const list = screen.getByRole('list', { name: 'Session notes' });
  expect(within(list).getAllByRole('listitem')).toHaveLength(1);
  await user.click(within(list).getByRole('button', { name: 'Delete session note from 2026-10-06' }));
  await user.click(within(list).getByRole('button', { name: 'Confirm delete' }));
  expect(vi.mocked(client.saveCharacter).mock.lastCall![0].notes).toEqual([]);
});
