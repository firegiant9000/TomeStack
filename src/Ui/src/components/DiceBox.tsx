import { useId, useRef, useState, type SubmitEvent } from 'react';
import { TomeStackError } from '../api/transport';

/** Tester feedback item 8: roll any dice from the summary. The engine owns the limits; a refusal is shown here, linked to the box. */
export function refusalText(error: unknown): string {
  if (error instanceof TomeStackError) {
    const messages = error.diagnostics.map((d) => d.message).filter((m) => m.length > 0);
    return messages.length > 0 ? messages.join(' ') : error.message;
  }
  return 'The dice could not be rolled. Try again.';
}

/**
 * A form with one text box and a Roll dice button. Blank (or spaces only) does nothing. A refusal sets `aria-invalid`, links the
 * visible message with `aria-describedby` and leaves focus on the box; the message goes when the text changes. The message sits in
 * one permanently mounted polite status (WCAG 4.1.3: focus is already on the box, so only a live region announces a refusal after
 * Enter); a success is announced only by Last roll's region. No `role="alert"`.
 */
export function DiceBox({ onRoll }: { onRoll: (formula: string) => Promise<void> }) {
  const [text, setText] = useState('');
  const [error, setError] = useState<string>();
  const errorId = useId();
  const box = useRef<HTMLInputElement>(null);
  const busy = useRef(false);
  const edits = useRef(0);

  async function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const formula = text.trim();
    if (formula === '' || busy.current) return;
    busy.current = true;
    const sent = edits.current;
    try {
      await onRoll(formula);
      setError(undefined);
    } catch (refused) {
      // A refusal for text that has since changed would be shown against the wrong text.
      if (edits.current === sent) {
        setError(refusalText(refused));
        box.current?.focus();
      }
    } finally {
      busy.current = false;
    }
  }

  return (
    <form className="dice-box" aria-label="Roll dice" onSubmit={(event) => void submit(event)}>
      <div className="dice-box-row">
        <label>
          Dice to roll
          <input
            ref={box}
            type="text"
            value={text}
            maxLength={80}
            inputMode="text"
            autoComplete="off"
            spellCheck={false}
            placeholder="2d6+3"
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? errorId : undefined}
            onChange={(event) => {
              edits.current += 1;
              setText(event.target.value);
              setError(undefined);
            }}
          />
        </label>
        <button type="submit" className="roll">
          Roll dice
        </button>
      </div>
      <p id={errorId} className="error" aria-live="polite" aria-atomic="true">
        {error}
      </p>
    </form>
  );
}
