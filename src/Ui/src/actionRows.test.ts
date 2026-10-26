// D73: the shared availability, Use and Spend rules (the Actions panel and the quickbar read the same answers).
import { expect, it } from 'vitest';
import { attackAvailability, poolFor, recordSpend, rollAvailability, toggleAvailability, useOf } from './actionRows';
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
  const hasAction = cases.map((e) => useOf(feature([e]), e, p, [p]).action !== undefined);
  expect(usable).toEqual(hasAction);
  expect(hasAction).toEqual([true, false, false, false, true]);
});

it("useOf: a compound use with no rollId can't be used here and sends nothing", () => {
  const e = plain({ drawsDown: true });
  const got = useOf(feature([e]), e, pool({ countsUp: true, spent: 3 }), []);
  expect(got.reason).toBe("Can't be used here");
  expect(got.action).toBeUndefined();
});

it('useOf: a plain use sends a spend of its cost from the pool named by the effect (else the feature)', () => {
  const e = plain();
  expect(useOf(feature([e]), e, pool(), []).action).toEqual({ action: 'spend', amount: 2, contentId: 'c1', resourceId: 'focus' });
  const shared = plain({ resourceContent: 'c-shared' });
  expect(useOf(feature([shared]), shared, pool({ content: { contentId: 'c-shared', revisionId: 'r2' } }), []).action).toEqual({ action: 'spend', amount: 2, contentId: 'c-shared', resourceId: 'focus' });
});

it('useOf: a compound use sends useAction with its rollId, and names the cost and the other pools', () => {
  const other = pool({ resourceId: 'charge', label: 'Testy charge', countsUp: true, spent: 0, current: 3, maximum: 3 });
  const e = plain({ rollId: 'surge-roll', alsoChange: [{ resourceContent: 'c1', resourceId: 'charge', change: 'spend', amount: 1 }] });
  const got = useOf(feature([e]), e, pool(), [other]);
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

it('recordSpend: says cantUse and costUnavailable for the held and failed records', () => {
  const held = record({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2, drawsDown: true }, provenance: { rollId: 'surge-roll', label: 'Surge', effectId: 'surge' } });
  expect(spends(held, effect({ drawsDown: true }), pool({ spent: 3 })).cantUse).toBe(true);
  const failed = spends(record({ provenance: { rollId: 'surge-roll', label: 'Surge', content: ref, effectId: 'surge', linkedResourceId: 'focus' } }), effect({ cost: undefined }));
  expect(failed.costUnavailable).toBe(true);
});
