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
 * Death saves, ending concentration and clearing its check have no exact inverse and are not undoable, and neither is
 * regaining hit points from 0 while death saves are recorded (it resets them). A resource-backed
 * toggle going on is undone with the use given back; going off is not undoable. Concentrating on another spell is undone
 * by concentrating on the earlier one. A change that changed nothing (a condition already held) has no entry.
 */
export function inverseOf(action: PlayAction, before: CharacterView, after: CharacterView): UndoEntry | undefined {
  const label = `${action.action === 'setTemporaryHitPoints' ? 'temporary hit points' : action.action.replace(/[A-Z]/g, (c) => ` ${c.toLowerCase()}`)}${action.amount !== undefined ? ` ${action.amount}` : ''}${action.condition ? ` ${action.condition}` : ''}`;
  const b = before.character.play;
  const entry = (inverse: PlayAction[]): UndoEntry => ({ label, inverse, after });
  const hadCondition = (condition?: string) => !!condition && (b?.conditions ?? []).some((c) => c.toLowerCase() === condition.toLowerCase());
  const backing = (toggle: PlayAction) =>
    before.sheet.toggles?.find((t) => t.content.contentId === toggle.contentId && t.toggleId === toggle.toggleId)?.resourceId;
  switch (action.action) {
    case 'damage':
    case 'heal':
    case 'setHitPoints': {
      const was = hp(before);
      const now = hp(after);
      if (!was || !now) return undefined;
      // Regaining hit points from 0 resets the death saves, and there is no command that puts them back: no entry at all,
      // rather than an undo that silently clears a dying character's saves.
      const saves = b?.deathSaves;
      if (was.current === 0 && now.current > 0 && ((saves?.successes ?? 0) > 0 || (saves?.failures ?? 0) > 0)) return undefined;
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
      // A condition the character already had changed nothing, so there is nothing to put back.
      return hadCondition(action.condition) ? undefined : entry([{ action: 'removeCondition', condition: action.condition }]);
    case 'removeCondition':
      return hadCondition(action.condition) ? entry([{ action: 'addCondition', condition: action.condition }]) : undefined;
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
    case 'toggleOn': {
      const resourceId = backing(action);
      // A resource-backed toggle spent a use when it went on: switching it off gives that use back.
      return entry([{ action: 'toggleOff', contentId: action.contentId, toggleId: action.toggleId }, ...(resourceId ? [{ action: 'regain' as const, amount: 1, contentId: action.contentId, resourceId }] : [])]);
    }
    case 'toggleOff':
      // Switching a resource-backed toggle back on would spend a use the player did not spend, so it is not undoable.
      return backing(action) ? undefined : entry([{ action: 'toggleOn', contentId: action.contentId, toggleId: action.toggleId }]);
    case 'startConcentration': {
      const held = b?.concentration?.spell.contentId;
      if (held === action.contentId) return undefined; // already concentrating on it: nothing changed
      // Concentrating on another spell is replaced, not added: undoing puts the earlier one back (its pending save DC is not restored).
      return entry([held ? { action: 'startConcentration', contentId: held } : { action: 'endConcentration' }]);
    }
    default:
      return undefined;
  }
}
