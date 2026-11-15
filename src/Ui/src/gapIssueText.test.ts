import { expect, it } from 'vitest';
import type { GapNote } from './api/types';
import { gapIssueText } from './gapIssueText';

const note = (target: GapNote['target']): GapNote => ({
  id: 'note-id-1',
  characterId: 'char-id-1',
  target,
  text: 'The table grants a cover bonus here.',
  status: 'open',
  schemaVersion: 1,
  createdAt: '2026-11-01T00:00:00Z',
  updatedAt: '2026-11-01T00:00:00Z',
});

it('has the target label, the note text and the version line', () => {
  const text = gapIssueText(note({ kind: 'field', fieldId: 'ac', label: 'Armor Class' }), '0.8.1');
  expect(text).toBe('Gap note: Armor Class\nThe table grants a cover bonus here.\n\nTomeStack 0.8.1');
});

it('carries no id, even when the target holds ids', () => {
  const text = gapIssueText(note({ kind: 'feature', contentId: 'content-id-9', effectId: 'effect-id-9', label: 'Second Wind' }), '0.8.1');
  for (const id of ['note-id-1', 'char-id-1', 'content-id-9', 'effect-id-9']) expect(text).not.toContain(id);
});

it('falls back to a plain word when the target has no label', () => {
  expect(gapIssueText(note({ kind: 'field', fieldId: 'ac' }), '1.0.0')).toContain('Gap note: a field\n');
});
