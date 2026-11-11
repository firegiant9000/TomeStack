import type { RulesFamilyPolicy } from './api/types';

/** Hit dice pools as "2 of 3 (d12)", joined by ", "; the one format used on the summary and the Hit points panel. */
export function hitDiceText(dice: { die: number; remaining: number; total: number }[]): string {
  return dice.map((h) => `${h.remaining} of ${h.total} (d${h.die})`).join(', ');
}

/** The one name for the inspiration toggle and its summary line, from the family's policy (`inspirationName`); "Inspiration" when the family is not in the list. */
export function inspirationLabel(families: RulesFamilyPolicy[], rulesFamily: string): string {
  return families.find((f) => f.id === rulesFamily)?.inspirationName ?? 'Inspiration';
}

/** A roll's flat bonus after its dice, with the sign spelled out: " + 3", " − 1" (minus sign, U+2212), or nothing. */
export function rollBonus(bonus: number | null | undefined): string {
  if (!bonus) return '';
  return bonus > 0 ? ` + ${bonus}` : ` − ${-bonus}`;
}
