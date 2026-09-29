import { useEffect, useState } from 'react';
import { client } from '../api/client';
import type { CharacterSummary, ContentRevision, RulesFamilyId, SandboxView } from '../api/types';

const shown = ['hitPoints', 'armorClass', 'initiative', 'proficiencyBonus', 'attacks', 'spellSaveDc'];

/**
 * M5 slice 3 (B03; owner decision LIVING_SPECS D14): "Try it". The unsaved revision on screen is calculated as if
 * published, at a chosen level, on an unsaved copy of a saved character or on a blank one. Nothing is saved: the copy has
 * its own id and exists only in this panel, and saved characters only ever use published revisions.
 */
export function SandboxPanel(props: { revision: ContentRevision; prepare: (r: ContentRevision) => ContentRevision; onError: (error: unknown) => void }) {
  const { revision, onError } = props;
  const [characters, setCharacters] = useState<CharacterSummary[]>([]);
  const [characterId, setCharacterId] = useState('');
  const [level, setLevel] = useState('');
  const [family, setFamily] = useState('');
  const [busy, setBusy] = useState(false);
  // The result belongs to what it was calculated for: an edit of the revision, or another character, level or family,
  // hides it until "Try it" runs again (review fix).
  const [tried, setTried] = useState<{ revision: ContentRevision; characterId: string; level: string; family: string; view: SandboxView }>();
  const [announcement, setAnnouncement] = useState('');

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

  const fitting = characters.filter((c) => revision.rulesFamilies.includes(c.rulesFamily));
  const levelProblem = level !== '' && !(/^\d+$/.test(level) && Number(level) >= 1 && Number(level) <= 20) ? 'The level must be a whole number from 1 to 20.' : undefined;

  const blankFamily = revision.rulesFamilies.includes(family as RulesFamilyId) ? (family as RulesFamilyId) : revision.rulesFamilies[0];

  async function tryIt() {
    const inputs = { revision, characterId, level, family };
    setBusy(true);
    try {
      const view = await client.sandbox({
        revision: props.prepare(inputs.revision),
        characterId: characterId || undefined,
        rulesFamily: characterId ? undefined : blankFamily,
        level: level === '' ? undefined : Number(level),
      });
      setTried({ ...inputs, view });
      setAnnouncement(`Tried at total level ${view.view.character.level}; ${view.changes.length} value(s) change. Nothing was saved.`);
    } catch (error) {
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  const current = tried && tried.revision === revision && tried.characterId === characterId && tried.level === level && tried.family === family ? tried : undefined;
  const result = current?.view;
  const sheet = result?.view.sheet;
  return (
    <section aria-labelledby="sandbox-heading" className="effect-editor">
      <h4 id="sandbox-heading">Try it</h4>
      <p className="hint">
        Calculates this {revision.kind} as if it were published, on a copy that is never saved. Your characters keep using published revisions.
      </p>
      <label className="field">
        Try it on
        <select value={characterId} onChange={(e) => setCharacterId(e.target.value)}>
          <option value="">A blank character</option>
          {fitting.map((c) => (
            <option key={c.id} value={c.id}>
              A copy of {c.name}
            </option>
          ))}
        </select>
      </label>
      {!characterId && revision.rulesFamilies.length > 1 && (
        <label className="field">
          Blank character's rules family
          <select value={blankFamily} onChange={(e) => setFamily(e.target.value)}>
            {revision.rulesFamilies.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
        </label>
      )}
      <label className="field">
        {revision.kind === 'class' ? 'Level in this class' : 'Level in its class'} (optional)
        <input
          inputMode="numeric"
          value={level}
          onChange={(e) => setLevel(e.target.value)}
          aria-invalid={levelProblem ? true : undefined}
          aria-describedby={levelProblem ? 'sandbox-level-error' : undefined}
        />
      </label>
      {levelProblem && (
        <p id="sandbox-level-error" className="error">
          {levelProblem}
        </p>
      )}
      <button type="button" onClick={() => void tryIt()} disabled={busy || levelProblem !== undefined}>
        Try it
      </button>
      <p aria-live="polite" className="hint">
        {announcement}
      </p>

      {result && sheet && (
        <div role="region" aria-label="Try it results">
          <p>
            Unsaved {current.characterId ? 'copy' : 'blank character'} at total level {result.view.character.level} ({result.view.character.rulesFamily}).
          </p>
          {result.changes.length > 0 && (
            <>
              <h5>What changes on the copy</h5>
              <ul>
                {result.changes.map((c) => (
                  <li key={c.field}>
                    {c.label}: {c.before} → {c.after}
                  </li>
                ))}
              </ul>
            </>
          )}
          <h5>Values</h5>
          <ul>
            {sheet.fields
              .filter((f) => shown.includes(f.field) && (f.field !== 'spellSaveDc' || (sheet.spellcasting ?? []).length > 0))
              .map((f) => (
                <li key={f.field}>
                  {f.label}: {f.value}
                </li>
              ))}
            {(sheet.resources ?? []).map((r) => (
              <li key={`${r.content.revisionId}-${r.resourceId}`}>
                {r.label}: {r.maximum ?? 'tracked by hand'}
              </li>
            ))}
            {(sheet.scales ?? []).map((s) => (
              <li key={`${s.content.revisionId}-${s.scaleId}`}>
                {s.label}: {s.value}
              </li>
            ))}
          </ul>
          <h5>Features</h5>
          <ul>
            {(sheet.features ?? []).map((f) => (
              <li key={f.content.revisionId}>{f.name}</li>
            ))}
          </ul>
          {(sheet.diagnostics.length > 0 || result.validation.errors.length > 0) && (
            <>
              <h5>Problems</h5>
              <ul>
                {result.validation.errors.map((d, i) => (
                  <li key={`v-${d.code}-${i}`} className="error">
                    Error: {d.message}
                  </li>
                ))}
                {sheet.diagnostics.map((d, i) => (
                  <li key={`d-${d.code}-${i}`} className="warn">
                    {d.message}
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}
    </section>
  );
}
