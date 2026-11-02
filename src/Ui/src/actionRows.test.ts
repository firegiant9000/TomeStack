// D73: the shared availability, Use and Spend rules (the Actions panel and the quickbar read the same answers).
import { expect, it } from 'vitest';
import { attackAvailability, hasDice, poolFor, recordPayment, recordSpend, rollAvailability, toggleAvailability, dicelessUse } from './actionRows';
import type { AttackEntry, FeatureEffect, FeatureEntry, ResourceValue, RollRecord, ToggleValue } from './api/types';

const ref = { contentId: 'c1', revisionId: 'r1' };
const pool = (extra: object = {}) =>
  ({
    content: ref, contentName: 'Testy Source', effectId: 'e-focus', resourceId: 'focus', label: 'Testy focus', maximum: 5, spent: 1, current: 4,
    trace: [], warnings: [], automation: 'automatic', recoveries: [], ...extra,
  }) as unknown as ResourceValue;
const effect = (extra: object = {}) =>
  ({ id: 'surge', type: 'roll', automation: 'automatic', label: 'Surge', dice: '2d6', activation: 'action', resourceId: 'focus', cost: 2, available: true, ...extra }) as unknown as FeatureEffect;
const plain = (extra: object = {}) => effect({ dice: undefined, ...extra });
const feature = (effects: FeatureEffect[] = [], diagnostics: object[] = []) =>
  ({ content: ref, name: 'Testy Source', kind: 'feature', effects, diagnostics }) as unknown as FeatureEntry;
const attack = (extra: object = {}) =>
  ({ item: ref, name: 'Testy Lash', effectId: 'lash', source: 'feature', automation: 'automatic', available: true, resourceId: 'focus', costResourceContent: 'c1', ...extra }) as unknown as AttackEntry;
const toggle = (extra: object = {}) =>
  ({ content: ref, contentName: 'Testy Source', effectId: 'e-stance', toggleId: 'stance', label: 'Testy stance', on: false, available: true, ...extra }) as unknown as ToggleValue;
const rollOf = (e: FeatureEffect, p = pool()) => rollAvailability(feature([e]), e, p, [p]);

const notTracked = 'Testy focus is not tracked on this sheet.';
const needs2 = 'Needs 2 Testy focus (1 left)';

