/** Hit dice pools as "2 of 3 (d12)", joined by ", "; the one format used on the summary and the Hit points panel. */
export function hitDiceText(dice: { die: number; remaining: number; total: number }[]): string {
  return dice.map((h) => `${h.remaining} of ${h.total} (d${h.die})`).join(', ');
}

/** A roll's flat bonus after its dice, with the sign spelled out: " + 3", " − 1" (minus sign, U+2212), or nothing. */
export function rollBonus(bonus: number | null | undefined): string {
  if (!bonus) return '';
  return bonus > 0 ? ` + ${bonus}` : ` − ${-bonus}`;
}
