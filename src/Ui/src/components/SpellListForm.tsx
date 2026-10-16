import { useEffect, useEffectEvent, useId, useRef, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { SpellListKeyCount } from '../api/types';
import { normaliseSpellListKey, spellListKeyProblem } from '../spellListKey';

interface Props {
  /** What the key will be added to, in words: "3 spells". It names the confirm button. */
  subject: string;
  /** Saves the normalised key. The form stays held until it settles, and the caller closes it. */
  onSubmit: (key: string) => Promise<void>;
  onCancel: () => void;
  onError: (error: unknown) => void;
  /** The caller holds the action (an unsaved edit, a bulk action in flight): the button is aria-disabled and points at `heldReasonId`. */
  held?: boolean;
  heldReasonId?: string;
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/**
 * Tester feedback item 10: the key box shared by "Add to spell list…" on spell candidates and on studio spells. The existing
 * keys come from published content (with their spell counts) as a `datalist`, so a typo is less likely to start a list nobody
 * uses; the normalised key is shown before saving, and says whether it matches a list in use. Refusals keep focus and point at
 * a visible reason (aria-disabled, never `disabled`). It has no live region of its own: results go to the app status.
 */
export function SpellListForm({ subject, onSubmit, onCancel, onError, held, heldReasonId }: Props) {
  const [text, setText] = useState('');
  const [keys, setKeys] = useState<SpellListKeyCount[]>([]);
  const [busy, setBusy] = useState(false);
  const submitting = useRef(false);
  const input = useRef<HTMLInputElement>(null);
  const listId = useId();
  const previewId = useId();
  const reasonId = useId();

  // WCAG 2.4.3: opening the form moves focus to its first control.
  useEffect(() => input.current?.focus(), []);

  // The keys are read once when the form opens. `onError` is an effect event, so a parent that passes a new function on each
  // render (an inline arrow) does not read them again.
  const reportError = useEffectEvent(onError);
  useEffect(() => {
    let current = true;
    client
      .spellListKeys()
      .then((all) => {
        if (current) setKeys(all);
      })
      .catch((error: unknown) => {
        if (current) reportError(error);
      });
    return () => {
      current = false;
    };
  }, []);

  const key = normaliseSpellListKey(text);
  const problem = spellListKeyProblem(text);
  const existing = problem ? undefined : keys.find((k) => k.key === key);
  const reason = problem ?? (busy ? 'Adding…' : undefined);
  const refused = Boolean(problem) || busy || Boolean(held);
  const describedBy = problem || busy ? reasonId : held ? heldReasonId : undefined;

  async function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (refused || submitting.current) return;
    submitting.current = true; // a second press while the first is in flight does nothing
    setBusy(true);
    try {
      await onSubmit(key);
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }

  return (
    <form onSubmit={(event) => void submit(event)} className="play-panel">
      <label className="field">
        Spell list key
        <input
          ref={input}
          type="text"
          list={listId}
          value={text}
          autoComplete="off"
          spellCheck={false}
          aria-describedby={previewId}
          onChange={(e) => setText(e.target.value)}
        />
      </label>
      <datalist id={listId}>
        {keys.map((k) => (
          <option key={k.key} value={k.key}>
            {`${k.key} (${plural(k.spells, 'spell', 'spells')})`}
          </option>
        ))}
      </datalist>
      <div id={previewId}>
        {!problem && <p>Saved as: {key}</p>}
        {existing ? (
          <p className="hint">
            {existing.spells === 0
              ? 'A class uses this list, but no spell is on it yet.'
              : `A list with this key is already in use by ${plural(existing.spells, 'spell', 'spells')}.`}
          </p>
        ) : (
          !problem && <p className="hint">No list has this key yet. A caster uses it only if its class names exactly this key.</p>
        )}
      </div>
      <div className="actions">
        <button type="submit" aria-disabled={refused || undefined} aria-describedby={describedBy}>
          Add {subject} to this list
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
        {reason && (
          <span id={reasonId} className="hint">
            {reason}
          </span>
        )}
      </div>
    </form>
  );
}
