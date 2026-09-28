import { useEffect, useRef, useState, type SubmitEvent } from 'react';
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
  RulesFamilyPolicy,
  SpellcastingEntry,
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
  campaignId?: string;
}

interface Props {
  mode: BuilderMode;
  rulesFamilies: RulesFamilyPolicy[];
  onCommitted: (view: CharacterView) => void;
  onCancel: () => void;
  onError: (error: unknown) => void;
}

/** An option as the builder shows it: outside the campaign counts as unavailable until the player gives a reason. */
type Shown = ContentOption & { outsideCampaign?: boolean };

function sourceLine(option: Shown) {
  return (
    <span className="option-source">
      {option.sourceTitle}
      {option.page ? `, ${option.page}` : ''} · {option.rulesFamilies.join(', ')}
      {option.outsideCampaign ? ' · not allowed in this campaign' : !option.compatible && ' · not available for this rules family'}
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
  campaigns: Campaign[];
  options: ContentOption[];
  onChange: (basics: Basics) => void;
  onNext: () => void;
  onCancel: () => void;
}) {
  const { basics, options, onChange } = props;
  const policy = props.rulesFamilies.find((f) => f.id === basics.rulesFamily);
  const ofKind = (kind: ContentKind) => options.filter((o) => o.kind === kind);
  // Spells are picked per caster in the choices step, never pinned as content.
  const other = options.filter((o) => !['species', 'background', 'class', 'subclass', 'spell'].includes(o.kind));

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
  /** False until `content.list` answers: options are then "loading", not "missing". */
  optionsLoaded: boolean;
  onChange: (selected: ContentReference[]) => void;
}) {
  const { choice } = props;
  const full = choice.selected.length >= choice.count;
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

const levelName = (level: number) => (level === 0 ? 'Cantrips' : `Level ${level}`);

/**
 * D04: the spells of one caster. Options are the spells on its list, up to the highest level it has a slot for; they are
 * recorded on the draft (character schema v6). Counts are shown, and going over is flagged on the sheet, not blocked.
 */
function SpellPicker(props: {
  entry: SpellcastingEntry;
  spells: ContentOption[];
  recorded: KnownSpell[];
  onChange: (spells: KnownSpell[]) => void;
}) {
  const { entry } = props;
  const highest = entry.slots.reduce((top, count, i) => (count > 0 ? i + 1 : top), 0);
  const options = props.spells.filter((o) => o.compatible && o.spell && o.spell.lists.includes(entry.spellList) && o.spell.level <= highest);
  const levels = [...new Set(options.map((o) => o.spell!.level))].sort((a, b) => a - b);
  const mine = props.recorded.filter((s) => s.caster === entry.content.contentId);
  const cantrips = mine.filter((s) => props.spells.find((o) => sameRef(o.reference, s.spell))?.spell?.level === 0).length;
  const counts = [
    entry.cantripsAllowed !== undefined ? `${cantrips} of ${entry.cantripsAllowed} cantrips` : undefined,
    entry.spellsAllowed !== undefined ? `${mine.length - cantrips} of ${entry.spellsAllowed} ${entry.preparation === 'known' ? 'known' : 'prepared'} spells` : undefined,
  ].filter(Boolean);

  function toggle(option: ContentOption) {
    const has = mine.some((s) => sameRef(s.spell, option.reference));
    props.onChange(
      has
        ? props.recorded.filter((s) => !(s.caster === entry.content.contentId && sameRef(s.spell, option.reference)))
        : [...props.recorded, { caster: entry.content.contentId, spell: option.reference, prepared: true }],
    );
  }

  return (
    <fieldset className="choice-picker">
      <legend>
        {entry.name} spells{counts.length > 0 ? ` (${counts.join(', ')})` : ''}
      </legend>
      {options.length === 0 && <p className="hint">No spells on the {entry.spellList} list are installed for this level.</p>}
      {levels.map((level) => (
        <fieldset key={level}>
          <legend>{levelName(level)}</legend>
          <ul className="options">
            {options
              .filter((o) => o.spell!.level === level)
              .map((option) => {
                const id = `spell-${entry.content.contentId}-${option.reference.revisionId}`;
                return (
                  <li key={option.reference.revisionId}>
                    <input id={id} type="checkbox" checked={mine.some((s) => sameRef(s.spell, option.reference))} onChange={() => toggle(option)} />
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

function ChoicesStep(props: {
  view: CharacterView;
  commitLabel: string;
  optionOf: (ref: ContentReference) => ContentOption | undefined;
  optionsLoaded: boolean;
  spellOptions: ContentOption[];
  onChoose: (choice: ChoiceStatus, selected: ContentReference[]) => void;
  onSpells: (spells: KnownSpell[]) => void;
  onBack?: () => void;
  onCommit: () => void;
  onCancel: () => void;
  busy: boolean;
}) {
  const choices = props.view.sheet.choices ?? [];
  const open = choices.filter((c) => !c.resolved);
  const casters = props.view.sheet.spellcasting ?? [];
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
          optionsLoaded={props.optionsLoaded}
          onChange={(selected) => props.onChoose(choice, selected)}
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
    schemaVersion: 6,
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
  const [step, setStep] = useState<Step>(mode.kind === 'create' ? 'basics' : mode.kind === 'levelUp' ? 'level' : 'choices');
  const [basics, setBasics] = useState<Basics>({
    name: '',
    rulesFamily: 'srd-5.1',
    scores: { str: 10, dex: 14, con: 12, int: 10, wis: 13, cha: 8 },
    other: [],
  });
  const [draftId] = useState(() => crypto.randomUUID());
  const [view, setView] = useState<CharacterView | undefined>(mode.kind === 'create' ? undefined : mode.view);
  const [listed, setListed] = useState<ContentOption[]>([]);
  // Which rules family and campaign `listed` belongs to; until it matches, options are still loading.
  const [listedFor, setListedFor] = useState<string>();
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [outside, setOutside] = useState({ allow: false, reason: '' });
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);

  const family = mode.kind === 'create' ? basics.rulesFamily : mode.view.character.rulesFamily;
  const campaignId = mode.kind === 'create' ? basics.campaignId : mode.view.character.campaignId;
  const campaign = campaigns.find((c) => c.id === campaignId);

  useEffect(() => {
    client.listCampaigns().then(setCampaigns).catch(onError);
  }, [onError]);

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
        setBasics((b) => ({
          ...b,
          species: fits(b.species) ? b.species : undefined,
          background: fits(b.background) ? b.background : undefined,
          startingClass: fits(b.startingClass) ? b.startingClass : undefined,
          other: b.other.filter(fits),
        }));
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [family, campaignId, onError]);

  // SPEC P-01: content outside the campaign is unavailable until the player says why they use it (a recorded exception).
  const outsideAllowed = outside.allow && outside.reason.trim().length > 0;
  const options: Shown[] = listed.map((o) =>
    o.allowedInCampaign === false && !outsideAllowed ? { ...o, compatible: false, outsideCampaign: true } : o,
  );

  /** Exceptions for every referenced revision the campaign does not allow, with the player's reason. */
  function withExceptions(draft: Character): Character {
    if (!outsideAllowed) return draft; // without a reason nothing is recorded; the sheet then warns about outside content
    const known = draft.campaignExceptions ?? [];
    const references = [...draft.pins, ...draft.classes.map((c) => c.class), ...draft.choices.flatMap((c) => c.selected)];
    const added: CampaignException[] = references
      .filter((r) => listed.find((o) => sameRef(o.reference, r))?.allowedInCampaign === false)
      .filter((r) => !known.some((e) => sameRef(e.content, r)))
      .map((content) => ({ content, reason: outside.reason.trim(), recordedAt: new Date().toISOString() }));
    return { ...draft, campaignExceptions: [...known, ...added] };
  }

  // WCAG 2.4.3: each step change moves focus to the builder heading, so keyboard and screen reader users start at the top.
  useEffect(() => {
    if (step !== 'basics') heading.current?.focus();
  }, [step]);

  // SPEC I-06: new picks get the newest revision of each content; older ones only name what saved characters pin.
  const pickable = options.filter((o) => !o.superseded);
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
      const draft = withExceptions(view.character);
      onCommitted(
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

      {step === 'basics' && (
        <BasicsStep
          basics={basics}
          rulesFamilies={rulesFamilies}
          campaigns={campaigns}
          options={pickable}
          onChange={setBasics}
          onNext={() => preview(draftOf(basics, draftId, view?.character))}
          onCancel={onCancel}
        />
      )}
      {step === 'level' && mode.kind === 'levelUp' && (
        <LevelStep
          character={mode.view.character}
          options={pickable}
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
          optionsLoaded={listedFor === `${family}|${campaignId ?? ''}`}
          spellOptions={pickable.filter((o) => o.kind === 'spell')}
          onChoose={choose}
          onSpells={(spells) => view && preview({ ...view.character, spells })}
          onBack={mode.kind === 'create' ? () => setStep('basics') : mode.kind === 'levelUp' ? () => setStep('level') : undefined}
          onCommit={commit}
          onCancel={onCancel}
          busy={busy}
        />
      )}
    </section>
  );
}
