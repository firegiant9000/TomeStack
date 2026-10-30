// @vitest-environment jsdom
// The dice display (ADR-015): the service's dice, drawn beside the text and hidden from assistive tech. The region's text
// is unchanged, so what a screen reader hears and what the e2e asserts stay the same. Nothing is rolled in the UI.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterView, DieResult, RollRecord } from '../api/types';
import { ActionsPanel, RollResult } from './PlayPanels';

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

it('heads a record whose label is an empty string with "Roll" (Review Focus 2)', () => {
  render(<RollResult record={record({ provenance: { rollId: 'fixture-roll', label: '' } })} resources={[]} features={[]} act={() => {}} />);
  expect(region().textContent).toMatch(/^Roll: 7 \(2d6\)/);
});

it('names a roll button by the effect id when its label is an empty string', () => {
  const view = {
    character: {},
    sheet: {
      features: [
        {
          content: { contentId: 'fixture-feature', revisionId: 'r1' },
          name: 'Fixture feature',
          effects: [{ id: 'fixture-effect', type: 'roll', automation: 'automatic', label: '', dice: '1d6' }],
        },
      ],
    },
  } as unknown as CharacterView;
  render(<ActionsPanel view={view} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll fixture-effect (1d6)' })).toBeTruthy();
});

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

// D73: the sheet's lifted "spent" pair. The record it names is the spent one, and Spend goes through the sheet's spender.
it('with a lifted spent pair, a record named as spent disables Spend and a press goes through the pair (D73)', async () => {
  const user = userEvent.setup();
  const spendRecord = record({ spend: { contentId: 'c1', resourceId: 'pool', amount: 1 } });
  const pool = [
    {
      content: { contentId: 'c1', revisionId: 'r1' }, contentName: 'Fixture', effectId: 'e1', resourceId: 'pool', label: 'Fixture pool',
      maximum: 3, spent: 0, current: 3, trace: [], warnings: [], automation: 'automatic' as const, recoveries: [],
    },
  ];
  const spendOnce = vi.fn().mockResolvedValue(true);
  const act = vi.fn();
  const { rerender } = render(<RollResult record={spendRecord} resources={pool} features={[]} act={act} spent={{ record: undefined, spendOnce }} />);
  await user.click(screen.getByRole('button', { name: 'Spend 1 Fixture pool (3 left)' }));
  expect(spendOnce).toHaveBeenCalledExactlyOnceWith(spendRecord, { action: 'spend', amount: 1, contentId: 'c1', resourceId: 'pool' }, undefined);
  expect(act).not.toHaveBeenCalled(); // the pair spends, not the component
  rerender(<RollResult record={spendRecord} resources={pool} features={[]} act={act} spent={{ record: spendRecord, spendOnce }} />);
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Spend 1 Fixture pool (3 left)' }).disabled).toBe(true);
});

// MECH-X item 1 (R37, R40): a paying record offers Pay (hit points) or Spend (a slot) from the Last roll, once.
const payee = {
  content: { contentId: 'c-fury', revisionId: 'r-fury' },
  name: 'Fixture Fury',
  kind: 'feature',
  origin: {},
  automation: 'automatic',
  diagnostics: [],
  effects: [{ id: 'lash', rollId: 'lash-roll', type: 'roll', automation: 'automatic', label: 'Lash', dice: '2d6', cost: 3, available: true, payment: { kind: 'hitPoints', payable: true } }],
};
const features = (payment: object) => [{ ...payee, effects: [{ ...payee.effects[0]!, payment }] }] as never;
const payRecord = (payment: object = {}) =>
  record({ payment: { contentId: 'c-fury', useId: 'lash-roll', kind: 'hitPoints', amount: 3, mayDropToZero: false, ...payment } });

it('a hit-point record offers "Pay 3 hit points", sends useAction by the record useId with the amount, and no Spend button', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<RollResult record={payRecord()} resources={[]} features={features({ kind: 'hitPoints', payable: true })} act={act} />);
  expect(screen.queryByRole('button', { name: /^Spend/ })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Pay 3 hit points' }));
  expect(act).toHaveBeenCalledExactlyOnceWith({ action: 'useAction', contentId: 'c-fury', rollId: 'lash-roll', amount: 3 });
});

it('a slot record offers "Spend a level 2+ spell slot" and sends no amount', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const slot = { kind: 'slot', payable: true, minSlotLevel: 2, slot: { level: 3, pact: false } };
  render(<RollResult record={payRecord({ kind: 'slot', amount: undefined, minSlotLevel: 2 })} resources={[]} features={features(slot)} act={act} />);
  await user.click(screen.getByRole('button', { name: 'Spend a level 2+ spell slot' }));
  expect(act).toHaveBeenCalledExactlyOnceWith({ action: 'useAction', contentId: 'c-fury', rollId: 'lash-roll' });
  expect(region().textContent).toContain('Slot, level 3 now');
});

it('Pay goes through the shared spent state: a second click sends nothing (S6)', async () => {
  const user = userEvent.setup();
  let release: (applied: boolean) => void = () => {};
  const act = vi.fn(() => new Promise<boolean>((resolve) => (release = resolve)));
  const shown = payRecord();
  render(<RollResult record={shown} resources={[]} features={features({ kind: 'hitPoints', payable: true })} act={act} />);
  const pay = screen.getByRole<HTMLButtonElement>('button', { name: 'Pay 3 hit points' });
  await user.click(pay);
  await user.click(pay);
  expect(act).toHaveBeenCalledTimes(1);
  expect(pay.disabled).toBe(true);
  release(true);
});

it('Pay with a lifted spent pair goes through the pair, and a record named as spent disables it', async () => {
  const user = userEvent.setup();
  const spendOnce = vi.fn().mockResolvedValue(true);
  const act = vi.fn();
  const shown = payRecord();
  const f = features({ kind: 'hitPoints', payable: true });
  const { rerender } = render(<RollResult record={shown} resources={[]} features={f} act={act} spent={{ record: undefined, spendOnce }} />);
  await user.click(screen.getByRole('button', { name: 'Pay 3 hit points' }));
  expect(spendOnce).toHaveBeenCalledExactlyOnceWith(shown, { action: 'useAction', contentId: 'c-fury', rollId: 'lash-roll', amount: 3 }, undefined);
  expect(act).not.toHaveBeenCalled();
  rerender(<RollResult record={shown} resources={[]} features={f} act={act} spent={{ record: shown, spendOnce }} />);
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Pay 3 hit points' }).disabled).toBe(true);
});

it('a payment the row says cannot be made is disabled and shows the reason (R40)', () => {
  const reason = 'Needs more than 3 current hit points.';
  render(<RollResult record={payRecord()} resources={[]} features={features({ kind: 'hitPoints', payable: false, reason })} act={() => {}} />);
  const pay = screen.getByRole<HTMLButtonElement>('button', { name: 'Pay 3 hit points' });
  expect(pay.disabled).toBe(true);
  expect(region().textContent).toContain(reason);
  expect(pay.getAttribute('aria-describedby')).toBeTruthy();
});

it('a refused payment gives the button back so it can be tried again', async () => {
  const user = userEvent.setup();
  const act = vi.fn().mockResolvedValueOnce(false).mockResolvedValue(true);
  render(<RollResult record={payRecord()} resources={[]} features={features({ kind: 'hitPoints', payable: true })} act={act} />);
  await user.click(screen.getByRole('button', { name: 'Pay 3 hit points' }));
  await vi.waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Pay 3 hit points' }).disabled).toBe(false));
});
