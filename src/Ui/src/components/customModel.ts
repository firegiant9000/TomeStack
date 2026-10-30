import type { ReactNode } from 'react';
import type { CharacterView, Companion, CompanionValue, CustomAttack, CustomItem, TraceEntry } from '../api/types';

/**
 * Tester feedback item 18 (character schema v9, D42): the drafts behind "Your own items" and "Your own attacks" on Inventory.
 * The limits mirror `Character` in RulesCore so a form can explain a refusal before it is sent; the service stays the authority.
 * A damage expression is deliberately not checked here: the dice engine owns that grammar (see `checkDamage` in the panel).
 */
export const maxCustomItems = 50;
export const maxCustomAttacks = 30;
export const maxName = 80;
export const maxNote = 200;
export const maxQuantity = 9999;
export const minToHit = -20;
export const maxToHit = 30;

/** The damage types of both SRDs, as the service stores them (lower case). */
export const damageTypes = [
  'acid',
  'bludgeoning',
  'cold',
  'fire',
  'force',
  'lightning',
  'necrotic',
  'piercing',
  'poison',
  'psychic',
  'radiant',
  'slashing',
  'thunder',
] as const;

/** A damage type as the select shows it ("Slashing"). */
export const damageTypeLabel = (type: string) => type.charAt(0).toUpperCase() + type.slice(1);

/**
 * Wave 3 item 21: a weapon's or custom attack's damage type is a free string. It pre-fills the damage-type select only when it
 * names one of the thirteen types (any case, no trimming, like the service); anything else gives '' (untyped).
 */
export const normalizeDamageType = (type?: string): string => damageTypes.find((t) => t === type?.toLowerCase()) ?? '';

export interface ItemDraft {
  [key: string]: string | boolean;
  name: string;
  quantity: string;
  note: string;
  equipped: boolean;
}

export interface AttackDraft {
  [key: string]: string | boolean;
  name: string;
  toHit: string;
  damage: string;
  damageType: string;
  note: string;
}

/** What a form shows: a linked message per field, and whether a required field is still empty (held, but not an error yet). */
export interface Problems {
  errors: Record<string, string>;
  incomplete: boolean;
}

export const itemDraft = (item?: CustomItem): ItemDraft => ({
  name: item?.name ?? '',
  quantity: String(item?.quantity ?? 1),
  note: item?.note ?? '',
  equipped: item?.equipped ?? false,
});

export const attackDraft = (attack?: CustomAttack): AttackDraft => ({
  name: attack?.name ?? '',
  toHit: String(attack?.toHit ?? 0),
  damage: attack?.damage ?? '',
  damageType: attack?.damageType ?? '',
  note: attack?.note ?? '',
});

const wholeNumber = (text: string) => (text.trim() !== '' && Number.isInteger(Number(text)) ? Number(text) : undefined);

function common(name: string, note: string, errors: Record<string, string>) {
  if (name.trim().length > maxName) errors.name = `The name can have at most ${maxName} characters.`;
  if (note.trim().length > maxNote) errors.note = `The note can have at most ${maxNote} characters.`;
}

export function itemProblems(draft: ItemDraft): Problems {
  const errors: Record<string, string> = {};
  common(draft.name, draft.note, errors);
  const quantity = wholeNumber(draft.quantity);
  if (quantity === undefined || quantity < 1 || quantity > maxQuantity) errors.quantity = `The quantity must be a whole number from 1 to ${maxQuantity}.`;
  return { errors, incomplete: draft.name.trim() === '' };
}

export function attackProblems(draft: AttackDraft): Problems {
  const errors: Record<string, string> = {};
  common(draft.name, draft.note, errors);
  const toHit = wholeNumber(draft.toHit);
  if (toHit === undefined || toHit < minToHit || toHit > maxToHit) errors.toHit = `The to-hit bonus must be a whole number from ${minToHit} to +${maxToHit}.`;
  return { errors, incomplete: draft.name.trim() === '' };
}

const text = (value: string) => (value.trim() === '' ? undefined : value.trim());

/** The record to save. Blank optional text is left out, so nothing new is written when it is absent. */
export function buildItem(id: string, draft: ItemDraft): CustomItem {
  return { id, name: draft.name.trim(), quantity: Number(draft.quantity), note: text(draft.note), equipped: draft.equipped };
}

export function buildAttack(id: string, draft: AttackDraft): CustomAttack {
  return {
    id,
    name: draft.name.trim(),
    toHit: Number(draft.toHit),
    damage: text(draft.damage),
    damageType: text(draft.damageType),
    note: text(draft.note),
  };
}

// ---- Wave 3 item 23 (D39): companions on Play. Limits mirror `Character` in RulesCore; the service stays the authority. ----
export const maxCompanions = 10;
export const maxCompanionAttacks = 5;
export const maxCompanionText = 80;
export const maxCompanionArmorClass = 50;
export const maxHitPoints = 10_000;

/** One attack line of a companion's draft: an attack draft with the id it keeps through edits. */
export interface AttackLine extends AttackDraft {
  id: string;
}

