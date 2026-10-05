// @vitest-environment jsdom
// Settings: a radio group for the theme and a checkbox for the dice animation, both kept in the page's own storage.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it } from 'vitest';
import type { AppInfo } from '../api/types';
import { SettingsPanel } from './SettingsPanel';

afterEach(() => {
  cleanup();
  localStorage.clear();
  delete document.documentElement.dataset.theme;
});

it('offers the three themes as a radio group, applies the pick at once and remembers it', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  expect(screen.getByRole('heading', { level: 2, name: 'Settings' })).toBeTruthy();
  const group = screen.getByRole('radiogroup', { name: 'Theme' });
  expect(screen.getAllByRole('radio').map((r) => r.getAttribute('aria-label') ?? r.closest('label')?.textContent?.trim())).toEqual(['Cool', 'Forest', 'Violet']);
  expect(screen.getByRole<HTMLInputElement>('radio', { name: 'Forest' }).checked).toBe(true);
  await user.click(screen.getByRole('radio', { name: 'Cool' }));
  expect(document.documentElement.dataset.theme).toBe('cool');
  expect(localStorage.getItem('tomestack.theme')).toBe('cool');
  expect(group).toBeTruthy();
});

it('names the version and the data schema, which the app header no longer shows', () => {
  render(<SettingsPanel info={{ version: '0.0.0-fixture', schemaVersion: 99 } as AppInfo} />);
  expect(screen.getByText(/^TomeStack 0\.0\.0-fixture, data schema 99\./)).toBeTruthy();
});

it('switches the dice animation off and on', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  const dice = screen.getByRole<HTMLInputElement>('checkbox', { name: 'Animate dice' });
  expect(dice.checked).toBe(true);
  await user.click(dice);
  expect(localStorage.getItem('tomestack.diceAnimation')).toBe('off');
  await user.click(dice);
  expect(localStorage.getItem('tomestack.diceAnimation')).toBeNull();
});
