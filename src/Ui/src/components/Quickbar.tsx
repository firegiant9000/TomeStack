import { useEffect, useId, useRef } from 'react';
import { quickbarSlot, isTypingTarget } from '../keys';
import type { QuickAction } from '../quickbar';

/** The short text on the button; the full `name` stays the accessible name, and this is always a part of it (WCAG 2.5.3). */
export function quickLabel(name: string): string {
  return (
    /^Roll (.+) and spend .+$/.exec(name)?.[1] ??
    /^Roll (.+) attack$/.exec(name)?.[1] ??
    /^Use (.+?)(?: \(.*\))?$/.exec(name)?.[1] ??
    /^Turn (.+)$/.exec(name)?.[1] ??
    /^Roll (.+)$/.exec(name)?.[1] ??
    name
  );
}

/**
 * D73: the pinned actions as a row of buttons in the summary bar, reachable from every tab. Alt+1 to Alt+8 press them.
 * `aria-disabled`, not `disabled`: an action that cannot be used stays in the Tab order and says why. `busy` holds every
 * button while a press is under way (the sheet's guard is its own ref; this only shows it).
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
    <section className="quickbar" aria-label="Quickbar">
      <ol>
        {actions.map((action, i) => {
          const held = busy || !action.availability.usable;
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
                aria-disabled={held || undefined}
                aria-describedby={reason ? hintId : undefined}
                onClick={() => {
                  if (!held) run(action);
                }}
              >
                <span aria-hidden="true" className="quick-slot">
                  {i + 1}
                </span>{' '}
                {quickLabel(action.name)}
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
