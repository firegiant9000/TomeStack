import { useEffect, useRef, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { CharacterView, DerivedValue, ExportPreview, ExportPurpose, FieldOverride, TraceOrigin } from '../api/types';
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
const display = (value: DerivedValue, n: number) => (value.units === 'score' ? `${n}` : signed(n));

/** Display groups for the calculated fields; the rules core decides what exists, this only orders it. */
const groups: { title: string; match: (field: string) => boolean }[] = [
  { title: 'Abilities', match: (f) => f.startsWith('ability.') },
  { title: 'Proficiency', match: (f) => f === 'proficiencyBonus' },
  { title: 'Saving throws', match: (f) => f.startsWith('save.') },
  { title: 'Skills', match: (f) => f.startsWith('skill.') },
  { title: 'Combat', match: (f) => f === 'initiative' || f === 'armorClass' || f === 'hitPoints' },
];

function TraceTable({ value, labels }: { value: DerivedValue; labels: Map<string, string> }) {
  return (
    <table className="trace">
      <caption>How {value.label.toLowerCase()} is calculated</caption>
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
        {value.trace.map((entry) => (
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

interface FieldProps {
  value: DerivedValue;
  labels: Map<string, string>;
  onOverride: (field: string, change: FieldOverride | undefined) => void;
}

/** One field: its own override form state, so fields never share input values. */
function FieldCard({ value, labels, onOverride }: FieldProps) {
  const [overrideValue, setOverrideValue] = useState('');
  const [overrideReason, setOverrideReason] = useState('');
  const headingId = `field-${value.field}`;

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const n = Number(overrideValue);
    if (!Number.isInteger(n)) return;
    onOverride(value.field, { field: value.field, value: n, reason: overrideReason || undefined });
    setOverrideValue('');
    setOverrideReason('');
  }

  return (
    <section aria-labelledby={headingId} className="field-card">
      <details>
        <summary>
          <h4 id={headingId}>
            {value.label}: <span className="derived">{display(value, value.value)}</span>
            {value.override && <span className="override-label"> overridden (calculated {display(value, value.computedValue)})</span>}
            {value.warnings.length > 0 && <span className="warning-count"> · {value.warnings.length} warning{value.warnings.length === 1 ? '' : 's'}</span>}
          </h4>
        </summary>
        <TraceTable value={value} labels={labels} />
        {value.warnings.length > 0 && (
          <ul className="warnings" aria-label={`${value.label} warnings`}>
            {value.warnings.map((w) => (
              <li key={`${w.code}-${w.content?.revisionId ?? ''}-${w.effectId ?? ''}`}>{w.message}</li>
            ))}
          </ul>
        )}
        <form className="override" onSubmit={submit}>
          <label className="field">
            Override value
            <input type="number" value={overrideValue} onChange={(e) => setOverrideValue(e.target.value)} required />
          </label>
          <label className="field">
            Reason (optional)
            <input value={overrideReason} onChange={(e) => setOverrideReason(e.target.value)} />
          </label>
          <button type="submit">Apply override</button>
          {value.override && (
            <button type="button" onClick={() => onOverride(value.field, undefined)}>
              Remove override
            </button>
          )}
        </form>
      </details>
    </section>
  );
}

interface ExportProps {
  characterId: string;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

/** ADR-007: choose backup or share; a share first lists everything it will leave out. */
function ExportPanel({ characterId, onError, onStatus }: ExportProps) {
  const [purpose, setPurpose] = useState<ExportPurpose>('backup');
  const [preview, setPreview] = useState<ExportPreview>();

  async function choose(next: ExportPurpose) {
    setPurpose(next);
    setPreview(undefined);
    if (next !== 'share') return;
    try {
      setPreview(await client.previewExport([characterId], next));
    } catch (error) {
      onError(error);
    }
  }

  async function exportCharacter() {
    try {
      const outcome = await client.saveExportAs([characterId], purpose);
      if (outcome.saved) onStatus(`Saved ${outcome.fileName}.`);
    } catch (error) {
      if (!(error instanceof TomeStackError && error.code === 'unsupported')) {
        onError(error);
        return;
      }
      // Browser development (DevHost) has no native dialog: fall back to a download.
      try {
        const exported = await client.exportCharacters([characterId], purpose);
        downloadBase64(exported.fileName, exported.base64);
      } catch (fallbackError) {
        onError(fallbackError);
      }
    }
  }

  return (
    <section aria-labelledby="export-heading" className="export-panel">
      <h3 id="export-heading">Export</h3>
      <fieldset>
        <legend>What is this package for?</legend>
        <label>
          <input type="radio" name="export-purpose" checked={purpose === 'backup'} onChange={() => choose('backup')} />
          Personal backup: includes everything. Do not share it.
        </label>
        <label>
          <input type="radio" name="export-purpose" checked={purpose === 'share'} onChange={() => choose('share')} />
          Share with someone: leaves out content you may not share
        </label>
      </fieldset>
      {purpose === 'share' && preview && (
        <div role="region" aria-label="Left out of the shared package">
          {preview.omitted.length === 0 ? (
            <p>Nothing is left out: every source this character uses may be shared.</p>
          ) : (
            <>
              <p>These are left out. The receiver sees them as missing until they install the source themselves:</p>
              <ul>
                {preview.omitted.map((source) => (
                  <li key={source.sourceId}>
                    {source.title} ({source.publisher}, {source.license}): {source.revisions.map((r) => r.name).join(', ')}
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}
      <button type="button" onClick={exportCharacter} disabled={purpose === 'share' && !preview}>
        Export package
      </button>
    </section>
  );
}

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

export function CharacterSheet({ view, onChanged, onError, onStatus }: Props) {
  const { character, sheet } = view;
  const labels = new Map(sheet.fields.map((f) => [f.field, f.label]));
  const heading = useRef<HTMLHeadingElement>(null);

  // WCAG 2.4.3: opening a sheet (after create, import or picking from the list) moves focus to its heading instead of
  // leaving it on <body>. The sheet is keyed by character, so this runs once per opened character, not on every save.
  useEffect(() => heading.current?.focus(), []);

  async function changeOverride(field: string, change: FieldOverride | undefined) {
    const overrides = [...character.overrides.filter((o) => o.field !== field), ...(change ? [change] : [])];
    try {
      onChanged(await client.saveCharacter({ ...character, overrides }));
    } catch (error) {
      onError(error);
    }
  }

  return (
    <article className="panel" aria-labelledby="sheet-heading">
      <header className="sheet-header">
        <h2 id="sheet-heading" tabIndex={-1} ref={heading}>
          {character.name}
        </h2>
        <span className="tag">{character.rulesFamily}</span>
        <span className="tag">Level {character.level}</span>
      </header>

      <ExportPanel characterId={character.id} onError={onError} onStatus={onStatus} />

      {groups.map((group) => {
        const fields = sheet.fields.filter((f) => group.match(f.field));
        if (fields.length === 0) return null;
        return (
          <section key={group.title} aria-label={group.title} className="field-group">
            <h3>{group.title}</h3>
            {fields.map((field) => (
              <FieldCard key={field.field} value={field} labels={labels} onOverride={changeOverride} />
            ))}
          </section>
        );
      })}

      {sheet.choices?.some((c) => !c.resolved) && (
        <section aria-labelledby="choices-heading">
          <h3 id="choices-heading">Choices to make</h3>
          <ul className="warnings">
            {sheet.choices
              .filter((c) => !c.resolved)
              .map((c) => (
                <li key={`${c.source.revisionId}-${c.choiceId}`}>
                  {c.sourceName}: choose {c.count} ({c.selected.length} chosen){c.text ? `. ${c.text}` : ''}
                </li>
              ))}
          </ul>
        </section>
      )}

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
