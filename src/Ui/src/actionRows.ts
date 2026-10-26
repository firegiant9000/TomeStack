// D73: what an action row can do right now, shared by the Actions panel and the quickbar. The rules here are the ones the panel's
// buttons already followed (RollItem's Use, RollResult's Spend); the service stays the authority for every change.
import type { AttackEntry, FeatureEffect, FeatureEntry, PlayAction, ResourceValue, RollRecord, ToggleValue } from './api/types';

/** `byHand`: the sheet cannot check this row (an untracked pool, an assisted attack), so the "Usable now" filter never hides it. */
export type Availability = { usable: boolean; reasons: string[]; byHand?: boolean };

const unavailable = (reason: string | null | undefined): Availability => ({ usable: false, reasons: [reason ?? 'Not available right now'] });

/** The pool an effect, attack or toggle spends from: the pool named `resourceId` in the content `contentId`. */
export function poolFor(pools: ResourceValue[], resourceId: string | undefined, contentId: string | undefined): ResourceValue | undefined {
  return resourceId ? pools.find((r) => r.resourceId === resourceId && r.content.contentId === contentId) : undefined;
}

/** Why a tracked pool cannot pay `cost`: an allowance at 0 is "used this round"; a draw-down needs what has built up. */
function needs(pool: ResourceValue, cost: number, drawsDown: boolean): string {
  if (pool.allowance && pool.current === 0) return `${pool.label} used this round`;
  return drawsDown ? `Needs ${cost} ${pool.label} built up (${pool.spent})` : `Needs ${cost} ${pool.label} (${pool.current} left)`;
}

/** The dice-less Use of a roll effect (RollItem today): its reason when it can't be used, and the action it sends. */
export function dicelessUse(
  feature: FeatureEntry,
  effect: FeatureEffect,
  pool: ResourceValue | undefined,
  pools: ResourceValue[],
): { cost: number; costFailed: boolean; poolName?: string; alsoText: string; reason?: string; action?: PlayAction; untracked: boolean } {
  const drawsDown = effect.drawsDown === true;
  // D43 slice 4: an action outside the form it needs is disabled, with the reason as text the button is described by.
  const locked = effect.available === false;
  // An absent cost means one use only when the action declares no cost. A declared cost that failed leaves `cost` absent and
  // puts an effect.invalid-formula diagnostic on the effect (the sheet does not say which formula); never guess one use then.
  const costFailed =
    effect.cost === undefined &&
    effect.resourceId !== undefined &&
    (feature.diagnostics ?? []).some((d) => d.code === 'effect.invalid-formula' && d.effectId === effect.id);
  const cost = effect.cost ?? 1;
  // A draw-down use spends what has built up (`spent`); any other use spends what is left (`current`).
  const stock = drawsDown ? pool?.spent : pool?.current;
  const unaffordable = !effect.dice && effect.resourceId !== undefined && (pool?.current === undefined || (stock ?? 0) < cost);
  const poolName = pool?.label ?? effect.resourceId;
  const alsoChange = effect.alsoChange ?? [];
  const compound = alsoChange.length > 0 || drawsDown;
  const alsoPool = (a: (typeof alsoChange)[number]) => pools.find((r) => r.resourceId === a.resourceId && r.content.contentId === a.resourceContent);
  const alsoName = (a: (typeof alsoChange)[number]) => alsoPool(a)?.label ?? a.resourceId;
  const alsoText = alsoChange
    .filter((a) => a.amount != null)
    // The sign follows what happens to the pool's number: a spend builds a counting-up pool up and uses a normal one up.
    .map((a) => `, ${(a.change === 'spend') === (alsoPool(a)?.countsUp === true) ? '+' : '−'}${a.amount} ${alsoName(a)}`)
    .join('');
  /** The first reason the client can already see that one of the other pools will not take the change; the service stays the authority. */
  const alsoReason = (): string | undefined => {
    for (const a of alsoChange) {
      if (a.amount == null) return 'Cost unavailable';
      const p = alsoPool(a);
      if (!p || p.current === undefined) return `${alsoName(a)} is not tracked on this sheet.`;
      if (a.change !== 'spend') continue;
      if (p.countsUp ? p.maximum !== undefined && p.spent + a.amount > p.maximum : p.current < a.amount) {
        return p.countsUp ? `${p.label} is full (${p.spent} of ${p.maximum}).` : `Not enough ${p.label} left (${p.current}).`;
      }
    }
    return undefined;
  };
  // A compound use is named by `rollId`; without it there is nothing to send (never a plain spend for such a roll).
  // `untracked`: the pool being untracked is what holds the use (an earlier reason wins), so the row is by hand, not unusable.
  let untracked = false;
  const firstReason = (): string | undefined => {
    if (locked) return effect.unavailableReason ?? 'Not available right now';
    if (compound && !effect.rollId) return "Can't be used here";
    if (costFailed) return 'Cost unavailable';
    if (cost < 1 && alsoChange.length === 0) return 'Costs nothing here'; // a compound use is worth it for its other changes
    if (!unaffordable) return alsoReason();
    if (pool?.current === undefined) {
      untracked = true;
      return `${poolName} is not tracked on this sheet.`;
    }
    return drawsDown ? `Not enough ${poolName} built up (${pool.spent}).` : `Not enough ${poolName} left (${pool.current}).`;
  };
  const reason = firstReason();
  // D43 S8: a use that also changes another pool, or draws a pool down, is one compound change on the service.
  const action: PlayAction | undefined =
    reason !== undefined || effect.resourceId === undefined
      ? undefined
      : compound
        ? { action: 'useAction', contentId: feature.content.contentId, rollId: effect.rollId! }
        : { action: 'spend', amount: cost, contentId: effect.resourceContent ?? feature.content.contentId, resourceId: effect.resourceId };
  return { cost, costFailed, poolName, alsoText, reason, action, untracked };
}