it.each([
  ['a locked roll effect', rollOf(effect({ available: false })), { usable: false, reasons: ['Not available right now'] }],
  ['a locked roll effect with its own reason', rollOf(effect({ available: false, unavailableReason: 'Available in Kindled mode' })), { usable: false, reasons: ['Available in Kindled mode'] }],
  ['a use that is locked', rollOf(plain({ available: false, unavailableReason: 'Available in Kindled mode' })), { usable: false, reasons: ['Available in Kindled mode'] }],
  ['a use with an unaffordable cost', rollOf(plain(), pool({ current: 1 })), { usable: false, reasons: ['Not enough Testy focus left (1).'] }],
  ['a use that costs nothing', rollOf(plain({ cost: 0 })), { usable: false, reasons: ['Costs nothing here'] }],
  ['a use of an untracked pool', rollOf(plain(), pool({ current: undefined })), { usable: false, reasons: [notTracked], byHand: true }],
  ['a use that can be paid', rollOf(plain()), { usable: true, reasons: [] }],
  ['a use with no pool named', rollAvailability(feature(), plain({ resourceId: undefined }), undefined, []), { usable: true, reasons: [] }],
  ['a roll with dice the pool cannot pay', rollOf(effect(), pool({ current: 1 })), { usable: false, reasons: [needs2] }],
  ['a roll with dice at an empty allowance', rollOf(effect({ cost: 1 }), pool({ allowance: true, current: 0 })), { usable: false, reasons: ['Testy focus used this round'] }],
  ['a roll with dice that draws down', rollOf(effect({ drawsDown: true }), pool({ spent: 1, current: 4 })), { usable: false, reasons: ['Needs 2 Testy focus built up (1)'] }],
  ['a roll with dice and a variable cost', rollOf(effect({ variableCost: true }), pool({ current: 0 })), { usable: true, reasons: [] }],
  ['a roll with dice and an untracked pool', rollOf(effect(), pool({ current: undefined })), { usable: true, reasons: [] }],
  ['a roll with dice whose cost failed', rollAvailability(feature([], [{ code: 'effect.invalid-formula', effectId: 'surge' }]), effect({ cost: undefined }), pool({ current: 0 }), []), { usable: true, reasons: [] }],
  ['a roll with dice that can be paid', rollOf(effect()), { usable: true, reasons: [] }],
  ['a locked attack', attackAvailability(attack({ available: false }), pool()), { usable: false, reasons: ['Not available right now'] }],
  ['a locked attack with its own reason', attackAvailability(attack({ available: false, unavailableReason: 'Needs a form' }), pool()), { usable: false, reasons: ['Needs a form'] }],
  ['a feature attack the pool cannot pay', attackAvailability(attack({ cost: 2 }), pool({ current: 1 })), { usable: false, reasons: [needs2] }],
  ['a feature attack at an empty allowance', attackAvailability(attack(), pool({ allowance: true, current: 0 })), { usable: false, reasons: ['Testy focus used this round'] }],
  ['a feature attack that can be paid', attackAvailability(attack({ cost: 2 }), pool()), { usable: true, reasons: [] }],
  ['a feature attack with an untracked pool', attackAvailability(attack(), pool({ current: undefined })), { usable: true, reasons: [] }],
  ['an assisted attack', attackAvailability(attack({ source: 'weapon', automation: 'assisted', resourceId: null }), undefined), { usable: true, reasons: [], byHand: true }],
  ['an automatic weapon attack', attackAvailability(attack({ source: 'weapon', resourceId: null }), undefined), { usable: true, reasons: [] }],
  ['a toggle that is on', toggleAvailability(toggle({ on: true, available: false }), undefined), { usable: true, reasons: [] }],
  ['an auto toggle', toggleAvailability(toggle({ auto: true, available: false }), undefined), { usable: true, reasons: [] }],
  ['a toggle that is off and locked', toggleAvailability(toggle({ available: false, unavailableReason: 'Needs a form' }), undefined), { usable: false, reasons: ['Needs a form'] }],
  ['a toggle that is off and locked without a reason', toggleAvailability(toggle({ available: false }), undefined), { usable: false, reasons: ['Not available right now'] }],
  ['a toggle whose pool cannot pay', toggleAvailability(toggle({ resourceId: 'focus', cost: 2 }), pool({ current: 1 })), { usable: false, reasons: [needs2] }],
  ['a toggle whose pool is an empty allowance', toggleAvailability(toggle({ resourceId: 'focus' }), pool({ allowance: true, current: 0 })), { usable: false, reasons: ['Testy focus used this round'] }],
  ['a toggle whose pool can pay', toggleAvailability(toggle({ resourceId: 'focus' }), pool()), { usable: true, reasons: [] }],
  ['a toggle with an untracked pool', toggleAvailability(toggle({ resourceId: 'focus' }), pool({ current: undefined })), { usable: true, reasons: [] }],
])('availability: %s', (_name, got, want) => {
  expect(got).toEqual(want);
});

it('a use is usable exactly when it has an action to send', () => {
  const cases = [plain(), plain({ cost: 0 }), plain({ available: false }), plain({ drawsDown: true }), plain({ drawsDown: true, rollId: 'surge' })];
  const p = pool({ spent: 3 });
  const usable = cases.map((e) => rollAvailability(feature([e]), e, p, [p]).usable);
  const hasAction = cases.map((e) => dicelessUse(feature([e]), e, p, [p]).action !== undefined);
  expect(usable).toEqual(hasAction);
  expect(hasAction).toEqual([true, false, false, false, true]);
});

it("dicelessUse: a compound use with no rollId can't be used here and sends nothing", () => {
  const e = plain({ drawsDown: true });
  const got = dicelessUse(feature([e]), e, pool({ countsUp: true, spent: 3 }), []);
  expect(got.reason).toBe("Can't be used here");
  expect(got.action).toBeUndefined();
});

