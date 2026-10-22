// @vitest-environment jsdom
// D23: the inverse of a play action, computed from the view before and after, through the same confirmed commands.
import { expect, it } from 'vitest';
import type { CharacterView, PlayAction } from './api/types';
import { inverseOf, isCompound } from './undo';

const view = (current: number, temporary: number, play: Partial<CharacterView['character']['play']> = {}): CharacterView =>
  ({
    character: { play: { temporaryHitPoints: temporary, resources: [], conditions: [], exhaustion: 0, ...play } },
    sheet: { hitPoints: { maximum: 35, current, temporary } },
  }) as unknown as CharacterView;

const concentrating = (pendingSaveDc?: number) => ({ concentration: { spell: { contentId: 'fixture-spell', revisionId: 'r1' }, name: 'Fixture Ward', pendingSaveDc } }) as never;

it('undoes damage by setting hit points and temporary hit points back', () => {
  const entry = inverseOf({ action: 'damage', amount: 12 }, view(35, 5), view(28, 0))!;
  expect(entry.label).toBe('damage 12');
  expect(entry.inverse).toEqual([{ action: 'setHitPoints', amount: 35 }, { action: 'setTemporaryHitPoints', amount: 5 }]);
});

it('labels typed damage with its type and what was taken, and undoes it with the same exact steps (item 21)', () => {
  // 13 slashing, halved to 6: 5 to temporary hit points, 1 to hit points. The inverse is unchanged by the type.
  const after = { ...view(34, 0), damage: { amount: 13, type: 'slashing', effect: 'halved', adjusted: 6, absorbed: 5, lost: 1, warnings: [] } } as unknown as CharacterView;
  const entry = inverseOf({ action: 'damage', amount: 13, damageType: 'Slashing' }, view(35, 5), after)!;
  expect(entry.label).toBe('damage 13 slashing (6 taken)');
  expect(entry.inverse).toEqual([{ action: 'setHitPoints', amount: 35 }, { action: 'setTemporaryHitPoints', amount: 5 }]);
});

it('keeps the plain label for untyped damage, and for typed damage whose reply carries no outcome', () => {
  const untyped = { ...view(23, 0), damage: { amount: 12, effect: 'none', adjusted: 12, absorbed: 0, lost: 12, warnings: [] } } as unknown as CharacterView;
  expect(inverseOf({ action: 'damage', amount: 12 }, view(35, 0), untyped)!.label).toBe('damage 12');
  expect(inverseOf({ action: 'damage', amount: 12, damageType: 'fire' }, view(35, 0), view(23, 0))!.label).toBe('damage 12');
});

it('undoes damage that bloodies the character by an exact hit point step and no toggle action (D38)', () => {
  // An auto toggle is computed from the hit points and never stored, so putting the hit points back switches it off for free.
  const grit = { content: { contentId: 'c', revisionId: 'r' }, toggleId: 'grit', on: false, auto: true };
  const bloodied = (current: number, on: boolean) =>
    ({ ...view(current, 0), sheet: { hitPoints: { maximum: 35, current, temporary: 0 }, bloodied: on || undefined, toggles: [{ ...grit, on }] } }) as unknown as CharacterView;
  const entry = inverseOf({ action: 'damage', amount: 18 }, bloodied(35, false), bloodied(17, true))!;
  expect(entry.inverse).toEqual([{ action: 'setHitPoints', amount: 35 }]);
  expect(entry.inverse.some((a) => a.action === 'toggleOn' || a.action === 'toggleOff')).toBe(false);
});

it('undoes heal by setting hit points back, without a temporary step when nothing changed', () => {
  expect(inverseOf({ action: 'heal', amount: 4 }, view(20, 0), view(24, 0))!.inverse).toEqual([{ action: 'setHitPoints', amount: 20 }]);
});

it('clears the concentration check that damage raised', () => {
  const entry = inverseOf({ action: 'damage', amount: 12 }, view(35, 0, concentrating()), view(23, 0, concentrating(10)))!;
  expect(entry.inverse).toEqual([{ action: 'setHitPoints', amount: 35 }, { action: 'clearConcentrationCheck' }]);
});

