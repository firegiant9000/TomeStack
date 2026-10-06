import { describe, expect, it } from 'vitest';
import { hitDiceText, rollBonus } from './format';

describe('hitDiceText', () => {
  it('writes each pool as remaining of total (die), joined by commas', () => {
    expect(hitDiceText([{ die: 12, remaining: 2, total: 3 }])).toBe('2 of 3 (d12)');
    expect(
      hitDiceText([
        { die: 8, remaining: 1, total: 2 },
        { die: 10, remaining: 4, total: 4 },
      ]),
    ).toBe('1 of 2 (d8), 4 of 4 (d10)');
    expect(hitDiceText([])).toBe('');
  });
});

describe('rollBonus', () => {
  it('spells out the sign of a roll bonus', () => {
    expect(rollBonus(3)).toBe(' + 3');
    expect(rollBonus(-1)).toBe(' − 1');
  });

  it('shows nothing when there is no bonus', () => {
    expect(rollBonus(0)).toBe('');
    expect(rollBonus(null)).toBe('');
    expect(rollBonus(undefined)).toBe('');
  });
});
