// @vitest-environment jsdom
// Settings: a radio group for the theme and a checkbox for the dice animation, both kept in the page's own storage.
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { clampAccent } from '../accentClamp';
import type { AppInfo } from '../api/types';
import { SettingsPanel } from './SettingsPanel';

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  Reflect.deleteProperty(document, 'adoptedStyleSheets');
  localStorage.clear();
  delete document.documentElement.dataset.theme;
  for (const key of Object.keys(document.documentElement.dataset)) delete document.documentElement.dataset[key];
});

it('offers the presets and Custom accent as a radio group, applies the pick at once and remembers it', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  expect(screen.getByRole('heading', { level: 2, name: 'Settings' })).toBeTruthy();
  const group = screen.getByRole('radiogroup', { name: 'Theme' });
  expect(within(group).getAllByRole('radio').map((r) => r.closest('label')?.textContent?.trim())).toEqual(['Cool', 'Ember', 'Forest', 'Rose', 'Tide', 'Violet', 'Custom accent']);
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

it('groups the screen under Appearance, Accessibility, Keyboard shortcuts and About headings', () => {
  render(<SettingsPanel />);
  expect(screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['Appearance', 'Accessibility', 'Keyboard shortcuts', 'About']);
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

it('offers the accessibility settings as labelled controls that apply at once', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  await user.click(within(screen.getByRole('radiogroup', { name: 'Motion' })).getByRole('radio', { name: 'Reduce motion' }));
  expect(document.documentElement.dataset.motion).toBe('reduce');
  await user.click(screen.getByRole('checkbox', { name: 'Stronger borders and labels' }));
  expect(document.documentElement.dataset.contrast).toBe('more');
  await user.click(screen.getByRole('checkbox', { name: 'Always show the focus outline' }));
  expect(document.documentElement.dataset.focus).toBe('always');
  await user.click(screen.getByRole('checkbox', { name: 'Larger buttons' }));
  expect(document.documentElement.dataset.targets).toBe('large');
  await user.click(screen.getByRole('checkbox', { name: 'Underline links and text buttons' }));
  expect(document.documentElement.dataset.underline).toBe('on');
  const announce = screen.getByRole<HTMLInputElement>('checkbox', { name: 'Announce each roll to screen readers' });
  expect(announce.checked).toBe(true);
  await user.click(announce);
  expect(localStorage.getItem('tomestack.announceRolls')).toBe('off');
});

function stubAdoptedSheets() {
  class FakeSheet {
    text = '';
    replaceSync(text: string) {
      this.text = text;
    }
  }
  vi.stubGlobal('CSSStyleSheet', FakeSheet);
  Object.defineProperty(document, 'adoptedStyleSheets', { configurable: true, writable: true, value: [] });
  return () => (document.adoptedStyleSheets as unknown as { text: string }[]).map((x) => x.text).join(' ');
}

it('applies each new preset at once and remembers it, with no hue slider', async () => {
  const user = userEvent.setup();
  render(<SettingsPanel />);
  for (const id of ['ember', 'rose', 'tide']) {
    await user.click(screen.getByRole('radio', { name: id[0]!.toUpperCase() + id.slice(1) }));
    expect(document.documentElement.dataset.theme).toBe(id);
    expect(localStorage.getItem('tomestack.theme')).toBe(id);
    expect(screen.queryByRole('slider')).toBeNull();
  }
});

it('shows the Accent hue slider only while Custom accent is chosen, and hides it again for a preset', async () => {
  const user = userEvent.setup();
  stubAdoptedSheets();
  render(<SettingsPanel />);
  expect(screen.queryByRole('slider')).toBeNull();
  await user.click(screen.getByRole('radio', { name: 'Custom accent' }));
  const slider = screen.getByRole<HTMLInputElement>('slider', { name: 'Accent hue' });
  expect(slider.type).toBe('range');
  expect([slider.min, slider.max, slider.step]).toEqual(['0', '359', '1']);
  expect(document.documentElement.dataset.theme).toBe('custom');
  expect(localStorage.getItem('tomestack.theme')).toBe('custom');
  await user.click(screen.getByRole('radio', { name: 'Tide' }));
  expect(screen.queryByRole('slider')).toBeNull();
  expect(localStorage.getItem('tomestack.theme')).toBe('tide');
  expect(localStorage.getItem('tomestack.accentHue')).toBeNull(); // nothing was dragged
});

it('reads the hue as a colour word and a number, stores it, and re-applies the generated accent as it moves', () => {
  localStorage.setItem('tomestack.theme', 'custom');
  localStorage.setItem('tomestack.accentHue', '180');
  const applied = stubAdoptedSheets();
  render(<SettingsPanel />);
  const slider = screen.getByRole<HTMLInputElement>('slider', { name: 'Accent hue' });
  expect(slider.value).toBe('180');
  expect(slider.getAttribute('aria-valuetext')).toBe('Teal, 180 degrees');
  fireEvent.change(slider, { target: { value: '30' } });
  expect(slider.getAttribute('aria-valuetext')).toBe('Red, 30 degrees');
  expect(localStorage.getItem('tomestack.accentHue')).toBe('30');
  expect(applied()).toContain(clampAccent(30, 'light').accent);
  expect(applied()).not.toContain(`--accent: light-dark(${clampAccent(180, 'light').accent},`); // (hue 30's accent-2 is hue 180's accent: 150 degrees apart)
});

it('offers a live sample: a line in the accent, a filled chip and a focus-ring swatch, hidden from assistive technology', () => {
  localStorage.setItem('tomestack.theme', 'custom');
  stubAdoptedSheets();
  render(<SettingsPanel />);
  const sample = screen.getByTestId('accent-sample');
  expect(sample.getAttribute('aria-hidden')).toBe('true');
  expect(sample.querySelector('.accent-sample-text')).not.toBeNull();
  expect(sample.querySelector('.accent-sample-chip')).not.toBeNull();
  expect(sample.querySelector('.accent-sample-ring')).not.toBeNull();
  expect(sample.hasAttribute('style')).toBe(false);
  expect(sample.querySelector('[style]')).toBeNull();
});
