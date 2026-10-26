// @vitest-environment jsdom
// D72: the "Why?" disclosure under the Last roll. It is built from the record alone, starts closed, and a new record closes it.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it } from 'vitest';
import type { RollRecord, TraceEntry } from '../api/types';
import { RollWhy } from './RollWhy';

afterEach(cleanup);

const step = (order: number, operation: string, description: string, amount: number | undefined, result: number): TraceEntry => ({
  order,
  operation,
  description,
  amount,
  result,
  origin: { kind: 'rulesPolicy', rulesFamily: 'srd-5.2.1' },
});

const record = (over: Partial<RollRecord> = {}): RollRecord => ({
  formula: '1d20+5',
  mode: 'normal',
  critical: false,
  dice: [{ term: 0, sides: 20, value: 12, kept: true, fromCritical: false }],
  diceTotal: 12,
  expressionConstant: 0,
  modifiers: [{ label: 'Fixture bonus', amount: 5 }],
  total: 17,
  explain: [step(0, 'base', 'Fixture ability modifier', 3, 3), step(1, 'add', 'Fixture proficiency +2', 2, 5)],
  ...over,
});

it('renders a closed details headed Why? with the sentences from the record', () => {
  const { container } = render(<RollWhy record={record()} />);
  const details = container.querySelector('details.roll-why') as HTMLDetailsElement;
  expect(details.open).toBe(false);
  expect(details.querySelector('summary')?.textContent).toBe('Why?');
  const list = screen.getByRole('list', { name: 'Why this roll came out this way', hidden: true });
  expect(list.textContent).toContain('Starts at 3: Fixture ability modifier');
  expect(list.textContent).toContain('Fixture proficiency +2');
});

it('renders nothing without a record', () => {
  const { container } = render(<RollWhy />);
  expect(container.firstChild).toBeNull();
});

it('renders nothing for a roll with nothing to explain (a hit die)', () => {
  const hitDie = record({ explain: undefined, modifiers: [], formula: '1d8', total: 5, diceTotal: 5 });
  const { container } = render(<RollWhy record={hitDie} />);
  expect(container.firstChild).toBeNull();
});

it('shows the record\'s own steps, not the sheet\'s current trace', () => {
  const stale = record({ explain: [step(0, 'base', 'Recorded step only', 9, 9)] });
  render(<RollWhy record={stale} />);
  expect(screen.getByText('Starts at 9: Recorded step only', { exact: false })).toBeTruthy();
});

it('collapses again when a new record arrives, even with identical faces', async () => {
  const first = record();
  const { container, rerender } = render(<RollWhy record={first} />);
  await userEvent.click(screen.getByText('Why?'));
  expect((container.querySelector('details') as HTMLDetailsElement).open).toBe(true);
  rerender(<RollWhy record={first} />);
  expect((container.querySelector('details') as HTMLDetailsElement).open).toBe(true);
  rerender(<RollWhy record={{ ...first }} />);
  expect((container.querySelector('details') as HTMLDetailsElement).open).toBe(false);
});
