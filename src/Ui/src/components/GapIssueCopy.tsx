import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { GapNote } from '../api/types';
import { gapIssueText } from '../gapIssueText';

/**
 * "Copy as issue text" for one gap note: the app has no clipboard route, so the text is shown selected and the user
 * presses Ctrl+C (as in DiagnosticsBox). Close returns focus to the button.
 */
export function GapIssueCopy({ note }: { note: GapNote }) {
  const [text, setText] = useState<string>();
  const button = useRef<HTMLButtonElement>(null);
  const box = useRef<HTMLTextAreaElement>(null);
  const pending = useRef(false);
  const label = note.target.label ?? 'this note';

  useEffect(() => {
    if (text === undefined) return;
    box.current?.focus();
    box.current?.select();
  }, [text]);

  async function show() {
    if (pending.current) return; // a press while the version request is in flight would reopen the box after Close
    pending.current = true;
    let version = 'unknown version';
    try {
      version = (await client.info()).version;
    } catch {
      // The text is still useful without the version; the person can add it.
    } finally {
      pending.current = false;
    }
    setText(gapIssueText(note, version));
  }

  function close() {
    setText(undefined);
    button.current?.focus();
  }

  return (
    <>
      <button ref={button} type="button" aria-label={`Copy as issue text: ${label}`} onClick={() => void show()}>
        Copy as issue text
      </button>
      {text !== undefined && (
        <div className="diagnostics-copy">
          <label className="field">
            {`Issue text: ${label}`}
            <textarea ref={box} readOnly rows={6} value={text} />
          </label>
          <p className="hint">The text is selected: press Ctrl+C to copy it.</p>
          <p className="hint">This may include text from your books or homebrew; check before posting.</p>
          <button type="button" onClick={close}>
            Close
          </button>
        </div>
      )}
    </>
  );
}
