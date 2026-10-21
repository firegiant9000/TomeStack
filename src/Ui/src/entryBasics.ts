import type { ContentKind, ContentOption, ContentReference, Effect, StudioEntry } from './api/types';
import { abilities, uniqueId } from './classBasics';

/**
 * Tester feedback item 12: the effects the studio's species, background and spell editors own, and pure edits of an
 * effect list. Each edit takes the latest list and returns a new one, changing only its own effects, so nothing the editor
 * does not show is lost or reordered (as `classBasics.ts`). Every shape is one the bundled packs already use, so the
 * builder and the calculator read it unchanged: no new field, effect or schema version.
 */

export type AbilityKey = (typeof abilities)[number][0];

type Modifier = Extract<Effect, { type: 'modifier' }>;
type Grant = Extract<Effect, { type: 'grant' }>;
type Choice = Extract<Effect, { type: 'choice' }>;
type Spell = Extract<Effect, { type: 'spell' }>;

const abilityTarget = /^ability\.(str|dex|con|int|wis|cha)\.score$/;
const abilityLabel = (key: AbilityKey) => abilities.find(([k]) => k === key)![1];

// ---- species: ability increases (2014: they come from the species) ----

/**
 * A plain increase the species editor shows: a bonus of 1 to 9 on an ability score that is always on. The first one per
 * ability; a second, a formula, a toggled or a non-bonus modifier stays in the rule list, where it can be seen and removed.
 */
export const isAbilityIncrease = (e: Effect, effects: Effect[]): e is Modifier =>
  e.type === 'modifier' &&
  e.operation === 'bonus' &&
  abilityTarget.test(e.target) &&
  /^[1-9]$/.test(e.value) &&
  e.toggle === undefined &&
  (e.timing === undefined || e.timing === 'always') &&
  effects.find((x) => x.type === 'modifier' && x.operation === 'bonus' && x.target === e.target && isPlainIncrease(x)) === e;

const isPlainIncrease = (x: Modifier) => /^[1-9]$/.test(x.value) && x.toggle === undefined && (x.timing === undefined || x.timing === 'always');

export function abilityIncreases(effects: Effect[]): Partial<Record<AbilityKey, number>> {
  const found: Partial<Record<AbilityKey, number>> = {};
  for (const e of effects) {
    if (isAbilityIncrease(e, effects)) found[abilityTarget.exec(e.target)![1] as AbilityKey] = Number(e.value);
  }
  return found;
}

/** Sets the increase of one ability in place (or last, when it has none); 0 or `undefined` removes it. */
export function setAbilityIncrease(effects: Effect[], key: AbilityKey, amount: number | undefined): Effect[] {
  const target = `ability.${key}.score`;
  const index = effects.findIndex((e) => isAbilityIncrease(e, effects) && e.target === target);
  if (amount === undefined || amount < 1) return index < 0 ? effects : effects.filter((_, i) => i !== index);
  const value = String(Math.min(9, Math.trunc(amount)));
  if (index >= 0) return effects.map((e, i) => (i === index ? { ...(e as Modifier), value } : e));
  return [...effects, { type: 'modifier', id: uniqueId(effects, `${key}-increase`), operation: 'bonus', target, value }];
}

// ---- species: speed (content v10; items 12 and 24) ----

export const speedRange = { min: 5, max: 120 } as const;

/** Any modifier on the character's walking speed. Below content v10 the calculator ignores these; publishing one writes v10. */
const isSpeedModifier = (e: Effect): e is Modifier => e.type === 'modifier' && e.target === 'speed';

const speedReplaces = (effects: Effect[]) => effects.filter((e): e is Modifier => isSpeedModifier(e) && e.operation === 'replace');

/**
 * The speed the species editor owns: the only `replace` on speed, always on, whose value is a plain whole number. Replacing
 * sets the base before bonuses (the highest replacement wins), so a feat's bonus still adds on top. Anything else on speed
 * (a bonus, a formula, a toggle, several replaces) is not claimed: it stays in the studio's rule list.
 */
const isSpeedValue = (value: string) => /^[1-9]\d{0,2}$/.test(value) && Number(value) >= speedRange.min && Number(value) <= speedRange.max;