/** A roll effect: a dice-less one is usable exactly when its Use has an action; a rolled one only waits on its pool. */
export function rollAvailability(feature: FeatureEntry, effect: FeatureEffect, pool: ResourceValue | undefined, pools: ResourceValue[]): Availability {
  if (effect.available === false) return unavailable(effect.unavailableReason);
  if (effect.resourceId === undefined) return { usable: true, reasons: [] };
  const use = dicelessUse(feature, effect, pool, pools);
  if (!effect.dice) {
    return { usable: use.action !== undefined, reasons: use.reason ? [use.reason] : [], ...(use.untracked ? { byHand: true } : {}) };
  }
  // A rolled action's cost is spent from its record, but the row can still say the pool could not pay it.
  if (pool?.current !== undefined && !effect.variableCost && !use.costFailed) {
    const drawsDown = effect.drawsDown === true;
    if ((drawsDown ? pool.spent : pool.current) < use.cost) return { usable: false, reasons: [needs(pool, use.cost, drawsDown)] };
  }
  return { usable: true, reasons: [] };
}

export function attackAvailability(attack: AttackEntry, pool: ResourceValue | undefined): Availability {
  if (attack.available === false) return unavailable(attack.unavailableReason);
  if (attack.source === 'feature' && attack.resourceId && pool?.current !== undefined) {
    const cost = attack.cost ?? 1;
    if (pool.current < cost) return { usable: false, reasons: [needs(pool, cost, false)] };
  }
  return attack.automation !== 'automatic' ? { usable: true, reasons: [], byHand: true } : { usable: true, reasons: [] };
}

export function toggleAvailability(toggle: ToggleValue, pool: ResourceValue | undefined): Availability {
  if (toggle.on || toggle.auto) return { usable: true, reasons: [] };
  if (toggle.available === false) return unavailable(toggle.unavailableReason);
  if (toggle.resourceId && pool?.current !== undefined) {
    const cost = toggle.cost ?? 1;
    if (pool.current < cost) return { usable: false, reasons: [needs(pool, cost, false)] };
  }
  return { usable: true, reasons: [] };
}

