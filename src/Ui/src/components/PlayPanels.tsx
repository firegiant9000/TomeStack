import { useEffect, useId, useRef, useState, type SubmitEvent } from 'react';
import type {
  Ability,
  Activation,
  AlsoChangeValue,
  AttackEntry,
  AutomationStatus,
  CharacterView,
  ContentKind,
  DamageOutcome,
  Diagnostic,
  FeatureEffect,
  FeatureEntry,
  PageRef,
  PlayAction,
  ResourceValue,
  ResponseValue,
  RollMode,
  RollRecord,
  RollTarget,
  ToggleValue,
} from '../api/types';
import { hitDiceText, rollBonus } from '../format';
import { rollButtonSuffix } from '../rollModes';
import { diceLine, rollModeText } from '../rollText';
import { announceRollsOn, diceAnimationOn } from '../settings';
import { damageTypeLabel, damageTypes, normalizeDamageType } from './customModel';
import { Pips } from './Pips';

/** The one name for the inspiration toggle and its summary line: "Heroic Inspiration" in 5.2.1, "Inspiration" otherwise. */
export const inspirationLabel = (rulesFamily: string) => (rulesFamily === 'srd-5.2.1' ? 'Heroic Inspiration' : 'Inspiration');

const conditionLabels: Record<string, string> = {
  blinded: 'Blinded',
  charmed: 'Charmed',
  deafened: 'Deafened',
  frightened: 'Frightened',
  grappled: 'Grappled',
  incapacitated: 'Incapacitated',
  invisible: 'Invisible',
  paralyzed: 'Paralyzed',
  petrified: 'Petrified',
  poisoned: 'Poisoned',
  prone: 'Prone',
  restrained: 'Restrained',
  stunned: 'Stunned',
  unconscious: 'Unconscious',
};

const automationLabels: Record<AutomationStatus, string> = {
  automatic: 'automatic',
  assisted: 'assisted: you apply it',
  reference: 'reference only: text, not calculated',
};

/** Investigation 2026-10-06 item 8: Features grouped by what granted them (D25, "From {name}"), else by what the content is. */
const featureGroups: { kind: ContentKind; title: string }[] = [
  { kind: 'class', title: 'Classes' },
  { kind: 'subclass', title: 'Subclasses' },
  { kind: 'species', title: 'Species' },
  { kind: 'background', title: 'Background' },
  { kind: 'feat', title: 'Feats' },
  { kind: 'feature', title: 'Granted features' },
  { kind: 'spell', title: 'Spells' },
  { kind: 'item', title: 'Items' },
];

/**
 * The kind groups first (entries with no granter), then one "From {name}" group per granter sorted by name, then "Other".
 * Every feature lands in exactly one group.
 */
function groupFeatures(features: FeatureEntry[]): { title: string; items: FeatureEntry[] }[] {
  const groupKey = (f: FeatureEntry) =>
    f.grantedByName ? `From ${f.grantedByName}` : (featureGroups.find((g) => g.kind === f.kind)?.title ?? 'Other');
  const byKey = new Map<string, FeatureEntry[]>();
  for (const f of features) {
    const key = groupKey(f);
    byKey.set(key, [...(byKey.get(key) ?? []), f]);
  }
  const kindTitles = featureGroups.map((g) => g.title);
  const granted = [...byKey.keys()].filter((k) => k.startsWith('From ')).sort((a, b) => a.localeCompare(b));
  const ordered = [...kindTitles.filter((t) => byKey.has(t)), ...granted, ...(byKey.has('Other') ? ['Other'] : [])];
  return ordered.map((title) => ({ title, items: byKey.get(title) ?? [] }));
}

export const pageText =(page?: PageRef) =>
  page ? (page.end && page.end !== page.start ? `pp. ${page.start}-${page.end}` : `p. ${page.start}`) : '';

/** Every change here is one deliberate button press, sent as a confirmed `character.play` (SPEC C-05). */
/** `act` resolves true when the change was applied or timed out (outcome unknown) and false when it was refused; a caller that does not wait may ignore it. */
type Act = (action: PlayAction) => void | Promise<boolean>;

export const effectWords: Record<DamageOutcome['effect'], string> = {
  none: 'no defence',
  immune: 'immune',
  halved: 'halved',
  doubled: 'doubled',
  halvedThenDoubled: 'halved, then doubled',
};

/** Wave 3 item 21: "13 slashing, halved: 6". Plain text, not a live region (the hit points heading above already changes). */
export function damageResultText(damage: DamageOutcome): string {
  const what = `${damage.amount} ${damage.type ?? 'untyped'}`;
  const taken = damage.type ? `${what}, ${effectWords[damage.effect]}: ${damage.adjusted}` : `${what}: ${damage.adjusted}`;
  return damage.absorbed > 0 ? `${taken}. Temporary hit points took ${damage.absorbed}.` : `${taken}.`;
}

/**
 * D43 S7: the labels of the responses a reply says were available for a hit and not chosen (`damage.responses-skipped`, one per
 * response, carrying the response id in `effectId`). A response the sheet no longer lists is left out: the message holds ids only.
 */
export function skippedLabelsOf(notices: Diagnostic[] | undefined, responses: ResponseValue[] | undefined): string[] {
  const labels = (notices ?? [])
    .filter((n) => n.code === 'damage.responses-skipped')
    .map((n) => responses?.find((r) => r.contentId === n.content?.contentId && r.responseId === n.effectId)?.label);
  return labels.filter((l): l is string => l !== undefined);
}

const skippedResponseLabels = (view: CharacterView) => skippedLabelsOf(view.notices, view.sheet.responses);

/**
 * The damage type, first option "Untyped" (no defence applies). `onDamageType` makes it controlled, so a weapon's damage
 * roll elsewhere on the Play tab can pre-fill it; alone it keeps its own value.
 */