export const isSpeciesSpeed =(e: Effect, effects: Effect[]): e is Modifier => {
  const replaces = speedReplaces(effects);
  return replaces.length === 1 && replaces[0] === e && isSpeedValue(e.value) && e.toggle === undefined && (e.timing === undefined || e.timing === 'always');
};

export const speciesSpeed = (effects: Effect[]): number | undefined => {
  const owned = effects.find((e) => isSpeciesSpeed(e, effects));
  return owned ? Number((owned as Modifier).value) : undefined;
};

/** True when the editor can neither show nor safely change speed: a replace exists that it does not own. */
export const hasForeignSpeedReplace = (effects: Effect[]): boolean => {
  const replaces = speedReplaces(effects);
  return replaces.length > 0 && !(replaces.length === 1 && isSpeciesSpeed(replaces[0]!, effects));
};

/** True when the species has any speed effect the editor does not show, so it can say where to find it. */
export const hasOtherSpeedRules = (effects: Effect[]): boolean => effects.some((e) => isSpeedModifier(e) && !isSpeciesSpeed(e, effects));

/** Sets the species speed in place (or last, when it has none); `undefined` removes it. Other speed effects stay as they were. */
export function setSpeciesSpeed(effects: Effect[], feet: number | undefined): Effect[] {
  const index = effects.findIndex((e) => isSpeciesSpeed(e, effects));
  if (feet === undefined) return index < 0 ? effects : effects.filter((_, i) => i !== index);
  const value = String(feet);
  if (index >= 0) return effects.map((e, i) => (i === index ? { ...(e as Modifier), value } : e));
  if (hasForeignSpeedReplace(effects)) return effects;
  return [...effects, { type: 'modifier', id: uniqueId(effects, 'speed'), operation: 'replace', target: 'speed', value }];
}

// ---- skill proficiencies (species and background) ----

/** A skill proficiency of the entry itself: not as a class only, and not from a class level. The first per skill. */
export const isSkillGrant = (e: Effect, effects: Effect[]): e is Grant =>
  e.type === 'grant' &&
  e.grant === 'proficiency' &&
  (e.target ?? '').startsWith('skill.') &&
  e.level === undefined &&
  e.onlyAs === undefined &&
  effects.find((x) => x.type === 'grant' && x.grant === 'proficiency' && x.target === e.target && x.level === undefined && x.onlyAs === undefined) === e;

export const skillGrants = (effects: Effect[]): string[] => effects.filter((e) => isSkillGrant(e, effects)).map((e) => (e as Grant).target!);

export function toggleSkill(effects: Effect[], field: string): Effect[] {
  const index = effects.findIndex((e) => isSkillGrant(e, effects) && e.target === field);
  if (index >= 0) return effects.filter((_, i) => i !== index);
  return [...effects, { type: 'grant', id: uniqueId(effects, field.replace('.', '-')), grant: 'proficiency', target: field }];
}

// ---- grants of other content (species traits; a background's feature or feat) ----

export const isContentGrant = (e: Effect): e is Grant & { content: ContentReference } =>
  e.type === 'grant' && e.grant === 'content' && e.content !== undefined && e.level === undefined && e.onlyAs === undefined;

export const contentGrants = (effects: Effect[]) => effects.filter(isContentGrant);

/** Grants the content, once: a second grant of the same content is not added. */
export function addContentGrant(effects: Effect[], content: ContentReference, idBase: string): Effect[] {
  if (effects.some((e) => isContentGrant(e) && e.content.contentId === content.contentId)) return effects;
  return [...effects, { type: 'grant', id: uniqueId(effects, idBase), grant: 'content', content }];
}

export const removeContentGrant = (effects: Effect[], contentId: string): Effect[] =>
  effects.filter((e) => !(isContentGrant(e) && e.content.contentId === contentId));

type KindOf = (content: ContentReference) => ContentKind | undefined;

/** The background's grant of one kind of content (a feature, or a feat), found by what the granted content is. */
export const grantOfKind = (effects: Effect[], kindOf: KindOf, kind: ContentKind) =>
  effects.filter(isContentGrant).find((e) => kindOf(e.content) === kind);

