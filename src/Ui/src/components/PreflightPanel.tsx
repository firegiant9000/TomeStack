import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { PreflightKind, PreflightReport, PreflightRow } from '../api/types';
import { reasonWords } from './preflightText';

const rowLabel = (row: PreflightRow) => (row.sourceName && row.sourceName !== row.name ? `${row.name} (${row.sourceName})` : row.name);

function Rows({ rows, withReason }: { rows: PreflightRow[]; withReason: boolean }) {
  return (
    <ul>
      {rows.map((row, i) => (
        <li key={`${row.contentId}:${row.revisionId}:${i}`}>
          {rowLabel(row)}
          {withReason ? `: ${reasonWords(row.reason)}` : ''}
        </li>
      ))}
    </ul>
  );
}

interface Props {
  kind: PreflightKind;
  selection: string[];
  /** Names the list for assistive technology and tests: "Preflight: <label>". */
  label: string;
  /** Change to ask again for the same selection (for example when the character was saved). */
  refreshKey?: string;
  /** The report when it arrives, `undefined` while a new one is being asked for. The caller disables Save with {@link canSaveAfter}. */
  onReport: (report: PreflightReport | undefined) => void;
  onError: (error: unknown) => void;
}

/**
 * Import Lab L5: before a share export is saved, what it would include, leave out (with the reason in words) and what blocks
 * it. The service asks the same rule again when it exports, so this list is advice that cannot go stale into a leak.
 */
export function PreflightPanel({ kind, selection, label, refreshKey, onReport, onError }: Props) {
  // The answer is kept with the question it answers, so a report for an earlier selection is never shown for a later one.
  const asked = `${kind}|${refreshKey ?? ''}|${selection.join(',')}`;
  const [answer, setAnswer] = useState<{ asked: string; report: PreflightReport }>();
  const report = answer?.asked === asked ? answer.report : undefined;
  const handlers = useRef({ onReport, onError });
  useEffect(() => {
    handlers.current = { onReport, onError };
  });

  useEffect(() => {
    let current = true;
    handlers.current.onReport(undefined); // the caller's Save stays off until this question is answered
    const ids = asked.slice(asked.lastIndexOf('|') + 1);
    client
      .packagePreflight({ kind, selection: ids === '' ? [] : ids.split(',') })
      .then((loaded) => {
        if (!current) return;
        setAnswer({ asked, report: loaded });
        handlers.current.onReport(loaded);
      })
      .catch((error: unknown) => {
        if (current) handlers.current.onError(error);
      });
    return () => {
      current = false;
    };
  }, [asked, kind]);

  if (!report) return <p className="hint">Checking what may be shared…</p>;
  return (
    <div role="region" aria-label={`Preflight: ${label}`}>
      {report.blocking.length > 0 && (
        <div role="alert">
          <p>These stop the export. Fix them, or leave them out of the selection:</p>
          <Rows rows={report.blocking} withReason />
        </div>
      )}
      <p>
        Included: {report.included.length} item{report.included.length === 1 ? '' : 's'}.
        {report.omitted.length === 0 ? ' Nothing is left out.' : ` Left out: ${report.omitted.length}.`}
      </p>
      {report.included.length > 0 && (
        <details>
          <summary>Included ({report.included.length})</summary>
          <Rows rows={report.included} withReason={false} />
        </details>
      )}
      {report.omitted.length > 0 && (
        <>
          <p>Left out of the file:</p>
          <Rows rows={report.omitted} withReason />
        </>
      )}
      {report.notices.length > 0 && (
        <details>
          <summary>License notices that travel with it ({report.notices.length})</summary>
          <ul>
            {report.notices.map((n) => (
              <li key={n}>{n}</li>
            ))}
          </ul>
        </details>
      )}
    </div>
  );
}
