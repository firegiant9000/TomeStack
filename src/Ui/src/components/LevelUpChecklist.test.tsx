// @vitest-environment jsdom
// D74: the "What changed at level N" panel. Values are invented.
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import type { CharacterSheet, ChoiceStatus } from '../api/types';
import type { LevelUpList } from '../levelUpChecklist';
import { LevelUpChecklist } from './LevelUpChecklist';

afterEach(cleanup);

const choice = (over: Partial<ChoiceStatus> = {}): ChoiceStatus => ({
  source: { contentId: 'fixture.feature', revisionId: 'r1' } as ChoiceStatus['source'],
  sourceName: 'Fixture feature',
  choiceId: 'pick',
  count: 1,
  options: [],
  selected: [],
  resolved: false,
  ...over,
});
const sheetOf = (over: Partial<CharacterSheet> = {}) => ({ characterId: 'fixture-2', rulesFamily: 'srd-5.1', diagnostics: [], fields: [], ...over }) as unknown as CharacterSheet;
const hp = (after: number): LevelUpList['changes'][number] => ({ kind: 'hitPoints', before: 8, after });
const handlers = () => ({ onGo: vi.fn(), onDismiss: vi.fn() });

function show(list: LevelUpList, sheet = sheetOf(), focusOnOpen = false, h = handlers()) {
  const view = render(<LevelUpChecklist checklist={list} sheet={sheet} focusOnOpen={focusOnOpen} {...h} />);
  return { ...view, ...h };
}

it('is a region named for the level, with one list item per row', () => {
  show({ level: 5, changes: [hp(12), { kind: 'slots', level: 1, before: 2, after: 3 }] });
  const region = screen.getByRole('region', { name: 'What changed at level 5' });
  const items = within(within(region).getByRole('list')).getAllByRole('listitem');
  expect(items.map((i) => i.querySelector('span')?.textContent)).toEqual(['Hit point maximum: 8 → 12', 'Level 1 spell slots: 2 → 3']);
});

it('focuses the heading on open only when asked', () => {
  show({ level: 5, changes: [hp(12)] }, sheetOf(), true);
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'What changed at level 5' }));
  cleanup();
  show({ level: 5, changes: [hp(12)] });
  expect(document.activeElement).toBe(document.body);
});

it('does not refocus the heading when the sheet re-renders', () => {
  const list: LevelUpList = { level: 5, changes: [hp(12)] };
  const h = handlers();
  const { rerender } = render(<LevelUpChecklist checklist={list} sheet={sheetOf()} focusOnOpen {...h} />);
  const other = document.createElement('button');
  document.body.append(other);
  other.focus();
  rerender(<LevelUpChecklist checklist={list} sheet={sheetOf()} focusOnOpen {...h} />);
  expect(document.activeElement).toBe(other);
  other.remove();
});

it('the row buttons call onGo with their target', () => {
  const { onGo } = show({ level: 5, changes: [{ kind: 'slots', level: 1, before: 0, after: 2 }] });
  fireEvent.click(screen.getByRole('button', { name: 'Show on Spells' }));
  expect(onGo).toHaveBeenCalledWith('spells');
});

it('Show on Play calls onGo(play)', () => {
  const { onGo } = show({ level: 5, changes: [hp(12)] });
  fireEvent.click(screen.getByRole('button', { name: 'Show on Play' }));
  expect(onGo).toHaveBeenCalledWith('play');
});

it('an unresolved choice offers Show choices', () => {
  const { onGo } = show({ level: 5, changes: [{ kind: 'choice', contentId: 'fixture.feature', choiceId: 'pick' }] }, sheetOf({ choices: [choice()] }));
  fireEvent.click(screen.getByRole('button', { name: 'Show choices' }));
  expect(onGo).toHaveBeenCalledWith('choices');
});

it('buttons that would share a name get the row text in their name', () => {
  show({ level: 5, changes: [hp(12), { kind: 'slots', level: 1, before: 0, after: 2 }, { kind: 'slots', level: 2, before: 0, after: 1 }] });
  const buttons = screen.getAllByRole('button').filter((b) => b.textContent === 'Show on Spells');
  expect(buttons).toHaveLength(2);
  const names = buttons.map((b) => b.getAttribute('aria-labelledby')?.split(' ').map((id) => document.getElementById(id)?.textContent).join(' '));
  expect(names).toEqual(['Show on Spells Level 1 spell slots: 0 → 2', 'Show on Spells Level 2 spell slots: 0 → 1']);
  expect(screen.getByRole('button', { name: 'Show on Play' }).getAttribute('aria-labelledby')).toBeNull();
});

it('says when only hit points changed', () => {
  show({ level: 5, changes: [hp(12)] });
  expect(screen.getByText('Nothing new besides hit points.')).toBeTruthy();
  expect(screen.queryByText('Nothing new at this level.')).toBeNull();
});

it('says when nothing changed, and when every change has gone from the sheet', () => {
  show({ level: 5, changes: [] });
  expect(screen.getByText('Nothing new at this level.')).toBeTruthy();
  expect(screen.queryByRole('list')).toBeNull();
  cleanup();
  show({ level: 5, changes: [{ kind: 'feature', contentId: 'gone' }] });
  expect(screen.getByText('These changes are no longer on the sheet.')).toBeTruthy();
});

it('Dismiss calls onDismiss', () => {
  const { onDismiss } = show({ level: 5, changes: [hp(12)] });
  fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
  expect(onDismiss).toHaveBeenCalledTimes(1);
});
