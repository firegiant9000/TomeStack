// @vitest-environment jsdom
// D74: a level-up is diffed into a list of ids, worded from the current sheet, and kept in the page's own storage.
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CharacterView, ChoiceStatus, FeatureEntry, ResourceValue, SlotValue, ToggleValue } from './api/types';
import { checklistRows, diffLevelUp, dismissChecklist, onlyHitPoints, readChecklist, writeChecklist, type LevelUpChange, type LevelUpList } from './levelUpChecklist';

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

const ref = (contentId: string, revisionId = 'r1') => ({ contentId, revisionId });
const feature = (contentId: string, name: string, automation = 'automatic', revisionId = 'r1') =>
  ({ content: ref(contentId, revisionId), name, kind: 'feature', automation, effects: [], diagnostics: [] }) as unknown as FeatureEntry;
const choice = (contentId: string, choiceId: string, over: object = {}) =>
  ({ source: ref(contentId), sourceName: 'Fixture Class', choiceId, count: 1, options: [], selected: [], resolved: false, ...over }) as unknown as ChoiceStatus;
const pool = (contentId: string, resourceId: string, over: object = {}) =>
  ({ content: ref(contentId), contentName: 'Fixture Source', effectId: 'e', resourceId, label: 'Fixture Pool', spent: 0, trace: [], warnings: [], automation: 'automatic', recoveries: [], ...over }) as unknown as ResourceValue;
const toggle = (contentId: string, toggleId: string, over: object = {}) =>
  ({ content: ref(contentId), contentName: 'Fixture Deep Form', effectId: 'e', toggleId, label: 'Tide pool', on: false, ...over }) as unknown as ToggleValue;
const slot = (level: number, maximum: number) => ({ level, maximum, spent: 0, remaining: maximum, field: `slots.${level}` }) as SlotValue;
const view = (level: number, sheet: object = {}): CharacterView => ({ character: { id: 'fixture', level }, sheet: { characterId: 'fixture', ...sheet } }) as unknown as CharacterView;
const rowsOf = (before: CharacterView, after: CharacterView) => checklistRows(diffLevelUp(before, after), after.sheet);
const texts = (rows: { text: string }[]) => rows.map((r) => r.text);