export function HitPointsPanel({
  view,
  act,
  damageType,
  onDamageType,
  onDamage,
  panelOpen = false,
}: {
  view: CharacterView;
  act: Act;
  damageType?: string;
  onDamageType?: (type: string) => void;
  /** D43 S7: opens the damage panel (amount, type '' for untyped). Used instead of a one-step damage when the sheet has damage responses. */
  onDamage?: (amount: number, type: string) => void;
  /** The damage panel is open and lists the skipped responses itself, so this panel does not repeat them. */
  panelOpen?: boolean;
}) {
  const hp = view.sheet.hitPoints;
  const [amount, setAmount] = useState('');
  const [ownType, setOwnType] = useState('');
  const type = damageType ?? ownType;
  const setType = onDamageType ?? setOwnType;
  if (!hp) return null;
  const n = Number(amount);
  const valid = amount !== '' && Number.isInteger(n) && n >= 0;

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
  }

  function apply(action: PlayAction['action']) {
    if (!valid) return;
    if (action === 'damage' && onDamage && (view.sheet.responses ?? []).length > 0) {
      onDamage(n, type);
      setAmount('');
      return;
    }
    act({ action, amount: n, ...(action === 'damage' && type ? { damageType: type } : {}) });
    setAmount('');
  }

  return (
    <section aria-labelledby="hp-heading" className="play-panel">
      <h3 id="hp-heading" tabIndex={-1}>
        Hit points: {hp.current} of {hp.maximum}
        {hp.temporary > 0 ? `, ${hp.temporary} temporary` : ''}
      </h3>
      <form className="inline-form" onSubmit={submit}>
        <label className="field">
          Amount
          <input type="number" min={0} value={amount} onChange={(e) => setAmount(e.target.value)} />
        </label>
        <label className="field">
          Damage type
          <select value={type} onChange={(e) => setType(e.target.value)}>
            <option value="">Untyped</option>
            {damageTypes.map((t) => (
              <option key={t} value={t}>
                {damageTypeLabel(t)}
              </option>
            ))}
          </select>
        </label>
        <button type="button" disabled={!valid} onClick={() => apply('damage')}>
          Take damage
        </button>
        <button type="button" disabled={!valid} onClick={() => apply('heal')}>
          Heal
        </button>
        <button type="button" disabled={!valid} onClick={() => apply('setTemporaryHitPoints')}>
          Set temporary hit points
        </button>
      </form>
      {view.damage && (
        <>
          <p className="damage-result">{damageResultText(view.damage)}</p>
          {view.damage.warnings.map((w) => (
            <p key={w.code} className="warn">
              {w.message}
            </p>
          ))}
        </>
      )}
      {!panelOpen && skippedResponseLabels(view).length > 0 && <p className="hint">Available and not chosen: {skippedResponseLabels(view).join(', ')}.</p>}
      <p className="hint">
        A damage type applies the character&apos;s resistances, immunities and vulnerabilities first (Untyped ignores them). Temporary hit points absorb the
        rest. They do not stack: setting them replaces the old value.
      </p>
      {(view.sheet.hitDice ?? []).length > 0 && (
        <p>
          Hit dice: {hitDiceText(view.sheet.hitDice ?? [])}
        </p>
      )}
      <label className="choice">
        <input
          type="checkbox"
          checked={view.character.play?.inspiration ?? false}
          onChange={(e) => act({ action: 'setInspiration', amount: e.target.checked ? 1 : 0 })}
        />
        {inspirationLabel(view.character.rulesFamily)}
      </label>
    </section>
  );
}

/**
 * SPEC C-05, SRD death saving throws (the same in both families): shown at 0 hit points, or while saves are recorded.
 * A roll is only a suggestion; "Record" is the confirmed change, and TomeStack applies the SRD outcome of the number.
 */
export function DeathSavesPanel({
  view,
  act,
  roll,
  lastRoll,
}: {
  view: CharacterView;
  act: Act;
  roll: (target: RollTarget) => void;
  lastRoll?: RollRecord;
}) {
  const hp = view.sheet.hitPoints;
  const saves = view.character.play?.deathSaves ?? { successes: 0, failures: 0 };
  const [entered, setEntered] = useState('');
  if (!hp || (hp.current > 0 && saves.successes === 0 && saves.failures === 0)) return null;
  const rolled = lastRoll?.provenance?.rollId === 'deathSave' ? lastRoll.dice.find((d) => d.kept)?.value : undefined;
  const value = Number(entered);
  const valid = entered !== '' && Number.isInteger(value) && value >= 1 && value <= 20;
  const state = saves.failures >= 3 ? ' (dead)' : saves.successes >= 3 ? ' (stable)' : '';
  return (
    <section aria-labelledby="death-saves-heading" className="play-panel">
      <h3 id="death-saves-heading">
        Death saving throws: {saves.successes} of 3 successes, {saves.failures} of 3 failures{state}
      </h3>
      {hp.current === 0 && (
        <div className="actions">
          <button type="button" onClick={() => roll({ deathSave: true })}>
            Roll death saving throw
          </button>
          {rolled !== undefined && (
            <button type="button" onClick={() => act({ action: 'recordDeathSave', amount: rolled })}>
              Record death saving throw ({rolled})
            </button>
          )}
          <label className="field">
            d20 rolled at the table
            <input type="number" min={1} max={20} value={entered} onChange={(e) => setEntered(e.target.value)} />
          </label>
          <button
            type="button"
            disabled={!valid}
            onClick={() => {
              act({ action: 'recordDeathSave', amount: value });
              setEntered('');
            }}
          >
            Record this roll
          </button>
          <button type="button" onClick={() => act({ action: 'addDeathSaveFailure', amount: 1 })}>
            Add a failure (damage at 0)
          </button>
        </div>
      )}
      <button type="button" onClick={() => act({ action: 'clearDeathSaves' })}>
        Clear death saving throws
      </button>
      <p className="hint">
        10 or higher is a success; a 1 counts as two failures; a 20 regains 1 hit point. A critical hit at 0 hit points is two
        failures. Regaining hit points clears them.
      </p>
    </section>
  );
}

/**
 * Character schema v8 (D19): concentration. Rolling the save changes nothing; "Kept concentration" and "End concentration" are
 * the confirmed changes (SPEC C-05). The DC was set by the service when damage was taken (max(10, half the damage dealt)).
 */
export function ConcentrationPanel({ view, act, roll }: { view: CharacterView; act: Act; roll: (target: RollTarget) => void }) {
  const con = view.character.play?.concentration;
  const pending = con?.pendingSaveDc !== undefined;
  const heading = useRef<HTMLHeadingElement>(null);
  const wasConcentrating = useRef(false);
  const wasPending = useRef(false);
  // WCAG 2.4.3: both confirmed outcomes unmount the focused button. If focus fell to <body>, "Kept concentration" puts it on
  // this panel's heading and "End concentration" (the whole panel gone) on the Hit points heading, which stays on the tab.
  useEffect(() => {
    const lost = !document.activeElement || document.activeElement === document.body;
    if (lost && con && wasPending.current && !pending) heading.current?.focus();
    if (lost && !con && wasConcentrating.current) document.getElementById('hp-heading')?.focus();
    wasConcentrating.current = con !== undefined;
    wasPending.current = pending;
  }, [con, pending]);
  if (!con) return null;
  // The roll applies every automatic save.con reason, so the button names them (as the Stats card does).
  const saveCon = view.sheet.fields.find((f) => f.field === 'save.con');
  return (
    <section aria-labelledby="concentration-heading" className="play-panel">
      <h3 id="concentration-heading" tabIndex={-1} ref={heading}>
        Concentration: {con.name}
        {pending ? `, Constitution saving throw DC ${con.pendingSaveDc} pending` : ''}
      </h3>
      <div className="actions">
        {pending && (
          <>
            <button type="button" onClick={() => roll({ field: 'save.con' })}>
              Roll Constitution saving throw
              {saveCon ? rollButtonSuffix(saveCon) : ''}
            </button>
            <button type="button" onClick={() => act({ action: 'clearConcentrationCheck' })}>
              Kept concentration
            </button>
          </>
        )}
        <button type="button" onClick={() => act({ action: 'endConcentration' })}>
          End concentration
        </button>
      </div>
      <p className="hint">
        Taking damage asks for a Constitution save of DC 10 or half the damage, whichever is higher (at most 30 under the 2024 rules); at 0
        hit points the spell ends by itself.
      </p>
    </section>
  );
}

