import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';

/**
 * "Copy diagnostics": the app has no clipboard route, so the text is shown selected and the user presses Ctrl+C.
 * With `onError` the caller reports a failure itself (Settings); without it the box says so in plain words (the error fallback).
 */
export function DiagnosticsBox({ onError }: { onError?: (error: unknown) => void }) {
  // The count re-runs the selection when a second click brings back the same text.
  const [diagnostics, setDiagnostics] = useState<{ text: string; shown: number }>();
  const [failed, setFailed] = useState(false);
  const box = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (diagnostics === undefined) return;
    box.current?.focus();
    box.current?.select();
  }, [diagnostics]);

  function select() {
    box.current?.focus();
    box.current?.select();
  }

  async function show() {
    setFailed(false);
    try {
      const { text } = await client.copyDiagnostics();
      setDiagnostics((previous) => ({ text, shown: (previous?.shown ?? 0) + 1 }));
    } catch (error) {
      if (onError) onError(error);
      else setFailed(true);
    }
  }

  return (
    <>
      <button type="button" onClick={() => void show()}>
        Copy diagnostics
      </button>
      {failed && <p role="alert">The diagnostics could not be read. Restart TomeStack and try again.</p>}
      {diagnostics !== undefined && (
        <div className="diagnostics-copy">
          <label className="field">
            Diagnostics text
            <textarea ref={box} readOnly rows={12} value={diagnostics.text} />
          </label>
          <p className="hint">The text is selected: press Ctrl+C to copy it.</p>
          <button type="button" onClick={select}>
            Select all
          </button>
        </div>
      )}
    </>
  );
}
