import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { CharacterView, RestPreview } from '../api/types';

interface Props {
  characterId: string;
  onRested: (view: CharacterView, applied: number) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
}

/**
 * SPEC C-05, D01 (long rest only in M2): the proposal is shown first, every change ticked. The player unticks what does
 * not apply (for example, no food and drink) and confirms; nothing changes before "Finish long rest".
 */
export function RestPanel({ characterId, onRested, onCancel, onError }: Props) {
  const [preview, setPreview] = useState<RestPreview>();
  const [skipped, setSkipped] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    client.restPreview(characterId).then(setPreview).catch(onError);
  }, [characterId, onError]);

  // WCAG 2.4.3: the proposal opens with focus on its heading.
  useEffect(() => {
    if (preview) heading.current?.focus();
  }, [preview]);

  async function finish() {
    if (!preview) return;
    setBusy(true);
    try {
      onRested(await client.rest(characterId, preview.basis, skipped), preview.changes.length - skipped.length);
    } catch (error) {
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby="rest-heading" className="play-panel">
      <h3 id="rest-heading" tabIndex={-1} ref={heading}>
        Long rest
      </h3>
      {!preview ? (
        <p className="hint">Working out what a long rest recovers…</p>
      ) : (
        <>
          {preview.changes.length === 0 ? (
            <p>A long rest would change nothing.</p>
          ) : (
            <fieldset>
              <legend>These changes will be applied. Untick any that do not apply.</legend>
              {preview.changes.map((change) => (
                <label key={change.id} className="choice">
                  <input
                    type="checkbox"
                    checked={!skipped.includes(change.id)}
                    onChange={() => setSkipped((s) => (s.includes(change.id) ? s.filter((id) => id !== change.id) : [...s, change.id]))}
                  />
                  {change.label}: {change.from} → {change.to}
                  <span className="option-source">
                    {change.reason}
                    {change.condition ? `. ${change.condition}` : ''}
                  </span>
                </label>
              ))}
            </fieldset>
          )}
          {preview.manual.length > 0 && (
            <>
              <p>Do these by hand:</p>
              <ul className="warnings" aria-label="Rest steps to do by hand">
                {preview.manual.map((m) => (
                  <li key={`${m.code}-${m.content?.revisionId ?? ''}-${m.effectId ?? ''}`}>{m.message}</li>
                ))}
              </ul>
            </>
          )}
          <p className="hint">Short rests, hit dice and spell slots are not handled yet; adjust them by hand.</p>
        </>
      )}
      <div className="actions">
        <button type="button" onClick={finish} disabled={!preview || busy}>
          {busy ? 'Resting…' : 'Finish long rest'}
        </button>
        <button type="button" onClick={onCancel}>
          Cancel rest
        </button>
      </div>
    </section>
  );
}
