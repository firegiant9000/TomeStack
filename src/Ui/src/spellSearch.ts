import type { SpellEntry } from './api/types';

/** Lower-case, accents stripped (NFD then combining marks removed), trimmed. */
export const foldName = (s: string): string => s.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();

/** D30: a substring match on the folded name; an empty (or marks-only) query matches every spell. */
export function matchesSpell(name: string, query: string): boolean {
  const q = foldName(query);
  return q === '' || foldName(name).includes(q);
}

export type CastingBucket = 'action' | 'bonusAction' | 'reaction' | 'longer';

export interface SpellFilter {
  text: string;
  levels: number[];
  schools: string[];
  casting: CastingBucket[];
  concentration?: boolean;
  ritual?: boolean;
  /** MECH-X item 1 (R39): spells carrying any of these tags. Absent or empty means any. */
  tags?: string[];
}

const castingStart = /^(?:1\s+)?(bonus action|action|reaction)\b/i;

/**
 * P09 (D70): the casting-time bucket from the leading words, case-insensitive with an optional leading "1 ". Both SRD packs
 * are covered ("1 bonus action" and "Bonus Action"); "Action or Ritual" and "1 action or 8 hours" are an action (the
 * fastest option wins). Anything else, including a missing or empty time, is `longer`.
 */
export function castingBucket(castingTime: string | undefined): CastingBucket {
  const word = castingStart.exec((castingTime ?? '').trim())?.[1]?.toLowerCase();
  return word === 'action' ? 'action' : word === 'bonus action' ? 'bonusAction' : word === 'reaction' ? 'reaction' : 'longer';
}

/** P09 (D70): every dimension is ANDed and an empty one means any. A spell with no school matches only when no school is ticked. */
export function filterSpells(spells: SpellEntry[], filter: SpellFilter): SpellEntry[] {
  const schools = new Set(filter.schools.map(foldName));
  const tags = new Set((filter.tags ?? []).map(foldName));
  return spells.filter(
    (s) =>
      matchesSpell(s.name, filter.text) &&
      (filter.levels.length === 0 || filter.levels.includes(s.level)) &&
      (schools.size === 0 || (s.school !== undefined && schools.has(foldName(s.school)))) &&
      (filter.casting.length === 0 || filter.casting.includes(castingBucket(s.castingTime))) &&
      (filter.concentration === undefined || s.concentration === filter.concentration) &&
      (filter.ritual === undefined || s.ritual === filter.ritual) &&
      (tags.size === 0 || (s.tags ?? []).some((t) => tags.has(foldName(t)))),
  );
}
