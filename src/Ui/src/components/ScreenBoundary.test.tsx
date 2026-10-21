// @vitest-environment jsdom
// A lazily loaded screen whose code cannot be loaded says so in plain words and leaves the rest of the page usable. Invented text only.
import { cleanup, render, screen } from '@testing-library/react';
import { lazy, Suspense } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { ScreenBoundary } from './ScreenBoundary';

afterEach(cleanup);

it('shows plain words, not a blank page, when a lazy screen fails to load, and keeps the navigation outside it usable', async () => {
  vi.spyOn(console, 'error').mockImplementation(() => {});
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
  expect(alert.textContent).toContain("This screen couldn't be loaded.");
  expect(alert.textContent).not.toContain('Fixture-abc'); // no text of the error
  expect(screen.getByRole('button', { name: 'Fixture nav' })).toBeTruthy();
  vi.restoreAllMocks();
});
