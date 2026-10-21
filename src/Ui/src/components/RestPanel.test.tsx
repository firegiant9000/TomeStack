// @vitest-environment jsdom
// The rest panel's in-flight guards (checkpoint A review): a hit-die roll in flight counts against the dice left, and
// "Cancel rest" does nothing while a Finish is in flight. The client is mocked; values are invented.
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CharacterView, RollRecord } from '../api/types';
import { RestPanel } from './RestPanel';

vi.mock('../api/client', () => ({ client: { restPreview: vi.fn(), roll: vi.fn(), rest: vi.fn() } }));

beforeEach(() => {
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'shortRest', changes: [], manual: [], basis: 'fixture' });
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.rest).mockReset();
});
afterEach(cleanup);

const hitDice = [{ die: 8, total: 1, spent: 0, remaining: 1, classes: ['Fixture class'] }];
const record = { rollId: 'r1', dice: [{ die: 8, value: 5 }] } as unknown as RollRecord;
const noop = () => {};

function panel(over: Partial<Parameters<typeof RestPanel>[0]> = {}) {
  return render(<RestPanel characterId="fixture-2" kind="shortRest" hitDice={hitDice} version={{}} onRested={noop} onCancel={noop} onError={noop} {...over} />);
}

function deferredRoll() {
  let resolve: (r: RollRecord) => void = () => {};
  vi.mocked(client.roll).mockReturnValue(new Promise<RollRecord>((r) => (resolve = r)));
  return () => resolve(record);
}

it('sends one roll when Roll a d8 is pressed twice with one die left', async () => {
  const user = userEvent.setup();
  const resolve = deferredRoll();
  panel();
  const rollButton = await screen.findByRole('button', { name: 'Roll a d8' });
  await user.click(rollButton);
  await user.click(rollButton);
  expect(client.roll).toHaveBeenCalledTimes(1);
  expect(rollButton.getAttribute('aria-disabled')).toBe('true');
  resolve();
  await screen.findByText(/d8: 0 of 1 left/);
});

it('does nothing for Add d8 while a roll of the last die is in flight, and ends with exactly one die', async () => {
  const user = userEvent.setup();
  const resolve = deferredRoll();
  const previewed: number[] = [];
  vi.mocked(client.restPreview).mockImplementation(async (_id, kind, dice = []) => {
    previewed.push(dice.length);
    return { kind, changes: [], manual: [], basis: 'fixture' };
  });
  panel();
  await user.click(await screen.findByRole('button', { name: 'Roll a d8' }));
  await user.type(screen.getByLabelText('d8 rolled at the table'), '4');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  expect(screen.getByRole('button', { name: 'Add d8' }).getAttribute('aria-disabled')).toBe('true');
  resolve();
  await screen.findByText(/d8: 0 of 1 left/);
  await waitFor(() => expect(previewed.at(-1)).toBe(1));
  expect(Math.max(...previewed)).toBe(1);
});

