import type { RollRecord } from '../api/types';
import { explainRoll } from '../explain';
import { rollKey } from '../rollText';

/**
 * D72: why the last roll came out the way it did, from the record alone (never the sheet as it is now). Collapsed by default;
 * keyed per record object so a new roll always mounts closed. It sits beside the Last roll region, never inside it, so
 * opening it is not announced as a new result.
 */
export function RollWhy({ record }: { record?: RollRecord }) {
  if (!record) return null;
  const sentences = explainRoll(record);
  if (sentences.length === 0) return null;
  return (
    <details className="roll-why" key={rollKey(record)}>
      <summary>Why?</summary>
      <ul aria-label="Why this roll came out this way">
        {sentences.map((s, i) => (
          <li key={i}>{s}</li>
        ))}
      </ul>
    </details>
  );
}
