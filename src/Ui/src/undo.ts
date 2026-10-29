import type { CharacterView, PlayAction } from './api/types';

export interface UndoEntry {
  /** What is undone, for the button's name ("damage 12"). */
  label: string;
  /** The confirmed commands that put the state back, in order. */
  inverse: PlayAction[];
  /** The view this change produced. The entry is offered only while the sheet still shows it (a rest, save or reload makes it stale). */
  after: CharacterView;
}

/**
 * D44: a compound play change decides several writes from one view (a form switch also turns the other form and its overlays off).
 * It must name the stored `updatedAt` (`expectedUpdatedAt`) and is undone by the service, never by a client inverse.
 * D43 S3 adds starting and ending an encounter; slice 7 adds damage with responses; slice 8 adds `useAction`. The server's switch
 * (`CompoundUndo` in Play.cs) and this one are checked against `tests/RulesFixtures/compound-play-actions.json`.
 */
export const isCompound = (action: PlayAction): boolean =>
  (action.action === 'toggleOn' && !!action.replace) ||
  action.action === 'startEncounter' ||
  action.action === 'endEncounter' ||
  action.action === 'useAction' ||
  (action.action === 'damage' && (action.responses?.length ?? 0) > 0);

const hp = (view: CharacterView) => view.sheet.hitPoints;
/**
 * D43: switching a primary on or off also drops the overlays its group no longer allows, and the client cannot restore them. True
 * when a toggle other than the one acted on was on before and is off after, so the change has no client undo.
 */
const droppedOthers = (action: PlayAction, before: CharacterView, after: CharacterView) =>
  (before.sheet.toggles ?? []).some(
    (o) =>
      o.on &&
      !o.auto &&
      !(o.content.contentId === action.contentId && o.toggleId === action.toggleId) &&
      after.sheet.toggles?.some((n) => n.content.contentId === o.content.contentId && n.toggleId === o.toggleId && !n.on),
  );
const findToggle =(view: CharacterView, toggle: PlayAction) =>
  view.sheet.toggles?.find((t) => t.content.contentId === toggle.contentId && t.toggleId === toggle.toggleId);

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
  if (isCompound(action)) return undefined; // D44: the service undoes it (`undoLabel`), exactly
  // Wave 3 item 21: typed damage names its type and what was taken after defences ("damage 13 slashing (6 taken)"); untyped keeps "damage 12".
  const typed = action.action === 'damage' && after.damage?.type ? ` ${after.damage.type} (${after.damage.adjusted} taken)` : '';
  const label = `${action.action === 'setTemporaryHitPoints' ? 'temporary hit points' : action.action.replace(/[A-Z]/g, (c) => ` ${c.toLowerCase()}`)}${action.amount !== undefined ? ` ${action.amount}` : ''}${typed}${action.condition ? ` ${action.condition}` : ''}`;
  const b = before.character.play;
  const entry = (inverse: PlayAction[]): UndoEntry => ({ label, inverse, after });
  const hadCondition = (condition?: string) => !!condition && (b?.conditions ?? []).some((c) => c.toLowerCase() === condition.toLowerCase());
  const backing = (toggle: PlayAction) => findToggle(before, toggle)?.resourceId;
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
      // A replace switch is compound (D44, handled above). A plain switch that still dropped another toggle has no client undo either.
      if (droppedOthers(action, before, after)) return undefined;
      const t = findToggle(before, action);
      const resourceId = t?.resourceId;
      // A resource-backed toggle spent its cost when it went on (1 when it names a pool but no cost, D43), from the pool it names
      // (its own content when none): switching it off gives back exactly that, to that pool. A cost of 0 spent nothing.
      const amount = t?.cost ?? 1;
      const regain: PlayAction[] = resourceId && amount > 0 ? [{ action: 'regain', amount, contentId: t?.costResourceContent ?? action.contentId, resourceId }] : [];
      return entry([{ action: 'toggleOff', contentId: action.contentId, toggleId: action.toggleId }, ...regain]);
    }
    case 'toggleOff': {
      // Switching a resource-backed toggle back on would spend a use the player did not spend, so it is not undoable.
      if (backing(action)) return undefined;
      // A timed toggle that is counting its rounds would restart at the full duration if switched back on, and toggleOff is not compound (no service undo), so it cannot be undone.
      if (before.character.play?.toggles?.some((t) => t.contentId === action.contentId && t.toggleId === action.toggleId && t.roundsLeft != null)) return undefined;
      return droppedOthers(action, before, after) ? undefined : entry([{ action: 'toggleOn', contentId: action.contentId, toggleId: action.toggleId }]);
    }
    case 'companionDamage':
    case 'companionHeal':
    case 'companionSetHitPoints':
    case 'companionSetTemporaryHitPoints': {
      // Wave 3 item 23: a companion's hit points are put back exactly, current and temporary both. No recorded entry means at the
      // maximum with none temporary. A change that left both as they were, or a companion the view no longer has, has no entry.
      const state = (v: CharacterView) => {
        const companion = v.character.companions?.find((c) => c.id === action.companionId);
        if (!companion) return undefined;
        const recorded = v.character.play?.companions?.find((c) => c.id === action.companionId);
        return { current: Math.min(recorded?.current ?? companion.hitPointMaximum, companion.hitPointMaximum), temporary: recorded?.temporary ?? 0 };
      };
      const was = state(before);
      const now = state(after);
      if (!was || !now || (was.current === now.current && was.temporary === now.temporary)) return undefined;
      return entry([{ action: 'companionSetHitPoints', companionId: action.companionId, amount: was.current, temporary: was.temporary }]);
    }
    case 'spendCharge':
    case 'regainCharge': {
      // Wave 3 item 25: the exact inverse is the other direction with the same amount on the same entry. An entry that had no id gets one
      // by the first change, so the inverse names the id the view after it shows. A rest's charge recovery is not undoable (a rest is
      // not a play command; the view it produces makes any earlier entry stale).
      const entryId = action.entryId ?? after.sheet.itemUses?.find((u) => u.entryIndex === action.entryIndex && u.item.contentId === action.contentId)?.entryId;
      if (!entryId) return undefined;
      return entry([{ action: action.action === 'spendCharge' ? 'regainCharge' : 'spendCharge', amount: action.amount, entryId }]);
    }
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
