import { useEffect, useRef, useState } from 'react';
import { abilities } from '../classBasics';
import type { Ability, ContentRevision, Effect } from '../api/types';
import { parseAreaSize, parseAreaWidth, parseMaxUnits, parsePerUnitDice, parseRollText, poolValue, type PoolChoice } from '../rollAuthoring';
import { ActivationSelect, FormulaField, StrictField } from './AttackFields';
import { PaymentFields } from './PaymentFields';
import { PoolSelect } from './ResponseFields';

type Roll = Extract<Effect, { type: 'roll' }>;
type Resource = Extract<Effect, { type: 'resource' }>;
type Area = NonNullable<Roll['area']>;
type ReportProblem = (field: string, problem: string | undefined) => void;

const shapes: { value: Area['shape']; label: string }[] = [
  { value: 'line', label: 'Line' },
  { value: 'cone', label: 'Cone' },
  { value: 'sphere', label: 'Sphere' },
  { value: 'cube', label: 'Cube' },
  { value: 'cylinder', label: 'Cylinder' },
  { value: 'emanation', label: 'Emanation' },
];

/** Reports a problem while it holds (also for a stored value, on load) and clears it when the field goes away. */
export function useReportedProblem(id: string, problem: string | undefined, onProblem: ReportProblem) {
  useEffect(() => {
    onProblem(id, problem);
    return () => onProblem(id, undefined);
  }, [id, problem, onProblem]);
}

/**
 * Extra dice for each point spent beyond the cost, and the most extra points. Both texts are kept here and read together: only a
 * valid pair is handed up, and a problem is shown next to its field and blocks saving.
 */
