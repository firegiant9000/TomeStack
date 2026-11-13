import { useId, useState } from 'react';

type Choice = 'new' | 'sample' | 'sheet' | 'file';

export interface AddCharacterPanelProps {
  /** False while the app is still loading its rules; "Make a new one" and the sheet import need it. */
  canCreate: boolean;
  /** 'import' preselects "From a TomeStack file". */
  start?: 'import';
  onNew: () => void;
  onSheetImport: () => void;
  onFileImport: () => void;
  /** Passed only while no sample exists; without it the sample choice is hidden. */
  onTrySample?: () => void;
}

export function AddCharacterPanel({ canCreate, start, onNew, onSheetImport, onFileImport, onTrySample }: AddCharacterPanelProps) {
  const [choice, setChoice] = useState<Choice | undefined>(start === 'import' ? 'file' : undefined);
  const base = useId();
  const reasonId = `${base}-reason`;

  const options: { id: Choice; label: string; description: string; needsApp: boolean }[] = [
    { id: 'new', label: 'Make a new one', description: 'Step by step: rules, class, abilities, species, background, gear, choices.', needsApp: true },
    ...(onTrySample ? [{ id: 'sample' as const, label: 'Try the sample', description: 'A ready-made 3rd-level cleric to explore.', needsApp: false }] : []),
    {
      id: 'sheet',
      label: 'From a character sheet (PDF or text)',
      description: 'A PDF or pasted text. TomeStack reads the sheet and keeps nothing of the file.',
      needsApp: true,
    },
    { id: 'file', label: 'From a TomeStack file', description: 'A .zip someone exported from TomeStack.', needsApp: false },
  ];

  const chosen = options.find((o) => o.id === choice);
  const blocked = !!chosen && chosen.needsApp && !canCreate;

  function proceed() {
    if (!chosen || blocked) return;
    if (chosen.id === 'new') onNew();
    else if (chosen.id === 'sample') onTrySample?.();
    else if (chosen.id === 'sheet') onSheetImport();
    else onFileImport();
  }

  return (
    <section className="panel" aria-labelledby={`${base}-heading`}>
      <h1 id={`${base}-heading`}>Add a character</h1>
      <fieldset className="add-choices">
        <legend>How do you want to start?</legend>
        {options.map((o) => {
          const disabled = o.needsApp && !canCreate;
          return (
            <div key={o.id} className="add-choice">
              <label>
                <input
                  type="radio"
                  name="add-choice"
                  checked={choice === o.id}
                  disabled={disabled}
                  aria-describedby={disabled ? `${base}-${o.id}-d ${reasonId}` : `${base}-${o.id}-d`}
                  onChange={() => setChoice(o.id)}
                />{' '}
                {o.label}
              </label>
              <p id={`${base}-${o.id}-d`} className="muted">
                {o.description}
              </p>
            </div>
          );
        })}
      </fieldset>
      {!canCreate && (
        <p id={reasonId} className="muted">
          TomeStack is still loading its rules, so making a new one or reading a sheet is not available yet.
        </p>
      )}
      <div className="actions">
        <button type="button" className="primary" disabled={!chosen || blocked} onClick={proceed}>
          Continue
        </button>
      </div>
    </section>
  );
}
