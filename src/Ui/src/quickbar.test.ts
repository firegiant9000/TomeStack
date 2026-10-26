// @vitest-environment jsdom
// D73: pins live in the page's own storage per character, bad storage never breaks the sheet, and a pin finds its action on the sheet.
import { afterEach, expect, it, vi } from 'vitest';
import type { AttackEntry, CharacterView, CustomAttack, FeatureEffect, FeatureEntry, ResourceValue, ToggleValue } from './api/types';
import { attackPin, customPin, editPins, isPin, maxPins, pinKey, pinnable, readPins, resolvePin, rollPin, togglePin, visiblePins, writePins, type Pin } from './quickbar';

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

const ref = { contentId: 'c1', revisionId: 'r1' };
const pool = (extra: object = {}) =>
  ({
    content: ref, contentName: 'Testy Source', effectId: 'e-focus', resourceId: 'focus', label: 'Focus', maximum: 5, spent: 1, current: 4,
    trace: [], warnings: [], automation: 'automatic', recoveries: [], ...extra,
  }) as unknown as ResourceValue;
const effect = (extra: object = {}) =>
  ({ id: 'wave', type: 'roll', automation: 'automatic', label: 'Tide Wave', dice: '2d6', resourceId: 'focus', cost: 2, available: true, ...extra }) as unknown as FeatureEffect;
const feature = (effects: FeatureEffect[], over: object = {}) =>
  ({ content: ref, name: 'Testy Source', kind: 'feature', effects, diagnostics: [], ...over }) as unknown as FeatureEntry;
const weapon = (extra: object = {}) =>
  ({ item: { contentId: 'w1', revisionId: 'wr1' }, name: 'Longsword', effectId: 'attack', source: 'weapon', automation: 'automatic', available: true, ...extra }) as unknown as AttackEntry;
const featureAttack = (extra: object = {}) =>
  ({ item: ref, name: 'Testy Lash', effectId: 'lash', source: 'feature', automation: 'automatic', available: true, resourceId: 'focus', costResourceContent: 'c1', ...extra }) as unknown as AttackEntry;
const toggle = (extra: object = {}) =>
  ({ content: ref, contentName: 'Testy Source', effectId: 'e-form', toggleId: 'form', label: 'Tide Form', on: false, available: true, ...extra }) as unknown as ToggleValue;
const custom: CustomAttack = { id: 'k1', name: 'Fist', toHit: 3 };
const view = (sheet: object = {}, customAttacks: CustomAttack[] = [custom]): CharacterView =>
  ({ character: { id: 'fixture', customAttacks }, sheet: { resources: [pool()], features: [], attacks: [], toggles: [], ...sheet } }) as unknown as CharacterView;

it('keeps pins per character under their own key', () => {
  const pins: Pin[] = [{ kind: 'roll', contentId: 'c1', effectId: 'wave' }, { kind: 'custom', attackId: 'k1' }];
  expect(readPins('c1')).toEqual([]);
  writePins('c1', pins);
  writePins("c2", [pins[1]!]);
  expect(readPins('c1')).toEqual(pins);
  expect(readPins('c2')).toEqual([pins[1]!]);
  expect(JSON.parse(localStorage.getItem('tomestack.quickbar.c1')!)).toEqual(pins);
});

it('normalises a bad stored value to the valid pins with only the four fields', () => {
  localStorage.setItem('tomestack.quickbar.c1', 'not json');
  expect(readPins('c1')).toEqual([]);
  localStorage.setItem('tomestack.quickbar.c1', '{"kind":"roll"}');
  expect(readPins('c1')).toEqual([]);
  localStorage.setItem(
    'tomestack.quickbar.c1',
    JSON.stringify([
      { kind: 'roll', contentId: 'c1', effectId: 'a', extra: 1 },
      { kind: 'roll', contentId: 'c1' },
      { kind: 'bogus', contentId: 'c1', effectId: 'a' },
      { kind: 'custom' },
      { kind: 'roll', contentId: 'c1', effectId: 'a' },
      { kind: 'custom', attackId: 'k1' },
      null,
      'text',
    ]),
  );
  expect(readPins('c1')).toEqual([{ kind: 'roll', contentId: 'c1', effectId: 'a' }, { kind: 'custom', attackId: 'k1' }]);
});

