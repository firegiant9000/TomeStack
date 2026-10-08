// @vitest-environment jsdom
// D28: previous rolls, newest first, at most ten, outside the live region, never stored.
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import type { RollRecord } from '../api/types';
import { RollLog, type LoggedRoll } from './RollLog';

afterEach(cleanup);

const record = (label: string | undefined, total: number): RollRecord => ({
  formula: '1d20', mode: 'normal', critical: false, dice: [{ term: 0, sides: 20, value: total, kept: true, fromCritical: false }],
  diceTotal: total, expressionConstant: 0, modifiers: [], total, provenance: label ? { rollId: 'fixture', label } : undefined,
});
const logged = (label: string | undefined, total: number, at = 1_760_000_000_000): LoggedRoll => ({ at, record: record(label, total) });

it('renders nothing with no previous rolls', () => {
  const { container } = render(<RollLog rolls={[]} />);
  expect(container.innerHTML).toBe('');
});

it('is a collapsed disclosure named by its count, with the entries newest first and no heading or region', () => {
  render(<RollLog rolls={[logged('Fixture Bow attack', 11), logged('Strength check', 17)]} />);
  const details = screen.getByText('Previous rolls (2)').closest('details')!;
  expect(details.open).toBe(false);
  const items = within(details).getAllByRole('listitem').map((li) => li.textContent);
  expect(items[0]).toMatch(/\d{1,2}:\d{2}.*Fixture Bow attack: 11 \(1d20\)/);
  expect(items[1]).toMatch(/Strength check: 17/);
  expect(screen.queryAllByRole('heading')).toHaveLength(0);
  expect(screen.queryAllByRole('region')).toHaveLength(0);
  expect(details.getAttribute('aria-live')).toBeNull();
});

it('falls back to "Roll" when the record has no label (Review Focus 2)', () => {
  render(<RollLog rolls={[logged(undefined, 4)]} />);
  expect(screen.getByRole('listitem').textContent).toMatch(/Roll: 4 \(1d20\)/);
});

it('shows at most ten entries', () => {
  render(<RollLog rolls={Array.from({ length: 12 }, (_, i) => logged(`Roll ${i}`, i))} />);
  expect(screen.getAllByRole('listitem')).toHaveLength(10);
  expect(screen.getByText('Previous rolls (10)')).toBeTruthy();
});
