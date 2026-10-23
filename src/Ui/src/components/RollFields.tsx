import { useEffect, useState } from 'react';
import { abilities } from '../classBasics';
import type { Ability, ContentRevision, Effect } from '../api/types';
import { parseAreaSize, parseAreaWidth, parseMaxUnits, parsePerUnitDice, parseRollText, poolValue, type PoolChoice } from '../rollAuthoring';
import { ActivationSelect, FormulaField, StrictField } from './AttackFields';

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
function useReportedProblem(id: string, problem: string | undefined, onProblem: ReportProblem) {
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

/** Content v11 (D43 slice 5): the editor of a roll or action; every v6 and v11 field. */
export function RollFields(props: {
  effect: Roll;
  resources: Resource[];
  otherPools: PoolChoice[];
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
      <label className="field">
        Uses a resource (a limited-use action)
        <select
          value={poolSelected}
          onChange={(e) => {
            const picked = e.target.value;
            if (!picked) return onChange({ ...effect, resourceId: undefined, resourceContent: undefined, cost: undefined, variableCost: undefined, perUnit: undefined });
            const other = props.otherPools.find((p) => poolValue(p.contentId, p.resourceId) === picked);
            if (other) return onChange({ ...effect, resourceId: other.resourceId, resourceContent: other.contentId });
            if (props.resources.some((r) => r.resourceId === picked)) onChange({ ...effect, resourceId: picked, resourceContent: undefined });
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
          <label className="choice">
            <input
              type="checkbox"
              checked={effect.variableCost === true}
              onChange={(e) => onChange(e.target.checked ? { ...effect, variableCost: true, perUnit: undefined } : { ...effect, variableCost: undefined })}
            />
            The player chooses how much to spend (up to the cost)
          </label>
          {/* Keyed on the dice being there, so its texts start over when clearing the dice removes the per-unit dice. */}
          {effect.variableCost !== true && <PerUnitFields key={effect.dice ? 'dice' : 'no-dice'} effect={effect} onChange={onChange} onProblem={onProblem} />}
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
