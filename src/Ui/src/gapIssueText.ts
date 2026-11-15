import type { GapNote } from './api/types';

/** What the UI shows for a note whose service did not fill in a target label. */
const FALLBACK_LABEL = { feature: 'a feature', field: 'a field', import: 'an import' } as const;

/**
 * A gap note as text to paste into a GitHub issue: the target's label, the note and the app version. Guaranteed: it holds
 * no character name and no character id (or note/content/effect id field). Not guaranteed: the note's own text is the
 * person's and appears as written, and the label can fall back to "<feature>: <effect id>" for an unlabelled effect.
 */
export function gapIssueText(note: GapNote, version: string): string {
  const label = note.target.label?.trim() || FALLBACK_LABEL[note.target.kind] || 'something';
  return `Gap note: ${label}\n${note.text}\n\nTomeStack ${version}`;
}
