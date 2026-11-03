import type { Effect } from './api/types';

/**
 * Content v11 (D43 slice 4): strict reading of what an author types for feature attacks and riders. Nothing is coerced: a
 * token that breaks the rule is a problem the editor shows next to the field, and it blocks saving (the rules mirror
 * ContentValidation, which stays the authority).
 */

/** A key: a lowercase letter, then letters and digits, 32 characters at most (a scale id, an attack tag, a stack group). */
const keyRule = /^[a-z][A-Za-z0-9]{0,31}$/;

/** `attack1`, `attack2` … : the first attack key no attack of this list uses. */
export function nextAttackKey(effects: Effect[]): string {
  const used = new Set(effects.flatMap((e) => (e.type === 'attack' ? [e.attackId] : [])));
  let n = 1;
  while (used.has(`attack${n}`)) n++;
  return `attack${n}`;
}

/** Tags typed as a comma list; every tag is a key or "all". An empty text is no tags, and a rider needs at least one. */
export function parseTags(text: string, options: { required?: boolean } = {}): { tags: string[]; problem?: string } {
  const trimmed = text.trim();
  if (trimmed === '') return options.required ? { tags: [], problem: 'at least one tag is needed' } : { tags: [] };
  // A trailing comma while typing is ignored; a value between two commas is empty.
  const tokens = trimmed.replace(/,\s*$/, '').split(',').map((t) => t.trim());
  if (tokens.some((t) => t === '')) return { tags: [], problem: 'a value between two commas is empty' };
  const bad = tokens.find((t) => t !== 'all' && !keyRule.test(t));
  return bad === undefined
    ? { tags: tokens }
    : { tags: [], problem: `"${bad}" is not a tag; a tag is a lowercase letter followed by up to 31 letters or digits (or all)` };
}

const riderDice = /^(\d+)d(\d+)$/;

/** Rider dice as the service reads them: a count from 1 to 100 and a die of d4, d6, d8, d10 or d12. Empty: none. */
export function parseRiderDice(text: string): { dice?: string; problem?: string } {
  if (text === '') return {};
  const match = riderDice.exec(text);
  const [count, sides] = match ? [Number(match[1]), Number(match[2])] : [0, 0];
  return count >= 1 && count <= 100 && [4, 6, 8, 10, 12].includes(sides)
    ? { dice: text }
    : { problem: `"${text}" is not dice; write a count and a die such as 3d8 (1 to 100 dice of d4, d6, d8, d10 or d12)` };
}

/** A stack group is a key (the rider's group name); empty: none. */
export function parseStackGroup(text: string): { group?: string; problem?: string } {
  if (text === '') return {};
  return keyRule.test(text) ? { group: text } : { problem: `"${text}" is not a group name; use a lowercase letter followed by up to 31 letters or digits` };
}

/** A toggle an effect can name: one of this revision, or one of another entry of the source. */
export interface ToggleChoice {
  /** Absent: this revision. */
  contentId?: string;
  toggleId: string;
  label: string;
  /** The entry that declares it, for a toggle of another entry. */
  entryName?: string;
}

/** The value of a toggle option: the plain toggle id for this revision's own, `content::toggle` for another entry's. */
export const toggleValue = (contentId: string | undefined, toggleId: string) => (contentId ? `${contentId}::${toggleId}` : toggleId);
