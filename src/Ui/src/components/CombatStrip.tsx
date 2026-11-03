import { useEffect, useRef, useState } from 'react';
import type { CharacterView, PlayAction, ResourceValue, ToggleValue, TurnMoment } from '../api/types';
import { FormGroups } from './FormGroups';

/** D43 slice 3: does the sheet have anything for the strip (a form group, an allowance, an upkeep, or an encounter in progress)? */
function hasCombat(view: CharacterView): boolean {
  const { sheet, character } = view;
  return (
    (sheet.toggles ?? []).some((t) => t.group || t.duration != null) ||
    (sheet.resources ?? []).some((r) => (r.allowance || r.countsUp) && !r.companionId) ||
    (sheet.upkeeps ?? []).length > 0 ||
    character.play?.encounter !== undefined
  );
}

/** "3 rounds left", or the by-hand text when the toggle has a duration but no count (set outside an encounter, or after the encounter ended). */
const durationText = (t: ToggleValue) =>
  t.roundsLeft != null ? `${t.roundsLeft} ${t.roundsLeft === 1 ? 'round' : 'rounds'} left` : 'duration: track by hand';

function poolText(r: ResourceValue): string {
  if (r.maximum === undefined || r.current === undefined) return `${r.label}: tracked by hand`;
  if (r.countsUp) {
    const full = r.spent >= r.maximum && r.maximum > 0 && r.fullText ? ` · full: ${r.fullText}` : '';
    return `${r.label}: built up ${r.spent} of ${r.maximum}${full}`;
  }
  return `${r.label}: ${r.current} of ${r.maximum}`;
}

/**
 * D43 slice 3: the strip above the Play columns when the sheet has form groups, an allowance pool, upkeeps or an encounter.
 * It hosts the form picker, the round, Start / End encounter and "Start my turn" / "End my turn" (which open the turn panel),
 * the durations of the toggles that are on, and the counting-up and allowance pools. Everything is text first (WCAG 1.4.1).
 * Encounter writes are compound: the sheet's `act` stamps them with the view on screen. `busy` waits for a change in flight.
 */
export function CombatStrip({
  view,
  act,
  busy,
  onTurn,
}: {
  view: CharacterView;
  /** May return a promise (the sheet's does). */
  act: (action: PlayAction) => void | Promise<unknown>;
  busy: boolean;
  onTurn: (at: TurnMoment) => void;
}) {
  // D43 S8: ending an encounter asks which built-up counting-up pools to clear, when there are any.
  const encounter = view.character.play?.encounter;
  const resources = view.sheet.resources ?? [];
  const builtUp = resources.filter((r) => r.countsUp && r.spent > 0 && !r.companionId); // a companion's pools are on its card
  const [confirming, setConfirming] = useState(false);
  const [ticked, setTicked] = useState<string[]>([]);
  // The confirmation is open only while there is an encounter with something built up; an encounter that ends (from anywhere), or
  // nothing left built up, closes it for good, so it never comes back with stale ticks.
  const open = confirming && encounter !== undefined && builtUp.length > 0;
  if (confirming && (encounter === undefined || builtUp.length === 0)) {
    setConfirming(false);
    setTicked([]);
  }
  const firstBox = useRef<HTMLInputElement>(null);
  const endButton = useRef<HTMLButtonElement>(null);
  const focusEnd = useRef(false);
  useEffect(() => {
    if (open) firstBox.current?.focus();
    else if (focusEnd.current) {
      focusEnd.current = false;
      endButton.current?.focus();
    }
  }, [open]);
  if (!hasCombat(view)) return null;
  const toggles = view.sheet.toggles ?? [];
  const clearKey = (r: ResourceValue) => `${r.content.contentId}/${r.resourceId}`;
  const closeConfirm = (returnFocus: boolean) => {
    focusEnd.current = returnFocus;
    setConfirming(false);
    setTicked([]);
  };
  const timed = toggles.filter((t) => t.on && t.duration != null);
  const pools = resources.filter((r) => (r.countsUp || r.allowance) && !r.companionId);
  return (
    <section aria-labelledby="combat-heading" className="play-panel combat-strip">
      <h3 id="combat-heading" tabIndex={-1}>
        Combat
      </h3>
      {toggles.some((t) => t.group) && (
        <section aria-labelledby="forms-heading">
          <h4 id="forms-heading">Forms (a switch is a confirmed change)</h4>
          <FormGroups toggles={toggles} act={act} busy={false} resources={view.sheet.resources} />
        </section>
      )}
      <p className="option-name">{encounter ? `Round ${Math.max(1, encounter.round)}` : 'No encounter running'}</p>
      <div className="actions">
        {encounter ? (
          <>
            <button type="button" id="turn-open-turnStart" disabled={busy} onClick={() => onTurn('turnStart')}>
              Start my turn
            </button>
            <button type="button" id="turn-open-turnEnd" disabled={busy} onClick={() => onTurn('turnEnd')}>
              End my turn
            </button>
            {open ? (
              <fieldset className="field-group">
                <legend>End encounter</legend>
                {builtUp.map((r, i) => (
                  <label key={clearKey(r)} className="choice">
                    <input
                      ref={i === 0 ? firstBox : undefined}
                      type="checkbox"
                      checked={ticked.includes(clearKey(r))}
                      onChange={(e) => setTicked((t) => (e.target.checked ? [...t, clearKey(r)] : t.filter((k) => k !== clearKey(r))))}
                    />
                    {`Clear ${r.label} (${r.spent} built up)`}
                  </label>
                ))}
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => {
                    const clear = builtUp.map(clearKey).filter((k) => ticked.includes(k));
                    // Closed only once the change is applied; a refusal keeps the choices and the focus in the group.
                    void Promise.resolve(act(clear.length > 0 ? { action: 'endEncounter', clear } : { action: 'endEncounter' }))
                      .then((applied) => applied !== false)
                      .catch(() => false)
                      .then((applied) => {
                        if (applied) closeConfirm(false);
                        else firstBox.current?.focus();
                      });
                  }}
                >
                  End encounter
                </button>
                <button type="button" onClick={() => closeConfirm(true)}>
                  Cancel
                </button>
              </fieldset>
            ) : (
              <button
                ref={endButton}
                type="button"
                disabled={busy}
                onClick={() => (builtUp.length > 0 ? setConfirming(true) : void act({ action: 'endEncounter' }))}
              >
                End encounter
              </button>
            )}
          </>
        ) : (
          <button type="button" disabled={busy} onClick={() => void act({ action: 'startEncounter' })}>
            Start encounter
          </button>
        )}
      </div>
      {timed.length > 0 && (
        <ul className="features" aria-label="Timed effects">
          {timed.map((t) => (
            <li key={`${t.content.revisionId}-${t.toggleId}`}>{`${t.label}: ${durationText(t)}`}</li>
          ))}
        </ul>
      )}
      {pools.length > 0 && (
        <ul className="features" aria-label="Combat pools">
          {pools.map((r) => (
            <li key={`${r.content.revisionId}-${r.resourceId}`}>{poolText(r)}</li>
          ))}
        </ul>
      )}
    </section>
  );
}
