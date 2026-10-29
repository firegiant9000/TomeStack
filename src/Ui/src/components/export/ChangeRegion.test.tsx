// @vitest-environment jsdom
// "Save for an older TomeStack": what the copy for a chosen target changes, or why the default file needs a newer TomeStack.
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import type { ExportChange, OpensInCause } from '../../api/types';
import { expectNoAxeViolations } from '../../test/axe';
import { ChangeRegion, isNewerThan } from './ChangeRegion';

afterEach(cleanup);

function change(action: ExportChange['action'], name: string, message: string): ExportChange {
  return { entry: `entry-${name}`, kind: 'content', id: `id-${name}`, name, action, code: `code.${action}`, message };
}

const causes: OpensInCause[] = [
  { entry: 'characters/compat-hero.json', kind: 'character', id: 'compat-hero', name: 'Compat Hero', schemaVersion: 10, message: 'Compat Hero needs TomeStack 0.7.0.' },
];

it('lists each non-empty group under its heading, in order, with the message text', () => {
  const changes = [
    change('copied', 'Compat Homebrew A', 'Compat Homebrew A gets a new id.'),
    change('leftOut', 'Compat Tide Stance', 'Compat Tide Stance is not in this copy.'),
    change('dropped', 'Compat Steady Hands', 'Compat Steady Hands is left out.'),
    change('blocks', 'Compat Hero', 'Compat Hero cannot be saved for 0.6.0.'),
  ];
  render(<ChangeRegion target="0.6.0" changes={changes} previousMinor="0.6.0" />);
  const region = screen.getByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  const lists = within(region).getAllByRole('list');
  // Each list is named by its heading, so a screen reader hears the heading with the list.
  const headings = lists.map((l) => document.getElementById(l.getAttribute('aria-labelledby') ?? '')?.textContent);
  expect(headings).toEqual(['Blocks this copy:', 'Left out of this copy:', 'Not in this copy:', 'Copied with a new id:']);
  expect(within(region).getByRole('list', { name: 'Left out of this copy:' })).toBe(lists[1]);
  expect(lists.map((l) => within(l).getAllByRole('listitem').map((i) => i.textContent))).toEqual([
    ['Compat Hero cannot be saved for 0.6.0.'],
    ['Compat Steady Hands is left out.'],
    ['Compat Tide Stance is not in this copy.'],
    ['Compat Homebrew A gets a new id.'],
  ]);
  expect(within(region).queryByText('Nothing is left out of this copy.')).toBeNull();
});

it('omits a group that is empty', () => {
  render(<ChangeRegion target="0.5.0" changes={[change('dropped', 'Compat Steady Hands', 'Left out.')]} previousMinor="0.6.0" />);
  expect(screen.getByText('Left out of this copy:')).toBeTruthy();
  expect(screen.queryByText('Blocks this copy:')).toBeNull();
  expect(screen.queryByText('Not in this copy:')).toBeNull();
  expect(screen.queryByText('Copied with a new id:')).toBeNull();
});

it('says nothing is left out for an empty or missing changes list', () => {
  const { rerender } = render(<ChangeRegion target="0.6.0" changes={[]} previousMinor="0.6.0" />);
  expect(screen.getByText('Nothing is left out of this copy.')).toBeTruthy();
  rerender(<ChangeRegion target="0.6.0" previousMinor="0.6.0" />);
  expect(screen.getByText('Nothing is left out of this copy.')).toBeTruthy();
});

it('with no target names the region Compatibility and shows the opens-in line', () => {
  render(<ChangeRegion target={null} opensIn="0.5.0" causes={causes} previousMinor="0.6.0" />);
  const region = screen.getByRole('region', { name: 'Compatibility' });
  expect(within(region).getByText('This file opens in TomeStack 0.5.0 or newer.')).toBeTruthy();
});

it('shows the causes only when the file needs something newer than the previous minor', () => {
  const shows = (opensIn: string | null) => {
    const { unmount } = render(<ChangeRegion target={null} opensIn={opensIn} causes={causes} previousMinor="0.6.0" />);
    const found = screen.queryByText('Compat Hero needs TomeStack 0.7.0.') !== null;
    unmount();
    return found;
  };
  expect(shows('0.7.0')).toBe(true);
  expect(shows('0.6.0')).toBe(false);
  expect(shows('0.5.0')).toBe(false);
  expect(shows(null)).toBe(true);
});

it('shows the causes for 0.10.0 against a previous minor of 0.9.0 (numeric, not string, comparison)', () => {
  render(<ChangeRegion target={null} opensIn="0.10.0" causes={causes} previousMinor="0.9.0" />);
  expect(screen.getByText('This file opens in TomeStack 0.10.0 or newer.')).toBeTruthy();
  expect(screen.getByText('Compat Hero needs TomeStack 0.7.0.')).toBeTruthy();
});

it('says the file needs a newer TomeStack when opensIn is null', () => {
  render(<ChangeRegion target={null} opensIn={null} causes={causes} previousMinor="0.6.0" />);
  expect(screen.getByText('This file needs a newer TomeStack than this one.')).toBeTruthy();
});

it('compares dotted numbers, not strings', () => {
  expect(isNewerThan('0.10.0', '0.9.0')).toBe(true);
  expect(isNewerThan('0.9.0', '0.10.0')).toBe(false);
  expect(isNewerThan('0.6.0', '0.6.0')).toBe(false);
  expect(isNewerThan('0.7.0', '0.6.0')).toBe(true);
});

it('shows the causes when there is no previous minor to compare with', () => {
  render(<ChangeRegion target={null} opensIn="0.5.0" causes={causes} previousMinor={null} />);
  expect(screen.getByText('Compat Hero needs TomeStack 0.7.0.')).toBeTruthy();
});

it('renders nothing when opensIn is undefined (the service does not report it)', () => {
  const { container } = render(<ChangeRegion target={null} causes={causes} previousMinor="0.6.0" />);
  expect(container.firstChild).toBeNull();
});

it('has no axe violations in either mode', async () => {
  const first = render(<ChangeRegion target="0.6.0" changes={[change('blocks', 'Compat Hero', 'Blocked.'), change('copied', 'Compat Homebrew A', 'New id.')]} previousMinor="0.6.0" />);
  await expectNoAxeViolations(first.container);
  first.unmount();
  const second = render(<ChangeRegion target={null} opensIn="0.7.0" causes={causes} previousMinor="0.6.0" />);
  await expectNoAxeViolations(second.container);
});
