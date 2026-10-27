// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { QuickAction } from '../quickbar';
import { Quickbar, quickLabel } from './Quickbar';

afterEach(cleanup);

const action = (n: number, over: Partial<QuickAction> = {}): QuickAction => ({
  pin: { kind: 'roll', contentId: `c${n}`, effectId: `e${n}` },
  key: `roll:c${n}:e${n}:`,
  name: `Roll Fixture ${n} and spend ${n} Focus`,
  availability: { usable: true, reasons: [] },
  target: { effectId: `e${n}` },
  ...over,
});
const two = [action(1), action(2)];
const alt = (code: string, init: KeyboardEventInit = {}) => new KeyboardEvent('keydown', { code, altKey: true, bubbles: true, cancelable: true, ...init });
const send = (event: KeyboardEvent, on: EventTarget = document.body) => {
  on.dispatchEvent(event);
  return event;
};

describe('Quickbar', () => {
  it('renders nothing without actions', () => {
    render(<Quickbar actions={[]} busy={false} run={vi.fn()} />);
    expect(screen.queryByRole('region', { name: 'Quickbar' })).toBeNull();
  });

  it('lists the buttons in pin order with their shortcuts, and Tab reaches each', async () => {
    const user = userEvent.setup();
    render(<Quickbar actions={two} busy={false} run={vi.fn()} />);
    const buttons = screen.getAllByRole('button');
    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual(['Roll Fixture 1 and spend 1 Focus', 'Roll Fixture 2 and spend 2 Focus']);
    expect(buttons.map((b) => b.getAttribute('aria-keyshortcuts'))).toEqual(['Alt+1', 'Alt+2']);
    expect(buttons[0]!.textContent).toBe('1 Fixture 1'); // shorter than the name; the slot number is aria-hidden
    await user.tab();
    expect(document.activeElement).toBe(buttons[0]);
    await user.tab();
    expect(document.activeElement).toBe(buttons[1]);
  });

  it('shortens each kind of name to a part of itself', () => {
    expect(quickLabel('Roll Tide Wave and spend 2 Focus')).toBe('Tide Wave');
    expect(quickLabel('Roll Longsword attack')).toBe('Longsword');
    expect(quickLabel('Use Guard (1 Reaction)')).toBe('Guard');
    expect(quickLabel('Use Guard')).toBe('Guard');
    expect(quickLabel('Turn Tide Form on')).toBe('Tide Form on');
    expect(quickLabel('Roll Tide Wave')).toBe('Tide Wave');
  });

  it('Alt+1 runs the first action and is prevented; Alt+3 with two actions is not a hit', () => {
    const run = vi.fn();
    render(<Quickbar actions={two} busy={false} run={run} />);
    const hit = send(alt('Digit1'));
    expect(hit.defaultPrevented).toBe(true);
    expect(run).toHaveBeenCalledExactlyOnceWith(two[0]);
    run.mockClear();
    const miss = send(alt('Digit3'));
    expect(miss.defaultPrevented).toBe(false);
    expect(run).not.toHaveBeenCalled();
  });

  it('a held key and a busy bar run nothing', () => {
    const run = vi.fn();
    const { rerender } = render(<Quickbar actions={two} busy={false} run={run} />);
    send(alt('Digit1', { repeat: true }));
    expect(run).not.toHaveBeenCalled();
    rerender(<Quickbar actions={two} busy run={run} />);
    send(alt('Digit1'));
    expect(run).not.toHaveBeenCalled();
    expect(screen.getAllByRole('button').every((b) => b.getAttribute('aria-disabled') === 'true')).toBe(true);
    fireEvent.click(screen.getAllByRole('button')[0]!);
    expect(run).not.toHaveBeenCalled();
  });

  it('reads the current actions, not the ones from the first render', () => {
    const run = vi.fn();
    const { rerender } = render(<Quickbar actions={two} busy={false} run={run} />);
    const next = [action(5)];
    rerender(<Quickbar actions={next} busy={false} run={run} />);
    send(alt('Digit1'));
    expect(run).toHaveBeenCalledExactlyOnceWith(next[0]);
  });

  it('passes the key through from a text field, other modifiers, the numpad and an alert dialog', () => {
    const run = vi.fn();
    render(
      <div>
        <Quickbar actions={two} busy={false} run={run} />
        <input aria-label="Fixture text" />
        <div role="alertdialog" aria-label="Fixture confirm">
          <button type="button">Inside</button>
        </div>
      </div>,
    );
    const events = [
      send(alt('Digit1'), screen.getByLabelText('Fixture text')),
      send(alt('Digit1', { ctrlKey: true })),
      send(alt('Digit1', { shiftKey: true })),
      send(alt('Numpad1')),
      send(alt('Digit1'), screen.getByRole('button', { name: 'Inside' })),
    ];
    expect(events.map((e) => e.defaultPrevented)).toEqual([false, false, false, false, false]);
    expect(run).not.toHaveBeenCalled();
  });

  it('an action that cannot be used is aria-disabled, says why, ignores a click and takes focus on its key', () => {
    const run = vi.fn();
    const held = action(1, { availability: { usable: false, reasons: ['Needs 1 Focus (0 left)'] } });
    render(<Quickbar actions={[held, action(2)]} busy={false} run={run} />);
    const button = screen.getAllByRole('button')[0]!;
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.hasAttribute('disabled')).toBe(false);
    expect(document.getElementById(button.getAttribute('aria-describedby')!)?.textContent).toBe('Needs 1 Focus (0 left)');
    fireEvent.click(button);
    expect(run).not.toHaveBeenCalled();
    const event = send(alt('Digit1'));
    expect(event.defaultPrevented).toBe(true);
    expect(run).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(button);
  });
});
