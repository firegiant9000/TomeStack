import { useEffect, useId, useRef, useState, type SubmitEvent } from 'react';
import { normaliseSpellListKey, spellListKeyProblem } from '../spellListKey';
import { SpellListKeyInput } from './SpellListKeyInput';

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

/**
 * Tester feedback item 10: the key box shared by "Add to spell list…" on spell candidates and on studio spells. The existing
 * keys come from published content (with their spell counts) as a `datalist`, so a typo is less likely to start a list nobody
 * uses; the normalised key is shown before saving, and says whether it matches a list in use. Refusals keep focus and point at
 * a visible reason (aria-disabled, never `disabled`). It has no live region of its own: results go to the app status.
 */
export function SpellListForm({ subject, onSubmit, onCancel, onError, held, heldReasonId }: Props) {
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const submitting = useRef(false);
  const input = useRef<HTMLInputElement>(null);
  const reasonId = useId();

  // WCAG 2.4.3: opening the form moves focus to its first control.
  useEffect(() => input.current?.focus(), []);

  const key = normaliseSpellListKey(text);
  const problem = spellListKeyProblem(text);
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
      <SpellListKeyInput label="Spell list key" text={text} onText={setText} onError={onError} inputRef={input} />
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
