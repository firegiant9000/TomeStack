import { useState, type SubmitEvent } from 'react';
import type {
  AutomationStatus,
  CharacterView,
  FeatureEntry,
  PageRef,
  PlayAction,
  ResourceValue,
  RollMode,
  RollRecord,
  RollTarget,
} from '../api/types';

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

export const pageText = (page?: PageRef) =>
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
      <h3 id="hp-heading">
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

export function FeaturesPanel({ view, roll }: { view: CharacterView; roll: (target: RollTarget) => void }) {
  const features = view.sheet.features ?? [];
  const [critical, setCritical] = useState(false);
  if (features.length === 0) return null;
  return (
    <section aria-labelledby="features-heading" className="play-panel">
      <h3 id="features-heading">Features</h3>
      <label className="choice">
        <input type="checkbox" checked={critical} onChange={(e) => setCritical(e.target.checked)} />
        Critical hit (double the damage dice)
      </label>
      <ul className="features">
        {features.map((feature) => (
          <FeatureItem key={feature.content.revisionId} feature={feature} critical={critical} roll={roll} />
        ))}
      </ul>
    </section>
  );
}

function FeatureItem({ feature, critical, roll }: { feature: FeatureEntry; critical: boolean; roll: (target: RollTarget) => void }) {
  const rolls = feature.effects.filter((e) => e.type === 'roll');
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
      {rolls.map((effect) => (
        <button key={effect.id} type="button" onClick={() => roll({ content: feature.content, effectId: effect.id, critical })}>
          Roll {effect.label ?? effect.id} ({effect.dice})
        </button>
      ))}
    </li>
  );
}

/** The last roll's record (SPEC C-04): formula, every die, modifiers and provenance. Announced politely. */
export function RollResult({
  record,
  resources,
  act,
}: {
  record?: RollRecord;
  resources: ResourceValue[];
  act: Act;
}) {
  if (!record) return <div role="region" aria-label="Last roll" aria-live="polite" />;
  const p = record.provenance;
  // A roll may name a resource its action spends; spending is a separate, explicit button (never automatic).
  const linked = p?.linkedResourceId
    ? (resources.find((r) => r.resourceId === p.linkedResourceId && r.content.contentId === p.content?.contentId) ??
      resources.find((r) => r.resourceId === p.linkedResourceId))
    : undefined;
  return (
    <div role="region" aria-label="Last roll" aria-live="polite" className="roll-result">
      <p>
        <strong>
          {p?.label ?? 'Roll'}: {record.total}
        </strong>{' '}
        ({record.formula}
        {record.mode !== 'normal' ? `, ${record.mode}` : ''}
        {record.critical ? ', critical' : ''})
      </p>
      <p className="hint">
        Dice:{' '}
        {record.dice
          .map((d) => `d${d.sides} ${d.value}${d.kept ? '' : ' (dropped)'}${d.fromCritical ? ' (critical)' : ''}`)
          .join(', ')}
        {record.expressionConstant !== 0 ? ` · constant ${record.expressionConstant}` : ''}
        {record.modifiers.map((m) => ` · ${m.label} ${m.amount >= 0 ? '+' : ''}${m.amount}`).join('')}
        {p?.sourceTitle ? ` · ${p.contentName ?? ''} (${p.sourceTitle}${p.page ? `, ${pageText(p.page)}` : ''})` : ''}
      </p>
      {linked && linked.current !== undefined && (
        <button
          type="button"
          disabled={linked.current === 0}
          onClick={() => act({ action: 'spend', amount: 1, contentId: linked.content.contentId, resourceId: linked.resourceId })}
        >
          Spend 1 {linked.label} ({linked.current} left)
        </button>
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
