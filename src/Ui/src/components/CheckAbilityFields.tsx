import type { Ability, Effect } from '../api/types';
import { abilities } from '../classBasics';
import { skillChoices } from './RollModeFields';
import { useReportedProblem } from './RollFields';

type CheckAbilityEffect = Extract<Effect, { type: 'checkAbility' }>;
type ReportProblem = (field: string, problem: string | undefined) => void;

/** The ability each skill is printed with (the service's skill table). */
const printedAbility: Record<string, Ability> = {
  acrobatics: 'dex',
  animalHandling: 'wis',
  arcana: 'int',
  athletics: 'str',
  deception: 'cha',
  history: 'int',
  insight: 'wis',
  intimidation: 'cha',
  investigation: 'int',
  medicine: 'wis',
  nature: 'int',
  perception: 'wis',
  performance: 'cha',
  persuasion: 'cha',
  religion: 'int',
  sleightOfHand: 'dex',
  stealth: 'dex',
  survival: 'wis',
};

/** Constitution has no skill, so "every Constitution skill" is not offered (the service refuses ability.con.check). */
const skillAbilities = abilities.filter(([k]) => k !== 'con');

const knownTargets = new Set<string>([...skillAbilities.map(([k]) => `ability.${k}.check`), ...skillChoices.map(([k]) => `skill.${k}`)]);

/** The ability the target is printed with: a skill's own, or the one an "every X skill" target names. */
function printedFor(target: string): string | undefined {
  if (target.startsWith('skill.')) return printedAbility[target.slice('skill.'.length)];
  const m = /^ability\.([a-z]+)\.check$/.exec(target);
  return m?.[1];
}

/**
 * Content v12 (G10): a skill may use another ability. The skill takes the ability whose checks have the better roll mode
 * (advantage, then normal, then disadvantage), then the higher modifier. Members this editor doesn't know are left alone, and a stored value it can't offer is shown and
 * reported so Save and Publish are blocked until it is fixed.
 */
export function CheckAbilityFields(props: { effect: CheckAbilityEffect; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
  const { effect, onChange, onProblem } = props;
  const targetKnown = knownTargets.has(effect.target);
  const abilityKnown = abilities.some(([k]) => k === effect.ability);
  const printed = printedFor(effect.target);
  const same = abilityKnown && printed !== undefined && printed === effect.ability;
  useReportedProblem(`${effect.id}-target`, targetKnown ? undefined : `Skill: "${effect.target}" is not a skill or an ability's skills.`, onProblem);
  useReportedProblem(`${effect.id}-ability`, abilityKnown ? undefined : `Uses instead: "${effect.ability}" is not an ability.`, onProblem);
  useReportedProblem(`${effect.id}-same`, same ? 'Uses instead: pick an ability other than the one the skill already uses.' : undefined, onProblem);

  return (
    <>
      <label className="field">
        Skill
        <select value={effect.target} onChange={(e) => onChange({ ...effect, target: e.target.value })}>
          {!targetKnown && <option value={effect.target}>{effect.target} (unknown)</option>}
          {skillAbilities.map(([k, name]) => (
            <option key={k} value={`ability.${k}.check`}>
              Every {name} skill
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
      <label className="field">
        Uses instead
        <select value={effect.ability} onChange={(e) => onChange({ ...effect, ability: e.target.value as Ability })}>
          {!abilityKnown && <option value={effect.ability}>{effect.ability} (unknown)</option>}
          {abilities.map(([k, name]) => (
            <option key={k} value={k} disabled={k === printed}>
              {name}
            </option>
          ))}
        </select>
      </label>
    </>
  );
}
