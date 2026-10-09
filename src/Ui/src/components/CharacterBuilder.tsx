import { useCallback, useEffect, useRef, useState, type Dispatch, type ReactNode, type SetStateAction, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type {
  Ability,
  AbilityScores,
  Campaign,
  CampaignException,
  Character,
  CharacterView,
  ChoiceStatus,
  ClassLevel,
  ContentKind,
  ContentOption,
  ContentReference,
  KnownSpell,
  RulesFamilyId,
  RollRecord,
  RulesFamilyPolicy,
  SpellcastingEntry,
} from '../api/types';
import {
  abilityKeys,
  assignmentComplete,
  assignmentValid,
  pointBuyBudget,
  pointBuyCost,
  pointBuyRange,
  scoreMethods,
  scoresFrom,
  standardArray,
  type Assignment,
  type ScoreMethod,
} from '../abilityScores';
import { matchesSpell } from '../spellSearch';
import { SpellSearch } from './sheet/SpellSearch';

const abilities: { key: Ability; label: string }[] = [
  { key: 'str', label: 'Strength' },
  { key: 'dex', label: 'Dexterity' },
  { key: 'con', label: 'Constitution' },
  { key: 'int', label: 'Intelligence' },
  { key: 'wis', label: 'Wisdom' },
  { key: 'cha', label: 'Charisma' },
];

const sameRef = (a: ContentReference, b: ContentReference) => a.contentId === b.contentId && a.revisionId === b.revisionId;

const maxLevel = 20;

/**
 * SPEC C-07: every builder flow edits a draft. The service previews it (`character.preview`, `character.previewChoice`)
 * and nothing is stored until the final button; Cancel discards the draft.
 */
export type BuilderMode =
  | { kind: 'create' }
  | { kind: 'levelUp'; view: CharacterView }
  | { kind: 'choices'; view: CharacterView };

type Step = 'rules' | 'scores' | 'species' | 'class' | 'background' | 'choices' | 'level';

/** D32: a new character is built in these six steps; level-up and answering choices use `level` and `choices` alone. */
const createSteps: Step[] = ['rules', 'scores', 'species', 'class', 'background', 'choices'];
const stepTitles: Record<Step, string> = {
  rules: 'Rules',
  scores: 'Ability scores',
  species: 'Species',
  class: 'Class',
  background: 'Background',
  choices: 'Choices and create',
  level: 'Level',
};

interface Basics {
  name: string;
  rulesFamily: RulesFamilyId;
  scores: AbilityScores;
  scoreMethod: ScoreMethod;
  /** The standard array or rolled scores as the player has assigned them so far (ability to value; a partial assignment is allowed). */
  assignment: Assignment;
  rolled: RollRecord[];
  species?: ContentReference;
  background?: ContentReference;
  startingClass?: ContentReference;
  other: ContentReference[];
  campaignId?: string;
}

interface Props {
  mode: BuilderMode;
  rulesFamilies: RulesFamilyPolicy[];
  /** Called after a successful save; it may return a promise (the app refreshing its list), which keeps the builder busy until it settles. */
  onCommitted: (view: CharacterView) => void | Promise<void>;
  onCancel: () => void;
  onError: (error: unknown) => void;
}

/**
 * An option as the builder shows it: outside the campaign counts as unavailable until the player gives a reason.
 * `familyOk` is the server's family fit before that overlay, so the other family stays out whatever the campaign says.
 */
type Shown = ContentOption & { outsideCampaign?: boolean; familyOk: boolean };

function sourceLine(option: Shown) {
  return (
    <span className="option-source">
      {option.sourceTitle}
      {option.page ? `, ${option.page}` : ''} · {option.rulesFamilies.join(', ')}
      {option.outsideCampaign ? ' · not allowed in this campaign' : !option.compatible && ' · not available for this rules family'}
    </span>
  );
}

/**
 * D31 (owner, 2026-10-07): content of the other rules family is never offered, inside a campaign or out (R32). Of the
 * character's own family, only campaign-outside content is shown disabled with its reason (P-01).
 */
const visible = (option: Shown) => option.familyOk && (option.compatible || option.outsideCampaign === true);

/** One origin or class pick: a radio group with "None", so the choice is explicit and keyboard-operable. */
function SinglePick(props: {
  legend: string;
  name: string;
  options: Shown[];
  value?: ContentReference;
  onChange: (value?: ContentReference) => void;
}) {
  return (
    <fieldset>
      <legend>{props.legend}</legend>
      <label className="choice">
        <input type="radio" name={props.name} checked={!props.value} onChange={() => props.onChange(undefined)} />
        None
      </label>
      {props.options.map((option) => (
        <label key={option.reference.revisionId} className={`choice${option.compatible ? '' : ' incompatible'}`}>
          <input
            type="radio"
            name={props.name}
            aria-disabled={!option.compatible || undefined}
            checked={!!props.value && sameRef(props.value, option.reference)}
            onChange={() => {
              if (option.compatible) props.onChange(option.reference); // inert, not disabled: a reason typed elsewhere can flip this under focus (2.4.3)
            }}
          />
          <span className="option-name">{option.name}</span> {sourceLine(option)}
        </label>
      ))}
    </fieldset>
  );
}

/**
 * R41: a pick that needs a campaign exception reason keeps its place when the reason goes; progress waits instead, with this
 * sentence linked to the button, until the player gives a reason again or unpicks it.
 */
function reasonHint(names: string[]): string | undefined {
  if (names.length === 0) return undefined;
  return names.length === 1 ? `${names[0]} needs a campaign exception reason.` : `${names.join(', ')} need a campaign exception reason.`;
}

/** D32: one named form per create step, so a screen reader announces where the player is. The hint, when shown, describes Next. */
function StepForm(props: {
  label: string;
  next: string;
  nextDisabled?: boolean;
  nextHint?: string;
  onNext: () => void;
  onBack?: () => void;
  onCancel: () => void;
  children: ReactNode;
}) {
  // aria-disabled, never `disabled`: Next can become inactive (a roll lands, a field is cleared) while it has focus (2.4.3).
  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!props.nextDisabled) props.onNext();
  }
  return (
    <form onSubmit={submit} aria-label={props.label}>
      {props.children}
      <div className="actions">
        {props.onBack && (
          <button type="button" onClick={props.onBack}>
            Back
          </button>
        )}
        <button type="submit" aria-disabled={props.nextDisabled || undefined} aria-describedby={props.nextHint ? 'next-hint' : undefined}>
          {props.next}
        </button>
        <button type="button" onClick={props.onCancel}>
          Cancel
        </button>
      </div>
      {props.nextHint && (
        <p id="next-hint" className="hint">
          {props.nextHint}
        </p>
      )}
    </form>
  );
}

