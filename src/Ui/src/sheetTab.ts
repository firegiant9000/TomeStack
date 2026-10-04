/** ADR-014: the sheet's tabs, in strip order. "spells" is offered to casters, or when a spell field has a value or override. */
export const sheetTabIds = ['play', 'spells', 'inventory', 'features', 'stats', 'notes', 'manage'] as const;
export type SheetTabId = (typeof sheetTabIds)[number];

export const isSheetTabId = (value: unknown): value is SheetTabId => typeof value === 'string' && (sheetTabIds as readonly string[]).includes(value);
