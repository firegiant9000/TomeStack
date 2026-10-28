import { useId, useRef, useState, type KeyboardEvent } from 'react';
import type { PlayAction, ResourceValue, ToggleValue } from '../api/types';
import { paying, paymentText } from '../payment';

/**
 * D43 slice 1: the form picker. Toggles that share a form group (`group` + `groupContent`) are one radio group, "Base" first
 * (no form), then the primaries; overlays are checkboxes beside it. Choosing another form while one is on sends `replace`,
 * so the server switches the old one off and the new one on as one confirmed change. Ungrouped toggles are not drawn here.
 *
 * A form costs something to switch on, so the radios are not native: arrow keys, Home and End only move focus (roving
 * tabindex), and Space, Enter or a click chooses. A native radio would pay for every form a keyboard user passed through.
 *
 * The group is named by the sheet's `groupLabel` (the declaring toggleGroup's label); the key in words is only the fallback.
 */
export function FormGroups({
  toggles,
  act,
  busy,
  resources = [],
}: {
  toggles: ToggleValue[];
  /** May return a promise (the sheet's does): further choices are ignored until it settles. */
  act: (action: PlayAction) => void | Promise<unknown>;
  busy: boolean;
  /** The sheet's resources, to name the pool a cost is paid from. */
  resources?: ResourceValue[];
}) {
  const [pending, setPending] = useState(false);
  const run = async (action: PlayAction) => {
    if (pending) return; // a paid change is in flight: a second choice would be a second payment
    setPending(true);
    try {
      await act(action);
    } catch {
      // The sheet's `act` reports a refusal itself; a rejection here only means the change did not happen.
    } finally {
      setPending(false);
    }
  };
  const groups = new Map<string, ToggleValue[]>();
  for (const t of toggles) {
    if (!t.group) continue;
    const key = `${t.groupContent ?? ''}|${t.group}`;
    groups.set(key, [...(groups.get(key) ?? []), t]);
  }
  return (
    <>
      {[...groups.entries()].map(([key, members]) => (
        <FormGroup key={key} members={members} run={run} busy={busy} pending={pending} resources={resources} />
      ))}
    </>
  );
}

