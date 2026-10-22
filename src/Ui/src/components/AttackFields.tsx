import { useState } from 'react';
import { abilities } from '../classBasics';
import { parseRiderDice, parseStackGroup, parseTags, toggleValue, type ToggleChoice } from '../attackAuthoring';
import type { Activation, ContentRevision, Effect } from '../api/types';
import { FormulaPreview } from './FormulaPreview';
import { damageTypeLabel, damageTypes } from './customModel';

type Attack = Extract<Effect, { type: 'attack' }>;
type Rider = Extract<Effect, { type: 'attackRider' }>;
type Toggle = Extract<Effect, { type: 'toggle' }>;
type Resource = Extract<Effect, { type: 'resource' }>;
type ReportProblem = (field: string, problem: string | undefined) => void;

/** The toggle choices of a rule: this revision's own, then those of other entries of the source. */
export interface ToggleChoices {
  own: Toggle[];
  others: ToggleChoice[];
}

const activations: { value: Activation; label: string }[] = [
  { value: 'action', label: 'Action' },
  { value: 'bonusAction', label: 'Bonus action' },
  { value: 'reaction', label: 'Reaction' },
  { value: 'other', label: 'Other' },
];

/**
 * A select over the toggles a rule can follow (D43). `current` is the toggle the rule names now (no content: this revision);
 * a toggle that is in neither list is kept as it is, never silently moved. `onPick` gets the toggle, or undefined for "always".
 */
export function ToggleSelect(props: {
  label: string;
  alwaysLabel: string;
  choices: ToggleChoices;
  current?: { contentId?: string; toggleId?: string };
  onPick: (toggle: { contentId?: string; toggleId: string } | undefined) => void;
}) {
  const { own, others } = props.choices;
  const { contentId, toggleId } = props.current ?? {};
  const value = toggleId ? toggleValue(contentId, toggleId) : '';
  const known = toggleId
    ? contentId
      ? others.some((o) => o.contentId === contentId && o.toggleId === toggleId)
      : own.some((t) => t.toggleId === toggleId)
    : true;
  return (
    <label className="field">
      {props.label}
      <select
        value={value}
        onChange={(e) => {
          const picked = e.target.value;
          if (!picked) return props.onPick(undefined);
          const other = others.find((o) => toggleValue(o.contentId, o.toggleId) === picked);
          // The kept "other content" option is not in either list: choosing it again changes nothing.
          if (other) return props.onPick({ contentId: other.contentId, toggleId: other.toggleId });
          if (own.some((t) => t.toggleId === picked)) return props.onPick({ toggleId: picked });
        }}
      >
        <option value="">{props.alwaysLabel}</option>
        {own.map((t) => (
          <option key={t.id} value={t.toggleId}>
            While {t.label || t.toggleId} is on
          </option>
        ))}
        {others.map((o) => (
          <option key={toggleValue(o.contentId, o.toggleId)} value={toggleValue(o.contentId, o.toggleId)}>
            While {o.label || o.toggleId} ({o.entryName}) is on
          </option>
        ))}
        {!known && toggleId && <option value={value}>{toggleId} (other content)</option>}
      </select>
    </label>
  );
}

/**
 * A text field read strictly: only a valid reading is handed up, and a problem is shown next to the field (linked by
 * aria-describedby) and reported so that saving is blocked until it is fixed.
 */
function StrictField<T>(props: {
  id: string;
  label: string;
  initial: string;
  parse: (text: string) => { value: T; problem?: string };
  onValue: (value: T) => void;
  onProblem: ReportProblem;
}) {
  const [text, setText] = useState(props.initial);
  const parsed = props.parse(text);
  const errorId = `${props.id}-error`;
  return (
    <div className="field">
      <label>
        {props.label}
        <input
          value={text}
          aria-invalid={parsed.problem ? true : undefined}
          aria-describedby={parsed.problem ? errorId : undefined}
          onChange={(e) => {
            setText(e.target.value);
            const next = props.parse(e.target.value);
            props.onProblem(props.id, next.problem && `${props.label}: ${next.problem}.`);
            if (!next.problem) props.onValue(next.value);
          }}
        />
      </label>
      {parsed.problem && (
        <p id={errorId} className="error">
          {parsed.problem}
        </p>
      )}
    </div>
  );
}

function FormulaField(props: { effectId: string; part: string; label: string; value: string; previewRevision?: ContentRevision; floorAtZero?: boolean; onChange: (value: string) => void }) {
  const previewId = `rule-${props.effectId}-${props.part}-preview`;
  return (
    <>
      <label className="field">
        {props.label}
        <input value={props.value} aria-describedby={previewId} onChange={(e) => props.onChange(e.target.value)} />
      </label>
      <FormulaPreview id={previewId} formula={props.value} revision={props.previewRevision} floorAtZero={props.floorAtZero} />
    </>
  );
}

