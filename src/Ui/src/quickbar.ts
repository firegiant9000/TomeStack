// D73: the quickbar's pins, kept per character in the page's own storage, and how a pin finds its action on the current sheet.
import { attackAvailability, dicelessUse, poolFor, rollAvailability, toggleAvailability, type Availability } from './actionRows';
import type { AttackEntry, CharacterView, CustomAttack, FeatureEffect, FeatureEntry, PlayAction, RollTarget, ToggleValue } from './api/types';

export type PinKind = 'roll' | 'attack' | 'custom' | 'toggle';
export type Pin = { kind: PinKind; contentId?: string; effectId?: string; attackId?: string };

/** The most pins the bar shows. */
export const maxPins = 8;
/** R14: visible pins first, then the ones that no longer resolve (kept in case the form or item comes back). */
export const maxStoredPins = 16;

export const pinKey = (pin: Pin): string => `${pin.kind}:${pin.contentId ?? ''}:${pin.effectId ?? ''}:${pin.attackId ?? ''}`;

const filled = (value: unknown): value is string => typeof value === 'string' && value !== '';

export function isPin(value: unknown): value is Pin {
  if (typeof value !== 'object' || value === null) return false;
  const p = value as Record<string, unknown>;
  switch (p.kind) {
    case 'roll':
    case 'attack':
    case 'toggle':
      return filled(p.contentId) && filled(p.effectId);
    case 'custom':
      return filled(p.attackId);
    default:
      return false;
  }
}

/** Keeps the four known fields of each valid pin, drops duplicates, and stops at `max`. */
function normalise(values: unknown[], max: number): Pin[] {
  const seen = new Set<string>();
  const pins: Pin[] = [];
  for (const value of values) {
    if (!isPin(value)) continue;
    const pin: Pin = { kind: value.kind };
    if (typeof value.contentId === 'string') pin.contentId = value.contentId;
    if (typeof value.effectId === 'string') pin.effectId = value.effectId;
    if (typeof value.attackId === 'string') pin.attackId = value.attackId;
    const k = pinKey(pin);
    if (seen.has(k)) continue;
    seen.add(k);
    pins.push(pin);
    if (pins.length === max) break;
  }
  return pins;
}

/** Same pattern as `sheetTab.ts`: a preference of this app on this machine, in no package, share or backup. */
const key = (characterId: string) => `tomestack.quickbar.${characterId}`;

export function readPins(characterId: string): Pin[] {
  try {
    const stored = globalThis.localStorage?.getItem(key(characterId));
    if (!stored) return [];
    const parsed: unknown = JSON.parse(stored);
    return Array.isArray(parsed) ? normalise(parsed, maxStoredPins) : [];
  } catch {
    return []; // storage unavailable or not JSON: no pins
  }
}

export function writePins(characterId: string, pins: Pin[]): void {
  try {
    const clean = normalise(pins, maxStoredPins);
    if (clean.length === 0) globalThis.localStorage?.removeItem(key(characterId));
    else globalThis.localStorage?.setItem(key(characterId), JSON.stringify(clean));
  } catch {
    // storage unavailable: the pins last for this sheet only
  }
}

export const rollPin = (feature: FeatureEntry, effect: FeatureEffect): Pin => ({ kind: 'roll', contentId: feature.content.contentId, effectId: effect.id });
export const attackPin = (attack: AttackEntry): Pin => ({ kind: 'attack', contentId: attack.item.contentId, effectId: attack.effectId });
export const customPin = (attack: CustomAttack): Pin => ({ kind: 'custom', attackId: attack.id });
export const togglePin = (toggle: ToggleValue): Pin => ({ kind: 'toggle', contentId: toggle.content.contentId, effectId: toggle.effectId });

export type QuickAction = {
  pin: Pin;
  key: string;
  /** Accessible name, e.g. "Roll Tide Wave and spend 2 Focus", "Use Guard (1 Reaction)", "Roll Longsword attack", "Turn Tide Form on". */
  name: string;
  availability: Availability;
  /** A roll (mode is added at run time for attacks); absent for a play change. */
  target?: RollTarget;
  /** A play change (dice-less Use, toggle); absent for a roll. */
  action?: PlayAction;
};

