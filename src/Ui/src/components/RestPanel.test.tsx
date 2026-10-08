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