const inWords = (id: string) => {
  const words = id.replace(/[-_]+/g, ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
};

const same = (a: ToggleValue, b: ToggleValue) => a.content.contentId === b.content.contentId && a.toggleId === b.toggleId;
const ref = (t: ToggleValue) => ({ contentId: t.content.contentId, toggleId: t.toggleId });

interface Option {
  key: string;
  label: string;
  toggle?: ToggleValue;
  checked: boolean;
  disabled: boolean;
}

function FormGroup({
  members,
  run,
  busy,
  pending,
  resources,
}: {
  members: ToggleValue[];
  run: (action: PlayAction) => Promise<void>;
  busy: boolean;
  pending: boolean;
  resources: ResourceValue[];
}) {
  const id = useId();
  const primaries = members.filter((t) => t.layer !== 'overlay');
  const overlays = members.filter((t) => t.layer === 'overlay');
  const current = primaries.find((t) => t.on);
  const label = members.find((t) => t.groupLabel)?.groupLabel ?? inWords(members[0]?.group ?? '');

  // What the server spends: `cost` (1 when it names a pool and no cost) of `resourceId` from `costResourceContent` (its own content when none).
  const costText = (t: ToggleValue) => {
    // MECH-X item 1: a form that pays hit points or a slot says so instead.
    if (paying(t.payment)) return paymentText(t.payment, t.cost);
    if (!t.resourceId) return undefined;
    const amount = t.cost ?? 1;
    if (amount <= 0) return undefined;
    const pool = resources.find((r) => r.resourceId === t.resourceId && r.content.contentId === (t.costResourceContent ?? t.content.contentId));
    return `${amount} ${pool?.label ?? inWords(t.resourceId)}`;
  };

  // R40: a form that is off and cannot be paid for now is held with the payment's own reason; one that is on is never held by it (B1).
  const unpayable = (t: ToggleValue) => !t.on && paying(t.payment) && !t.payment.payable;
  const whyNot = (t: ToggleValue) => (!t.available ? t.unavailableReason : unpayable(t) ? (t.payment?.reason ?? 'Cannot be paid now') : undefined);
  const reasonId = (t: ToggleValue) => (whyNot(t) ? `${id}-${t.content.contentId}-${t.toggleId}-why` : undefined);
  const reason = (t: ToggleValue) =>
    reasonId(t) && (
      <p id={reasonId(t)} className="hint">
        {whyNot(t)}
      </p>
    );
  const cost = (t: ToggleValue) => {
    const text = costText(t);
    return text ? <span className="option-source"> · {text}</span> : null;
  };

  // The radios: "Base" (no form) then each primary. Disabled options are skipped by the arrow keys.
  const options: Option[] = [
    { key: 'base', label: 'Base', checked: !current, disabled: busy },
    ...primaries.map((t) => ({ key: `${t.content.contentId}-${t.toggleId}`, label: t.label, toggle: t, checked: t.on, disabled: busy || (!t.available && !t.on) || unpayable(t) })),
  ];
  const [roving, setRoving] = useState<string>();
  const elements = useRef(new Map<string, HTMLElement>());
  const checkedKey = options.find((o) => o.checked)?.key ?? 'base';
  const tabbable = options.some((o) => o.key === roving && !o.disabled) ? roving : checkedKey;

  // The roving stop follows a choice only once it has been made: when the change settles it goes back to the checked radio, which is
  // the chosen one after a success and the old one after a refusal. Leaving the group resets it too.
  const settle = (action: PlayAction) => void run(action).finally(() => setRoving(undefined));

  const choose = (option: Option) => {
    if (option.disabled || option.checked) return;
    if (!option.toggle) {
      if (current) settle({ action: 'toggleOff', ...ref(current) });
      return;
    }
    settle(current && !same(current, option.toggle) ? { action: 'toggleOn', ...ref(option.toggle), replace: true } : { action: 'toggleOn', ...ref(option.toggle) });
  };

  const onKeyDown = (event: KeyboardEvent, index: number) => {
    const enabled = options.flatMap((o, i) => (o.disabled ? [] : [i]));
    const at = enabled.indexOf(index);
    let target: number | undefined;
    if (event.key === 'ArrowDown' || event.key === 'ArrowRight') target = enabled[(at + 1) % enabled.length];
    else if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') target = enabled[(at - 1 + enabled.length) % enabled.length];
    else if (event.key === 'Home') target = enabled[0];
    else if (event.key === 'End') target = enabled[enabled.length - 1];
    else if (event.key === ' ' || event.key === 'Enter') {
      event.preventDefault();
      const option = options[index];
      if (option) choose(option);
      return;
    } else return;
    event.preventDefault();
    const next = target === undefined ? undefined : options[target];
    if (next) {
      setRoving(next.key);
      elements.current.get(next.key)?.focus(); // focus only: nothing is chosen until Space, Enter or a click
    }
  };

  return (
    <div className="form-group" aria-busy={pending || undefined}>
      <p id={`${id}-label`} className="option-name">
        {label}
      </p>
      <div
        role="radiogroup"
        aria-labelledby={`${id}-label`}
        onBlur={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget)) setRoving(undefined);
        }}
      >
        {options.map((option, index) => (
          <div key={option.key}>
            <div
              role="radio"
              className="form-option"
              aria-checked={option.checked}
              aria-disabled={option.disabled || undefined}
              aria-describedby={option.toggle ? reasonId(option.toggle) : undefined}
              tabIndex={option.key === tabbable ? 0 : -1}
              ref={(element) => {
                if (element) elements.current.set(option.key, element);
                else elements.current.delete(option.key);
              }}
              onClick={() => choose(option)}
              onKeyDown={(event) => onKeyDown(event, index)}
            >
              <span aria-hidden="true">{option.checked ? '(●) ' : '( ) '}</span>
              {option.label}
              {option.toggle && cost(option.toggle)}
            </div>
            {option.toggle && reason(option.toggle)}
          </div>
        ))}
      </div>
      {overlays.map((t) => (
        <div key={`${t.content.revisionId}-${t.toggleId}`}>
          <label className="choice">
            <input
              type="checkbox"
              checked={t.on}
              disabled={busy || (!t.available && !t.on) || unpayable(t)}
              aria-describedby={reasonId(t)}
              onChange={() => run({ action: t.on ? 'toggleOff' : 'toggleOn', ...ref(t) })}
            />
            {t.label}
            {cost(t)}
          </label>
          {reason(t)}
        </div>
      ))}
    </div>
  );
}
