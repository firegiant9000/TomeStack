import type { TraceEntry } from '../api/types';
import { explainSteps, type StepContext } from '../explain';

/**
 * D72 X04: a closed "Why?" under a number that has a trace, one sentence per step. Not a live region: opening it announces nothing.
 * `summary` replaces the "Why?" wording where the number must be named (the save line's "Why DC 13?").
 */
export function ExplainList({ label, steps, context, summary = 'Why?' }: { label: string; steps: TraceEntry[]; context?: StepContext; summary?: string }) {
  if (steps.length === 0) return null;
  return (
    <details className="explain">
      <summary>
        {summary}
        {summary === 'Why?' && <span className="visually-hidden"> ({label})</span>}
      </summary>
      <ul aria-label={`Why ${label}`}>
        {explainSteps(steps, context).map((sentence, i) => (
          <li key={i}>{sentence}</li>
        ))}
      </ul>
    </details>
  );
}
