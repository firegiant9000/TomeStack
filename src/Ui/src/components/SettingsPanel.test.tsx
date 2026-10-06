// @vitest-environment jsdom
// Settings: a radio group for the theme and a checkbox for the dice animation, both kept in the page's own storage.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it } from 'vitest';
import type { AppInfo } from '../api/types';
import { SettingsPanel } from './SettingsPanel';

afterEach(() => {
  cleanup();
  localStorage.clear();
  delete document.documentElement.dataset.theme;
  for (const key of Object.keys(document.documentElement.dataset)) delete document.documentElement.dataset[key];
});

it('offers the three themes as a radio group, applies the pick at once and remembers it', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  expect(screen.getByRole('heading', { level: 2, name: 'Settings' })).toBeTruthy();
  const group = screen.getByRole('radiogroup', { name: 'Theme' });
  expect(within(group).getAllByRole('radio').map((r) => r.closest('label')?.textContent?.trim())).toEqual(['Cool', 'Forest', 'Violet']);
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

it('groups the screen under Appearance, Keyboard shortcuts and About headings', () => {
  render(<SettingsPanel />);
  expect(screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['Appearance', 'Keyboard shortcuts', 'About']);
  expect(screen.getByText('Ctrl+B', { selector: 'dt' }).nextElementSibling!.textContent).toContain('sidebar');
});

it('offers System, Light and Dark as the colour scheme, applies the pick at once and remembers it', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  const group = screen.getByRole('radiogroup', { name: 'Colour scheme' });
  expect(within(group).getByRole<HTMLInputElement>('radio', { name: 'System' }).checked).toBe(true);
  await user.click(within(group).getByRole('radio', { name: 'Dark' }));
  expect(document.documentElement.dataset.appearance).toBe('dark');
  expect(localStorage.getItem('tomestack.appearance')).toBe('dark');
  await user.click(within(group).getByRole('radio', { name: 'System' }));
  expect(document.documentElement.dataset.appearance).toBeUndefined();
  expect(localStorage.getItem('tomestack.appearance')).toBeNull();
});

it('offers text sizes from 90% to 200% and applies the pick to the root element', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  const size = screen.getByRole<HTMLSelectElement>('combobox', { name: 'Text size' });
  expect(Array.from(size.options).map((o) => o.value)).toEqual(['90', '100', '110', '125', '150', '175', '200']);
  expect(size.value).toBe('100');
  await user.selectOptions(size, '150');
  expect(document.documentElement.dataset.textSize).toBe('150');
  expect(localStorage.getItem('tomestack.textSize')).toBe('150');
});

it('offers modifier-first or score-first ability boxes (owner, 2026-10-06)', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  const group = screen.getByRole('radiogroup', { name: 'Ability boxes' });
  expect(within(group).getByRole<HTMLInputElement>('radio', { name: 'Modifier first' }).checked).toBe(true);
  await user.click(within(group).getByRole('radio', { name: 'Score first' }));
  expect(document.documentElement.dataset.abilityOrder).toBe('score');
  expect(localStorage.getItem('tomestack.abilityOrder')).toBe('score');
});
