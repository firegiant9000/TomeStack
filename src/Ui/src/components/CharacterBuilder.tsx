import { useEffect, useRef, useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type {
  Ability,
  AbilityScores,
  Character,
  CharacterView,
  ChoiceStatus,
  ClassLevel,
  ContentKind,
  ContentOption,
  ContentReference,
  RulesFamilyId,
  RulesFamilyPolicy,
} from '../api/types';

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

type Step = 'basics' | 'level' | 'choices';

interface Basics {
  name: string;
  rulesFamily: RulesFamilyId;
  scores: AbilityScores;
  species?: ContentReference;
  background?: ContentReference;
  startingClass?: ContentReference;
  other: ContentReference[];
}

interface Props {
  mode: BuilderMode;
  rulesFamilies: RulesFamilyPolicy[];
  onCommitted: (view: CharacterView) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
}

function sourceLine(option: ContentOption) {
  return (
    <span className="option-source">
      {option.sourceTitle}
      {option.page ? `, ${option.page}` : ''} · {option.rulesFamilies.join(', ')}
      {!option.compatible && ' · not available for this rules family'}
    </span>
  );
}

/** One origin or class pick: a radio group with "None", so the choice is explicit and keyboard-operable. */
function SinglePick(props: {
  legend: string;
  name: string;
  options: ContentOption[];
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
            disabled={!option.compatible}
            checked={!!props.value && sameRef(props.value, option.reference)}
            onChange={() => props.onChange(option.reference)}
          />
          <span className="option-name">{option.name}</span> {sourceLine(option)}
        </label>
      ))}
    </fieldset>
  );
}

function BasicsStep(props: {
  basics: Basics;
  rulesFamilies: RulesFamilyPolicy[];
  options: ContentOption[];
  onChange: (basics: Basics) => void;
  onNext: () => void;
  onCancel: () => void;
}) {
  const { basics, options, onChange } = props;
  const policy = props.rulesFamilies.find((f) => f.id === basics.rulesFamily);
  const ofKind = (kind: ContentKind) => options.filter((o) => o.kind === kind);
  const other = options.filter((o) => !['species', 'background', 'class', 'subclass'].includes(o.kind));

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    props.onNext();
  }

  function toggleOther(option: ContentOption) {
    const selected = basics.other.some((p) => sameRef(p, option.reference));
    onChange({
      ...basics,
      other: selected ? basics.other.filter((p) => !sameRef(p, option.reference)) : [...basics.other, option.reference],
    });
  }

  return (
    <form onSubmit={submit} aria-label="Basics">
      <label className="field">
        Name
        <input required value={basics.name} onChange={(e) => onChange({ ...basics, name: e.target.value })} autoFocus />
      </label>

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
              onChange={(e) => onChange({ ...basics, scores: { ...basics.scores, [key]: Number(e.target.value) } })}
            />
          </label>
        ))}
      </fieldset>

      <p className="hint">Every option shows its source and rules family. Options for the other family cannot be selected.</p>
      <SinglePick legend="Species" name="species" options={ofKind('species')} value={basics.species} onChange={(species) => onChange({ ...basics, species })} />
      <SinglePick
        legend="Background"
        name="background"
        options={ofKind('background')}
        value={basics.background}
        onChange={(background) => onChange({ ...basics, background })}
      />
      <SinglePick
        legend="Class (level 1)"
        name="startingClass"
        options={ofKind('class')}
        value={basics.startingClass}
        onChange={(startingClass) => onChange({ ...basics, startingClass })}
      />

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
                    disabled={!option.compatible}
                    checked={basics.other.some((p) => sameRef(p, option.reference))}
                    onChange={() => toggleOther(option)}
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

      <div className="actions">
        <button type="submit">Next: choices</button>
        <button type="button" onClick={props.onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}

/** Level-up: which class gains the level, an existing one or a new one (multiclass). */
function LevelStep(props: {
  character: Character;
  options: ContentOption[];
  nameOf: (ref: ContentReference) => string;
  onNext: (classes: ClassLevel[]) => void;
  onCancel: () => void;
}) {
  const { character } = props;
  const [target, setTarget] = useState<ContentReference | undefined>(character.classes[0]?.class);
  const atMaximum = character.level >= maxLevel;
  const newClasses = props.options.filter((o) => o.kind === 'class' && !character.classes.some((c) => c.class.contentId === o.reference.contentId));

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!target) return;
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
      {atMaximum ? (
        <p className="warn">This character is level {maxLevel}, the maximum.</p>
      ) : (
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
                disabled={!option.compatible}
                checked={!!target && sameRef(target, option.reference)}
                onChange={() => setTarget(option.reference)}
              />
              {option.name} (new class, level 1) {sourceLine(option)}
            </label>
          ))}
        </fieldset>
      )}
      <p className="hint">
        Hit points use the fixed value for each new level. If you rolled, record the total as an override on the sheet.
      </p>
      <div className="actions">
        <button type="submit" disabled={atMaximum || !target}>
          Next: choices
        </button>
        <button type="button" onClick={props.onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}

