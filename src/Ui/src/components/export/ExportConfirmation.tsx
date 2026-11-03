import type { ExportChange } from '../../api/types';

interface Props {
  changes?: ExportChange[];
  confirmed: boolean;
  onConfirmedChange: (confirmed: boolean) => void;
  /** Id of the blocked hint: the panel's disabled Export button points its aria-describedby here. */
  hintId: string;
}

/** The copy leaves something out (dropped or not included): the sender must tick before it is made. */
export function needsConfirmation(changes?: ExportChange[]): boolean {
  return (changes ?? []).some((c) => c.action === 'dropped' || c.action === 'leftOut');
}

/** Something stops the copy being made at all. */
export function hasBlocker(changes?: ExportChange[]): boolean {
  return (changes ?? []).some((c) => c.action === 'blocks');
}

export function ExportConfirmation({ changes, confirmed, onConfirmedChange, hintId }: Props) {
  return (
    <>
      {needsConfirmation(changes) && (
        <label>
          <input type="checkbox" checked={confirmed} onChange={(e) => onConfirmedChange(e.target.checked)} />
          I understand this copy leaves out the items listed, and my own copy is unchanged
        </label>
      )}
      {hasBlocker(changes) && <p id={hintId}>This file is not made while something blocks it.</p>}
    </>
  );
}
