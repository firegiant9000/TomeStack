import { useEffect, useEffectEvent, useId, useState, type Ref } from 'react';
import { client } from '../api/client';
import type { SpellListKeyCount } from '../api/types';
import { normaliseSpellListKey, spellListKeyProblem } from '../spellListKey';

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/**
 * The spell list key box shared by "Add to a spell list…" (SpellListForm) and the studio's spell editor (tester feedback items
 * 10 and 12): a text box with a `datalist` of the keys already in use and their spell counts, a "Saved as: <key>" line for
 * what the typed text becomes, and a hint whether that key matches a list in use. The keys are read once, when it appears.
 * It has no live region and no button: the caller owns what happens with the key. With `showProblem`, a key that cannot be
 * saved is also written under the box and the box is marked invalid; otherwise the caller states the problem itself.
 */
export function SpellListKeyInput({ label, text, onText, onError, inputRef, showProblem }: {
  label: string;
  text: string;
  onText: (text: string) => void;
  onError: (error: unknown) => void;
  inputRef?: Ref<HTMLInputElement>;
  showProblem?: boolean;
}) {
  const [keys, setKeys] = useState<SpellListKeyCount[]>([]);
  const listId = useId();
  const previewId = useId();
  // An effect event, so a parent that passes a new function on each render does not read the keys again.
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

  const typed = text.trim() !== '';
  const key = normaliseSpellListKey(text);
  const problem = typed ? spellListKeyProblem(text) : undefined;
  const existing = typed && !problem ? keys.find((k) => k.key === key) : undefined;

  return (
    <>
      <label className="field">
        {label}
        <input
          ref={inputRef}
          type="text"
          list={listId}
          value={text}
          autoComplete="off"
          spellCheck={false}
          aria-invalid={showProblem && problem ? true : undefined}
          aria-describedby={previewId}
          onChange={(e) => onText(e.target.value)}
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
        {typed && !problem && <p>Saved as: {key}</p>}
        {showProblem && problem && <p className="error">{problem}</p>}
        {existing ? (
          <p className="hint">
            {existing.spells === 0
              ? 'A class uses this list, but no spell is on it yet.'
              : `A list with this key is already in use by ${plural(existing.spells, 'spell', 'spells')}.`}
          </p>
        ) : (
          typed && !problem && <p className="hint">No list has this key yet. A caster uses it only if its class names exactly this key.</p>
        )}
      </div>
    </>
  );
}
