/**
 * Item 10: the mirror of the service's `SpellListKey` (RulesCore). A key is trimmed, lower-cased, and each run of spaces
 * becomes one hyphen; it is valid at 1 to 60 characters of a-z, 0-9 and hyphen. The service normalises and validates again,
 * so this only previews what will be saved.
 */
export const normaliseSpellListKey = (text: string): string => text.trim().toLowerCase().replace(/\s+/g, '-');

export const isValidSpellListKey = (key: string): boolean => /^[a-z0-9-]{1,60}$/.test(key);

/** Why a typed key cannot be saved, in words; undefined when it can. */
export function spellListKeyProblem(text: string): string | undefined {
  const key = normaliseSpellListKey(text);
  if (key === '') return 'Type a spell list key.';
  return isValidSpellListKey(key) ? undefined : 'A spell list key is 1 to 60 letters, digits or hyphens.';
}
