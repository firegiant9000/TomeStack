import { useEffect, useId, useRef } from 'react';
import { quickbarSlot, isTypingTarget } from '../keys';
import type { QuickAction } from '../quickbar';

/**
 * D73: the pinned actions as a row of buttons in the summary bar, reachable from every tab. Alt+1 to Alt+8 press them.
 * `aria-disabled`, not `disabled`: an action that cannot be used stays in the Tab order and says why. `busy` is shown as
 * `aria-busy` on the bar, not on the buttons, so they don't flip style on every press (the sheet's guard is its own ref).
 */
export function Quickbar({ actions, busy, run }: { actions: QuickAction[]; busy: boolean; run: (action: QuickAction) => void }) {
  const idBase = useId();
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  // The listener is registered once and reads what is current through this ref, so it never runs a stale list.
  const latest = useRef({ actions, busy, run });
  useEffect(() => {
    latest.current = { actions, busy, run };
  });
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      const slot = quickbarSlot(event);
      if (slot === undefined || isTypingTarget(event.target)) return;
      if ((event.target as Element | null)?.closest?.('[role="alertdialog"]')) return; // a confirmation owns the keys while it is open
      const current = latest.current;
      const action = current.actions[slot];
      if (!action) return; // not a hit: the key keeps whatever else it does
      event.preventDefault();
      if (event.repeat || current.busy) return; // holding the keys must not spend twice
      if (!action.availability.usable) {
        buttons.current[slot]?.focus(); // the reason is read with the button
        return;
      }
      current.run(action);
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  if (actions.length === 0) return null;
  return (
    <section className="quickbar" aria-label="Quickbar" aria-busy={busy || undefined}>
      <ol>
        {actions.map((action, i) => {
          const unusable = !action.availability.usable;
          const reason = action.availability.reasons.join(' ');
          const hintId = `${idBase}-${i}`;
          return (
            <li key={action.key}>
              <button
                type="button"
                ref={(el) => {
                  buttons.current[i] = el;
                }}
                aria-label={action.name}
                aria-keyshortcuts={`Alt+${i + 1}`}
                aria-disabled={unusable || undefined}
                aria-describedby={reason ? hintId : undefined}
                onClick={() => {
                  if (!busy && !unusable) run(action);
                }}
              >
                <span aria-hidden="true" className="quick-slot">
                  {i + 1}
                </span>{' '}
                {action.label}
              </button>
              {reason && (
                <span id={hintId} className="visually-hidden">
                  {reason}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </section>
  );
}
