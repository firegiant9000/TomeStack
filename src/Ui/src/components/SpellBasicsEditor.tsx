import { useEffect, useEffectEvent, useId, useState } from 'react';
import { client } from '../api/client';
import type { SpellListKeyCount } from '../api/types';
import { schools, setSpell, spellOf, type SpellPatch } from '../entryBasics';
import { normaliseSpellListKey, spellListKeyProblem } from '../spellListKey';
import type { BasicsProps } from './OriginPieces';

const ordinal = (n: number) => `${n}${n === 1 ? 'st' : n === 2 ? 'nd' : n === 3 ? 'rd' : 'th'} level`;
const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
const problemField = 'spell-lists';

/**
 * Tester feedback item 12: a spell's own data, written as the one `spell` effect the bundled spells and the PDF import use
 * (content v5: level, school, ritual, casting time, range, components, duration, concentration, lists, text). Fields the form
 * does not show (the attack kind, a saving throw, dice) are kept as they are. The lists use item 10's key normaliser and the
 * keys already in use (a `datalist`), and show the saved key before it is added. Nothing here runs code.
 */
export function SpellBasicsEditor(props: BasicsProps) {
  const { revision, onEffects } = props;
  const spell = spellOf(revision.effects);
  const lists = spell?.lists ?? [];
  const level = spell?.level ?? 1;
  const school = spell?.school ?? '';
  const set = (patch: SpellPatch) => onEffects((current) => setSpell(current, patch));

  const [keys, setKeys] = useState<SpellListKeyCount[]>([]);
  const [text, setText] = useState('');
  const dataListId = useId();
  const previewId = useId();
  const reasonId = useId();
  const reportError = useEffectEvent(props.onError);
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
  const onList = typed && !problem && lists.includes(key);
  const existing = typed && !problem ? keys.find((k) => k.key === key) : undefined;
  const refusal = !typed ? 'Type a spell list key.' : (problem ?? (onList ? `The spell is already on the list ${key}.` : undefined));

  const { onProblem } = props;
  const pending = typed ? 'Spell lists: add the typed list key with Add list, or clear it.' : undefined;
  useEffect(() => {
    onProblem(problemField, pending);
    return () => onProblem(problemField, undefined);
  }, [pending, onProblem]);

  return (
    <section aria-labelledby="spell-basics-heading" className="effect-editor origin-basics">
      <h4 id="spell-basics-heading">Spell basics</h4>
      <fieldset id="spell-basics-core" tabIndex={-1}>
        <legend>What the spell is</legend>
        <div className="origin-grid">
          <label className="field">
            Level
            <select value={level} onChange={(e) => set({ level: Number(e.target.value) })}>
              {Array.from({ length: 10 }, (_, n) => (
                <option key={n} value={n}>
                  {n === 0 ? 'Cantrip' : ordinal(n)}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            School
            <select value={school} onChange={(e) => set({ school: e.target.value })}>
              <option value="">Not set</option>
              {school !== '' && !(schools as readonly string[]).includes(school) && <option value={school}>{school}</option>}
              {schools.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
          <label className="choice">
            <input type="checkbox" checked={spell?.ritual ?? false} onChange={(e) => set({ ritual: e.target.checked })} />
            Ritual
          </label>
          <label className="choice">
            <input type="checkbox" checked={spell?.concentration ?? false} onChange={(e) => set({ concentration: e.target.checked })} />
            Concentration
          </label>
        </div>
      </fieldset>

      <fieldset id="spell-basics-casting" tabIndex={-1}>
        <legend>Casting</legend>
        <div className="origin-grid">
          <label className="field">
            Casting time
            <input value={spell?.castingTime ?? ''} onChange={(e) => set({ castingTime: e.target.value })} />
          </label>
          <label className="field">
            Range
            <input value={spell?.range ?? ''} onChange={(e) => set({ range: e.target.value })} />
          </label>
          <label className="field">
            Components
            <input value={spell?.components ?? ''} onChange={(e) => set({ components: e.target.value })} />
          </label>
          <label className="field">
            Duration
            <input value={spell?.duration ?? ''} onChange={(e) => set({ duration: e.target.value })} />
          </label>
        </div>
        <label className="field">
          Spell text
          <textarea rows={4} value={spell?.text ?? ''} onChange={(e) => set({ text: e.target.value })} />
        </label>
      </fieldset>

      <fieldset id="spell-basics-lists" tabIndex={-1}>
        <legend>Spell lists</legend>
        {lists.length === 0 ? (
          <p className="hint">On no spell list yet: no caster can choose it.</p>
        ) : (
          <ul aria-label="Spell lists" className="origin-list">
            {lists.map((l) => (
              <li key={l}>
                <span className="option-name">{l}</span>{' '}
                <button type="button" onClick={() => set({ lists: lists.filter((x) => x !== l) })}>
                  Remove list {l}
                </button>
              </li>
            ))}
          </ul>
        )}
        <label className="field">
          Add a spell list
          <input
            type="text"
            list={dataListId}
            value={text}
            autoComplete="off"
            spellCheck={false}
            aria-invalid={problem ? true : undefined}
            aria-describedby={previewId}
            onChange={(e) => setText(e.target.value)}
          />
        </label>
        <datalist id={dataListId}>
          {keys.map((k) => (
            <option key={k.key} value={k.key}>
              {`${k.key} (${plural(k.spells, 'spell', 'spells')})`}
            </option>
          ))}
        </datalist>
        <div id={previewId}>
          {typed && !problem && <p>Saved as: {key}</p>}
          {problem && <p className="error">{problem}</p>}
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
        <button
          type="button"
          aria-disabled={refusal !== undefined || undefined}
          aria-describedby={refusal === undefined ? undefined : reasonId}
          onClick={() => {
            if (refusal !== undefined) return;
            set({ lists: [...lists, key] });
            setText('');
          }}
        >
          Add list
        </button>
        {refusal !== undefined && (
          <span id={reasonId} className="hint">
            {refusal}
          </span>
        )}
      </fieldset>
    </section>
  );
}
