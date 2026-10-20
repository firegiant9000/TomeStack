import type { CustomAttack, CustomItem } from '../api/types';

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

export const signedBonus = (n: number) => `${n >= 0 ? '+' : ''}${n}`;

/** One line for a list row: "Testy Sling: +5 to hit, 1d6+2 bludgeoning". */
export const attackSummary = (a: CustomAttack) =>
  `${a.name}: ${signedBonus(a.toHit)} to hit${a.damage ? `, ${a.damage}${a.damageType ? ` ${a.damageType}` : ''}` : ''}`;
