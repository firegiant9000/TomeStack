import { useEffect, useRef } from 'react';
import type { CharacterSheet } from '../api/types';
import { checklistRows, onlyHitPoints, type LevelUpList } from '../levelUpChecklist';

const buttonLabel = { play: 'Show on Play', spells: 'Show on Spells', choices: 'Show choices' } as const;

/**
 * D74: "What changed at level N" on the Features tab. Rows are worded from the current sheet (R17); a row with a target links to
 * where the change lives (R19). `focusOnOpen` is true only on the save path: the heading takes focus once, on mount. The panel
 * adds no live region; moving focus to the heading is the announcement.
 */
export function LevelUpChecklist({
  checklist,
  sheet,
  focusOnOpen,
  spellsOffered = true,
  onGo,
  onDismiss,
}: {
  checklist: LevelUpList;
  sheet: CharacterSheet;
  /** R19: true only on the save path; the heading takes focus once, on mount. */
  focusOnOpen: boolean;
  /** False when the sheet does not offer the Spells tab: a Spells row then has no button. */
  spellsOffered?: boolean;
  onGo(target: 'play' | 'spells' | 'choices'): void;
  onDismiss(): void;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  // Once, on mount: a later render (a rest, a spend) must not take focus back.
  const takeFocus = useRef(focusOnOpen);
  useEffect(() => {
    if (takeFocus.current) heading.current?.focus();
  }, []);

  const rows = checklistRows(checklist, sheet).map((r) => (r.target === 'spells' && !spellsOffered ? { ...r, target: undefined } : r));
  const labelCount = new Map<string, number>();
  for (const row of rows) if (row.target) labelCount.set(row.target, (labelCount.get(row.target) ?? 0) + 1);

  return (
    <section aria-labelledby="level-up-heading" className="play-panel">
      <h3 id="level-up-heading" tabIndex={-1} ref={heading}>
        What changed at level {checklist.level}
      </h3>
      {rows.length > 0 && (
        <ul>
          {rows.map((row, i) => {
            const textId = `level-up-row-${i}`;
            const shared = row.target !== undefined && (labelCount.get(row.target) ?? 0) > 1;
            return (
              <li key={row.key}>
                <span id={textId}>{row.text}</span>
                {row.target && (
                  <>
                    {' '}
                    <button
                      type="button"
                      id={`${textId}-go`}
                      aria-labelledby={shared ? `${textId}-go ${textId}` : undefined}
                      onClick={() => onGo(row.target!)}
                    >
                      {buttonLabel[row.target]}
                    </button>
                  </>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {onlyHitPoints(rows) && <p className="hint">Nothing new besides hit points.</p>}
      {checklist.changes.length === 0 && <p className="hint">Nothing new at this level.</p>}
      {checklist.changes.length > 0 && rows.length === 0 && <p className="hint">These changes are no longer on the sheet.</p>}
      <button type="button" onClick={onDismiss}>
        Dismiss
      </button>
    </section>
  );
}
