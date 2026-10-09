import type { Ability, AbilityScores } from './api/types';

export type ScoreMethod = 'array' | 'pointBuy' | 'roll' | 'manual';
export const scoreMethods: { id: ScoreMethod; label: string }[] = [
  { id: 'array', label: 'Standard array' },
  { id: 'pointBuy', label: 'Point buy' },
  { id: 'roll', label: 'Roll' },
  { id: 'manual', label: 'Enter by hand' },
];
export const abilityKeys: Ability[] = ['str', 'dex', 'con', 'int', 'wis', 'cha'];
export const standardArray: readonly number[] = [15, 14, 13, 12, 10, 8];
export const pointBuyBudget = 27;
export const pointBuyRange = { min: 8, max: 15 } as const;
const costs: Record<number, number> = { 8: 0, 9: 1, 10: 2, 11: 3, 12: 4, 13: 5, 14: 7, 15: 9 };
export const pointBuyCost = (score: number): number => costs[score] ?? NaN;
export const pointsSpent = (scores: AbilityScores): number => abilityKeys.reduce((sum, k) => sum + pointBuyCost(scores[k]), 0);

export type Assignment = Partial<Record<Ability, number>>;
export const assignmentComplete = (a: Assignment): boolean => abilityKeys.every((k) => a[k] !== undefined);
export function assignmentValid(a: Assignment, pool: readonly number[]): boolean {
  const left = [...pool];
  for (const k of abilityKeys) {
    const v = a[k];
    if (v === undefined) continue;
    const i = left.indexOf(v);
    if (i < 0) return false;
    left.splice(i, 1);
  }
  return true;
}
export const scoresFrom = (a: Assignment): AbilityScores => ({ str: a.str!, dex: a.dex!, con: a.con!, int: a.int!, wis: a.wis!, cha: a.cha! });
