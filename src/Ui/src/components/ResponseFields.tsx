import type { ContentRevision, Diagnostic, Effect, ResponseOutcomeDef, ResponseOutcomeKind, ResponsePhase } from '../api/types';
import { toggleValue } from '../attackAuthoring';
import { poolValue, type PoolChoice } from '../rollAuthoring';
import { FormulaField, ToggleSelect, type ToggleChoices } from './AttackFields';

type ResponseEffect = Extract<Effect, { type: 'response' }>;
type Resource = Extract<Effect, { type: 'resource' }>;
type Roll = Extract<Effect, { type: 'roll' }>;

/** The most outcomes a response can have (the service's MaxOutcomesPerResponse). */
export const maxOutcomes = 8;

const phases: { value: ResponsePhase; label: string }[] = [
  { value: 'damageReduction', label: 'Before the hit: take less damage' },
  { value: 'hitPointOutcome', label: 'A hit that would drop me to 0: stay up' },
  { value: 'threshold', label: 'After the hit: when a condition holds' },
];

const kinds: { value: ResponseOutcomeKind; label: string }[] = [
  { value: 'spend', label: 'Spend from a pool' },
  { value: 'regain', label: 'Regain to a pool' },
  { value: 'toggleOn', label: 'Turn a toggle on' },
  { value: 'offerRoll', label: 'Offer a roll' },
  { value: 'regainHitPoints', label: 'Regain hit points' },
  { value: 'temporaryHitPoints', label: 'Give temporary hit points' },
];

/** Drops the members a phase does not take and gives it the ones it needs (a valid start). */
function withPhase(effect: ResponseEffect, phase: ResponsePhase): ResponseEffect {
  const { amount, when, ...rest } = effect;
  if (phase === 'threshold') return { ...rest, phase, when: when ?? { survived: true } };
  return { ...rest, phase, amount: amount ?? '1' };
}

/** A new outcome of a kind with only that kind's members. */
function startOutcome(kind: ResponseOutcomeKind, resources: Resource[], rolls: Roll[], previous?: ResponseOutcomeDef): ResponseOutcomeDef {
  switch (kind) {
    case 'spend':
    case 'regain':
      return { kind, resourceId: previous?.resourceId ?? resources[0]?.resourceId, resourceContent: previous?.resourceContent, amount: previous?.amount ?? '1' };
    case 'toggleOn':
      return { kind, toggle: previous?.toggle };
    case 'offerRoll':
      return { kind, rollId: previous?.rollId ?? rolls[0]?.rollId, rollContent: previous?.rollContent };
    default:
      return { kind, amount: previous?.amount ?? '1' };
  }
}

function PoolSelect(props: {
  label: string;
  noneLabel: string;
  current: { contentId?: string; resourceId?: string };
  resources: Resource[];
  otherPools: PoolChoice[];
  onPick: (pool: { contentId?: string; resourceId: string } | undefined) => void;
}) {
  const { contentId, resourceId } = props.current;
  const known = resourceId ? (contentId ? props.otherPools.some((p) => p.contentId === contentId && p.resourceId === resourceId) : props.resources.some((r) => r.resourceId === resourceId)) : true;
  const selected = resourceId ? poolValue(contentId, resourceId) : '';
  return (
    <label className="field">
      {props.label}
      <select
        value={selected}
        onChange={(e) => {
          const picked = e.target.value;
          if (!picked) return props.onPick(undefined);
          const other = props.otherPools.find((p) => poolValue(p.contentId, p.resourceId) === picked);
          if (other) return props.onPick({ contentId: other.contentId, resourceId: other.resourceId });
          if (props.resources.some((r) => r.resourceId === picked)) props.onPick({ resourceId: picked });
        }}
      >
        <option value="">{props.noneLabel}</option>
        {props.resources.map((r) => (
          <option key={r.id} value={r.resourceId}>
            {r.label || r.resourceId}
          </option>
        ))}
        {props.otherPools.map((p) => (
          <option key={poolValue(p.contentId, p.resourceId)} value={poolValue(p.contentId, p.resourceId)}>
            {p.label || p.resourceId} ({p.entryName})
          </option>
        ))}
        {/* A pool that is in neither list is kept as it is, not silently moved. */}
        {!known && resourceId && <option value={selected}>{resourceId} (other content)</option>}
      </select>
    </label>
  );
}