/** What RollResult's Spend offers for a record (moved verbatim from RollResult). */
export type RecordSpend = {
  linked?: ResourceValue;
  cost: number;
  costUnavailable: boolean;
  cantUse: boolean;
  variableCost: boolean;
  stock?: number;
  stockText: string;
  most: number;
  spendAction(amount: number, resourceContent: string, resourceId: string): PlayAction | undefined;
  /** The fixed-cost Spend button's action when that button would be enabled for an unspent record; else undefined. */
  autoAction?: PlayAction;
};

export function recordSpend(record: RollRecord, resources: ResourceValue[], features: FeatureEntry[], attacks: AttackEntry[]): RecordSpend {
  const p = record.provenance;
  // A roll may name a resource its action spends; spending is a separate, explicit button (never automatic). A shared
  // resource (content v6) is found through the content that defines it.
  // D43 S5: `record.spend` (set by the service for a roll effect that names a pool) says exactly which pool and how much.
  const spend = record.spend;
  const holder = spend?.contentId ?? p?.linkedResourceContent ?? p?.content?.contentId;
  const linkedId = spend?.resourceId ?? p?.linkedResourceId;
  const linked = linkedId
    ? (resources.find((r) => r.resourceId === linkedId && r.content.contentId === holder) ?? (spend ? undefined : resources.find((r) => r.resourceId === linkedId)))
    : undefined;
  const effect = features.find((f) => f.content.revisionId === p?.content?.revisionId)?.effects.find((e) => e.id === p?.effectId);
  const featureAttack = attacks.find((a) => a.source === 'feature' && a.item.revisionId === p?.content?.revisionId && a.effectId === p?.effectId);
  // The service leaves `spend` out of a content roll effect's record exactly when its cost formula failed: never guess 1 then.
  const costUnavailable = effect !== undefined && featureAttack === undefined && spend === undefined && linkedId !== undefined;
  const cost = spend?.amount ?? featureAttack?.cost ?? effect?.cost ?? 1;
  // D43 S8: a roll that draws a counting-up pool down, or also changes another pool, is spent as one `useAction` (a plain `spend`
  // would build the charge up instead). The roll is named by the record's own `rollId`; a draw-down is bounded by what has built up.
  const drawsDown = spend?.drawsDown === true || effect?.drawsDown === true;
  const useRollId = p?.rollId ?? effect?.rollId;
  const compound = drawsDown || (effect?.alsoChange?.length ?? 0) > 0;
  // Never a plain `spend` for such a roll: without the roll's content, its id or its effect, the Spend is held with a reason.
  // A content roll's record whose effect has left the sheet cannot say whether it was compound: hold it as well.
  const cantUse = (compound && (useRollId === undefined || p?.content?.contentId === undefined)) || (effect === undefined && featureAttack === undefined && p?.content !== undefined && p.rollId !== undefined);
  const spendAction = (amount: number, resourceContent: string, resourceId: string): PlayAction | undefined =>
    cantUse
      ? undefined
      : compound
        ? { action: 'useAction', contentId: p!.content!.contentId, rollId: useRollId!, ...(effect?.perUnit ? { amount } : {}) }
        : { action: 'spend', amount, contentId: resourceContent, resourceId };
  const stock = linked ? (drawsDown ? linked.spent : linked.current) : undefined;
  const stockText = linked ? (drawsDown ? `${linked.spent} built up` : `${linked.current} left`) : '';
  const most = linked?.current !== undefined && stock !== undefined ? Math.min(cost, stock) : 0;
  const variableCost = effect?.variableCost === true;
  // Exactly the fixed Spend button's enabled condition, minus "already spent" (that is the component's own state).
  const autoAction =
    linked && !costUnavailable && linked.current !== undefined && !variableCost && !cantUse && !(cost < 1 && (effect?.alsoChange?.length ?? 0) === 0) && (stock ?? 0) >= cost
      ? spendAction(cost, spend?.contentId ?? linked.content.contentId, spend?.resourceId ?? linked.resourceId)
      : undefined;
  return { linked, cost, costUnavailable, cantUse, variableCost, stock, stockText, most, spendAction, autoAction };
}
