import type { ContentRevision, Effect } from '../api/types';

type ReportProblem = (field: string, problem: string | undefined) => void;
type CompanionEffect = Extract<Effect, { type: 'companion' }>;

/**
 * Content v12 (MECH-X item 4): the editor of a companion. Every change spreads the stored effect, so `companionId` (never shown)
 * and any key this editor does not show survive an edit; an empty speed and an empty choice of attacks or pools delete their keys.
 * Attacks are chosen by effect id and pools by resource id, as the service reads them. The formulas are checked by the service.
 */
export function CompanionFields(props: { effect: CompanionEffect; revision: ContentRevision; onChange: (effect: Effect) => void; onProblem: ReportProblem }) {
  const { effect, revision, onChange } = props;
  const set = (field: string, value: unknown) => {
    const next: Record<string, unknown> = { ...effect, [field]: value };
    if (value === undefined) delete next[field];
    onChange(next as unknown as Effect);
  };
  const toggle = (field: 'attacks' | 'resourceIds', key: string, on: boolean) => {
    const current = effect[field] ?? [];
    const list = on ? [...current, key] : current.filter((k) => k !== key);
    set(field, list.length === 0 ? undefined : list);
  };
  const attacks = revision.effects.flatMap((e) => (e.type === 'attack' ? [{ key: e.id, text: e.label || e.id }] : []));
  const pools = revision.effects.flatMap((e) => (e.type === 'resource' ? [{ key: e.resourceId, text: e.label || e.resourceId }] : []));
  return (
    <>
      <label className="field">
        Label
        <input value={effect.label} onChange={(e) => set('label', e.target.value)} />
      </label>
      <label className="field">
        Armor Class formula
        <input value={effect.armorClass} onChange={(e) => set('armorClass', e.target.value)} />
      </label>
      <label className="field">
        Hit point maximum formula
        <input value={effect.hitPointMaximum} onChange={(e) => set('hitPointMaximum', e.target.value)} />
      </label>
      <label className="field">
        Speed (optional)
        <input value={effect.speed ?? ''} onChange={(e) => set('speed', e.target.value === '' ? undefined : e.target.value)} />
      </label>
      <fieldset>
        <legend>Attacks</legend>
        {attacks.length === 0 && <p>Add an attack to this content first.</p>}
        {attacks.map((a) => (
          <label key={a.key} className="check">
            <input type="checkbox" checked={(effect.attacks ?? []).includes(a.key)} onChange={(e) => toggle('attacks', a.key, e.target.checked)} />
            {a.text}
          </label>
        ))}
      </fieldset>
      <fieldset>
        <legend>Pools</legend>
        {pools.length === 0 && <p>This content has no pools.</p>}
        {pools.map((p) => (
          <label key={p.key} className="check">
            <input type="checkbox" checked={(effect.resourceIds ?? []).includes(p.key)} onChange={(e) => toggle('resourceIds', p.key, e.target.checked)} />
            {p.text}
          </label>
        ))}
      </fieldset>
    </>
  );
}