function OutcomeFields(props: {
  effect: ResponseEffect;
  outcome: ResponseOutcomeDef;
  index: number;
  count: number;
  resources: Resource[];
  otherPools: PoolChoice[];
  toggles: ToggleChoices;
  rolls: Roll[];
  previewRevision?: ContentRevision;
  onChange: (outcome: ResponseOutcomeDef) => void;
  onMove: (by: -1 | 1) => void;
  onRemove: () => void;
}) {
  const { outcome, index } = props;
  const n = index + 1;
  const { own, others } = props.toggles;
  const toggleId = outcome.toggle?.toggleId;
  const toggleContent = outcome.toggle?.contentId;
  const toggleKnown = toggleId ? (toggleContent ? others.some((o) => o.contentId === toggleContent && o.toggleId === toggleId) : own.some((t) => t.toggleId === toggleId)) : true;
  const rollKnown = outcome.rollId ? !outcome.rollContent && props.rolls.some((r) => r.rollId === outcome.rollId) : true;
  const hitPoints = outcome.kind === 'regainHitPoints' || outcome.kind === 'temporaryHitPoints';
  const amountLabel = outcome.kind === 'regainHitPoints' ? 'Hit points regained (a number or a formula)' : outcome.kind === 'temporaryHitPoints' ? 'Temporary hit points (a number or a formula)' : 'Amount (a number or a formula)';
  const moveButton = (by: -1 | 1) => {
    const at = by < 0 ? index === 0 : index === props.count - 1;
    return (
      <button type="button" aria-disabled={at || undefined} onClick={() => !at && props.onMove(by)}>
        {by < 0 ? `Move outcome ${n} up` : `Move outcome ${n} down`}
      </button>
    );
  };
  return (
    <fieldset className="bare-fieldset">
      <legend>{`Outcome ${n}`}</legend>
      <label className="field">
        What it does
        <select value={outcome.kind} onChange={(e) => props.onChange(startOutcome(e.target.value as ResponseOutcomeKind, props.resources, props.rolls, outcome))}>
          {kinds.map((k) => (
            <option key={k.value} value={k.value}>
              {k.label}
            </option>
          ))}
        </select>
      </label>
      {(outcome.kind === 'spend' || outcome.kind === 'regain') && (
        <PoolSelect
          label="Pool"
          noneLabel="Choose a pool"
          current={{ contentId: outcome.resourceContent, resourceId: outcome.resourceId }}
          resources={props.resources}
          otherPools={props.otherPools}
          onPick={(pool) => props.onChange({ ...outcome, resourceId: pool?.resourceId, resourceContent: pool?.contentId })}
        />
      )}
      {(outcome.kind === 'spend' || outcome.kind === 'regain' || hitPoints) && (
        <FormulaField
          effectId={props.effect.id}
          part={`outcome${n}`}
          label={amountLabel}
          value={outcome.amount ?? ''}
          previewRevision={props.previewRevision}
          floorAtZero
          onChange={(amount) => props.onChange({ ...outcome, amount })}
        />
      )}
      {outcome.kind === 'toggleOn' && (
        <label className="field">
          Toggle to turn on
          <select
            value={toggleId ? toggleValue(toggleContent, toggleId) : ''}
            onChange={(e) => {
              const picked = e.target.value;
              if (!picked) return props.onChange({ ...outcome, toggle: undefined });
              const other = others.find((o) => toggleValue(o.contentId, o.toggleId) === picked);
              if (other) return props.onChange({ ...outcome, toggle: { contentId: other.contentId, toggleId: other.toggleId } });
              if (own.some((t) => t.toggleId === picked)) props.onChange({ ...outcome, toggle: { toggleId: picked } });
            }}
          >
            <option value="">Choose a toggle</option>
            {own.map((t) => (
              <option key={t.id} value={t.toggleId}>
                {t.label || t.toggleId}
              </option>
            ))}
            {others.map((o) => (
              <option key={toggleValue(o.contentId, o.toggleId)} value={toggleValue(o.contentId, o.toggleId)}>
                {o.label || o.toggleId} ({o.entryName})
              </option>
            ))}
            {!toggleKnown && toggleId && <option value={toggleValue(toggleContent, toggleId)}>{toggleId} (other content)</option>}
          </select>
        </label>
      )}
      {outcome.kind === 'offerRoll' && (
        <label className="field">
          Roll to offer
          <select value={outcome.rollId ?? ''} onChange={(e) => props.onChange({ ...outcome, rollId: e.target.value || undefined, rollContent: undefined })}>
            <option value="">Choose a roll</option>
            {props.rolls.map((r) => (
              <option key={r.id} value={r.rollId}>
                {r.label || r.rollId}
              </option>
            ))}
            {!rollKnown && outcome.rollId && <option value={outcome.rollId}>{outcome.rollId} (other content)</option>}
          </select>
        </label>
      )}
      <div className="actions">
        {moveButton(-1)}
        {moveButton(1)}
        <button type="button" onClick={props.onRemove}>
          {`Remove outcome ${n}`}
        </button>
      </div>
    </fieldset>
  );
}

