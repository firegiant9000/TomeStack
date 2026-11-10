import { expect, it } from 'vitest';
import type { AttackModeValue, RollModeReason } from './api/types';
import { attackModeFor } from './rollModes';

const reason = (mode: RollModeReason['mode'], source: string, automatic = true): RollModeReason => ({ mode, source, automatic }) as RollModeReason;
const value = (r: RollModeReason, tags: string[], ability?: AttackModeValue['ability']): AttackModeValue => ({ reason: r, tags, ability });

it('attackModeFor matches the all tag', () => {
  const m = value(reason('advantage', 'A'), ['all']);
  expect(attackModeFor([m], ['spell', 'ranged'], 'int')).toEqual({ automatic: 'advantage', reasons: [m.reason] });
});

it('attackModeFor matches a shared tag and not another', () => {
  const m = value(reason('disadvantage', 'B'), ['melee']);
  expect(attackModeFor([m], ['spell', 'melee'], 'int')?.automatic).toBe('disadvantage');
  expect(attackModeFor([m], ['spell', 'ranged'], 'int')).toBeUndefined();
});

it('attackModeFor needs the ability when the mode names one', () => {
  const m = value(reason('advantage', 'C'), ['all'], 'int');
  expect(attackModeFor([m], ['spell'], 'int')?.automatic).toBe('advantage');
  expect(attackModeFor([m], ['spell'], 'wis')).toBeUndefined();
});

it('attackModeFor cancels advantage and disadvantage, and ignores non-automatic reasons in the combination', () => {
  const a = value(reason('advantage', 'A'), ['all']);
  const d = value(reason('disadvantage', 'D'), ['all']);
  const n = value(reason('advantage', 'N', false), ['all']);
  expect(attackModeFor([a, d], ['spell'], 'int')?.automatic).toBe('normal');
  expect(attackModeFor([n], ['spell'], 'int')).toEqual({ automatic: 'normal', reasons: [n.reason] });
});

it('attackModeFor is undefined with no modes', () => {
  expect(attackModeFor(undefined, ['spell'], 'int')).toBeUndefined();
  expect(attackModeFor([], ['spell'], 'int')).toBeUndefined();
});
