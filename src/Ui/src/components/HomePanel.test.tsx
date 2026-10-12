// @vitest-environment jsdom
// The Characters home screen (item 3B, D25): one card per active character, "Open {name}" buttons, an empty state.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterSummary } from '../api/types';
import { HomePanel } from './HomePanel';

afterEach(cleanup);

const summaries: CharacterSummary[] = [
  { id: 'a', name: 'Fixture Pell', rulesFamily: 'srd-5.1', level: 4, updatedAt: '2026-10-05T10:00:00Z' },
  { id: 'b', name: 'Fixture Quill', rulesFamily: 'srd-5.2.1', level: 1, updatedAt: '2026-10-06T10:00:00Z', archivedAt: '2026-10-06T11:00:00Z' },
];

it('lists active characters as cards with level and last change, newest first, and opens one', async () => {
  const user = userEvent.setup();
  const onOpen = vi.fn();
  render(<HomePanel characters={summaries} onOpen={onOpen} onNew={() => {}} canCreate />);
  expect(document.activeElement).toBe(screen.getByRole('heading', { level: 2, name: 'Characters' }));
  const cards = within(screen.getByRole('list', { name: 'Characters' })).getAllByRole('listitem');
  expect(cards).toHaveLength(1); // the archived one is not a card
  expect(cards[0]!.textContent).toContain('Level 4');
  expect(cards[0]!.textContent).toContain('srd-5.1');
  await user.click(within(cards[0]!).getByRole('button', { name: 'Open Fixture Pell' }));
  expect(onOpen).toHaveBeenCalledWith('a');
});

it('does not take focus on mount when focusOnMount is false (the app start screen)', () => {
  render(<HomePanel characters={summaries} onOpen={() => {}} onNew={() => {}} canCreate focusOnMount={false} />);
  expect(document.activeElement).not.toBe(screen.getByRole('heading', { level: 2, name: 'Characters' }));
});

it('orders cards newest change first', () => {
  const two: CharacterSummary[] = [
    { id: 'a', name: 'Fixture Pell', rulesFamily: 'srd-5.1', level: 4, updatedAt: '2026-10-05T10:00:00Z' },
    { id: 'c', name: 'Fixture Rook', rulesFamily: 'srd-5.1', level: 2, updatedAt: '2026-10-07T10:00:00Z' },
  ];
  render(<HomePanel characters={two} onOpen={() => {}} onNew={() => {}} canCreate />);
  const cards = within(screen.getByRole('list', { name: 'Characters' })).getAllByRole('listitem');
  expect(cards[0]!.textContent).toContain('Fixture Rook');
});

it('shows the empty state and a Create a character button when there is nothing to open', async () => {
  const user = userEvent.setup();
  const onNew = vi.fn();
  render(<HomePanel characters={[]} onOpen={() => {}} onNew={onNew} canCreate />);
  expect(screen.getByText(/No characters yet\./)).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Create a character' }));
  expect(onNew).toHaveBeenCalled();
});
