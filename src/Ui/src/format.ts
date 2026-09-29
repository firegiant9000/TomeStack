/** A roll's flat bonus after its dice, with the sign spelled out: " + 3", " − 1" (minus sign, U+2212), or nothing. */
export function rollBonus(bonus: number | null | undefined): string {
  if (!bonus) return '';
  return bonus > 0 ? ` + ${bonus}` : ` − ${-bonus}`;
}
