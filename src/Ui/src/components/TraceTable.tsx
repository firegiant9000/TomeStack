import type { TraceEntry, TraceOrigin } from '../api/types';

export function describeOrigin(origin: TraceOrigin): string {
  switch (origin.kind) {
    case 'characterChoice':
      return 'Your choice';
    case 'rulesPolicy':
      return `Rules: ${origin.rulesFamily}`;
    case 'override':
      return 'Your override';
    case 'content': {
      const page = origin.page ? (origin.page.end && origin.page.end !== origin.page.start ? `, pp. ${origin.page.start}-${origin.page.end}` : `, p. ${origin.page.start}`) : '';
      return `${origin.sourceTitle ?? 'Unknown source'}${page}`;
    }
  }
}

const signed = (n: number) => (n >= 0 ? `+${n}` : `${n}`);

/** How one number is calculated: a sheet field's trace, or a secondary caster's attack bonus or save DC (M2.1). */
export function TraceTable({ label, trace, labels }: { label: string; trace: TraceEntry[]; labels: Map<string, string> }) {
  return (
    <table className="trace">
      <caption>How {label.toLowerCase()} is calculated</caption>
      <thead>
        <tr>
          <th scope="col">#</th>
          <th scope="col">Field</th>
          <th scope="col">Step</th>
          <th scope="col">Amount</th>
          <th scope="col">Result</th>
          <th scope="col">Source</th>
        </tr>
      </thead>
      <tbody>
        {trace.map((entry) => (
          <tr key={entry.order} className={`op-${entry.operation}`}>
            <td>{entry.order}</td>
            <td>{entry.field ? (labels.get(entry.field) ?? entry.field) : ''}</td>
            <td>{entry.description}</td>
            <td>{entry.amount === undefined ? '' : entry.operation === 'add' ? signed(entry.amount) : entry.amount}</td>
            <td>{entry.result}</td>
            <td>{describeOrigin(entry.origin)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
