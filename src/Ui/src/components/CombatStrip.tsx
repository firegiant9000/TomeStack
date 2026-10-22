import type { CharacterView, PlayAction, ResourceValue, ToggleValue, TurnMoment } from '../api/types';
import { FormGroups } from './FormGroups';

/** D43 slice 3: does the sheet have anything for the strip (a form group, an allowance, an upkeep, or an encounter in progress)? */
function hasCombat(view: CharacterView): boolean {
  const { sheet, character } = view;
  return (
    (sheet.toggles ?? []).some((t) => t.group || t.duration != null) ||
    (sheet.resources ?? []).some((r) => r.allowance) ||
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
  act: (action: PlayAction) => void | Promise<void>;
  busy: boolean;
  onTurn: (at: TurnMoment) => void;
}) {
  if (!hasCombat(view)) return null;
  const toggles = view.sheet.toggles ?? [];
  const resources = view.sheet.resources ?? [];
  const encounter = view.character.play?.encounter;
  const timed = toggles.filter((t) => t.on && t.duration != null);
  const pools = resources.filter((r) => r.countsUp || r.allowance);
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
            <button type="button" disabled={busy} onClick={() => void act({ action: 'endEncounter' })}>
              End encounter
            </button>
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
