import { useRef, useState, type RefObject, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { CharacterView } from '../api/types';
import {
  appearanceFields,
  backgroundFields,
  buildDetails,
  hasProblem,
  listFields,
  listProblem,
  maxDetailText,
  maxListEntries,
  maxListEntry,
  personalityFields,
  readList,
  textProblem,
  toDraft,
  type Draft,
  type DraftKey,
  type TextField,
} from './detailsModel';

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** The heading, so the Stats link can move focus here. */
  headingRef?: RefObject<HTMLHeadingElement | null>;
  /**
   * False while the tab holding the form is hidden: the form is then not rendered (its draft is kept), so a hidden tab does not
   * add some eighty elements to every sheet. Defaults to shown.
   */
  shown?: boolean;
}

/** The hint shows only when this few characters are left, so it stays quiet while there is plenty of room. */
const nearLimit = 200;

const keys: DraftKey[] = [...[...personalityFields, ...appearanceFields, ...backgroundFields].map((f) => f.key), ...listFields.map((f) => f.key)];

const listKeys: DraftKey[] = listFields.map((f) => f.key);
const normalised = (key: DraftKey, text: string) => (listKeys.includes(key) ? readList(text).entries.join('\n') : text.trim());

function DetailText({ field, value, onChange }: { field: TextField; value: string; onChange: (text: string) => void }) {
  const id = `details-${field.key}`;
  const problem = textProblem(value);
  const left = maxDetailText - value.trim().length;
  const near = !problem && left < nearLimit;
  const describedBy = problem ? `${id}-error` : near ? `${id}-hint` : undefined;
  const shared = { id, value, 'aria-invalid': problem ? true : undefined, 'aria-describedby': describedBy } as const;
  return (
    <div className="field">
      <label htmlFor={id}>{field.label}</label>
      {field.multiline ? (
        <textarea {...shared} rows={3} onChange={(e) => onChange(e.target.value)} />
      ) : (
        <input {...shared} type="text" autoComplete="off" onChange={(e) => onChange(e.target.value)} />
      )}
      {problem && (
        <p id={`${id}-error`} className="error">
          {problem}
        </p>
      )}
      {near && (
        <p id={`${id}-hint`} className="hint">
          {left} {left === 1 ? 'character' : 'characters'} left
        </p>
      )}
    </div>
  );
}

function DetailList({ id, label, value, onChange }: { id: string; label: string; value: string; onChange: (text: string) => void }) {
  const problem = listProblem(value);
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <textarea
        id={id}
        value={value}
        rows={4}
        aria-invalid={problem ? true : undefined}
        aria-describedby={problem ? `${id}-hint ${id}-error` : `${id}-hint`}
        onChange={(e) => onChange(e.target.value)}
      />
      <p id={`${id}-hint`} className="hint">
        One per line. Up to {maxListEntries} entries of {maxListEntry} characters each.
      </p>
      {problem && (
        <p id={`${id}-error`} className="error">
          {problem}
        </p>
      )}
    </div>
  );
}

/**
 * Character schema v9 (D42, item 17): personality, appearance, backstory, languages and tools, in the player's own words.
 * Never calculated. Save is held with aria-disabled (never `disabled`, so it keeps focus and its place) until something
 * changed and every field is within the service's limits; the reason is linked. Text over a limit is a linked error and is
 * never cut. Saving sends the whole loaded character, so the v9 fields this form does not edit survive.
 */
export function DetailsPanel({ view, onChanged, onError, onStatus, headingRef, shown = true }: Props) {
  const { character } = view;
  const stored = toDraft(character.details);
  const storedKey = JSON.stringify(stored);
  const [draft, setDraft] = useState<Draft>(stored);
  // A save, snapshot restore or import changes the stored details without a remount, so the draft is re-seeded here (a remount
  // would drop focus to <body>, WCAG 2.4.3). Only fields the user has not edited since the last seed take the new value.
  const [seeded, setSeeded] = useState(storedKey);
  if (seeded !== storedKey) {
    const previous = JSON.parse(seeded) as Draft;
    setSeeded(storedKey);
    // Equivalent text (trimmed, blank list lines dropped) counts as untouched: the service stores it normalised.
    setDraft(Object.fromEntries(keys.map((k) => [k, normalised(k, draft[k]) === normalised(k, previous[k]) ? stored[k] : draft[k]])) as Draft);
  }
  const [saving, setSaving] = useState(false);
  const running = useRef(false);

  const built = buildDetails(draft);
  const invalid = hasProblem(draft);
  const changed = JSON.stringify(built ?? null) !== JSON.stringify(buildDetails(stored) ?? null);
  const reason = saving ? 'Saving the details…' : invalid ? 'Save is held until the fields marked above are fixed.' : !changed ? 'Nothing has changed yet.' : '';
  const held = reason !== '';

  const set = (key: DraftKey) => (text: string) => setDraft((d) => ({ ...d, [key]: text }));

  async function save(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (held || running.current) return; // aria-disabled with its reason shown, so this press is refused openly
    running.current = true;
    setSaving(true);
    let saved: CharacterView | undefined;
    try {
      saved = await client.saveCharacter({ ...character, details: built });
    } catch (error) {
      onError(error); // what was typed stays
    }
    running.current = false;
    setSaving(false);
    if (saved) {
      // Show what the service stored (trimmed), so the draft is never left holding raw whitespace that reads as an edit later.
      const answer = toDraft(saved.character.details);
      setSeeded(JSON.stringify(answer));
      setDraft(answer);
      onChanged(saved);
      onStatus('Details saved.');
    }
  }

  if (!shown) return null;

  return (
    <form aria-labelledby="details-heading" className="play-panel details-form" onSubmit={save}>
      <h3 id="details-heading" tabIndex={-1} ref={headingRef}>
        Details
      </h3>
      <p className="hint">
        Your own words about this character. A share package or backup carries them; a sheet or Foundry export does not. Print them by ticking
        &quot;Include details&quot; in the print preview.
      </p>
      <fieldset>
        <legend>Personality</legend>
        {personalityFields.map((f) => (
          <DetailText key={f.key} field={f} value={draft[f.key]} onChange={set(f.key)} />
        ))}
      </fieldset>
      <fieldset>
        <legend>Appearance</legend>
        <div className="details-grid">
          {appearanceFields.map((f) => (
            <DetailText key={f.key} field={f} value={draft[f.key]} onChange={set(f.key)} />
          ))}
        </div>
      </fieldset>
      <fieldset>
        <legend>Background story</legend>
        {backgroundFields.map((f) => (
          <DetailText key={f.key} field={f} value={draft[f.key]} onChange={set(f.key)} />
        ))}
      </fieldset>
      <fieldset>
        <legend>Languages and tools</legend>
        {listFields.map((f) => (
          <DetailList key={f.key} id={`details-${f.key}`} label={f.label} value={draft[f.key]} onChange={set(f.key)} />
        ))}
      </fieldset>
      <div className="actions">
        {/* aria-disabled, never `disabled`: the press is refused in the handler, the reason is linked, and focus stays here after a save (2.4.3, 4.1.2). */}
        <button type="submit" aria-disabled={held || undefined} aria-describedby={held ? 'details-save-reason' : undefined}>
          Save details
        </button>
      </div>
      {held && (
        <p id="details-save-reason" className="hint">
          {reason}
        </p>
      )}
    </form>
  );
}
