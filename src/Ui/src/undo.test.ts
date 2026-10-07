// @vitest-environment jsdom
// D23: the inverse of a play action, computed from the view before and after, through the same confirmed commands.
import { expect, it } from 'vitest';
import type { CharacterView } from './api/types';
import { inverseOf } from './undo';

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

it('undoes heal by setting hit points back, without a temporary step when nothing changed', () => {
  expect(inverseOf({ action: 'heal', amount: 4 }, view(20, 0), view(24, 0))!.inverse).toEqual([{ action: 'setHitPoints', amount: 20 }]);
});

it('clears the concentration check that damage raised (R19)', () => {
  const entry = inverseOf({ action: 'damage', amount: 12 }, view(35, 0, concentrating()), view(23, 0, concentrating(10)))!;
  expect(entry.inverse).toEqual([{ action: 'setHitPoints', amount: 35 }, { action: 'clearConcentrationCheck' }]);
});

it('starts concentration again when damage to 0 ended it, after the hit points are back (R19)', () => {
  const entry = inverseOf({ action: 'damage', amount: 40 }, view(10, 0, concentrating()), view(0, 0))!;
  expect(entry.inverse).toEqual([{ action: 'setHitPoints', amount: 10 }, { action: 'startConcentration', contentId: 'fixture-spell' }]);
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
