import { useEffect, useRef, useState, type ReactNode, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { CharacterView, CustomAttack, CustomItem } from '../api/types';
import { refusalText } from './DiceBox';
import {
  attackDraft,
  attackProblems,
  attackSummary,
  buildAttack,
  buildItem,
  damageTypes,
  itemDraft,
  itemProblems,
  maxCustomAttacks,
  maxCustomItems,
  maxName,
  maxNote,
  maxQuantity,
  maxToHit,
  minToHit,
  type AttackDraft,
  type ItemDraft,
  type Problems,
} from './customModel';

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

type Draft = Record<string, string | boolean>;

/** A refusal the dice engine gave for one field, shown linked to it. It goes when that field changes. */
interface FieldRefusal {
  field: string;
  message: string;
}

interface Config<T extends { id: string; name: string }, D extends Draft> {
  /** Element ids start with this. */
  prefix: string;
  /** The section heading (focusable), the last place focus can fall back to. */
  headingId: string;
  /** "item" or "attack", for the sentences. */
  noun: string;
  entries: T[];
  cap: number;
  draftOf: (entry?: T) => D;
  build: (id: string, draft: D) => T;
  problems: (draft: D) => Problems;
  /** The dice engine's verdict on a field, asked when Save is pressed; a refusal keeps the form open. */
  check?: (draft: D) => Promise<FieldRefusal | undefined>;
  persist: (next: T[]) => Promise<CharacterView>;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

interface Editing {
  id: string;
  isNew: boolean;
}

/**
 * The shared add / edit / remove behaviour of "Your own items" and "Your own attacks".
 *
 * - Save is held with aria-disabled (never `disabled`, so it keeps focus) and a linked reason; a press while held is refused.
 * - The draft is re-seeded when the stored entry changes without a remount (a restore or import), so focus is not lost (WCAG 2.4.3).
 *   Only fields the user has not edited since the last seed take the stored value.
 * - Text typed while a save is in flight is kept: the form then stays open on the saved entry instead of closing over it.
 * - Focus after add, save, cancel and remove always lands on a control that exists (never the page body).
 */
function useEntryEditor<T extends { id: string; name: string }, D extends Draft>(config: Config<T, D>) {
  const { entries, draftOf } = config;
  const [editing, setEditing] = useState<Editing | null>(null);
  const [draft, setDraft] = useState<D>(() => draftOf());
  const [seeded, setSeeded] = useState('');
  const [saving, setSaving] = useState(false);
  const [refusal, setRefusal] = useState<FieldRefusal>();
  const [removing, setRemoving] = useState(false);
  const running = useRef(false);
  const draftNow = useRef(draft);
  // A request is a new object each time, so asking for the same control twice still focuses it. It is served after the render
  // that holds the control (the request and the change that creates the control are batched into one render).
  const [focusRequest, setFocusRequest] = useState<{ id: string }>();
  const focusOn = (id: string) => setFocusRequest({ id });

  useEffect(() => {
    draftNow.current = draft;
  });
  const { prefix, headingId } = config;
  useEffect(() => {
    if (!focusRequest) return;
    // The asked-for control may not be on screen (its row is hidden by an open form, or Add is hidden while the New form is open):
    // fall back to the open form's Name box, then Add, then the section heading, so focus never drops to <body>.
    const target = [focusRequest.id, `${prefix}-name`, `${prefix}-add`, headingId].map((id) => document.getElementById(id)).find((el) => el !== null);
    target?.focus();
  }, [focusRequest, prefix, headingId]);

  const stored = editing && !editing.isNew ? entries.find((e) => e.id === editing.id) : undefined;
  if (editing && !editing.isNew) {
    if (!stored) {
      // The entry being edited is gone (a restore or import): close the form and leave focus on the Add button.
      setEditing(null);
      setRefusal(undefined);
      focusOn(`${config.prefix}-add`);
    } else {
      const storedKey = JSON.stringify(draftOf(stored));
      if (seeded !== storedKey) {
        const previous = JSON.parse(seeded) as D;
        const next = draftOf(stored);
        setSeeded(storedKey);
        setDraft(Object.fromEntries(Object.keys(next).map((k) => [k, draft[k] === previous[k] ? next[k] : draft[k]])) as D);
      }
    }
  }

  const { errors, incomplete } = config.problems(draft);
  const baseline = editing?.isNew === false && stored ? draftOf(stored) : undefined;
  const changed = baseline ? JSON.stringify(draft) !== JSON.stringify(baseline) : true;
  const invalid = Object.keys(errors).length > 0;
  const busy = saving || removing;
  const reason = saving
    ? `Saving the ${config.noun}…`
    : removing
      ? 'Removing…'
      : invalid
      ? 'Save is held until the fields marked above are fixed.'
      : incomplete
        ? `Save is held until the ${config.noun} has a name.`
        : !changed
          ? 'Nothing has changed yet.'
          : '';

  const rowId = (id: string) => `${config.prefix}-edit-${id}`;
  const atCap = entries.length >= config.cap;

  function begin(entry?: T) {
    if (!entry && atCap) return; // the Add button is held with its reason; this press is refused
    const id = entry?.id ?? crypto.randomUUID();
    const start = draftOf(entry);
    setEditing({ id, isNew: !entry });
    setDraft(start);
    setSeeded(JSON.stringify(start));
    setRefusal(undefined);
    focusOn(`${config.prefix}-name`);
  }

  function cancel() {
    const was = editing;
    setEditing(null);
    setRefusal(undefined);
    focusOn(was && !was.isNew ? rowId(was.id) : `${config.prefix}-add`);
  }

  async function save(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editing || reason !== '' || running.current) return;
    running.current = true;
    const submitted = draft; // anything typed after this press is not in the save
    const target = editing;
    setSaving(true);
    let saved: CharacterView | undefined;
    try {
      const verdict = await config.check?.(submitted);
      if (verdict) {
        setRefusal(verdict);
        focusOn(`${config.prefix}-${verdict.field}`);
      } else {
        const entry = config.build(target.id, submitted);
        const next = target.isNew ? [...entries, entry] : entries.map((e) => (e.id === target.id ? entry : e));
        saved = await config.persist(next);
      }
    } catch (error) {
      config.onError(error); // what was typed stays
    }
    running.current = false;
    setSaving(false);
    if (!saved) return;
    if (JSON.stringify(draftNow.current) === JSON.stringify(submitted)) {
      setEditing(null);
      focusOn(rowId(target.id));
    } else {
      // Text was typed during the save: keep it, now as an edit of the saved entry.
      setEditing({ id: target.id, isNew: false });
      setSeeded(JSON.stringify(submitted));
    }
    config.onChanged(saved);
    config.onStatus(`${target.isNew ? 'Added' : 'Saved'} ${config.build(target.id, submitted).name}.`);
  }

  async function remove(entry: T) {
    if (running.current) return; // the Remove buttons are held with their reason; this press is refused
    running.current = true;
    setRemoving(true);
    const index = entries.findIndex((e) => e.id === entry.id);
    const neighbour = entries[index + 1] ?? entries[index - 1];
    try {
      const saved = await config.persist(entries.filter((e) => e.id !== entry.id));
      focusOn(neighbour ? rowId(neighbour.id) : `${config.prefix}-add`);
      if (editing?.id === entry.id) setEditing(null);
      config.onChanged(saved);
      config.onStatus(`Removed ${entry.name}.`);
    } catch (error) {
      config.onError(error);
    }
    running.current = false;
    setRemoving(false);
  }

  const set = (key: string) => (value: string | boolean) => {
    setDraft((d) => ({ ...d, [key]: value }));
    if (refusal?.field === key) setRefusal(undefined);
  };

  return { editing, draft, errors, reason, saving, busy, refusal, atCap, rowId, begin, cancel, save, remove, set };
}

function TextBox({
  id,
  label,
  value,
  onChange,
  error,
  hint,
  type = 'text',
  min,
  max,
  placeholder,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  hint?: string;
  type?: 'text' | 'number';
  min?: number;
  max?: number;
  placeholder?: string;
}) {
  const describedBy = [hint ? `${id}-hint` : undefined, error ? `${id}-error` : undefined].filter(Boolean).join(' ') || undefined;
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        type={type}
        value={value}
        min={min}
        max={max}
        step={type === 'number' ? 1 : undefined}
        placeholder={placeholder}
        autoComplete="off"
        spellCheck={false}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        onChange={(e) => onChange(e.target.value)}
      />
      {hint && (
        <p id={`${id}-hint`} className="hint">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="error">
          {error}
        </p>
      )}
    </div>
  );
}

