import type { DebugFinding, DebugReport } from '../api/types';

const severityLabel = { error: 'Error', warning: 'Warning', note: 'Note' } as const;

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

/**
 * M5 slice 2 (B02): what the homebrew debugger found, each finding with a button that opens its entry in the studio
 * editor and moves focus to the rule it concerns. Reading the findings changes nothing.
 */
export function DebugFindings(props: {
  report: DebugReport;
  label: string;
  /** Name the entry of each finding (a whole source); in one entry's editor, the entry is obvious. */
  showNames: boolean;
  onShow: (finding: DebugFinding) => void;
}) {
  const { report } = props;
  const notes = report.findings.length - report.errors - report.warnings;
  return (
    <div role="region" aria-label={props.label}>
      {report.findings.length === 0 ? (
        <p>The debugger found no problems.</p>
      ) : (
        <>
          <p>
            {plural(report.errors, 'error')}, {plural(report.warnings, 'warning')} and {plural(notes, 'note')}. Errors block publishing;
            warnings and notes explain what will not work as it may seem.
          </p>
          <ul>
            {report.findings.map((f, i) => {
              const where = f.effectId ? `rule ${f.effectId}` : 'entry';
              return (
                <li key={`${f.content.revisionId}-${f.code}-${f.effectId ?? ''}-${i}`} className={f.severity === 'note' ? undefined : f.severity === 'error' ? 'error' : 'warn'}>
                  {severityLabel[f.severity]}
                  {props.showNames ? ` in ${f.contentName}` : ''}: {f.message}{' '}
                  <button type="button" onClick={() => props.onShow(f)}>
                    Show {where}
                    {props.showNames ? ` of ${f.contentName}` : ''}
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      )}
      {report.truncated && <p className="warn">The content is too large to follow completely, so some findings may be missing.</p>}
    </div>
  );
}
