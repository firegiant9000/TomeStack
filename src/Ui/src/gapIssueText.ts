import type { GapNote } from './api/types';

/** What the UI shows for a note whose service did not fill in a target label. */
const FALLBACK_LABEL = { feature: 'a feature', field: 'a field', import: 'an import' } as const;

/**
 * A gap note as text to paste into a GitHub issue: the target's label, the note and the app version. It never carries the
 * character's name or id, or any other id (the label is a feature or field name from the sheet, not an identifier).
 */
export function gapIssueText(note: GapNote, version: string): string {
  const label = note.target.label?.trim() || FALLBACK_LABEL[note.target.kind] || 'something';
  return `Gap note: ${label}\n${note.text}\n\nTomeStack ${version}`;
}