/** A pin finds its row by content id (not revision), so a pinned row survives a revision update; a missing row resolves to nothing. */
export function resolvePin(view: CharacterView, pin: Pin): QuickAction | undefined {
  const base = { pin, key: pinKey(pin) };
  const resources = view.sheet.resources ?? [];
  switch (pin.kind) {
    case 'roll': {
      const feature = (view.sheet.features ?? []).find((f) => f.content.contentId === pin.contentId && f.effects.some((e) => e.type === 'roll' && e.id === pin.effectId));
      const effect = feature?.effects.find((e) => e.type === 'roll' && e.id === pin.effectId);
      if (!feature || !effect) return undefined;
      const pool = poolFor(resources, effect.resourceId, effect.resourceContent ?? feature.content.contentId);
      const availability = rollAvailability(feature, effect, pool, resources);
      const label = effect.label || effect.id;
      const use = dicelessUse(feature, effect, pool, resources);
      if (!effect.dice) {
        if (effect.resourceId === undefined) return undefined; // nothing to roll and nothing to spend
        const cost = use.costFailed ? '' : ` (${use.cost} ${use.poolName}${use.alsoText})`;
        return { ...base, name: `Use ${label}${cost}`, availability, action: use.action };
      }
      // R13: a per-unit roll carries its base cost; the unit count is chosen at run time.
      const target: RollTarget = { content: feature.content, effectId: effect.id, ...(effect.perUnit && pool?.current !== undefined ? { amount: effect.cost ?? 0 } : {}) };
      // The name promises a spend only when the roll's record would offer the fixed Spend (recordSpend's autoAction).
      const stock = effect.drawsDown ? pool?.spent : pool?.current;
      const spends =
        pool?.current !== undefined && !effect.variableCost && !use.costFailed && !(use.cost < 1 && (effect.alsoChange?.length ?? 0) === 0) && (stock ?? 0) >= use.cost;
      return { ...base, name: spends ? `Roll ${label} and spend ${use.cost} ${pool.label}` : `Roll ${label}`, availability, target };
    }
    case 'attack': {
      const attack = (view.sheet.attacks ?? []).find((a) => a.item.contentId === pin.contentId && a.effectId === pin.effectId);
      if (!attack) return undefined;
      const availability = attackAvailability(attack, poolFor(resources, attack.resourceId ?? undefined, attack.costResourceContent ?? undefined));
      const target: RollTarget = attack.source === 'feature' ? { content: attack.item, effectId: attack.effectId } : { weapon: attack.item };
      return { ...base, name: `Roll ${attack.name} attack`, availability, target };
    }
    case 'custom': {
      const attack = (view.character.customAttacks ?? []).find((a) => a.id === pin.attackId);
      if (!attack) return undefined;
      return { ...base, name: `Roll ${attack.name} attack`, availability: { usable: true, reasons: [] }, target: { customAttack: attack.id } };
    }
    case 'toggle': {
      const t = (view.sheet.toggles ?? []).find((x) => x.content.contentId === pin.contentId && x.effectId === pin.effectId);
      // R15: an auto toggle follows the hit points and a grouped one is the form picker's; neither is a one-tap action.
      if (!t || t.auto || t.group) return undefined;
      const availability = toggleAvailability(t, poolFor(resources, t.resourceId, t.costResourceContent ?? t.content.contentId));
      const action: PlayAction = { action: t.on ? 'toggleOff' : 'toggleOn', contentId: t.content.contentId, toggleId: t.toggleId };
      return { ...base, name: `Turn ${t.label} ${t.on ? 'off' : 'on'}`, availability, action };
    }
  }
}

export const pinnable = (view: CharacterView, pin: Pin): boolean => resolvePin(view, pin) !== undefined;

/** The first `maxPins` stored pins that still resolve on this sheet. */
export function visiblePins(view: CharacterView, stored: Pin[]): Pin[] {
  return stored.filter((p) => pinnable(view, p)).slice(0, maxPins);
}

/** R14: the stored list after the player pins or unpins `pin`: visible pins (edited; adding only when fewer than 8 are visible), then the hidden ones, capped at 16. */
export function editPins(view: CharacterView, stored: Pin[], pin: Pin): Pin[] {
  const visible = visiblePins(view, stored);
  const shown = new Set(visible.map(pinKey));
  const hidden = stored.filter((p) => !shown.has(pinKey(p)));
  const k = pinKey(pin);
  let next: Pin[];
  let rest = hidden;
  if (shown.has(k)) {
    next = visible.filter((p) => pinKey(p) !== k);
  } else if (visible.length < maxPins) {
    next = [...visible, pin];
    rest = hidden.filter((p) => pinKey(p) !== k);
  } else {
    next = visible;
  }
  return [...next, ...rest].slice(0, maxStoredPins);
}
