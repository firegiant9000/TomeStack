import type { ContentRevision, Effect } from '../api/types';
import type { ToggleChoice } from '../attackAuthoring';
import { ToggleSelect } from './AttackFields';
import { FormulaPreview } from './FormulaPreview';

type Modifier = Extract<Effect, { type: 'modifier' }>;
type Resource = Extract<Effect, { type: 'resource' }>;
type Toggle = Extract<Effect, { type: 'toggle' }>;

/**
 * The fields of a modifier rule. A top-level modifier also offers the pool maximums and the toggle choice. A nested one (inside
 * an item mod, MECH-X item 3) shows only operation, target, value and text: the service rejects a toggle, timing or automation
 * there, so none is offered.
 */
export function ModifierFields(props: {
  effect: Modifier;
  fields: { id: string; label: string }[];
  previewRevision?: ContentRevision;
  onChange: (effect: Modifier) => void;
  nested?: boolean;
  resources?: Resource[];
  toggles?: Toggle[];
  otherToggles?: ToggleChoice[];
}) {
  const { effect, onChange, nested } = props;
  const resources = props.resources ?? [];
  const toggles = props.toggles ?? [];
  const otherToggles = props.otherToggles ?? [];
  // Content v12 (G5): only a bonus to an ability score can carry a cap; any change that hides the field drops it.
  const isScore = (target: string) => /^ability\.[^.]+\.score$/.test(target);
  const capped = (next: Modifier, cap: number | undefined): Modifier => {
    const copy = { ...next };
    if (cap === undefined) delete copy.cap;
    else copy.cap = cap;
    return copy;
  };
  const withoutCap = (next: Modifier): Modifier =>
    next.cap === undefined || (next.operation === 'bonus' && isScore(next.target)) ? next : capped(next, undefined);
  const showCap = !nested && effect.operation === 'bonus' && isScore(effect.target);
  const setCap = (text: string) => {
    const n = Number(text);
    onChange(capped(effect, text.trim() === '' || !Number.isFinite(n) ? undefined : n));
  };
  return (
    <>
      <label className="field">
        Changes
        <select value={effect.target} onChange={(e) => onChange(withoutCap({ ...effect, target: e.target.value }))}>
          {props.fields.map((f) => (
            <option key={f.id} value={f.id}>
              {f.label}
            </option>
          ))}
          {/* A stored target this list does not offer stays visible, so choosing another is deliberate. */}
          {nested && !props.fields.some((f) => f.id === effect.target) && <option value={effect.target}>{effect.target}</option>}
          {/* Content v11 (D43): a bonus can raise the maximum of a pool of this revision. Another operation cannot, so the
              targets are offered for a bonus (and kept while the modifier already names one, for the check to explain). */}
          {!nested &&
            (effect.operation === 'bonus' || effect.target.startsWith('resource.')) &&
            resources.map((r) => (
              <option key={r.id} value={`resource.${r.resourceId}`}>
                Maximum of {r.label || r.resourceId}
              </option>
            ))}
          {!nested && effect.target.startsWith('resource.') && !resources.some((r) => `resource.${r.resourceId}` === effect.target) && (
            <option value={effect.target}>{effect.target} (other content)</option>
          )}
        </select>
      </label>
      <label className="field">
        How
        <select value={effect.operation} onChange={(e) => onChange(withoutCap({ ...effect, operation: e.target.value as Modifier['operation'] }))}>
          <option value="bonus">Add (bonus)</option>
          <option value="replace">Replace the base</option>
          <option value="set">Set to</option>
        </select>
      </label>
      <label className="field">
        Value (a number or a formula such as PB + CON.MOD)
        <input value={effect.value} aria-describedby={`rule-${effect.id}-value-preview`} onChange={(e) => onChange({ ...effect, value: e.target.value })} />
      </label>
      <FormulaPreview id={`rule-${effect.id}-value-preview`} formula={effect.value} revision={props.previewRevision} />
      {(effect.target === 'speed.climb' || effect.target === 'speed.swim' || effect.target === 'speed.fly') && (
        <p className="hint">You can use SPEED (your walking speed) in a climb, swim or fly speed modifier, such as SPEED / 2.</p>
      )}
      {showCap && (
        <>
          <label className="field">
            Highest the score can reach
            <input type="number" min={21} max={30} step={1} value={effect.cap ?? ''} aria-describedby={`rule-${effect.id}-cap-hint`} onChange={(e) => setCap(e.target.value)} />
          </label>
          <p className="hint" id={`rule-${effect.id}-cap-hint`}>
            Leave empty for the usual 20; up to 30.
          </p>
        </>
      )}
      {nested && (
        <label className="field">
          Text
          <input value={effect.text ?? ''} onChange={(e) => onChange({ ...effect, text: e.target.value || undefined })} />
        </label>
      )}
      {/* Only for a modifier that is always on or switched by a toggle: another timing (such as onRoll, from imported
          content) is left as it is, never turned into "always" (review fix). */}
      {!nested && (effect.toggle || ((effect.timing ?? 'always') === 'always' && (toggles.length > 0 || otherToggles.length > 0))) && (
        <ToggleSelect
          label="Applies"
          alwaysLabel="Always"
          choices={{ own: toggles, others: otherToggles }}
          current={{ contentId: effect.toggleContent, toggleId: effect.toggle }}
          // Content v6: a toggled modifier applies only while its toggle is on, so its timing is whileActive; without
          // the toggle it is always on again (its timing was whileActive only because of the toggle). Content v11: the
          // toggle may be another entry's (toggleContent).
          onPick={(toggle) =>
            onChange(
              toggle
                ? { ...effect, toggle: toggle.toggleId, toggleContent: toggle.contentId, timing: 'whileActive' }
                : { ...effect, toggle: undefined, toggleContent: undefined, timing: undefined },
            )
          }
        />
      )}
    </>
  );
}
