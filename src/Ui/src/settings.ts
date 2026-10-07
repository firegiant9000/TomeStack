/**
 * ADR-015: preferences of this app on this machine. They live in the page's own storage (the WebView2 profile in the
 * data folder), so they are in no package, share or library backup, and nothing else reads them. Same pattern as
 * `designFeedback.ts` and `sheetTab.ts`: a missing or throwing storage means the default.
 */
export const themeIds = ['cool', 'forest', 'violet'] as const;
export type ThemeId = (typeof themeIds)[number];

const isThemeId = (value: unknown): value is ThemeId => typeof value === 'string' && (themeIds as readonly string[]).includes(value);

const keys = {
  theme: 'tomestack.theme',
  dice: 'tomestack.diceAnimation',
  sidebar: 'tomestack.sidebar',
  appearance: 'tomestack.appearance',
  textSize: 'tomestack.textSize',
  abilityOrder: 'tomestack.abilityOrder',
  motion: 'tomestack.motion',
  contrast: 'tomestack.contrast',
  focus: 'tomestack.focus',
  targets: 'tomestack.targets',
  underline: 'tomestack.underline',
  announceRolls: 'tomestack.announceRolls',
  compactPlay: 'tomestack.compactPlay',
} as const;

/** This session's choices, read only while storage is unavailable, so a pick still applies and lasts until the app closes. */
const sessionChoices = new Map<string, string | null>();

/** Test-only: forget the session choices so one unit test cannot leak into the next. */
export function forgetSessionChoices(): void {
  sessionChoices.clear();
}

function read(key: string): string | null {
  try {
    const storage = globalThis.localStorage;
    if (!storage) return sessionChoices.get(key) ?? null;
    return storage.getItem(key);
  } catch {
    return sessionChoices.get(key) ?? null; // storage unavailable: this session's choice, else the default
  }
}

function write(key: string, value: string | null): void {
  sessionChoices.set(key, value);
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

/**
 * Investigation 2026-10-06 (items 4, 7, 10; owner answers 3, 8, 9). Each preference is one page-storage key applied as one
 * `html[data-*]` attribute that plain CSS keys on. The default means "no attribute", so the CSS base rules apply unchanged.
 */
function oneOf<T extends string>(ids: readonly T[], stored: string | null, fallback: T): T {
  return (ids as readonly string[]).includes(stored ?? '') ? (stored as T) : fallback;
}

function applyAttribute(name: 'appearance' | 'textSize' | 'abilityOrder' | 'motion' | 'contrast' | 'focus' | 'targets' | 'underline' | 'compactPlay', value: string | undefined): void {
  if (value === undefined) delete document.documentElement.dataset[name];
  else document.documentElement.dataset[name] = value;
}

/** Colour scheme: `system` follows Windows (no attribute); `light`/`dark` set `color-scheme` on `html`, so `light-dark()` resolves without the OS. */
export const appearanceIds = ['system', 'light', 'dark'] as const;
export type AppearanceId = (typeof appearanceIds)[number];
export const appearance = (): AppearanceId => oneOf(appearanceIds, read(keys.appearance), 'system');
export const setAppearance = (id: AppearanceId): void => write(keys.appearance, id === 'system' ? null : id);
export const applyAppearance = (id: AppearanceId): void => applyAttribute('appearance', id === 'system' ? undefined : id);

/** Root font size in percent; everything is rem, so this scales the whole UI (WCAG 1.4.4). Browser zoom multiplies with it. */
export const textSizeIds = ['90', '100', '110', '125', '150', '175', '200'] as const;
export type TextSizeId = (typeof textSizeIds)[number];
export const textSize = (): TextSizeId => oneOf(textSizeIds, read(keys.textSize), '100');
export const setTextSize = (id: TextSizeId): void => write(keys.textSize, id === '100' ? null : id);
export const applyTextSize = (id: TextSizeId): void => applyAttribute('textSize', id === '100' ? undefined : id);

/** Which number is large in a summary ability box: the modifier (the paper convention) or the score. One markup; CSS `order`. */
export const abilityOrderIds = ['modifier', 'score'] as const;
export type AbilityOrderId = (typeof abilityOrderIds)[number];
export const abilityOrder = (): AbilityOrderId => oneOf(abilityOrderIds, read(keys.abilityOrder), 'modifier');
export const setAbilityOrder = (id: AbilityOrderId): void => write(keys.abilityOrder, id === 'modifier' ? null : id);
export const applyAbilityOrder = (id: AbilityOrderId): void => applyAttribute('abilityOrder', id === 'modifier' ? undefined : id);

/** Accessibility settings (investigation 2026-10-06 item 11). "System"/default means no attribute: the OS media queries rule. */
export const motionIds = ['system', 'reduce'] as const;
export type MotionId = (typeof motionIds)[number];
export const motion = (): MotionId => oneOf(motionIds, read(keys.motion), 'system');
export const setMotion = (id: MotionId): void => write(keys.motion, id === 'system' ? null : id);

export const contrastIds = ['default', 'more'] as const;
export type ContrastId = (typeof contrastIds)[number];
export const contrast = (): ContrastId => oneOf(contrastIds, read(keys.contrast), 'default');
export const setContrast = (id: ContrastId): void => write(keys.contrast, id === 'default' ? null : id);

export const focusRingIds = ['auto', 'always'] as const;
export type FocusRingId = (typeof focusRingIds)[number];
export const focusRing = (): FocusRingId => oneOf(focusRingIds, read(keys.focus), 'auto');
export const setFocusRing = (id: FocusRingId): void => write(keys.focus, id === 'auto' ? null : id);

export const targetsIds = ['default', 'large'] as const;
export type TargetsId = (typeof targetsIds)[number];
export const targets = (): TargetsId => oneOf(targetsIds, read(keys.targets), 'default');
export const setTargets = (id: TargetsId): void => write(keys.targets, id === 'default' ? null : id);

export const underlineIds = ['off', 'on'] as const;
export type UnderlineId = (typeof underlineIds)[number];
export const underline = (): UnderlineId => oneOf(underlineIds, read(keys.underline), 'off');
export const setUnderline = (id: UnderlineId): void => write(keys.underline, id === 'off' ? null : id);

/** Off removes `aria-live` from the Last roll region (the text stays): a screen-reader user then reads it on demand (4.1.3). */
export const announceRollsOn = (): boolean => read(keys.announceRolls) !== 'off';
export const setAnnounceRolls = (on: boolean): void => write(keys.announceRolls, on ? null : 'off');

/** D20: the compact combat layout of the Play tab (CSS hides traces, hints and class columns). Off is the default: no attribute. */
export const compactPlay = (): boolean => read(keys.compactPlay) === 'on';
export const setCompactPlay = (on: boolean): void => write(keys.compactPlay, on ? 'on' : null);

/** Applied once at start (`App.tsx`) and by the Settings screen after every change. */
export function applyPreferences(): void {
  applyTheme(theme());
  applyAppearance(appearance());
  applyTextSize(textSize());
  applyAbilityOrder(abilityOrder());
  applyAttribute('motion', motion() === 'system' ? undefined : 'reduce');
  applyAttribute('contrast', contrast() === 'default' ? undefined : 'more');
  applyAttribute('focus', focusRing() === 'auto' ? undefined : 'always');
  applyAttribute('targets', targets() === 'default' ? undefined : 'large');
  applyAttribute('underline', underline() === 'off' ? undefined : 'on');
  applyAttribute('compactPlay', compactPlay() ? 'on' : undefined);
}