it('keeps Finish inert while a hit die is being rolled (carry 2)', async () => {
  const user = userEvent.setup();
  const resolve = deferredRoll();
  vi.mocked(client.rest).mockResolvedValue({} as CharacterView);
  panel();
  const finish = await screen.findByRole('button', { name: 'Finish short rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(screen.getByRole('button', { name: 'Roll a d8' }));
  expect(finish.getAttribute('aria-disabled')).toBe('true');
  await user.click(finish);
  expect(client.rest).not.toHaveBeenCalled();
  resolve();
  await screen.findByText(/d8: 0 of 1 left/);
});

it('keeps Cancel rest inert, focus on it, and the panel open while a Finish is in flight', async () => {
  const user = userEvent.setup();
  const onCancel = vi.fn();
  vi.mocked(client.rest).mockReturnValue(new Promise<CharacterView>(() => {}));
  panel({ onCancel });
  const finish = await screen.findByRole('button', { name: 'Finish short rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);
  const cancel = screen.getByRole('button', { name: 'Cancel rest' });
  cancel.focus();
  expect(cancel.getAttribute('aria-disabled')).toBe('true');
  await user.click(cancel);
  expect(onCancel).not.toHaveBeenCalled();
  expect(screen.getByRole('region', { name: 'Short rest' })).toBeTruthy();
  expect(document.activeElement).toBe(cancel);
});

// Wave 3 item 25 (R160, R171): the dice an item regains charges with are rolled here, like a hit die, and the proposal follows them.
const waiting = {
  id: 'charges:entry-1',
  kind: 'charges',
  label: 'Fixture wand charges',
  from: 3,
  to: 3,
  reason: 'Fixture wand: roll 1d6+1 for the charges it regains on a long rest',
  origin: { kind: 'content' },
  resourceId: 'entry-1',
  dice: '1d6+1',
};
const rolled = { ...waiting, to: 7, reason: 'Fixture wand: regain 4 charge(s) on a long rest (1d6+1 showed 5)', spentAfter: 0, amount: 4 };

it('asks for the regain dice, rolls them through the roller, logs the roll, and previews and finishes with the total', async () => {
  const user = userEvent.setup();
  const rolls: unknown[] = [];
  const logged: RollRecord[] = [];
  vi.mocked(client.restPreview).mockImplementation(
    async (...[, kind, , chargeRolls = []]) =>
      ({ kind, changes: [chargeRolls.length > 0 ? rolled : waiting], manual: [], basis: chargeRolls.length > 0 ? 'with-roll' : 'without' }) as never,
  );
  vi.mocked(client.roll).mockImplementation(async (_id, target) => {
    rolls.push(target);
    return { rollId: 'r2', total: 5, dice: [{ die: 6, value: 4 }] } as unknown as RollRecord;
  });
  vi.mocked(client.rest).mockResolvedValue({} as CharacterView);
  const rested: unknown[] = [];
  panel({ kind: 'longRest', hitDice: [], onRoll: (r) => logged.push(r), onRested: (_v, applied) => rested.push(applied) });

  const roll = await screen.findByRole('button', { name: 'Roll 1d6+1: Fixture wand' });
  await user.click(roll);

  expect(rolls).toEqual([{ chargeEntry: 'entry-1' }]);
  expect(logged).toHaveLength(1);
  await waitFor(() => expect(client.restPreview).toHaveBeenLastCalledWith('fixture-2', 'longRest', [], [{ entry: 'entry-1', roll: 5 }]));
  const box = await screen.findByRole('checkbox', { name: /Fixture wand charges: 3 → 7/ });
  expect((box as HTMLInputElement).checked).toBe(true);

  const finish = screen.getByRole('button', { name: 'Finish long rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);

  await waitFor(() => expect(client.rest).toHaveBeenCalledWith('fixture-2', 'longRest', 'with-roll', [], [], [{ entry: 'entry-1', roll: 5 }]));
  expect(rested).toEqual([1]);
});

it('lets a roll that went stale mid-rest be cleared, instead of leaving only Cancel (the charges regained to 0 elsewhere)', async () => {
  const user = userEvent.setup();
  let stale = false;
  vi.mocked(client.restPreview).mockImplementation(async (...[, kind, , chargeRolls = []]) => {
    if (stale && chargeRolls.length > 0) throw new Error('rest.charge-roll-unknown');
    return { kind, changes: [chargeRolls.length > 0 ? rolled : waiting], manual: [], basis: 'b' } as never;
  });
  const errors: unknown[] = [];
  const first = { version: 1 };
  const { rerender } = panel({ kind: 'longRest', hitDice: [], version: first, onError: (e) => errors.push(e) });

  await user.type(await screen.findByLabelText('Fixture wand: 1d6+1 rolled at the table (total)'), '5');
  await user.click(screen.getByRole('button', { name: 'Add the 1d6+1 total: Fixture wand' }));
  await screen.findByRole('button', { name: 'Clear the roll: Fixture wand charges' });

  // The character changed under the open rest: the stored roll no longer has a dice row, so the proposal is refused.
  stale = true;
  rerender(<RestPanel characterId="fixture-2" kind="longRest" hitDice={[]} version={{ version: 2 }} onRested={noop} onCancel={noop} onError={(e) => errors.push(e)} />);
  await screen.findByText('Could not work out this rest. Cancel and try again.');
  expect(errors).toHaveLength(1);

  const clear = screen.getByRole('button', { name: 'Clear the roll: Fixture wand charges' });
  expect(clear.getAttribute('aria-disabled')).not.toBe('true');
  await user.click(clear);

  await screen.findByRole('button', { name: 'Roll 1d6+1: Fixture wand' });
  expect(screen.queryByText('Could not work out this rest. Cancel and try again.')).toBeNull();
  await waitFor(() => expect(vi.mocked(client.restPreview)).toHaveBeenLastCalledWith('fixture-2', 'longRest', [], []));
});

it('takes a total rolled at the table, can clear it, and counts a row still waiting for its roll as nothing applied', async () => {
  const user = userEvent.setup();
  vi.mocked(client.restPreview).mockImplementation(
    async (...[, kind, , chargeRolls = []]) => ({ kind, changes: [chargeRolls.length > 0 ? rolled : waiting], manual: [], basis: 'b' }) as never,
  );
  vi.mocked(client.rest).mockResolvedValue({} as CharacterView);
  const rested: number[] = [];
  panel({ kind: 'longRest', hitDice: [], onRested: (_v, applied) => rested.push(applied) });

  await user.type(await screen.findByLabelText('Fixture wand: 1d6+1 rolled at the table (total)'), '5');
  await user.click(screen.getByRole('button', { name: 'Add the 1d6+1 total: Fixture wand' }));
  const clear = await screen.findByRole('button', { name: 'Clear the roll: Fixture wand charges' });
  await waitFor(() => expect(vi.mocked(client.restPreview)).toHaveBeenLastCalledWith('fixture-2', 'longRest', [], [{ entry: 'entry-1', roll: 5 }]));

  await waitFor(() => expect(clear.getAttribute('aria-disabled')).toBe('false'));
  await user.click(clear);
  const finish = await screen.findByRole('button', { name: 'Roll 1d6+1: Fixture wand' }).then(() => screen.getByRole('button', { name: 'Finish long rest' }));
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);

  await waitFor(() => expect(rested).toEqual([0])); // finished without the roll: the charges are left as they were
  expect(client.rest).toHaveBeenCalledWith('fixture-2', 'longRest', 'b', [], [], []);
});
