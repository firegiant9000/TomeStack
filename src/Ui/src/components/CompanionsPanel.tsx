import { useEffect, useState, type ReactNode, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { CharacterView, Companion, PlayAction, RollMode, RollTarget } from '../api/types';
import { refusalText } from './DiceBox';
import { AddButton, EntryForm, TextBox, useEntryEditor } from './CustomEntries';
import {
  attackLines,
  attackSummary,
  buildCompanion,
  companionDraft,
  companionHitPoints,
  companionProblems,
  damageTypeLabel,
  damageTypes,
  maxCompanionArmorClass,
  maxCompanionAttacks,
  maxCompanions,
  maxCompanionText,
  maxHitPoints,
  maxName,
  maxNote,
  maxToHit,
  minToHit,
  type AttackLine,
  type CompanionDraft,
} from './customModel';
import { directSave, type SheetSave } from './sheetSave';

export interface CompanionsPanelProps {
  view: CharacterView;
  act: (action: PlayAction) => void;
  roll: (target: RollTarget) => void;
  /** The summary's advantage / disadvantage choice, used by the attack rolls (damage is always normal). */
  rollMode?: RollMode;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** The sheet's one save queue (R132); a panel mounted alone saves directly. */
  saveCharacter?: SheetSave;
}

/**
 * Wave 3 item 23 (D39, R174): companions on Play. One named region per companion. The definition (name, kind, Armor Class, maximum,
 * speed, attack lines, note) is character data, saved through the sheet's character save. Current and temporary hit points are play
 * state and change only by the `companion*` play actions (SPEC C-05). Free text and typed numbers: nothing here is calculated, no
 * damage type is applied, and a rest does not touch a companion.
 */
export function CompanionsPanel({ view, act, roll, rollMode = 'normal', onChanged, onError, onStatus, saveCharacter }: CompanionsPanelProps) {
  const { character } = view;
  const persist = saveCharacter ?? directSave(character);
  const entries: Companion[] = character.companions ?? [];
  const [critical, setCritical] = useState(false);
  // WCAG 2.4.3: a new or removed attack line moves focus to a control that exists.
  const [focusLine, setFocusLine] = useState<{ id: string }>();
  const editor = useEntryEditor<Companion, CompanionDraft>({
    prefix: 'companion',
    headingId: 'companions-heading',
    noun: 'companion',
    entries,
    cap: maxCompanions,
    draftOf: companionDraft,
    build: buildCompanion,
    problems: companionProblems,
    // The dice engine owns the damage grammar: ask it (the same call the dice box uses) and show its own message on the line.
    check: async (d) => {
      const lines = attackLines(d);
      for (const [i, line] of lines.entries()) {
        if (line.damage.trim() === '') continue;
        try {
          await client.rollDice(line.damage.trim(), undefined, 'Dice');
        } catch (error) {
          return { field: 'attacks', message: refusalText(error), focus: `companion-attack${i}-damage` };
        }
      }
      return undefined;
    },
    persist: (change) => persist((latest) => ({ ...latest, companions: change(latest.companions ?? []) })),
    onChanged,
    onError,
    onStatus,
  });
  const { editing, draft, errors, refusal, set, replace } = editor;

  useEffect(() => {
    if (!focusLine) return;
    (document.getElementById(focusLine.id) ?? document.getElementById('companion-add-line'))?.focus();
  }, [focusLine]);

  const lines = editing ? attackLines(draft) : [];
  const setLines = (next: AttackLine[]) => replace({ ...draft, attacks: JSON.stringify(next) });
  const changeLine = (index: number, key: keyof AttackLine, value: string) => {
    setLines(lines.map((line, i) => (i === index ? { ...line, [key]: value } : line)));
  };
  const linesAtCap = lines.length >= maxCompanionAttacks;

  const form = (title: string) => (
    <EntryForm prefix="companion" noun="companion" title={title} reason={editor.reason} onSubmit={(e) => void editor.save(e)} onCancel={editor.cancel}>
      <TextBox id="companion-name" label="Name" value={draft.name} onChange={set('name')} error={errors.name} hint={`Up to ${maxName} characters.`} />
      <TextBox id="companion-kind" label="Kind (optional)" value={draft.kind} onChange={set('kind')} error={errors.kind} hint={`For example a familiar or a mount. Up to ${maxCompanionText} characters.`} />
      <TextBox
        id="companion-armorClass"
        label="Armor Class (optional)"
        type="number"
        min={0}
        max={maxCompanionArmorClass}
        value={draft.armorClass}
        onChange={set('armorClass')}
        error={errors.armorClass}
      />
      <TextBox
        id="companion-hitPointMaximum"
        label="Hit point maximum"
        type="number"
        min={0}
        max={maxHitPoints}
        value={draft.hitPointMaximum}
        onChange={set('hitPointMaximum')}
        error={errors.hitPointMaximum}
        hint="Typed in, never calculated. Current hit points are changed on the card, not here."
      />
      <TextBox id="companion-speed" label="Speed (optional)" value={draft.speed} onChange={set('speed')} error={errors.speed} hint={`Free text. Up to ${maxCompanionText} characters.`} />
      <TextBox id="companion-note" label="Note (optional)" value={draft.note} onChange={set('note')} error={errors.note} hint={`Up to ${maxNote} characters.`} />
      <fieldset>
        <legend>Attack lines (optional)</legend>
        {lines.map((line, i) => (
          <div key={line.id} className="companion-line">
            <TextBox id={`companion-attack${i}-name`} label={`Attack ${i + 1} name`} value={line.name} onChange={(v) => changeLine(i, 'name', v)} error={errors[`attack${i}-name`]} />
            <TextBox
              id={`companion-attack${i}-toHit`}
              label={`Attack ${i + 1} to hit bonus`}
              type="number"
              min={minToHit}
              max={maxToHit}
              value={line.toHit}
              onChange={(v) => changeLine(i, 'toHit', v)}
              error={errors[`attack${i}-toHit`]}
              hint="Added to a d20. Typed in, never calculated."
            />
            <TextBox
              id={`companion-attack${i}-damage`}
              label={`Attack ${i + 1} damage (optional)`}
              value={line.damage}
              onChange={(v) => changeLine(i, 'damage', v)}
              error={refusal?.focus === `companion-attack${i}-damage` ? refusal.message : undefined}
              placeholder="1d6+2"
              hint="Dice such as 1d6+2. Leave blank for an attack with no damage roll."
            />
            <div className="field">
              <label htmlFor={`companion-attack${i}-damageType`}>{`Attack ${i + 1} damage type (optional)`}</label>
              <select id={`companion-attack${i}-damageType`} value={line.damageType} onChange={(e) => changeLine(i, 'damageType', e.target.value)}>
                <option value="">None</option>
                {damageTypes.map((t) => (
                  <option key={t} value={t}>
                    {damageTypeLabel(t)}
                  </option>
                ))}
              </select>
            </div>
            <TextBox
              id={`companion-attack${i}-note`}
              label={`Attack ${i + 1} note (optional)`}
              value={line.note}
              onChange={(v) => changeLine(i, 'note', v)}
              error={errors[`attack${i}-note`]}
              hint={`Up to ${maxNote} characters.`}
            />
            <div className="actions">
              <button
                type="button"
                onClick={() => {
                  setLines(lines.filter((_, j) => j !== i));
                  setFocusLine({ id: 'companion-add-line' });
                }}
              >
                {`Remove attack ${i + 1}`}
              </button>
            </div>
          </div>
        ))}
        <div className="actions">
          <button
            id="companion-add-line"
            type="button"
            aria-disabled={linesAtCap || undefined}
            aria-describedby={linesAtCap ? 'companion-lines-cap' : undefined}
            onClick={() => {
              if (linesAtCap) return;
              const added: AttackLine = { id: crypto.randomUUID(), name: '', toHit: '0', damage: '', damageType: '', note: '' };
              setLines([...lines, added]);
              setFocusLine({ id: `companion-attack${lines.length}-name` });
            }}
          >
            Add an attack line
          </button>
        </div>
        {linesAtCap && (
          <p id="companion-lines-cap" className="hint">
            {`A companion can have up to ${maxCompanionAttacks} attack lines.`}
          </p>
        )}
      </fieldset>
    </EntryForm>
  );

  const anyDamage = entries.some((c) => c.attacks?.some((a) => a.damage));

  return (
    <section aria-labelledby="companions-heading" className="play-panel">
      <h3 id="companions-heading" tabIndex={-1}>
        Companions
      </h3>
      <p className="hint">
        Familiars, mounts and the like, kept as free text. TomeStack applies no rules to them: you type their numbers and press the buttons.
        Their hit points change only here, and a rest does not change them.
      </p>
      {entries.length === 0 && !editing && <p className="hint">No companions yet.</p>}
      {anyDamage && (
        <label className="choice">
          <input type="checkbox" checked={critical} onChange={(e) => setCritical(e.target.checked)} />
          Companion critical hit (double the damage dice)
        </label>
      )}
      {(entries.length > 0 || editing?.isNew) && (
        <ul className="resources">
          {entries.map((companion) => (
            <li key={companion.id} className="resource companion-card">
              <CompanionCard
                view={view}
                companion={companion}
                act={act}
                roll={roll}
                rollMode={rollMode}
                critical={critical}
                busy={editor.busy}
                editing={editing?.id === companion.id && !editing.isNew}
                form={form(`Edit ${companion.name}`)}
                onEdit={() => editor.begin(companion)}
                onRemove={() => void editor.remove(companion)}
                editId={editor.rowId(companion.id)}
              />
            </li>
          ))}
          {editing?.isNew && <li className="resource companion-card">{form('New companion')}</li>}
        </ul>
      )}
      {editor.busy && (
        <p id="companion-busy" className="hint">
          Wait for the current change to finish.
        </p>
      )}
      {!editing?.isNew && (
        <AddButton prefix="companion" label="Add a companion" atCap={editor.atCap} capText={`You can keep up to ${maxCompanions} companions.`} onClick={() => editor.begin()} />
      )}
    </section>
  );
}

/** One companion: a named region. Its hit point controls follow the character's (same labels), held with aria-disabled and a linked reason. */
function CompanionCard({
  view,
  companion,
  act,
  roll,
  rollMode,
  critical,
  busy,
  editing,
  form,
  onEdit,
  onRemove,
  editId,
}: {
  view: CharacterView;
  companion: Companion;
  act: (action: PlayAction) => void;
  roll: (target: RollTarget) => void;
  rollMode: RollMode;
  critical: boolean;
  busy: boolean;
  editing: boolean;
  form: ReactNode;
  onEdit: () => void;
  onRemove: () => void;
  editId: string;
}) {
  const [amount, setAmount] = useState('');
  const hp = companionHitPoints(view, companion);
  const key = `companion-${companion.id}`;
  const n = Number(amount);
  const entered = amount !== '' && Number.isInteger(n) && n >= 0 && n <= maxHitPoints;
  const amountReason = amount !== '' && !entered ? `Enter a whole number from 0 to ${maxHitPoints}.` : '';
  const aboveMaximum = entered && n > companion.hitPointMaximum;
  const setReason = aboveMaximum ? `Current hit points cannot be above the maximum (${companion.hitPointMaximum}).` : '';
  const target = { companionId: companion.id };

  function apply(action: PlayAction) {
    act(action);
    setAmount('');
  }

  const heldButton = (label: string, held: boolean, reasonId: string | undefined, press: () => void) => (
    <button type="button" aria-label={`${label}: ${companion.name}`} aria-disabled={held || undefined} aria-describedby={held ? reasonId : undefined} onClick={() => !held && press()}>
      {label}
    </button>
  );

  return (
    <section aria-labelledby={`${key}-heading`}>
      <h4 id={`${key}-heading`} tabIndex={-1}>
        {companion.name}
      </h4>
      {editing ? (
        form
      ) : (
        <>
          {companion.kind && <p className="hint">{companion.kind}</p>}
          {(companion.armorClass !== undefined || companion.speed) && (
            <p className="companion-stats">
              {companion.armorClass !== undefined && <span>{`Armor Class ${companion.armorClass}`}</span>}
              {companion.speed && <span>{`Speed ${companion.speed}`}</span>}
            </p>
          )}
          <p>{`Hit points: ${hp.current} of ${companion.hitPointMaximum}${hp.temporary > 0 ? `, ${hp.temporary} temporary` : ''}`}</p>
          <form className="inline-form" onSubmit={(e: SubmitEvent<HTMLFormElement>) => e.preventDefault()}>
            <div className="field">
              <label htmlFor={`${key}-amount`}>Amount</label>
              <input
                id={`${key}-amount`}
                type="number"
                min={0}
                max={maxHitPoints}
                value={amount}
                aria-invalid={amountReason ? true : undefined}
                aria-describedby={amountReason ? `${key}-reason` : undefined}
                onChange={(e) => setAmount(e.target.value)}
              />
            </div>
            {heldButton('Take damage', !entered, `${key}-reason`, () => apply({ action: 'companionDamage', ...target, amount: n }))}
            {heldButton('Heal', !entered, `${key}-reason`, () => apply({ action: 'companionHeal', ...target, amount: n }))}
            {heldButton('Set hit points', !entered || aboveMaximum, aboveMaximum ? `${key}-set-reason` : `${key}-reason`, () =>
              apply({ action: 'companionSetHitPoints', ...target, amount: n }),
            )}
            {heldButton('Set temporary hit points', !entered, `${key}-reason`, () =>
              apply({ action: 'companionSetHitPoints', ...target, amount: hp.current, temporary: n }),
            )}
          </form>
          {amountReason && (
            <p id={`${key}-reason`} className="hint">
              {amountReason}
            </p>
          )}
          {setReason && (
            <p id={`${key}-set-reason`} className="hint">
              {setReason}
            </p>
          )}
          {(companion.attacks ?? []).length > 0 && (
            <ul className="features">
              {(companion.attacks ?? []).map((attack) => {
                const attackTarget = { companionId: companion.id, attackId: attack.id };
                return (
                  <li key={attack.id} className="feature">
                    <span className="option-name">{attackSummary(attack)}</span>
                    {attack.note ? <span className="hint"> · {attack.note}</span> : null}{' '}
                    <button type="button" aria-label={`Roll ${attack.name} attack for ${companion.name}`} onClick={() => roll({ companionAttack: attackTarget, mode: rollMode })}>
                      Roll {attack.name} attack
                    </button>
                    {attack.damage && (
                      <button type="button" aria-label={`Roll ${attack.name} damage for ${companion.name}`} onClick={() => roll({ companionAttack: attackTarget, damage: true, critical })}>
                        Roll {attack.name} damage
                      </button>
                    )}
                  </li>
                );
              })}
            </ul>
          )}
          {companion.note && <p className="hint">{companion.note}</p>}
          <div className="actions">
            <button id={editId} type="button" onClick={onEdit}>
              Edit {companion.name}
            </button>
            <button type="button" aria-disabled={busy || undefined} aria-describedby={busy ? 'companion-busy' : undefined} onClick={() => !busy && onRemove()}>
              Remove {companion.name}
            </button>
          </div>
        </>
      )}
    </section>
  );
}
