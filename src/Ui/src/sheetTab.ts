/** ADR-014: the sheet's tabs, in strip order. "spells" is offered to casters, or when a spell field has a value or override. */
export const sheetTabIds = ['play', 'spells', 'inventory', 'features', 'stats', 'notes', 'manage'] as const;
export type SheetTabId = (typeof sheetTabIds)[number];

export const isSheetTabId = (value: unknown): value is SheetTabId => typeof value === 'string' && (sheetTabIds as readonly string[]).includes(value);

/**
 * ADR-014: the last tab opened for a character, a preference of this app on this machine. It lives in the page's own
 * storage (the WebView2 profile in the data folder), so it is in no package, share or library backup, and nothing else
 * reads it. Same pattern as `designFeedback.ts`.
 */
const key = (characterId: string) => `tomestack.sheetTab.${characterId}`;

export function rememberedSheetTab(characterId: string): SheetTabId | undefined {
  try {
    const stored = globalThis.localStorage?.getItem(key(characterId));
    return isSheetTabId(stored) ? stored : undefined;
  } catch {
    return undefined; // storage unavailable: no memory
  }
}

export function rememberSheetTab(characterId: string, tab: SheetTabId): void {
  try {
    globalThis.localStorage?.setItem(key(characterId), tab);
  } catch {
    // storage unavailable: the choice lasts for this sheet only
  }
}
