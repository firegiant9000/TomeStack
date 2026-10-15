import { useId, useRef, useState, type FormEvent } from 'react';
import { TomeStackError } from '../api/transport';

/** Tester feedback item 8: roll any dice from the summary. The engine owns the limits; a refusal is shown here, linked to the box. */
function refusalText(error: unknown): string {
  if (error instanceof TomeStackError) {
    const messages = error.diagnostics.map((d) => d.message).filter((m) => m.length > 0);
    return messages.length > 0 ? messages.join(' ') : error.message;
  }
  return 'The dice could not be rolled. Try again.';
}

/**
 * A form with one text box and a Roll dice button. Blank (or spaces only) does nothing. A refusal sets `aria-invalid`, links the
 * visible message with `aria-describedby` and leaves focus on the box; the message goes when the text changes. There is no live
 * region here (the result is announced by Last roll's) and no `role="alert"` (WCAG 4.1.3: sparing).
 */
export function DiceBox({ onRoll }: { onRoll: (formula: string) => Promise<void> }) {
  const [text, setText] = useState('');
  const [error, setError] = useState<string>();
  const errorId = useId();
  const box = useRef<HTMLInputElement>(null);
  const busy = useRef(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const formula = text.trim();
    if (formula === '' || busy.current) return;
    busy.current = true;
    try {
      await onRoll(formula);
      setError(undefined);
    } catch (refused) {
      setError(refusalText(refused));
      box.current?.focus();
    } finally {
      busy.current = false;
    }
  }

  return (
    <form className="dice-box" aria-label="Roll dice" onSubmit={(event) => void submit(event)}>
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
            setText(event.target.value);
            setError(undefined);
          }}
        />
      </label>
      <button type="submit" className="roll">
        Roll dice
      </button>
      {error && (
        <p id={errorId} className="error">
          {error}
        </p>
      )}
    </form>
  );
}