it('dicelessUse: a plain use sends a spend of its cost from the pool named by the effect (else the feature)', () => {
  const e = plain();
  expect(dicelessUse(feature([e]), e, pool(), []).action).toEqual({ action: 'spend', amount: 2, contentId: 'c1', resourceId: 'focus' });
  const shared = plain({ resourceContent: 'c-shared' });
  expect(dicelessUse(feature([shared]), shared, pool({ content: { contentId: 'c-shared', revisionId: 'r2' } }), []).action).toEqual({ action: 'spend', amount: 2, contentId: 'c-shared', resourceId: 'focus' });
});

it('dicelessUse: a compound use sends useAction with its rollId, and names the cost and the other pools', () => {
  const other = pool({ resourceId: 'charge', label: 'Testy charge', countsUp: true, spent: 0, current: 3, maximum: 3 });
  const e = plain({ rollId: 'surge-roll', alsoChange: [{ resourceContent: 'c1', resourceId: 'charge', change: 'spend', amount: 1 }] });
  const got = dicelessUse(feature([e]), e, pool(), [other]);
  expect(got.action).toEqual({ action: 'useAction', contentId: 'c1', rollId: 'surge-roll' });
  expect(got.reason).toBeUndefined();
  expect(got.cost).toBe(2);
  expect(got.poolName).toBe('Testy focus');
  expect(got.alsoText).toBe(', +1 Testy charge');
});

it('poolFor finds the pool by id and content, and nothing without an id', () => {
  const a = pool();
  const b = pool({ content: { contentId: 'c2', revisionId: 'r2' } });
  expect(poolFor([a, b], 'focus', 'c2')).toBe(b);
  expect(poolFor([a, b], 'focus', 'c3')).toBeUndefined();
  expect(poolFor([a, b], undefined, 'c1')).toBeUndefined();
});

const record = (extra: object = {}) =>
  ({
    formula: '2d6', mode: 'normal', critical: false, dice: [], diceTotal: 7, expressionConstant: 0, modifiers: [], total: 7,
    provenance: { rollId: 'surge-roll', label: 'Surge', content: ref, effectId: 'surge' }, ...extra,
  }) as unknown as RollRecord;
const spends = (r: RollRecord, e: FeatureEffect, p: ResourceValue = pool(), attacks: AttackEntry[] = []) => recordSpend(r, [p], [feature([e])], attacks);

it('recordSpend: a fixed-cost record offers the spend its record names', () => {
  const r = record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2 } });
  expect(spends(r, effect()).autoAction).toEqual({ action: 'spend', amount: 2, contentId: 'c1', resourceId: 'focus' });
});

it('recordSpend: a draw-down record offers useAction with the record rollId', () => {
  const r = record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2, drawsDown: true } });
  const got = spends(r, effect({ drawsDown: true }), pool({ countsUp: true, spent: 3, current: 2 }));
  expect(got.autoAction).toEqual({ action: 'useAction', contentId: 'c1', rollId: 'surge-roll' });
  expect(got.stockText).toBe('3 built up');
});

it.each([
  ['a variable cost', record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 4 } }), effect({ variableCost: true }), pool()],
  ['a held record (compound, no content)', record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2, drawsDown: true }, provenance: { rollId: 'surge-roll', label: 'Surge', effectId: 'surge' } }), effect({ drawsDown: true }), pool({ spent: 3 })],
  ['stock below the cost', record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2 } }), effect(), pool({ current: 1 })],
  ['a cost that failed', record({}), effect({ cost: undefined }), pool()],
  ['an untracked pool', record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2 } }), effect(), pool({ current: undefined })],
  ['a cost of nothing', record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 0 } }), effect(), pool()],
])('recordSpend: no autoAction for %s', (_name, r, e, p) => {
  expect(spends(r, e, p).autoAction).toBeUndefined();
});

it('recordSpend: a content roll whose effect has left the sheet is held, with no autoAction', () => {
  const r = record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2 } });
  const got = recordSpend(r, [pool()], [], []);
  expect(got.cantUse).toBe(true);
  expect(got.autoAction).toBeUndefined();
});