function EntryForm({
  prefix,
  noun,
  title,
  reason,
  onSubmit,
  onCancel,
  children,
}: {
  prefix: string;
  noun: string;
  title: string;
  reason: string;
  onSubmit: (event: SubmitEvent<HTMLFormElement>) => void;
  onCancel: () => void;
  children: ReactNode;
}) {
  const held = reason !== '';
  return (
    <form aria-label={title} className="details-form own-entry-form" onSubmit={onSubmit}>
      {children}
      <div className="actions">
        {/* aria-disabled, never `disabled`: the press is refused in the handler and the reason is linked (WCAG 2.4.3, 4.1.2). */}
        <button type="submit" aria-disabled={held || undefined} aria-describedby={held ? `${prefix}-save-reason` : undefined}>
          Save {noun}
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
      {held && (
        <p id={`${prefix}-save-reason`} className="hint">
          {reason}
        </p>
      )}
    </form>
  );
}

function AddButton({ prefix, label, atCap, capText, onClick }: { prefix: string; label: string; atCap: boolean; capText: string; onClick: () => void }) {
  return (
    <>
      <div className="actions">
        <button id={`${prefix}-add`} type="button" aria-disabled={atCap || undefined} aria-describedby={atCap ? `${prefix}-cap` : undefined} onClick={() => !atCap && onClick()}>
          {label}
        </button>
      </div>
      {atCap && (
        <p id={`${prefix}-cap`} className="hint">
          {capText}
        </p>
      )}
    </>
  );
}

