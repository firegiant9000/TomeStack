// @vitest-environment jsdom
// MoreMenu is the sheet's overflow disclosure: a "More" button that opens a plain list of buttons (not an ARIA menu).
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { closeOnEscape } from '../../escape';
import { expectNoAxeViolations } from '../../test/axe';
import { MoreMenu } from './MoreMenu';

afterEach(cleanup);

function items(onSelect = vi.fn()) {
  return [
    { id: 'print', label: 'Print', onSelect },
    { id: 'export', label: 'Export', onSelect },
    { id: 'notes', label: 'Notes', onSelect, disabled: true },
  ];
}

const more = () => screen.getByRole('button', { name: 'More' });

it('is closed at first and opens on click', async () => {
  const user = userEvent.setup();
  render(<MoreMenu items={items()} />);
  expect(more().getAttribute('aria-expanded')).toBe('false');
  expect(screen.queryByRole('button', { name: 'Print' })).toBeNull();
  await user.click(more());
  expect(more().getAttribute('aria-expanded')).toBe('true');
  expect(screen.getByRole('button', { name: 'Print' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Export' })).toBeTruthy();
  expect(document.getElementById(more().getAttribute('aria-controls') ?? '')?.tagName).toBe('UL');
  expect(screen.queryByRole('menu')).toBeNull();
  expect(screen.queryByRole('menuitem')).toBeNull();
});

it('closes on a second click', async () => {
  const user = userEvent.setup();
  render(<MoreMenu items={items()} />);
  await user.click(more());
  await user.click(more());
  expect(more().getAttribute('aria-expanded')).toBe('false');
  expect(screen.queryByRole('button', { name: 'Print' })).toBeNull();
});

it('closes on Esc and returns focus to More', async () => {
  const user = userEvent.setup();
  render(<MoreMenu items={items()} />);
  await user.click(more());
  await user.tab();
  expect(document.activeElement?.textContent).toBe('Print');
  await user.keyboard('{Escape}');
  expect(more().getAttribute('aria-expanded')).toBe('false');
  expect(screen.queryByRole('button', { name: 'Print' })).toBeNull();
  expect(document.activeElement).toBe(more());
});

it('claims Esc while open so the global Esc handler skips it, and leaves Esc alone while closed', async () => {
  const user = userEvent.setup();
  const prevented: boolean[] = [];
  const handled: boolean[] = [];
  // Runs after React's root listener, as App.tsx's document listener does.
  const spy = (event: KeyboardEvent) => {
    if (event.key !== 'Escape') return;
    prevented.push(event.defaultPrevented);
    handled.push(closeOnEscape(event));
  };
  document.addEventListener('keydown', spy);
  try {
    render(<MoreMenu items={items()} />);
    await user.click(more());
    await user.tab();
    await user.keyboard('{Escape}');
    expect(prevented).toEqual([true]);
    expect(handled).toEqual([false]);
    expect(more().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(more());
    await user.keyboard('{Escape}'); // closed: the menu does not claim the key, and nothing reopens it
    expect(prevented).toEqual([true, false]);
    expect(more().getAttribute('aria-expanded')).toBe('false');
  } finally {
    document.removeEventListener('keydown', spy);
  }
});

it('calls onSelect once for the chosen item, closes, and leaves focus on More', async () => {
  const user = userEvent.setup();
  const onSelect = vi.fn();
  render(<MoreMenu items={items(onSelect)} />);
  await user.click(more());
  await user.tab();
  await user.tab();
  expect(document.activeElement?.textContent).toBe('Export');
  await user.keyboard('{Enter}');
  expect(onSelect).toHaveBeenCalledTimes(1);
  expect(more().getAttribute('aria-expanded')).toBe('false');
  expect(screen.queryByRole('button', { name: 'Export' })).toBeNull();
  expect(document.activeElement).toBe(more());
});

it('lets onSelect move focus elsewhere (a panel heading)', async () => {
  const user = userEvent.setup();
  render(
    <>
      <MoreMenu items={[{ id: 'a', label: 'Open', onSelect: () => document.getElementById('target')?.focus() }]} />
      <h2 id="target" tabIndex={-1}>Panel</h2>
    </>,
  );
  await user.click(more());
  await user.click(screen.getByRole('button', { name: 'Open' }));
  expect(document.activeElement?.id).toBe('target');
});

it('closes when focus leaves it, without moving focus', async () => {
  const user = userEvent.setup();
  render(
    <>
      <MoreMenu items={[{ id: 'a', label: 'Only', onSelect: vi.fn() }]} />
      <button type="button">After</button>
    </>,
  );
  await user.click(more());
  await user.tab();
  await user.tab();
  expect(document.activeElement?.textContent).toBe('After');
  expect(more().getAttribute('aria-expanded')).toBe('false');
});

it('walks the item buttons in order with Tab', async () => {
  const user = userEvent.setup();
  render(
    <MoreMenu
      items={[
        { id: 'a', label: 'First', onSelect: vi.fn() },
        { id: 'b', label: 'Second', onSelect: vi.fn() },
      ]}
    />,
  );
  await user.click(more());
  await user.tab();
  expect(document.activeElement?.textContent).toBe('First');
  await user.tab();
  expect(document.activeElement?.textContent).toBe('Second');
});

it('renders a disabled item as a disabled button that does not select', async () => {
  const user = userEvent.setup();
  const onSelect = vi.fn();
  render(<MoreMenu items={[{ id: 'x', label: 'Unavailable', onSelect, disabled: true }]} />);
  await user.click(more());
  const button = screen.getByRole('button', { name: 'Unavailable' }) as HTMLButtonElement;
  expect(button.disabled).toBe(true);
  await user.click(button);
  expect(onSelect).not.toHaveBeenCalled();
});

it('has no accessibility violations closed or open', async () => {
  const user = userEvent.setup();
  const { container } = render(<MoreMenu items={items()} />);
  await expectNoAxeViolations(container);
  await user.click(more());
  await expectNoAxeViolations(container);
});
