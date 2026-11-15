// @vitest-environment jsdom
// A screen that throws, or whose code cannot be loaded, says so in plain words, offers the diagnostics and a way home, and leaves the rest of the page usable. Invented text only.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { lazy, Suspense, useState } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import { expectNoAxeViolations } from '../test/axe';
import { ScreenBoundary } from './ScreenBoundary';

vi.mock('../api/client', () => ({ client: { copyDiagnostics: vi.fn() } }));

beforeEach(() => {
  vi.spyOn(console, 'error').mockImplementation(() => {});
  vi.mocked(client.copyDiagnostics).mockReset();
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function Throws(): never {
  throw new Error('Fixture failure');
}

/** Throws until "fixed" flips; stands in for the next screen rendering after Back. */
function Harness({ onHome }: { onHome: () => void }) {
  const [fixed, setFixed] = useState(false);
  return (
    <ScreenBoundary
      onHome={() => {
        setFixed(true);
        onHome();
      }}
    >
      {fixed ? <p>Fixture home</p> : <Throws />}
    </ScreenBoundary>
  );
}

it('shows plain words, not a blank page, when a lazy screen fails to load, and keeps the navigation outside it usable', async () => {
  const Broken = lazy<() => null>(() => Promise.reject(new Error('Failed to fetch dynamically imported module: /assets/Fixture-abc.js')));
  render(
    <div>
      <nav>
        <button type="button">Fixture nav</button>
      </nav>
      <ScreenBoundary>
        <Suspense fallback={<p role="status">Loading…</p>}>
          <Broken />
        </Suspense>
      </ScreenBoundary>
    </div>,
  );
  const alert = await screen.findByRole('alert');
  expect(alert.textContent).toContain('Your saved characters are safe.');
  expect(document.body.textContent).not.toContain('Fixture-abc'); // no text of the error
  expect(screen.getByRole('button', { name: 'Fixture nav' })).toBeTruthy();
});

it('a throwing screen shows the fallback with its heading, the diagnostics and the way back', () => {
  render(<Harness onHome={() => {}} />);
  const heading = screen.getByRole('heading', { level: 1 });
  expect(heading.textContent).toBe('Something went wrong on this screen');
  expect(document.activeElement).toBe(heading);
  expect(screen.getByRole('alert').textContent).toBe('Your saved characters are safe.');
  expect(screen.getByRole('button', { name: 'Copy diagnostics' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Back to your characters' })).toBeTruthy();
  expect(document.body.textContent).not.toContain('Fixture failure');
});

it('Back resets the boundary, goes home and renders the next screen', async () => {
  const user = userEvent.setup();
  const onHome = vi.fn();
  render(<Harness onHome={onHome} />);
  await user.click(screen.getByRole('button', { name: 'Back to your characters' }));
  expect(onHome).toHaveBeenCalledTimes(1);
  expect(screen.getByText('Fixture home')).toBeTruthy();
  expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
});

it('shows the diagnostics text selected after Copy diagnostics', async () => {
  const user = userEvent.setup();
  vi.mocked(client.copyDiagnostics).mockResolvedValue({ text: 'Fixture diagnostics' });
  render(<Harness onHome={() => {}} />);
  await user.click(screen.getByRole('button', { name: 'Copy diagnostics' }));
  const box = (await screen.findByLabelText('Diagnostics text')) as HTMLTextAreaElement;
  expect(box.value).toBe('Fixture diagnostics');
});

it('says so in plain words when the diagnostics cannot be read, and Back still works', async () => {
  const user = userEvent.setup();
  vi.mocked(client.copyDiagnostics).mockRejectedValue(new Error('Fixture secret path'));
  const onHome = vi.fn();
  render(<Harness onHome={onHome} />);
  await user.click(screen.getByRole('button', { name: 'Copy diagnostics' }));
  const alerts = await screen.findAllByRole('alert');
  expect(alerts.some((a) => a.textContent?.includes('The diagnostics could not be read'))).toBe(true);
  expect(document.body.textContent).not.toContain('Fixture secret path');
  await user.click(screen.getByRole('button', { name: 'Back to your characters' }));
  expect(onHome).toHaveBeenCalledTimes(1);
  expect(screen.getByText('Fixture home')).toBeTruthy();
});

it('has no axe violations', async () => {
  const { container } = render(<Harness onHome={() => {}} />);
  await expectNoAxeViolations(container);
});

it('is reachable and usable from the keyboard', async () => {
  const user = userEvent.setup();
  const onHome = vi.fn();
  render(<Harness onHome={onHome} />);
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Copy diagnostics' }));
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Back to your characters' }));
  await user.keyboard('{Enter}');
  expect(onHome).toHaveBeenCalledTimes(1);
  expect(screen.getByText('Fixture home')).toBeTruthy();
});
