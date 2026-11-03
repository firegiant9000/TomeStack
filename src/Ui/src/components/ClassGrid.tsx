import { useEffect, useState } from 'react';
import { client } from '../api/client';
import type { ClassLevelRow, ClassTableView } from '../api/types';

type Loaded = { revisionId: string; view: ClassTableView } | { revisionId: string; failed: true };

const anySlots = (rows: ClassLevelRow[]) => rows.some((r) => r.slots && r.slots.length > 0);
const anyPact = (rows: ClassLevelRow[]) => rows.some((r) => (r.pactSlots ?? 0) > 0);
const anyChoices = (rows: ClassLevelRow[]) => rows.some((r) => r.choices.length > 0);

/**
 * Import Lab (D62 L2): the 20-level progression of one class revision, from `content.classTable`. Read-only. A feature is a
 * button that asks the studio to open it for editing; nothing here changes content. The table scrolls sideways inside a
 * focusable region, so a keyboard user can reach it at 400% zoom. No live region: the app keeps one status line.
 */
export function ClassGrid({ revisionId, draft, onEditFeature }: { revisionId: string; draft?: boolean; onEditFeature: (contentId: string) => void }) {
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    let current = true;
    Promise.resolve()
      .then(() => client.contentClassTable(revisionId))
      .then((view) => { if (current) setLoaded({ revisionId, view }); })
      .catch(() => { if (current) setLoaded({ revisionId, failed: true }); });
    return () => { current = false; };
  }, [revisionId]);

  // Only a reply for this revision is shown.
  const result = loaded && loaded.revisionId === revisionId ? loaded : null;

  return (
    <div>
      {draft && <p className="hint">Draft: not yet published</p>}
      {result === null ? (
        <p className="hint">Loading the class progression.</p>
      ) : 'failed' in result ? (
        <p className="hint">Couldn't load the class progression.</p>
      ) : (
        <Grid view={result.view} onEditFeature={onEditFeature} />
      )}
    </div>
  );
}

function Grid({ view, onEditFeature }: { view: ClassTableView; onEditFeature: (contentId: string) => void }) {
  const slots = anySlots(view.rows);
  const pact = anyPact(view.rows);
  const choices = anyChoices(view.rows);
  return (
    <div role="region" aria-label="Class progression, scrollable" tabIndex={0} className="class-table">
      <table>
        <caption>Class progression</caption>
        <thead>
          <tr>
            <th scope="col">Level</th>
            <th scope="col">Proficiency bonus</th>
            <th scope="col">Features</th>
            {choices && <th scope="col">Choices</th>}
            {view.columns.map((c) => (
              <th key={c} scope="col">{c}</th>
            ))}
            {slots && <th scope="col">Spell slots by spell level</th>}
            {pact && <th scope="col">Pact slots</th>}
          </tr>
        </thead>
        <tbody>
          {view.rows.map((r) => (
            <tr key={r.level}>
              <th scope="row">{r.level}</th>
              <td>+{r.proficiencyBonus}</td>
              <td>
                {r.features.length === 0
                  ? '—'
                  : r.features.map((f) => (
                      <button key={f.contentId} type="button" aria-label={`Edit ${f.name}`} onClick={() => onEditFeature(f.contentId)}>
                        {f.name}
                      </button>
                    ))}
              </td>
              {choices && (
                <td>
                  {r.choices.length === 0
                    ? '—'
                    : r.choices.map((c, i) => (
                        <span key={c.contentId}>
                          {i > 0 && ', '}
                          {c.name}
                        </span>
                      ))}
                </td>
              )}
              {view.columns.map((c) => (
                <td key={c}>{r.scales[c] || '—'}</td>
              ))}
              {slots && <td>{r.slots && r.slots.length > 0 ? r.slots.join(' / ') : '—'}</td>}
              {pact && <td>{(r.pactSlots ?? 0) > 0 ? `${r.pactSlots} at spell level ${r.pactLevel}` : '—'}</td>}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