it('starts concentration again when damage to 0 ended it, after the hit points are back', () => {
  const entry = inverseOf({ action: 'damage', amount: 40 }, view(10, 0, concentrating()), view(0, 0))!;
  expect(entry.inverse).toEqual([{ action: 'setHitPoints', amount: 10 }, { action: 'startConcentration', contentId: 'fixture-spell' }]);
});

it('offers no undo for regaining hit points from 0, which reset death saves that cannot be put back', () => {
  const dying = { deathSaves: { successes: 1, failures: 2 } };
  expect(inverseOf({ action: 'heal', amount: 1 }, view(0, 0, dying), view(1, 0))).toBeUndefined();
  expect(inverseOf({ action: 'setHitPoints', amount: 5 }, view(0, 0, dying), view(5, 0))).toBeUndefined();
  // With no death saves recorded there is nothing to lose, so the heal is undone as usual.
  expect(inverseOf({ action: 'heal', amount: 1 }, view(0, 0, { deathSaves: { successes: 0, failures: 0 } }), view(1, 0))!.inverse).toEqual([{ action: 'setHitPoints', amount: 0 }]);
  expect(inverseOf({ action: 'heal', amount: 1 }, view(0, 0), view(1, 0))!.inverse).toEqual([{ action: 'setHitPoints', amount: 0 }]);
});

it('names the condition in the label', () => {
  expect(inverseOf({ action: 'addCondition', condition: 'prone' }, view(1, 0), view(1, 0))!.label).toBe('add condition prone');
  expect(inverseOf({ action: 'removeCondition', condition: 'prone' }, view(1, 0, { conditions: ['prone'] }), view(1, 0))!.label).toBe('remove condition prone');
});

it('has no inverse for a condition change that changed nothing (a stale double click)', () => {
  expect(inverseOf({ action: 'addCondition', condition: 'prone' }, view(1, 0, { conditions: ['prone'] }), view(1, 0, { conditions: ['prone'] }))).toBeUndefined();
  expect(inverseOf({ action: 'removeCondition', condition: 'prone' }, view(1, 0), view(1, 0))).toBeUndefined();
  expect(inverseOf({ action: 'removeCondition', condition: 'prone' }, view(1, 0, { conditions: ['prone'] }), view(1, 0))!.inverse).toEqual([{ action: 'addCondition', condition: 'prone' }]);
});

const withToggle = (resourceId?: string): CharacterView => {
  const v = view(1, 0);
  (v.sheet as unknown as { toggles: unknown[] }).toggles = [{ content: { contentId: 'c' }, toggleId: 't', on: false, resourceId }];
  return v;
};

it('undoes turning on a resource-backed toggle by turning it off and giving the use back', () => {
  expect(inverseOf({ action: 'toggleOn', contentId: 'c', toggleId: 't' }, withToggle('rage'), withToggle('rage'))!.inverse).toEqual([
    { action: 'toggleOff', contentId: 'c', toggleId: 't' },
    { action: 'regain', amount: 1, contentId: 'c', resourceId: 'rage' },
  ]);
});

it('does not undo turning off a resource-backed toggle, which would spend a use the player did not spend', () => {
  expect(inverseOf({ action: 'toggleOff', contentId: 'c', toggleId: 't' }, withToggle('rage'), withToggle('rage'))).toBeUndefined();
});

it('keeps the plain mirror for a toggle without a resource', () => {
  expect(inverseOf({ action: 'toggleOff', contentId: 'c', toggleId: 't' }, withToggle(), withToggle())!.inverse).toEqual([{ action: 'toggleOn', contentId: 'c', toggleId: 't' }]);
});

const withToggles = (...toggles: Record<string, unknown>[]): CharacterView => {
  const v = view(1, 0);
  (v.sheet as unknown as { toggles: unknown[] }).toggles = toggles.map((t) => ({ content: { contentId: 'c' }, on: false, ...t }));
  return v;
};

