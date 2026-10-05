// @vitest-environment jsdom
// Preferences of this app on this machine (theme, dice animation, sidebar) live in the page's own storage; bad or
// missing storage never breaks the app, and an unknown theme falls back to the default.
import { afterEach, expect, it, vi } from 'vitest';
import { applyTheme, diceAnimationOn, setDiceAnimation, setSidebarCollapsed, setTheme, sidebarCollapsed, theme } from './settings';

afterEach(() => {
  localStorage.clear();
  delete document.documentElement.dataset.theme;
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
