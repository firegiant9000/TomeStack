import { describe, expect, it } from 'vitest';
import { rollBonus } from './format';

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
