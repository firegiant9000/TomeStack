// @vitest-environment jsdom
// The remembered tab lives in the page's own storage, per character, and bad or missing storage never breaks the sheet.
import { afterEach, expect, it, vi } from 'vitest';
import { rememberSheetTab, rememberedSheetTab } from './sheetTab';

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

it('remembers a tab per character and forgets nothing else', () => {
  expect(rememberedSheetTab('fixture-a')).toBeUndefined();
  rememberSheetTab('fixture-a', 'manage');
  rememberSheetTab('fixture-b', 'spells');
  expect(rememberedSheetTab('fixture-a')).toBe('manage');
  expect(rememberedSheetTab('fixture-b')).toBe('spells');
  expect(localStorage.getItem('tomestack.sheetTab.fixture-a')).toBe('manage');
});

it('ignores a stored value that is not a tab', () => {
  localStorage.setItem('tomestack.sheetTab.fixture-a', 'biography');
  expect(rememberedSheetTab('fixture-a')).toBeUndefined();
});

it('treats a throwing storage as empty and swallows write failures', () => {
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new Error('blocked');
  });
  expect(rememberedSheetTab('fixture-a')).toBeUndefined();
  expect(() => rememberSheetTab('fixture-a', 'play')).not.toThrow();
});
