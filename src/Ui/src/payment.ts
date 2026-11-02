// MECH-X item 1: how a hit-point or slot payment reads on the sheet. A pool payment (`resource`) is the sheet's old cost line and is not described here.
import type { CastSpellValue, PaymentValue } from './api/types';

/** A payment the sheet makes from hit points or a spell slot (the sheet leaves `payment` out for a pool). */
export const paying = (payment: Pick<PaymentValue, 'kind'> | null | undefined): payment is PaymentValue => payment != null && payment.kind !== 'resource';

/**
 * "3 hit points", "a level 2+ spell slot (level 2 now)", "a spell slot (pact, level 1 now)". `cost` is the hit points a hit-point
 * payment takes; a slot payment is always one slot. `slot` is the slot it would take now, when the sheet can say.
 */
export function paymentText(payment: Pick<PaymentValue, 'kind' | 'minSlotLevel' | 'slot'>, cost?: number | null): string {
  if (payment.kind === 'hitPoints') return cost == null ? 'hit points' : `${cost} hit point${cost === 1 ? '' : 's'}`;
  const from = payment.minSlotLevel !== undefined && payment.minSlotLevel > 1 ? `a level ${payment.minSlotLevel}+ spell slot` : 'a spell slot';
  return payment.slot ? `${from} (${payment.slot.pact ? 'pact, ' : ''}level ${payment.slot.level} now)` : from;
}

/**
 * R38: what a roll that casts a spell without a slot says on its row: "Casts Fixture Spark (level 2) · DC 14", "· +6 to hit", or
 * "· numbers by hand" when the character has no caster to give the spell attack bonus or save DC.
 */
export function castsText(cast: Pick<CastSpellValue, 'name' | 'level' | 'attack' | 'save' | 'attackBonus' | 'saveDc'>): string {
  const numbers: string[] = [];
  if (cast.attack !== 'none') numbers.push(cast.attackBonus == null ? 'numbers by hand' : `${cast.attackBonus >= 0 ? '+' : ''}${cast.attackBonus} to hit`);
  if (cast.save) numbers.push(cast.saveDc == null ? 'numbers by hand' : `DC ${cast.saveDc}`);
  return [`Casts ${cast.name} (level ${cast.level})`, ...new Set(numbers)].join(' · ');
}