/** Replaces the grant of that kind in place, adds it, or (with no content) removes it; grants of other kinds stay. */
export function setKindGrant(effects: Effect[], kindOf: KindOf, kind: ContentKind, content: ContentReference | undefined, idBase: string): Effect[] {
  const current = grantOfKind(effects, kindOf, kind);
  if (content === undefined) return current ? effects.filter((e) => e !== current) : effects;
  if (current) return effects.map((e) => (e === current ? { ...current, content } : e));
  return [...effects, { type: 'grant', id: uniqueId(effects, idBase), grant: 'content', content }];
}

// ---- which effects an editor shows (the studio's rule list shows every other one) ----

export const isSpeciesBasic = (e: Effect, effects: Effect[]): boolean =>
  isAbilityIncrease(e, effects) || isSpeciesSpeed(e, effects) || isSkillGrant(e, effects) || isContentGrant(e);

export const isBackgroundBasic = (e: Effect, effects: Effect[], kindOf: KindOf): boolean =>
  isSkillGrant(e, effects) ||
  effects.find(isIncreaseChoice) === e ||
  (grantOfKind(effects, kindOf, 'feature') as Effect | undefined) === e ||
  (grantOfKind(effects, kindOf, 'feat') as Effect | undefined) === e;

export const isSpellBasic = (e: Effect, effects: Effect[]): boolean => e.type === 'spell' && spellOf(effects) === e;

// ---- what the pickers can offer, and what is still a draft ----

/** A content the editors can grant or name: one of this source's entries, one made by an editor, or a published installed one. */
export interface Known {
  contentId: string;
  name: string;
  kind: ContentKind;
  /** The revision a grant should point at: the newest published one, or the draft when nothing is published yet. */
  reference: ContentReference;
  draft: boolean;
  /** In this source (not installed from elsewhere). */
  own: boolean;
  /** Offered for a new pick (an installed content that is superseded, incompatible or only granted by another is not). */
  pickable?: boolean;
}

export function knownContent(entries: StudioEntry[], installed: ContentOption[], made: Known[]): Known[] {
  const found = new Map<string, Known>();
  for (const e of entries) {
    const revision = e.latestPublished ?? e.latest;
    found.set(e.contentId, {
      contentId: e.contentId,
      name: e.name,
      kind: e.kind,
      reference: { contentId: revision.contentId, revisionId: revision.revisionId },
      draft: e.latestPublished === undefined,
      own: true,
    });
  }
  for (const m of made) if (!found.has(m.contentId)) found.set(m.contentId, m);
  for (const o of installed) {
    if (!found.has(o.reference.contentId)) {
      found.set(o.reference.contentId, {
        contentId: o.reference.contentId,
        name: o.name,
        kind: o.kind,
        reference: o.reference,
        draft: false,
        own: false,
        pickable: o.compatible && !o.superseded && o.standalone !== false,
      });
    }
  }
  return [...found.values()];
}

/** The drafts that grants and choice options of these effects point at: they stay inactive until those are published too. */
export function pinnedDrafts(effects: Effect[], draftRevisionIds: ReadonlySet<string>): ContentReference[] {
  const refs = effects.flatMap((e) => (isContentGrant(e) ? [e.content] : e.type === 'choice' ? e.options : []));
  return refs.filter((r) => draftRevisionIds.has(r.revisionId));
}

// ---- background: ability score increases (2024: they come from the background) ----

/** `fixture-scout-ability-scores`: the bundled backgrounds name this choice after themselves. */
export function increaseChoiceId(name: string): string {
  const slug = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return `${slug || 'background'}-ability-scores`;
}

export const isIncreaseChoice = (e: Effect): e is Choice => e.type === 'choice' && e.choiceId.endsWith('-ability-scores');

export interface IncreaseOption {
  name: string;
  effects: Effect[];
}

/**
 * The options the bundled 5.2.1 backgrounds offer for three listed abilities: +2 to one and +1 to another for each ordered
 * pair (in sheet order), then +1 to all three. Each is a feature of bonus modifiers named
 * "{Background} Ability Scores: Strength +2, Dexterity +1". Anything but three different abilities gives none.
 */
