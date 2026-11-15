import type { DerivedValue } from '../../api/types';
import { ExplainList } from '../ExplainList';
import { TraceTable } from '../TraceTable';

/** The "why" body of one calculated field: the plain-language steps, the trace table and the warnings. */
export function FieldWhy({ field, label, labels }: { field: DerivedValue; label: string; labels: Map<string, string> }) {
  return (
    <>
      <ExplainList label={label} steps={field.trace} />
      <TraceTable label={label} trace={field.trace} labels={labels} />
      {field.warnings.length > 0 && (
        <ul className="warnings" aria-label={`${label} warnings`}>
          {field.warnings.map((w) => (
            <li key={`${w.code}-${w.content?.revisionId ?? ''}-${w.effectId ?? ''}`}>{w.message}</li>
          ))}
        </ul>
      )}
    </>
  );
}
