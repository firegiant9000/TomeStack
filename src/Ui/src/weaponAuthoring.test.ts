import { describe, expect, it } from 'vitest';
import { bonusText, diceProblem, parseBonus, parseOptionalDice, parseProperties, parseRange, parseWeaponDice, parseWeaponKey, weaponReads, type Weapon } from './weaponAuthoring';

// The table follows DiceTests.cs in RulesCore.Tests: what the service reads, the reading here must read.
const goodDice = ['d6', '1d8', '1d8+1', '1d8+1d6+2', ' 1D8 + 1 ', '-1d6', '+2d6', '100d6', '1d1000', '1d4+1000', '1d4+1d4+1d4+1d4+1d4+1d4+1d4+1'];
const badDice = ['', '   ', 'd', '1d', '0d6', '5', '1d20+', '1d20++2', '1d20*2', '1d1', '1d100000', '101d6', '99999999d6', '60d6+60d6', '1d20+5000', '1d4+1d4+1d4+1d4+1d4+1d4+1d4+1d4+1', '1d20+PB', '1d20; rm', '1d20+1d20+1d20+1d20+1d20+1d20+1d20+1d20+1', '1d', 'x', '1.5', '1d6.5'];

describe('weapon dice read as the service reads them', () => {
  it.each(goodDice)('reads %j', (text) => {
    expect(diceProblem(text)).toBeUndefined();
  });
  it.each(badDice)('refuses %j', (text) => {
    expect(diceProblem(text)).toBeTruthy();
  });
  it('required dice need text, optional dice may be empty', () => {
    expect(parseWeaponDice('').problem).toBeTruthy();
    expect(parseWeaponDice(' 1d8 ').value).toBe('1d8');
    expect(parseOptionalDice('')).toEqual({ value: undefined });
    expect(parseOptionalDice('d10')).toEqual({ value: 'd10' });
    expect(parseOptionalDice('1d').problem).toBeTruthy();
  });
});

describe('bonus', () => {
  it.each(['1.5', '-1', '11', '+2', '2a', 'x', '1e1', '0.0'])('refuses %j with a problem', (text) => {
    expect(parseBonus(text).problem).toBeTruthy();
  });
  it('blank and 0 are none, with no problem (R23)', () => {
    expect(parseBonus('')).toEqual({ value: undefined });
    expect(parseBonus('0')).toEqual({ value: undefined });
  });
  it('reads 1 to 10', () => {
    expect(parseBonus('10')).toEqual({ value: 10 });
    expect(parseBonus(' 2 ')).toEqual({ value: 2 });
  });
  it('shows a stored value as it is', () => {
    expect(bonusText(undefined)).toBe('');
    expect(bonusText(2)).toBe('2');
    expect(bonusText('2')).toBe('2');
    expect(bonusText('x')).toBe('x');
    expect(bonusText(1.5)).toBe('1.5');
  });
});

describe('other fields', () => {
  it('properties are a comma list that never drops a token', () => {
    expect(parseProperties('finesse, light').value).toEqual(['finesse', 'light']);
    expect(parseProperties('').value).toEqual([]);
    expect(parseProperties('light,,finesse').problem).toBeTruthy();
    expect(parseProperties('light, ,finesse').problem).toBeTruthy();
    expect(parseProperties('light,').value).toEqual(['light']);
  });
  it('range is free text, empty is none', () => {
    expect(parseRange('').value).toBeUndefined();
    expect(parseRange('reach').value).toBe('reach');
    expect(parseRange('80/').problem).toBeUndefined();
  });
  it('the weapon key is required', () => {
    expect(parseWeaponKey('  ').problem).toBeTruthy();
    expect(parseWeaponKey(' blade ').value).toBe('blade');
  });
});

describe('weaponReads', () => {
  const base = { type: 'weapon', id: 'weapon-1', category: 'simple', attack: 'melee', damage: '1d6', damageType: 'slashing', weaponKey: 'blade' } as Weapon;
  it('reads a whole weapon', () => {
    expect(weaponReads(base)).toBe(true);
    expect(weaponReads({ ...base, properties: ['light'], versatile: 'd8', range: 'reach', mastery: 'm' })).toBe(true);
  });
  it.each([{ category: 'exotic' }, { attack: 'thrown' }, { properties: 'light' }, { damage: undefined }, { weaponKey: 5 }, { versatile: 4 }])('does not read %j', (patch) => {
    expect(weaponReads({ ...base, ...patch } as unknown as Weapon)).toBe(false);
  });
});
