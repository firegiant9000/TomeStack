import { expect, it } from 'vitest';
import { assignmentComplete, assignmentValid, pointBuyCost, pointsSpent, scoresFrom, standardArray } from './abilityScores';

it('prices point buy by the 27-point table', () => {
  expect([8, 9, 10, 11, 12, 13, 14, 15].map(pointBuyCost)).toEqual([0, 1, 2, 3, 4, 5, 7, 9]);
  expect(pointBuyCost(16)).toBeNaN();
  expect(pointsSpent({ str: 15, dex: 15, con: 15, int: 8, wis: 8, cha: 8 })).toBe(27);
  expect(pointsSpent({ str: 15, dex: 14, con: 13, int: 12, wis: 10, cha: 8 })).toBe(27);
});

it('accepts an assignment only when it uses the pool as a multiset', () => {
  expect(assignmentValid({ str: 15, dex: 14 }, standardArray)).toBe(true);
  expect(assignmentValid({ str: 15, dex: 15 }, standardArray)).toBe(false);
  expect(assignmentValid({ str: 12, dex: 12 }, [12, 12, 10, 9, 9, 8])).toBe(true);
  expect(assignmentValid({ str: 16 }, standardArray)).toBe(false);
  expect(assignmentComplete({ str: 15, dex: 14, con: 13, int: 12, wis: 10 })).toBe(false);
  expect(scoresFrom({ str: 15, dex: 14, con: 13, int: 12, wis: 10, cha: 8 })).toEqual({ str: 15, dex: 14, con: 13, int: 12, wis: 10, cha: 8 });
});

it('returns NaN from pointsSpent when a score is out of range, so callers must check the range', () => {
  expect(pointsSpent({ str: 16, dex: 8, con: 8, int: 8, wis: 8, cha: 8 })).toBeNaN();
  expect(pointsSpent({ str: 7, dex: 8, con: 8, int: 8, wis: 8, cha: 8 })).toBeNaN();
});

it('treats an empty assignment as valid and a complete one as complete', () => {
  expect(assignmentValid({}, standardArray)).toBe(true);
  expect(assignmentComplete({})).toBe(false);
  expect(assignmentComplete({ str: 15, dex: 14, con: 13, int: 12, wis: 10, cha: 8 })).toBe(true);
});

it('rejects a value used more often than the pool holds it', () => {
  expect(assignmentValid({ str: 12, dex: 12, con: 12 }, [12, 12, 10, 9, 9, 8])).toBe(false);
  expect(assignmentValid({ str: 9, dex: 9, con: 9 }, [12, 12, 10, 9, 9, 8])).toBe(false);
});
