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

it('ends closed with focus on More when the global Esc handler is installed too', async () => {
  const user = userEvent.setup();
  document.addEventListener('keydown', closeOnEscape);
  try {
    render(<MoreMenu items={items()} />);
    await user.click(more());
    await user.tab();
    await user.keyboard('{Escape}');
    expect(more().getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(more());
    await user.keyboard('{Escape}'); // already closed: nothing reopens it
    expect(more().getAttribute('aria-expanded')).toBe('false');
  } finally {
    document.removeEventListener('keydown', closeOnEscape);
  }
});

it('calls onSelect once for the chosen item and closes', async () => {
  const user = userEvent.setup();
  const onSelect = vi.fn();
  render(<MoreMenu items={items(onSelect)} />);
  await user.click(more());
  await user.click(screen.getByRole('button', { name: 'Export' }));
  expect(onSelect).toHaveBeenCalledTimes(1);
  expect(more().getAttribute('aria-expanded')).toBe('false');
  expect(screen.queryByRole('button', { name: 'Export' })).toBeNull();
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
