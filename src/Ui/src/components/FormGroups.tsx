import { useId } from 'react';
import type { PlayAction, ResourceValue, ToggleValue } from '../api/types';

/**
 * D43 slice 1: the form picker. Toggles that share a form group (`group` + `groupContent`) are one radio group, "Base" first
 * (no form), then the primaries; overlays are checkboxes beside it. Choosing another form while one is on sends `replace`,
 * so the server switches the old one off and the new one on as one confirmed change. Ungrouped toggles are not drawn here.
 *
 * The sheet carries the group's id, not its label, and a pool's id; the label is the id in words ("forms" is "Forms") and the
 * pool's label comes from `resources` when given. Both are only for display.
 */
export function FormGroups({
  toggles,
  act,
  busy,
  resources = [],
}: {
  toggles: ToggleValue[];
  act: (action: PlayAction) => void;
  busy: boolean;
  /** The sheet's resources, to name the pool a cost is paid from. */
  resources?: ResourceValue[];
}) {
  const groups = new Map<string, ToggleValue[]>();
  for (const t of toggles) {
    if (!t.group) continue;
    const key = `${t.groupContent ?? ''}|${t.group}`;
    groups.set(key, [...(groups.get(key) ?? []), t]);
  }
  return (
    <>
      {[...groups.entries()].map(([key, members]) => (
        <FormGroup key={key} members={members} act={act} busy={busy} resources={resources} />
      ))}
    </>
  );
}

const inWords = (id: string) => {
  const words = id.replace(/[-_]+/g, ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
};

const same = (a: ToggleValue, b: ToggleValue) => a.content.contentId === b.content.contentId && a.toggleId === b.toggleId;

function FormGroup({ members, act, busy, resources }: { members: ToggleValue[]; act: (action: PlayAction) => void; busy: boolean; resources: ResourceValue[] }) {
  const id = useId();
  const primaries = members.filter((t) => t.layer !== 'overlay');
  const overlays = members.filter((t) => t.layer === 'overlay');
  const current = primaries.find((t) => t.on);
  const label = inWords(members[0]?.group ?? '');
  const ref = (t: ToggleValue) => ({ contentId: t.content.contentId, toggleId: t.toggleId });

  const costText = (t: ToggleValue) => {
    const amount = t.cost ?? (t.costResourceContent ? 1 : 0);
    if (amount <= 0 || !t.resourceId) return undefined;
    const pool = resources.find((r) => r.resourceId === t.resourceId && (!t.costResourceContent || r.content.contentId === t.costResourceContent));
    return `${amount} ${pool?.label ?? inWords(t.resourceId)}`;
  };

  const row = (t: ToggleValue, input: (describedBy: string | undefined) => React.ReactNode) => {
    const cost = costText(t);
    const reasonId = !t.available && t.unavailableReason ? `${id}-${t.content.contentId}-${t.toggleId}-why` : undefined;
    return (
      <div key={`${t.content.revisionId}-${t.toggleId}`}>
        <label className="choice">
          {input(reasonId)}
          {t.label}
          {cost && <span className="option-source"> · {cost}</span>}
        </label>
        {reasonId && (
          <p id={reasonId} className="hint">
            {t.unavailableReason}
          </p>
        )}
      </div>
    );
  };

  return (
    <div className="form-group">
      <p id={`${id}-label`} className="option-name">
        {label}
      </p>
      <div role="radiogroup" aria-labelledby={`${id}-label`}>
        <label className="choice">
          <input type="radio" name={`${id}-form`} checked={!current} disabled={busy} onChange={() => current && act({ action: 'toggleOff', ...ref(current) })} />
          Base
        </label>
        {primaries.map((t) =>
          row(t, (describedBy) => (
            <input
              type="radio"
              name={`${id}-form`}
              checked={t.on}
              disabled={busy || (!t.available && !t.on)}
              aria-describedby={describedBy}
              onChange={() => act(current && !same(current, t) ? { action: 'toggleOn', ...ref(t), replace: true } : { action: 'toggleOn', ...ref(t) })}
            />
          )),
        )}
      </div>
      {overlays.map((t) =>
        row(t, (describedBy) => (
          <input
            type="checkbox"
            checked={t.on}
            disabled={busy || (!t.available && !t.on)}
            aria-describedby={describedBy}
            onChange={() => act({ action: t.on ? 'toggleOff' : 'toggleOn', ...ref(t) })}
          />
        )),
      )}
    </div>
  );
}
