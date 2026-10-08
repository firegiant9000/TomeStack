import { useEffect, useRef, useState, type SubmitEvent } from 'react';
import type {
  Activation,
  AttackEntry,
  AutomationStatus,
  CharacterView,
  ContentKind,
  FeatureEntry,
  PageRef,
  PlayAction,
  ResourceValue,
  RollMode,
  RollRecord,
  RollTarget,
} from '../api/types';
import { hitDiceText, rollBonus } from '../format';
import { diceLine } from '../rollText';
import { announceRollsOn, diceAnimationOn } from '../settings';
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
type Act = (action: PlayAction) => void;

export function HitPointsPanel({ view, act }: { view: CharacterView; act: Act }) {
  const hp = view.sheet.hitPoints;
  const [amount, setAmount] = useState('');
  if (!hp) return null;
  const n = Number(amount);
  const valid = amount !== '' && Number.isInteger(n) && n >= 0;

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
  }

  function apply(action: PlayAction['action']) {
    if (!valid) return;
    act({ action, amount: n });
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
      <p className="hint">Temporary hit points absorb damage first. They do not stack: setting them replaces the old value.</p>
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

function ResourceCard({ resource, act }: { resource: ResourceValue; act: Act }) {
  const key = { contentId: resource.content.contentId, resourceId: resource.resourceId };
  const tracked = resource.current !== undefined && resource.maximum !== undefined;
  const headingId = `resource-${resource.content.revisionId}-${resource.resourceId}`;
  return (
    <li className="resource">
      <h4 id={headingId}>
        {resource.label}: {tracked ? `${resource.current} of ${resource.maximum}` : 'tracked by hand'}
        {tracked && <Pips filled={resource.current!} total={resource.maximum!} />}
      </h4>
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
  return (
    <section aria-labelledby="resources-heading" className="play-panel">
      <h3 id="resources-heading">Resources</h3>
      <ul className="resources">
        {resources.map((resource) => (
          <ResourceCard key={`${resource.content.revisionId}-${resource.resourceId}`} resource={resource} act={act} />
        ))}
      </ul>
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
 * SPEC C-04: attacks and feature rolls grouped by action, bonus action, reaction and other. Rolling never spends anything;
 * a roll that names a resource offers "Spend" in its record.
 */
export function ActionsPanel({ view, roll, act }: { view: CharacterView; roll: (target: RollTarget) => void; act: Act }) {
  const [critical, setCritical] = useState(false);
  const attacks = view.sheet.attacks ?? [];
  const toggles = view.sheet.toggles ?? [];
  const rolls = (view.sheet.features ?? []).flatMap((feature) =>
    feature.effects.filter((e) => e.type === 'roll').map((effect) => ({ feature, effect })),
  );
  if (attacks.length === 0 && rolls.length === 0 && toggles.length === 0) return null;
  return (
    <section aria-labelledby="actions-heading" className="play-panel">
      <h3 id="actions-heading">Attacks and actions</h3>
      {toggles.length > 0 && (
        <fieldset>
          <legend>Active effects (switching one is a confirmed change)</legend>
          {toggles.map((t) => (
            <label key={`${t.content.revisionId}-${t.toggleId}`} className="choice">
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
          ))}
        </fieldset>
      )}
      <label className="choice">
        <input type="checkbox" checked={critical} onChange={(e) => setCritical(e.target.checked)} />
        Critical hit (double the damage dice)
      </label>
      {activationGroups.map(({ key, title }) => {
        const groupAttacks = key === 'action' ? attacks : [];
        const groupRolls = rolls.filter(({ effect }) => (effect.activation ?? 'other') === key);
        if (groupAttacks.length === 0 && groupRolls.length === 0) return null;
        return (
          <section key={key} aria-labelledby={`actions-${key}`}>
            <h4 id={`actions-${key}`}>{title}</h4>
            <ul className="features">
              {groupAttacks.map((attack) => (
                <AttackItem key={`${attack.item.revisionId}-${attack.effectId}`} attack={attack} critical={critical} roll={roll} />
              ))}
              {groupRolls.map(({ feature, effect }) => (
                <li key={`${feature.content.revisionId}-${effect.id}`} className="feature">
                  <button type="button" onClick={() => roll({ content: feature.content, effectId: effect.id, critical })}>
                    Roll {effect.label ?? effect.id} ({effect.dice}
                    {rollBonus(effect.bonus)})
                  </button>{' '}
                  <span className="hint">
                    {feature.name}
                    {effect.resourceId ? ' · uses a resource (spend it separately)' : ''}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
    </section>
  );
}

function AttackItem({ attack, critical, roll }: { attack: AttackEntry; critical: boolean; roll: (target: RollTarget) => void }) {
  return (
    <li className="feature">
      <details>
        <summary>
          <span className="option-name">{attack.name}</span>: {signed(attack.toHit)} to hit, {attack.damage} {attack.damageType}
          {attack.versatileDamage ? ` (${attack.versatileDamage} two-handed)` : ''}
          {attack.automation !== 'automatic' && <span className="warn"> {automationLabels[attack.automation]}</span>}
        </summary>
        <p className="hint">
          {attack.category} {attack.attack} weapon
          {attack.range ? ` · range ${attack.range}` : ''}
          {attack.properties.length > 0 ? ` · ${attack.properties.join(', ')}` : ''}
          {attack.mastery ? ` · mastery: ${attack.mastery}` : ''}
        </p>
        <ul aria-label={`How the ${attack.name} attack is calculated`}>
          {attack.trace.map((step) => (
            <li key={step.order}>
              {step.description}: {signed(step.amount ?? 0)} = {signed(step.result)}
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
      <button type="button" onClick={() => roll({ weapon: attack.item })}>
        Roll {attack.name} attack
      </button>
      <button type="button" onClick={() => roll({ weapon: attack.item, damage: true, critical })}>
        Roll {attack.name} damage ({attack.damage})
      </button>
      {attack.versatileDamage && (
        <button type="button" onClick={() => roll({ weapon: attack.item, damage: true, versatile: true, critical })}>
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
  act,
}: {
  record?: RollRecord;
  resources: ResourceValue[];
  features: FeatureEntry[];
  act: Act;
}) {
  // The typed amount belongs to the roll it was typed for, so a new roll never inherits it.
  const [typed, setTyped] = useState<{ record?: RollRecord; value: string }>({ value: '' });
  const amount = typed.record === record ? typed.value : '';
  const setAmount = (value: string) => setTyped({ record, value });
  // Toggling aria-live on a mounted region is unreliable in some screen readers; the "Announce each roll" setting takes
  // full effect on the next sheet open, which the Settings hint says.
  if (!record) return <div role="region" aria-label="Last roll" aria-live={announceRollsOn() ? 'polite' : undefined} className="roll-result" />;
  const p = record.provenance;
  // A roll may name a resource its action spends; spending is a separate, explicit button (never automatic). A shared
  // resource (content v6) is found through the content that defines it.
  const holder = p?.linkedResourceContent ?? p?.content?.contentId;
  const linked = p?.linkedResourceId
    ? (resources.find((r) => r.resourceId === p.linkedResourceId && r.content.contentId === holder) ??
      resources.find((r) => r.resourceId === p.linkedResourceId))
    : undefined;
  const effect = features.find((f) => f.content.revisionId === p?.content?.revisionId)?.effects.find((e) => e.id === p?.effectId);
  const cost = effect?.cost ?? 1;
  const most = linked?.current !== undefined ? Math.min(cost, linked.current) : 0;
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
          {p?.label ?? 'Roll'}: {record.total}
        </strong>{' '}
        ({record.formula}
        {record.mode !== 'normal' ? `, ${record.mode}` : ''}
        {record.critical ? ', critical' : ''})
      </p>
      <p className="hint">
        {diceLine(record)}
        {p?.sourceTitle ? ` · ${p.contentName ?? ''} (${p.sourceTitle}${p.page ? `, ${pageText(p.page)}` : ''})` : ''}
      </p>
      {linked && linked.current !== undefined && !effect?.variableCost && (
        <button
          type="button"
          disabled={cost < 1 /* a cost formula can evaluate to 0 at some levels */ || linked.current < cost}
          onClick={() => act({ action: 'spend', amount: cost, contentId: linked.content.contentId, resourceId: linked.resourceId })}
        >
          Spend {cost} {linked.label} ({linked.current} left)
        </button>
      )}
      {linked && linked.current !== undefined && effect?.variableCost && (
        <div className="inline-form">
          <label className="field">
            {linked.label} to spend (1 to {most})
            <input type="number" min={1} max={most} value={amount} onChange={(e) => setAmount(e.target.value)} />
          </label>
          <button
            type="button"
            disabled={!validChoice}
            onClick={() => {
              act({ action: 'spend', amount: chosen, contentId: linked.content.contentId, resourceId: linked.resourceId });
              setAmount('');
            }}
          >
            Spend {validChoice ? chosen : ''} {linked.label} ({linked.current} left)
          </button>
        </div>
      )}
    </div>
  );
}

export function RollModePicker({ mode, onChange }: { mode: RollMode; onChange: (mode: RollMode) => void }) {
  return (
    <fieldset className="roll-mode">
      <legend>d20 rolls</legend>
      {(['normal', 'advantage', 'disadvantage'] as const).map((m) => (
        <label key={m} className="choice">
          <input type="radio" name="roll-mode" checked={mode === m} onChange={() => onChange(m)} />
          {m === 'normal' ? 'Normal' : m === 'advantage' ? 'Advantage' : 'Disadvantage'}
        </label>
      ))}
    </fieldset>
  );
}
