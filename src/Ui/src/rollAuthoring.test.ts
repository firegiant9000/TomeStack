import { expect, it } from 'vitest';
import type { Effect } from './api/types';
import { isValueKey, nextValueKey, parseAreaSize, parseAreaWidth, parseMaxUnits, parsePerUnitDice, parseRollText, poolValue } from './rollAuthoring';

it('an area size is a whole number from 1 to 1000 and is required; a width is optional', () => {
  expect(parseAreaSize('100')).toEqual({ size: 100 });
  for (const bad of ['', '0', '1001', '2.5', '1e2', '-3', 'ten']) expect(parseAreaSize(bad).problem).toBeTruthy();
  expect(parseAreaWidth('')).toEqual({});
  expect(parseAreaWidth('5')).toEqual({ width: 5 });
  expect(parseAreaWidth('0').problem).toBeTruthy();
});

it('max units are 1 to 100 or empty; per-unit dice are NdM with a d4, d6, d8, d10 or d12', () => {
  expect(parseMaxUnits('')).toEqual({});
  expect(parseMaxUnits('100')).toEqual({ maxUnits: 100 });
  expect(parseMaxUnits('101').problem).toBeTruthy();
  expect(parsePerUnitDice('')).toEqual({});
  expect(parsePerUnitDice('2d8')).toEqual({ dice: '2d8' });
  for (const bad of ['1d20', 'd6', '0d6', '101d6', '1d6+1', ' 1d6']) expect(parsePerUnitDice(bad).problem).toBeTruthy();
});

it('range and targets are at most 200 characters', () => {
  expect(parseRollText('')).toEqual({});
  expect(parseRollText('a'.repeat(200)).problem).toBeUndefined();
  expect(parseRollText('a'.repeat(201)).problem).toBeTruthy();
});

it('a pool option is the plain id for this revision and content::id for another entry', () => {
  expect(poolValue(undefined, 'focus')).toBe('focus');
  expect(poolValue('c1', 'focus')).toBe('c1::focus');
});

it('a value key follows the scale-key rule, and the next key skips the used ones', () => {
  expect(isValueKey('surgeDice')).toBe(true);
  for (const bad of ['', '9a', 'Abc', 'a_b', 'a'.repeat(33)]) expect(isValueKey(bad)).toBe(false);
  const used = [{ type: 'value', id: 'v', valueId: 'value1', formula: '1' }, { type: 'value', id: 'w', valueId: 'value3', formula: '1' }] as Effect[];
  expect(nextValueKey([])).toBe('value1');
  expect(nextValueKey(used)).toBe('value2');
});
