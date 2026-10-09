/** Lower-case, accents stripped (NFD then combining marks removed), trimmed. */
export const foldName = (s: string): string => s.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().trim();

/** D30: a substring match on the folded name; an empty (or marks-only) query matches every spell. */
export function matchesSpell(name: string, query: string): boolean {
  const q = foldName(query);
  return q === '' || foldName(name).includes(q);
}
