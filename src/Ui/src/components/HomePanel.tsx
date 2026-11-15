import { useEffect, useRef } from 'react';
import type { CharacterSummary } from '../api/types';
import { useRulesLabel } from '../rulesLabel';

interface Props {
  characters: CharacterSummary[];
  onOpen: (id: string) => void;
  onNew: () => void;
  /** False until app.info has loaded (the builder needs the rules families). */
  canCreate: boolean;
  /** Focus the heading on mount (WCAG 2.4.3). The app start screen passes false so the first Tab reaches the header. */
  focusOnMount?: boolean;
  /** First run: "Try a sample character" (one click to a playable sheet). Without it the button is hidden. */
  onTrySample?: () => void;
  /** First run: "Bring in a character" opens the chooser on its import choices. */
  onBringIn?: () => void;
  /** False until the first character list has loaded: an empty list then is not yet "no characters", so no welcome card. */
  loaded?: boolean;
}

const changed = (iso: string) => new Date(iso).toLocaleDateString();

/**
 * Investigation 2026-10-06 item 3 (D25): the home screen. Cards for the active characters (name, family, level, last change)
 * with an "Open {name}" button each: a name distinct from the sidebar's "{name} {family}" so the e2e lookups stay unique.
 * Archived characters stay in the sidebar's "Archived" list only (SPEC C-08).
 */
export function HomePanel({ characters, onOpen, onNew, canCreate, focusOnMount = true, onTrySample, onBringIn, loaded = true }: Props) {
  const rulesLabel = useRulesLabel();
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (focusOnMount) heading.current?.focus();
  }, [focusOnMount]);
  const active = characters.filter((c) => !c.archivedAt).sort((a, b) => (a.updatedAt < b.updatedAt ? 1 : -1));
  return (
    <section className="panel home" aria-labelledby="home-heading">
      <h1 id="home-heading" tabIndex={-1} ref={heading}>
        Your characters
      </h1>
      {characters.length === 0 && !loaded ? null : characters.length === 0 ? (
        <section className="welcome-card" aria-labelledby="welcome-heading">
          <h2 id="welcome-heading">Welcome to TomeStack</h2>
          <p>TomeStack keeps your characters on this computer and shows how every number is worked out.</p>
          <div className="actions">
            {onTrySample && (
              <button type="button" className="primary" onClick={onTrySample}>
                Try a sample character
              </button>
            )}
            <button type="button" disabled={!canCreate} onClick={onNew}>
              Make my own
            </button>
          </div>
          {onBringIn && (
            <p>
              Already have one?{' '}
              <button type="button" onClick={onBringIn}>
                Bring in a character
              </button>
            </p>
          )}
        </section>
      ) : active.length === 0 ? (
        <>
          <p className="hint">Choose + Add a character in the sidebar, or create one here.</p>
          <button type="button" disabled={!canCreate} onClick={onNew}>
            Create a character
          </button>
        </>
      ) : (
        <ul className="character-cards" aria-label="Characters">
          {active.map((c) => (
            <li key={c.id} className="character-card">
              <strong>{c.name}</strong> <span className="tag">{rulesLabel(c.rulesFamily)}</span>
              <p className="hint">
                Level {c.level} · Changed {changed(c.updatedAt)}
              </p>
              <button type="button" onClick={() => onOpen(c.id)}>
                Open {c.name}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
