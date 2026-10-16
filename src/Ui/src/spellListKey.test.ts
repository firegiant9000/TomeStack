import { expect, it } from 'vitest';
import { isValidSpellListKey, normaliseSpellListKey, spellListKeyProblem } from './spellListKey';

it('trims, lower-cases and turns each run of spaces into one hyphen, as the service does', () => {
  expect(normaliseSpellListKey('  Fixture   Seer ')).toBe('fixture-seer');
  expect(normaliseSpellListKey('WIZARD')).toBe('wizard');
  expect(normaliseSpellListKey('a\tb')).toBe('a-b');
  expect(normaliseSpellListKey('')).toBe('');
});

it('accepts 1 to 60 letters, digits and hyphens only', () => {
  expect(isValidSpellListKey('wizard')).toBe(true);
  expect(isValidSpellListKey('fixture-seer-2')).toBe(true);
  expect(isValidSpellListKey('a'.repeat(60))).toBe(true);
  expect(isValidSpellListKey('a'.repeat(61))).toBe(false);
  expect(isValidSpellListKey('')).toBe(false);
  expect(isValidSpellListKey('Wizard')).toBe(false);
  expect(isValidSpellListKey("seer's")).toBe(false);
});

it('words the problem with a key, or says there is none', () => {
  expect(spellListKeyProblem('   ')).toBe('Type a spell list key.');
  expect(spellListKeyProblem("Seer's List")).toBe('A spell list key is 1 to 60 letters, digits or hyphens.');
  expect(spellListKeyProblem('Fixture Seer')).toBeUndefined();
});