function RulesStep(props: {
  basics: Basics;
  rulesFamilies: RulesFamilyPolicy[];
  campaigns: Campaign[];
  onChange: (basics: Basics) => void;
  onNext: () => void;
  onCancel: () => void;
}) {
  const { basics, onChange } = props;
  const policy = props.rulesFamilies.find((f) => f.id === basics.rulesFamily);

  return (
    <StepForm
      label="Rules"
      next="Next: ability scores"
      nextDisabled={!basics.name.trim()}
      nextHint={basics.name.trim() ? undefined : 'Enter a name to continue.'}
      onNext={props.onNext}
      onCancel={props.onCancel}
    >
      <label className="field">
        Name
        <input required value={basics.name} onChange={(e) => onChange({ ...basics, name: e.target.value })} autoFocus />
      </label>

      {props.campaigns.length > 0 && (
        <label className="field">
          Campaign
          <select
            value={basics.campaignId ?? ''}
            onChange={(e) => {
              const campaign = props.campaigns.find((c) => c.id === e.target.value);
              // A campaign names its rules family; the character follows it (it can still be changed below, with a warning).
              onChange({ ...basics, campaignId: campaign?.id, rulesFamily: campaign?.rulesFamily ?? basics.rulesFamily });
            }}
          >
            <option value="">No campaign</option>
            {props.campaigns.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name} ({c.rulesFamily})
              </option>
            ))}
          </select>
        </label>
      )}

      <fieldset>
        <legend>Rules family</legend>
        {props.rulesFamilies.map((family) => (
          <label key={family.id} className="choice">
            <input
              type="radio"
              name="rulesFamily"
              value={family.id}
              checked={basics.rulesFamily === family.id}
              onChange={() => onChange({ ...basics, rulesFamily: family.id })}
            />
            {family.displayName} <code>{family.id}</code>
          </label>
        ))}
        {policy && (
          <p className="hint">
            Ability score increases under this family come from <strong>{policy.abilityIncreaseSource}</strong> content.
            Backgrounds {policy.backgroundGrantsFeat ? 'can' : 'cannot'} grant a feat.
          </p>
        )}
      </fieldset>

    </StepForm>
  );
}

/** The values a pool method hands out: the standard array, or the totals of the rolled sets. */
function poolOf(basics: Basics, method: ScoreMethod): readonly number[] | undefined {
  return method === 'array' ? standardArray : method === 'roll' ? basics.rolled.map((r) => r.total) : undefined;
}

/** Each pool value goes to one ability: a value already used is shown "(used)" and cannot be picked again. */
function AssignPool(props: { legend: string; pool: readonly number[]; assignment: Assignment; onChange: (a: Assignment) => void }) {
  const { pool, assignment } = props;
  const usedCount = (v: number) => abilityKeys.filter((k) => assignment[k] === v).length;
  const poolCount = (v: number) => pool.filter((p) => p === v).length;
  const values = [...new Set(pool)].sort((a, b) => b - a);
  return (
    <fieldset>
      <legend>{props.legend}</legend>
      <div className="assign-grid">
        {abilities.map(({ key, label }) => (
          <label key={key} className="field">
            {label}
            <select
              value={assignment[key] ?? ''}
              onChange={(e) => {
                const v = e.target.value === '' ? undefined : Number(e.target.value);
                const next = { ...assignment, [key]: v };
                // A value that is already used (a stale select) resets this one to "Choose" and assigns nothing twice.
                props.onChange(assignmentValid(next, pool) ? next : { ...assignment, [key]: undefined });
              }}
            >
              <option value="">Choose</option>
              {values.map((v) => {
                const free = poolCount(v) - usedCount(v) + (assignment[key] === v ? 1 : 0);
                return (
                  <option key={v} value={v} disabled={free <= 0}>
                    {free <= 0 ? `${v} (used)` : v}
                  </option>
                );
              })}
            </select>
          </label>
        ))}
      </div>
      <p className="hint">Assigned: {abilityKeys.filter((k) => assignment[k] !== undefined).length} of 6</p>
    </fieldset>
  );
}

const allEights: AbilityScores = { str: 8, dex: 8, con: 8, int: 8, wis: 8, cha: 8 };
const pointBuyCostOrZero = (score: number) => (Number.isFinite(pointBuyCost(score)) ? pointBuyCost(score) : 0);

/**
 * D32: how the base scores are set. `basics.scores` always holds the scores the draft will use: Enter by hand and point buy
 * edit it directly, the array and the rolled sets write it once all six are assigned. Switching method: point buy starts at
 * all 8; the array and roll keep the assignment if the new pool can still hold it (else it is cleared) and apply it when
 * complete; Enter by hand keeps the last scores. Next stays disabled until the chosen method gives valid scores.
 */
