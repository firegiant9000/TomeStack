import { useId } from 'react';
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
  if (!targets || targets.targets.length === 0) return null;
  return (
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
    </fieldset>
  );
}
