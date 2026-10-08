import type { RollRecord } from './api/types';

/** "{label}: {total} ({formula}, advantage|disadvantage, critical)", exactly as the Last roll region shows it. */
export function rollHeadline(record: RollRecord): string {
  const mode = record.mode !== 'normal' ? `, ${record.mode}` : '';
  return `${record.provenance?.label ?? 'Roll'}: ${record.total} (${record.formula}${mode}${record.critical ? ', critical' : ''})`;
}

/** "Dice: d20 14, d20 7 (dropped) · constant N · {modifier} +N …" (no source citation; the region adds that itself). */
export function diceLine(record: RollRecord): string {
  const dice = record.dice.map((d) => `d${d.sides} ${d.value}${d.kept ? '' : ' (dropped)'}${d.fromCritical ? ' (critical)' : ''}`).join(', ');
  const constant = record.expressionConstant !== 0 ? ` · constant ${record.expressionConstant}` : '';
  const modifiers = record.modifiers.map((m) => ` · ${m.label} ${m.amount >= 0 ? '+' : ''}${m.amount}`).join('');
  return `Dice: ${dice}${constant}${modifiers}`;
}
