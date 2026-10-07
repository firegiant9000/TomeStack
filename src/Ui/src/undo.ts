import type { CharacterView, PlayAction } from './api/types';

export interface UndoEntry {
  /** What is undone, for the button's name ("damage 12"). */
  label: string;
  /** The confirmed commands that put the state back, in order. */
  inverse: PlayAction[];
  /** The view this change produced. The entry is offered only while the sheet still shows it (a rest, save or reload makes it stale). */
  after: CharacterView;
}

const hp = (view: CharacterView) => view.sheet.hitPoints;

/**
 * D23 (owner, 2026-10-06): undo is the inverse play command, computed from the view before and after, never a stored
 * history. Hit points are put back with setHitPoints (exact), so an undone damage does not re-absorb temporary hit points.
 * Damage that raised a concentration check clears it, and damage to 0 that ended concentration starts it again.
 * Death saves, ending concentration and clearing its check have no exact inverse and are not undoable.
 */
export function inverseOf(action: PlayAction, before: CharacterView, after: CharacterView): UndoEntry | undefined {
  const label = `${action.action === 'setTemporaryHitPoints' ? 'temporary hit points' : action.action.replace(/[A-Z]/g, (c) => ` ${c.toLowerCase()}`)}${action.amount !== undefined ? ` ${action.amount}` : ''}${action.condition ? ` ${action.condition}` : ''}`;
  const b = before.character.play;
  const entry = (inverse: PlayAction[]): UndoEntry => ({ label, inverse, after });
  switch (action.action) {
    case 'damage':
    case 'heal':
    case 'setHitPoints': {
      const was = hp(before);
      const now = hp(after);
      if (!was || !now) return undefined;
      const inverse: PlayAction[] = [{ action: 'setHitPoints', amount: was.current }];
      if (was.temporary !== now.temporary) inverse.push({ action: 'setTemporaryHitPoints', amount: was.temporary });
      const concBefore = b?.concentration;
      const concAfter = after.character.play?.concentration;
      if (concAfter?.pendingSaveDc !== undefined && concBefore?.pendingSaveDc === undefined) inverse.push({ action: 'clearConcentrationCheck' });
      if (concBefore && !concAfter) inverse.push({ action: 'startConcentration', contentId: concBefore.spell.contentId });
      return entry(inverse);
    }
    case 'setTemporaryHitPoints':
      return entry([{ action: 'setTemporaryHitPoints', amount: hp(before)?.temporary ?? 0 }]);
    case 'spend':
      return entry([{ ...action, action: 'regain' }]);
    case 'regain':
      return entry([{ ...action, action: 'spend' }]);
    case 'addCondition':
      return entry([{ action: 'removeCondition', condition: action.condition }]);
    case 'removeCondition':
      return entry([{ action: 'addCondition', condition: action.condition }]);
    case 'setExhaustion':
      return entry([{ action: 'setExhaustion', amount: b?.exhaustion ?? 0 }]);
    case 'setInspiration':
      return entry([{ action: 'setInspiration', amount: b?.inspiration ? 1 : 0 }]);
    case 'spendSlot':
      return entry([{ action: 'regainSlot', amount: action.amount }]);
    case 'regainSlot':
      return entry([{ action: 'spendSlot', amount: action.amount }]);
    case 'spendPactSlot':
      return entry([{ action: 'regainPactSlot' }]);
    case 'regainPactSlot':
      return entry([{ action: 'spendPactSlot' }]);
    case 'toggleOn':
      return entry([{ action: 'toggleOff', contentId: action.contentId, toggleId: action.toggleId }]);
    case 'toggleOff':
      return entry([{ action: 'toggleOn', contentId: action.contentId, toggleId: action.toggleId }]);
    case 'startConcentration':
      return entry([{ action: 'endConcentration' }]);
    default:
      return undefined;
  }
}