it('recordSpend: a compound per-unit record sends useAction carrying the amount spent', () => {
  const r = record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 3, drawsDown: true } });
  const got = spends(r, effect({ drawsDown: true, perUnit: { dice: '1d6' } }), pool({ countsUp: true, spent: 4, current: 1 }));
  expect(got.autoAction).toEqual({ action: 'useAction', contentId: 'c1', rollId: 'surge-roll', amount: 3 });
});

it('recordSpend: says cantUse and costUnavailable for the held and failed records', () => {
  const held = record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2, drawsDown: true }, provenance: { rollId: 'surge-roll', label: 'Surge', effectId: 'surge' } });
  expect(spends(held, effect({ drawsDown: true }), pool({ spent: 3 })).cantUse).toBe(true);
  const failed = spends(record({ provenance: { rollId: 'surge-roll', label: 'Surge', content: ref, effectId: 'surge', linkedResourceId: 'focus' } }), effect({ cost: undefined }));
  expect(failed.costUnavailable).toBe(true);
});

// MECH-X item 1 (R37, R40): hit-point and slot payments. Affordability is the payment's own `payable`, never `available`.
const hp = (extra: object = {}) => ({ kind: 'hitPoints', payable: true, ...extra });
const slot = (extra: object = {}) => ({ kind: 'slot', payable: true, ...extra });
const lowHp = hp({ payable: false, reason: 'Needs more than 5 current hit points.' });
const noSlot = slot({ payable: false, reason: 'No spell slot of level 2 or higher is left.' });

it.each([
  ['a paying roll with dice that cannot be paid', rollAvailability(feature(), effect({ resourceId: undefined, cost: 5, payment: lowHp }), undefined, []), { usable: false, reasons: ['Needs more than 5 current hit points.'] }],
  ['a paying roll with dice that can be paid', rollAvailability(feature(), effect({ resourceId: undefined, cost: 3, payment: hp() }), undefined, []), { usable: true, reasons: [] }],
  ['a paying use that cannot be paid', rollAvailability(feature(), plain({ resourceId: undefined, cost: 2, payment: noSlot, rollId: 'surge-roll' }), undefined, []), { usable: false, reasons: ['No spell slot of level 2 or higher is left.'] }],
  ['a paying use that can be paid', rollAvailability(feature(), plain({ resourceId: undefined, cost: 2, payment: slot(), rollId: 'surge-roll' }), undefined, []), { usable: true, reasons: [] }],
  ['a locked paying roll keeps the lock reason', rollAvailability(feature(), effect({ resourceId: undefined, payment: lowHp, available: false, unavailableReason: 'Needs a form' }), undefined, []), { usable: false, reasons: ['Needs a form'] }],
  ['a paying feature attack that cannot be paid', attackAvailability(attack({ resourceId: null, cost: 4, payment: lowHp }), undefined), { usable: false, reasons: ['Needs more than 5 current hit points.'] }],
  ['a paying feature attack that can be paid', attackAvailability(attack({ resourceId: null, cost: 4, payment: hp() }), undefined), { usable: true, reasons: [] }],
  ['a paying toggle that is off and cannot be paid', toggleAvailability(toggle({ cost: 3, payment: lowHp }), undefined), { usable: false, reasons: ['Needs more than 5 current hit points.'] }],
  ['a paying toggle that is on never waits on payable', toggleAvailability(toggle({ on: true, cost: 3, payment: lowHp }), undefined), { usable: true, reasons: [] }],
  ['a paying toggle that is off and can be paid', toggleAvailability(toggle({ cost: 3, payment: hp() }), undefined), { usable: true, reasons: [] }],
])('payment availability: %s', (_name, got, want) => {
  expect(got).toEqual(want);
});

it('dicelessUse: a dice-less paying roll with no pool sends useAction with its rollId and names the payment (S5)', () => {
  const e = plain({ resourceId: undefined, cost: 3, rollId: 'surge-roll', payment: hp() });
  const got = dicelessUse(feature([e]), e, undefined, []);
  expect(got.action).toEqual({ action: 'useAction', contentId: 'c1', rollId: 'surge-roll' });
  expect(got.reason).toBeUndefined();
  expect(got.payText).toBe('3 hit points');
  const slotUse = plain({ resourceId: undefined, cost: undefined, rollId: 'surge-roll', payment: slot({ minSlotLevel: 2, slot: { level: 2, pact: false } }) });
  expect(dicelessUse(feature([slotUse]), slotUse, undefined, []).payText).toBe('a level 2+ spell slot (level 2 now)');
});

