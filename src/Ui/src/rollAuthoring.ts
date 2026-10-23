import type { Effect } from './api/types';

/**
 * Content v11 (D43 slice 5): strict reading of what an author types for an action's area, text and per-unit dice. Nothing is
 * coerced: a value that breaks the rule is a problem the editor shows next to the field, and it blocks saving (the rules
 * mirror ContentValidation, which stays the authority).
 */

/** A pool a roll can spend: this revision's own (no content) or another entry's of the same source. */
export interface PoolChoice {
  /** Absent: this revision. */
  contentId?: string;
  resourceId: string;
  label: string;
  /** The entry that declares it, for a pool of another entry. */
  entryName?: string;
}

/** The value of a pool option: the plain resource id for this revision's own, `content::resource` for another entry's. */
export const poolValue = (contentId: string | undefined, resourceId: string) => (contentId ? `${contentId}::${resourceId}` : resourceId);

/** The longest range or targets text. */
export const maxRollText = 200;

const wholeNumber = /^\d+$/;

function whole(text: string, min: number, max: number, what: string): { value?: number; problem?: string } {
  const trimmed = text.trim();
  if (trimmed === '') return {};
  const n = wholeNumber.test(trimmed) ? Number(trimmed) : Number.NaN;
  return n >= min && n <= max ? { value: n } : { problem: `"${text}" is not ${what}; use a whole number from ${min} to ${max}` };
}

/** An area's size in feet: a whole number from 1 to 1000. It is required once there is an area. */
export function parseAreaSize(text: string): { size?: number; problem?: string } {
  if (text.trim() === '') return { problem: 'the size is needed' };
  const parsed = whole(text, 1, 1000, 'a size');
  return { size: parsed.value, problem: parsed.problem };
}

/** A line's width in feet: a whole number from 1 to 1000; empty is none. */
export function parseAreaWidth(text: string): { width?: number; problem?: string } {
  const parsed = whole(text, 1, 1000, 'a width');
  return { width: parsed.value, problem: parsed.problem };
}

/** The most extra units a per-unit action may take: a whole number from 1 to 100; empty is no limit but the pool. */
export function parseMaxUnits(text: string): { maxUnits?: number; problem?: string } {
  const parsed = whole(text, 1, 100, 'a number of units');
  return { maxUnits: parsed.value, problem: parsed.problem };
}

const perUnitDice = /^(\d+)d(\d+)$/;

/** Per-unit dice as the service reads them: a count from 1 to 100 and a die of d4, d6, d8, d10 or d12. Empty: none. */
export function parsePerUnitDice(text: string): { dice?: string; problem?: string } {
  if (text === '') return {};
  const match = perUnitDice.exec(text);
  const [count, sides] = match ? [Number(match[1]), Number(match[2])] : [0, 0];
  return count >= 1 && count <= 100 && [4, 6, 8, 10, 12].includes(sides)
    ? { dice: text }
    : { problem: `"${text}" is not dice; write a count and a die such as 1d6 (1 to 100 dice of d4, d6, d8, d10 or d12)` };
}

/** Range or targets text: at most 200 characters; empty is none. */
export function parseRollText(text: string): { text?: string; problem?: string } {
  if (text === '') return {};
  return text.length > maxRollText ? { problem: `it is ${text.length} characters; at most ${maxRollText}` } : { text };
}

/** Whether a named value's key follows the scale-key rule: a lowercase letter, then letters or digits, 32 characters at most. */
export const isValueKey = (key: string) => /^[a-z][A-Za-z0-9]{0,31}$/.test(key);

/** `value1`, `value2` … : the first named-value key no value of this list uses. */
export function nextValueKey(effects: Effect[]): string {
  const used = new Set(effects.flatMap((e) => (e.type === 'value' ? [e.valueId] : [])));
  let n = 1;
  while (used.has(`value${n}`)) n++;
  return `value${n}`;
}