it('undo of a toggle with a cost regains the cost from its pool', () => {
  const v = withToggles({ toggleId: 't', resourceId: 'focus', cost: 3, costResourceContent: 'pool' });
  expect(inverseOf({ action: 'toggleOn', contentId: 'c', toggleId: 't' }, v, v)!.inverse).toEqual([
    { action: 'toggleOff', contentId: 'c', toggleId: 't' },
    { action: 'regain', amount: 3, contentId: 'pool', resourceId: 'focus' },
  ]);
});

it('a cost of 0 regains nothing, and a toggle with only a pool regains the default 1 from that pool', () => {
  const free = withToggles({ toggleId: 't', resourceId: 'focus', cost: 0, costResourceContent: 'pool' });
  expect(inverseOf({ action: 'toggleOn', contentId: 'c', toggleId: 't' }, free, free)!.inverse).toEqual([{ action: 'toggleOff', contentId: 'c', toggleId: 't' }]);
  const pooled = withToggles({ toggleId: 't', resourceId: 'focus', costResourceContent: 'pool' });
  expect(inverseOf({ action: 'toggleOn', contentId: 'c', toggleId: 't' }, pooled, pooled)!.inverse).toEqual([
    { action: 'toggleOff', contentId: 'c', toggleId: 't' },
    { action: 'regain', amount: 1, contentId: 'pool', resourceId: 'focus' },
  ]);
});

it('a compound action is marked compound and gets no client inverse (D44)', () => {
  const v = withToggles({ toggleId: 't', resourceId: 'focus', cost: 3, costResourceContent: 'pool' });
  const replace: PlayAction = { action: 'toggleOn', contentId: 'c', toggleId: 't', replace: true };
  expect(isCompound(replace)).toBe(true);
  expect(isCompound({ action: 'toggleOn', contentId: 'c', toggleId: 't' })).toBe(false);
  expect(isCompound({ action: 'toggleOff', contentId: 'c', toggleId: 't', replace: true })).toBe(false);
  expect(isCompound({ action: 'damage', amount: 3 })).toBe(false);
  expect(inverseOf(replace, v, v)).toBeUndefined();
});

it('turning on a primary that dropped an overlay the new form does not allow has no client undo', () => {
  const before = withToggles({ toggleId: 'p', on: false, group: 'g', layer: 'primary' }, { toggleId: 'o', on: true, group: 'g', layer: 'overlay' });
  const after = withToggles({ toggleId: 'p', on: true, group: 'g', layer: 'primary' }, { toggleId: 'o', on: false, group: 'g', layer: 'overlay' });
  expect(inverseOf({ action: 'toggleOn', contentId: 'c', toggleId: 'p' }, before, after)).toBeUndefined();
  const kept = withToggles({ toggleId: 'p', on: true, group: 'g', layer: 'primary' }, { toggleId: 'o', on: true, group: 'g', layer: 'overlay' });
  expect(inverseOf({ action: 'toggleOn', contentId: 'c', toggleId: 'p' }, before, kept)!.inverse).toEqual([{ action: 'toggleOff', contentId: 'c', toggleId: 'p' }]);
});

it('turning off a primary that also dropped an overlay has no client undo, and turning off a costed toggle is not undoable', () => {
  const before = withToggles({ toggleId: 'p', on: true, group: 'g', layer: 'primary' }, { toggleId: 'o', on: true, group: 'g', layer: 'overlay' });
  const after = withToggles({ toggleId: 'p', on: false, group: 'g', layer: 'primary' }, { toggleId: 'o', on: false, group: 'g', layer: 'overlay' });
  expect(inverseOf({ action: 'toggleOff', contentId: 'c', toggleId: 'p' }, before, after)).toBeUndefined();
  const alone = withToggles({ toggleId: 'p', on: true, group: 'g', layer: 'primary' }, { toggleId: 'o', on: false, group: 'g', layer: 'overlay' });
  expect(inverseOf({ action: 'toggleOff', contentId: 'c', toggleId: 'p' }, alone, alone)!.inverse).toEqual([{ action: 'toggleOn', contentId: 'c', toggleId: 'p' }]);
  const costed = withToggles({ toggleId: 'p', on: true, resourceId: 'focus', cost: 2, costResourceContent: 'pool' });
  expect(inverseOf({ action: 'toggleOff', contentId: 'c', toggleId: 'p' }, costed, costed)).toBeUndefined();
});

