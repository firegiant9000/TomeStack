import type { DerivedValue, RollModeReason } from './api/types';

const word = (mode: string) => mode.charAt(0).toUpperCase() + mode.slice(1);

/** The reasons the service applied by itself: it says so on each reason (`automatic`). */
export function automaticReasons(value: DerivedValue): RollModeReason[] {
  return value.mode?.reasons.filter((r) => r.automatic) ?? [];
}

/** " (advantage: A, B)", " (normal: advantage and disadvantage cancel)" or "" when no reason applies automatically. */
export function rollButtonSuffix(value: DerivedValue): string {
  const reasons = automaticReasons(value);
  if (reasons.length === 0 || !value.mode) return '';
  if (value.mode.automatic === 'normal') return ' (normal: advantage and disadvantage cancel)';
  return ` (${value.mode.automatic}: ${reasons.map((r) => r.source).join(', ')})`;
}

/** One line per reason the player confirms or the sheet only lists, for the field's note. */
export function assistedNotes(value: DerivedValue): string[] {
  if (!value.mode) return [];
  const automatic = new Set(automaticReasons(value));
  return value.mode.reasons
    .filter((r) => !automatic.has(r))
    .map((r) =>
      r.against && r.against.length > 0
        ? `${word(r.mode)} against ${r.against.join(', ')} (${r.source})`
        : `${word(r.mode)} (${r.source}), not applied automatically`,
    );
}
