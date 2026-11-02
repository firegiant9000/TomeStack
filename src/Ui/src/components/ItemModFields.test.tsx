// @vitest-environment jsdom
// MECH-X item 3 (T5): the item mod editor.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import type { Effect } from '../api/types';
import { ItemModFields } from './ItemModFields';

afterEach(cleanup);

type ItemMod = Extract<Effect, { type: 'itemMod' }>;

function Harness(props: { start: ItemMod; onProblem?: (field: string, problem: string | undefined) => void; fields?: { id: string; label: string }[]; onSeen: (effect: ItemMod) => void }) {
  const [effect, setEffect] = useState(props.start);
  return (
    <ItemModFields
      effect={effect}
      fields={props.fields ?? [{ id: 'initiative', label: 'Initiative' }]}
      onChange={(e) => {
        props.onSeen(e as ItemMod);
        setEffect(e as ItemMod);
      }}
      onProblem={props.onProblem ?? (() => {})}
    />
  );
}

const record = (seen: { last?: ItemMod }) => (effect: ItemMod) => {
  seen.last = effect;
};

const start: ItemMod = { type: 'itemMod', id: 'itemMod-1', appliesTo: 'weapon', bonus: 1 };

it('labels every field and shows the stored values', () => {
  render(<Harness start={start} onSeen={() => {}} />);
  expect((screen.getByRole('combobox', { name: 'Applies to' }) as HTMLSelectElement).value).toBe('weapon');
  expect(screen.getByRole('option', { name: 'Weapons' })).toBeTruthy();
  expect(screen.getByRole('option', { name: 'Armor and shields' })).toBeTruthy();
  expect(screen.getByRole('option', { name: 'Either' })).toBeTruthy();
  expect((screen.getByRole('textbox', { name: 'Bonus (1 to 10; empty for none)' }) as HTMLInputElement).value).toBe('1');
  expect(screen.getByRole('button', { name: 'Add a modifier to this mod' })).toBeTruthy();
});

it('changing Applies to keeps the other keys', async () => {
  const user = userEvent.setup();
  const seen: { last?: ItemMod } = {};
  render(<Harness start={{ ...start, text: 'note' }} onSeen={record(seen)} />);
  await user.selectOptions(screen.getByRole('combobox', { name: 'Applies to' }), 'any');
  expect(seen.last).toMatchObject({ appliesTo: 'any', bonus: 1, text: 'note' });
});

it('a bad bonus is aria-invalid with a reported problem, and clearing it removes the key', async () => {
  const user = userEvent.setup();
  const problem = vi.fn();
  const seen: { last?: ItemMod } = {};
  render(<Harness start={start} onSeen={record(seen)} onProblem={problem} />);
  const bonus = screen.getByRole('textbox', { name: /^Bonus/ });
  await user.clear(bonus);
  expect('bonus' in seen.last!).toBe(false);
  await user.type(bonus, '11');
  expect(bonus.getAttribute('aria-invalid')).toBe('true');
  expect(problem).toHaveBeenLastCalledWith(expect.stringContaining('bonus'), expect.stringMatching(/too high/));
  await user.clear(bonus);
  await user.type(bonus, '4');
  expect(bonus.hasAttribute('aria-invalid')).toBe(false);
  expect(seen.last!.bonus).toBe(4);
});

it('adds nested modifiers with the default, edits and removes them, and stops at 8', async () => {
  const user = userEvent.setup();
  const seen: { last?: ItemMod } = {};
  render(<Harness start={{ type: 'itemMod', id: 'itemMod-1', appliesTo: 'any' }} onSeen={record(seen)} />);
  const add = screen.getByRole('button', { name: 'Add a modifier to this mod' });
  await user.click(add);
  expect(seen.last!.modifiers).toEqual([{ type: 'modifier', id: 'itemMod-1-m1', operation: 'bonus', target: 'initiative', value: '1' }]);
  // Only operation, target, value and text: no toggle, timing or automation.
  expect(screen.getByRole('combobox', { name: 'Changes' })).toBeTruthy();
  expect(screen.getByRole('combobox', { name: 'How' })).toBeTruthy();
  expect(screen.getByRole('textbox', { name: /^Value/ })).toBeTruthy();
  expect(screen.getByRole('textbox', { name: 'Text' })).toBeTruthy();
  expect(screen.queryByRole('combobox', { name: 'Applies' })).toBeNull();
  expect(screen.queryByRole('combobox', { name: 'Automation' })).toBeNull();
  await user.selectOptions(screen.getByRole('combobox', { name: 'How' }), 'set');
  await user.type(screen.getByRole('textbox', { name: 'Text' }), 'x');
  expect(seen.last!.modifiers![0]).toMatchObject({ operation: 'set', text: 'x' });
  for (let i = 0; i < 7; i++) await user.click(add);
  expect(seen.last!.modifiers).toHaveLength(8);
  expect((add as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Remove modifier 1 of this mod' }));
  expect(seen.last!.modifiers).toHaveLength(7);
  expect((add as HTMLButtonElement).disabled).toBe(false);
}, 20_000);

it('removing the last nested modifier drops the modifiers key', async () => {
  const user = userEvent.setup();
  const seen: { last?: ItemMod } = {};
  render(<Harness start={{ ...start, modifiers: [{ type: 'modifier', id: 'itemMod-1-m1', operation: 'bonus', target: 'initiative', value: '1' }] }} onSeen={record(seen)} />);
  await user.click(screen.getByRole('button', { name: 'Remove modifier 1 of this mod' }));
  expect('modifiers' in seen.last!).toBe(false);
});
