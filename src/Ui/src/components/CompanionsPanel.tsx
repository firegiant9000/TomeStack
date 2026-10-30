import { useEffect, useState, type ReactNode, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { AttackEntry, CharacterSheet, CharacterView, Companion, CompanionValue, PlayAction, ResourceValue, RollMode, RollTarget } from '../api/types';
import { attackAvailability, poolFor } from '../actionRows';
import { refusalText } from './DiceBox';
import { ExplainList } from './ExplainList';
import { AttackItem, ResourceCard } from './PlayPanels';
import { AddButton, EntryForm, TextBox, useEntryEditor } from './CustomEntries';
import {
  attackLines,
  attackSummary,
  buildCompanion,
  companionDraft,
  companionHitPoints,
  companionProblems,
  contentCompanionModel,
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
  type CardModel,
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

  // MECH-X item 4: companions that content gives come first, calculated; their attacks and pools are drawn here, not in Actions or Resources.
  const sheet = view.sheet as CharacterSheet | undefined;
  const given = sheet?.companions ?? [];
  const givenModel = (value: CompanionValue): CardModel => ({
    ...contentCompanionModel(value),
    extra: (
      <>
        {contentAttacks(sheet, value).length > 0 && (
          <ul className="features">
            {contentAttacks(sheet, value).map((attack) => (
              <AttackItem
                key={`${attack.item.revisionId}-${attack.effectId}`}
                attack={attack}
                label={attack.name}
                critical={critical}
                rollMode={rollMode}
                roll={roll}
                rollDamage={(target) => roll(target)}
                pool={poolFor(sheet?.resources ?? [], attack.resourceId ?? undefined, attack.costResourceContent ?? undefined)}
                availability={attackAvailability(attack, poolFor(sheet?.resources ?? [], attack.resourceId ?? undefined, attack.costResourceContent ?? undefined))}
              />
            ))}
          </ul>
        )}
        {contentPools(sheet, value).length > 0 && (
          <ul className="resources">
            {contentPools(sheet, value).map((resource) => (
              <ResourceCard key={`${resource.content.revisionId}-${resource.resourceId}`} resource={resource} act={act} level={5} />
            ))}
          </ul>
        )}
      </>
    ),
  });
  const anyDamage = entries.some((c) => c.attacks?.some((a) => a.damage)) || given.some((v) => contentAttacks(sheet, v).some((a) => a.damage));

  return (
    <section aria-labelledby="companions-heading" className="play-panel">
      <h3 id="companions-heading" tabIndex={-1}>
        Companions
      </h3>
      <p className="hint">
        Familiars, mounts and the like, kept as free text. TomeStack applies no rules to them: you type their numbers and press the buttons.
        Their hit points change only here, and a rest does not change them. A companion marked &quot;From&quot; a feature or item is calculated
        from it: you cannot edit or remove it here.
      </p>
      {entries.length === 0 && given.length === 0 && !editing && <p className="hint">No companions yet.</p>}
      {anyDamage && (
        <label className="choice">
          <input type="checkbox" checked={critical} onChange={(e) => setCritical(e.target.checked)} />
          Companion critical hit (double the damage dice)
        </label>
      )}
      {(entries.length > 0 || given.length > 0 || editing?.isNew) && (
        <ul className="resources">
          {given.map((value) => {
            const model = givenModel(value);
            return (
              <li key={model.key} className="resource companion-card">
                <CompanionCard model={model} act={act} busy={editor.busy} editing={false} />
              </li>
            );
          })}
          {entries.map((companion) => (
            <li key={companion.id} className="resource companion-card">
              <CompanionCard
                model={handCardModel(view, companion, roll, rollMode, critical)}
                act={act}
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

/** A hand (W3) companion as a card: its kind is the subtitle; its attack lines and note follow the hit point controls. */
function handCardModel(
  view: CharacterView,
  companion: Companion,
  roll: (target: RollTarget) => void,
  rollMode: RollMode,
  critical: boolean,
): CardModel {
  const hp = companionHitPoints(view, companion);
  return {
    key: `companion-${companion.id}`,
    name: companion.name,
    subtitle: companion.kind,
    armorClass: companion.armorClass,
    speed: companion.speed,
    hitPointMaximum: companion.hitPointMaximum,
    current: hp.current,
    temporary: hp.temporary,
    target: { companionId: companion.id },
    extra: (
      <>
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
      </>
    ),
  };
}

/** The attacks of a content companion, as the Actions list shows an attack (rolled by its content and effect). */
function contentAttacks(sheet: CharacterSheet | undefined, value: CompanionValue): AttackEntry[] {
  return (sheet?.attacks ?? []).filter(
    (a) => a.item.contentId === value.content.contentId && (a.companionId ?? value.companionId) === value.companionId && value.attackEffectIds.includes(a.effectId),
  );
}

function contentPools(sheet: CharacterSheet | undefined, value: CompanionValue): ResourceValue[] {
  return (sheet?.resources ?? []).filter(
    (r) => r.content.contentId === value.content.contentId && (r.companionId ?? value.companionId) === value.companionId && value.resourceIds.includes(r.resourceId),
  );
}

/**
 * One companion: a named region. Its hit point controls follow the character's (same labels), held with aria-disabled and a linked reason.
 * A card with `onEdit` is a hand companion; a content companion is calculated and has no Edit or Remove.
 */
function CompanionCard({
  model,
  act,
  busy,
  editing,
  form,
  onEdit,
  onRemove,
  editId,
}: {
  model: CardModel;
  act: (action: PlayAction) => void;
  busy: boolean;
  editing: boolean;
  form?: ReactNode;
  onEdit?: () => void;
  onRemove?: () => void;
  editId?: string;
}) {
  const [amount, setAmount] = useState('');
  const { key, target } = model;
  const maximum = model.hitPointMaximum;
  const n = Number(amount);
  const entered = amount !== '' && Number.isInteger(n) && n >= 0 && n <= maxHitPoints;
  const unknown = maximum === null;
  const amountReason = unknown
    ? "This companion's hit point maximum could not be calculated."
    : amount === ''
      ? 'Enter an amount first.'
      : !entered
        ? `Enter a whole number from 0 to ${maxHitPoints}.`
        : '';
  const aboveMaximum = entered && maximum !== null && n > maximum;
  const setReason = aboveMaximum ? `Current hit points cannot be above the maximum (${maximum}).` : '';

  function apply(action: PlayAction) {
    act(action);
    setAmount('');
  }

  const heldButton = (label: string, held: boolean, reasonId: string | undefined, press: () => void) => (
    <button type="button" aria-label={`${label}: ${model.name}`} aria-disabled={held || undefined} aria-describedby={held ? reasonId : undefined} onClick={() => !held && press()}>
      {label}
    </button>
  );

  return (
    <section aria-labelledby={`${key}-heading`}>
      <h4 id={`${key}-heading`} tabIndex={-1}>
        {model.name}
      </h4>
      {editing ? (
        form
      ) : (
        <>
          {model.subtitle && <p className="hint">{model.subtitle}</p>}
          {(model.armorClass != null || model.speed) && (
            <p className="companion-stats">
              {model.armorClass != null && <span>{`Armor Class ${model.armorClass}`}</span>}
              {model.speed && <span>{`Speed ${model.speed}`}</span>}
            </p>
          )}
          {model.explain && model.armorClass != null && <ExplainList label={`${model.name} Armor Class`} steps={model.explain.armorClass} />}
          <p>{maximum === null ? 'Hit points: maximum unknown' : `Hit points: ${model.current} of ${maximum}${model.temporary > 0 ? `, ${model.temporary} temporary` : ''}`}</p>
          {model.explain && <ExplainList label={`${model.name} hit point maximum`} steps={model.explain.hitPoints} />}
          <form className="inline-form" onSubmit={(e: SubmitEvent<HTMLFormElement>) => e.preventDefault()}>
            <div className="field">
              <label htmlFor={`${key}-amount`}>Amount</label>
              <input
                id={`${key}-amount`}
                type="number"
                min={0}
                max={maxHitPoints}
                value={amount}
                aria-invalid={amount !== '' && !entered ? true : undefined}
                aria-describedby={amountReason ? `${key}-reason` : undefined}
                onChange={(e) => setAmount(e.target.value)}
              />
            </div>
            {heldButton('Take damage', !entered || unknown, `${key}-reason`, () => apply({ action: 'companionDamage', ...target, amount: n }))}
            {heldButton('Heal', !entered || unknown, `${key}-reason`, () => apply({ action: 'companionHeal', ...target, amount: n }))}
            {heldButton('Set hit points', !entered || unknown || aboveMaximum, aboveMaximum ? `${key}-set-reason` : `${key}-reason`, () =>
              apply({ action: 'companionSetHitPoints', ...target, amount: n }),
            )}
            {heldButton('Set temporary hit points', !entered || unknown, `${key}-reason`, () =>
              apply({ action: 'companionSetTemporaryHitPoints', ...target, amount: n }), // temporary only: the server keeps the stored current
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
          {model.extra}
          {onEdit && onRemove && (
            <div className="actions">
              <button id={editId} type="button" onClick={onEdit}>
                Edit {model.name}
              </button>
              <button type="button" aria-disabled={busy || undefined} aria-describedby={busy ? 'companion-busy' : undefined} onClick={() => !busy && onRemove()}>
                Remove {model.name}
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}
