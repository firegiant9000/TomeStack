import { expect, it } from 'vitest';
import type { RollRecord } from './api/types';
import { diceLine, rollHeadline } from './rollText';

const record = (over: Partial<RollRecord> = {}): RollRecord => ({
  formula: '1d20+3', mode: 'advantage', critical: false,
  dice: [{ term: 0, sides: 20, value: 14, kept: true, fromCritical: false }, { term: 0, sides: 20, value: 7, kept: false, fromCritical: false }],
  diceTotal: 14, expressionConstant: 0, modifiers: [{ label: 'Strength', amount: 3 }], total: 17,
  provenance: { rollId: 'ability.str.mod', label: 'Strength check' }, ...over,
});

it('writes the headline as the Last roll region does', () => {
  expect(rollHeadline(record())).toBe('Strength check: 17 (1d20+3, advantage)');
  expect(rollHeadline(record({ mode: 'normal', critical: true, formula: '2d6' }))).toBe('Strength check: 17 (2d6, critical)');
  expect(rollHeadline(record({ provenance: undefined }))).toBe('Roll: 17 (1d20+3, advantage)');
  expect(rollHeadline(record({ provenance: { rollId: 'fixture', label: '' } }))).toBe('Roll: 17 (1d20+3, advantage)');
});

it('lists every die, the constant and the modifiers', () => {
  expect(diceLine(record({ expressionConstant: 2 }))).toBe('Dice: d20 14, d20 7 (dropped) · constant 2 · Strength +3');
});
