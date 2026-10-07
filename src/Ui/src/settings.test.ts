// @vitest-environment jsdom
// Preferences of this app on this machine (theme, dice animation, sidebar) live in the page's own storage; bad or
// missing storage never breaks the app, and an unknown theme falls back to the default.
import { afterEach, expect, it, vi } from 'vitest';
import {
  abilityOrder,
  announceRollsOn,
  appearance,
  applyPreferences,
  applyTheme,
  compactPlay,
  contrast,
  diceAnimationOn,
  focusRing,
  forgetSessionChoices,
  motion,
  setAbilityOrder,
  setAnnounceRolls,
  setAppearance,
  setCompactPlay,
  setContrast,
  setDiceAnimation,
  setFocusRing,
  setMotion,
  setSidebarCollapsed,
  setTargets,
  setTextSize,
  setTheme,
  setUnderline,
  sidebarCollapsed,
  targets,
  textSize,
  theme,
  underline,
} from './settings';

afterEach(() => {
  localStorage.clear();
  forgetSessionChoices();
  delete document.documentElement.dataset.theme;
  for (const key of Object.keys(document.documentElement.dataset)) delete document.documentElement.dataset[key];
  vi.restoreAllMocks();
});

it('defaults to the forest theme, dice on and the sidebar expanded', () => {
  expect(theme()).toBe('forest');
  expect(diceAnimationOn()).toBe(true);
  expect(sidebarCollapsed()).toBe(false);
});

it('remembers each preference under its own key', () => {
  setTheme('violet');
  setDiceAnimation(false);
  setSidebarCollapsed(true);
  expect(theme()).toBe('violet');
  expect(diceAnimationOn()).toBe(false);
  expect(sidebarCollapsed()).toBe(true);
  expect(localStorage.getItem('tomestack.theme')).toBe('violet');
  expect(localStorage.getItem('tomestack.diceAnimation')).toBe('off');
  expect(localStorage.getItem('tomestack.sidebar')).toBe('collapsed');
});

it('removes the key when a preference returns to its default', () => {
  setTheme('cool');
  setTheme('forest');
  expect(localStorage.getItem('tomestack.theme')).toBeNull();
  setDiceAnimation(false);
  setDiceAnimation(true);
  setSidebarCollapsed(true);
  setSidebarCollapsed(false);
  expect(localStorage.getItem('tomestack.diceAnimation')).toBeNull();
  expect(localStorage.getItem('tomestack.sidebar')).toBeNull();
});

it('ignores a stored theme that is not one of the three and never applies it', () => {
  localStorage.setItem('tomestack.theme', 'parchment');
  expect(theme()).toBe('forest');
  applyTheme(theme());
  expect(document.documentElement.dataset.theme).toBe('forest');
});

it('applies a theme as a data attribute on the root element', () => {
  applyTheme('violet');
  expect(document.documentElement.dataset.theme).toBe('violet');
});

it('treats a throwing storage as empty and swallows write failures', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  expect(theme()).toBe('forest');
  expect(diceAnimationOn()).toBe(true);
  expect(sidebarCollapsed()).toBe(false);
  expect(() => setTheme('cool')).not.toThrow();
  expect(() => setDiceAnimation(false)).not.toThrow();
  expect(() => setSidebarCollapsed(true)).not.toThrow();
});

it('defaults appearance to system, text size to 100 and ability boxes to modifier first, with no attribute on html', () => {
  expect(appearance()).toBe('system');
  expect(textSize()).toBe('100');
  expect(abilityOrder()).toBe('modifier');
  applyPreferences();
  expect(document.documentElement.dataset.appearance).toBeUndefined();
  expect(document.documentElement.dataset.textSize).toBeUndefined();
  expect(document.documentElement.dataset.abilityOrder).toBeUndefined();
  expect(document.documentElement.dataset.theme).toBe('forest');
});

it('remembers appearance, text size and ability order under their own keys and applies them as html attributes', () => {
  setAppearance('dark');
  setTextSize('150');
  setAbilityOrder('score');
  expect(localStorage.getItem('tomestack.appearance')).toBe('dark');
  expect(localStorage.getItem('tomestack.textSize')).toBe('150');
  expect(localStorage.getItem('tomestack.abilityOrder')).toBe('score');
  applyPreferences();
  expect(document.documentElement.dataset.appearance).toBe('dark');
  expect(document.documentElement.dataset.textSize).toBe('150');
  expect(document.documentElement.dataset.abilityOrder).toBe('score');
  setAppearance('system');
  setTextSize('100');
  setAbilityOrder('modifier');
  expect(localStorage.getItem('tomestack.appearance')).toBeNull();
  expect(localStorage.getItem('tomestack.textSize')).toBeNull();
  expect(localStorage.getItem('tomestack.abilityOrder')).toBeNull();
  applyPreferences();
  expect(document.documentElement.dataset.appearance).toBeUndefined();
  expect(document.documentElement.dataset.textSize).toBeUndefined();
});

it('ignores junk values for every new preference and never applies them', () => {
  localStorage.setItem('tomestack.appearance', 'sepia');
  localStorage.setItem('tomestack.textSize', 'huge');
  localStorage.setItem('tomestack.abilityOrder', 'random');
  expect(appearance()).toBe('system');
  expect(textSize()).toBe('100');
  expect(abilityOrder()).toBe('modifier');
  applyPreferences();
  expect(document.documentElement.dataset.appearance).toBeUndefined();
  expect(document.documentElement.dataset.textSize).toBeUndefined();
  expect(document.documentElement.dataset.abilityOrder).toBeUndefined();
});

it('keeps the session\'s pick and applies it when storage is unavailable', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  setAppearance('dark');
  applyPreferences();
  expect(document.documentElement.dataset.appearance).toBe('dark');
});

it('defaults the accessibility settings to the OS or off, and remembers each under its own key', () => {
  expect(motion()).toBe('system');
  expect(contrast()).toBe('default');
  expect(focusRing()).toBe('auto');
  expect(targets()).toBe('default');
  expect(underline()).toBe('off');
  expect(announceRollsOn()).toBe(true);
  setMotion('reduce');
  setContrast('more');
  setFocusRing('always');
  setTargets('large');
  setUnderline('on');
  setAnnounceRolls(false);
  expect(localStorage.getItem('tomestack.motion')).toBe('reduce');
  expect(localStorage.getItem('tomestack.contrast')).toBe('more');
  expect(localStorage.getItem('tomestack.focus')).toBe('always');
  expect(localStorage.getItem('tomestack.targets')).toBe('large');
  expect(localStorage.getItem('tomestack.underline')).toBe('on');
  expect(localStorage.getItem('tomestack.announceRolls')).toBe('off');
  applyPreferences();
  expect(document.documentElement.dataset).toMatchObject({ motion: 'reduce', contrast: 'more', focus: 'always', targets: 'large', underline: 'on' });
  expect(document.documentElement.dataset.announceRolls).toBeUndefined(); // read by the component, not the CSS
  expect(compactPlay()).toBe(false);
  setCompactPlay(true);
  expect(localStorage.getItem('tomestack.compactPlay')).toBe('on');
  applyPreferences();
  expect(document.documentElement.dataset.compactPlay).toBe('on');
  setCompactPlay(false);
  applyPreferences();
  expect(document.documentElement.dataset.compactPlay).toBeUndefined();
});
