import type { Effect } from './api/types';

/**
 * M5 slice 1b (ADR-010): the effects the studio's class editor owns, and pure edits of an effect list. Each edit takes
 * the latest list and returns a new one, changing only its own effects and keeping every other effect where it was, so
 * nothing the editor does not show is lost or reordered (review fix).
 */

/** The six abilities: the key used in field ids (save.int, ability.int.score) and the label. */
export const abilities = [
  ['str', 'Strength'],
  ['dex', 'Dexterity'],
  ['con', 'Constitution'],
  ['int', 'Intelligence'],
  ['wis', 'Wisdom'],
  ['cha', 'Charisma'],
] as const;

type Grant = Extract<Effect, { type: 'grant' }>;
type Restriction = Extract<Effect, { type: 'restriction' }>;
type Choice = Extract<Effect, { type: 'choice' }>;

/** The group "any one of these is enough" puts the prerequisites in (content v5 restriction.group). */
export const anyOneGroup = 'multiclass';

export const isSave = (e: Effect): e is Grant => e.type === 'grant' && e.grant === 'proficiency' && (e.target ?? '').startsWith('save.');

/** A multiclass prerequisite on an ability score: the only restrictions the class editor shows. */
export const isPrerequisite = (e: Effect): e is Restriction =>
  e.type === 'restriction' && e.multiclass === true && /^ability\.(str|dex|con|int|wis|cha)\.score$/.test(e.field);

export const isEditorChoice = (e: Effect): e is Choice => e.type === 'choice' && (e.choiceId === 'skills' || e.choiceId === 'subclass');

/** Exactly what the class editor renders; the rule list shows every other effect, so it can be seen and removed. */
export const isClassBasic = (e: Effect): boolean => e.type === 'hitDie' || isSave(e) || isPrerequisite(e) || isEditorChoice(e);

/** Sets the class's hit die in place (or first, when it has none), or removes it. */
export function setHitDie(effects: Effect[], die: number | undefined): Effect[] {
  if (die === undefined) return effects.filter((e) => e.type !== 'hitDie');
  if (!effects.some((e) => e.type === 'hitDie')) return [{ type: 'hitDie', id: uniqueId(effects, 'hit-die'), die }, ...effects];
  return effects.map((e) => (e.type === 'hitDie' ? { ...e, die } : e));
}

/** Starting-class saving throw proficiency in one ability, on or off. */
export function toggleSave(effects: Effect[], key: string): Effect[] {
  const target = `save.${key}`;
  if (effects.some((e) => isSave(e) && e.target === target)) return effects.filter((e) => !(isSave(e) && e.target === target));
  return [...effects, { type: 'grant', id: uniqueId(effects, `save-${key}`), grant: 'proficiency', target, onlyAs: 'startingClass' }];
}

/** Whether the prerequisites are alternatives: any one of them in the "any one" group. */
export const anyOne = (effects: Effect[]): boolean => effects.some((e) => isPrerequisite(e) && e.group === anyOneGroup);

/** Sets or clears the minimum for one ability, keeping the "any one" choice as it was (review fix). */
export function setPrerequisite(effects: Effect[], key: string, minimum: number | undefined): Effect[] {
  const field = `ability.${key}.score`;
  const group = anyOne(effects) ? anyOneGroup : undefined;
  const existing = effects.findIndex((e) => isPrerequisite(e) && e.field === field);
  if (minimum === undefined) return existing < 0 ? effects : effects.filter((_, i) => i !== existing);
  if (existing >= 0) return effects.map((e, i) => (i === existing ? { ...(e as Restriction), minimum } : e));
  return [...effects, { type: 'restriction', id: uniqueId(effects, `multiclass-${key}`), field, minimum, multiclass: true, group }];
}

/**
 * "Any one of these is enough": puts the ability prerequisites in one group, or takes them out of it. A prerequisite
 * in a group of another name (authored elsewhere) keeps it (review fix).
 */
