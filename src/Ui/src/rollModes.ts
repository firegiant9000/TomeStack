import type { DerivedValue, RollModeReason, RollModeSummary } from './api/types';

const word = (mode: string) => mode.charAt(0).toUpperCase() + mode.slice(1);

/** The reasons the service applied by itself: it says so on each reason (`automatic`). */
export function automaticReasons(value: { mode?: RollModeSummary }): RollModeReason[] {
  return value.mode?.reasons.filter((r) => r.automatic) ?? [];
}

/** " (advantage: A, B)", " (normal: advantage and disadvantage cancel)" or "" when no reason applies automatically. Takes a field or an attack. */
export function rollButtonSuffix(value: { mode?: RollModeSummary }): string {
  const reasons = automaticReasons(value);
  if (reasons.length === 0 || !value.mode) return '';
  if (value.mode.automatic === 'normal') return ' (normal: advantage and disadvantage cancel)';
  return ` (${value.mode.automatic}: ${reasons.map((r) => r.source).join(', ')})`;
}

/** One line per reason the player confirms or the sheet only lists, for the field's (or attack's) note. */
export function assistedNotes(value: DerivedValue | { mode?: RollModeSummary }): string[] {
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

/**
 * Content v12 (G7): what the sheet says about attacks other creatures make against the character, e.g.
 * "have advantage (A, B); can't have disadvantage (C)". `automatic` already applies a prevent; a prevent reason names the mode
 * attackers can't have. Empty when nothing is to be said.
 */
export function attacksAgainstText(summary: RollModeSummary | undefined): string {
  if (!summary) return '';
  const parts: string[] = [];
  if (summary.automatic !== 'normal') {
    const sources = summary.reasons.filter((r) => r.automatic && !r.prevent && r.mode === summary.automatic).map((r) => r.source);
    parts.push(`have ${summary.automatic}${sources.length > 0 ? ` (${sources.join(', ')})` : ''}`);
  }
  for (const mode of ['advantage', 'disadvantage'] as const) {
    const sources = summary.reasons.filter((r) => r.automatic && r.prevent && r.mode === mode).map((r) => r.source);
    if (sources.length > 0) parts.push(`can't have ${mode} (${sources.join(', ')})`);
  }
  return parts.join('; ');
}
