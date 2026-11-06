import type { Effect } from '../api/types';
import { abilities } from '../classBasics';
import { FormulaField } from './AttackFields';
import { skillChoices } from './RollModeFields';
import { useReportedProblem } from './RollFields';
import type { ContentRevision } from '../api/types';

type RollFloorEffect = Extract<Effect, { type: 'rollFloor' }>;
type ReportProblem = (field: string, problem: string | undefined) => void;

const knownTargets = new Set<string>(['check', ...abilities.map(([k]) => `ability.${k}.check`), ...skillChoices.map(([k]) => `skill.${k}`)]);

/**
 * Content v12 (G9): a floor on check rolls. A d20 below `face`, or a total below `total`, counts as it. Exactly one of the two
 * is kept; switching drops the other. Members this editor doesn't know are left alone, and a stored value it can't offer is
 * shown and reported so Save and Publish are blocked until it is fixed.
 */
export function RollFloorFields(props: { effect: RollFloorEffect; previewRevision?: ContentRevision; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
  const { effect, onChange, onProblem } = props;
  const onTotal = effect.total !== undefined;
  const targetKnown = knownTargets.has(effect.target);
  const faceOk = onTotal || (Number.isInteger(effect.face) && effect.face! >= 2 && effect.face! <= 20);
  const totalOk = !onTotal || effect.total!.trim() !== '';
  const both = effect.face !== undefined && effect.total !== undefined;
  useReportedProblem(`${effect.id}-target`, targetKnown ? undefined : `Applies to: "${effect.target}" is not a check, ability check or skill.`, onProblem);
  useReportedProblem(`${effect.id}-face`, faceOk ? undefined : 'The d20 floor: use a whole number from 2 to 20.', onProblem);
  useReportedProblem(`${effect.id}-total`, totalOk ? undefined : 'The total floor: enter a formula.', onProblem);
  useReportedProblem(`${effect.id}-both`, both ? 'Floor on: use the d20 or the total, not both.' : undefined, onProblem);

  const setOn = (total: boolean) => {
    const next = { ...effect };
    if (total) {
      delete next.face;
      next.total = '';
    } else {
      delete next.total;
      next.face = 10;
    }
    onChange(next);
  };

  return (
    <>
      <label className="field">
        Applies to
        <select value={effect.target} onChange={(e) => onChange({ ...effect, target: e.target.value })}>
          {!targetKnown && <option value={effect.target}>{effect.target} (unknown)</option>}
          <option value="check">All ability checks</option>
          {abilities.map(([k, name]) => (
            <option key={k} value={`ability.${k}.check`}>
              {name} checks
            </option>
          ))}
          <optgroup label="Skills">
            {skillChoices.map(([k, name]) => (
              <option key={k} value={`skill.${k}`}>
                {name}
              </option>
            ))}
          </optgroup>
        </select>
      </label>
      <fieldset className="field">
        <legend>Floor on</legend>
        <label className="choice">
          <input type="radio" name={`${effect.id}-floor-on`} checked={!onTotal} onChange={() => setOn(false)} />
          The d20
        </label>
        <label className="choice">
          <input type="radio" name={`${effect.id}-floor-on`} checked={onTotal} onChange={() => setOn(true)} />
          The total
        </label>
      </fieldset>
      {onTotal ? (
        <FormulaField effectId={effect.id} part="total" label="At least" value={effect.total ?? ''} previewRevision={props.previewRevision} onChange={(total) => onChange({ ...effect, total })} />
      ) : (
        <label className="field">
          At least
          <input
            type="number"
            min={2}
            max={20}
            value={effect.face ?? ''}
            onChange={(e) => onChange({ ...effect, face: e.target.value === '' ? undefined : Number(e.target.value) })}
          />
        </label>
      )}
      <label className="choice">
        <input type="checkbox" checked={effect.proficient === true} onChange={(e) => onChange({ ...effect, proficient: e.target.checked ? true : undefined })} />
        Only when proficient
      </label>
    </>
  );
}
