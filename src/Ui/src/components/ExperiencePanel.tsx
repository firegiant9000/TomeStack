import { useRef, useState } from 'react';
import type { CharacterView } from '../api/types';
import { directSave, type SheetSave } from './sheetSave';

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  /** The sheet's one save queue (R132); a panel mounted alone saves directly. */
  saveCharacter?: SheetSave;
}

/** The bounds the service enforces (character.xp-out-of-range). */
const maxXp = 1_000_000;

const grouped = (n: number) => n.toLocaleString('en-US');

const readTotal = (text: string): number | undefined => {
  const n = Number(text);
  return text.trim() !== '' && Number.isInteger(n) && n >= 0 && n <= maxXp ? n : undefined;
};

/**
 * Wave 3 item 27 (character v9 `xp`, null = not tracked): optional experience points on Stats. "Track XP" turns the field on
 * (a total of 0) or off (the total is dropped); like the damage defences, each press saves at once through the sheet's one save
 * path, with no confirmation. "Level up available" is shown from the sheet and is display only: nothing here changes the level.
 */
export function ExperiencePanel({ view, onChanged, onError, saveCharacter }: Props) {
  const { character, sheet } = view;
  const persist = saveCharacter ?? directSave(character);
  const stored = character.xp;
  const tracked = stored !== undefined && stored !== null;
  const [draft, setDraft] = useState(String(stored ?? 0));
  // A save, snapshot restore or import changes the stored total without a remount, so the draft is re-seeded here (a remount
  // would drop focus, WCAG 2.4.3). A draft the user has not edited since the last seed takes the new value.
  const [seeded, setSeeded] = useState(stored);
  if (seeded !== stored) {
    if (draft === String(seeded ?? 0)) setDraft(String(stored ?? 0));
    setSeeded(stored);
  }
  const heading = useRef<HTMLHeadingElement>(null);
  const parsed = readTotal(draft);
  const invalid = parsed === undefined;
  const changed = parsed !== stored;

  async function save(change: (latest: typeof character) => typeof character, keepFocusOnHeading: boolean) {
    try {
      onChanged(await persist(change));
      if (keepFocusOnHeading) heading.current?.focus(); // the Save button is aria-disabled now that nothing differs; focus goes to the section
    } catch (error) {
      onError(error);
    }
  }

  const toggle = (on: boolean) => save((latest) => ({ ...latest, xp: on ? (latest.xp ?? 0) : undefined }), false);

  // Refused in the handler (the button is aria-disabled), with the reason shown in text beside the field.
  const saveTotal = () => (invalid || !changed ? undefined : save((latest) => ({ ...latest, xp: parsed }), true));

  return (
    <section aria-labelledby="experience-heading" className="field-group experience">
      <h3 id="experience-heading" tabIndex={-1} ref={heading}>
        Experience points
      </h3>
      <label>
        <input type="checkbox" checked={tracked} onChange={(e) => toggle(e.target.checked)} /> Track XP
      </label>
      {!tracked && <p className="hint">Off. Tick Track XP to keep a running total; TomeStack never changes your level for you.</p>}
      {tracked && (
        <>
          <div className="inline-form">
            <label className="field">
              Experience points
              <input
                type="number"
                min={0}
                max={maxXp}
                step={1}
                value={draft}
                aria-invalid={invalid ? true : undefined}
                aria-describedby={invalid ? 'experience-error' : undefined}
                onChange={(e) => setDraft(e.target.value)}
              />
            </label>
            {/* aria-disabled, never `disabled`: the press is refused in the handler and focus keeps its place (2.4.3, 4.1.2). */}
            <button type="button" aria-disabled={invalid || !changed || undefined} onClick={saveTotal}>
              Save experience points
            </button>
          </div>
          {invalid && (
            <p id="experience-error" className="error">
              Experience points must be a whole number from 0 to {grouped(maxXp)}.
            </p>
          )}
          {sheet.xpForNextLevel !== undefined && <p className="hint">Next level at {grouped(sheet.xpForNextLevel)}</p>}
          {sheet.levelUpAvailable === true && (
            <p className="level-up">
              <strong>Level up available</strong>
            </p>
          )}
        </>
      )}
    </section>
  );
}