/** Character schema v9 (D42, item 18): things carried that are not content. Never calculated, whatever the "equipped" box says. */
export function CustomItemsPanel({ view, onChanged, onError, onStatus }: Props) {
  const { character } = view;
  const entries: CustomItem[] = character.customItems ?? [];
  const editor = useEntryEditor<CustomItem, ItemDraft>({
    prefix: 'own-item',
    headingId: 'own-items-heading',
    noun: 'item',
    entries,
    cap: maxCustomItems,
    draftOf: itemDraft,
    build: buildItem,
    problems: itemProblems,
    persist: (next) => client.saveCharacter({ ...character, customItems: next }),
    onChanged,
    onError,
    onStatus,
  });
  const { editing, draft, errors, set } = editor;

  const form = (title: string) => (
    <EntryForm prefix="own-item" noun="item" title={title} reason={editor.reason} onSubmit={(e) => void editor.save(e)} onCancel={editor.cancel}>
      <TextBox id="own-item-name" label="Name" value={draft.name} onChange={set('name')} error={errors.name} hint={`Up to ${maxName} characters.`} />
      <TextBox id="own-item-quantity" label="Quantity" type="number" min={1} max={maxQuantity} value={draft.quantity} onChange={set('quantity')} error={errors.quantity} />
      <TextBox id="own-item-note" label="Note (optional)" value={draft.note} onChange={set('note')} error={errors.note} hint={`Up to ${maxNote} characters.`} />
      <label className="choice">
        <input type="checkbox" checked={draft.equipped} onChange={(e) => set('equipped')(e.target.checked)} />
        Equipped (a reminder only)
      </label>
    </EntryForm>
  );

  return (
    <section aria-labelledby="own-items-heading" className="play-panel">
      <h3 id="own-items-heading" tabIndex={-1}>Your own items</h3>
      <p className="hint">Things you carry that are not in the rules content. They never count toward Armor Class or any calculation.</p>
      {entries.length === 0 && !editing && <p className="hint">No items of your own yet.</p>}
      {(entries.length > 0 || editing?.isNew) && (
        <ul className="resources">
          {entries.map((item) => (
            <li key={item.id} className="resource">
              {editing?.id === item.id && !editing.isNew ? (
                form(`Edit ${item.name}`)
              ) : (
                <div className="own-entry-row">
                  <span>
                    <span className="option-name">{item.name}</span>
                    {item.quantity > 1 ? ` (${item.quantity})` : ''}
                    {item.equipped ? ', equipped' : ''}
                    {item.note ? <span className="hint"> · {item.note}</span> : null}
                  </span>
                  <span className="actions">
                    <button id={editor.rowId(item.id)} type="button" onClick={() => editor.begin(item)}>
                      Edit {item.name}
                    </button>
                    <button type="button" aria-disabled={editor.busy || undefined} aria-describedby={editor.busy ? 'own-item-busy' : undefined} onClick={() => void editor.remove(item)}>
                      Remove {item.name}
                    </button>
                  </span>
                </div>
              )}
            </li>
          ))}
          {editing?.isNew && <li className="resource">{form('New item')}</li>}
        </ul>
      )}
      {editor.busy && (
        <p id="own-item-busy" className="hint">
          Wait for the current change to finish.
        </p>
      )}
      {!editing?.isNew && (
        <AddButton prefix="own-item" label="Add an item of your own" atCap={editor.atCap} capText={`You can keep up to ${maxCustomItems} items of your own.`} onClick={() => editor.begin()} />
      )}
    </section>
  );
}

