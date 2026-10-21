// @vitest-environment jsdom
// The free dice box on the summary (tester feedback item 8): a typed formula is trimmed and sent, a blank box does nothing, and
// the engine's refusal is shown linked to the box with focus kept there (no alert role: Last roll's live region announces results).
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { TomeStackError } from '../api/transport';
import { DiceBox } from './DiceBox';

afterEach(cleanup);

const refusal = () => new TomeStackError({ code: 'bad-request', message: 'Too many dice.', diagnostics: [{ code: 'dice.too-many-dice', message: 'Too many dice.' }] });
// The refusal's polite live region (not role="status": the app's own status line owns that role, which e2e queries by role).
const statusRegion = () => document.querySelector<HTMLElement>('.dice-box [aria-live="polite"]')!;
const input = () => screen.getByRole('textbox', { name: 'Dice to roll' });

it('rolls a typed formula', async () => {
  const onRoll = vi.fn().mockResolvedValue(undefined);
  render(<DiceBox onRoll={onRoll} />);
  await userEvent.type(input(), '2d6+3');
  await userEvent.click(screen.getByRole('button', { name: 'Roll dice' }));
  expect(onRoll).toHaveBeenCalledWith('2d6+3');
});

it.each(['d20', '2d6 + 3'])('sends the good form %s as typed', async (text) => {
  const onRoll = vi.fn().mockResolvedValue(undefined);
  render(<DiceBox onRoll={onRoll} />);
  await userEvent.type(input(), `${text}{Enter}`);
  expect(onRoll).toHaveBeenCalledWith(text);
  expect(input().getAttribute('aria-invalid')).toBeNull();
});

it('trims the text before sending it', async () => {
  const onRoll = vi.fn().mockResolvedValue(undefined);
  render(<DiceBox onRoll={onRoll} />);
  await userEvent.type(input(), '  1d8  {Enter}');
  expect(onRoll).toHaveBeenCalledWith('1d8');
});

it('does nothing for a blank or spaces-only box', async () => {
  const onRoll = vi.fn().mockResolvedValue(undefined);
  render(<DiceBox onRoll={onRoll} />);
  await userEvent.click(screen.getByRole('button', { name: 'Roll dice' }));
  await userEvent.type(input(), '   ');
  await userEvent.click(screen.getByRole('button', { name: 'Roll dice' }));
  await userEvent.type(input(), '{Enter}');
  expect(onRoll).not.toHaveBeenCalled();
  expect(input().getAttribute('aria-invalid')).toBeNull();
  expect(statusRegion().textContent).toBe('');
});

it('shows the engine refusal linked to the box and keeps focus there', async () => {
  const onRoll = vi.fn().mockRejectedValue(refusal());
  render(<DiceBox onRoll={onRoll} />);
  const box = input();
  await userEvent.type(box, '1000d6{Enter}');
  expect(box.getAttribute('aria-invalid')).toBe('true');
  expect(box.getAttribute('aria-describedby')).toBeTruthy();
  expect(screen.getByText('Too many dice.')).toBeTruthy();
  expect(document.getElementById(box.getAttribute('aria-describedby')!)!.textContent).toBe('Too many dice.');
  expect(document.activeElement).toBe(box);
  expect(screen.queryByRole('alert')).toBeNull();
});

it('keeps focus on the box when the Roll dice button was pressed and the roll is refused', async () => {
  const onRoll = vi.fn().mockRejectedValue(refusal());
  render(<DiceBox onRoll={onRoll} />);
  await userEvent.type(input(), '1000d6');
  await userEvent.click(screen.getByRole('button', { name: 'Roll dice' }));
  expect(document.activeElement).toBe(input());
});

it('says something plain, without the typed text, when the failure is not an engine refusal', async () => {
  const onRoll = vi.fn().mockRejectedValue(new Error('boom 2d6'));
  render(<DiceBox onRoll={onRoll} />);
  await userEvent.type(input(), '2d6{Enter}');
  expect(input().getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(input().getAttribute('aria-describedby')!)!.textContent).toBe('The dice could not be rolled. Try again.');
});

it('clears the error once the text changes', async () => {
  const onRoll = vi.fn().mockRejectedValue(refusal());
  render(<DiceBox onRoll={onRoll} />);
  await userEvent.type(input(), '1000d6{Enter}');
  expect(screen.getByText('Too many dice.')).toBeTruthy();
  await userEvent.type(input(), '0');
  expect(input().getAttribute('aria-invalid')).toBeNull();
  expect(input().getAttribute('aria-describedby')).toBeNull();
  expect(screen.queryByText('Too many dice.')).toBeNull();
});

it('limits the text to 80 characters and offers no autocomplete or spellcheck', () => {
  render(<DiceBox onRoll={vi.fn()} />);
  expect(input().getAttribute('maxlength')).toBe('80');
  expect(input().getAttribute('autocomplete')).toBe('off');
  expect(input().getAttribute('spellcheck')).toBe('false');
});

it('announces a refusal after Enter in a polite status that is empty before and cleared on edit', async () => {
  const onRoll = vi.fn().mockRejectedValue(refusal());
  render(<DiceBox onRoll={onRoll} />);
  const status = statusRegion();
  expect(status.textContent).toBe('');
  expect(input().getAttribute('aria-describedby')).toBeNull();
  await userEvent.type(input(), '1000d6{Enter}');
  expect(statusRegion()).toBe(status); // permanently mounted, not created by the refusal
  expect(status.textContent).toBe('Too many dice.');
  expect(input().getAttribute('aria-describedby')).toBe(status.id);
  await userEvent.type(input(), '0');
  expect(status.textContent).toBe('');
});

it('ignores a refusal that arrives after the text was edited', async () => {
  let reject!: (e: unknown) => void;
  const onRoll = vi.fn().mockReturnValue(new Promise((_, r) => (reject = r)));
  render(<DiceBox onRoll={onRoll} />);
  await userEvent.type(input(), '1000d6{Enter}');
  await userEvent.type(input(), '0');
  reject(refusal());
  await new Promise((r) => setTimeout(r, 0));
  expect(input().getAttribute('aria-invalid')).toBeNull();
  expect(statusRegion().textContent).toBe('');
});

it('is a form named Roll dice', () => {
  render(<DiceBox onRoll={vi.fn()} />);
  expect(screen.getByRole('form', { name: 'Roll dice' })).toBeTruthy();
});
