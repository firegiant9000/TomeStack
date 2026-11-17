// Helpers shared by the e2e files. Everything here is used by two or more of them; a helper used by one file stays in that file.
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expect } from 'vitest';

/**
 * Group 2 navigation: a new character goes "+ Add a character" (sidebar) > "Make a new one" > Continue. The sidebar button is
 * enabled before app.info loads; the chooser disables "Make a new one" until it has, so the radio is waited on.
 */
export async function startNewCharacter(user: ReturnType<typeof userEvent.setup>): Promise<void> {
  await user.click(await screen.findByRole('button', { name: '+ Add a character' }));
  const make = await screen.findByRole<HTMLInputElement>('radio', { name: 'Make a new one' });
  await waitFor(() => expect(make.disabled).toBe(false));
  await user.click(make);
  await user.click(screen.getByRole('button', { name: 'Continue' }));
}

/** The brand button in the window title area always goes home (the sidebar's "All characters" shows only with 2+ characters). */
export async function goHome(user: ReturnType<typeof userEvent.setup>): Promise<void> {
  await user.click(await screen.findByRole('button', { name: 'TomeStack, your characters' }));
}

export function bytesOf(base64: string): Uint8Array<ArrayBuffer> {
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

/**
 * Waits until the status line says `text`. The status line stays on screen, so `findByRole('status')` would return at
 * once with the previous message (lint forbids it here).
 */
export async function expectStatus(text: RegExp): Promise<HTMLElement> {
  await waitFor(() => expect(appStatus().textContent).toMatch(text));
  return appStatus();
}

/**
 * The app's status line. On a sheet the "Last roll" is a second `role="status"` node (D96), so a plain
 * `getByRole('status')` finds two; this excludes it by name and expects exactly one line left.
 */
export function appStatus(): HTMLElement {
  const lines = screen.getAllByRole('status').filter((el) => el.getAttribute('aria-label') !== 'Last roll');
  if (lines.length !== 1) throw new Error(`Expected one status line, found ${lines.length}.`);
  return lines[0]!;
}

/**
 * Activates one tab of a character sheet (ADR-014). Inactive panels are hidden, so a query finds only the active
 * tab's sections; a test must open the tab a user would open. A no-op when the tab is already selected.
 */
export async function openTab(user: ReturnType<typeof userEvent.setup>, sheet: HTMLElement, name: string): Promise<void> {
  const tab = within(sheet).getByRole('tab', { name });
  if (tab.getAttribute('aria-selected') !== 'true') await user.click(tab);
}

/** Opens the sheet's More disclosure and chooses one item (Choices, Print…, Export…, Snapshots, Updates, Archive). */
export async function openMore(user: ReturnType<typeof userEvent.setup>, sheet: HTMLElement, item: string): Promise<void> {
  await user.click(within(sheet).getByRole('button', { name: 'More' }));
  await user.click(within(sheet).getByRole('button', { name: item }));
}

/** One value of the sheet's status bar: the `.status-value` beside the `.status-label` named `term` (button or plain item). */
export function summaryValue(sheet: HTMLElement, term: string): string {
  const summary = within(sheet).getByRole('region', { name: 'Status' });
  const label = Array.from(summary.querySelectorAll('.status-label')).find((l) => l.textContent?.trim() === term);
  if (!label) throw new Error(`No status bar item named ${term}`);
  return label.parentElement!.querySelector('.status-value')!.textContent!.trim();
}

/** Ticks one option of the choice whose legend starts with `legend`. */
export async function pick(user: ReturnType<typeof userEvent.setup>, legend: RegExp, option: RegExp) {
  const group = await screen.findByRole('group', { name: legend });
  await user.click(await within(group).findByRole('checkbox', { name: option })); // options load after the choices
}

/** D32: walks the seven create steps (rules, class, scores, species, background, gear, choices). `scores` uses "Enter by hand" ({} keeps the default scores); otherwise the standard array is assigned in order. */
export async function createCharacter(
  user: ReturnType<typeof userEvent.setup>,
  opts: {
    name: string;
    family?: 'srd-5.1' | 'srd-5.2.1';
    scores?: Partial<Record<'Strength' | 'Dexterity' | 'Constitution' | 'Intelligence' | 'Wisdom' | 'Charisma', number>>;
    species?: RegExp;
    cls?: RegExp;
    background?: RegExp;
    /** A checkbox under "Other content" on the Background step. */
    other?: RegExp;
    /** Keep the starting kits on the Gear step instead of skipping it; `beforeChoices` runs there first. */
    kit?: boolean;
    beforeChoices?: () => Promise<void>;
  },
) {
  await startNewCharacter(user);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), opts.name);
  if (opts.family) await user.click(screen.getByRole('radio', { name: opts.family === 'srd-5.2.1' ? /2024 rules/ : /2014 rules/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  // The class options only appear once the listing for the chosen rules family has arrived, so this pick is never dropped.
  if (opts.cls) await user.click(await screen.findByRole('radio', { name: opts.cls }));
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  if (opts.scores) {
    await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
    const group = screen.getByRole('group', { name: 'Base ability scores' });
    for (const [label, value] of Object.entries(opts.scores)) {
      const input = within(group).getByRole('spinbutton', { name: label });
      await user.clear(input);
      await user.type(input, String(value));
    }
  } else {
    const group = screen.getByRole('group', { name: 'Assign the standard array' });
    for (const [label, value] of [['Strength', '15'], ['Dexterity', '14'], ['Constitution', '13'], ['Intelligence', '12'], ['Wisdom', '10'], ['Charisma', '8']] as const)
      await user.selectOptions(within(group).getByRole('combobox', { name: label }), value);
  }
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  if (opts.species) await user.click(await screen.findByRole('radio', { name: opts.species }));
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
  if (opts.background) await user.click(await screen.findByRole('radio', { name: opts.background }));
  if (opts.other) {
    await user.click(screen.getByText(/^Other content \(/)); // the summary, not the legend inside it
    await user.click(await screen.findByRole('checkbox', { name: opts.other }));
  }
  await user.click(screen.getByRole('button', { name: 'Next: gear' }));
  // Gear: most walks skip it so inventories stay as the test expects; `kit` keeps the guides' defaults (option a) and `beforeChoices` runs on the step.
  if (opts.kit) await opts.beforeChoices?.();
  else await user.click(screen.getByRole('button', { name: /^Skip: I'll add gear myself/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
}

export const srd = (n: number) => ({
  contentId: `52c00000-0000-4000-8000-${String(n).padStart(12, '0')}`,
  revisionId: `52e00000-0000-4000-8000-${String(n).padStart(12, '0')}`,
});

export const fighter = (n: number) => srd(408000 + n);