/** Character schema v9 (D42, item 18): attacks typed in by hand. Rolled on Play; the to-hit and damage are exactly what was typed. */
export function CustomAttacksPanel({ view, onChanged, onError, onStatus }: Props) {
  const { character } = view;
  const entries: CustomAttack[] = character.customAttacks ?? [];
  const editor = useEntryEditor<CustomAttack, AttackDraft>({
    prefix: 'own-attack',
    headingId: 'own-attacks-heading',
    noun: 'attack',
    entries,
    cap: maxCustomAttacks,
    draftOf: attackDraft,
    build: buildAttack,
    problems: attackProblems,
    // The dice engine owns the damage grammar: ask it (the same call the dice box uses) and show its own message.
    check: async (d) => {
      if (d.damage.trim() === '') return undefined;
      try {
        await client.rollDice(d.damage.trim(), undefined, 'Dice');
        return undefined;
      } catch (error) {
        return { field: 'damage', message: refusalText(error) };
      }
    },
    persist: (next) => client.saveCharacter({ ...character, customAttacks: next }),
    onChanged,
    onError,
    onStatus,
  });
  const { editing, draft, errors, refusal, set } = editor;
  const damageError = refusal?.field === 'damage' ? refusal.message : undefined;

  const form = (title: string) => (
    <EntryForm prefix="own-attack" noun="attack" title={title} reason={editor.reason} onSubmit={(e) => void editor.save(e)} onCancel={editor.cancel}>
      <TextBox id="own-attack-name" label="Name" value={draft.name} onChange={set('name')} error={errors.name} hint={`Up to ${maxName} characters.`} />
      <TextBox
        id="own-attack-toHit"
        label="To hit bonus"
        type="number"
        min={minToHit}
        max={maxToHit}
        value={draft.toHit}
        onChange={set('toHit')}
        error={errors.toHit}
        hint="Added to a d20. Typed in, never calculated."
      />
      <TextBox
        id="own-attack-damage"
        label="Damage (optional)"
        value={draft.damage}
        onChange={set('damage')}
        error={damageError}
        placeholder="1d6+2"
        hint="Dice such as 1d6+2. Leave blank for an attack with no damage roll."
      />
      <div className="field">
        <label htmlFor="own-attack-damageType">Damage type (optional)</label>
        <select id="own-attack-damageType" value={draft.damageType} onChange={(e) => set('damageType')(e.target.value)}>
          <option value="">None</option>
          {damageTypes.map((t) => (
            <option key={t} value={t}>
              {t[0]!.toUpperCase() + t.slice(1)}
            </option>
          ))}
        </select>
      </div>
      <TextBox id="own-attack-note" label="Note (optional)" value={draft.note} onChange={set('note')} error={errors.note} hint={`Up to ${maxNote} characters.`} />
    </EntryForm>
  );

  return (
    <section aria-labelledby="own-attacks-heading" className="play-panel">
      <h3 id="own-attacks-heading" tabIndex={-1}>Your own attacks</h3>
      <p className="hint">
        Attacks you type in, such as a weapon the rules content does not list. Roll them on Play. Nothing here is calculated.
      </p>
      {entries.length === 0 && !editing && <p className="hint">No attacks of your own yet.</p>}
      {(entries.length > 0 || editing?.isNew) && (
        <ul className="resources">
          {entries.map((attack) => (
            <li key={attack.id} className="resource">
              {editing?.id === attack.id && !editing.isNew ? (
                form(`Edit ${attack.name}`)
              ) : (
                <div className="own-entry-row">
                  <span>
                    <span className="option-name">{attackSummary(attack)}</span>
                    {attack.note ? <span className="hint"> · {attack.note}</span> : null}
                  </span>
                  <span className="actions">
                    <button id={editor.rowId(attack.id)} type="button" onClick={() => editor.begin(attack)}>
                      Edit {attack.name}
                    </button>
                    <button type="button" aria-disabled={editor.busy || undefined} aria-describedby={editor.busy ? 'own-attack-busy' : undefined} onClick={() => void editor.remove(attack)}>
                      Remove {attack.name}
                    </button>
                  </span>
                </div>
              )}
            </li>
          ))}
          {editing?.isNew && <li className="resource">{form('New attack')}</li>}
        </ul>
      )}
      {editor.busy && (
        <p id="own-attack-busy" className="hint">
          Wait for the current change to finish.
        </p>
      )}
      {!editing?.isNew && (
        <AddButton prefix="own-attack" label="Add an attack of your own" atCap={editor.atCap} capText={`You can keep up to ${maxCustomAttacks} attacks of your own.`} onClick={() => editor.begin()} />
      )}
    </section>
  );
}