it('reads the first 16 of a longer list and writes it normalised', () => {
  const many = Array.from({ length: 20 }, (_, i): Pin => ({ kind: 'custom', attackId: `k${i}` }));
  localStorage.setItem('tomestack.quickbar.c1', JSON.stringify(many));
  expect(readPins('c1')).toEqual(many.slice(0, 16));
  writePins('c2', many);
  expect(JSON.parse(localStorage.getItem('tomestack.quickbar.c2')!)).toHaveLength(16);
});

it('removes the key for an empty list', () => {
  writePins('c1', [{ kind: 'custom', attackId: 'k1' }]);
  writePins('c1', []);
  expect(localStorage.getItem('tomestack.quickbar.c1')).toBeNull();
});

it('treats a throwing storage as empty and swallows write failures', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  expect(readPins('c1')).toEqual([]);
  expect(() => writePins('c1', [{ kind: 'custom', attackId: 'k1' }])).not.toThrow();
});

it('keys a pin by its kind and ids, and checks the ids each kind needs', () => {
  expect(pinKey({ kind: 'roll', contentId: 'c1', effectId: 'wave' })).toBe('roll:c1:wave:');
  expect(pinKey({ kind: 'custom', attackId: 'k1' })).toBe('custom:::k1');
  expect(isPin({ kind: 'toggle', contentId: 'c1', effectId: '' })).toBe(false);
  expect(isPin({ kind: 'custom', attackId: 'k1' })).toBe(true);
});

const customPins = (n: number): Pin[] => Array.from({ length: n }, (_, i) => ({ kind: 'custom', attackId: `k${i}` }));
const customView = (n: number) => view({}, customPins(n).map((p, i) => ({ id: p.attackId!, name: `Fist ${i}`, toHit: 1 })));
const ghost = (i: number): Pin => ({ kind: 'roll', contentId: 'gone', effectId: `g${i}` });

it('editPins puts the visible pins first and keeps the hidden ones', () => {
  const v = customView(5);
  const pins = customPins(5);
  const stored = [pins[0]!, ghost(1), pins[1]!, ghost(2), pins[2]!];
  const added = editPins(v, stored, pins[3]!);
  expect(added).toEqual([pins[0]!, pins[1]!, pins[2]!, pins[3]!, ghost(1), ghost(2)]);
  expect(editPins(v, stored, pins[1]!)).toEqual([pins[0]!, pins[2]!, ghost(1), ghost(2)]);
});

it('editPins adds nothing when 8 are visible, and never stores more than 16', () => {
  const v = customView(9);
  const pins = customPins(9);
  const eight = pins.slice(0, 8);
  expect(editPins(v, eight, pins[8]!)).toEqual(eight);
  const stored = [...eight, ...Array.from({ length: 8 }, (_, i) => ghost(i))];
  expect(visiblePins(v, stored)).toEqual(eight);
  const unpinned = editPins(v, stored, pins[0]!);
  expect(unpinned).toHaveLength(15);
  const repinned = editPins(v, unpinned, pins[8]!);
  expect(repinned).toHaveLength(16);
  expect(repinned.slice(0, 8)).toEqual([...pins.slice(1, 8), pins[8]]);
  expect(visiblePins(v, repinned).length).toBeLessThanOrEqual(maxPins);
  expect(editPins(v, repinned, pins[0]!)).toHaveLength(16);
});

it('resolves a roll with dice to a target and says what it spends', () => {
  const f = feature([effect()]);
  const a = resolvePin(view({ features: [f] }), rollPin(f, f.effects[0]!))!;
  expect(a.name).toBe('Roll Tide Wave and spend 2 Focus');
  expect(a.target).toEqual({ content: ref, effectId: 'wave' });
  expect(a.action).toBeUndefined();
  expect(a.availability.usable).toBe(true);
});

it('names a roll with dice without a spend when it would not offer one', () => {
  const rollName = (e: FeatureEffect, p = pool()) => {
    const f = feature([e]);
    return resolvePin(view({ features: [f], resources: [p] }), rollPin(f, e))!.name;
  };
  expect(rollName(effect({ variableCost: true }))).toBe('Roll Tide Wave');
  expect(rollName(effect(), pool({ current: undefined }))).toBe('Roll Tide Wave');
  expect(rollName(effect({ resourceId: undefined, cost: undefined }))).toBe('Roll Tide Wave');
  expect(rollName(effect(), pool({ current: 1 }))).toBe('Roll Tide Wave');
});

it('carries the base cost for a per-unit roll', () => {
  const f = feature([effect({ perUnit: { dice: '1d6' } })]);
  expect(resolvePin(view({ features: [f] }), rollPin(f, f.effects[0]!))!.target).toEqual({ content: ref, effectId: 'wave', amount: 2 });
});

