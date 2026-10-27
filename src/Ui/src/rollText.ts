import type { RollRecord } from './api/types';

/** The mode part of the headline, with a leading comma or empty. D43 S6: a mode the sheet applied is named with its sources. */
export function rollModeText(record: RollRecord): string {
  const mode = record.mode !== 'normal' ? `, ${record.mode}` : '';
  const sources = (record.modeSources ?? []).join(', ');
  if (!record.automaticMode || !sources) return mode;
  if (record.automaticMode === 'normal') return `${mode}, automatic mode normal: advantage and disadvantage cancel (${sources})`;
  return record.automaticMode === record.mode ? `${mode} from ${sources}` : `${mode}, automatic ${record.automaticMode} from ${sources}`;
}

/** "{label}: {total} ({formula}, advantage|disadvantage, critical)", exactly as the Last roll region shows it. */
export function rollHeadline(record: RollRecord): string {
  return `${record.provenance?.label || 'Roll'}: ${record.total} (${record.formula}${rollModeText(record)}${record.critical ? ', critical' : ''})`;
}

/** "Dice: d20 14, d20 7 (dropped) · constant N · {modifier} +N …" (no source citation; the region adds that itself). */
export function diceLine(record: RollRecord): string {
  const dice = record.dice.map((d) => `d${d.sides} ${d.value}${d.kept ? '' : ' (dropped)'}${d.fromCritical ? ' (critical)' : ''}`).join(', ');
  const constant = record.expressionConstant !== 0 ? ` · constant ${record.expressionConstant}` : '';
  const modifiers = record.modifiers.map((m) => ` · ${m.label} ${m.amount >= 0 ? '+' : ''}${m.amount}`).join('');
  return `Dice: ${dice}${constant}${modifiers}`;
}