export function ConditionsPanel({ view, act }: { view: CharacterView; act: Act }) {
  const play = view.character.play;
  const active = play?.conditions ?? [];
  return (
    <section aria-labelledby="conditions-heading" className="play-panel">
      <h3 id="conditions-heading">Conditions</h3>
      <fieldset className="conditions">
        <legend>Conditions (reminders; they do not change calculated values)</legend>
        {Object.entries(conditionLabels).map(([key, label]) => (
          <label key={key} className="choice">
            <input
              type="checkbox"
              checked={active.includes(key)}
              onChange={() => act({ action: active.includes(key) ? 'removeCondition' : 'addCondition', condition: key })}
            />
            {label}
          </label>
        ))}
      </fieldset>
      <label className="field">
        Exhaustion level
        <select value={play?.exhaustion ?? 0} onChange={(e) => act({ action: 'setExhaustion', amount: Number(e.target.value) })}>
          {[0, 1, 2, 3, 4, 5, 6].map((level) => (
            <option key={level} value={level}>
              {level}
            </option>
          ))}
        </select>
      </label>
    </section>
  );
}

function ResourceCard({ resource, act, level = 4 }: { resource: ResourceValue; act: Act; level?: 4 | 5 }) {
  const key = { contentId: resource.content.contentId, resourceId: resource.resourceId };
  const tracked = resource.current !== undefined && resource.maximum !== undefined;
  const headingId = `resource-${resource.content.revisionId}-${resource.resourceId}`;
  const Heading = level === 4 ? 'h4' : 'h5';
  // D43 slice 3: the amount box spends the chosen number in one confirmed change (1 up to what is left); "Spend 1" stays for one click.
  const [amount, setAmount] = useState('1');
  const chosen = Number(amount);
  const chosenValid = tracked && amount.trim() !== '' && Number.isInteger(chosen) && chosen >= 1 && chosen <= resource.current!;
  // An invalid amount holds the button with aria-disabled (never `disabled`) and a plain-text reason linked by aria-describedby (WCAG 3.3.1).
  const amountReasonId = `${headingId}-amount-reason`;
  const amountReason = chosenValid
    ? ''
    : amount.trim() === ''
      ? 'Enter an amount first.'
      : resource.current === 0
        ? `No ${resource.label} left to spend.`
        : `Enter a whole number from 1 to ${resource.current}.`;
  // A counting-up pool (strain) reads as what has built up, not what is left.
  const state = resource.countsUp ? `built up ${resource.spent} of ${resource.maximum}` : `${resource.current} of ${resource.maximum}`;
  return (
    <li className="resource">
      <Heading id={headingId}>
        {resource.label}: {tracked ? state : 'tracked by hand'}
        {tracked && <Pips filled={resource.countsUp ? resource.spent : resource.current!} total={resource.maximum!} />}
      </Heading>
      <p className="hint">
        From {resource.contentName} · {automationLabels[resource.automation]}
        {resource.recoveries.length > 0 &&
          ` · recovers on ${resource.recoveries.map((r) => `${r.on === 'longRest' ? 'long' : 'short'} rest (${r.amount})`).join(', ')}`}
      </p>
      {tracked && (
        <div className="actions">
          <button type="button" disabled={resource.current === 0} onClick={() => act({ action: 'spend', amount: 1, ...key })}>
            Spend 1 {resource.label}
          </button>
          <button type="button" disabled={resource.spent === 0} onClick={() => act({ action: 'regain', amount: 1, ...key })}>
            Regain 1 {resource.label}
          </button>
          <label className="field">
            Amount to spend: {resource.label}
            <input
              type="number"
              min={1}
              max={resource.current}
              step={1}
              value={amount}
              aria-invalid={amount.trim() !== '' && !chosenValid ? true : undefined}
              aria-describedby={amountReason ? amountReasonId : undefined}
              onChange={(e) => setAmount(e.target.value)}
            />
          </label>
          <button
            type="button"
            aria-disabled={!chosenValid || undefined}
            aria-describedby={amountReason ? amountReasonId : undefined}
            onClick={() => chosenValid && act({ action: 'spend', amount: chosen, ...key })}
          >
            Spend amount: {resource.label}
          </button>
          {amountReason && (
            <p id={amountReasonId} className="hint">
              {amountReason}
            </p>
          )}
        </div>
      )}
      {resource.warnings.length > 0 && (
        <ul className="warnings" aria-label={`${resource.label} warnings`}>
          {resource.warnings.map((w) => (
            <li key={`${w.code}-${w.effectId ?? ''}`}>{w.message}</li>
          ))}
        </ul>
      )}
      {resource.trace.length > 0 && (
        <details>
          <summary>How the maximum is calculated</summary>
          <ul>
            {resource.trace.map((step) => (
              <li key={step.order}>
                {step.description} = {step.result}
                {step.inputs && ` (${step.inputs.map((i) => `${i.name} ${i.value}`).join(', ')})`}
                {step.origin.sourceTitle && ` · ${step.origin.sourceTitle}${step.origin.page ? `, ${pageText(step.origin.page)}` : ''}`}
              </li>
            ))}
          </ul>
        </details>
      )}
    </li>
  );
}

