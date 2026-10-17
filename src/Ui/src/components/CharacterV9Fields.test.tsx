// @vitest-environment jsdom
// Schema v9 fields the UI does not edit survive a save from another panel (every save spreads the loaded character). Values are invented.
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet, CharacterView } from '../api/types';
import { CurrencyPanel } from './EquipmentPanel';
import { SessionNotesPanel } from './SessionNotesPanel';

vi.mock('../api/client', () => ({ client: { saveCharacter: vi.fn(), listContent: vi.fn() } }));

afterEach(cleanup);

const character: Character = {
  id: 'fixture-v9',
  schemaVersion: 9,
  name: 'Fixture Keeper',
  rulesFamily: 'srd-5.1',
  level: 1,
  classes: [],
  choices: [],
  crossFamilyExceptions: [],
  baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
  pins: [],
  overrides: [],
  details: { alignment: 'Fixture neutral', appearance: { hair: 'Fixture auburn' }, languages: ['Fixture tongue'] },
  customAttacks: [{ id: 'atk-1', name: 'Fixture Jab', toHit: 4, damage: '1d4', damageType: 'piercing' }],
  customItems: [{ id: 'itm-1', name: 'Fixture rope', quantity: 1, equipped: false }],
  damageDefences: [{ type: 'fire', kind: 'resistance' }],
  companions: [{ id: 'cmp-1', name: 'Fixture Hound', hitPointMaximum: 5 }],
  xp: 300,
  updatedAt: '2026-10-16T00:00:00Z',
};
const sheet: CharacterSheet = { characterId: 'fixture-v9', rulesFamily: 'srd-5.1', diagnostics: [], fields: [] };
const view: CharacterView = { character, sheet };

it('keeps details and custom attacks (and the other v9 fields) when currency is saved', async () => {
  vi.mocked(client.saveCharacter).mockResolvedValue(view);
  const user = userEvent.setup();
  render(<CurrencyPanel view={view} onChanged={() => {}} onError={() => {}} />);
  const gold = screen.getByRole('spinbutton', { name: 'Gold (gp)' });
  await user.clear(gold);
  await user.type(gold, '7');
  await user.click(screen.getByRole('button', { name: 'Save currency' }));
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalled());
  const sent = vi.mocked(client.saveCharacter).mock.calls[0]![0];
  expect(sent.currency?.gp).toBe(7);
  expect(sent.details).toEqual(character.details);
  expect(sent.customAttacks).toEqual(character.customAttacks);
  expect(sent.customItems).toEqual(character.customItems);
  expect(sent.damageDefences).toEqual(character.damageDefences);
  expect(sent.companions).toEqual(character.companions);
  expect(sent.xp).toBe(300);
});

it('keeps details and custom attacks when a session note is saved', async () => {
  vi.mocked(client.saveCharacter).mockResolvedValue(view);
  const user = userEvent.setup();
  render(<SessionNotesPanel view={view} onChanged={() => {}} onError={() => {}} onStatus={() => {}} />);
  await user.type(screen.getByRole('textbox', { name: 'Note' }), 'Fixture note.');
  await user.click(screen.getByRole('button', { name: 'Save session note' }));
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalled());
  const sent = vi.mocked(client.saveCharacter).mock.calls.at(-1)![0];
  expect(sent.details).toEqual(character.details);
  expect(sent.customAttacks).toEqual(character.customAttacks);
});