it('undoes concentrating on another spell by concentrating on the earlier one again', () => {
  expect(inverseOf({ action: 'startConcentration', contentId: 'other-spell' }, view(1, 0, concentrating(10)), view(1, 0))!.inverse).toEqual([{ action: 'startConcentration', contentId: 'fixture-spell' }]);
  expect(inverseOf({ action: 'startConcentration', contentId: 'fixture-spell' }, view(1, 0, concentrating()), view(1, 0))).toBeUndefined();
});

it('mirrors spend and regain, conditions, slots, toggles and concentration', () => {
  expect(inverseOf({ action: 'spend', amount: 1, contentId: 'c', resourceId: 'r' }, view(1, 0), view(1, 0))!.inverse).toEqual([{ action: 'regain', amount: 1, contentId: 'c', resourceId: 'r' }]);
  expect(inverseOf({ action: 'addCondition', condition: 'prone' }, view(1, 0), view(1, 0))!.inverse).toEqual([{ action: 'removeCondition', condition: 'prone' }]);
  expect(inverseOf({ action: 'spendSlot', amount: 2 }, view(1, 0), view(1, 0))!.inverse).toEqual([{ action: 'regainSlot', amount: 2 }]);
  expect(inverseOf({ action: 'toggleOn', contentId: 'c', toggleId: 't' }, view(1, 0), view(1, 0))!.inverse).toEqual([{ action: 'toggleOff', contentId: 'c', toggleId: 't' }]);
  expect(inverseOf({ action: 'startConcentration', contentId: 'c' }, view(1, 0), view(1, 0))!.inverse).toEqual([{ action: 'endConcentration' }]);
});

it('restores the previous exhaustion and inspiration', () => {
  expect(inverseOf({ action: 'setExhaustion', amount: 3 }, view(1, 0, { exhaustion: 1 }), view(1, 0, { exhaustion: 3 }))!.inverse).toEqual([{ action: 'setExhaustion', amount: 1 }]);
  expect(inverseOf({ action: 'setInspiration', amount: 1 }, view(1, 0, { inspiration: false }), view(1, 0, { inspiration: true }))!.inverse).toEqual([{ action: 'setInspiration', amount: 0 }]);
});

it('has no inverse for death saves and for ending concentration', () => {
  expect(inverseOf({ action: 'recordDeathSave', amount: 12 }, view(0, 0), view(0, 0))).toBeUndefined();
  expect(inverseOf({ action: 'endConcentration' }, view(1, 0), view(1, 0))).toBeUndefined();
});

// Wave 3 item 23 (D23): a companion's hit points are put back exactly, current and temporary both.
const withCompanion = (play?: { current?: number | null; temporary: number }): CharacterView =>
  ({
    character: { companions: [{ id: 'k1', name: 'Testy Hound', hitPointMaximum: 20 }], play: { temporaryHitPoints: 0, resources: [], conditions: [], exhaustion: 0, ...(play ? { companions: [{ id: 'k1', ...play }] } : {}) } },
    sheet: { hitPoints: { maximum: 35, current: 35, temporary: 0 } },
  }) as unknown as CharacterView;