it('resolves a locked form-only roll as not usable with its reason', () => {
  const f = feature([effect({ available: false, unavailableReason: 'Available in Tide Form' })]);
  const a = resolvePin(view({ features: [f] }), rollPin(f, f.effects[0]!))!;
  expect(a.availability).toEqual({ usable: false, reasons: ['Available in Tide Form'] });
  expect(a.target).toBeDefined();
});

it('resolves a dice-less Use to a spend', () => {
  const e = effect({ dice: undefined, label: 'Guard', cost: 1 });
  const f = feature([e]);
  const a = resolvePin(view({ features: [f] }), rollPin(f, e))!;
  expect(a.name).toBe('Use Guard (1 Focus)');
  expect(a.action).toEqual({ action: 'spend', amount: 1, contentId: 'c1', resourceId: 'focus' });
  expect(a.target).toBeUndefined();
});

it('resolves a dice-less Use of an untracked pool as not usable, with no action', () => {
  const e = effect({ dice: undefined, label: 'Guard', cost: 1 });
  const f = feature([e]);
  const a = resolvePin(view({ features: [f], resources: [pool({ current: undefined })] }), rollPin(f, e))!;
  expect(a.availability.usable).toBe(false);
  expect(a.availability.reasons).toEqual(['Focus is not tracked on this sheet.']);
  expect(a.action).toBeUndefined();
  expect(a.target).toBeUndefined();
});

it('does not resolve a dice-less roll that names no pool, or a removed feature', () => {
  const e = effect({ dice: undefined, resourceId: undefined });
  const f = feature([e]);
  expect(resolvePin(view({ features: [f] }), rollPin(f, e))).toBeUndefined();
  expect(resolvePin(view(), { kind: 'roll', contentId: 'c1', effectId: 'wave' })).toBeUndefined();
});

it('finds a pinned row again under a new revision of the same content', () => {
  const f = feature([effect()], { content: { contentId: 'c1', revisionId: 'r2' } });
  const a = resolvePin(view({ features: [f] }), { kind: 'roll', contentId: 'c1', effectId: 'wave' })!;
  expect(a.target?.content).toEqual({ contentId: 'c1', revisionId: 'r2' });
});

it('resolves attacks: a weapon, a feature attack and a custom one', () => {
  const w = weapon();
  const fa = featureAttack();
  const v = view({ attacks: [w, fa] });
  const wa = resolvePin(v, attackPin(w))!;
  expect(wa.name).toBe('Roll Longsword attack');
  expect(wa.target).toEqual({ weapon: w.item });
  const faa = resolvePin(v, attackPin(fa))!;
  expect(faa.target).toEqual({ content: ref, effectId: 'lash' });
  expect(faa.availability.usable).toBe(true);
  const ca = resolvePin(v, customPin(custom))!;
  expect(ca.name).toBe('Roll Fist attack');
  expect(ca.target).toEqual({ customAttack: 'k1' });
  expect(ca.availability).toEqual({ usable: true, reasons: [] });
  expect(resolvePin(v, { kind: 'custom', attackId: 'gone' })).toBeUndefined();
});

it('resolves a feature attack the pool cannot pay as not usable', () => {
  const fa = featureAttack({ cost: 9 });
  const a = resolvePin(view({ attacks: [fa] }), attackPin(fa))!;
  expect(a.availability.usable).toBe(false);
});

it('resolves a toggle to the play change it sends, and leaves auto and grouped ones out', () => {
  const off = toggle();
  const on = toggle({ on: true });
  const pin = togglePin(off);
  const a = resolvePin(view({ toggles: [off] }), pin)!;
  expect(a.name).toBe('Turn Tide Form on');
  expect(a.action).toEqual({ action: 'toggleOn', contentId: 'c1', toggleId: 'form' });
  expect(a.target).toBeUndefined();
  const b = resolvePin(view({ toggles: [on] }), pin)!;
  expect(b.name).toBe('Turn Tide Form off');
  expect(b.action).toEqual({ action: 'toggleOff', contentId: 'c1', toggleId: 'form' });
  expect(resolvePin(view({ toggles: [toggle({ auto: true })] }), pin)).toBeUndefined();
  expect(resolvePin(view({ toggles: [toggle({ group: 'forms', groupContent: 'c1', layer: 'primary' })] }), pin)).toBeUndefined();
  expect(pinnable(view({ toggles: [off] }), pin)).toBe(true);
  expect(pinnable(view(), pin)).toBe(false);
});