function ScoresStep(props: {
  basics: Basics;
  policy?: RulesFamilyPolicy;
  onChange: Dispatch<SetStateAction<Basics>>;
  onNext: () => void;
  onBack: () => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
}) {
  const { basics, onChange } = props;
  const method = basics.scoreMethod;
  const pool = poolOf(basics, method);
  const spent = abilityKeys.reduce((sum, k) => sum + pointBuyCostOrZero(basics.scores[k]), 0);
  const inRange = abilityKeys.every((k) => Number.isFinite(pointBuyCost(basics.scores[k])));
  const assigned = !!pool && assignmentComplete(basics.assignment) && assignmentValid(basics.assignment, pool);
  const complete = method === 'manual' || (method === 'pointBuy' ? inRange && spent <= pointBuyBudget : assigned);
  const [rolling, setRolling] = useState(false);
  const nextHint = rolling
    ? 'Rolling the scores…'
    : complete
      ? undefined
      : method === 'pointBuy'
        ? inRange
          ? 'Spend at most 27 points to continue.'
          : `Keep every score between ${pointBuyRange.min} and ${pointBuyRange.max} to continue.`
        : method === 'roll' && basics.rolled.length === 0
          ? 'Roll the scores to continue.'
          : 'Assign all six scores to continue.';

  const rollingNow = useRef(false);
  const [rollNote, setRollNote] = useState('');
  // A roll can finish after the step is gone (Back, Cancel): it then reports nothing.
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  function chooseMethod(next: ScoreMethod) {
    onChange((b) => {
      const nextPool = poolOf(b, next);
      const keep = nextPool && assignmentValid(b.assignment, nextPool);
      const assignment = nextPool ? (keep ? b.assignment : {}) : b.assignment;
      const scores = next === 'pointBuy' ? allEights : nextPool && assignmentComplete(assignment) && keep ? scoresFrom(assignment) : b.scores;
      return { ...b, scoreMethod: next, assignment, scores };
    });
  }

  function assign(assignment: Assignment) {
    onChange((b) => ({ ...b, assignment, scores: assignmentComplete(assignment) ? scoresFrom(assignment) : b.scores }));
  }

  async function rollAll() {
    if (rollingNow.current) return; // aria-disabled, not disabled: focus stays on the button, so the press is ignored here
    rollingNow.current = true;
    setRolling(true);
    setRollNote('');
    try {
      const rolled: RollRecord[] = [];
      for (let i = 1; i <= 6; i++) rolled.push(await client.rollDice('4d6', 3, `Ability score roll ${i}`));
      // Only a complete set replaces the old one; an error part-way leaves what was there.
      // The assignment is cleared only while the method is still Roll: one made in the array meanwhile is not the rolled pool's.
      // A roll that lands after the step is gone is dropped: it must not change a draft that has moved on.
      if (!mounted.current) return;
      onChange((b) => ({ ...b, rolled, assignment: b.scoreMethod === 'roll' ? {} : b.assignment }));
      setRollNote('Six scores rolled.');
    } catch (error) {
      if (mounted.current) props.onError(error);
    } finally {
      rollingNow.current = false;
      setRolling(false);
    }
  }

  const shown = (key: Ability) => (pool ? (basics.assignment[key] ?? '–') : basics.scores[key]);
  return (
    <StepForm label="Ability scores" next="Next: species" nextDisabled={!complete || rolling} nextHint={nextHint} onNext={props.onNext} onBack={props.onBack} onCancel={props.onCancel}>
      <fieldset>
        <legend>How are the scores determined?</legend>
        {scoreMethods.map((m) => (
          <label key={m.id} className="choice">
            <input type="radio" name="scoreMethod" checked={method === m.id} onChange={() => chooseMethod(m.id)} />
            {m.label}
          </label>
        ))}
      </fieldset>
      {method === 'array' && <AssignPool legend="Assign the standard array" pool={standardArray} assignment={basics.assignment} onChange={assign} />}
      {method === 'pointBuy' && (
        <fieldset className="abilities">
          <legend>Point buy</legend>
          {abilities.map(({ key, label }) => (
            <label key={key} className="field">
              {label}
              <input
                type="number"
                min={pointBuyRange.min}
                max={pointBuyRange.max}
                value={basics.scores[key]}
                onChange={(e) => onChange((b) => ({ ...b, scores: { ...b.scores, [key]: Number(e.target.value) } }))}
              />
            </label>
          ))}
        </fieldset>
      )}
      {method === 'pointBuy' && inRange && (
        <>
          <p className={spent > pointBuyBudget ? 'warn' : 'hint'}>
            Points left: {pointBuyBudget - spent} of {pointBuyBudget}
          </p>
          {spent < pointBuyBudget && <p className="hint">You can still spend {pointBuyBudget - spent} points.</p>}
        </>
      )}
      {method === 'roll' && (
        <>
          <div className="actions">
            <button type="button" onClick={rollAll} aria-disabled={rolling || undefined}>
              {basics.rolled.length ? 'Roll again' : 'Roll six scores (4d6, drop the lowest)'}
            </button>
          </div>
          {/* Present before it has text, so the announcement is made when the text arrives. */}
          <p role="status" className="hint">
            {rollNote}
          </p>
          {basics.rolled.length > 0 && (
            <>
              <ul aria-label="Rolled sets" className="rolled-sets">
                {basics.rolled.map((r, i) => (
                  <li key={i}>
                    <div className="dice dice-still" aria-hidden="true">
                      {r.dice.map((d, j) => (
                        <span key={j} className="die" data-sides={d.sides} data-value={d.value} data-kept={d.kept ? 'true' : 'false'} />
                      ))}
                    </div>
                    {`Roll ${i + 1}: ${r.total} (${r.dice.filter((d) => d.kept).map((d) => d.value).join(', ')}, dropped ${r.dice.filter((d) => !d.kept).map((d) => d.value).join(', ')})`}
                  </li>
                ))}
              </ul>
              <AssignPool legend="Assign the rolled scores" pool={pool!} assignment={basics.assignment} onChange={assign} />
            </>
          )}
        </>
      )}
      {method === 'manual' && (
        <fieldset className="abilities">
          <legend>Base ability scores</legend>
          {abilities.map(({ key, label }) => (
            <label key={key} className="field">
              {label}
              <input
                type="number"
                min={1}
                max={30}
                required
                value={basics.scores[key]}
                onChange={(e) => onChange((b) => ({ ...b, scores: { ...b.scores, [key]: Number(e.target.value) } }))}
              />
            </label>
          ))}
        </fieldset>
      )}
      <p className="hint">Base scores: {abilities.map(({ key, label }) => `${label.slice(0, 3)} ${shown(key)}`).join(', ')}.</p>
      {props.policy && (
        <p className="hint">
          Increases from your {props.policy.abilityIncreaseSource} are applied on the sheet ({props.policy.displayName}).
        </p>
      )}
    </StepForm>
  );
}

/** The options of one kind the character's rules family can use (D31); what a campaign disallows stays listed, disabled (P-01). */
const ofKind = (options: Shown[], kind: ContentKind) => options.filter((o) => o.kind === kind && visible(o));

/** With one option or none installed, say where more come from, so a short list does not look like a fault. */
function fewHint(count: number, one: string, many: string, loaded: boolean, policy?: RulesFamilyPolicy) {
  // Not while the listing is loading or after it failed: "0 species are installed" would be false then.
  if (count > 1 || !policy || !loaded) return null;
  return (
    <p className="hint">
      {count} {count === 1 ? one : many} {count === 1 ? 'is' : 'are'} installed for {policy.displayName}. More can come from the homebrew studio or a content pack.
    </p>
  );
}

interface PickStepProps {
  basics: Basics;
  policy?: RulesFamilyPolicy;
  options: Shown[];
  /** The listing for the current rules family and campaign has arrived. */
  loaded: boolean;
  /** R41: the names among these picks that need a campaign exception reason which is not given (now). */
  reasonNames: (refs: (ContentReference | undefined)[]) => string[];
  onChange: (basics: Basics) => void;
  onNext: () => void;
  onBack: () => void;
  onCancel: () => void;
}

function SpeciesStep(props: PickStepProps) {
  const { basics, options, onChange } = props;
  const list = ofKind(options, 'species');
  const hint = reasonHint(props.reasonNames([basics.species]));
  return (
    <StepForm label="Species" next="Next: class" nextDisabled={!!hint} nextHint={hint} onNext={props.onNext} onBack={props.onBack} onCancel={props.onCancel}>
      <p className="hint">Every option shows its source and rules family.</p>
      <SinglePick legend="Species" name="species" options={list} value={basics.species} onChange={(species) => onChange({ ...basics, species })} />
      {fewHint(list.length, 'species', 'species', props.loaded, props.policy)}
    </StepForm>
  );
}