it('undoes companion damage, healing and setting with an exact companionSetHitPoints (current and temporary)', () => {
  const damaged = inverseOf({ action: 'companionDamage', companionId: 'k1', amount: 9 }, withCompanion({ current: 15, temporary: 4 }), withCompanion({ current: 10, temporary: 0 }))!;
  expect(damaged.label).toBe('companion damage 9');
  expect(damaged.inverse).toEqual([{ action: 'companionSetHitPoints', companionId: 'k1', amount: 15, temporary: 4 }]);
  const healed = inverseOf({ action: 'companionHeal', companionId: 'k1', amount: 5 }, withCompanion({ current: 3, temporary: 2 }), withCompanion({ current: 8, temporary: 2 }))!;
  expect(healed.inverse).toEqual([{ action: 'companionSetHitPoints', companionId: 'k1', amount: 3, temporary: 2 }]);
  const set = inverseOf({ action: 'companionSetHitPoints', companionId: 'k1', amount: 1, temporary: 7 }, withCompanion({ current: 12, temporary: 0 }), withCompanion({ current: 1, temporary: 7 }))!;
  expect(set.inverse).toEqual([{ action: 'companionSetHitPoints', companionId: 'k1', amount: 12, temporary: 0 }]);
});

it('undoes a companion temporary-only set with an exact companionSetHitPoints (current and temporary)', () => {
  const set = inverseOf({ action: 'companionSetTemporaryHitPoints', companionId: 'k1', amount: 5 }, withCompanion({ current: 12, temporary: 1 }), withCompanion({ current: 12, temporary: 5 }))!;
  expect(set.inverse).toEqual([{ action: 'companionSetHitPoints', companionId: 'k1', amount: 12, temporary: 1 }]);
  expect(inverseOf({ action: 'companionSetTemporaryHitPoints', companionId: 'k1', amount: 5 }, withCompanion({ current: 12, temporary: 5 }), withCompanion({ current: 12, temporary: 5 }))).toBeUndefined();
});

it('reads a companion with no recorded hit points as at its maximum with none temporary, and offers nothing when nothing changed', () => {
  const entry = inverseOf({ action: 'companionDamage', companionId: 'k1', amount: 6 }, withCompanion(), withCompanion({ current: 14, temporary: 0 }))!;
  expect(entry.inverse).toEqual([{ action: 'companionSetHitPoints', companionId: 'k1', amount: 20, temporary: 0 }]);
  expect(inverseOf({ action: 'companionHeal', companionId: 'k1', amount: 6 }, withCompanion(), withCompanion())).toBeUndefined();
  expect(inverseOf({ action: 'companionDamage', companionId: 'gone', amount: 6 }, withCompanion(), withCompanion({ current: 14, temporary: 0 }))).toBeUndefined();
});

// Wave 3 item 25 (D23): charges are put back by the other direction with the same amount on the same entry.
const withUses = (uses: unknown[]): CharacterView => ({ character: { play: { temporaryHitPoints: 0, resources: [], conditions: [], exhaustion: 0 } }, sheet: { itemUses: uses } }) as unknown as CharacterView;
const wandUse = (entryId?: string) => ({ item: { contentId: 'c-wand', revisionId: 'r-wand' }, entryIndex: 1, ...(entryId ? { entryId } : {}) });

it('undoes spending a charge with the regain of the same amount on the same entry, and the reverse', () => {
  const spent = inverseOf({ action: 'spendCharge', amount: 2, entryId: 'e1' }, withUses([wandUse('e1')]), withUses([wandUse('e1')]))!;
  expect(spent.label).toBe('spend charge 2');
  expect(spent.inverse).toEqual([{ action: 'regainCharge', amount: 2, entryId: 'e1' }]);
  expect(inverseOf({ action: 'regainCharge', amount: 1, entryId: 'e1' }, withUses([wandUse('e1')]), withUses([wandUse('e1')]))!.inverse).toEqual([
    { action: 'spendCharge', amount: 1, entryId: 'e1' },
  ]);
});

it('names the id an entry was given by the change it undoes, and offers nothing when the entry is not in the view after', () => {
  // Spent by position and item while the entry had no id; the view after shows the id the service gave it.
  const action = { action: 'spendCharge' as const, amount: 1, entryIndex: 1, contentId: 'c-wand' };
  expect(inverseOf(action, withUses([wandUse()]), withUses([wandUse('given')]))!.inverse).toEqual([{ action: 'regainCharge', amount: 1, entryId: 'given' }]);
  expect(inverseOf(action, withUses([wandUse()]), withUses([]))).toBeUndefined();
});