export interface CompanionDraft {
  [key: string]: string | boolean;
  name: string;
  kind: string;
  armorClass: string;
  hitPointMaximum: string;
  speed: string;
  note: string;
  /** JSON of `AttackLine[]`: the form edits the lines through this one string, so the shared entry editor needs no nested drafts. */
  attacks: string;
}

export const attackLines = (draft: CompanionDraft): AttackLine[] => JSON.parse(draft.attacks) as AttackLine[];

export const companionDraft = (companion?: Companion): CompanionDraft => ({
  name: companion?.name ?? '',
  kind: companion?.kind ?? '',
  armorClass: companion?.armorClass === undefined ? '' : String(companion.armorClass),
  hitPointMaximum: String(companion?.hitPointMaximum ?? 1),
  speed: companion?.speed ?? '',
  note: companion?.note ?? '',
  attacks: JSON.stringify((companion?.attacks ?? []).map((a): AttackLine => ({ ...attackDraft(a), id: a.id }))),
});

export function companionProblems(draft: CompanionDraft): Problems {
  const errors: Record<string, string> = {};
  common(draft.name, draft.note, errors);
  if (draft.kind.trim().length > maxCompanionText) errors.kind = `The kind can have at most ${maxCompanionText} characters.`;
  if (draft.speed.trim().length > maxCompanionText) errors.speed = `The speed can have at most ${maxCompanionText} characters.`;
  if (draft.armorClass.trim() !== '') {
    const ac = wholeNumber(draft.armorClass);
    if (ac === undefined || ac < 0 || ac > maxCompanionArmorClass) errors.armorClass = `The Armor Class must be a whole number from 0 to ${maxCompanionArmorClass}.`;
  }
  const max = wholeNumber(draft.hitPointMaximum);
  if (max === undefined || max < 0 || max > maxHitPoints) errors.hitPointMaximum = `The hit point maximum must be a whole number from 0 to ${maxHitPoints}.`;
  let incomplete = draft.name.trim() === '';
  attackLines(draft).forEach((line, i) => {
    const problems = attackProblems(line);
    for (const [field, message] of Object.entries(problems.errors)) errors[`attack${i}-${field}`] = message;
    incomplete ||= problems.incomplete;
  });
  return { errors, incomplete };
}

export function buildCompanion(id: string, draft: CompanionDraft): Companion {
  const attacks = attackLines(draft).map((line) => buildAttack(line.id, line));
  return {
    id,
    name: draft.name.trim(),
    kind: text(draft.kind),
    armorClass: text(draft.armorClass) === undefined ? undefined : Number(draft.armorClass),
    hitPointMaximum: Number(draft.hitPointMaximum),
    speed: text(draft.speed),
    attacks: attacks.length > 0 ? attacks : undefined,
    note: text(draft.note),
  };
}

/** A companion's hit points as the card shows them: no recorded current means at the maximum, and a lowered maximum caps the record. */
export function companionHitPoints(view: CharacterView, companion: Companion): { current: number; temporary: number } {
  const recorded = view.character.play?.companions?.find((p) => p.id === companion.id);
  return { current: Math.min(recorded?.current ?? companion.hitPointMaximum, companion.hitPointMaximum), temporary: recorded?.temporary ?? 0 };
}

/** What one companion card shows, whether the companion is the player's own (W3) or given by content (MECH-X item 4). */
export interface CardModel {
  /** `companion-${id}` for a hand companion, `companion-${contentId}-${companionId}` for a content one; every DOM id of the card derives from it. */
  key: string;
  name: string;
  subtitle?: string;
  armorClass?: number | null;
  speed?: string;
  /** Null: the server could not calculate it, so the hit point controls are held. */
  hitPointMaximum: number | null;
  current: number;
  temporary: number;
  target: { companionId: string; contentId?: string };
  explain?: { armorClass: TraceEntry[]; hitPoints: TraceEntry[] };
  extra?: ReactNode;
}

/** A content companion as a card, without its attacks and pools (the panel adds those as `extra`). */
export function contentCompanionModel(value: CompanionValue): CardModel {
  return {
    key: `companion-${value.content.contentId}-${value.companionId}`,
    name: value.label,
    subtitle: `From ${value.contentName}`,
    armorClass: value.armorClass,
    speed: value.speed,
    hitPointMaximum: value.hitPointMaximum ?? null,
    current: value.current,
    temporary: value.temporary,
    target: { companionId: value.companionId, contentId: value.content.contentId },
    explain: { armorClass: value.armorClassTrace, hitPoints: value.hitPointTrace },
  };
}

export const signedBonus =(n: number) => `${n >= 0 ? '+' : ''}${n}`;

/** One line for a list row: "Testy Sling: +5 to hit, 1d6+2 bludgeoning". */
export const attackSummary = (a: CustomAttack) =>
  `${a.name}: ${signedBonus(a.toHit)} to hit${a.damage ? `, ${a.damage}${a.damageType ? ` ${a.damageType}` : ''}` : ''}`;