/** Every choice the draft offers now, answered through `character.previewChoice`. */
function ChoicePicker(props: {
  choice: ChoiceStatus;
  optionOf: (ref: ContentReference) => ContentOption | undefined;
  onChange: (selected: ContentReference[]) => void;
}) {
  const { choice } = props;
  const full = choice.selected.length >= choice.count;
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
          return (
            <li key={ref.revisionId} className={unavailable ? 'incompatible' : ''}>
              <input
                id={id}
                type="checkbox"
                checked={checked}
                disabled={!checked && (full || unavailable)}
                onChange={() => props.onChange(checked ? choice.selected.filter((s) => !sameRef(s, ref)) : [...choice.selected, ref])}
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

function ChoicesStep(props: {
  view: CharacterView;
  commitLabel: string;
  optionOf: (ref: ContentReference) => ContentOption | undefined;
  onChoose: (choice: ChoiceStatus, selected: ContentReference[]) => void;
  onBack?: () => void;
  onCommit: () => void;
  onCancel: () => void;
  busy: boolean;
}) {
  const choices = props.view.sheet.choices ?? [];
  const open = choices.filter((c) => !c.resolved);
  return (
    <div role="group" aria-label="Choices">
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
          onChange={(selected) => props.onChoose(choice, selected)}
        />
      ))}
      <div className="actions">
        <button type="button" onClick={props.onCommit} disabled={props.busy}>
          {props.busy ? 'Saving…' : props.commitLabel}
        </button>
        {props.onBack && (
          <button type="button" onClick={props.onBack}>
            Back
          </button>
        )}
        <button type="button" onClick={props.onCancel}>
          Cancel
        </button>
      </div>
    </div>
  );
}

function draftOf(basics: Basics, id: string, previous?: Character): Character {
  return {
    id,
    schemaVersion: 4,
    name: basics.name.trim(),
    rulesFamily: basics.rulesFamily,
    level: 1,
    classes: basics.startingClass ? [{ class: basics.startingClass, level: 1 }] : [],
    // Answers survive going back to the basics; ones that no longer apply are flagged by the rules core.
    choices: previous?.choices ?? [],
    crossFamilyExceptions: [],
    baseAbilities: basics.scores,
    pins: [basics.species, basics.background, ...basics.other].filter((p): p is ContentReference => !!p),
    overrides: [],
    updatedAt: new Date(0).toISOString(),
  };
}

export function CharacterBuilder({ mode, rulesFamilies, onCommitted, onCancel, onError }: Props) {
  const [step, setStep] = useState<Step>(mode.kind === 'create' ? 'basics' : mode.kind === 'levelUp' ? 'level' : 'choices');
  const [basics, setBasics] = useState<Basics>({
    name: '',
    rulesFamily: 'srd-5.1',
    scores: { str: 10, dex: 14, con: 12, int: 10, wis: 13, cha: 8 },
    other: [],
  });
  const [draftId] = useState(() => crypto.randomUUID());
  const [view, setView] = useState<CharacterView | undefined>(mode.kind === 'create' ? undefined : mode.view);
  const [options, setOptions] = useState<ContentOption[]>([]);
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);

  const family = mode.kind === 'create' ? basics.rulesFamily : mode.view.character.rulesFamily;

  useEffect(() => {
    let current = true;
    client
      .listContent(family)
      .then((result) => {
        if (current) setOptions(result);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [family, onError]);

  // WCAG 2.4.3: each step change moves focus to the builder heading, so keyboard and screen reader users start at the top.
  useEffect(() => {
    if (step !== 'basics') heading.current?.focus();
  }, [step]);

  const byRevision = new Map(options.map((o) => [o.reference.revisionId, o]));
  const optionOf = (ref: ContentReference) => byRevision.get(ref.revisionId);
  const nameOf = (ref: ContentReference) => optionOf(ref)?.name ?? ref.revisionId;

  async function preview(draft: Character) {
    try {
      setView(await client.preview(draft));
      setStep('choices');
    } catch (error) {
      onError(error);
    }
  }

  async function choose(choice: ChoiceStatus, selected: ContentReference[]) {
    if (!view) return;
    try {
      setView(await client.previewChoice(view.character, choice.source, choice.choiceId, selected));
    } catch (error) {
      onError(error);
    }
  }

  async function commit() {
    if (!view) return;
    setBusy(true);
    try {
      const draft = view.character;
      onCommitted(
        mode.kind === 'create'
          ? await client.createCharacter({
              name: draft.name,
              rulesFamily: draft.rulesFamily,
              baseAbilities: draft.baseAbilities,
              pins: draft.pins,
              classes: draft.classes,
              choices: draft.choices,
            })
          : await client.saveCharacter(draft),
      );
    } catch (error) {
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  const title =
    mode.kind === 'create' ? 'New character' : mode.kind === 'levelUp' ? `Level up ${mode.view.character.name}` : `Choices for ${mode.view.character.name}`;

  return (
    <section className="panel" aria-labelledby="builder-heading">
      <h2 id="builder-heading" tabIndex={-1} ref={heading}>
        {title}
      </h2>
      <p className="hint">Nothing is saved until you press the last button. Cancel discards this draft.</p>

      {step === 'basics' && (
        <BasicsStep
          basics={basics}
          rulesFamilies={rulesFamilies}
          options={options}
          onChange={setBasics}
          onNext={() => preview(draftOf(basics, draftId, view?.character))}
          onCancel={onCancel}
        />
      )}
      {step === 'level' && mode.kind === 'levelUp' && (
        <LevelStep
          character={mode.view.character}
          options={options}
          nameOf={nameOf}
          onNext={(classes) => preview({ ...(view?.character ?? mode.view.character), classes })}
          onCancel={onCancel}
        />
      )}
      {step === 'choices' && view && (
        <ChoicesStep
          view={view}
          commitLabel={mode.kind === 'create' ? 'Create and save' : mode.kind === 'levelUp' ? 'Save level-up' : 'Save choices'}
          optionOf={optionOf}
          onChoose={choose}
          onBack={mode.kind === 'create' ? () => setStep('basics') : mode.kind === 'levelUp' ? () => setStep('level') : undefined}
          onCommit={commit}
          onCancel={onCancel}
          busy={busy}
        />
      )}
    </section>
  );
}
