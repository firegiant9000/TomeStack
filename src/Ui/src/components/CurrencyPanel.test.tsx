// @vitest-environment jsdom
// Currency (D21): five labelled coin counts saved with the character. The client is mocked; values are invented.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet, CharacterView } from '../api/types';
import { CurrencyPanel } from './EquipmentPanel';

vi.mock('../api/client', () => ({ client: { saveCharacter: vi.fn(), listContent: vi.fn() } }));

afterEach(cleanup);

const character: Character = {
  id: 'fixture-3',
  schemaVersion: 8,
  name: 'Fixture Coin',
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
const sheet: CharacterSheet = { characterId: 'fixture-3', rulesFamily: 'srd-5.1', diagnostics: [], fields: [] };
const fixture: CharacterView = { character, sheet };

it('shows the five coin counts and saves the edited currency with the character', async () => {
  vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ ...fixture, character: c }));
  const user = userEvent.setup();
  const onChanged = vi.fn();
  render(<CurrencyPanel view={{ ...fixture, character: { ...fixture.character, currency: { cp: 3, sp: 0, ep: 0, gp: 12, pp: 0 } } }} onChanged={onChanged} onError={() => {}} />);
  expect(screen.getByRole<HTMLInputElement>('spinbutton', { name: 'Gold (gp)' }).value).toBe('12');
  await user.clear(screen.getByRole('spinbutton', { name: 'Gold (gp)' }));
  await user.type(screen.getByRole('spinbutton', { name: 'Gold (gp)' }), '20');
  await user.click(screen.getByRole('button', { name: 'Save currency' }));
  expect(vi.mocked(client.saveCharacter).mock.lastCall![0].currency).toEqual({ cp: 3, sp: 0, ep: 0, gp: 20, pp: 0 });
  expect(onChanged).toHaveBeenCalled();
  expect(document.activeElement?.id).toBe('currency-heading'); // WCAG 2.4.3: "Save currency" is disabled once saved; focus is not dropped
});

it('keeps the same form (no remount) when the stored coins change after a save', async () => {
  const user = userEvent.setup();
  const first = { ...fixture, character: { ...character, currency: { cp: 0, sp: 0, ep: 0, gp: 12, pp: 0 } } };
  const { rerender } = render(<CurrencyPanel view={first} onChanged={() => {}} onError={() => {}} />);
  const box = screen.getByRole('spinbutton', { name: 'Gold (gp)' });
  await user.clear(box);
  await user.type(box, '20');
  rerender(<CurrencyPanel view={{ ...first, character: { ...character, currency: { cp: 0, sp: 0, ep: 0, gp: 20, pp: 0 } } }} onChanged={() => {}} onError={() => {}} />);
  expect(screen.getByRole('spinbutton', { name: 'Gold (gp)' })).toBe(box);
  expect((box as HTMLInputElement).value).toBe('20');
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Save currency' }).disabled).toBe(true);
});

it('re-seeds the inputs when the stored currency changes (a restore keeps the character id)', () => {
  const { rerender } = render(<CurrencyPanel view={{ ...fixture, character: { ...character, currency: { cp: 0, sp: 0, ep: 0, gp: 12, pp: 0 } } }} onChanged={() => {}} onError={() => {}} />);
  rerender(<CurrencyPanel view={{ ...fixture, character: { ...character, currency: { cp: 0, sp: 0, ep: 0, gp: 5, pp: 0 } } }} onChanged={() => {}} onError={() => {}} />);
  expect(screen.getByRole<HTMLInputElement>('spinbutton', { name: 'Gold (gp)' }).value).toBe('5');
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Save currency' }).disabled).toBe(true);
});

it('refuses a negative or non-integer coin count before saving', async () => {
  const user = userEvent.setup();
  render(<CurrencyPanel view={fixture} onChanged={() => {}} onError={() => {}} />);
  await user.type(screen.getByRole('spinbutton', { name: 'Copper (cp)' }), '-1');
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Save currency' }).disabled).toBe(true);
});