export function increaseOptions(backgroundName: string, picked: AbilityKey[]): IncreaseOption[] {
  const keys = abilities.map(([k]) => k).filter((k) => picked.includes(k));
  if (keys.length !== 3 || new Set(picked).size !== 3) return [];
  const modifier = (key: AbilityKey, value: number): Effect => ({ type: 'modifier', id: key, operation: 'bonus', target: `ability.${key}.score`, value: String(value) });
  const prefix = `${backgroundName.trim()} Ability Scores`;
  const options: IncreaseOption[] = [];
  for (const big of keys) {
    for (const small of keys.filter((k) => k !== big)) {
      options.push({ name: `${prefix}: ${abilityLabel(big)} +2, ${abilityLabel(small)} +1`, effects: [modifier(big, 2), modifier(small, 1)] });
    }
  }
  options.push({ name: `${prefix}: ${keys.map((k) => `${abilityLabel(k)} +1`).join(', ')}`, effects: keys.map((k) => modifier(k, 1)) });
  return options;
}

/** The choice a background offers. `keepChoiceId` is the stored id of an existing choice: characters' picks are keyed by it (R63). */
export function increaseChoice(backgroundName: string, options: ContentReference[], keepChoiceId?: string): Choice {
  const id = keepChoiceId ?? increaseChoiceId(backgroundName);
  return {
    type: 'choice',
    id,
    choiceId: id,
    count: 1,
    options,
    text: 'Choose how this background raises your ability scores: +2 to one and +1 to another, or +1 to all three.',
  };
}

/** The abilities the option features raise, in sheet order. */
export function keysOfOptions(optionEffects: Effect[][]): AbilityKey[] {
  const found = new Set<string>();
  for (const effects of optionEffects) {
    for (const e of effects) {
      const match = e.type === 'modifier' ? abilityTarget.exec(e.target) : null;
      if (match) found.add(match[1]!);
    }
  }
  return abilities.map(([k]) => k).filter((k) => found.has(k));
}

/** Replaces the increase choice in place, adds it, or (with `null`) removes it. */
export function setIncreaseChoice(effects: Effect[], choice: Choice | null): Effect[] {
  const index = effects.findIndex(isIncreaseChoice);
  if (choice === null) return index < 0 ? effects : effects.filter((_, i) => i !== index);
  return index < 0 ? [...effects, choice] : effects.map((e, i) => (i === index ? choice : e));
}

// ---- spell ----

export const schools = ['abjuration', 'conjuration', 'divination', 'enchantment', 'evocation', 'illusion', 'necromancy', 'transmutation'] as const;

export const spellOf = (effects: Effect[]): Spell | undefined => effects.find((e): e is Spell => e.type === 'spell');

type TextField = 'school' | 'castingTime' | 'range' | 'components' | 'duration' | 'text';
const textFields: TextField[] = ['school', 'castingTime', 'range', 'components', 'duration', 'text'];

export type SpellPatch = Partial<Pick<Spell, 'level' | 'concentration' | 'ritual' | 'lists'>> & Partial<Record<TextField, string>>;

/**
 * Changes the given fields of the spell effect (creating it, as a level 1 spell on no list, when there is none) and keeps
 * every other field. A text field with only spaces is removed rather than saved empty; the text is not trimmed while
 * typing, so a space can be typed (`tidySpell` trims on save).
 */
export function setSpell(effects: Effect[], patch: SpellPatch): Effect[] {
  const current = spellOf(effects);
  const base: Spell = current ?? { type: 'spell', id: uniqueId(effects, 'spell'), level: 1, concentration: false, ritual: false, lists: [] };
  const next: Spell = { ...base, ...patch };
  for (const field of textFields) {
    const value = next[field];
    if (typeof value === 'string' && value.trim() === '') delete next[field];
  }
  return current ? effects.map((e) => (e === current ? next : e)) : [...effects, next];
}

/** The spell effect as it is saved: text fields trimmed and spell list keys without repeats. */
export function tidySpell(effects: Effect[]): Effect[] {
  return effects.map((e) => {
    if (e.type !== 'spell') return e;
    const next: Spell = { ...e };
    for (const field of textFields) {
      const value = next[field];
      if (typeof value === 'string') {
        const trimmed = value.trim();
        if (trimmed === '') delete next[field];
        else next[field] = trimmed;
      }
    }
    if (next.lists) next.lists = [...new Set(next.lists)];
    return next;
  });
}
