import { useEffect, useState } from 'react';
import { client } from '../api/client';
import type { CharacterSummary, ContentComparison, ContentRevision, StudioEntry, TextChange } from '../api/types';
import { MechanicsDiffTable } from './MechanicsDiffTable';

const unsavedKey = 'unsaved';
const maxCharacters = 20;
const lineMark = { same: ' ', added: '+', removed: '−' } as const;
const lineWord = { same: 'unchanged', added: 'added', removed: 'removed' } as const;

const where = (w: string) => (w === 'name' ? 'Name' : w === 'summary' ? 'Description' : `Rule ${w.slice('effect:'.length)} text`);

/**
 * M5 slice 4 (B04 before/after tests, B07 diff viewer): two revisions of the entry being edited, one of them possibly
 * the unsaved revision on screen. It shows the rule changes, the texts line by line, and what each revision gives
 * unsaved copies of chosen characters (and a blank character, for a class or subclass). Nothing is written or applied:
 * characters change only through a reviewed update.
 */
export function ComparePanel(props: { entry?: StudioEntry; unsaved: ContentRevision; prepare: (r: ContentRevision) => ContentRevision; onError: (error: unknown) => void }) {
  const { entry, unsaved, onError } = props;
  const stored = entry?.revisions ?? [];
  const [fromId, setFromId] = useState(entry?.latestPublished?.revisionId ?? stored[0]?.revisionId ?? '');
  const [toId, setToId] = useState(unsavedKey);
  const [characters, setCharacters] = useState<CharacterSummary[]>([]);
  const [picked, setPicked] = useState<string[]>([]);
  const [blank, setBlank] = useState(false);
  const [level, setLevel] = useState('');
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ key: string; comparison: ContentComparison }>();
  const classLike = unsaved.kind === 'class' || unsaved.kind === 'subclass';

  useEffect(() => {
    let current = true;
    client
      .listCharacters()
      .then((all) => {
        if (current) setCharacters(all.filter((c) => !c.archivedAt));
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [onError]);

  if (stored.length === 0) return null; // nothing stored to compare with yet

  // The chosen "From", or, when it is not (or no longer) one of the stored revisions, the newest published one or the
  // first: an entry saved for the first time while the editor is open has revisions only from then on (review fix).
  const from = stored.find((r) => r.revisionId === fromId) ?? stored.find((r) => r.revisionId === entry?.latestPublished?.revisionId) ?? stored[0]!;
  const label = (r: ContentRevision, i: number) => `Revision ${i + 1} (${r.status}${r.revisionId === entry?.latestPublished?.revisionId ? ', newest published' : ''})`;
  const levelProblem = level !== '' && !(/^\d+$/.test(level) && Number(level) >= 1 && Number(level) <= 20) ? 'The level must be a whole number from 1 to 20.' : undefined;
  // What the result was computed for; any change of it, or of the revision on screen, hides the result.
  const key = JSON.stringify([from.revisionId, toId, picked, blank, level, toId === unsavedKey ? unsaved : null]);

  async function compare() {
    const computedFor = key;
    setBusy(true);
    try {
      const to = stored.find((r) => r.revisionId === toId);
      const comparison = await client.compare({
        from: { contentId: from.contentId, revisionId: from.revisionId },
        to: to && { contentId: to.contentId, revisionId: to.revisionId },
        toRevision: to ? undefined : props.prepare(unsaved),
        characterIds: picked,
        blank: classLike && blank ? { level: level === '' ? undefined : Number(level) } : undefined,
      });
      setResult({ key: computedFor, comparison });
    } catch (error) {
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  const shown = result?.key === key ? result.comparison : undefined;
  const fitting = characters.filter((c) => unsaved.rulesFamilies.includes(c.rulesFamily));
  return (
    <section aria-labelledby="compare-heading" className="effect-editor">
      <h4 id="compare-heading">Compare revisions</h4>
      <p className="hint">Nothing is saved or applied: characters keep their revision until you review and apply an update.</p>
      <label className="field">
        From
        <select value={from.revisionId} onChange={(e) => setFromId(e.target.value)}>
          {stored.map((r, i) => (
            <option key={r.revisionId} value={r.revisionId}>
              {label(r, i)}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        To
        <select value={toId} onChange={(e) => setToId(e.target.value)}>
          <option value={unsavedKey}>The revision on screen (unsaved)</option>
          {stored.map((r, i) => (
            <option key={r.revisionId} value={r.revisionId}>
              {label(r, i)}
            </option>
          ))}
        </select>
      </label>
      {fitting.length > 0 && (
        <fieldset>
          <legend>Run both on copies of (at most {maxCharacters})</legend>
          {fitting.map((c) => (
            <label key={c.id} className="choice">
              <input
                type="checkbox"
                checked={picked.includes(c.id)}
                disabled={!picked.includes(c.id) && picked.length >= maxCharacters}
                onChange={() => setPicked((p) => (p.includes(c.id) ? p.filter((x) => x !== c.id) : [...p, c.id]))}
              />
              {c.name}
            </label>
          ))}
        </fieldset>
      )}
      {classLike && (
        <>
          <label className="choice">
            <input type="checkbox" checked={blank} onChange={() => setBlank((b) => !b)} />
            Also a blank character
          </label>
          {blank && (
            <label className="field">
              Blank character's level (optional)
              <input
                inputMode="numeric"
                value={level}
                onChange={(e) => setLevel(e.target.value)}
                aria-invalid={levelProblem ? true : undefined}
                aria-describedby={levelProblem ? 'compare-level-error' : undefined}
              />
            </label>
          )}
          {blank && levelProblem && (
            <p id="compare-level-error" className="error">
              {levelProblem}
            </p>
          )}
        </>
      )}
      <button type="button" onClick={() => void compare()} disabled={busy || from.revisionId === toId || (blank && levelProblem !== undefined)}>
        Compare
      </button>

      {shown && (
        <div role="region" aria-label="Comparison results">
          <MechanicsDiffTable mechanics={shown.mechanics} />
          {shown.text.length === 0 ? <p>The texts are the same.</p> : shown.text.map((t) => <TextDiff key={t.where} change={t} />)}
          {shown.runs.map((run, i) => (
            <section key={`${run.characterId ?? 'blank'}-${i}`} aria-label={`${run.name} with each revision`}>
              <h5>{run.name}</h5>
              {run.problems.length > 0 ? (
                <p className="warn">Not run: {run.problems.map((p) => p.message).join(' ')}</p>
              ) : run.fields.length === 0 ? (
                <p>No calculated value changes.</p>
              ) : (
                <table>
                  <caption>Calculated values that change for {run.name}</caption>
                  <thead>
                    <tr>
                      <th scope="col">Field</th>
                      <th scope="col">From</th>
                      <th scope="col">To</th>
                    </tr>
                  </thead>
                  <tbody>
                    {run.fields.map((f) => (
                      <tr key={f.field}>
                        <td>{f.label}</td>
                        <td>{f.before}</td>
                        <td>{f.after}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              {run.newDiagnostics.length > 0 && (
                <ul className="warnings" aria-label={`New problems for ${run.name}`}>
                  {run.newDiagnostics.map((d, j) => (
                    <li key={`${d.code}-${j}`}>{d.message}</li>
                  ))}
                </ul>
              )}
            </section>
          ))}
        </div>
      )}
    </section>
  );
}

function TextDiff({ change }: { change: TextChange }) {
  return (
    <figure>
      <figcaption>
        {where(change.where)}
        {change.whole ? ' (too long to line up: old text, then new)' : ''}
      </figcaption>
      <pre className="text-diff">
        {change.lines.map((l, i) => (
          <span key={i} className={`diff-${l.kind}`}>
            <span aria-hidden="true">{lineMark[l.kind]} </span>
            <span className="visually-hidden">{lineWord[l.kind]}: </span>
            {l.text}
            {'\n'}
          </span>
        ))}
      </pre>
    </figure>
  );
}