describe('diffLevelUp and checklistRows', () => {
  it('lists the feature, choice, hit points and form of a level, in order', () => {
    const before = view(4, { features: [feature('base', 'Fixture Base')], hitPoints: { maximum: 38, current: 38, temporary: 0 } });
    const after = view(5, {
      features: [feature('base', 'Fixture Base'), feature('surge', 'Fixture Surge')],
      choices: [choice('cls', 'pick')],
      hitPoints: { maximum: 45, current: 45, temporary: 0 },
      toggles: [toggle('deep', 'deep', { group: 'forms' })],
    });
    const list = diffLevelUp(before, after);
    expect(list.level).toBe(5);
    const rows = checklistRows(list, after.sheet);
    expect(texts(rows)).toEqual(['New feature: Fixture Surge', 'New choice: Fixture Class: choose 1 (still to make)', 'Hit point maximum: 38 → 45', 'New form available: Fixture Deep Form']);
    expect(rows.map((r) => r.target)).toEqual([undefined, 'choices', 'play', 'play']);
    expect(onlyHitPoints(rows)).toBe(false);
  });

  it('says only hit points for a quiet level', () => {
    const rows = rowsOf(view(1, { hitPoints: { maximum: 10, current: 10, temporary: 0 } }), view(2, { hitPoints: { maximum: 17, current: 17, temporary: 0 } }));
    expect(texts(rows)).toEqual(['Hit point maximum: 10 → 17']);
    expect(onlyHitPoints(rows)).toBe(true);
    expect(onlyHitPoints([])).toBe(false);
  });

  it('diffs two minimal views without throwing', () => {
    expect(diffLevelUp(view(1), view(2))).toEqual({ level: 2, changes: [] });
  });

  it('lists hit points only when both sides have them', () => {
    expect(diffLevelUp(view(1), view(2, { hitPoints: { maximum: 9, current: 9, temporary: 0 } })).changes).toEqual([]);
  });

  it('does not list a feature at a new revision', () => {
    const rows = rowsOf(view(1, { features: [feature('a', 'Fixture A', 'automatic', 'r1')] }), view(2, { features: [feature('a', 'Fixture A', 'automatic', 'r2')] }));
    expect(rows).toEqual([]);
  });

  it('skips an already unresolved choice, and words a resolved new one as made with no target', () => {
    const before = view(1, { choices: [choice('cls', 'old')] });
    const after = view(2, { choices: [choice('cls', 'old'), choice('cls', 'fresh', { resolved: true, count: 2 })] });
    const rows = rowsOf(before, after);
    expect(texts(rows)).toEqual(['New choice: Fixture Class: choose 2 (made)']);
    expect(rows[0]!.target).toBeUndefined();
  });

  it('lists a new class at level 1 and uses the total level (multiclass)', () => {
    const before = view(3, { features: [feature('a', 'Fixture A')], choices: [choice('a', 'x', { resolved: true })] });
    const after = view(4, { features: [feature('a', 'Fixture A'), feature('b1', 'Fixture Opener')], choices: [choice('a', 'x', { resolved: true }), choice('b', 'y', { sourceName: 'Fixture Second' })] });
    const list = diffLevelUp(before, after);
    expect(list.level).toBe(4);
    expect(texts(checklistRows(list, after.sheet))).toEqual(['New feature: Fixture Opener', 'New choice: Fixture Second: choose 1 (still to make)']);
  });

  it('lists changed spell slot levels only, a missing level counting as 0', () => {
    const rows = rowsOf(view(2, { spellSlots: [slot(1, 4), slot(2, 2)] }), view(3, { spellSlots: [slot(1, 4), slot(2, 3)] }));
    expect(texts(rows)).toEqual(['Level 2 spell slots: 2 → 3']);
    expect(rows[0]!.target).toBe('spells');
    expect(texts(rowsOf(view(1, { spellSlots: [] }), view(2, { spellSlots: [slot(1, 2)] })))).toEqual(['Level 1 spell slots: 0 → 2']);
    expect(texts(rowsOf(view(1, { spellSlots: [slot(1, 2)] }), view(2, {})))).toEqual(['Level 1 spell slots: 2 → 0']);
  });

  it('words pact slots: new, changed level, unchanged, lost', () => {
    const pact = (maximum: number, level: number) => ({ pactSlots: { ...slot(level, maximum) } });
    expect(texts(rowsOf(view(1), view(2, pact(1, 1))))).toEqual(['New pact slots: 1 at level 1']);
    expect(texts(rowsOf(view(2, pact(2, 1)), view(3, pact(2, 2))))).toEqual(['Pact slots: 2 at level 1 → 2 at level 2']);
    expect(rowsOf(view(2, pact(2, 1)), view(3, pact(2, 1)))).toEqual([]);
    expect(texts(rowsOf(view(2, pact(2, 1)), view(3)))).toEqual(['Pact slots: 2 at level 1 → none']);
  });

  it('words new and grown pools', () => {
    const allowance = pool('p', 'turn', { maximum: 1, allowance: true });
    expect(texts(rowsOf(view(1), view(2, { resources: [allowance] }))))
      .toEqual(['New pool: Fixture Pool, 1, refreshes each round']);
    const short = pool('p', 'burst', { maximum: 2, recoveries: [{ effectId: 'r', on: 'shortRest', amount: 'all' }] });
    expect(texts(rowsOf(view(1), view(2, { resources: [short] })))).toEqual(['New pool: Fixture Pool, 2, refreshes on a short rest']);
    const long = pool('p', 'rest', { maximum: 2, recoveries: [{ effectId: 'r', on: 'longRest', amount: 'all' }] });
    expect(texts(rowsOf(view(1), view(2, { resources: [long] })))).toEqual(['New pool: Fixture Pool, 2, refreshes on a long rest']);
    const both = pool('p', 'both', { maximum: 2, recoveries: [{ effectId: 'r', on: 'longRest', amount: 'all' }, { effectId: 's', on: 'shortRest', amount: 'all' }] });
    expect(texts(rowsOf(view(1), view(2, { resources: [both] })))).toEqual(['New pool: Fixture Pool, 2, refreshes on a short rest']);
    expect(texts(rowsOf(view(1), view(2, { resources: [pool('p', 'plain', { maximum: 3 })] })))).toEqual(['New pool: Fixture Pool, 3']);
    expect(texts(rowsOf(view(1), view(2, { resources: [pool('p', 'free')] })))).toEqual(['New pool: Fixture Pool']);
    const grown = rowsOf(view(1, { resources: [pool('p', 'a', { maximum: 3 })] }), view(2, { resources: [pool('p', 'a', { maximum: 4 })] }));
    expect(texts(grown)).toEqual(['Fixture Pool: 3 → 4']);
    expect(grown[0]!.target).toBe('play');
  });

  it('does not list a pool whose maximum is unknown on a side', () => {
    expect(rowsOf(view(1, { resources: [pool('p', 'a')] }), view(2, { resources: [pool('p', 'a')] }))).toEqual([]);
    expect(rowsOf(view(1, { resources: [pool('p', 'a')] }), view(2, { resources: [pool('p', 'a', { maximum: 4 })] }))).toEqual([]);
    expect(rowsOf(view(1, { resources: [pool('p', 'a', { maximum: 4 })] }), view(2, { resources: [pool('p', 'a')] }))).toEqual([]);
  });

  it('words toggles, and lists two grouped toggles of one content once', () => {
    expect(texts(rowsOf(view(1), view(2, { toggles: [toggle('c', 't')] })))).toEqual(['New toggle: Tide pool']);
    const twins = [toggle('deep', 'one', { group: 'forms' }), toggle('deep', 'two', { group: 'forms' })];
    const rows = rowsOf(view(1), view(2, { toggles: twins }));
    expect(texts(rows)).toEqual(['New form available: Fixture Deep Form']);
    expect(rows[0]!.key).toBe('toggle:deep:one');
  });

  it('keeps two distinct new choices and two pools that share their wording', () => {
    const choices = rowsOf(view(1), view(2, { choices: [choice('cls', 'one'), choice('cls', 'two')] }));
    expect(texts(choices)).toEqual(['New choice: Fixture Class: choose 1 (still to make)', 'New choice: Fixture Class: choose 1 (still to make)']);
    const pools = rowsOf(view(1), view(2, { resources: [pool('p', 'a', { maximum: 1 }), pool('p', 'b', { maximum: 1 })] }));
    expect(texts(pools)).toHaveLength(2);
  });

  it('closes with the by-hand row for reference and assisted features', () => {
    const after = view(2, {
      features: [feature('a', 'A', 'reference'), feature('z', 'Auto One'), feature('b', 'B', 'assisted')],
      hitPoints: { maximum: 12, current: 12, temporary: 0 },
    });
    const rows = rowsOf(view(1, { hitPoints: { maximum: 8, current: 8, temporary: 0 } }), after);
    expect(texts(rows)).toEqual(['New feature: A', 'New feature: Auto One', 'New feature: B', 'Hit point maximum: 8 → 12', 'By hand from now on: A, B']);
  });

  it('drops a by-hand feature missing from the current sheet, and omits the row when none remain', () => {
    const list: LevelUpList = { level: 2, changes: [{ kind: 'feature', contentId: 'a' }, { kind: 'feature', contentId: 'gone' }] };
    const sheet = view(2, { features: [feature('a', 'A', 'reference')] }).sheet;
    expect(texts(checklistRows(list, sheet))).toEqual(['New feature: A', 'By hand from now on: A']);
    expect(checklistRows({ level: 2, changes: [{ kind: 'feature', contentId: 'gone' }] }, view(2, {}).sheet)).toEqual([]);
  });
});

