/** ADR-014: the sheet's tabs, in strip order. "spells" is offered only to casters. */
export const sheetTabIds = ['play', 'spells', 'inventory', 'features', 'stats', 'notes', 'manage'] as const;
export type SheetTabId = (typeof sheetTabIds)[number];

export const isSheetTabId = (value: unknown): value is SheetTabId => typeof value === 'string' && (sheetTabIds as readonly string[]).includes(value);
