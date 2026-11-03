import type { Effect } from './api/types';

/**
 * Content v12 (MECH-X item 0): strict reading of what an author types for an item's weapon. Nothing is coerced: text that
 * breaks a rule is a problem the editor shows next to the field, and it blocks saving. No reading is stricter than the
 * service (ContentValidation stays the authority), so a stored legal value never locks saving.
 */

export type Parsed<T> = { value: T; problem?: string };

export type Weapon = Extract<Effect, { type: 'weapon' }>;

// Mirrors DiceLimits in RulesCore/Dice.cs.
const maxLength = 40;
const maxTerms = 8;
const maxDice = 100;
const minSides = 2;
const maxSides = 1000;
const maxConstant = 1000;

/** Up to 4 digits are read; anything longer is out of every range (the service's ReadNumber). */
function readNumber(text: string, at: number): { value: number | undefined; next: number } {
  let i = at;
  while (i < text.length && text[i]! >= '0' && text[i]! <= '9') i++;
  if (i === at) return { value: undefined, next: i };
  return { value: i - at > 4 ? Number.MAX_SAFE_INTEGER : Number(text.slice(at, i)), next: i };
}

/**
 * The problem with a dice expression, or undefined when it reads: the grammar of DiceExpression.TryParse. Spaces are ignored,
 * case does not matter, a leading sign is allowed, and at least one die is needed (a bare number is not dice).
 */
export function diceProblem(source: string): string | undefined {
  if (source.trim() === '') return 'the dice are empty';
  if (source.length > maxLength) return `the dice are ${source.length} characters; the limit is ${maxLength}`;
  const text = source.replace(/\s/g, '').toLowerCase();
  let i = 0;
  let terms = 0;
  let totalDice = 0;
  let hasDice = false;
  if (text[0] === '+' || text[0] === '-') i++;
  for (;;) {
    if (terms === maxTerms) return `there are more than ${maxTerms} terms`;
    const count = readNumber(text, i);
    i = count.next;
    if (text[i] === 'd') {
      i++;
      const sides = readNumber(text, i);
      i = sides.next;
      if (sides.value === undefined) return `write the number of sides after "d" in "${source}"`;
      const n = count.value ?? 1;
      if (n < 1) return `"${source}" rolls zero dice`;
      totalDice += n;
      if (n > maxDice || totalDice > maxDice) return `"${source}" rolls more than ${maxDice} dice`;
      if (sides.value < minSides || sides.value > maxSides) return `a die has ${minSides} to ${maxSides} sides, not ${sides.value}`;
      hasDice = true;
    } else if (count.value !== undefined) {
      if (count.value > maxConstant) return `a modifier is at most ${maxConstant}`;
    } else {
      return `"${source}" is not dice such as 1d8 or 1d8+1`;
    }
    terms++;
    if (i === text.length) break;
    if (text[i] !== '+' && text[i] !== '-') return `unexpected "${text[i]}" in "${source}"`;
    i++;
    if (i === text.length) return `"${source}" ends with an operator`;
  }
  return hasDice ? undefined : `"${source}" has no dice to roll`;
}

/** Required dice (damage). */
export function parseWeaponDice(text: string): Parsed<string> {
  const problem = diceProblem(text);
  return problem ? { value: '', problem } : { value: text.trim() };
}

/** Optional dice (versatile damage): empty is none. */
export function parseOptionalDice(text: string): Parsed<string | undefined> {
  if (text.trim() === '') return { value: undefined };
  const problem = diceProblem(text);
  return problem ? { value: undefined, problem } : { value: text.trim() };
}

/** A required word (the weapon key, the damage type). */
export function parseRequiredText(text: string, what: string): Parsed<string> {
  const trimmed = text.trim();
  return trimmed === '' ? { value: '', problem: `${what} is needed` } : { value: trimmed };
}

export const parseWeaponKey = (text: string) => parseRequiredText(text, 'a weapon key');
export const parseDamageType = (text: string) => parseRequiredText(text, 'a damage type');

/** Properties as a comma list. Empty is none; a value between two commas is a problem, never silently dropped. */
export function parseProperties(text: string): Parsed<string[]> {
  const trimmed = text.trim();
  if (trimmed === '') return { value: [] };
  // A trailing comma while typing is ignored; a value between two commas is empty.
  const tokens = trimmed.replace(/,\s*$/, '').split(',').map((t) => t.trim());
  return tokens.some((t) => t === '') ? { value: [], problem: 'a value between two commas is empty' } : { value: tokens };
}

/** Free text (the service does not check it); empty is none. */
export function parseRange(text: string): Parsed<string | undefined> {
  const trimmed = text.trim();
  return { value: trimmed === '' ? undefined : trimmed };
}

/** The weapon's own bonus: empty or 0 is none (R23: nothing is written, so the item stays at its old version); else 1 to 10. */
export function parseBonus(text: string): Parsed<number | undefined> {
  const trimmed = text.trim();
  if (trimmed === '') return { value: undefined };
  if (!/^\d+$/.test(trimmed)) return { value: undefined, problem: `"${trimmed}" is not a bonus; write a whole number from 1 to 10, or leave it empty` };
  const n = Number(trimmed);
  if (n > 10) return { value: undefined, problem: `${n} is too high; a bonus is a whole number from 1 to 10` };
  return { value: n === 0 ? undefined : n };
}

/**
 * True when the service reads a stored string bonus as a whole number 0 to 10: the serializer's web defaults read a numeral
 * string (optional sign, digits, leading zeros allowed, no spaces, not empty). Anything else makes the weapon malformed.
 */
export function storedBonusReads(text: string): boolean {
  if (!/^[+-]?\d+$/.test(text)) return false;
  const n = Number(text);
  return n >= 0 && n <= 10;
}

/** A stored bonus as the field shows it: a number, or a numeral the service reads as that number; anything else raw so the author sees it. */
export function bonusText(bonus: unknown): string {
  if (bonus === undefined || bonus === null) return '';
  if (typeof bonus === 'string') return storedBonusReads(bonus) ? String(Number(bonus)) : bonus;
  return typeof bonus === 'number' ? String(bonus) : JSON.stringify(bonus);
}

/** The stored bonus text the service would reject, or undefined when there is none (a number, a readable numeral, or no bonus). */
export function unreadableStoredBonus(bonus: unknown): string | undefined {
  return typeof bonus === 'string' && !storedBonusReads(bonus) ? bonus : undefined;
}

const optionalText = (v: unknown) => v === undefined || v === null || typeof v === 'string';

/** True when a stored weapon has the shape the editor reads. Anything else gets a notice instead of fields. */
export function weaponReads(effect: Weapon): boolean {
  const w = effect as unknown as Record<string, unknown>;
  // The service reads keys without regard to case, so a Bonus beside (or instead of) bonus would become a second key on edit.
  if (Object.keys(w).some((k) => k !== 'bonus' && k.toLowerCase() === 'bonus')) return false;
  return (
    (w.category === 'simple' || w.category === 'martial') &&
    (w.attack === 'melee' || w.attack === 'ranged') &&
    typeof w.damage === 'string' &&
    typeof w.damageType === 'string' &&
    typeof w.weaponKey === 'string' &&
    (w.properties === undefined || (Array.isArray(w.properties) && w.properties.every((p) => typeof p === 'string'))) &&
    optionalText(w.versatile) &&
    optionalText(w.range) &&
    optionalText(w.mastery)
  );
}
