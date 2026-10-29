import type { Effect } from '../api/types';
import { abilities } from '../classBasics';
import { ToggleSelect, type ToggleChoices } from './AttackFields';
import { damageTypeLabel, damageTypes } from './customModel';
import { useReportedProblem } from './RollFields';

type RollModeEffect = Extract<Effect, { type: 'rollMode' }>;
type Grant = Extract<Effect, { type: 'grant' }>;
type ReportProblem = (field: string, problem: string | undefined) => void;

/** The skills of the rules (the keys the service reads as skill.<key>) with their labels. */
export const skillChoices: [string, string][] = [
  ['acrobatics', 'Acrobatics'],
  ['animalHandling', 'Animal Handling'],
  ['arcana', 'Arcana'],
  ['athletics', 'Athletics'],
  ['deception', 'Deception'],
  ['history', 'History'],
  ['insight', 'Insight'],
  ['intimidation', 'Intimidation'],
  ['investigation', 'Investigation'],
  ['medicine', 'Medicine'],
  ['nature', 'Nature'],
  ['perception', 'Perception'],
  ['performance', 'Performance'],
  ['persuasion', 'Persuasion'],
  ['religion', 'Religion'],
  ['sleightOfHand', 'Sleight of Hand'],
  ['stealth', 'Stealth'],
  ['survival', 'Survival'],
];

/** The condition keys a roll mode can be limited to (the service's ConditionKeys.All). */
export const conditionKeys = [
  'blinded',
  'charmed',
  'deafened',
  'frightened',
  'grappled',
  'incapacitated',
  'invisible',
  'paralyzed',
  'petrified',
  'poisoned',
  'prone',
  'restrained',
  'stunned',
  'unconscious',
];

const label = (key: string) => key.charAt(0).toUpperCase() + key.slice(1);

const knownTargets = new Set<string>([
  ...abilities.map(([k]) => `save.${k}`),
  ...skillChoices.map(([k]) => `skill.${k}`),
  ...abilities.map(([k]) => `ability.${k}.check`),
]);

/**
 * Content v11 (D43, slice 6): automatic advantage or disadvantage on a save, a skill check or an ability check, optionally
 * only against some conditions and only while a form is on. A stored value this editor cannot offer (an unknown target, a
 * mode other than the two, a condition key that is not one of the fourteen) is shown and reported, so Save and Publish are
 * blocked until it is fixed.
 */
export function RollModeFields(props: { effect: RollModeEffect; toggles: ToggleChoices; showToggle: boolean; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
  const { effect, onChange, onProblem } = props;
  const against = effect.against ?? [];
  const unknownKeys = against.filter((k) => !conditionKeys.includes(k));
  const targetKnown = knownTargets.has(effect.target);
  const modeKnown = effect.mode === 'advantage' || effect.mode === 'disadvantage';
  useReportedProblem(`${effect.id}-target`, targetKnown ? undefined : `Applies to: "${effect.target}" is not a saving throw, skill or ability check.`, onProblem);
  useReportedProblem(`${effect.id}-mode`, modeKnown ? undefined : `Mode: "${String(effect.mode)}" is not advantage or disadvantage.`, onProblem);
  useReportedProblem(`${effect.id}-against`, unknownKeys.length === 0 ? undefined : `Only against conditions: ${unknownKeys.join(', ')} ${unknownKeys.length === 1 ? 'is' : 'are'} not a known condition.`, onProblem);

  const setAgainst = (next: string[]) => {
    // Never an empty list: none checked means always (the key is dropped when the revision is sent).
    onChange({ ...effect, against: next.length === 0 ? undefined : next });
  };

  return (
    <>
      <label className="field">
        Applies to
        <select value={effect.target} onChange={(e) => onChange({ ...effect, target: e.target.value })}>
          {!targetKnown && <option value={effect.target}>{effect.target} (unknown)</option>}
          <optgroup label="Saving throws">
            {abilities.map(([k, name]) => (
              <option key={k} value={`save.${k}`}>
                {name} saving throw
              </option>
            ))}
          </optgroup>
          <optgroup label="Skills">
            {skillChoices.map(([k, name]) => (
              <option key={k} value={`skill.${k}`}>
                {name}
              </option>
            ))}
          </optgroup>
          <optgroup label="Ability checks">
            {abilities.map(([k, name]) => (
              <option key={k} value={`ability.${k}.check`}>
                {name} check
              </option>
            ))}
          </optgroup>
        </select>
      </label>
      <label className="field">
        Mode
        <select value={effect.mode} onChange={(e) => onChange({ ...effect, mode: e.target.value as RollModeEffect['mode'] })}>
          {!modeKnown && <option value={effect.mode}>{String(effect.mode)} (unknown)</option>}
          <option value="advantage">Advantage</option>
          <option value="disadvantage">Disadvantage</option>
        </select>
      </label>
      <fieldset className="field">
        <legend>Only against conditions (none checked: always)</legend>
        {[...conditionKeys, ...unknownKeys].map((key) => (
          <label key={key} className="choice">
            <input type="checkbox" checked={against.includes(key)} onChange={(e) => setAgainst(e.target.checked ? [...against, key] : against.filter((k) => k !== key))} />
            {conditionKeys.includes(key) ? label(key) : key}
          </label>
        ))}
      </fieldset>
      {(effect.toggle || props.showToggle) && (
        <ToggleSelect
          label="Only while"
          alwaysLabel="Always"
          choices={props.toggles}
          current={{ contentId: effect.toggleContent, toggleId: effect.toggle }}
          onPick={(toggle) => onChange({ ...effect, toggle: toggle?.toggleId, toggleContent: toggle?.contentId })}
        />
      )}
    </>
  );
}

const defenceKinds: { value: Grant['grant']; label: string }[] = [
  { value: 'damageResistance', label: 'Resistance' },
  { value: 'damageImmunity', label: 'Immunity' },
  { value: 'damageVulnerability', label: 'Vulnerability' },
];

/** Whether a grant is one of the three damage defences, which this minimal editor can edit. */
export const isDamageDefence = (effect: Effect): effect is Grant => effect.type === 'grant' && defenceKinds.some((k) => k.value === effect.grant);

/** Content v11 (D43, slice 6): a damage defence, optionally only while a form is on (`whileToggle`). */
export function DefenceFields(props: { effect: Grant; toggles: ToggleChoices; showToggle: boolean; onChange: (effect: Effect) => void }) {
  const { effect, onChange } = props;
  const typeKnown = damageTypes.some((t) => t === effect.damageType);
  return (
    <>
      <label className="field">
        Kind
        <select value={effect.grant} onChange={(e) => onChange({ ...effect, grant: e.target.value as Grant['grant'] })}>
          {defenceKinds.map((k) => (
            <option key={k.value} value={k.value}>
              {k.label}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        Damage type
        <select value={effect.damageType ?? ''} onChange={(e) => onChange({ ...effect, damageType: e.target.value })}>
          {!typeKnown && <option value={effect.damageType ?? ''}>{effect.damageType || 'Choose a damage type'}</option>}
          {damageTypes.map((t) => (
            <option key={t} value={t}>
              {damageTypeLabel(t)}
            </option>
          ))}
        </select>
      </label>
      {(effect.whileToggle || props.showToggle) && (
        <ToggleSelect
          label="Only while"
          alwaysLabel="Always"
          choices={props.toggles}
          current={effect.whileToggle}
          onPick={(toggle) => onChange({ ...effect, whileToggle: toggle })}
        />
      )}
    </>
  );
}