function ClassStep(props: PickStepProps) {
  const { basics, options, onChange } = props;
  const hint = reasonHint(props.reasonNames([basics.startingClass]));
  return (
    <StepForm label="Class" next="Next: background" nextDisabled={!!hint} nextHint={hint} onNext={props.onNext} onBack={props.onBack} onCancel={props.onCancel}>
      <p className="hint">Every option shows its source and rules family.</p>
      <SinglePick
        legend="Class (level 1)"
        name="startingClass"
        options={ofKind(options, 'class')}
        value={basics.startingClass}
        onChange={(startingClass) => onChange({ ...basics, startingClass })}
      />
    </StepForm>
  );
}

function BackgroundStep(props: PickStepProps) {
  const { basics, options, onChange } = props;
  const list = ofKind(options, 'background');
  // Spells are picked per caster in the choices step, never pinned as content. Content another revision grants or
  // offers (class features, skill options) arrives through it, so only standalone content is listed here.
  const other = options.filter((o) => !['species', 'background', 'class', 'subclass', 'spell'].includes(o.kind) && o.standalone !== false && visible(o));

  function toggleOther(option: ContentOption) {
    const selected = basics.other.some((p) => sameRef(p, option.reference));
    onChange({
      ...basics,
      other: selected ? basics.other.filter((p) => !sameRef(p, option.reference)) : [...basics.other, option.reference],
    });
  }

  const hint = reasonHint(props.reasonNames([basics.background, ...basics.other]));
  return (
    <StepForm label="Background" next="Next: choices" nextDisabled={!!hint} nextHint={hint} onNext={props.onNext} onBack={props.onBack} onCancel={props.onCancel}>
      <p className="hint">Every option shows its source and rules family.</p>
      <SinglePick
        legend="Background"
        name="background"
        options={list}
        value={basics.background}
        onChange={(background) => onChange({ ...basics, background })}
      />
      {fewHint(list.length, 'background', 'backgrounds', props.loaded, props.policy)}

      <details>
        <summary>Other content ({basics.other.length} selected)</summary>
        <fieldset>
          <legend>Other content</legend>
          <ul className="options">
            {other.map((option) => {
              const id = `opt-${option.reference.revisionId}`;
              return (
                <li key={option.reference.revisionId} className={option.compatible ? '' : 'incompatible'}>
                  <input
                    id={id}
                    type="checkbox"
                    aria-disabled={(!option.compatible && !basics.other.some((p) => sameRef(p, option.reference))) || undefined}
                    checked={basics.other.some((p) => sameRef(p, option.reference))}
                    onChange={() => {
                      // Inert while not offerable, but a ticked one can always be unticked (2.4.3: never `disabled` under focus).
                      if (option.compatible || basics.other.some((p) => sameRef(p, option.reference))) toggleOther(option);
                    }}
                  />
                  <label htmlFor={id}>
                    <span className="option-name">{option.name}</span> <span className="tag">{option.kind}</span> {sourceLine(option)}
                  </label>
                </li>
              );
            })}
          </ul>
        </fieldset>
      </details>
    </StepForm>
  );
}

/** Level-up: which class gains the level, an existing one or a new one (multiclass). */
function LevelStep(props: {
  character: Character;
  options: Shown[];
  nameOf: (ref: ContentReference) => string;
  onNext: (classes: ClassLevel[]) => void;
  onCancel: () => void;
}) {
  const { character } = props;
  const [target, setTarget] = useState<ContentReference | undefined>(character.classes[0]?.class);
  const atMaximum = character.level >= maxLevel;
  const newClasses = props.options.filter((o) => o.kind === 'class' && visible(o) && !character.classes.some((c) => c.class.contentId === o.reference.contentId));

  // The picked new class can become inert after it was picked (its campaign reason was cleared): Next then says why it waits.
  const needsReason = !!target && newClasses.some((o) => !o.compatible && sameRef(o.reference, target));
  // R43: Next is never natively `disabled` and always says why it waits (3.3.2, 4.1.2).
  const blocked = atMaximum
    ? `Already at level ${maxLevel}, the highest level.`
    : !target
      ? 'Choose a class to continue.'
      : needsReason
        ? 'This class needs a campaign exception reason.'
        : undefined;

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (blocked || !target) return; // aria-disabled with its reason shown, so this press is refused openly
    const existing = character.classes.find((c) => sameRef(c.class, target));
    props.onNext(
      existing
        ? character.classes.map((c) => (c === existing ? { ...c, level: c.level + 1 } : c))
        : [...character.classes, { class: target, level: 1 }],
    );
  }

  return (
    <form onSubmit={submit} aria-label="Level">
      <p>
        Level {character.level} → {character.level + 1}
      </p>
      {atMaximum ? null : (
        <fieldset>
          <legend>Class to gain a level in</legend>
          {character.classes.map((c) => (
            <label key={c.class.revisionId} className="choice">
              <input type="radio" name="levelClass" checked={!!target && sameRef(target, c.class)} onChange={() => setTarget(c.class)} />
              {props.nameOf(c.class)} (level {c.level} → {c.level + 1})
            </label>
          ))}
          {newClasses.map((option) => (
            <label key={option.reference.revisionId} className={`choice${option.compatible ? '' : ' incompatible'}`}>
              <input
                type="radio"
                name="levelClass"
                aria-disabled={!option.compatible || undefined}
                checked={!!target && sameRef(target, option.reference)}
                onChange={() => {
                  if (option.compatible) setTarget(option.reference);
                }}
              />
              {option.name} (new class, level 1) {sourceLine(option)}
            </label>
          ))}
        </fieldset>
      )}
      {!atMaximum && (
        <p className="hint">
          Hit points use the fixed value for each new level. If you rolled, record the total as an override on the sheet.
        </p>
      )}
      <div className="actions">
        <button type="submit" aria-disabled={!!blocked || undefined} aria-describedby={blocked ? 'next-hint' : undefined}>
          Next: choices
        </button>
        <button type="button" onClick={props.onCancel}>
          Cancel
        </button>
      </div>
      {blocked && (
        <p id="next-hint" className="hint">
          {blocked}
        </p>
      )}
    </form>
  );
}