export function setAnyOne(effects: Effect[], on: boolean): Effect[] {
  return effects.map((e) =>
    isPrerequisite(e) && (e.group === undefined || e.group === anyOneGroup) ? { ...e, group: on ? anyOneGroup : undefined } : e,
  );
}

/** Replaces, adds or removes the editor's choice with this choice id (skills or subclass). */
export function setChoice(effects: Effect[], choiceId: 'skills' | 'subclass', choice: Choice | null): Effect[] {
  const index = effects.findIndex((e) => e.type === 'choice' && e.choiceId === choiceId);
  if (choice === null) return index < 0 ? effects : effects.filter((_, i) => i !== index);
  return index < 0 ? [...effects, choice] : effects.map((e, i) => (i === index ? choice : e));
}

/** `base`, or `base-2`, `base-3` … until no effect uses it. */
export function uniqueId(effects: Effect[], base: string): string {
  let id = base;
  for (let n = 2; effects.some((e) => e.id === id); n++) id = `${base}-${n}`;
  return id;
}

/** `column1`, `column2` … : the first class-column key no scale of this list uses (review fix: never a repeat). */
export function nextScaleKey(effects: Effect[]): string {
  const used = new Set(effects.flatMap((e) => (e.type === 'scale' ? [e.scaleId] : [])));
  let n = 1;
  while (used.has(`column${n}`)) n++;
  return `column${n}`;
}

/**
 * Strict parsing of a list of whole numbers separated by commas or spaces (review fix). Nothing is dropped: a token
 * that is not a whole number from 0 to `max` is a problem the editor shows next to the field, and it blocks saving.
 */
export function parseWholeNumbers(text: string, max: number): { values: number[]; problem?: string } {
  const trimmed = text.trim();
  if (trimmed === '') return { values: [] };
  // With commas, each comma separates one value, so "4, 3, , 2" has an empty third value and is an error (positions
  // matter in a slot row); a trailing comma while typing is ignored. Without commas, spaces separate the values.
  const tokens = trimmed.includes(',') ? trimmed.replace(/,\s*$/, '').split(',').map((t) => t.trim()) : trimmed.split(/\s+/);
  if (tokens.some((t) => t === '')) return { values: [], problem: 'a value between two commas is empty' };
  const bad = tokens.find((t) => !/^\d+$/.test(t) || Number(t) > max);
  return bad === undefined ? { values: tokens.map(Number) } : { values: [], problem: `"${bad}" is not a whole number from 0 to ${max}` };
}

/** A list that must have exactly 20 whole numbers (a class column or a multiclass table). */
export function parseTwenty(text: string, max: number): { values: number[]; problem?: string } {
  const parsed = parseWholeNumbers(text, max);
  if (parsed.problem) return parsed;
  return parsed.values.length === 20 ? parsed : { values: parsed.values, problem: `${parsed.values.length} values given; 20 are needed (class levels 1 to 20)` };
}

/**
 * A slot table typed as one line per class level, each line the slots of spell levels 1, 2, … (an empty line: none).
 * Trailing blank lines are ignored; there must be at most 20 lines, and fewer lines mean no slots at the higher levels.
 * Positions matter, so every token must be a slot count (review fix: "4, 3, x, 2" is an error, never [4, 3, 2]).
 */
export function parseSlotRows(text: string): { rows: number[][]; problem?: string } {
  const lines = text.replace(/\s+$/, '').split('\n');
  if (text.trim() === '') return { rows: Array.from({ length: 20 }, () => []) };
  if (lines.length > 20) return { rows: [], problem: `${lines.length} lines; one per class level, so at most 20` };
  const rows: number[][] = [];
  for (const [i, line] of lines.entries()) {
    const row = parseWholeNumbers(line, 20);
    if (row.problem) return { rows: [], problem: `Class level ${i + 1}: ${row.problem}` };
    if (row.values.length > 9) return { rows: [], problem: `Class level ${i + 1}: at most 9 spell levels` };
    rows.push(row.values);
  }
  while (rows.length < 20) rows.push([]);
  return { rows };
}
