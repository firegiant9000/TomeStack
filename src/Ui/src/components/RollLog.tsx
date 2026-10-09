import type { RollRecord } from '../api/types';
import { diceLine, rollHeadline } from '../rollText';

export interface LoggedRoll {
  /** Client clock when the reply arrived (a RollRecord has no timestamp). */
  at: number;
  record: RollRecord;
}

/** D28 (owner, 2026-10-07): the previous rolls of this sheet session. Ten at most, newest first, never stored. */
export const rollLogLimit = 10;

/** A record stamped with the client clock now; called from event handlers only. */
export const logged = (record: RollRecord): LoggedRoll => ({ at: Date.now(), record });

const time = (at: number) => new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

/**
 * A collapsed disclosure under the Last roll region: a `details` is a group, not a heading or a named region, so the
 * summary's "no headings, only Last roll" contract holds; it is outside the live region, so nothing is announced twice.
 */
export function RollLog({ rolls }: { rolls: LoggedRoll[] }) {
  const shown = rolls.slice(0, rollLogLimit);
  if (shown.length === 0) return null;
  return (
    <details className="roll-log">
      <summary>Previous rolls ({shown.length})</summary>
      <ol>
        {shown.map((r, i) => (
          <li key={`${r.at}-${i}-${r.record.total}-${r.record.formula}`}>
            <span className="roll-time">{time(r.at)}</span> <strong>{rollHeadline(r.record)}</strong> <span className="hint">{diceLine(r.record)}</span>
          </li>
        ))}
      </ol>
    </details>
  );
}