/** Every choice the draft offers now, answered through `character.previewChoice`. */
function ChoicePicker(props: {
  choice: ChoiceStatus;
  optionOf: (ref: ContentReference) => Shown | undefined;
  /** False until `content.list` answers: options are then "loading", not "missing". */
  optionsLoaded: boolean;
  /** Ticks (`add`) or unticks one option; the builder applies it to the latest draft. */
  onChange: (ref: ContentReference, add: boolean) => void;
}) {
  const { choice } = props;
  const full = choice.selected.length >= choice.count;
  // R33 (2.4.3): an option ticked when the picker appeared stays rendered for as long as the picker does, so unticking the
  // focused checkbox never removes it; once unticked and not offerable it is aria-disabled, never `disabled` under focus.
  const [atMount] = useState(() => new Set(choice.selected.map((s) => s.revisionId)));
  if (!props.optionsLoaded) {
    return (
      <fieldset className="choice-picker" aria-busy="true">
        <legend>
          {choice.sourceName}: choose {choice.count}
        </legend>
        <p className="hint">Loading the options…</p>
      </fieldset>
    );
  }
  return (
    <fieldset className="choice-picker">
      <legend>
        {choice.sourceName}: choose {choice.count}{' '}
        <span className={choice.resolved ? 'hint' : 'warn'}>{choice.resolved ? '(done)' : `(to do: ${choice.selected.length} of ${choice.count} chosen)`}</span>
      </legend>
      {choice.text && <p className="hint">{choice.text}</p>}
      <ul className="options">
        {choice.options.map((ref) => {
          const option = props.optionOf(ref);
          const checked = choice.selected.some((s) => sameRef(s, ref));
          const id = `choice-${choice.source.revisionId}-${choice.choiceId}-${ref.revisionId}`;
          const unavailable = !option || !option.compatible;
          // D31: an option of the other family is not offered, unless it was chosen when the picker appeared (an imported character).
          const kept = atMount.has(ref.revisionId);
          if (option && !visible(option) && !checked && !kept) return null;
          // Never natively `disabled`: the count fills, or the campaign reason changes, while a row can hold the focus (2.4.3).
          const inert = !checked && (full || unavailable);
          return (
            <li key={ref.revisionId} className={unavailable ? 'incompatible' : ''}>
              <input
                id={id}
                type="checkbox"
                checked={checked}
                aria-disabled={inert || undefined}
                onChange={() => {
                  if (!inert) props.onChange(ref, !checked);
                }}
              />
              <label htmlFor={id}>
                <span className="option-name">{option?.name ?? `Missing content ${ref.revisionId}`}</span>
                {option && <> {sourceLine(option)}</>}
              </label>
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}

const levelName = (level: number) => (level === 0 ? 'Cantrips' : `Level ${level}`);

/**
 * D04: the spells of one caster. Options are the spells on its list, up to the highest level it has a slot for; they are
 * recorded on the draft (character schema v6). Counts are shown, and going over is flagged on the sheet, not blocked.
 */
function SpellPicker(props: {
  entry: SpellcastingEntry;
  spells: Shown[];
  recorded: KnownSpell[];
  /** Records (`add`) or removes one spell of this caster; the builder applies it to the latest draft. */
  onChange: (caster: KnownSpell['caster'], spell: ContentReference, add: boolean) => void;
}) {
  const { entry } = props;
  const highest = entry.slots.reduce((top, count, i) => (count > 0 ? i + 1 : top), 0);
  // R32, R34: never the other family; an own-family spell outside the campaign is listed inert with its reason.
  const options = props.spells.filter((o) => visible(o) && o.spell && o.spell.lists.includes(entry.spellList) && o.spell.level <= highest);
  // D30: a search by name; the legend's counts below still count what is recorded, not what is shown.
  const [query, setQuery] = useState('');
  const shown = options.filter((o) => matchesSpell(o.name, query));
  const levels = [...new Set(shown.map((o) => o.spell!.level))].sort((a, b) => a - b);
  const mine = props.recorded.filter((s) => s.caster === entry.content.contentId);
  const cantrips = mine.filter((s) => props.spells.find((o) => sameRef(o.reference, s.spell))?.spell?.level === 0).length;
  // R23: a chosen spell the search hides would otherwise give no cue; the status names how many.
  const hiddenChosen = options.filter((o) => !shown.includes(o) && mine.some((s) => sameRef(s.spell, o.reference))).length;
  const counts = [
    entry.cantripsAllowed !== undefined ? `${cantrips} of ${entry.cantripsAllowed} cantrips` : undefined,
    entry.spellsAllowed !== undefined ? `${mine.length - cantrips} of ${entry.spellsAllowed} ${entry.preparation === 'known' ? 'known' : 'prepared'} spells` : undefined,
  ].filter(Boolean);

  function toggle(option: Shown) {
    const chosen = mine.some((s) => sameRef(s.spell, option.reference));
    if (!chosen && !option.compatible) return; // inert, not disabled: the focus stays where it is (2.4.3)
    props.onChange(entry.content.contentId, option.reference, !chosen);
  }

  return (
    <fieldset className="choice-picker">
      <legend>
        {entry.name} spells{counts.length > 0 ? ` (${counts.join(', ')})` : ''}
      </legend>
      {options.length > 0 && (
        <SpellSearch id={`spell-search-${entry.content.contentId}`} label={`Search ${entry.name} spells by name`} value={query} onChange={setQuery} shown={shown.length} total={options.length} hiddenChosen={hiddenChosen} />
      )}
      {options.length === 0 && <p className="hint">No spells on the {entry.spellList} list are installed for this level.</p>}
      {options.length > 0 && shown.length === 0 && <p className="hint">No spells match “{query}”.</p>}
      {levels.map((level) => (
        <fieldset key={level}>
          <legend>{levelName(level)}</legend>
          <ul className="options">
            {shown
              .filter((o) => o.spell!.level === level)
              .map((option) => {
                const id = `spell-${entry.content.contentId}-${option.reference.revisionId}`;
                const chosen = mine.some((s) => sameRef(s.spell, option.reference));
                return (
                  <li key={option.reference.revisionId} className={option.compatible ? '' : 'incompatible'}>
                    <input id={id} type="checkbox" checked={chosen} aria-disabled={(!chosen && !option.compatible) || undefined} onChange={() => toggle(option)} />
                    <label htmlFor={id}>
                      <span className="option-name">{option.name}</span> {sourceLine(option)}
                    </label>
                  </li>
                );
              })}
          </ul>
        </fieldset>
      ))}
    </fieldset>
  );
}

/** The review list's Campaign row: never "none" for a campaign the character has: its name once known, else loading or the id. */
export function campaignLabel(campaignId: string | undefined, campaigns: Campaign[], state: 'loading' | 'loaded' | 'failed'): string {
  if (!campaignId) return 'none';
  const found = campaigns.find((c) => c.id === campaignId);
  if (found) return found.name;
  if (state === 'loading') return 'loading…';
  return state === 'failed' ? `(unknown campaign ${campaignId})` : '(unknown campaign)';
}

function ChoicesStep(props: {
  view: CharacterView;
  commitLabel: string;
  optionOf: (ref: ContentReference) => Shown | undefined;
  optionsLoaded: boolean;
  spellOptions: Shown[];
  onChoose: (choice: ChoiceStatus, ref: ContentReference, add: boolean) => void;
  onSpells: (caster: KnownSpell['caster'], spell: ContentReference, add: boolean) => void;
  onBack?: () => void;
  onCommit: () => void;
  onCancel: () => void;
  busy: boolean;
  /** R41: set while a pick in the draft needs a campaign exception reason that is not given; the commit button waits. */
  reasonHint?: string;
  review?: { name: string; family: string; campaign?: string; scores: AbilityScores; method: string; species?: string; cls?: string; background?: string };
}) {
  const choices = props.view.sheet.choices ?? [];
  const open = choices.filter((c) => !c.resolved);
  const casters = props.view.sheet.spellcasting ?? [];
  const review = props.review;
  const rows: [string, string][] = review
    ? [
        ['Name', review.name],
        ['Rules', review.family],
        ['Campaign', review.campaign ?? 'none'],
        ['Scores', `${abilities.map(({ key, label }) => `${label.slice(0, 3)} ${review.scores[key]}`).join(', ')} (${review.method})`],
        ['Species', review.species ?? 'none'],
        ['Class', review.cls ?? 'none'],
        ['Background', review.background ?? 'none'],
      ]
    : [];
  return (
    <div role="group" aria-label="Choices">
      {review && (
        <section aria-labelledby="review-heading">
          <h4 id="review-heading">Review</h4>
          <dl className="review-list">
            {rows.map(([term, value]) => (
              <div key={term}>
                {/* The colon is only for the eye: a screen reader says "Name", not "Name colon". */}
                <dt>{term}<span aria-hidden="true">:</span></dt> <dd>{value}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}
      {choices.length === 0 ? (
        <p>Nothing to choose at this level.</p>
      ) : (
        <p className={open.length > 0 ? 'warn' : ''}>
          {open.length === 0
            ? 'All choices are made.'
            : `${open.length} choice${open.length === 1 ? '' : 's'} still to make. You can save now; the sheet lists them under "Choices to make".`}
        </p>
      )}
      {choices.map((choice) => (
        <ChoicePicker
          key={`${choice.source.revisionId}-${choice.choiceId}`}
          choice={choice}
          optionOf={props.optionOf}
          optionsLoaded={props.optionsLoaded}
          onChange={(ref, add) => props.onChoose(choice, ref, add)}
        />
      ))}
      {casters.map((entry) => (
        <SpellPicker
          key={entry.content.revisionId}
          entry={entry}
          spells={props.spellOptions}
          recorded={props.view.character.spells ?? []}
          onChange={props.onSpells}
        />
      ))}
      <div className="actions">
        {/* aria-disabled, not disabled: a failed save leaves the focus on the button (2.4.3); the guards are in commit() and Cancel. */}
        <button type="button" onClick={props.onCommit} aria-disabled={props.busy || !!props.reasonHint || undefined} aria-describedby={props.reasonHint ? 'commit-hint' : undefined}>
          {props.busy ? 'Saving…' : props.commitLabel}
        </button>
        {props.onBack && (
          <button type="button" onClick={props.onBack}>
            Back
          </button>
        )}
        <button type="button" onClick={props.onCancel} aria-disabled={props.busy || undefined}>
          Cancel
        </button>
      </div>
      {props.reasonHint && (
        <p id="commit-hint" className="hint">
          {props.reasonHint}
        </p>
      )}
    </div>
  );
}

function draftOf(basics: Basics, id: string, previous?: Character): Character {
  return {
    id,
    schemaVersion: 8,
    name: basics.name.trim(),
    rulesFamily: basics.rulesFamily,
    level: 1,
    classes: basics.startingClass ? [{ class: basics.startingClass, level: 1 }] : [],
    // Answers survive going back to the basics; ones that no longer apply are flagged by the rules core.
    choices: previous?.choices ?? [],
    crossFamilyExceptions: [],
    campaignId: basics.campaignId,
    campaignExceptions: previous?.campaignExceptions ?? [],
    spells: previous?.spells ?? [],
    baseAbilities: basics.scores,
    pins: [basics.species, basics.background, ...basics.other].filter((p): p is ContentReference => !!p),
    overrides: [],
    updatedAt: new Date(0).toISOString(),
  };
}

export function CharacterBuilder({ mode, rulesFamilies, onCommitted, onCancel, onError }: Props) {
  const [step, setStep] = useState<Step>(mode.kind === 'create' ? 'rules' : mode.kind === 'levelUp' ? 'level' : 'choices');
  const [basics, setBasics] = useState<Basics>({
    name: '',
    rulesFamily: 'srd-5.1',
    scores: { str: 10, dex: 14, con: 12, int: 10, wis: 13, cha: 8 },
    scoreMethod: 'array',
    assignment: {},
    rolled: [],
    other: [],
  });
  const [draftId] = useState(() => crypto.randomUUID());
  const [view, setView] = useState<CharacterView | undefined>(mode.kind === 'create' ? undefined : mode.view);
  const [listed, setListed] = useState<ContentOption[]>([]);
  // Which rules family and campaign `listed` belongs to; until it matches, options are still loading.
  const [listedFor, setListedFor] = useState<string>();
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [campaignsState, setCampaignsState] = useState<'loading' | 'loaded' | 'failed'>('loading');
  const [outside, setOutside] = useState({ allow: false, reason: '' });
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  // After Cancel (or any unmount) nothing this builder started may report, set state or commit: "Draft discarded" must stay true.
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  const fail = useCallback((error: unknown) => {
    if (mounted.current) onError(error);
  }, [onError]);
  const stepNow = useRef(step);
  useEffect(() => {
    stepNow.current = step;
    previewSeq.current.next(); // leaving a step withdraws any preview it asked for, even if the player returns before it answers
  }, [step]);
  const previewSeq = useRef({
    n: 0,
    next() {
      this.n += 1;
      return this.n;
    },
  });
  const committing = useRef(false);

  const family = mode.kind === 'create' ? basics.rulesFamily : mode.view.character.rulesFamily;
  const campaignId = mode.kind === 'create' ? basics.campaignId : mode.view.character.campaignId;
  const campaign = campaigns.find((c) => c.id === campaignId);

  useEffect(() => {
    client
      .listCampaigns()
      .then((result) => {
        if (!mounted.current) return;
        setCampaigns(result);
        setCampaignsState('loaded');
      })
      .catch((error) => {
        if (mounted.current) setCampaignsState('failed');
        fail(error);
      });
  }, [fail]);

  useEffect(() => {
    let current = true;
    client
      .listContent(family, campaignId)
      .then((result) => {
        if (!current) return;
        setListed(result);
        setListedFor(`${family}|${campaignId ?? ''}`);
        // A pick that does not fit the (new) rules family is dropped, not left checked but disabled (SPEC S-02).
        const fits = (ref?: ContentReference) => !!ref && result.some((o) => o.compatible && sameRef(o.reference, ref));
        setBasics((b) => {
          const other = b.other.filter(fits);
          if ((!b.species || fits(b.species)) && (!b.background || fits(b.background)) && (!b.startingClass || fits(b.startingClass)) && other.length === b.other.length) return b; // nothing dropped: no change, so a pending preview stays valid
          return {
            ...b,
            species: fits(b.species) ? b.species : undefined,
            background: fits(b.background) ? b.background : undefined,
            startingClass: fits(b.startingClass) ? b.startingClass : undefined,
            other,
          };
        });
      })
      .catch(fail);
    return () => {
      current = false;
    };
  }, [family, campaignId, fail]);

  // SPEC P-01: content outside the campaign is unavailable until the player says why they use it (a recorded exception).
  const outsideAllowed = outside.allow && outside.reason.trim().length > 0;
  const options: Shown[] = listed.map((o) =>
    o.allowedInCampaign === false && !outsideAllowed ? { ...o, familyOk: o.compatible, compatible: false, outsideCampaign: true } : { ...o, familyOk: o.compatible },
  );

  /** Exceptions for every referenced revision the campaign does not allow, with the player's reason. */
  function withExceptions(draft: Character): Character {
    if (!outsideAllowed) return draft; // without a reason nothing is recorded; the sheet then warns about outside content
    const known = draft.campaignExceptions ?? [];
    // R34: the chosen spells are campaign-restricted like the rest.
    const references = [...draft.pins, ...draft.classes.map((c) => c.class), ...draft.choices.flatMap((c) => c.selected), ...(draft.spells ?? []).map((s) => s.spell)];
    const added: CampaignException[] = references
      .filter((r) => listed.find((o) => sameRef(o.reference, r))?.allowedInCampaign === false)
      .filter((r) => !known.some((e) => sameRef(e.content, r)))
      .map((content) => ({ content, reason: outside.reason.trim(), recordedAt: new Date().toISOString() }));
    return { ...draft, campaignExceptions: [...known, ...added] };
  }

  // WCAG 2.4.3: each step change moves focus to the builder heading, so keyboard and screen reader users start at the top
  // (the focused Back or Next button is gone by then). A new character's first step keeps the Name field's focus.
  const focusedFor = useRef<Step | undefined>(mode.kind === 'create' ? 'rules' : undefined);
  useEffect(() => {
    if (focusedFor.current === step) return;
    focusedFor.current = step;
    heading.current?.focus();
  }, [step]);

  // N2: a pick changed while a preview is pending (another Background, Other content, a listing that drops a pick) makes the
  // reply stale: it would build a draft the Review no longer describes. Withdraw it; Next previews the current picks.
  // Only the fields a preview is built from count; the scores method, a roll or an assignment do not change the draft.
  const { name: draftName, rulesFamily: draftFamily, scores: draftScores, species: draftSpecies, background: draftBackground, startingClass: draftClass, other: draftOther, campaignId: draftCampaign } = basics;
  useEffect(() => {
    previewSeq.current.next();
  }, [draftName, draftFamily, draftScores, draftSpecies, draftBackground, draftClass, draftOther, draftCampaign]);

  // SPEC I-06: new picks get the newest revision of each content; older ones only name what saved characters pin.
  const pickable = options.filter((o) => !o.superseded);
  const byRevision = new Map(options.map((o) => [o.reference.revisionId, o]));
  const optionOf = (ref: ContentReference) => byRevision.get(ref.revisionId);
  const nameOf = (ref: ContentReference) => optionOf(ref)?.name ?? ref.revisionId;

  // R41: a pick made in this builder (not one the saved character already holds, nor one with a recorded exception) of content
  // the campaign does not allow needs a reason now; the names of those that lack it block the step's Next or the save.
  const original = mode.kind === 'create' ? undefined : mode.view.character;
  // A pick is held by where it sits: a spell by its caster, a choice option by its source and choice (the same content for
  // another caster or choice is a new pick); pins and classes by the content itself.
  type Pick = { ref: ContentReference; key: string };
  const plain = (r: ContentReference): Pick => ({ ref: r, key: `ref:${r.revisionId}` });
  const picksOf = (c: Character): Pick[] => [
    ...c.pins.map(plain),
    ...c.classes.map((k) => plain(k.class)),
    ...c.choices.flatMap((k) => k.selected.map((r) => ({ ref: r, key: `choice:${k.source.revisionId}/${k.choiceId}/${r.revisionId}` }))),
    ...(c.spells ?? []).map((s) => ({ ref: s.spell, key: `spell:${s.caster}/${s.spell.revisionId}` })),
  ];
  const held = new Set(original ? picksOf(original).map((p) => p.key) : []);
  function needReason(picks: Pick[]): string[] {
    if (outsideAllowed) return [];
    const names = picks
      .filter((p) => listed.find((o) => sameRef(o.reference, p.ref))?.allowedInCampaign === false)
      .filter((p) => !held.has(p.key) && !(original?.campaignExceptions ?? []).some((e) => sameRef(e.content, p.ref)))
      .map((p) => nameOf(p.ref));
    return [...new Set(names)];
  }
  const reasonNames = (refs: (ContentReference | undefined)[]) => needReason(refs.filter((r): r is ContentReference => !!r).map(plain));
  const commitHint = view ? reasonHint(needReason(picksOf(view.character))) : undefined;

  /**
   * Previews the draft and moves to the choices. The reply applies only if it is the latest request and the player is still
   * on the step that asked: Back while it is pending cancels the jump, so a late reply never carries an old class forward.
   */
  async function preview(draft: Character, from: 'background' | 'level') {
    const request = previewSeq.current.next();
    const current = () => mounted.current && request === previewSeq.current.n && stepNow.current === from;
    try {
      const result = await client.preview(draft);
      if (!current()) return;
      setView(result);
      setStep('choices');
    } catch (error) {
      if (current()) onError(error);
    }
  }

  // Ticks can come faster than character.previewChoice answers. Each one waits for the previous answer and applies to the
  // draft it returned; built from the rendered selection, a second quick tick would drop the first.
  const latest = useRef(view);
  useEffect(() => {
    latest.current = view;
  }, [view]);
  const choosing = useRef<Promise<void>>(Promise.resolve());

  function choose(choice: ChoiceStatus, ref: ContentReference, add: boolean) {
    choosing.current = choosing.current.then(async () => {
      const current = latest.current;
      if (!current) return;
      const now = (current.sheet.choices ?? []).find((c) => sameRef(c.source, choice.source) && c.choiceId === choice.choiceId) ?? choice;
      const others = now.selected.filter((s) => !sameRef(s, ref));
      try {
        const next = await client.previewChoice(current.character, choice.source, choice.choiceId, add ? [...others, ref] : others);
        if (!mounted.current) return;
        latest.current = next;
        setView(next);
      } catch (error) {
        fail(error);
      }
    });
  }

  function toggleSpell(caster: KnownSpell['caster'], spell: ContentReference, add: boolean) {
    choosing.current = choosing.current.then(async () => {
      const current = latest.current;
      if (!current) return;
      const others = (current.character.spells ?? []).filter((s) => !(s.caster === caster && sameRef(s.spell, spell)));
      try {
        const next = await client.preview({ ...current.character, spells: add ? [...others, { caster, spell, prepared: true }] : others });
        if (!mounted.current) return;
        latest.current = next;
        setView(next);
      } catch (error) {
        fail(error);
      }
    });
  }

  async function commit() {
    if (!view || committing.current) return; // aria-disabled while busy, so the press arrives here and is ignored
    if (commitHint) return; // R41: aria-disabled with its reason shown; nothing is saved without the exception it needs
    committing.current = true;
    setBusy(true);
    try {
      const draft = withExceptions(view.character);
      const saved =
        mode.kind === 'create'
          ? await client.createCharacter({
              name: draft.name,
              rulesFamily: draft.rulesFamily,
              baseAbilities: draft.baseAbilities,
              pins: draft.pins,
              classes: draft.classes,
              choices: draft.choices,
              campaignId: draft.campaignId,
              campaignExceptions: draft.campaignExceptions,
              spells: draft.spells,
            })
          : await client.saveCharacter(draft);
      // Always: the save happened, so the app says so even if the player has left (R36). The app switches the screen before it
      // awaits anything, so a second press cannot reach a builder that is leaving; one that stays is usable again below.
      await Promise.resolve(onCommitted(saved));
    } catch (error) {
      fail(error);
    } finally {
      committing.current = false;
      setBusy(false);
    }
  }

  const at = createSteps.indexOf(step);
  const back = () => setStep(createSteps[Math.max(at - 1, 0)]!);
  const next = () => setStep(createSteps[Math.min(at + 1, createSteps.length - 1)]!);
  const policy = rulesFamilies.find((f) => f.id === basics.rulesFamily);
  const optionsLoaded = listedFor === `${family}|${campaignId ?? ''}`;
  const pickProps = { basics, policy, options: pickable, loaded: optionsLoaded, reasonNames, onChange: setBasics, onBack: back, onCancel };

  const title =
    mode.kind === 'create' ? 'New character' : mode.kind === 'levelUp' ? `Level up ${mode.view.character.name}` : `Choices for ${mode.view.character.name}`;

  return (
    <section className="panel" aria-labelledby="builder-heading">
      <h2 id="builder-heading" tabIndex={-1} ref={heading}>
        {title}
      </h2>
      <p className="hint">
        {mode.kind === 'create'
          ? `Step ${at + 1} of ${createSteps.length}. Nothing is saved until you press Create and save.`
          : 'Nothing is saved until you press the last button. Cancel discards this draft.'}
      </p>
      {mode.kind === 'create' && <h3 id="builder-step-heading">{stepTitles[step]}</h3>}

      {campaign && (
        <fieldset aria-label="Campaign sources">
          <legend>Campaign: {campaign.name}</legend>
          <p className="hint">Only content from the campaign&apos;s sources can be picked.</p>
          <label className="choice">
            <input type="checkbox" checked={outside.allow} onChange={(e) => setOutside({ ...outside, allow: e.target.checked })} />
            Use content from outside the campaign
          </label>
          {outside.allow && (
            <label className="field">
              Reason (recorded with each exception)
              <input value={outside.reason} onChange={(e) => setOutside({ ...outside, reason: e.target.value })} />
            </label>
          )}
        </fieldset>
      )}

      {mode.kind === 'create' && step === 'rules' && (
        <RulesStep basics={basics} rulesFamilies={rulesFamilies} campaigns={campaigns} onChange={setBasics} onNext={next} onCancel={onCancel} />
      )}
      {mode.kind === 'create' && step === 'scores' && (
        <ScoresStep basics={basics} policy={policy} onChange={setBasics} onNext={next} onBack={back} onCancel={onCancel} onError={onError} />
      )}
      {mode.kind === 'create' && step === 'species' && <SpeciesStep {...pickProps} onNext={next} />}
      {mode.kind === 'create' && step === 'class' && <ClassStep {...pickProps} onNext={next} />}
      {mode.kind === 'create' && step === 'background' && (
        <BackgroundStep {...pickProps} onNext={() => preview(draftOf(basics, draftId, view?.character), 'background')} />
      )}
      {step === 'level' && mode.kind === 'levelUp' && (
        <LevelStep
          character={mode.view.character}
          options={pickable}
          nameOf={nameOf}
          onNext={(classes) => preview({ ...(view?.character ?? mode.view.character), classes }, 'level')}
          onCancel={onCancel}
        />
      )}
      {step === 'choices' && view && (
        <ChoicesStep
          view={view}
          commitLabel={mode.kind === 'create' ? 'Create and save' : mode.kind === 'levelUp' ? 'Save level-up' : 'Save choices'}
          optionOf={optionOf}
          optionsLoaded={optionsLoaded}
          spellOptions={pickable.filter((o) => o.kind === 'spell')}
          onChoose={choose}
          onSpells={toggleSpell}
          onBack={mode.kind === 'create' ? () => setStep('background') : mode.kind === 'levelUp' ? () => setStep('level') : undefined}
          onCommit={commit}
          onCancel={() => {
            if (!busy) onCancel(); // a save in flight cannot be discarded; the button says so with aria-disabled
          }}
          busy={busy}
          reasonHint={commitHint}
          review={
            mode.kind === 'create'
              ? {
                  name: basics.name.trim(),
                  family: policy?.displayName ?? basics.rulesFamily,
                  campaign: campaignLabel(basics.campaignId, campaigns, campaignsState),
                  scores: basics.scores,
                  method: scoreMethods.find((m) => m.id === basics.scoreMethod)!.label.toLowerCase(),
                  species: basics.species && nameOf(basics.species),
                  cls: basics.startingClass && nameOf(basics.startingClass),
                  background: basics.background && nameOf(basics.background),
                }
              : undefined
          }
        />
      )}
    </section>
  );
}
