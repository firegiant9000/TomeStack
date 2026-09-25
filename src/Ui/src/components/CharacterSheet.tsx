import { useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { CharacterView, DerivedValue, TraceOrigin } from '../api/types';
import { downloadBase64 } from '../files';

function describeOrigin(origin: TraceOrigin): string {
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

function TraceTable({ value }: { value: DerivedValue }) {
  return (
    <table className="trace">
      <caption>How {value.label.toLowerCase()} is calculated</caption>
      <thead>
        <tr>
          <th scope="col">#</th>
          <th scope="col">Step</th>
          <th scope="col">Amount</th>
          <th scope="col">Result</th>
          <th scope="col">Source</th>
        </tr>
      </thead>
      <tbody>
        {value.trace.map((entry) => (
          <tr key={entry.order} className={`op-${entry.operation}`}>
            <td>{entry.order}</td>
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

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
}

export function CharacterSheet({ view, onChanged, onError }: Props) {
  const { character, sheet } = view;
  const [overrideValue, setOverrideValue] = useState('');
  const [overrideReason, setOverrideReason] = useState('');

  async function save(overrides: typeof character.overrides) {
    try {
      onChanged(await client.saveCharacter({ ...character, overrides }));
    } catch (error) {
      onError(error);
    }
  }

  async function applyOverride(event: SubmitEvent<HTMLFormElement>, field: string) {
    event.preventDefault();
    const value = Number(overrideValue);
    if (!Number.isInteger(value)) return;
    await save([...character.overrides.filter((o) => o.field !== field), { field, value, reason: overrideReason || undefined }]);
    setOverrideValue('');
    setOverrideReason('');
  }

  async function exportCharacter() {
    try {
      const exported = await client.exportCharacters([character.id]);
      downloadBase64(exported.fileName, exported.base64);
    } catch (error) {
      onError(error);
    }
  }

  return (
    <article className="panel" aria-labelledby="sheet-heading">
      <header className="sheet-header">
        <h2 id="sheet-heading">{character.name}</h2>
        <span className="tag">{character.rulesFamily}</span>
        <button type="button" onClick={exportCharacter}>
          Export package
        </button>
      </header>

      {sheet.fields.map((field) => (
        <section key={field.field} aria-labelledby={`field-${field.field}`}>
          <h3 id={`field-${field.field}`}>
            {field.label}: <output className="derived">{signed(field.value)}</output>
            {field.override && (
              <span className="override-label">
                {' '}
                overridden (calculated {signed(field.computedValue)})
              </span>
            )}
          </h3>
          <TraceTable value={field} />
          {field.warnings.length > 0 && (
            <ul className="warnings" aria-label={`${field.label} warnings`}>
              {field.warnings.map((w) => (
                <li key={`${w.code}-${w.effectId ?? ''}`}>{w.message}</li>
              ))}
            </ul>
          )}
          <form className="override" onSubmit={(e) => applyOverride(e, field.field)}>
            <label className="field">
              Override value
              <input type="number" value={overrideValue} onChange={(e) => setOverrideValue(e.target.value)} required />
            </label>
            <label className="field">
              Reason (optional)
              <input value={overrideReason} onChange={(e) => setOverrideReason(e.target.value)} />
            </label>
            <button type="submit">Apply override</button>
            {field.override && (
              <button type="button" onClick={() => save(character.overrides.filter((o) => o.field !== field.field))}>
                Remove override
              </button>
            )}
          </form>
        </section>
      ))}

      {sheet.diagnostics.length > 0 && (
        <section aria-labelledby="diagnostics-heading">
          <h3 id="diagnostics-heading">Content not applied</h3>
          <ul className="warnings">
            {sheet.diagnostics.map((d) => (
              <li key={`${d.code}-${d.content?.revisionId ?? ''}-${d.effectId ?? ''}`}>{d.message}</li>
            ))}
          </ul>
        </section>
      )}
    </article>
  );
}
