// @vitest-environment jsdom
// The dice display (ADR-015): the service's dice, drawn beside the text and hidden from assistive tech. The region's text
// is unchanged, so what a screen reader hears and what the e2e asserts stay the same. Nothing is rolled in the UI.
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import type { DieResult, RollRecord } from '../api/types';
import { RollResult } from './PlayPanels';

const die = (sides: number, value: number, over: Partial<DieResult> = {}): DieResult => ({ term: 0, sides, value, kept: true, fromCritical: false, ...over });

const record = (over: Partial<RollRecord> = {}): RollRecord => ({
  formula: '2d6',
  mode: 'normal',
  critical: false,
  dice: [die(6, 4), die(6, 2)],
  diceTotal: 6,
  expressionConstant: 0,
  modifiers: [{ label: 'Fixture bonus', amount: 1 }],
  total: 7,
  provenance: { rollId: 'fixture-roll', label: 'Fixture damage' },
  ...over,
});

afterEach(() => {
  cleanup();
  localStorage.clear();
});

const region = () => screen.getByRole('region', { name: 'Last roll' });
const dice = () => Array.from(region().querySelectorAll<HTMLElement>('.die'));

it('draws one die per record die with the service values at once, hidden from assistive tech, text unchanged', () => {
  render(<RollResult record={record()} resources={[]} features={[]} act={() => {}} />);
  expect(region().textContent).toMatch(/^Fixture damage: 7 \(2d6\)/);
  expect(region().textContent).toMatch(/Dice: d6 4, d6 2 · Fixture bonus \+1/);
  const row = region().querySelector('.dice')!;
  expect(row.getAttribute('aria-hidden')).toBe('true');
  expect(row.textContent).toBe(''); // faces are drawn by CSS attr(), never as text
  expect(dice().map((d) => [d.dataset.sides, d.dataset.value])).toEqual([['6', '4'], ['6', '2']]);
  expect(row.className).toBe('dice');
});

it('keeps the dice still when the animation is off in Settings', () => {
  localStorage.setItem('tomestack.diceAnimation', 'off');
  render(<RollResult record={record()} resources={[]} features={[]} act={() => {}} />);
  expect(region().querySelector('.dice')!.className).toBe('dice dice-still');
});

it('marks dropped and critical dice, and draws a fresh row for an identical second roll so the tumble runs again', () => {
  const advantage = record({ formula: '1d20', mode: 'advantage', dice: [die(20, 15), die(20, 3, { kept: false })], total: 16 });
  const { rerender } = render(<RollResult record={advantage} resources={[]} features={[]} act={() => {}} />);
  expect(dice().map((d) => d.dataset.kept)).toEqual(['true', 'false']);
  const before = region().querySelector('.dice');
  rerender(<RollResult record={{ ...advantage }} resources={[]} features={[]} act={() => {}} />); // same faces, new record
  expect(region().querySelector('.dice')).not.toBe(before);
  cleanup();
  render(<RollResult record={record({ critical: true, dice: [die(8, 5, { fromCritical: true })] })} resources={[]} features={[]} act={() => {}} />);
  expect(dice()[0]!.dataset.critical).toBe('true');
});

it('keeps the same dice row when re-rendered with the same record, so unrelated re-renders do not re-tumble', () => {
  const same = record();
  const { rerender } = render(<RollResult record={same} resources={[]} features={[]} act={() => {}} />);
  const before = region().querySelector('.dice');
  rerender(<RollResult record={same} resources={[]} features={[]} act={() => {}} />);
  expect(region().querySelector('.dice')).toBe(before);
});

it('draws at most ten dice and counts the rest without adding text', () => {
  const many = record({ formula: '12d6', dice: Array.from({ length: 12 }, (_, i) => die(6, (i % 6) + 1)), total: 42 });
  render(<RollResult record={many} resources={[]} features={[]} act={() => {}} />);
  expect(dice()).toHaveLength(10);
  expect(region().querySelector<HTMLElement>('.die-more')!.dataset.more).toBe('2');
  expect(region().querySelector('.dice')!.textContent).toBe('');
});

it('renders an empty live region with no dice when there is no record', () => {
  render(<RollResult resources={[]} features={[]} act={() => {}} />);
  expect(region().children).toHaveLength(0);
});

it('drops aria-live from the Last roll region when "Announce each roll" is off, keeping the text (4.1.3 trade, item 11)', () => {
  localStorage.setItem('tomestack.announceRolls', 'off');
  render(<RollResult record={record()} resources={[]} features={[]} act={() => {}} />);
  expect(region().getAttribute('aria-live')).toBeNull();
  expect(region().textContent).toMatch(/^Fixture damage: 7/);
  cleanup();
  localStorage.removeItem('tomestack.announceRolls');
  render(<RollResult record={record()} resources={[]} features={[]} act={() => {}} />);
  expect(region().getAttribute('aria-live')).toBe('polite');
});