function PerUnitFields(props: { effect: Roll; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
  const { effect, onChange, onProblem } = props;
  const [diceText, setDiceText] = useState(effect.perUnit?.dice ?? '');
  const [maxText, setMaxText] = useState(effect.perUnit?.maxUnits === undefined ? '' : String(effect.perUnit.maxUnits));
  const dice = parsePerUnitDice(diceText);
  const max = parseMaxUnits(maxText);
  const needsDice = maxText.trim() !== '' && diceText === '';
  const diceLabel = 'Extra dice for each extra point (such as 1d6; empty for none)';
  const maxLabel = 'Most extra points (1 to 100; empty for no limit but the pool)';
  const diceProblem = dice.problem;
  const maxProblem = max.problem ?? (needsDice ? 'extra dice are needed first' : undefined);
  useReportedProblem(`${effect.id}-perunit-dice`, diceProblem && `${diceLabel}: ${diceProblem}.`, onProblem);
  useReportedProblem(`${effect.id}-perunit-max`, maxProblem && `${maxLabel}: ${maxProblem}.`, onProblem);
  const commit = (nextDice: string, nextMax: string) => {
    const d = parsePerUnitDice(nextDice);
    const m = parseMaxUnits(nextMax);
    if (d.problem || m.problem || (m.maxUnits !== undefined && !d.dice)) return;
    onChange({ ...effect, perUnit: d.dice ? { dice: d.dice, ...(m.maxUnits === undefined ? {} : { maxUnits: m.maxUnits }) } : undefined });
  };
  const field = (id: string, label: string, text: string, problem: string | undefined, set: (value: string) => void) => (
    <div className="field">
      <label>
        {label}
        <input
          value={text}
          aria-invalid={problem ? true : undefined}
          aria-describedby={problem ? `${id}-error` : undefined}
          onChange={(e) => set(e.target.value)}
        />
      </label>
      {problem && (
        <p id={`${id}-error`} className="error">
          {problem}
        </p>
      )}
    </div>
  );
  return (
    <>
      {field(`${effect.id}-perunit-dice`, diceLabel, diceText, diceProblem, (value) => {
        setDiceText(value);
        commit(value, maxText);
      })}
      {field(`${effect.id}-perunit-max`, maxLabel, maxText, maxProblem, (value) => {
        setMaxText(value);
        commit(diceText, value);
      })}
    </>
  );
}

/** The most pool changes a roll can also make (the service's MaxAlsoChanges). */
export const maxAlsoChanges = 4;

type AlsoChange = NonNullable<Roll['alsoChange']>[number];

/**
 * Content v11 (D43 S8): the other pools a roll changes in the same use. The roll's own pool is not offered (it is the cost). The
 * amounts are formulas the service checks; an empty list is saved as absent.
 */
function AlsoChangeFields(props: { effect: Roll; resources: Resource[]; otherPools: PoolChoice[]; previewRevision?: ContentRevision; onChange: (effect: Effect) => void }) {
  const { effect, onChange } = props;
  const rows = effect.alsoChange ?? [];
  const box = useRef<HTMLFieldSetElement>(null);
  const focusAfter = useRef<string | null>(null);
  // WCAG 2.4.3: Add and Remove rebuild the list, so focus is put back by hand once the new list is drawn.
  useEffect(() => {
    const key = focusAfter.current;
    if (key === null) return;
    focusAfter.current = null;
    const target = key === 'add' ? box.current?.querySelector<HTMLElement>('[data-focus="add"]') : box.current?.querySelector<HTMLElement>(`[data-row="${key}"] select`);
    target?.focus();
  });
  const isOwn = (contentId: string | undefined, resourceId: string) => resourceId === effect.resourceId && contentId === effect.resourceContent;
  const resources = props.resources.filter((r) => !isOwn(undefined, r.resourceId));
  const otherPools = props.otherPools.filter((p) => !isOwn(p.contentId, p.resourceId));
  const first = resources[0] ? { resourceId: resources[0].resourceId } : otherPools[0] ? { resourceId: otherPools[0].resourceId, resourceContent: otherPools[0].contentId } : undefined;
  const atLimit = rows.length >= maxAlsoChanges;
  const cannotAdd = atLimit || first === undefined;
  const hintId = `rule-${effect.id}-alsochange-limit`;
  const set = (next: AlsoChange[]) => onChange({ ...effect, alsoChange: next.length === 0 ? undefined : next });
  const replaceAt = (at: number, row: AlsoChange) => set(rows.map((r, i) => (i === at ? row : r)));
  const add = () => {
    if (cannotAdd || !first) return;
    focusAfter.current = String(rows.length);
    set([...rows, { ...first, change: 'spend', amount: '1' }]);
  };
  const remove = (at: number) => {
    focusAfter.current = at < rows.length - 1 ? String(at) : 'add';
    set(rows.filter((_, i) => i !== at));
  };
  return (
    <fieldset className="bare-fieldset" ref={box}>
      <legend>Also changes</legend>
      {rows.length > 0 && (
        <ol>
          {rows.map((row, i) => {
            const n = i + 1;
            return (
              <li key={i} data-row={i}>
                <PoolSelect
                  label={`Change ${n}: pool`}
                  noneLabel="Choose a pool"
                  current={{ contentId: row.resourceContent, resourceId: row.resourceId }}
                  resources={resources}
                  otherPools={otherPools}
                  onPick={(pool) => replaceAt(i, { ...row, resourceId: pool?.resourceId ?? '', resourceContent: pool?.contentId })}
                />
                <label className="field">
                  {`Change ${n}: spend or regain`}
                  <select value={row.change} onChange={(e) => replaceAt(i, { ...row, change: e.target.value === 'regain' ? 'regain' : 'spend' })}>
                    <option value="spend">Spend</option>
                    <option value="regain">Regain</option>
                  </select>
                </label>
                <FormulaField
                  effectId={effect.id}
                  part={`alsochange${n}`}
                  label={`Change ${n}: amount (a number or a formula)`}
                  value={row.amount}
                  previewRevision={props.previewRevision}
                  floorAtZero
                  onChange={(amount) => replaceAt(i, { ...row, amount })}
                />
                <button type="button" onClick={() => remove(i)}>
                  {`Remove change ${n}`}
                </button>
              </li>
            );
          })}
        </ol>
      )}
      <button type="button" data-focus="add" aria-disabled={cannotAdd || undefined} aria-describedby={cannotAdd ? hintId : undefined} onClick={add}>
        Add change
      </button>
      {cannotAdd && (
        <p id={hintId} className="hint">
          {atLimit ? `A roll also changes at most ${maxAlsoChanges} pools; remove one to add another.` : 'There is no other pool to change.'}
        </p>
      )}
    </fieldset>
  );
}

/** Content v11 (D43 S8): a roll that draws down the built-up charge of a pool that counts up. */
function DrawsDownField(props: { effect: Roll; countsUp: boolean | undefined; poolKnown: boolean; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
  const { effect } = props;
  const ticked = effect.drawsDown === true;
  const wrong = ticked && props.poolKnown && props.countsUp !== true;
  const reasonId = `rule-${effect.id}-draws-down-reason`;
  const label = 'Draws down built-up charge';
  useReportedProblem(`${effect.id}-draws-down`, wrong ? `${label}: only for a pool that counts up.` : undefined, props.onProblem);
  return (
    <>
      <label className="choice">
        <input
          type="checkbox"
          checked={ticked}
          // A stored tick on a pool that does not count up stays enabled, so it can be cleared.
          disabled={props.countsUp !== true && !ticked}
          aria-invalid={wrong ? true : undefined}
          aria-describedby={props.countsUp === true ? undefined : reasonId}
          onChange={(e) => props.onChange({ ...effect, drawsDown: e.target.checked ? true : undefined })}
        />
        {`${label} (the roll uses what has built up)`}
      </label>
      {props.countsUp !== true && (
        <p id={reasonId} className={wrong ? 'error' : 'hint'}>
          Only for a pool that counts up
        </p>
      )}
    </>
  );
}

/** A spell a roll can cast without a slot: its content id (castsSpell is a bare id), name and level. */
export interface SpellChoice {
  contentId: string;
  name: string;
  level?: number;
}

/**
 * Content v12 (R38): the spell a roll casts without a slot. Disabled, with the reason beside it, while the roll pays a slot (the
 * validator refuses both). A stored spell that is not listed is kept as it is, never silently moved.
 */
function CastsSpellField(props: { effect: Roll; spells: SpellChoice[] | undefined; onChange: (effect: Effect) => void }) {
  const { effect } = props;
  const spells = props.spells ?? [];
  const reasonId = `rule-${effect.id}-casts-reason`;
  const slot = effect.payment?.kind === 'slot';
  const listed = effect.castsSpell === undefined || spells.some((s) => s.contentId === effect.castsSpell);
  return (
    <>
      <label className="field">
        Casts a spell (without spending a slot)
        <select
          value={effect.castsSpell ?? ''}
          disabled={slot}
          aria-describedby={slot ? reasonId : undefined}
          onChange={(e) => props.onChange({ ...effect, castsSpell: e.target.value || undefined })}
        >
          <option value="">No spell</option>
          {spells.map((s) => (
            <option key={s.contentId} value={s.contentId}>
              {s.level === undefined ? s.name : `${s.name} (${s.level === 0 ? 'cantrip' : `level ${s.level}`})`}
            </option>
          ))}
          {!listed && effect.castsSpell && <option value={effect.castsSpell}>{effect.castsSpell} (other content)</option>}
        </select>
      </label>
      {slot && (
        <p id={reasonId} className="hint">
          A roll that pays a spell slot cannot also cast a spell without one.
        </p>
      )}
    </>
  );
}

/** Content v11 (D43 slice 5): the editor of a roll or action; every v6 and v11 field. */
export function RollFields(props: {
  effect: Roll;
  resources: Resource[];
  otherPools: PoolChoice[];
  /** The spells a roll can cast without a slot (R38): the published spells of the source. */
  spells?: SpellChoice[];
  previewRevision?: ContentRevision;
  onChange: (effect: Effect) => void;
  onProblem: ReportProblem;
}) {
  const { effect, onChange, onProblem } = props;
  const { area, save } = effect;
  const poolKnown = effect.resourceId
    ? effect.resourceContent
      ? props.otherPools.some((p) => p.contentId === effect.resourceContent && p.resourceId === effect.resourceId)
      : props.resources.some((r) => r.resourceId === effect.resourceId)
    : true;
  const poolSelected = effect.resourceId ? poolValue(effect.resourceContent, effect.resourceId) : '';
  // Whether the chosen pool counts up (a pool that is in neither list is not known).
  const countsUp = effect.resourceContent
    ? props.otherPools.find((p) => p.contentId === effect.resourceContent && p.resourceId === effect.resourceId)?.countsUp === true
    : props.resources.some((r) => r.resourceId === effect.resourceId && r.counts === 'up');
  const poolPays = effect.payment === undefined || effect.payment.kind === 'resource';
  const isCompound = (e: Roll) => e.drawsDown === true || (e.alsoChange?.length ?? 0) > 0;
  const variableAllowed = !isCompound(effect);
  const onCharging = (next: Effect) => onChange(isCompound(next as Roll) ? { ...(next as Roll), variableCost: undefined } : next);
  return (
    <>
      <label className="field">
        Dice (such as 1d8 + 2; empty for an action that rolls nothing)
        <input value={effect.dice ?? ''} onChange={(e) => onChange(e.target.value ? { ...effect, dice: e.target.value } : { ...effect, dice: undefined, perUnit: undefined })}
        />
      </label>
      <FormulaField
        effectId={effect.id}
        part="bonus"
        label="Bonus added to the dice (a number or a formula; empty adds nothing)"
        value={effect.bonus ?? ''}
        previewRevision={props.previewRevision}
        onChange={(value) => onChange({ ...effect, bonus: value || undefined })}
      />
      <ActivationSelect value={effect.activation} unset="other" onPick={(activation) => onChange({ ...effect, activation })} />
      <PaymentFields
        effect={effect}
        onChange={onChange}
        onProblem={onProblem}
        slotRefusal={effect.castsSpell ? 'A roll that casts a spell without a slot cannot also pay a slot.' : undefined}
      />
      <CastsSpellField effect={effect} spells={props.spells} onChange={onChange} />
      {effect.payment?.kind === 'hitPoints' && (
        <>
          <FormulaField
            effectId={effect.id}
            part="cost"
            label="Hit points paid (a number or a formula)"
            value={effect.cost ?? ''}
            previewRevision={props.previewRevision}
            floorAtZero
            onChange={(value) => onChange({ ...effect, cost: value || undefined })}
          />
          <PerUnitFields key={effect.dice ? 'dice' : 'no-dice'} effect={effect} onChange={onChange} onProblem={onProblem} />
        </>
      )}
      {/* Hit points and a slot take no pool (R33), so the pool choice is hidden while either is chosen. */}
      {poolPays && (
      <>
      <label className="field">
        Uses a resource (a limited-use action)
        <select
          value={poolSelected}
          onChange={(e) => {
            const picked = e.target.value;
            // Another pool starts the charging members over: they were chosen against the old pool.
            const charging = { alsoChange: undefined, drawsDown: undefined };
            if (!picked) return onChange({ ...effect, resourceId: undefined, resourceContent: undefined, cost: undefined, variableCost: undefined, perUnit: undefined, ...charging });
            const other = props.otherPools.find((p) => poolValue(p.contentId, p.resourceId) === picked);
            if (other) return onChange({ ...effect, resourceId: other.resourceId, resourceContent: other.contentId, ...charging });
            if (props.resources.some((r) => r.resourceId === picked)) onChange({ ...effect, resourceId: picked, resourceContent: undefined, ...charging });
          }}
        >
          <option value="">No resource</option>
          {props.resources.map((r) => (
            <option key={r.id} value={r.resourceId}>
              {r.label || r.resourceId}
            </option>
          ))}
          {props.otherPools.map((p) => (
            <option key={poolValue(p.contentId, p.resourceId)} value={poolValue(p.contentId, p.resourceId)}>
              {p.label || p.resourceId} ({p.entryName})
            </option>
          ))}
          {/* A pool that is in neither list is kept as it is, not silently moved. */}
          {!poolKnown && effect.resourceId && <option value={poolSelected}>{effect.resourceId} (other content)</option>}
        </select>
      </label>
      {effect.resourceId && (
        <>
          <FormulaField
            effectId={effect.id}
            part="cost"
            label="Cost (a number or a formula; empty is one use)"
            value={effect.cost ?? ''}
            previewRevision={props.previewRevision}
            floorAtZero
            onChange={(value) => onChange({ ...effect, cost: value || undefined })}
          />
          {/* A compound use charges the calculated cost, so a chosen amount would be ignored: the choice is hidden while either is
              set (a stored one stays visible so it can be cleared), and setting either clears it. */}
          {(variableAllowed || effect.variableCost === true) && (
            <label className="choice">
              <input
                type="checkbox"
                checked={effect.variableCost === true}
                onChange={(e) => onChange(e.target.checked ? { ...effect, variableCost: true, perUnit: undefined } : { ...effect, variableCost: undefined })}
              />
              The player chooses how much to spend (up to the cost)
            </label>
          )}
          {/* Keyed on the dice being there, so its texts start over when clearing the dice removes the per-unit dice. */}
          {effect.variableCost !== true && <PerUnitFields key={effect.dice ? 'dice' : 'no-dice'} effect={effect} onChange={onChange} onProblem={onProblem} />}
          <AlsoChangeFields effect={effect} resources={props.resources} otherPools={props.otherPools} previewRevision={props.previewRevision} onChange={onCharging} />
          <DrawsDownField effect={effect} countsUp={countsUp} poolKnown={poolKnown} onChange={onCharging} onProblem={onProblem} />
        </>
      )}
      </>
      )}
      <label className="field">
        Saving throw
        <select
          value={save?.ability ?? ''}
          onChange={(e) => onChange({ ...effect, save: e.target.value ? { ability: e.target.value as Ability, dc: save?.dc ?? '8 + PB' } : undefined })}
        >
          <option value="">No saving throw</option>
          {abilities.map(([key, name]) => (
            <option key={key} value={key}>
              {name}
            </option>
          ))}
        </select>
      </label>
      {save && (
        <FormulaField
          effectId={effect.id}
          part="savedc"
          label="Save DC (a formula, such as 8 + PB + WIS.MOD)"
          value={save.dc}
          previewRevision={props.previewRevision}
          onChange={(dc) => onChange({ ...effect, save: { ...save, dc } })}
        />
      )}
      <label className="field">
        Area shape
        <select
          value={area?.shape ?? ''}
          onChange={(e) => {
            const shape = e.target.value as Area['shape'] | '';
            if (!shape) return onChange({ ...effect, area: undefined });
            // A width means something for a line only; leaving the line drops it.
            onChange({ ...effect, area: { shape, size: area?.size ?? 30, ...(shape === 'line' && area?.width !== undefined ? { width: area.width } : {}) } });
          }}
        >
          <option value="">No area</option>
          {shapes.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </select>
      </label>
      {area && (
        <StrictField
          id={`${effect.id}-area-size`}
          label="Area size in feet (1 to 1000)"
          initial={String(area.size)}
          parse={(text) => {
            const parsed = parseAreaSize(text);
            return { value: parsed.size, problem: parsed.problem };
          }}
          onValue={(size) => size !== undefined && onChange({ ...effect, area: { ...area, size } })}
          onProblem={onProblem}
        />
      )}
      {area?.shape === 'line' && (
        <StrictField
          id={`${effect.id}-area-width`}
          label="Line width in feet (1 to 1000; empty for none)"
          initial={area.width === undefined ? '' : String(area.width)}
          parse={(text) => {
            const parsed = parseAreaWidth(text);
            return { value: parsed.width, problem: parsed.problem };
          }}
          onValue={(width) => onChange({ ...effect, area: width === undefined ? { shape: area.shape, size: area.size } : { shape: area.shape, size: area.size, width } })}
          onProblem={onProblem}
        />
      )}
      <StrictField
        id={`${effect.id}-range`}
        label="Range (such as Self or 60 ft; empty for none)"
        initial={effect.range ?? ''}
        parse={(text) => {
          const parsed = parseRollText(text);
          return { value: parsed.text, problem: parsed.problem };
        }}
        onValue={(range) => onChange({ ...effect, range })}
        onProblem={onProblem}
      />
      <StrictField
        id={`${effect.id}-targets`}
        label="Targets (who it affects, such as Each creature in the area; empty for none)"
        initial={effect.targets ?? ''}
        parse={(text) => {
          const parsed = parseRollText(text);
          return { value: parsed.text, problem: parsed.problem };
        }}
        onValue={(targets) => onChange({ ...effect, targets })}
        onProblem={onProblem}
      />
    </>
  );
}