/** Content v11 (D43): the editor of an attack a feature gives. */
export function AttackFields(props: {
  effect: Attack;
  resources: Resource[];
  toggles: ToggleChoices;
  previewRevision?: ContentRevision;
  onChange: (effect: Effect) => void;
  onProblem: ReportProblem;
}) {
  const { effect, onChange, onProblem } = props;
  const abilitiesId = `${effect.id}-abilities`;
  const abilitiesError = `${abilitiesId}-error`;
  const noAbility = effect.abilities.length === 0;
  const typeKnown = damageTypes.some((t) => t === effect.damageType);
  return (
    <>
      <label className="field">
        Attack name
        <input value={effect.label} onChange={(e) => onChange({ ...effect, label: e.target.value })} />
      </label>
      <label className="field">
        Kind
        <select value={effect.kind} onChange={(e) => onChange({ ...effect, kind: e.target.value === 'ranged' ? 'ranged' : 'melee' })}>
          <option value="melee">Melee</option>
          <option value="ranged">Ranged</option>
        </select>
      </label>
      <fieldset className="field" aria-describedby={noAbility ? abilitiesError : undefined}>
        <legend>Abilities (the best one counts)</legend>
        {abilities.map(([key, name]) => (
          <label key={key} className="choice">
            <input
              type="checkbox"
              checked={effect.abilities.includes(key)}
              onChange={(e) => {
                const next = abilities.map(([k]) => k).filter((k) => (k === key ? e.target.checked : effect.abilities.includes(k)));
                onProblem(abilitiesId, next.length === 0 ? 'Abilities: choose at least one.' : undefined);
                onChange({ ...effect, abilities: next });
              }}
            />
            {name}
          </label>
        ))}
        {noAbility && (
          <p id={abilitiesError} className="error">
            Choose at least one ability.
          </p>
        )}
      </fieldset>
      <label className="choice">
        <input type="checkbox" checked={effect.proficient !== false} onChange={(e) => onChange({ ...effect, proficient: e.target.checked })} />
        Add the proficiency bonus to the attack roll
      </label>
      <FormulaField
        effectId={effect.id}
        part="count"
        label="Damage dice: how many (a number or a formula)"
        value={effect.damage.count}
        previewRevision={props.previewRevision}
        onChange={(count) => onChange({ ...effect, damage: { ...effect.damage, count } })}
      />
      <FormulaField
        effectId={effect.id}
        part="die"
        label="Damage die (a number such as 6, or a formula such as SCALE.handDie)"
        value={effect.damage.die}
        previewRevision={props.previewRevision}
        onChange={(die) => onChange({ ...effect, damage: { ...effect.damage, die } })}
      />
      <FormulaField
        effectId={effect.id}
        part="bonus"
        label="Damage bonus (a number or a formula; empty adds the ability modifier)"
        value={effect.damageBonus ?? ''}
        previewRevision={props.previewRevision}
        onChange={(value) => onChange({ ...effect, damageBonus: value || undefined })}
      />
      <label className="field">
        Damage type
        <select value={effect.damageType} onChange={(e) => onChange({ ...effect, damageType: e.target.value })}>
          {!typeKnown && <option value={effect.damageType}>{effect.damageType || 'Choose a type'}</option>}
          {damageTypes.map((t) => (
            <option key={t} value={t}>
              {damageTypeLabel(t)}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        Range (such as 60 ft; empty for none)
        <input value={effect.range ?? ''} onChange={(e) => onChange({ ...effect, range: e.target.value || undefined })} />
      </label>
      <StrictField
        id={`${effect.id}-tags`}
        label="Tags (separated by commas; riders match them)"
        initial={(effect.tags ?? []).join(', ')}
        parse={(text) => {
          const parsed = parseTags(text);
          return { value: parsed.tags, problem: parsed.problem };
        }}
        onValue={(tags) => onChange({ ...effect, tags: tags.length > 0 ? tags : undefined })}
        onProblem={onProblem}
      />
      <label className="choice">
        <input type="checkbox" checked={effect.replacesAttack ?? false} onChange={(e) => onChange({ ...effect, replacesAttack: e.target.checked || undefined })} />
        Can replace one attack of the Attack action
      </label>
      <label className="field">
        Activation
        <select value={effect.activation ?? ''} onChange={(e) => onChange({ ...effect, activation: (e.target.value || undefined) as Activation | undefined })}>
          <option value="">Action (the default)</option>
          {activations.map((a) => (
            <option key={a.value} value={a.value}>
              {a.label}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        Spends from
        <select
          value={effect.resourceId ?? ''}
          onChange={(e) => onChange(e.target.value ? { ...effect, resourceId: e.target.value, resourceContent: undefined } : { ...effect, resourceId: undefined, cost: undefined, resourceContent: undefined })}
        >
          <option value="">No pool</option>
          {props.resources.map((r) => (
            <option key={r.id} value={r.resourceId}>
              {r.label || r.resourceId}
            </option>
          ))}
          {/* A pool of other content is kept as it is, not silently moved. */}
          {effect.resourceId && (effect.resourceContent || !props.resources.some((r) => r.resourceId === effect.resourceId)) && (
            <option value={effect.resourceId}>{effect.resourceId} (other content)</option>
          )}
        </select>
      </label>
      {effect.resourceId && (
        <FormulaField
          effectId={effect.id}
          part="cost"
          label="Cost (a number or a formula; empty is one use)"
          value={effect.cost ?? ''}
          previewRevision={props.previewRevision}
          floorAtZero
          onChange={(value) => onChange({ ...effect, cost: value || undefined })}
        />
      )}
      <ToggleSelect
        label="Available"
        alwaysLabel="Always"
        choices={props.toggles}
        current={effect.whileToggle}
        onPick={(toggle) => onChange({ ...effect, whileToggle: toggle })}
      />
    </>
  );
}

/** Content v11 (D43): the editor of a rider that adds to the attacks carrying one of its tags. */
export function RiderFields(props: {
  effect: Rider;
  toggles: ToggleChoices;
  previewRevision?: ContentRevision;
  onChange: (effect: Effect) => void;
  onProblem: ReportProblem;
}) {
  const { effect, onChange, onProblem } = props;
  const typeKnown = !effect.damageType || damageTypes.some((t) => t === effect.damageType);
  return (
    <>
      <StrictField
        id={`${effect.id}-tags`}
        label="Tags (separated by commas; all matches every attack)"
        initial={effect.tags.join(', ')}
        parse={(text) => {
          const parsed = parseTags(text, { required: true });
          return { value: parsed.tags, problem: parsed.problem };
        }}
        onValue={(tags) => onChange({ ...effect, tags })}
        onProblem={onProblem}
      />
      <FormulaField
        effectId={effect.id}
        part="tohit"
        label="To hit (a number or a formula; empty adds nothing)"
        value={effect.toHit ?? ''}
        previewRevision={props.previewRevision}
        onChange={(value) => onChange({ ...effect, toHit: value || undefined })}
      />
      <StrictField
        id={`${effect.id}-dice`}
        label="Extra damage dice (such as 3d8; empty adds none)"
        initial={effect.dice ?? ''}
        parse={(text) => {
          const parsed = parseRiderDice(text);
          return { value: parsed.dice, problem: parsed.problem };
        }}
        onValue={(dice) => onChange({ ...effect, dice })}
        onProblem={onProblem}
      />
      <FormulaField
        effectId={effect.id}
        part="bonus"
        label="Damage bonus (a number or a formula; empty adds nothing)"
        value={effect.damageBonus ?? ''}
        previewRevision={props.previewRevision}
        onChange={(value) => onChange({ ...effect, damageBonus: value || undefined })}
      />
      <label className="field">
        Damage type of the extra dice
        <select value={effect.damageType ?? ''} onChange={(e) => onChange({ ...effect, damageType: e.target.value || undefined })}>
          <option value="">Same as the attack</option>
          {!typeKnown && <option value={effect.damageType}>{effect.damageType}</option>}
          {damageTypes.map((t) => (
            <option key={t} value={t}>
              {damageTypeLabel(t)}
            </option>
          ))}
        </select>
      </label>
      <ToggleSelect
        label="Applies"
        alwaysLabel="Always"
        choices={props.toggles}
        current={{ contentId: effect.toggleContent, toggleId: effect.toggle }}
        onPick={(toggle) => onChange({ ...effect, toggle: toggle?.toggleId, toggleContent: toggle?.contentId })}
      />
      <StrictField
        id={`${effect.id}-group`}
        label="Stack group (riders of one group do not add up; empty for none)"
        initial={effect.stackGroup ?? ''}
        parse={(text) => {
          const parsed = parseStackGroup(text);
          return { value: parsed.group, problem: parsed.problem };
        }}
        onValue={(stackGroup) => onChange({ ...effect, stackGroup })}
        onProblem={onProblem}
      />
    </>
  );
}
