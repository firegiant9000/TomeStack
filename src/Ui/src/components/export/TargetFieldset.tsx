import { useId, useState } from 'react';
import type { PackageTargets } from '../../api/types';

interface Props {
  /** Null until the service has answered (or when it cannot): nothing is shown. */
  targets: PackageTargets | null;
  /** The chosen older release, or null for the file this TomeStack makes today. */
  value: string | null;
  onChange: (value: string | null) => void;
}

/** Which version will open the file: the current row first (everything), then each older release the service offers. */
export function TargetFieldset({ targets, value, onChange }: Props) {
  const name = useId();
  // Closed by default; opens when an older release is chosen. The user can still close it.
  const [open, setOpen] = useState(value !== null);
  const [prevValue, setPrevValue] = useState(value);
  if (value !== prevValue) {
    setPrevValue(value);
    if (value !== null) setOpen(true);
  }
  if (!targets) return null;
  if (targets.targets.length === 0) return <p className="hint">This TomeStack has no older version to save for.</p>;
  return (
    <details open={open} onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary>Opening this in an older TomeStack?</summary>
      <fieldset>
        <legend>Which version will open this file?</legend>
        <label>
          <input type="radio" name={name} checked={value === null} onChange={() => onChange(null)} />
          {targets.currentRow} or newer (everything)
        </label>
        {targets.targets.map((t) => (
          <label key={t.release}>
            <input type="radio" name={name} checked={value === t.release} onChange={() => onChange(t.release)} />
            {t.release}
          </label>
        ))}
        <p className="hint">Only the version before this one is listed. To open the file in an older TomeStack, update that TomeStack first.</p>
      </fieldset>
    </details>
  );
}
