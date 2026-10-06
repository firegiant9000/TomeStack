/**
 * ADR-015: preferences of this app on this machine. They live in the page's own storage (the WebView2 profile in the
 * data folder), so they are in no package, share or library backup, and nothing else reads them. Same pattern as
 * `designFeedback.ts` and `sheetTab.ts`: a missing or throwing storage means the default.
 */
export const themeIds = ['cool', 'forest', 'violet'] as const;
export type ThemeId = (typeof themeIds)[number];

const isThemeId = (value: unknown): value is ThemeId => typeof value === 'string' && (themeIds as readonly string[]).includes(value);

const keys = { theme: 'tomestack.theme', dice: 'tomestack.diceAnimation', sidebar: 'tomestack.sidebar' } as const;

function read(key: string): string | null {
  try {
    return globalThis.localStorage?.getItem(key) ?? null;
  } catch {
    return null; // storage unavailable: the default
  }
}

function write(key: string, value: string | null): void {
  try {
    if (value === null) globalThis.localStorage?.removeItem(key);
    else globalThis.localStorage?.setItem(key, value);
  } catch {
    // storage unavailable: the choice lasts for this session only
  }
}

export function theme(): ThemeId {
  const stored = read(keys.theme);
  return isThemeId(stored) ? stored : 'forest'; // owner, 2026-10-05: forest is the default
}

export function setTheme(id: ThemeId): void {
  write(keys.theme, id === 'forest' ? null : id);
}

/** The themes are CSS: `:root` holds forest; `html[data-theme='cool']` and `html[data-theme='violet']` override it. */
export function applyTheme(id: ThemeId): void {
  document.documentElement.dataset.theme = id;
}

export function diceAnimationOn(): boolean {
  return read(keys.dice) !== 'off';
}

export function setDiceAnimation(on: boolean): void {
  write(keys.dice, on ? null : 'off');
}

export function sidebarCollapsed(): boolean {
  return read(keys.sidebar) === 'collapsed';
}

export function setSidebarCollapsed(collapsed: boolean): void {
  write(keys.sidebar, collapsed ? 'collapsed' : null);
}