/** Content v9 (ADR-010): each class-table column at the character's level in that class, such as "Ink: 4". */
export function ClassColumnsPanel({ view }: { view: CharacterView }) {
  const scales = view.sheet.scales ?? [];
  if (scales.length === 0) return null;
  return (
    <section aria-labelledby="class-columns-heading" className="play-panel">
      <h3 id="class-columns-heading">Class columns</h3>
      <ul className="resources">
        {scales.map((s) => (
          <li key={`${s.class.revisionId}-${s.scaleId}`} className="resource">
            <span className="option-name">
              {s.label}: {s.value}
            </span>{' '}
            <span className="hint">
              {s.className} level {s.classLevel}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

export function ResourcesPanel({ view, act }: { view: CharacterView; act: Act }) {
  const resources = view.sheet.resources ?? [];
  if (resources.length === 0) return null;
  // Wave 3 item 22 (R157): a resource with an activation sits under the same group headings as the actions; the rest are listed as before.
  const ungrouped = resources.filter((r) => !r.activation);
  const card = (resource: ResourceValue, level?: 4 | 5) => (
    <ResourceCard key={`${resource.content.revisionId}-${resource.resourceId}`} resource={resource} act={act} level={level} />
  );
  return (
    <section aria-labelledby="resources-heading" className="play-panel">
      <h3 id="resources-heading">Resources</h3>
      {ungrouped.length > 0 && <ul className="resources">{ungrouped.map((resource) => card(resource))}</ul>}
      {activationGroups.map(({ key, title }) => {
        const group = resources.filter((r) => r.activation === key);
        if (group.length === 0) return null;
        return (
          <section key={key} aria-labelledby={`resources-${key}`}>
            <h4 id={`resources-${key}`}>{title}</h4>
            <ul className="resources">{group.map((resource) => card(resource, 5))}</ul>
          </section>
        );
      })}
    </section>
  );
}

const activationGroups: { key: Activation; title: string }[] = [
  { key: 'action', title: 'Actions' },
  { key: 'bonusAction', title: 'Bonus actions' },
  { key: 'reaction', title: 'Reactions' },
  { key: 'other', title: 'Other' },
];

const signed = (n: number) => `${n >= 0 ? '+' : ''}${n}`;

/**
 * One toggle of the active effects. D38: an auto toggle follows the hit points, so it shows its state as text and has no switch
 * (there is nothing to confirm). Kept small so activation groups can take it over.
 */
function ToggleRow({ toggle: t, act }: { toggle: ToggleValue; act: Act }) {
  if (t.auto) {
    return (
      <div className="choice">
        {t.label}
        <span className="option-source">
          {t.contentName} · On while bloodied ({t.on ? 'on now' : 'off now'})
          {t.text ? ` · ${t.text}` : ''}
        </span>
      </div>
    );
  }
  return (
    <label className="choice">
      <input
        type="checkbox"
        checked={t.on}
        onChange={() => act({ action: t.on ? 'toggleOff' : 'toggleOn', contentId: t.content.contentId, toggleId: t.toggleId })}
      />
      {t.label}
      <span className="option-source">
        {t.contentName}
        {t.resourceId ? ' · turning it on spends 1 use' : ''}
        {t.text ? ` · ${t.text}` : ''}
      </span>
    </label>
  );
}

/**
 * SPEC C-04: attacks and feature rolls grouped by action, bonus action, reaction and other. Rolling never spends anything;
 * a roll that names a resource offers "Spend" in its record.
 */
export function ActionsPanel({
  view,
  roll,
  act,
  rollMode = 'normal',
  onDamageType,
}: {
  view: CharacterView;
  roll: (target: RollTarget) => void;
  act: Act;
  /** The summary's advantage / disadvantage choice. Used by the attack rolls of weapons and of the character's own attacks (R93); damage is always normal. */
  rollMode?: RollMode;
  /** Wave 3 item 21: a damage roll pre-fills the Hit points damage type with the attack's type ('' when it names none of the thirteen). */
  onDamageType?: (type: string) => void;
}) {
  const [critical, setCritical] = useState(false);
  const rollDamage = (target: RollTarget, type?: string) => {
    onDamageType?.(normalizeDamageType(type));
    roll(target);
  };
  const attacks = view.sheet.attacks ?? [];
  const customAttacks = view.character.customAttacks ?? [];
  const toggles = view.sheet.toggles ?? [];
  const rolls = (view.sheet.features ?? []).flatMap((feature) =>
    feature.effects.filter((e) => e.type === 'roll').map((effect) => ({ feature, effect })),
  );
  // D43 slice 1: grouped toggles are the form picker, whatever their activation; slice 3 moved the picker to the combat strip.
  const plainToggles = toggles.filter((t) => !t.group);
  // D43 slice 3: an action or switch that spends an allowance pool (reactions, say) is listed under that pool's name, not its activation group.
  const resourcePools = view.sheet.resources ?? [];
  const allowances = resourcePools.filter((r) => r.allowance);
  const allowanceOf = (resourceId: string | undefined, contentId: string) =>
    resourceId ? allowances.find((r) => r.resourceId === resourceId && r.content.contentId === contentId) : undefined;
  const rollPool = ({ feature, effect }: (typeof rolls)[number]) => allowanceOf(effect.resourceId, effect.resourceContent ?? feature.content.contentId);
  const togglePool = (t: ToggleValue) => allowanceOf(t.resourceId, t.costResourceContent ?? t.content.contentId);
  const poolGroups = allowances
    .map((pool) => ({ pool, rolls: rolls.filter((r) => rollPool(r) === pool), toggles: plainToggles.filter((t) => togglePool(t) === pool) }))
    .filter((g) => g.rolls.length > 0 || g.toggles.length > 0);
  if (attacks.length === 0 && customAttacks.length === 0 && rolls.length === 0 && plainToggles.length === 0) return null;
  // Wave 3 item 22 (R157): a toggle with an activation is listed in that group; one without stays in "Active effects".
  const ungroupedToggles = plainToggles.filter((t) => !t.activation && !togglePool(t));
  const rollItem = ({ feature, effect }: (typeof rolls)[number]) => (
    <RollItem
      key={`${feature.content.revisionId}-${effect.id}`}
      feature={feature}
      effect={effect}
      pool={effect.resourceId ? resourcePools.find((r) => r.resourceId === effect.resourceId && r.content.contentId === (effect.resourceContent ?? feature.content.contentId)) : undefined}
      pools={resourcePools}
      critical={critical}
      roll={roll}
      act={act}
    />
  );
  const toggleItem = (t: ToggleValue) => (
    <li key={`${t.content.revisionId}-${t.toggleId}`} className="feature">
      <ToggleRow toggle={t} act={act} />
    </li>
  );
  return (
    <section aria-labelledby="actions-heading" className="play-panel">
      <h3 id="actions-heading">Attacks and actions</h3>
      {ungroupedToggles.length > 0 && (
        <fieldset>
          <legend>Active effects (a switch is a confirmed change)</legend>
          {ungroupedToggles.map((t) => (
            <ToggleRow key={`${t.content.revisionId}-${t.toggleId}`} toggle={t} act={act} />
          ))}
        </fieldset>
      )}
      <label className="choice">
        <input type="checkbox" checked={critical} onChange={(e) => setCritical(e.target.checked)} />
        Critical hit (double the damage dice)
      </label>
      {activationGroups.map(({ key, title }) => {
        // Weapons stay in 'action'; a feature attack goes where its activation says (default 'action').
        const groupAttacks = attacks.filter((a) => (a.source === 'feature' ? (a.activation ?? 'action') : 'action') === key);
        const groupCustom = key === 'action' ? customAttacks : [];
        const groupRolls = rolls.filter((r) => (r.effect.activation ?? 'other') === key && !rollPool(r));
        const groupToggles = plainToggles.filter((t) => t.activation === key && !togglePool(t));
        if (groupAttacks.length === 0 && groupCustom.length === 0 && groupRolls.length === 0 && groupToggles.length === 0) return null;
        return (
          <section key={key} aria-labelledby={`actions-${key}`}>
            <h4 id={`actions-${key}`}>{title}</h4>
            <ul className="features">
              {groupAttacks.map((attack) => (
                <AttackItem
                  key={`${attack.item.revisionId}-${attack.effectId}`}
                  attack={attack}
                  critical={critical}
                  rollMode={rollMode}
                  roll={roll}
                  rollDamage={rollDamage}
                  pool={attack.resourceId ? resourcePools.find((r) => r.resourceId === attack.resourceId && r.content.contentId === attack.costResourceContent) : undefined}
                />
              ))}
              {groupCustom.map((attack) => (
                <li key={attack.id} className="feature">
                  <span className="option-name">{attack.name}</span>: {signed(attack.toHit)} to hit
                  {attack.damage ? `, ${attack.damage}${attack.damageType ? ` ${attack.damageType}` : ''}` : ''}
                  {attack.note ? <span className="hint"> · {attack.note}</span> : null}{' '}
                  <button type="button" onClick={() => roll({ customAttack: attack.id, mode: rollMode })}>
                    Roll {attack.name} attack
                  </button>
                  {attack.damage && (
                    <button type="button" onClick={() => rollDamage({ customAttack: attack.id, damage: true, critical }, attack.damageType)}>
                      Roll {attack.name} damage
                    </button>
                  )}
                </li>
              ))}
              {groupRolls.map(rollItem)}
              {groupToggles.map(toggleItem)}
            </ul>
          </section>
        );
      })}
      {poolGroups.map(({ pool, rolls: poolRolls, toggles: poolToggles }) => {
        const id = `actions-pool-${pool.content.revisionId}-${pool.resourceId}`;
        return (
          <section key={id} aria-labelledby={id}>
            <h4 id={id}>{pool.label}</h4>
            {pool.maximum !== undefined && pool.current !== undefined && (
              <p className="hint">
                {pool.current} of {pool.maximum} left
              </p>
            )}
            <ul className="features">
              {poolRolls.map(rollItem)}
              {poolToggles.map(toggleItem)}
            </ul>
          </section>
        );
      })}
    </section>
  );
}

const saveAbilityNames: Record<Ability, string> = {
  str: 'Strength',
  dex: 'Dexterity',
  con: 'Constitution',
  int: 'Intelligence',
  wis: 'Wisdom',
  cha: 'Charisma',
};

/** D43 S5: "Save: DC 13 Dexterity · 100-ft line, 5 ft wide · Range: Self · Each creature in the line"; absent parts are left out. */
export function actionDetails(effect: FeatureEffect): string {
  const parts: string[] = [];
  if (effect.saveAbility) {
    const name = saveAbilityNames[effect.saveAbility] ?? effect.saveAbility;
    parts.push(effect.saveDc !== undefined ? `Save: DC ${effect.saveDc} ${name}` : `Save: ${name} (DC unavailable)`);
  }
  if (effect.area) {
    const { shape, size, width } = effect.area;
    parts.push(`${size}-ft ${shape}${shape === 'line' && width ? `, ${width} ft wide` : ''}`);
  }
  if (effect.range) parts.push(`Range: ${effect.range}`);
  if (effect.targets) parts.push(effect.targets);
  return parts.join(' · ');
}

/**
 * D43 S5: one roll or action of a feature. A roll with dice is a Roll button (a per-unit action also has a "Points to spend" box
 * whose value is always sent as the roll's amount); an action with no dice and a pool is a "Use" button that spends its cost.
 */
function RollItem({
  feature,
  effect,
  pool,
  pools,
  critical,
  roll,
  act,
}: {
  feature: FeatureEntry;
  effect: FeatureEffect;
  pool?: ResourceValue;
  /** Every pool on the sheet: an `alsoChange` entry can name another one. */
  pools: ResourceValue[];
  critical: boolean;
  roll: (target: RollTarget) => void;
  act: Act;
}) {
  const baseId = `${feature.content.revisionId}-${effect.id}`;
  const baseCost = effect.cost ?? 0;
  // An untracked pool has no points to choose from: the roll goes without an amount and the service charges the base cost.
  const perUnit = effect.dice && pool?.current !== undefined ? effect.perUnit : undefined;
  const drawsDown = effect.drawsDown === true;
  // D43 S8: a release can only draw down what has built up, so its most is limited by the pool's `spent`.
  const unitMax = perUnit ? (perUnit.maxUnits !== undefined ? baseCost + perUnit.maxUnits : pool?.maximum) : undefined;
  const maxAmount = perUnit && drawsDown && pool ? Math.min(unitMax ?? pool.spent, pool.spent) : unitMax;
  // The amount starts at the base cost and starts over when the base cost, or the action itself, changes.
  const amountKey = `${baseId}:${baseCost}`;
  const [typed, setTyped] = useState({ key: amountKey, text: String(baseCost) });
  const amount = typed.key === amountKey ? typed.text : String(baseCost);
  const setAmount = (text: string) => setTyped({ key: amountKey, text });
  const chosen = Number(amount);
  const amountOk = amount.trim() !== '' && /^\d+$/.test(amount.trim()) && chosen >= baseCost && (maxAmount === undefined || chosen <= maxAmount);
  const amountReason = `Enter a whole number ${maxAmount === undefined ? `of at least ${baseCost}` : `from ${baseCost} to ${maxAmount}`}.`;
  // D43 slice 4: an action outside the form it needs is disabled, with the reason as text the button is described by.
  const locked = effect.available === false;
  const reasonId = `roll-reason-${baseId}`;
  const amountReasonId = `roll-amount-reason-${baseId}`;
  const details = actionDetails(effect);
  const label = effect.label || effect.id;
  const diceless = !effect.dice;
  // An absent cost means one use only when the action declares no cost. A declared cost that failed leaves `cost` absent and
  // puts an effect.invalid-formula diagnostic on the effect (the sheet does not say which formula); never guess one use then.
  const costFailed =
    effect.cost === undefined &&
    effect.resourceId !== undefined &&
    (feature.diagnostics ?? []).some((d) => d.code === 'effect.invalid-formula' && d.effectId === effect.id);
  const useCost = effect.cost ?? 1;
  // A draw-down use spends what has built up (`spent`); any other use spends what is left (`current`).
  const available = drawsDown ? pool?.spent : pool?.current;
  const unaffordable = diceless && effect.resourceId !== undefined && (pool?.current === undefined || (available ?? 0) < useCost);
  const poolName = pool?.label ?? effect.resourceId;
  const alsoChange = effect.alsoChange ?? [];
  const compound = alsoChange.length > 0 || drawsDown;
  const alsoPool = (a: AlsoChangeValue) => pools.find((r) => r.resourceId === a.resourceId && r.content.contentId === a.resourceContent);
  const alsoName = (a: AlsoChangeValue) => alsoPool(a)?.label ?? a.resourceId;
  const alsoText = alsoChange
    .filter((a) => a.amount != null)
    .map((a) => `, ${a.change === 'regain' ? '−' : '+'}${a.amount} ${alsoName(a)}`)
    .join('');
  /** The first reason the client can already see that one of the other pools will not take the change; the service stays the authority. */
  const alsoReason = (): string | undefined => {
    for (const a of alsoChange) {
      if (a.amount == null) return 'Cost unavailable';
      const p = alsoPool(a);
      if (!p || a.change !== 'spend' || p.current === undefined) continue;
      if (p.countsUp ? p.maximum !== undefined && p.spent + a.amount > p.maximum : p.current < a.amount) {
        return p.countsUp ? `${p.label} is full (${p.spent} of ${p.maximum}).` : `Not enough ${p.label} left (${p.current}).`;
      }
    }
    return undefined;
  };
  // A compound use is named by `rollId`; without it there is nothing to send (never a plain spend for such a roll).
  const useReason = locked
    ? (effect.unavailableReason ?? 'Not available right now')
    : compound && !effect.rollId
      ? "Can't be used here"
      : costFailed
      ? 'Cost unavailable'
      : useCost < 1
        ? 'Costs nothing here'
        : unaffordable
          ? pool?.current === undefined
            ? `${poolName} is not tracked on this sheet.`
            : drawsDown
              ? `Not enough ${poolName} built up (${pool.spent}).`
              : `Not enough ${poolName} left (${pool.current}).`
          : alsoReason();
  const rollDescribedBy = [locked ? reasonId : undefined, perUnit && !amountOk ? amountReasonId : undefined].filter(Boolean).join(' ') || undefined;
  return (
    <li className="feature">
      {diceless ? (
        effect.resourceId ? (
          <button
            type="button"
            disabled={useReason !== undefined}
            aria-describedby={useReason !== undefined ? reasonId : undefined}
            onClick={() => {
              // D43 S8: a use that also changes another pool, or draws a pool down, is one compound change on the service.
              if (compound) {
                if (effect.rollId) act({ action: 'useAction', contentId: feature.content.contentId, rollId: effect.rollId });
                return;
              }
              act({ action: 'spend', amount: useCost, contentId: effect.resourceContent ?? feature.content.contentId, resourceId: effect.resourceId! });
            }}
          >
            Use {label}
            {costFailed ? '' : ` (${useCost} ${poolName}${alsoText})`}
          </button>
        ) : (
          <span className="option-name">{label}</span>
        )
      ) : (
        <button
          type="button"
          disabled={locked || (perUnit !== undefined && !amountOk)}
          aria-describedby={rollDescribedBy}
          onClick={() => roll({ content: feature.content, effectId: effect.id, critical, ...(perUnit ? { amount: chosen } : {}) })}
        >
          Roll {label}
          {` (${effect.dice}${rollBonus(effect.bonus)})`}
        </button>
      )}{' '}
      <span className="hint">
        {feature.name}
        {effect.resourceId && !diceless ? ' · uses a resource (spend it separately)' : ''}
      </span>
      {(locked || (diceless && useReason !== undefined)) && (
        <span id={reasonId} className="hint">
          {' '}
          · {locked ? (effect.unavailableReason ?? 'Not available right now') : useReason}
        </span>
      )}
      {diceless && !effect.resourceId && effect.text && <span className="hint"> · {effect.text}</span>}
      {details && <p className="hint">{details}</p>}
      {perUnit && (
        <div className="inline-form">
          <label className="field">
            Points to spend{pool ? ` (${pool.label})` : ''}
            <input
              type="number"
              min={baseCost}
              max={maxAmount}
              value={amount}
              aria-invalid={amountOk ? undefined : true}
              aria-describedby={amountOk ? undefined : amountReasonId}
              onChange={(e) => setAmount(e.target.value)}
            />
          </label>
          {!amountOk && (
            <span id={amountReasonId} className="hint">
              {amountReason}
            </span>
          )}
        </div>
      )}
    </li>
  );
}

function AttackItem({
  attack,
  critical,
  rollMode,
  roll,
  rollDamage,
  pool,
}: {
  attack: AttackEntry;
  critical: boolean;
  rollMode: RollMode;
  roll: (target: RollTarget) => void;
  rollDamage: (target: RollTarget, type?: string) => void;
  /** The pool a feature attack spends from, to name it in the cost line. */
  pool?: ResourceValue;
}) {
  const reasonId = useId();
  // D43 slice 4: a feature attack is rolled by the content that gives it and its effect id; a weapon by its item.
  const feature = attack.source === 'feature';
  const target: RollTarget = feature ? { content: attack.item, effectId: attack.effectId } : { weapon: attack.item };
  // An unavailable attack is a disabled button whose reason is text beside it, not a tooltip.
  const locked = attack.available === false;
  const described = locked ? reasonId : undefined;
  return (
    <li className="feature">
      <details>
        <summary>
          <span className="option-name">{attack.name}</span>: {signed(attack.toHit)} to hit, {attack.damage ? `${attack.damage} ` : ''}{attack.damageType}
          {attack.versatileDamage ? ` (${attack.versatileDamage} two-handed)` : ''}
          {attack.automation !== 'automatic' && <span className="warn"> {automationLabels[attack.automation]}</span>}
        </summary>
        <p className="hint">
          {feature ? `${attack.attack === 'ranged' ? 'Ranged' : 'Melee'} attack` : `${attack.category} ${attack.attack} weapon`}
          {attack.range ? ` · range ${attack.range}` : ''}
          {attack.properties.length > 0 ? ` · ${attack.properties.join(', ')}` : ''}
          {attack.mastery ? ` · mastery: ${attack.mastery}` : ''}
          {feature && attack.resourceId ? ` · uses ${attack.cost ?? 1} ${pool?.label ?? 'of a resource'} (spend it from the roll)` : ''}
        </p>
        <ul aria-label={`How the ${attack.name} attack is calculated`}>
          {attack.trace.map((step) => (
            <li key={step.order}>
              {/* A step with no amount (damage dice, a rider that lost) is the sentence alone: no invented "+0". */}
              {typeof step.amount === 'number' ? `${step.description}: ${signed(step.amount)} = ${signed(step.result)}` : step.description}
            </li>
          ))}
        </ul>
        {attack.warnings.length > 0 && (
          <ul className="warnings" aria-label={`${attack.name} warnings`}>
            {attack.warnings.map((w) => (
              <li key={`${w.code}-${w.effectId ?? ''}`}>{w.message}</li>
            ))}
          </ul>
        )}
      </details>
      {feature && attack.replacesAttack && <p className="hint">replaces one attack of the Attack action</p>}
      <button type="button" disabled={locked} aria-describedby={described} onClick={() => roll({ ...target, mode: rollMode })}>
        Roll {attack.name} attack
      </button>
      <button type="button" disabled={locked} aria-describedby={described} onClick={() => rollDamage({ ...target, damage: true, critical }, attack.damageType)}>
        Roll {attack.name} damage{attack.damage ? ` (${attack.damage})` : ''}
      </button>
      {locked && (
        <span id={reasonId} className="hint">
          {' '}
          {attack.unavailableReason ?? 'Not available right now'}
        </span>
      )}
      {attack.versatileDamage && (
        <button type="button" onClick={() => rollDamage({ weapon: attack.item, damage: true, versatile: true, critical }, attack.damageType)}>
          Roll {attack.name} two-handed damage ({attack.versatileDamage})
        </button>
      )}
    </li>
  );
}

export function FeaturesPanel({
  view,
  pdfSources,
  openPage,
  reportGap,
}: {
  view: CharacterView;
  /** Sources with an available PDF (ADR-005); their cited pages can be opened. */
  pdfSources: ReadonlySet<string>;
  openPage: (sourceId: string, page: number) => void;
  /** M3 C5: pre-fills the gap note form with this feature. */
  reportGap?: (contentId: string) => void;
}) {
  const features = view.sheet.features ?? [];
  if (features.length === 0) return null;
  return (
    <section aria-labelledby="features-heading" className="play-panel">
      <h3 id="features-heading">Features</h3>
      {groupFeatures(features).map(({ title, items }, index) => {
        return (
          <section key={title} aria-labelledby={`features-${index}`}>
            <h4 id={`features-${index}`}>{title}</h4>
            <ul className="features">
              {items.map((feature) => (
                <FeatureItem
                  key={feature.content.revisionId}
                  feature={feature}
                  openPage={feature.origin.sourceId && feature.origin.page && pdfSources.has(feature.origin.sourceId) ? openPage : undefined}
                  reportGap={reportGap}
                />
              ))}
            </ul>
          </section>
        );
      })}
    </section>
  );
}

function FeatureItem({
  feature,
  openPage,
  reportGap,
}: {
  feature: FeatureEntry;
  openPage?: (sourceId: string, page: number) => void;
  reportGap?: (contentId: string) => void;
}) {
  const texts = feature.effects.filter((e) => e.text);
  return (
    <li className="feature">
      <details>
        <summary>
          <span className="option-name">{feature.name}</span> <span className="tag">{feature.kind}</span>{' '}
          <span className={feature.automation === 'automatic' ? 'hint' : 'warn'}>{automationLabels[feature.automation]}</span>
        </summary>
        <p className="hint">
          {feature.origin.sourceTitle}
          {feature.origin.page ? `, ${pageText(feature.origin.page)}` : ''}
          {feature.via ? ` · ${feature.via}` : ''}
        </p>
        {feature.summary && <p>{feature.summary}</p>}
        {texts.length > 0 && (
          <ul>
            {texts.map((effect) => (
              <li key={effect.id}>
                {effect.text} <span className="tag">{automationLabels[effect.automation]}</span>
              </li>
            ))}
          </ul>
        )}
        {feature.diagnostics.length > 0 && (
          <ul className="warnings" aria-label={`${feature.name} problems`}>
            {feature.diagnostics.map((d) => (
              <li key={`${d.code}-${d.effectId ?? ''}`}>{d.message}</li>
            ))}
          </ul>
        )}
      </details>
      {openPage && feature.origin.sourceId && feature.origin.page && (
        <button type="button" onClick={() => openPage(feature.origin.sourceId!, feature.origin.page!.start)}>
          Open {feature.name}, {pageText(feature.origin.page)}
        </button>
      )}
      {reportGap && (
        <button type="button" onClick={() => reportGap(feature.content.contentId)}>
          Report a gap: {feature.name}
        </button>
      )}
    </li>
  );
}

/** ADR-015: at most this many dice are drawn; the record's text always lists every die. */
const drawnDice = 10;

/**
 * A key per record object, so an identical second roll (a new object with the same faces) mounts a new dice row and the
 * CSS tumble runs again. A WeakMap keeps it pure: no state, no effect, and old records are collected.
 */
const rollKeys = new WeakMap<RollRecord, number>();
let nextRollKey = 0;
function rollKey(record: RollRecord): number {
  let key = rollKeys.get(record);
  if (key === undefined) rollKeys.set(record, (key = ++nextRollKey));
  return key;
}

/** The last roll's record (SPEC C-04): formula, every die, modifiers and provenance. Announced politely. */
export function RollResult({
  record,
  resources,
  features,
  attacks = [],
  act,
}: {
  record?: RollRecord;
  resources: ResourceValue[];
  features: FeatureEntry[];
  /** D43 slice 4: a feature attack's cost is on its sheet row, not on a feature effect. */
  attacks?: AttackEntry[];
  act: Act;
}) {
  // The typed amount belongs to the roll it was typed for, so a new roll never inherits it.
  const [typed, setTyped] = useState<{ record?: RollRecord; value: string }>({ value: '' });
  const amount = typed.record === record ? typed.value : '';
  const setAmount = (value: string) => setTyped({ record, value });
  // A record's Spend is used once: the record it was used for stays spent, and a new record starts fresh.
  const [spentFor, setSpentFor] = useState<RollRecord>();
  const spentAlready = record !== undefined && spentFor === record;
  const cantUseId = useId();
  /** Holds the button while the change is under way, and gives it back when the change is refused so it can be retried. */
  function spendOnce(action: PlayAction, refused?: () => void) {
    setSpentFor(record);
    void Promise.resolve(act(action))
      .then((applied) => applied !== false) // a caller that returns nothing is taken to have applied it
      .catch(() => false)
      .then((applied) => {
        if (applied) return;
        setSpentFor((current) => (current === record ? undefined : current));
        refused?.();
      });
  }
  // Toggling aria-live on a mounted region is unreliable in some screen readers; the "Announce each roll" setting takes
  // full effect on the next sheet open, which the Settings hint says.
  if (!record) return <div role="region" aria-label="Last roll" aria-live={announceRollsOn() ? 'polite' : undefined} className="roll-result" />;
  const p = record.provenance;
  // A roll may name a resource its action spends; spending is a separate, explicit button (never automatic). A shared
  // resource (content v6) is found through the content that defines it.
  // D43 S5: `record.spend` (set by the service for a roll effect that names a pool) says exactly which pool and how much.
  const spend = record.spend;
  const holder = spend?.contentId ?? p?.linkedResourceContent ?? p?.content?.contentId;
  const linkedId = spend?.resourceId ?? p?.linkedResourceId;
  const linked = linkedId
    ? (resources.find((r) => r.resourceId === linkedId && r.content.contentId === holder) ?? (spend ? undefined : resources.find((r) => r.resourceId === linkedId)))
    : undefined;
  const effect = features.find((f) => f.content.revisionId === p?.content?.revisionId)?.effects.find((e) => e.id === p?.effectId);
  const featureAttack = attacks.find((a) => a.source === 'feature' && a.item.revisionId === p?.content?.revisionId && a.effectId === p?.effectId);
  // The service leaves `spend` out of a content roll effect's record exactly when its cost formula failed: never guess 1 then.
  const costUnavailable = effect !== undefined && featureAttack === undefined && spend === undefined && linkedId !== undefined;
  const cost = spend?.amount ?? featureAttack?.cost ?? effect?.cost ?? 1;
  // D43 S8: a roll that draws a counting-up pool down, or also changes another pool, is spent as one `useAction` (a plain `spend`
  // would build the charge up instead). The roll is named by the record's own `rollId`; a draw-down is bounded by what has built up.
  const drawsDown = spend?.drawsDown === true || effect?.drawsDown === true;
  const useRollId = p?.rollId ?? effect?.rollId;
  const compound = drawsDown || (effect?.alsoChange?.length ?? 0) > 0;
  // Never a plain `spend` for such a roll: without the roll's content, its id or its effect, the Spend is held with a reason.
  const cantUse = compound && (useRollId === undefined || p?.content?.contentId === undefined || effect === undefined);
  const spendAction = (amount: number, resourceContent: string, resourceId: string): PlayAction | undefined =>
    cantUse
      ? undefined
      : compound
        ? { action: 'useAction', contentId: p!.content!.contentId, rollId: useRollId!, ...(effect?.perUnit ? { amount } : {}) }
        : { action: 'spend', amount, contentId: resourceContent, resourceId };
  const cantUseText = "Can't be used here";
  const stock = linked ? (drawsDown ? linked.spent : linked.current) : undefined;
  const stockText = linked ? (drawsDown ? `${linked.spent} built up` : `${linked.current} left`) : '';
  const most = linked?.current !== undefined && stock !== undefined ? Math.min(cost, stock) : 0;
  const chosen = Number(amount);
  const validChoice = amount !== '' && Number.isInteger(chosen) && chosen >= 1 && chosen <= most;
  return (
    <div role="region" aria-label="Last roll" aria-live={announceRollsOn() ? 'polite' : undefined} className="roll-result">
      {/* ADR-015: the service's dice, drawn. aria-hidden and textless (faces come from CSS attr()), so the text below is
          the result for everyone and the region announces once. Nothing is rolled here. */}
      <div key={rollKey(record)} className={diceAnimationOn() ? 'dice' : 'dice dice-still'} aria-hidden="true">
        {record.dice.slice(0, drawnDice).map((d, i) => (
          <span
            key={i}
            className="die"
            data-sides={d.sides}
            data-value={d.value}
            data-kept={d.kept ? 'true' : 'false'}
            data-critical={d.fromCritical ? 'true' : 'false'}
          />
        ))}
        {record.dice.length > drawnDice && <span className="die-more" data-more={record.dice.length - drawnDice} />}
      </div>
      <p>
        <strong>
          {p?.label || 'Roll'}: {record.total}
        </strong>{' '}
        ({record.formula}
        {rollModeText(record)}
        {record.critical ? ', critical' : ''})
      </p>
      <p className="hint">
        {diceLine(record)}
        {p?.sourceTitle ? ` · ${p.contentName ?? ''} (${p.sourceTitle}${p.page ? `, ${pageText(p.page)}` : ''})` : ''}
      </p>
      {costUnavailable && <p className="hint">Cost unavailable</p>}
      {!costUnavailable && linked && linked.current !== undefined && !effect?.variableCost && (
        <button
          type="button"
          disabled={spentAlready || cantUse || cost < 1 /* a cost formula can evaluate to 0 at some levels */ || (stock ?? 0) < cost}
          aria-describedby={cantUse ? cantUseId : undefined}
          onClick={() => {
            const action = spendAction(cost, spend?.contentId ?? linked.content.contentId, spend?.resourceId ?? linked.resourceId);
            if (action) spendOnce(action);
          }}
        >
          Spend {cost} {linked.label} ({stockText})
        </button>
      )}
      {!costUnavailable && linked && linked.current !== undefined && effect?.variableCost && (
        <div className="inline-form">
          <label className="field">
            {linked.label} to spend (1 to {most})
            <input type="number" min={1} max={most} value={amount} onChange={(e) => setAmount(e.target.value)} />
          </label>
          <button
            type="button"
            disabled={spentAlready || cantUse || !validChoice}
            aria-describedby={cantUse ? cantUseId : undefined}
            onClick={() => {
              const action = spendAction(chosen, linked.content.contentId, linked.resourceId);
              if (!action) return;
              const typedAmount = amount;
              spendOnce(action, () => setTyped((t) => (t.record === record && t.value === '' ? { record, value: typedAmount } : t)));
              setAmount('');
            }}
          >
            Spend {validChoice ? chosen : ''} {linked.label} ({stockText})
          </button>
        </div>
      )}
      {!costUnavailable && linked && linked.current !== undefined && cantUse && (
        <span id={cantUseId} className="hint">
          {cantUseText}
        </span>
      )}
    </div>
  );
}

export function RollModePicker({ mode, onChange }: { mode: RollMode; onChange: (mode: RollMode) => void }) {
  return (
    <fieldset className="roll-mode" aria-describedby="roll-mode-hint">
      <legend>d20 rolls</legend>
      <span id="roll-mode-hint" className="visually-hidden">
        Automatic modes from the sheet are combined with this choice; advantage and disadvantage cancel.
      </span>
      {(['normal', 'advantage', 'disadvantage'] as const).map((m) => (
        <label key={m} className="choice">
          <input type="radio" name="roll-mode" checked={mode === m} onChange={() => onChange(m)} />
          {m === 'normal' ? 'Normal' : m === 'advantage' ? 'Advantage' : 'Disadvantage'}
        </label>
      ))}
    </fieldset>
  );
}