it('dicelessUse: a dice-less paying roll that cannot be paid, or has no rollId, sends nothing and says why', () => {
  const broke = plain({ resourceId: undefined, cost: 5, rollId: 'surge-roll', payment: lowHp });
  const got = dicelessUse(feature([broke]), broke, undefined, []);
  expect(got.action).toBeUndefined();
  expect(got.reason).toBe('Needs more than 5 current hit points.');
  const unnamed = plain({ resourceId: undefined, cost: 3, payment: hp() });
  expect(dicelessUse(feature([unnamed]), unnamed, undefined, []).reason).toBe("Can't be used here");
});

it('dicelessUse: a hit-point use that costs 0 is still offered; the server refuses it (use.unavailable)', () => {
  const e = plain({ resourceId: undefined, cost: 0, rollId: 'surge-roll', payment: hp() });
  expect(dicelessUse(feature([e]), e, undefined, []).action).toEqual({ action: 'useAction', contentId: 'c1', rollId: 'surge-roll' });
});

it('hasDice: a roll with its own dice, or one that casts a spell with dice, rolls', () => {
  expect(hasDice(effect())).toBe(true);
  expect(hasDice(plain())).toBe(false);
  expect(hasDice(plain({ castsSpell: { name: 'Testy spark', level: 1, attack: 'none', dice: '2d8' } }))).toBe(true);
  expect(hasDice(plain({ castsSpell: { name: 'Testy ward', level: 1, attack: 'none' } }))).toBe(false);
});

const payRecord = (payment: object, extra: object = {}) => record({ payment: { contentId: 'c1', useId: 'surge-roll', mayDropToZero: false, ...payment }, ...extra });

it('recordPayment: a hit-point record offers Pay with the amount, sent as useAction by the record useId', () => {
  const e = effect({ resourceId: undefined, cost: 3, rollId: 'surge-roll', payment: hp() });
  const got = recordPayment(payRecord({ kind: 'hitPoints', amount: 3 }), [feature([e])], [])!;
  expect(got.label).toBe('Pay 3 hit points');
  expect(got.payable).toBe(true);
  expect(got.action).toEqual({ action: 'useAction', contentId: 'c1', rollId: 'surge-roll', amount: 3 });
});

it('recordPayment: a slot record offers Spend and sends no amount; the row says which slot it takes now', () => {
  const e = effect({ resourceId: undefined, rollId: 'surge-roll', payment: slot({ minSlotLevel: 2, slot: { level: 2, pact: false } }) });
  const got = recordPayment(payRecord({ kind: 'slot', minSlotLevel: 2 }), [feature([e])], [])!;
  expect(got.label).toBe('Spend a level 2+ spell slot');
  expect(got.note).toBe('Slot, level 2 now');
  expect(got.action).toEqual({ action: 'useAction', contentId: 'c1', rollId: 'surge-roll' });
});

it('recordPayment: the row that cannot pay holds the button with its reason (R40)', () => {
  const e = effect({ resourceId: undefined, cost: 5, rollId: 'surge-roll', payment: lowHp });
  const got = recordPayment(payRecord({ kind: 'hitPoints', amount: 5 }), [feature([e])], [])!;
  expect(got.payable).toBe(false);
  expect(got.reason).toBe('Needs more than 5 current hit points.');
});

it('recordPayment: a feature attack is found by its effect id; a record with no payment offers nothing', () => {
  const a = attack({ resourceId: null, effectId: 'lash', cost: 4, payment: slot({ slot: { level: 1, pact: true } }) });
  const got = recordPayment(payRecord({ kind: 'slot', useId: 'lash' }), [], [a])!;
  expect(got.action).toEqual({ action: 'useAction', contentId: 'c1', rollId: 'lash' });
  expect(got.note).toBe('Pact slot, level 1 now');
  expect(recordPayment(record(), [feature([effect()])], [])).toBeUndefined();
});
