import { describe, expect, it } from 'vitest';
import type { Effect } from './api/types';
import {
  anyOne,
  isClassBasic,
  nextScaleKey,
  parseSlotRows,
  parseTwenty,
  parseWholeNumbers,
  setAnyOne,
  setChoice,
  setHitDie,
  setPrerequisite,
  toggleSave,
} from './classBasics';

// A class as an author might have it before opening the class editor: the editor's own effects, and others it must
// keep exactly where they are (M5 slice 1b review: nothing the editor does not show is lost or reordered).
const existing: Effect[] = [
  { type: 'hitDie', id: 'hit-die', die: 10 },
  { type: 'grant', id: 'armor', grant: 'proficiency', target: 'armor.light' },
  { type: 'restriction', id: 'pb', field: 'proficiencyBonus', minimum: 2 },
  { type: 'restriction', id: 'str-plain', field: 'ability.str.score', minimum: 11 },
  { type: 'restriction', id: 'other-group', field: 'ability.dex.score', minimum: 13, multiclass: true, group: 'agile' },
  { type: 'choice', id: 'style', choiceId: 'fighting-style', count: 1, options: [] },
  { type: 'resource', id: 'r', resourceId: 'r', label: 'Ink', maximum: 'SCALE.ink' },
];
const others = (effects: Effect[]) => effects.filter((e) => !isClassBasic(e) || e.id === 'other-group');

describe('isClassBasic', () => {
  it('owns only what the class editor shows', () => {
    expect(existing.filter(isClassBasic).map((e) => e.id)).toEqual(['hit-die', 'other-group']);
  });
});

describe('class editor edits', () => {
  it('keep every other effect and its place', () => {
    let effects = setHitDie(existing, 8);
    effects = toggleSave(effects, 'int');
    effects = setPrerequisite(effects, 'int', 13);
    effects = setAnyOne(effects, false);
    effects = setChoice(effects, 'subclass', { type: 'choice', id: 'subclass', choiceId: 'subclass', count: 1, options: [], level: 3 });
    expect(others(effects).map((e) => e.id)).toEqual(['armor', 'pb', 'str-plain', 'other-group', 'style', 'r']);
    expect(effects[0]).toEqual({ type: 'hitDie', id: 'hit-die', die: 8 });
    expect(effects.find((e) => e.id === 'other-group')).toEqual(existing[4]); // its own group name is kept
    expect(setAnyOne(effects, true).find((e) => e.id === 'other-group')).toEqual(existing[4]);
  });

  it('never turns a plain restriction on an ability into a multiclass one', () => {
    const effects = setPrerequisite(existing, 'str', 13);
    expect(effects.find((e) => e.id === 'str-plain')).toEqual(existing[3]);
    expect(effects.filter((e) => e.type === 'restriction' && e.field === 'ability.str.score')).toHaveLength(2);
  });

  it('keeps "any one of these" when a prerequisite is cleared and typed again', () => {
    let effects: Effect[] = [];
    effects = setPrerequisite(effects, 'int', 13);
    effects = setPrerequisite(effects, 'wis', 13);
    effects = setAnyOne(effects, true);
    effects = setPrerequisite(effects, 'wis', undefined); // backspacing the field to empty
    expect(anyOne(effects)).toBe(true);
    effects = setPrerequisite(effects, 'wis', 15);
    expect(effects.filter((e) => e.type === 'restriction').map((e) => (e.type === 'restriction' ? e.group : ''))).toEqual(['multiclass', 'multiclass']);
  });

  it('toggles a save and gives new effects ids no other effect uses', () => {
    const withSave = toggleSave([{ type: 'grant', id: 'save-int', grant: 'content' }], 'int');
    expect(withSave.map((e) => e.id)).toEqual(['save-int', 'save-int-2']);
    expect(toggleSave(withSave, 'int').map((e) => e.id)).toEqual(['save-int']);
  });

  it('replaces the skills and subclass choices in place', () => {
    const skills: Effect = { type: 'choice', id: 'skills', choiceId: 'skills', count: 1, options: [] };
    const effects = setChoice(setChoice(existing, 'skills', skills), 'skills', { ...skills, count: 2 });
    expect(effects.filter((e) => e.type === 'choice' && e.choiceId === 'skills')).toEqual([{ ...skills, count: 2 }]);
    expect(setChoice(effects, 'skills', null)).toEqual(existing);
  });
});

describe('nextScaleKey', () => {
  it('never repeats a key in use', () => {
    const scale = (scaleId: string): Effect => ({ type: 'scale', id: scaleId, scaleId, label: '', values: [] });
    expect(nextScaleKey([])).toBe('column1');
    expect(nextScaleKey([scale('column2')])).toBe('column1'); // column1 was removed
    expect(nextScaleKey([scale('column1'), scale('column2')])).toBe('column3');
  });
});

describe('strict number lists', () => {
  it('reads commas or spaces and refuses anything else instead of dropping it', () => {
    expect(parseWholeNumbers('4, 3, 2', 20)).toEqual({ values: [4, 3, 2] });
    expect(parseWholeNumbers('4 3 2', 20)).toEqual({ values: [4, 3, 2] });
    expect(parseWholeNumbers('4, 3,', 20)).toEqual({ values: [4, 3] }); // still typing
    expect(parseWholeNumbers('4, 3, x, 2', 20).problem).toMatch(/"x"/);
    expect(parseWholeNumbers('4, 3, , 2', 20).problem).toMatch(/empty/); // never [4, 3, 2]
    expect(parseWholeNumbers('4, 3 2', 20).problem).toMatch(/"3 2"/);
    expect(parseWholeNumbers('4; 3', 20).problem).toMatch(/"4;"/);
    expect(parseWholeNumbers('2.5', 20).problem).toMatch(/"2.5"/);
    expect(parseWholeNumbers('21', 20).problem).toMatch(/0 to 20/);
  });

  it('needs exactly 20 values for a column or a caster table', () => {
    expect(parseTwenty(Array(20).fill('1').join(', '), 10000).problem).toBeUndefined();
    expect(parseTwenty('1, 2', 10000).problem).toMatch(/2 values given; 20 are needed/);
  });
});

describe('parseSlotRows', () => {
  it('keeps each row in its place and pads to 20 levels', () => {
    const { rows, problem } = parseSlotRows('2\n3, 1\n\n4, 2\n');
    expect(problem).toBeUndefined();
    expect(rows.slice(0, 4)).toEqual([[2], [3, 1], [], [4, 2]]);
    expect(rows).toHaveLength(20);
  });

  it('ignores trailing blank lines but refuses more than 20 levels', () => {
    expect(parseSlotRows(`${Array(20).fill('1').join('\n')}\n\n`).problem).toBeUndefined();
    expect(parseSlotRows(Array(21).fill('1').join('\n')).problem).toMatch(/21 lines/);
  });

  it('names the class level of a typo instead of moving slots to another spell level', () => {
    expect(parseSlotRows('4, 3\n4, 3, , 2').problem).toMatch(/^Class level 2: a value between two commas is empty/);
    expect(parseSlotRows('4, 3\n4, 3, x, 2').problem).toMatch(/^Class level 2: "x"/);
    expect(parseSlotRows('1, 1, 1, 1, 1, 1, 1, 1, 1, 1').problem).toMatch(/at most 9 spell levels/);
  });
});
