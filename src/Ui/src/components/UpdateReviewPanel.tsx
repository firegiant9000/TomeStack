import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { CharacterView, ContentReference, UpdateReview } from '../api/types';

interface Props {
  characterId: string;
  characterName: string;
  contentName: string;
  from: ContentReference;
  to: ContentReference;
  onApplied: (view: CharacterView) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
}

/**
 * SPEC I-06: before a character moves to a newer revision, show what changes (mechanics diff, calculated values,
 * diagnostics, open choices and overrides whose calculated value moves). Nothing changes until "Apply update".
 */
export function UpdateReviewPanel({ characterId, characterName, contentName, from, to, onApplied, onCancel, onError }: Props) {
  const [review, setReview] = useState<UpdateReview>();
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    client.reviewUpdate(characterId, from, to).then(setReview).catch(onError);
  }, [characterId, from, to, onError]);

  useEffect(() => {
    if (review) heading.current?.focus();
  }, [review]);

  async function apply() {
    setBusy(true);
    try {
      onApplied(await client.applyUpdate(characterId, from, to));
    } catch (error) {
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  const title = `Update ${characterName}: ${contentName}`;
  return (
    <section aria-labelledby="update-heading" className="play-panel">
      <h3 id="update-heading" tabIndex={-1} ref={heading}>
        {title}
      </h3>
      {!review ? (
        <p className="hint">Comparing the revisions…</p>
      ) : (
        <>
          <table>
            <caption>Rule changes</caption>
            <thead>
              <tr>
                <th scope="col">Effect or property</th>
                <th scope="col">Change</th>
                <th scope="col">Before</th>
                <th scope="col">After</th>
              </tr>
            </thead>
            <tbody>
              {review.mechanics.properties.map((p) => (
                <tr key={`p-${p.property}`}>
                  <td>{p.property}</td>
                  <td>changed</td>
                  <td>{p.before ?? ''}</td>
                  <td>{p.after ?? ''}</td>
                </tr>
              ))}
              {review.mechanics.effects.map((e) => (
                <tr key={`e-${e.effectId}`}>
                  <td>{e.effectId}</td>
                  <td>{e.change}</td>
                  <td>
                    <code>{e.before ?? ''}</code>
                  </td>
                  <td>
                    <code>{e.after ?? ''}</code>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {review.fields.length === 0 ? (
            <p>No calculated value changes.</p>
          ) : (
            <table>
              <caption>Calculated values that change</caption>
              <thead>
                <tr>
                  <th scope="col">Field</th>
                  <th scope="col">Now</th>
                  <th scope="col">After the update</th>
                </tr>
              </thead>
              <tbody>
                {review.fields.map((f) => (
                  <tr key={f.field}>
                    <td>{f.label}</td>
                    <td>{f.before}</td>
                    <td>{f.after}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {review.affectedOverrides.length > 0 && (
            <p className="warn">
              Your overrides stay: {review.affectedOverrides.map((o) => o.field).join(', ')}. Their calculated values change underneath them.
            </p>
          )}
          {review.unresolvedChoices.length > 0 && (
            <p className="warn">Choices left to make after the update: {review.unresolvedChoices.map((c) => `${c.sourceName} (${c.choiceId})`).join(', ')}.</p>
          )}
          {review.newDiagnostics.length > 0 && (
            <ul className="warnings" aria-label="New problems after the update">
              {review.newDiagnostics.map((d) => (
                <li key={`${d.code}-${d.effectId ?? ''}-${d.content?.revisionId ?? ''}`}>{d.message}</li>
              ))}
            </ul>
          )}
        </>
      )}
      <div className="actions">
        <button type="button" onClick={apply} disabled={!review || busy}>
          Apply update
        </button>
        <button type="button" onClick={onCancel}>
          Keep the current revision
        </button>
      </div>
    </section>
  );
}
