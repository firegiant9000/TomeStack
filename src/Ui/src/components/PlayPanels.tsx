import { useEffect, useId, useRef, useState, type SubmitEvent } from 'react';
import type {
  Ability,
  Activation,
  AttackEntry,
  EquipmentEntry,
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
import { attackAvailability, dicelessUse, hasDice, poolFor, recordPayment, recordSpend, rollAvailability, toggleAvailability, type Availability } from '../actionRows';
import { castsText, paying, paymentText } from '../payment';
import { explainSteps } from '../explain';
import { hitDiceText, rollBonus } from '../format';
import { attackPin, customPin, maxPins, pinKey, rollPin, togglePin, type Pin } from '../quickbar';
import { rollButtonSuffix } from '../rollModes';
import { diceLine, rollKey, rollModeText } from '../rollText';
import { announceRollsOn, diceAnimationOn } from '../settings';
import { damageTypeLabel, damageTypes, normalizeDamageType } from './customModel';
import { ExplainList } from './ExplainList';
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
      <ExplainList label={`${resource.label} maximum`} steps={resource.trace} />
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
function ToggleRow({ toggle: t, act, availability, pinning }: { toggle: ToggleValue; act: Act; availability: Availability; pinning?: Pinning }) {
  const reasonId = useId();
  const reason = availability.reasons.join(' ');
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
  // MECH-X item 1 (R40): only switching a paying toggle ON waits on whether it can be paid; switching it off never does.
  const payment = paying(t.payment) ? t.payment : undefined;
  const held = !t.on && payment?.payable === false;
  return (
    <>
      <label className="choice">
        <input
          type="checkbox"
          checked={t.on}
          disabled={held}
          aria-describedby={reason ? reasonId : undefined}
          onChange={() => act({ action: t.on ? 'toggleOff' : 'toggleOn', contentId: t.content.contentId, toggleId: t.toggleId })}
        />
        {t.label}
        <span className="option-source">
          {t.contentName}
          {t.resourceId ? ' · turning it on spends 1 use' : ''}
          {payment ? ` · Costs ${paymentText(payment, t.cost)}` : ''}
          {t.text ? ` · ${t.text}` : ''}
        </span>
      </label>
      {reason && (
        <span id={reasonId} className="hint">
          {' '}
          {reason}
        </span>
      )}
      {pinning && <PinButton {...pinning} />}
    </>
  );
}

/** D73: how a row is pinned to the quickbar; absent when the panel is not given `onPin`. */
type Pinning = { name: string; pinned: boolean; full: boolean; onPin: () => void };

/** Pin / Unpin at the end of a row. The name stays the same in both states (`aria-pressed` says which); at the limit a new pin is refused, not hidden. */
function PinButton({ name, pinned, full, onPin }: Pinning) {
  const hintId = useId();
  const blocked = full && !pinned;
  return (
    <>
      {' '}
      <button type="button" aria-label={`Pin ${name} to the quickbar`} aria-pressed={pinned} aria-disabled={blocked ? true : undefined} aria-describedby={blocked ? hintId : undefined} onClick={() => !blocked && onPin()}>
        {pinned ? 'Unpin' : 'Pin'}
      </button>
      {blocked && (
        <span id={hintId} className="hint">
          {' '}
          The quickbar holds {maxPins} actions.
        </span>
      )}
    </>
  );
}

/**
 * SPEC C-04: attacks and feature rolls grouped by action, bonus action, reaction and other. A roll from the panel never spends; a quickbar
 * press rolls and then spends, once (D73). A roll that names a resource offers "Spend" in its record.
 */
export function ActionsPanel({
  view,
  roll,
  act,
  rollMode = 'normal',
  onDamageType,
  pins = [],
  onPin,
}: {
  view: CharacterView;
  roll: (target: RollTarget) => void;
  act: Act;
  /** D73: the visible, resolved quickbar pins; a row whose pin is among them shows "Unpin". */
  pins?: Pin[];
  /** D73: pins the row's action, or unpins it when already pinned. Without it no Pin buttons show. */
  onPin?: (pin: Pin) => void;
  /** The summary's advantage / disadvantage choice. Used by the attack rolls of weapons and of the character's own attacks (R93); damage is always normal. */
  rollMode?: RollMode;
  /** Wave 3 item 21: a damage roll pre-fills the Hit points damage type with the attack's type ('' when it names none of the thirteen). */
  onDamageType?: (type: string) => void;
}) {
  const [critical, setCritical] = useState(false);
  const [usableOnly, setUsableOnly] = useState(false);
  // D73: a click can empty the pool and so unmount its own row under "Usable now"; focus then goes to the filter checkbox, not <body>.
  const usableRef = useRef<HTMLInputElement>(null);
  // The row control last clicked while the filter was on. Armed until that control has left the page: the action's reply arrives after
  // other renders of the same view (the sheet's in-flight state), so spending it on the first render would miss the row being hidden.
  const refocusFrom = useRef<HTMLElement | null>(null);
  useEffect(() => {
    const el = refocusFrom.current;
    if (!el) return;
    if (!usableOnly) {
      refocusFrom.current = null;
      return;
    }
    if (el.isConnected) return;
    refocusFrom.current = null;
    const active = document.activeElement;
    // Focus lost to <body> means the row just used was hidden; a real element anywhere else was chosen, so leave it.
    if (active === null || active === document.body) usableRef.current?.focus();
  });
  const disarmUnless = (target: EventTarget) => {
    const armed = refocusFrom.current;
    if (armed && !(target instanceof Node && armed.contains(target))) refocusFrom.current = null;
  };
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
  const allowanceOf = (resourceId: string | undefined, contentId: string) => poolFor(allowances, resourceId, contentId);
  const togglePool = (t: ToggleValue) => allowanceOf(t.resourceId, t.costResourceContent ?? t.content.contentId);
  if (attacks.length === 0 && customAttacks.length === 0 && rolls.length === 0 && plainToggles.length === 0) return null;
  // D73: "Usable now" hides what the sheet can see is not usable; a by-hand row and a pinned one (so it can be unpinned) always stay.
  const pinned = (pin: Pin) => pins.some((p) => pinKey(p) === pinKey(pin));
  const pinning = (pin: Pin | undefined, name: string): Pinning | undefined =>
    onPin && pin ? { name, pinned: pinned(pin), full: pins.length >= maxPins, onPin: () => onPin(pin) } : undefined;
  const listed = (a: Availability, pin?: Pin) => !usableOnly || a.usable || a.byHand === true || (pin !== undefined && pinned(pin));
  const rollRows = rolls
    .map(({ feature, effect }) => ({
      feature,
      effect,
      availability: rollAvailability(feature, effect, poolFor(resourcePools, effect.resourceId, effect.resourceContent ?? feature.content.contentId), resourcePools),
      // A roll with nothing to roll and nothing to spend is only text.
      // (A paying roll with nothing to roll has no resource, so it is not pinned either: a tap would pay, R37.)
      pin: hasDice(effect) || effect.resourceId !== undefined ? rollPin(feature, effect) : undefined,
    }))
    .filter((r) => listed(r.availability, r.pin));
  const attackRows = attacks
    .map((attack) => ({ attack, availability: attackAvailability(attack, poolFor(resourcePools, attack.resourceId ?? undefined, attack.costResourceContent ?? undefined)), pin: attackPin(attack) }))
    .filter((r) => listed(r.availability, r.pin));
  const toggleRows = plainToggles
    .map((t) => ({ toggle: t, availability: toggleAvailability(t, poolFor(resourcePools, t.resourceId, t.costResourceContent ?? t.content.contentId)), pin: t.auto || paying(t.payment) ? undefined : togglePin(t) }))
    .filter((r) => listed(r.availability, r.pin));
  const rowPool = ({ feature, effect }: (typeof rollRows)[number]) => allowanceOf(effect.resourceId, effect.resourceContent ?? feature.content.contentId);
  const poolGroups = allowances
    .map((pool) => ({ pool, rolls: rollRows.filter((r) => rowPool(r) === pool), toggles: toggleRows.filter((r) => togglePool(r.toggle) === pool) }))
    .filter((g) => g.rolls.length > 0 || g.toggles.length > 0);
  // Wave 3 item 22 (R157): a toggle with an activation is listed in that group; one without stays in "Active effects".
  const ungroupedToggles = toggleRows.filter((r) => !r.toggle.activation && !togglePool(r.toggle));
  const rollItem = (r: (typeof rollRows)[number]) => (
    <RollItem
      key={`${r.feature.content.revisionId}-${r.effect.id}`}
      feature={r.feature}
      effect={r.effect}
      pool={poolFor(resourcePools, r.effect.resourceId, r.effect.resourceContent ?? r.feature.content.contentId)}
      pools={resourcePools}
      availability={r.availability}
      pinning={pinning(r.pin, r.effect.label || r.effect.id)}
      critical={critical}
      roll={roll}
      act={act}
    />
  );
  const toggleItem = (r: (typeof toggleRows)[number]) => (
    <li key={`${r.toggle.content.revisionId}-${r.toggle.toggleId}`} className="feature">
      <ToggleRow toggle={r.toggle} act={act} availability={r.availability} pinning={pinning(r.pin, r.toggle.label)} />
    </li>
  );
  const nothingListed = attackRows.length === 0 && customAttacks.length === 0 && rollRows.length === 0 && toggleRows.length === 0;
  return (
    <section
      aria-labelledby="actions-heading"
      className="play-panel"
      onClickCapture={(e) => {
        // Any row control (Roll, Use, Pin, a switch) may hide its own row; the filter checkbox itself and plain inputs do not count.
        const control = e.target instanceof Element ? e.target.closest('button, input[type="checkbox"]') : null;
        if (usableOnly && control && control !== usableRef.current) refocusFrom.current = control as HTMLElement;
      }}
      // The refocus is for the control just used. A pointer or focus move to anything else in the panel means the user has moved on, so a
      // row that left later (another action emptied its pool) must not pull focus back to "Usable now".
      onPointerDownCapture={(e) => disarmUnless(e.target)}
      onFocusCapture={(e) => disarmUnless(e.target)}
    >
      <h3 id="actions-heading">Attacks and actions</h3>
      {ungroupedToggles.length > 0 && (
        <fieldset>
          <legend>Active effects (a switch is a confirmed change)</legend>
          {ungroupedToggles.map((r) => (
            <ToggleRow key={`${r.toggle.content.revisionId}-${r.toggle.toggleId}`} toggle={r.toggle} act={act} availability={r.availability} pinning={pinning(r.pin, r.toggle.label)} />
          ))}
        </fieldset>
      )}
      <label className="choice">
        <input type="checkbox" checked={critical} onChange={(e) => setCritical(e.target.checked)} />
        Critical hit (double the damage dice)
      </label>
      <label className="choice">
        <input ref={usableRef} type="checkbox" checked={usableOnly} onChange={(e) => setUsableOnly(e.target.checked)} />
        Usable now
      </label>
      <p className="hint" aria-live="polite">
        {usableOnly && nothingListed ? 'Nothing is usable right now.' : ''}
      </p>
      {activationGroups.map(({ key, title }) => {
        // Weapons stay in 'action'; a feature attack goes where its activation says (default 'action').
        const groupAttacks = attackRows.filter((r) => (r.attack.source === 'feature' ? (r.attack.activation ?? 'action') : 'action') === key);
        const groupCustom = key === 'action' ? customAttacks : [];
        const groupRolls = rollRows.filter((r) => (r.effect.activation ?? 'other') === key && !rowPool(r));
        const groupToggles = toggleRows.filter((r) => r.toggle.activation === key && !togglePool(r.toggle));
        if (groupAttacks.length === 0 && groupCustom.length === 0 && groupRolls.length === 0 && groupToggles.length === 0) return null;
        return (
          <section key={key} aria-labelledby={`actions-${key}`}>
            <h4 id={`actions-${key}`}>{title}</h4>
            <ul className="features">
              {groupAttacks.map(({ attack, availability, pin }) => (
                <AttackItem
                  key={`${attack.item.revisionId}-${attack.effectId}-${attack.entryIndex ?? ''}`}
                  attack={attack}
                  label={attackLabel(attack, view.character.equipment ?? [])}
                  critical={critical}
                  rollMode={rollMode}
                  roll={roll}
                  rollDamage={rollDamage}
                  pool={poolFor(resourcePools, attack.resourceId ?? undefined, attack.costResourceContent ?? undefined)}
                  availability={availability}
                  pinning={pinning(pin, attackLabel(attack, view.character.equipment ?? []))}
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
                  {onPin && <PinButton {...pinning(customPin(attack), attack.name)!} />}
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
  availability,
  pinning,
  critical,
  roll,
  act,
}: {
  feature: FeatureEntry;
  effect: FeatureEffect;
  pool?: ResourceValue;
  /** Every pool on the sheet: an `alsoChange` entry can name another one. */
  pools: ResourceValue[];
  /** D73: what the sheet can see about using this row now; its reasons are shown as text the main control is described by. */
  availability: Availability;
  pinning?: Pinning;
  critical: boolean;
  roll: (target: RollTarget) => void;
  act: Act;
}) {
  const baseId = `${feature.content.revisionId}-${effect.id}`;
  const baseCost = effect.cost ?? 0;
  // An untracked pool has no points to choose from: the roll goes without an amount and the service charges the base cost.
  // MECH-X item 1: a per-unit roll that pays hit points has no pool; the points it pays are still the player's to choose.
  const payment = paying(effect.payment) ? effect.payment : undefined;
  const rolled = hasDice(effect);
  const perUnit = rolled && (pool?.current !== undefined || payment?.kind === 'hitPoints') ? effect.perUnit : undefined;
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
  const diceless = !rolled;
  // D73: the Use (its reason, its cost text and the action it sends) lives in actionRows.ts, shared with the quickbar.
  const { cost: useCost, costFailed, poolName, alsoText, reason: useReason, action: useAction, payText } = dicelessUse(feature, effect, pool, pools);
  // D73: a roll with dice that the pool can't pay stays enabled (the cost is spent from its record) but says so; a locked one reuses the lock's reason.
  const needsId = `roll-needs-${baseId}`;
  const needs = !diceless && !locked ? availability.reasons.join(' ') : '';
  const rollDescribedBy =
    [locked ? reasonId : undefined, needs ? needsId : undefined, perUnit && !amountOk ? amountReasonId : undefined].filter(Boolean).join(' ') || undefined;
  return (
    <li className="feature">
      {diceless ? (
        effect.resourceId || payment ? (
          <button
            type="button"
            disabled={useReason !== undefined}
            aria-describedby={useReason !== undefined ? reasonId : undefined}
            onClick={() => {
              if (useAction) act(useAction);
            }}
          >
            Use {label}
            {payText ? ` (${payText})` : costFailed ? '' : ` (${useCost} ${poolName}${alsoText})`}
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
          {` (${effect.dice ?? effect.castsSpell?.dice}${rollBonus(effect.bonus)})`}
        </button>
      )}{' '}
      <span className="hint">
        {feature.name}
        {effect.resourceId && !diceless ? ' · uses a resource (spend it separately)' : ''}
        {payment && !diceless ? ` · Costs ${paymentText(payment, effect.cost)} (pay it from the roll)` : ''}
      </span>
      {(locked || (diceless && useReason !== undefined)) && (
        <span id={reasonId} className="hint">
          {' '}
          · {locked ? (effect.unavailableReason ?? 'Not available right now') : useReason}
        </span>
      )}
      {needs && (
        <span id={needsId} className="hint">
          {' '}
          · {needs}
        </span>
      )}
      {diceless && !effect.resourceId && !payment && effect.text && <span className="hint"> · {effect.text}</span>}
      {effect.castsSpell && <p className="hint">{castsText(effect.castsSpell)}</p>}
      {details && <p className="hint">{details}</p>}
      {effect.saveDc !== undefined && effect.saveTrace && (
        <ExplainList label={`${label} save DC`} steps={effect.saveTrace} summary={`Why DC ${effect.saveDc}?`} />
      )}
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
      {pinning && <PinButton {...pinning} />}
    </li>
  );
}

/**
 * MECH-X item 3: a weapon carried as several copies has one row per copy (`entryIndex`, the equipment index). The copies are numbered
 * exactly as the Inventory numbers them: every equipment entry of the item, in list order, equipped or not.
 */
function attackLabel(attack: AttackEntry, equipment: EquipmentEntry[]): string {
  if (attack.entryIndex === undefined || attack.entryIndex === null) return attack.name;
  const copies = equipment.map((e, i) => ({ e, i })).filter(({ e }) => e.item.contentId === attack.item.contentId && e.item.revisionId === attack.item.revisionId);
  const n = copies.findIndex(({ i }) => i === attack.entryIndex) + 1;
  return copies.length > 1 && n > 0 ? `${attack.name} (copy ${n} of ${copies.length})` : attack.name;
}

function AttackItem({
  attack,
  label,
  critical,
  rollMode,
  roll,
  rollDamage,
  pool,
  availability,
  pinning,
}: {
  attack: AttackEntry;
  /** The name shown and used in control names: the attack's, with its copy number when the weapon is carried several times. */
  label: string;
  critical: boolean;
  rollMode: RollMode;
  roll: (target: RollTarget) => void;
  rollDamage: (target: RollTarget, type?: string) => void;
  /** The pool a feature attack spends from, to name it in the cost line. */
  pool?: ResourceValue;
  /** D73: a feature attack the pool can't pay stays enabled and says so. */
  availability: Availability;
  pinning?: Pinning;
}) {
  const reasonId = useId();
  const needsId = useId();
  // D43 slice 4: a feature attack is rolled by the content that gives it and its effect id; a weapon by its item.
  const feature = attack.source === 'feature';
  const target: RollTarget = feature ? { content: attack.item, effectId: attack.effectId } : { weapon: attack.item };
  // An unavailable attack is a disabled button whose reason is text beside it, not a tooltip.
  const locked = attack.available === false;
  const needs = locked ? '' : availability.reasons.join(' ');
  const described = locked ? reasonId : needs ? needsId : undefined;
  return (
    <li className="feature">
      <details>
        <summary>
          <span className="option-name">{label}</span>: {signed(attack.toHit)} to hit, {attack.damage ? `${attack.damage} ` : ''}{attack.damageType}
          {attack.versatileDamage ? ` (${attack.versatileDamage} two-handed)` : ''}
          {attack.automation !== 'automatic' && <span className="warn"> {automationLabels[attack.automation]}</span>}
        </summary>
        <p className="hint">
          {feature ? `${attack.attack === 'ranged' ? 'Ranged' : 'Melee'} attack` : `${attack.category} ${attack.attack} weapon`}
          {attack.range ? ` · range ${attack.range}` : ''}
          {attack.properties.length > 0 ? ` · ${attack.properties.join(', ')}` : ''}
          {attack.mastery ? ` · mastery: ${attack.mastery}` : ''}
          {feature && attack.resourceId ? ` · uses ${attack.cost ?? 1} ${pool?.label ?? 'of a resource'} (spend it from the roll)` : ''}
          {feature && paying(attack.payment) ? ` · Costs ${paymentText(attack.payment, attack.cost)} (pay it from the roll)` : ''}
        </p>
        <ul aria-label={`How the ${label} attack is calculated`}>
          {explainSteps(attack.trace, 'attack').map((sentence, i) => (
            <li key={i}>{sentence}</li>
          ))}
        </ul>
        {attack.warnings.length > 0 && (
          <ul className="warnings" aria-label={`${label} warnings`}>
            {attack.warnings.map((w) => (
              <li key={`${w.code}-${w.effectId ?? ''}`}>{w.message}</li>
            ))}
          </ul>
        )}
      </details>
      {feature && attack.replacesAttack && <p className="hint">replaces one attack of the Attack action</p>}
      <button type="button" disabled={locked} aria-describedby={described} onClick={() => roll({ ...target, mode: rollMode })}>
        Roll {label} attack
      </button>
      <button type="button" disabled={locked} aria-describedby={described} onClick={() => rollDamage({ ...target, damage: true, critical }, attack.damageType)}>
        Roll {label} damage{attack.damage ? ` (${attack.damage})` : ''}
      </button>
      {locked && (
        <span id={reasonId} className="hint">
          {' '}
          {attack.unavailableReason ?? 'Not available right now'}
        </span>
      )}
      {needs && (
        <span id={needsId} className="hint">
          {' '}
          {needs}
        </span>
      )}
      {attack.versatileDamage && (
        <button type="button" onClick={() => rollDamage({ weapon: attack.item, damage: true, versatile: true, critical }, attack.damageType)}>
          Roll {label} two-handed damage ({attack.versatileDamage})
        </button>
      )}
      {pinning && <PinButton {...pinning} />}
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

/** D73: which record's Spend is used, and the one way to spend (resolves to whether the change applied; a refusal gives the button back and calls `refused`). */
export type RecordSpent = {
  record?: RollRecord;
  spendOnce: (record: RollRecord, action: PlayAction, refused?: () => void) => Promise<boolean>;
};

/** The last roll's record (SPEC C-04): formula, every die, modifiers and provenance. Announced politely. */
export function RollResult({
  record,
  resources,
  features,
  attacks = [],
  act,
  spent,
}: {
  record?: RollRecord;
  resources: ResourceValue[];
  features: FeatureEntry[];
  /** D43 slice 4: a feature attack's cost is on its sheet row, not on a feature effect. */
  attacks?: AttackEntry[];
  act: Act;
  /** D73: the sheet's own "this record was spent" state and spender, shared with the quickbar so a press and this button spend once between them. Absent: kept here. */
  spent?: RecordSpent;
}) {
  // The typed amount belongs to the roll it was typed for, so a new roll never inherits it.
  const [typed, setTyped] = useState<{ record?: RollRecord; value: string }>({ value: '' });
  const amount = typed.record === record ? typed.value : '';
  const setAmount = (value: string) => setTyped({ record, value });
  // A record's Spend is used once: the record it was used for stays spent, and a new record starts fresh.
  const [ownSpentFor, setSpentFor] = useState<RollRecord>();
  const spentFor = spent ? spent.record : ownSpentFor;
  const spentAlready = record !== undefined && spentFor === record;
  const cantUseId = useId();
  const payReasonId = useId();
  /** Holds the button while the change is under way, and gives it back when the change is refused so it can be retried. */
  function spendOnce(action: PlayAction, refused?: () => void) {
    if (spent) {
      if (record) void spent.spendOnce(record, action, refused);
      return;
    }
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
  // D73: which pool a record spends, what it costs and the action that spends it are worked out in actionRows.ts.
  const { linked, cost, costUnavailable, cantUse, variableCost, stockText, most, spendAction, autoAction } = recordSpend(record, resources, features, attacks);
  // MECH-X item 1 (R37): hit points and slots are paid here, by a deliberate press, once per record through the same `spendOnce`.
  const payment = recordPayment(record, features, attacks);
  const cantUseText = "Can't be used here";
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
      {!costUnavailable && linked && linked.current !== undefined && !variableCost && (
        <button
          type="button"
          disabled={spentAlready || autoAction === undefined /* held, a cost of 0 (a cost formula can evaluate to 0 at some levels) or too little stock */}
          aria-describedby={cantUse ? cantUseId : undefined}
          onClick={() => {
            if (autoAction) spendOnce(autoAction);
          }}
        >
          Spend {cost} {linked.label} ({stockText})
        </button>
      )}
      {!costUnavailable && linked && linked.current !== undefined && variableCost && (
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
      {payment && (
        <>
          <button
            type="button"
            disabled={spentAlready || !payment.payable}
            aria-describedby={payment.reason ? payReasonId : undefined}
            onClick={() => spendOnce(payment.action)}
          >
            {payment.label}
          </button>
          {payment.note && <span className="hint"> {payment.note}</span>}
          {payment.reason && (
            <span id={payReasonId} className="hint">
              {' '}
              {payment.reason}
            </span>
          )}
        </>
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