/**
 * Content v11 (D43 S7): the editor of a damage response. The fields follow the phase: a reduction or a hit point outcome takes an
 * amount, a threshold takes a `when` (hit points left at or below a formula, or surviving the hit). A field the chosen phase does
 * not take is not shown and is dropped from the saved effect. The numbers are checked by the service, never here: `diagnostics` are
 * the check's errors and warnings for this effect.
 */
export function ResponseFields(props: {
  effect: ResponseEffect;
  resources: Resource[];
  otherPools: PoolChoice[];
  toggles: ToggleChoices;
  rolls: Roll[];
  previewRevision?: ContentRevision;
  diagnostics: Diagnostic[];
  onChange: (effect: Effect) => void;
}) {
  const { effect, onChange } = props;
  const outcomes = effect.outcomes ?? [];
  const atLimit = outcomes.length >= maxOutcomes;
  const limitId = `rule-${effect.id}-outcome-limit`;
  const diagnosticsId = `rule-${effect.id}-diagnostics`;
  const whenMode = effect.when?.atOrBelow !== undefined ? 'atOrBelow' : 'survived';
  const hintId = `rule-${effect.id}-when-hint`;
  const setOutcomes = (next: ResponseOutcomeDef[]) => onChange({ ...effect, outcomes: next.length === 0 ? undefined : next });
  const replaceAt = (at: number, outcome: ResponseOutcomeDef) => setOutcomes(outcomes.map((o, i) => (i === at ? outcome : o)));
  const move = (at: number, by: -1 | 1) => {
    const next = [...outcomes];
    const [moved] = next.splice(at, 1);
    next.splice(at + by, 0, moved!);
    setOutcomes(next);
  };
  const add = () => {
    if (atLimit) return;
    const kind: ResponseOutcomeKind = props.resources.length > 0 ? 'spend' : 'regainHitPoints';
    setOutcomes([...outcomes, startOutcome(kind, props.resources, props.rolls)]);
  };
  return (
    <>
      <label className="field">
        Response name
        <input value={effect.label} onChange={(e) => onChange({ ...effect, label: e.target.value })} />
      </label>
      <label className="field">
        Phase
        <select value={effect.phase} onChange={(e) => onChange(withPhase(effect, e.target.value as ResponsePhase))}>
          {phases.map((p) => (
            <option key={p.value} value={p.value}>
              {p.label}
            </option>
          ))}
        </select>
      </label>
      {effect.phase === 'damageReduction' && (
        <FormulaField effectId={effect.id} part="amount" label="Damage taken off (a number or a formula)" value={effect.amount ?? ''} previewRevision={props.previewRevision} floorAtZero onChange={(amount) => onChange({ ...effect, amount })} />
      )}
      {effect.phase === 'hitPointOutcome' && (
        <FormulaField effectId={effect.id} part="amount" label="Hit points left (a number or a formula)" value={effect.amount ?? ''} previewRevision={props.previewRevision} floorAtZero onChange={(amount) => onChange({ ...effect, amount })} />
      )}
      {effect.phase === 'threshold' && (
        <>
          <label className="field">
            Offered when
            <select
              value={whenMode}
              onChange={(e) => onChange({ ...effect, when: e.target.value === 'atOrBelow' ? { atOrBelow: '' } : { survived: true } })}
            >
              <option value="survived">I am still standing after the hit</option>
              <option value="atOrBelow">The hit points left are at or below a number</option>
            </select>
          </label>
          {whenMode === 'atOrBelow' && (
            <>
              <label className="field">
                Hit points left at or below (a number or a formula)
                <input value={effect.when?.atOrBelow ?? ''} aria-describedby={hintId} onChange={(e) => onChange({ ...effect, when: { atOrBelow: e.target.value } })} />
              </label>
              <p id={hintId} className="hint">
                HP.MAX is the hit point maximum before this hit. It can be read here and nowhere else in a response.
              </p>
            </>
          )}
        </>
      )}
      <PoolSelect
        label="Spends from"
        noneLabel="Nothing"
        current={{ contentId: effect.resourceContent, resourceId: effect.resourceId }}
        resources={props.resources}
        otherPools={props.otherPools}
        onPick={(pool) =>
          onChange(
            pool
              ? { ...effect, resourceId: pool.resourceId, resourceContent: pool.contentId, cost: effect.cost ?? '1' }
              : { ...effect, resourceId: undefined, resourceContent: undefined, cost: undefined },
          )
        }
      />
      {effect.resourceId && (
        <FormulaField effectId={effect.id} part="cost" label="Cost (a number or a formula)" value={effect.cost ?? ''} previewRevision={props.previewRevision} floorAtZero onChange={(cost) => onChange({ ...effect, cost: cost || undefined })} />
      )}
      {(effect.toggle || props.toggles.own.length > 0 || props.toggles.others.length > 0) && (
        <ToggleSelect label="Offered" alwaysLabel="Always" choices={props.toggles} current={effect.toggle} onPick={(toggle) => onChange({ ...effect, toggle })} />
      )}
      <fieldset className="bare-fieldset">
        <legend>{`Outcomes (at most ${maxOutcomes})`}</legend>
        {outcomes.length > 0 && (
          <ol>
            {outcomes.map((outcome, i) => (
              <li key={i}>
                <OutcomeFields
                  effect={effect}
                  outcome={outcome}
                  index={i}
                  count={outcomes.length}
                  resources={props.resources}
                  otherPools={props.otherPools}
                  toggles={props.toggles}
                  rolls={props.rolls}
                  previewRevision={props.previewRevision}
                  onChange={(next) => replaceAt(i, next)}
                  onMove={(by) => move(i, by)}
                  onRemove={() => setOutcomes(outcomes.filter((_, at) => at !== i))}
                />
              </li>
            ))}
          </ol>
        )}
        <button type="button" aria-disabled={atLimit || undefined} aria-describedby={atLimit ? limitId : undefined} onClick={add}>
          Add outcome
        </button>
        {atLimit && (
          <p id={limitId} className="hint">
            {`A response has at most ${maxOutcomes} outcomes; remove one to add another.`}
          </p>
        )}
      </fieldset>
      {props.diagnostics.length > 0 && (
        <ul id={diagnosticsId} className="error" aria-label="Problems with this response">
          {props.diagnostics.map((d, i) => (
            <li key={`${d.code}-${i}`}>{d.message}</li>
          ))}
        </ul>
      )}
    </>
  );
}
