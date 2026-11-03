import { useId } from 'react';
import type { ExportChange, OpensInCause } from '../../api/types';

interface Props {
  target: string | null;
  /** Undefined when the service does not report it: nothing is shown. Null means a newer TomeStack than this one. */
  opensIn?: string | null;
  causes?: OpensInCause[];
  changes?: ExportChange[];
  previousMinor: string | null;
}

const groups: { action: ExportChange['action']; heading: string }[] = [
  { action: 'blocks', heading: 'Blocks this copy:' },
  { action: 'dropped', heading: 'Left out of this copy:' },
  { action: 'leftOut', heading: 'Not in this copy:' },
  { action: 'copied', heading: 'Copied with a new id:' },
];

/** True when dotted release `a` is numerically newer than `b` ("0.10.0" is newer than "0.9.0"). */
export function isNewerThan(a: string, b: string): boolean {
  const left = a.split('.').map(Number);
  const right = b.split('.').map(Number);
  for (let i = 0; i < Math.max(left.length, right.length); i++) {
    const diff = (left[i] ?? 0) - (right[i] ?? 0);
    if (diff !== 0) return diff > 0;
  }
  return false;
}

/** What the copy for an older TomeStack changes, or (for the default file) which TomeStack opens it and why. */
export function ChangeRegion({ target, opensIn, causes, changes, previousMinor }: Props) {
  const headingId = useId();
  if (target !== null) {
    const all = changes ?? [];
    return (
      <div role="region" aria-label={`Changed for TomeStack ${target}`}>
        {all.length === 0 && <p>Nothing is left out of this copy.</p>}
        {groups.map(({ action, heading }) => {
          const items = all.filter((c) => c.action === action);
          if (items.length === 0) return null;
          return (
            <div key={action}>
              <p id={`${headingId}-${action}`}>{heading}</p>
              <ul aria-labelledby={`${headingId}-${action}`}>
                {items.map((c, i) => (
                  <li key={`${c.entry}:${c.id}:${i}`}>{c.message}</li>
                ))}
              </ul>
            </div>
          );
        })}
      </div>
    );
  }
  if (opensIn === undefined) return null;
  const shownCauses = causes ?? [];
  const showCauses = shownCauses.length > 0 && (opensIn === null || previousMinor === null || isNewerThan(opensIn, previousMinor));
  return (
    <div role="region" aria-label="Compatibility">
      <p>{opensIn === null ? 'This file needs a newer TomeStack than this one.' : `This file opens in TomeStack ${opensIn} or newer.`}</p>
      {showCauses && (
        <ul>
          {shownCauses.map((c, i) => (
            <li key={`${c.entry}:${c.id}:${i}`}>{c.message}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