describe('page storage', () => {
  const changes: LevelUpChange[] = [
    { kind: 'feature', contentId: 'a' },
    { kind: 'choice', contentId: 'a', choiceId: 'x' },
    { kind: 'hitPoints', before: 1, after: 2 },
    { kind: 'slots', level: 1, before: 2, after: 3 },
    { kind: 'pact', after: { maximum: 1, level: 1 } },
    { kind: 'pact', before: { maximum: 1, level: 1 } },
    { kind: 'pool', contentId: 'a', resourceId: 'r', after: 2 },
    { kind: 'pool', contentId: 'a', resourceId: 'r', before: 2, after: 3 },
    { kind: 'toggle', contentId: 'a', toggleId: 't' },
  ];
  const keyFor = (id: string) => `tomestack.levelUpChecklist.${id}`;

  it('round-trips under its own key', () => {
    writeChecklist('c1', { level: 5, changes });
    expect(readChecklist('c1', 5)).toEqual({ level: 5, changes });
    expect(JSON.parse(localStorage.getItem(keyFor('c1'))!)).toEqual({ level: 5, changes });
    expect(readChecklist('c2', 5)).toBeUndefined();
  });

  it('ignores a list for another level', () => {
    writeChecklist('c1', { level: 5, changes });
    expect(readChecklist('c1', 6)).toBeUndefined();
  });

  it.each([
    ['broken json', '{'],
    ['null', 'null'],
    ['an array', '[]'],
    ['changes not a list', JSON.stringify({ level: 5, changes: 'x' })],
    ['level 21', JSON.stringify({ level: 21, changes: [] })],
    ['level 0', JSON.stringify({ level: 0, changes: [] })],
    ['a fractional level', JSON.stringify({ level: 5.5, changes: [] })],
  ])('reads %s as no list', (_name, stored) => {
    localStorage.setItem(keyFor('c1'), stored);
    expect(readChecklist('c1', 5)).toBeUndefined();
    expect(readChecklist('c1', 21)).toBeUndefined();
  });

  it('keeps well-formed changes and drops the rest, with only the known fields', () => {
    localStorage.setItem(
      keyFor('c1'),
      JSON.stringify({
        level: 5,
        changes: [
          { kind: 'feature', contentId: 'a', extra: 1 },
          { kind: 'bogus', contentId: 'a' },
          { kind: 'feature', contentId: 7 },
          { kind: 'hitPoints', before: 1, after: null },
          { kind: 'pool', contentId: 'a', resourceId: 'r', before: 'x' },
          { kind: 'pact', after: { maximum: 1 } },
          { kind: 'pact' },
          { kind: 'pool', contentId: 'a', resourceId: 'r', before: 3 },
          null,
          'text',
          { kind: 'toggle', contentId: 'a', toggleId: 't' },
        ],
      }),
    );
    expect(readChecklist('c1', 5)).toEqual({ level: 5, changes: [{ kind: 'feature', contentId: 'a' }, { kind: 'toggle', contentId: 'a', toggleId: 't' }] });
  });

  it('caps a long list at 64 on read and on write', () => {
    const many = Array.from({ length: 100 }, (_, i) => ({ kind: 'feature', contentId: `f${i}` }) as LevelUpChange);
    localStorage.setItem(keyFor('c1'), JSON.stringify({ level: 5, changes: many }));
    expect(readChecklist('c1', 5)!.changes).toHaveLength(64);
    writeChecklist('c2', { level: 5, changes: many });
    expect(JSON.parse(localStorage.getItem(keyFor('c2'))!).changes).toHaveLength(64);
  });

  it('dismiss removes the key', () => {
    writeChecklist('c1', { level: 5, changes });
    dismissChecklist('c1');
    expect(localStorage.getItem(keyFor('c1'))).toBeNull();
    expect(readChecklist('c1', 5)).toBeUndefined();
  });

  it('survives no storage at all', () => {
    vi.stubGlobal('localStorage', undefined);
    try {
      expect(readChecklist('c1', 5)).toBeUndefined();
      expect(() => writeChecklist('c1', { level: 5, changes })).not.toThrow();
      expect(() => dismissChecklist('c1')).not.toThrow();
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it('survives a storage that throws', () => {
    const boom = () => {
      throw new Error('storage unavailable');
    };
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(boom);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(boom);
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(boom);
    expect(readChecklist('c1', 5)).toBeUndefined();
    expect(() => writeChecklist('c1', { level: 5, changes })).not.toThrow();
    expect(() => dismissChecklist('c1')).not.toThrow();
  });
});
