// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell): create -> sheet -> override
// -> export -> import. Only the transport differs from the desktop app: HTTP to loopback instead of the WebView2
// bridge, so export takes the download fallback instead of the native Save dialog.
import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, inject, it, vi } from 'vitest';
import { zip } from './zip';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { downloadBase64 } from '../src/files';
import { fromTemplate, templates } from '../src/templates';

vi.mock('../src/api/client', async (importOriginal) => {
  const { inject } = await import('vitest');
  const { createHttpTransport } = await import('../src/api/transport');
  const original = await importOriginal<typeof import('../src/api/client')>();
  const { endpoint, token } = inject('devHost');
  return { ...original, client: original.createClient(createHttpTransport(endpoint, { 'X-TomeStack-Token': token })) };
});

vi.mock('../src/files', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../src/files')>()),
  downloadBase64: vi.fn(),
}));

afterEach(() => {
  cleanup();
  // One jsdom window serves the whole file: a preference or `html` attribute left by one test would leak into the next.
  // The per-character tab memory stays (ids are unique per test and the deep-link test relies on it).
  for (const key of Object.keys(localStorage)) if (key.startsWith('tomestack.') && !key.startsWith('tomestack.sheetTab.')) localStorage.removeItem(key);
  for (const key of Object.keys(document.documentElement.dataset)) delete document.documentElement.dataset[key];
});

function bytesOf(base64: string): Uint8Array<ArrayBuffer> {
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

/**
 * Waits until the status line says `text`. The status line stays on screen, so `findByRole('status')` would return at
 * once with the previous message (lint forbids it here).
 */
async function expectStatus(text: RegExp): Promise<HTMLElement> {
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(text));
  return screen.getByRole('status');
}

/**
 * Activates one tab of a character sheet (ADR-014). Inactive panels are hidden, so a query finds only the active
 * tab's sections; a test must open the tab a user would open. A no-op when the tab is already selected.
 */
async function openTab(user: ReturnType<typeof userEvent.setup>, sheet: HTMLElement, name: string): Promise<void> {
  const tab = within(sheet).getByRole('tab', { name });
  if (tab.getAttribute('aria-selected') !== 'true') await user.click(tab);
}

/** One value of the sheet's summary bar: the text of the `<dd>` after the `<dt>` named `term` (slice 2). */
function summaryValue(sheet: HTMLElement, term: string): string {
  const summary = within(sheet).getByRole('region', { name: 'Summary' });
  return within(summary).getByText(term, { selector: 'dt' }).nextElementSibling!.textContent!.trim();
}

it('creates a character, shows its traced sheet, overrides, exports and re-imports it', async () => {
  const user = userEvent.setup();
  render(<App />);

  // Create (the default scores: Dex 14)
  await createCharacter(user, { name: 'E2E Pell', family: 'srd-5.1', scores: {}, species: /^Fixture Quickfoot/ });
  expect(await screen.findByText('Nothing to choose at this level.')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Create and save' }));

  // Sheet with a source-aware trace: Dex 14 + 2 (Fixture Quickfoot, species under 2014 rules) = 16 -> +3
  const sheet = await screen.findByRole('article', { name: 'E2E Pell' });
  await waitFor(() => expect(document.activeElement).toBe(within(sheet).getByRole('heading', { level: 2, name: 'E2E Pell' })));
  await openTab(user, sheet, 'Stats');
  const initiative = within(sheet).getByRole('region', { name: /^Initiative:/ });
  expect(within(initiative).getByRole('heading').textContent).toContain('+3');
  expect(within(sheet).getByRole('heading', { name: /^Dexterity score: 16/ })).toBeTruthy();
  await user.click(within(initiative).getByRole('heading')); // expand the field (summary)
  const trace = within(initiative).getByRole('table', { name: 'How initiative is calculated' });
  expect(within(trace).getByText(/TomeStack Fixtures: 2014 Family, p\. 1/)).toBeTruthy();

  // Override (labeled, keeps the computed value)
  await user.type(within(initiative).getByRole('spinbutton', { name: 'Override value' }), '9');
  await user.type(within(initiative).getByRole('textbox', { name: 'Reason (optional)' }), 'Table ruling');
  await user.click(within(initiative).getByRole('button', { name: 'Apply override' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Initiative:/ }).textContent).toContain('overridden (calculated +3)'));

  // D22: a session note on Notes is the player's journal; the share preview below must not list it.
  await openTab(user, sheet, 'Notes');
  const journal = within(sheet).getByRole('form', { name: 'New session note' });
  await user.type(within(journal).getByRole('textbox', { name: 'Note' }), 'Private session note for the share test.');
  await user.click(within(journal).getByRole('button', { name: 'Save session note' }));
  await within(sheet).findByText('Private session note for the share test.');

  // Export (DevHost has no native dialog: package.saveAs -> unsupported -> download fallback)
  await openTab(user, sheet, 'Manage');
  await user.click(screen.getByRole('button', { name: 'Export package' }));
  await waitFor(() => expect(downloadBase64).toHaveBeenCalledTimes(1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[0]!;
  expect(fileName).toBe('E2E-Pell-personal-backup.tomestack.zip'); // default purpose: personal backup (ADR-007)

  // Share: the preview says what is left out (nothing here: the fixture sources may be shared), then exports.
  await user.click(screen.getByRole('radio', { name: /Share with someone/ }));
  const leftOut = await screen.findByRole('region', { name: 'Left out of the shared package' });
  expect(leftOut.textContent).toMatch(/Nothing is left out/);
  expect(leftOut.textContent).not.toMatch(/session note/i);
  await user.click(screen.getByRole('button', { name: 'Export package' }));
  await waitFor(() => expect(downloadBase64).toHaveBeenCalledTimes(2));
  expect(vi.mocked(downloadBase64).mock.calls[1]![0]).toBe('E2E-Pell.tomestack.zip');

  // Import: preview first, then apply. The character already exists, so the local copy is backed up.
  const file = new File([bytesOf(base64)], fileName, { type: 'application/zip' });
  await user.upload(screen.getByLabelText('Package file'), file);
  const preview = await screen.findByRole('region', { name: `Import ${fileName}` });
  expect(within(preview).getByText(/already exists and will be replaced/)).toBeTruthy();
  await user.click(within(preview).getByRole('button', { name: 'Apply import' }));

  const status = await expectStatus(/1 replaced/);
  expect(status.getAttribute('role')).toBe('status');
  expect(status.textContent).toMatch(/backed up to backups\/pre-import-/);
  await openTab(user, await screen.findByRole('article', { name: 'E2E Pell' }), 'Stats');
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Initiative:/ }).textContent).toContain('overridden (calculated +3)'));
});

/** Ticks one option of the choice whose legend starts with `legend`. */
async function pick(user: ReturnType<typeof userEvent.setup>, legend: RegExp, option: RegExp) {
  const group = await screen.findByRole('group', { name: legend });
  await user.click(await within(group).findByRole('checkbox', { name: option })); // options load after the choices
}

/** D32: walks the six create steps. `scores` uses "Enter by hand" ({} keeps the default scores); otherwise the standard array is assigned in order. */
async function createCharacter(
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
  },
) {
  // "New character" is disabled until app.info has loaded, so a click is never silently ignored.
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));
  await user.click(newCharacter);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), opts.name);
  if (opts.family) await user.click(screen.getByRole('radio', { name: opts.family === 'srd-5.2.1' ? /SRD 5\.2\.1/ : /SRD 5\.1/ }));
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
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  if (opts.cls) await user.click(await screen.findByRole('radio', { name: opts.cls }));
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
  if (opts.background) await user.click(await screen.findByRole('radio', { name: opts.background }));
  if (opts.other) {
    await user.click(screen.getByText(/^Other content \(/)); // the summary, not the legend inside it
    await user.click(await screen.findByRole('checkbox', { name: opts.other }));
  }
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
}

it('builds an SRD 5.2.1 Barbarian as drafts: create, cancel a level-up, level to 3 with a subclass', async () => {
  const user = userEvent.setup();
  render(<App />);
  // The M1 acceptance character Brenna (Str 15, Dex 13, Con 14, Int 8, Wis 12, Cha 10); D31: only the character's family is listed.
  await createCharacter(user, {
    name: 'E2E Brenna',
    family: 'srd-5.2.1',
    scores: { Strength: 15, Dexterity: 13, Constitution: 14, Intelligence: 8, Wisdom: 12, Charisma: 10 },
    species: /^Dwarf/,
    cls: /^Barbarian/,
    background: /^Soldier/,
  });

  // Level-1 choices are offered and flagged until answered; the subclass (level 3) is not offered yet.
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'New character' })));
  expect(await screen.findByText(/2 choices still to make/)).toBeTruthy();
  expect(screen.queryByRole('group', { name: /^Barbarian: choose 1/ })).toBeNull();
  await pick(user, /^Soldier: choose 1/, /^Soldier Ability Scores: Strength \+2, Constitution \+1/);
  await pick(user, /^Barbarian: choose 2/, /^Barbarian Skill: Perception/);
  await pick(user, /^Barbarian: choose 2/, /^Barbarian Skill: Survival/);
  expect(await screen.findByText('All choices are made.')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Create and save' }));

  let sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^Strength score: 17/ })).toBeTruthy();
  expect(within(sheet).queryByRole('heading', { name: 'Choices to make' })).toBeNull();

  // A cancelled level-up draft changes nothing.
  await user.click(within(sheet).getByRole('button', { name: 'Level up' }));
  await user.click(await screen.findByRole('radio', { name: /Barbarian \(level 1 → 2\)/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  expect(await screen.findByText('All choices are made.')).toBeTruthy(); // level 2 offers no new choice
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  await expectStatus(/Draft discarded/);
  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  expect(within(sheet).getByText('Level 1')).toBeTruthy();

  // Level 2, then 3: the subclass and Primal Knowledge choices appear at level 3.
  for (const next of [2, 3]) {
    await user.click(within(await screen.findByRole('article', { name: 'E2E Brenna' })).getByRole('button', { name: 'Level up' }));
    await user.click(await screen.findByRole('radio', { name: new RegExp(`Barbarian \\(level ${next - 1} → ${next}\\)`) }));
    await user.click(screen.getByRole('button', { name: 'Next: choices' }));
    if (next === 3) {
      await pick(user, /^Barbarian: choose 1/, /^Path of the Berserker/);
      // Leave Primal Knowledge open: the save is allowed and the sheet flags it.
      expect(await screen.findByText(/1 choice still to make/)).toBeTruthy();
    }
    await user.click(await screen.findByRole('button', { name: 'Save level-up' }));
  }

  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  expect(within(sheet).getByText('Level 3')).toBeTruthy();
  const open = within(sheet).getByRole('heading', { name: 'Choices to make' }).parentElement!;
  expect(open.textContent).toMatch(/Primal Knowledge: choose 1 \(0 chosen\)/);

  // Answer it from the sheet: Survival is already chosen elsewhere, so pick Animal Handling.
  await user.click(within(open).getByRole('button', { name: 'Make choices' }));
  await pick(user, /^Primal Knowledge: choose 1/, /^Barbarian Skill: Animal Handling/);
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));

  // The M1 acceptance values for Brenna (m1-acceptance.md): AC 13, HP 35, Animal Handling +3.
  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^Armor Class: 13/ })).toBeTruthy();
  expect(within(sheet).getByRole('heading', { name: /^Hit point maximum: 35/ })).toBeTruthy();
  expect(within(sheet).getByRole('heading', { name: /^Animal Handling: \+3/ })).toBeTruthy();
  expect(within(sheet).queryByRole('heading', { name: 'Choices to make' })).toBeNull();

  // M2 item 2, play: resources with calculated maximums, explicit spending, hit points, conditions and rolls.
  await openTab(user, sheet, 'Play');
  const resources = within(sheet).getByRole('region', { name: 'Resources' });
  expect(within(resources).getByRole('heading', { name: 'Rages: 3 of 3' })).toBeTruthy();
  expect(within(resources).getByRole('heading', { name: 'Stonecunning: 2 of 2' })).toBeTruthy();
  await user.click(within(resources).getByRole('button', { name: 'Spend 1 Rages' }));
  await waitFor(() => expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 2 of 3' })).toBeTruthy());

  const hp = screen.getByRole('region', { name: /^Hit points:/ });
  await user.type(within(hp).getByRole('spinbutton', { name: 'Amount' }), '5');
  await user.click(within(hp).getByRole('button', { name: 'Set temporary hit points' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35, 5 temporary' })).toBeTruthy());
  await user.type(within(screen.getByRole('region', { name: /^Hit points:/ })).getByRole('spinbutton', { name: 'Amount' }), '12');
  await user.click(within(screen.getByRole('region', { name: /^Hit points:/ })).getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 28 of 35' })).toBeTruthy());

  // D23: Undo puts the hit points and the temporary hit points back, then the damage is taken again.
  await user.click(await screen.findByRole('button', { name: 'Undo last change: damage 12' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35, 5 temporary' })).toBeTruthy());
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true));
  const amount = () => within(screen.getByRole('region', { name: /^Hit points:/ })).getByRole<HTMLInputElement>('spinbutton', { name: 'Amount' });
  await user.clear(amount());
  await user.type(amount(), '12');
  await user.click(within(screen.getByRole('region', { name: /^Hit points:/ })).getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 28 of 35' })).toBeTruthy());

  await user.click(screen.getByRole('checkbox', { name: 'Poisoned' }));
  await waitFor(() => expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Poisoned' }).checked).toBe(true));

  // A feature roll shows its record, and rolling spends nothing.
  await user.click(screen.getByRole('checkbox', { name: /Critical hit/ }));
  await user.click(screen.getByRole('button', { name: 'Roll Frenzy extra damage (Rage Damage +2: 2d6) (2d6)' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  expect(within(screen.getByRole('region', { name: 'Summary' })).getByRole('region', { name: 'Last roll' })).toBe(lastRoll);
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Frenzy extra damage.*: \d+ \(2d6, critical\)/));
  expect(lastRoll.textContent).toMatch(/d6 \d \(critical\)/); // doubled dice are marked
  expect(lastRoll.textContent).toMatch(/Frenzy \(System Reference Document 5\.2\.1, p\. \d+\)/);

  // A d20 test with advantage keeps one die and drops the other.
  await user.click(screen.getByRole('radio', { name: 'Advantage' }));
  await openTab(user, sheet, 'Stats');
  const strSave = screen.getByRole('region', { name: /^Strength saving throw:/ });
  await user.click(within(strSave).getByRole('heading'));
  await user.click(within(strSave).getByRole('button', { name: 'Roll Strength saving throw' }));
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Strength saving throw \(d20 test\): \d+ \(1d20, advantage\)/));
  expect(lastRoll.textContent).toMatch(/\(dropped\)/);
  expect(lastRoll.textContent).toMatch(/Strength saving throw \+5/);
  // D28: the roll before it is in Previous rolls, outside the live region.
  await user.click(within(sheet).getByText('Previous rolls (1)'));
  expect(within(sheet).getByText('Previous rolls (1)').closest('details')!.textContent).toMatch(/Frenzy extra damage.*: \d+ \(2d6, critical\)/);
  expect(lastRoll.textContent).not.toMatch(/Previous rolls/);
  await openTab(user, sheet, 'Play');
  expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 2 of 3' })).toBeTruthy();

  // Features list their automation status.
  await openTab(user, sheet, 'Features');
  const features = screen.getByRole('region', { name: 'Features' });
  expect(within(features).getByText('Danger Sense').closest('li')!.textContent).toMatch(/reference only|assisted/);

  // M2 item 3, long rest (D01): preview first, cancel changes nothing, then confirm.
  await openTab(user, sheet, 'Play');
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  let rest = await screen.findByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(document.activeElement).toBe(within(rest).getByRole('heading', { name: 'Long rest' })));
  // The heading is focused on mount; the preview's changes arrive afterwards (restPreview), so wait for them.
  expect(await within(rest).findByRole('checkbox', { name: /^Hit points: 28 → 35/ })).toBeTruthy();
  expect(await within(rest).findByRole('checkbox', { name: /^Rages: 2 → 3/ })).toBeTruthy();
  await user.click(within(rest).getByRole('button', { name: 'Cancel rest' }));
  expect(screen.getByRole('heading', { name: 'Hit points: 28 of 35' })).toBeTruthy();

  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  rest = await screen.findByRole('region', { name: 'Long rest' });
  await user.click(await within(rest).findByRole('checkbox', { name: /^Rages: 2 → 3/ })); // untick: keep Rages as they are
  await user.click(within(rest).getByRole('button', { name: 'Finish long rest' }));
  await expectStatus(/Long rest finished: 1 change applied/);
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35' })).toBeTruthy());
  expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 2 of 3' })).toBeTruthy();

  // Short rest (D01 follow-up): spend a hit die rolled at the table (5 + Con 2 = 7), and Rage regains one use (2024).
  const hpPanel = () => screen.getByRole('region', { name: /^Hit points:/ });
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '10');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 25 of 35' })).toBeTruthy());
  expect(hpPanel().textContent).toMatch(/Hit dice: 3 of 3 \(d12\)/);
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  rest = await screen.findByRole('region', { name: 'Short rest' });
  await waitFor(() => expect(document.activeElement).toBe(within(rest).getByRole('heading', { name: 'Short rest' })));
  expect(await within(rest).findByRole('checkbox', { name: /^Rages: 2 → 3/ })).toBeTruthy();
  await user.type(within(rest).getByRole('spinbutton', { name: 'd12 rolled at the table' }), '5');
  await user.click(within(rest).getByRole('button', { name: 'Add d12' }));
  const spend = await within(rest).findByRole('list', { name: 'Hit dice to spend' });
  expect(spend.textContent).toMatch(/Rolled 5, Constitution modifier \+2: 7 hit point\(s\)\. Hit points 25 → 32/);
  await user.click(within(rest).getByRole('button', { name: 'Finish short rest' }));
  await expectStatus(/Short rest finished: 2 changes applied/);
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 32 of 35' })).toBeTruthy());
  expect(hpPanel().textContent).toMatch(/Hit dice: 2 of 3 \(d12\)/);
  expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 3 of 3' })).toBeTruthy();

  // Heroic Inspiration (2024) and death saving throws (SPEC C-05): a 20 at the table regains 1 hit point.
  await user.click(within(hpPanel()).getByRole('checkbox', { name: 'Heroic Inspiration' }));
  await waitFor(() => expect(within(hpPanel()).getByRole<HTMLInputElement>('checkbox', { name: 'Heroic Inspiration' }).checked).toBe(true));
  expect(screen.queryByRole('region', { name: /^Death saving throws/ })).toBeNull();
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '40');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Take damage' }));
  const deathSaves = await screen.findByRole('region', { name: /^Death saving throws: 0 of 3 successes, 0 of 3 failures/ });
  await user.click(within(deathSaves).getByRole('button', { name: 'Add a failure (damage at 0)' }));
  await screen.findByRole('region', { name: /^Death saving throws: 0 of 3 successes, 1 of 3 failures/ });
  // D23: healing from 0 clears the death saves, which no command puts back, so it offers no Undo (one that silently wiped
  // the failure would be worse than none). Then back to 0 hit points for the roll below.
  await user.clear(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }));
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '1');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Heal' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 1 of 35' })).toBeTruthy());
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
  await user.clear(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }));
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '1');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Take damage' }));
  await screen.findByRole('region', { name: /^Death saving throws: 0 of 3 successes, 0 of 3 failures/ });
  await user.type(within(screen.getByRole('region', { name: /^Death saving throws/ })).getByRole('spinbutton', { name: 'd20 rolled at the table' }), '20');
  await user.click(within(screen.getByRole('region', { name: /^Death saving throws/ })).getByRole('button', { name: 'Record this roll' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 1 of 35' })).toBeTruthy());
  expect(screen.queryByRole('region', { name: /^Death saving throws/ })).toBeNull(); // regaining hit points cleared them
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '34');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Heal' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35' })).toBeTruthy());

  // M2 item 4: armor replaces Unarmored Defense (13); a shield adds to it. Original fixture equipment.
  await openTab(user, sheet, 'Inventory');
  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^Fixture Scale Vest/ })).toBeTruthy());
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Scale Vest/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Scale Vest' }));
  await waitFor(() => expect(summaryValue(sheet, 'Armor Class')).toBe('15')); // 14 + Dex 1
  await openTab(user, sheet, 'Stats');
  const ac = screen.getByRole('region', { name: /^Armor Class:/ });
  await user.click(within(ac).getByRole('heading'));
  expect(within(ac).getByRole('table').textContent).toMatch(/Unarmored Defense.*not used: it applies only while no armor is worn/);

  await openTab(user, sheet, 'Inventory');
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Kite Shield/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Kite Shield' }));
  await waitFor(() => expect(summaryValue(sheet, 'Armor Class')).toBe('17'));
  await user.click(within(equipment()).getByRole('checkbox', { name: 'Equip Fixture Scale Vest' })); // take the armor off
  await waitFor(() => expect(summaryValue(sheet, 'Armor Class')).toBe('15')); // Unarmored Defense 13 + shield 2
});

const srd = (n: number) => ({
  contentId: `52c00000-0000-4000-8000-${String(n).padStart(12, '0')}`,
  revisionId: `52e00000-0000-4000-8000-${String(n).padStart(12, '0')}`,
});

it('authors a homebrew subclass in the studio, plays it, and reviews an update', async () => {
  const user = userEvent.setup();
  // Setup through the client: an SRD 5.2.1 Barbarian 3 without a subclass (the builder test covers building one).
  await client.createCharacter({
    name: 'E2E Storm',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 15, dex: 13, con: 14, int: 8, wis: 12, cha: 10 },
    pins: [srd(1), srd(2)],
    classes: [{ class: srd(11), level: 3 }],
    choices: [
      { source: srd(2), choiceId: 'soldier-ability-scores', selected: [srd(4)] },
      { source: srd(11), choiceId: 'barbarian-skills', selected: [srd(21), srd(22)] },
      { source: srd(25), choiceId: 'primal-knowledge-skill', selected: [srd(17)] },
    ],
  });
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);

  // A homebrew source for 2024 rules.
  const newSource = await screen.findByRole('form', { name: 'New homebrew source' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Source title' }), 'E2E Homebrew');
  await user.click(within(newSource).getByRole('checkbox', { name: 'SRD 5.2.1 (2024 rules)' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create source' }));
  await screen.findByRole('heading', { name: 'Content in E2E Homebrew' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(editor()).getByRole('group', { name });
  async function publish(name: string) {
    await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe(`Published ${name}.`));
  }

  // Feature 1: a class resource, its long-rest recovery, and a limited-use action that spends it.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Storm Ward');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(within(rule(/^Rule 1: Resource/)).getByRole('textbox', { name: 'Resource name' }), 'Storm charges');
  await user.click(within(editor()).getByRole('button', { name: 'Add recovery' }));
  await user.click(within(editor()).getByRole('button', { name: 'Add roll or action' }));
  const action = rule(/^Rule 3: Roll or action/);
  await user.type(within(action).getByRole('textbox', { name: 'Roll name' }), 'Storm bolt');
  await user.clear(within(action).getByRole('textbox', { name: /^Dice/ }));
  await user.type(within(action).getByRole('textbox', { name: /^Dice/ }), '1d8 + 2');
  await user.selectOptions(within(action).getByRole('combobox', { name: /Uses a resource/ }), 'Storm charges');
  await publish('E2E Storm Ward');

  // Feature 2: reference-only text.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Sky Lore');
  await user.type(within(editor()).getByRole('textbox', { name: /^Description/ }), 'You can read tomorrow’s weather.');
  await publish('E2E Sky Lore');

  // The subclass: offered in the SRD Barbarian's subclass choice, a modifier, and both features at level 3.
  await user.click(screen.getByRole('button', { name: 'New subclass' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'Path of the E2E Storm');
  const offered = within(editor()).getByRole('combobox', { name: 'Offered in the choice' });
  await waitFor(() => expect(within(offered).getByRole('option', { name: /^Barbarian: Level 3: Barbarian Subclass/ })).toBeTruthy());
  await user.selectOptions(offered, within(offered).getByRole('option', { name: /^Barbarian: Level 3: Barbarian Subclass/ }));
  await user.click(within(editor()).getByRole('button', { name: 'Add modifier' })); // default: Initiative +1
  await user.click(within(editor()).getByRole('button', { name: 'Grant a feature' }));
  await user.selectOptions(within(rule(/^Rule 2: Granted feature/)).getByRole('combobox', { name: 'Feature' }), 'E2E Storm Ward (published)');
  await user.click(within(editor()).getByRole('button', { name: 'Grant a feature' }));
  await user.selectOptions(within(rule(/^Rule 3: Granted feature/)).getByRole('combobox', { name: 'Feature' }), 'E2E Sky Lore (published)');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect(await within(editor()).findByText('No problems found. It can be published.')).toBeTruthy();
  await publish('Path of the E2E Storm');

  // Play it: the homebrew subclass is an option of the SRD choice.
  await user.click(screen.getByRole('button', { name: /^E2E Storm/ }));
  let sheet = await screen.findByRole('article', { name: 'E2E Storm' });
  await user.click(within(sheet).getByRole('button', { name: 'Make choices' }));
  await pick(user, /^Barbarian: choose 1/, /^Path of the E2E Storm/);
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));
  sheet = await screen.findByRole('article', { name: 'E2E Storm' });
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^Initiative: \+2/ })).toBeTruthy(); // Dex +1, homebrew +1
  await openTab(user, sheet, 'Play');
  const resources = within(sheet).getByRole('region', { name: 'Resources' });
  expect(within(resources).getByRole('heading', { name: 'Storm charges: 2 of 2' })).toBeTruthy();
  await openTab(user, sheet, 'Features');
  expect(within(within(sheet).getByRole('region', { name: 'Features' })).getByText('E2E Sky Lore').closest('li')!.textContent).toMatch(/reference only/);
  await openTab(user, sheet, 'Play');
  await user.click(within(sheet).getByRole('button', { name: 'Roll Storm bolt (1d8 + 2)' }));
  await user.click(await screen.findByRole('button', { name: 'Spend 1 Storm charges (2 left)' }));
  await waitFor(() => expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Storm charges: 1 of 2' })).toBeTruthy());

  // Republish with +3 initiative: the character keeps +2 until the reviewed update is applied.
  await user.click(screen.getByRole('button', { name: 'Homebrew studio' }));
  await user.click(await screen.findByRole('button', { name: 'Edit Path of the E2E Storm' }));
  const modifier = rule(/^Rule 1: Modifier/);
  await user.clear(within(modifier).getByRole('textbox', { name: /^Value/ }));
  await user.type(within(modifier).getByRole('textbox', { name: /^Value/ }), '3');
  await publish('Path of the E2E Storm');
  expect(await screen.findByRole('button', { name: 'Review update for E2E Storm' })).toBeTruthy();

  // M3 C7: the sheet offers the new revision too, and nothing changes until the reviewed update is applied.
  await user.click(screen.getByRole('button', { name: /^E2E Storm/ }));
  sheet = await screen.findByRole('article', { name: 'E2E Storm' });
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^Initiative: \+2/ })).toBeTruthy();
  await openTab(user, sheet, 'Manage');
  const updates = await within(sheet).findByRole('region', { name: 'Updates available' });
  // The setup pins the M1 SRD Barbarian revision, so the newer bundled revision is offered as well.
  expect(within(updates).getByRole('button', { name: 'Review update: Barbarian' }).closest('li')!.textContent).toMatch(/bundled/);
  expect(within(updates).getByRole('button', { name: 'Review update: Path of the E2E Storm' }).closest('li')!.textContent).toMatch(/your source/);
  await user.click(within(updates).getByRole('button', { name: 'Review update: Path of the E2E Storm' }));
  const review = await within(updates).findByRole('region', { name: 'Update E2E Storm: Path of the E2E Storm' });
  const values = await within(review).findByRole('table', { name: 'Calculated values that change' });
  expect(values.textContent).toMatch(/Initiative24/);
  await user.click(within(review).getByRole('button', { name: 'Apply update' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/Updated E2E Storm: Path of the E2E Storm/));
  await waitFor(() => expect(summaryValue(screen.getByRole('article', { name: 'E2E Storm' }), 'Initiative')).toBe('+4'));
  const remaining = () => within(within(screen.getByRole('article', { name: 'E2E Storm' })).getByRole('region', { name: 'Updates available' }));
  await waitFor(() => expect(remaining().queryByRole('button', { name: 'Review update: Path of the E2E Storm' })).toBeNull());
  expect(remaining().getByRole('button', { name: 'Review update: Barbarian' })).toBeTruthy(); // still only offered
});

it('attaches a PDF to a source, offers the cited page on a feature, and removes it after a warning', async () => {
  const user = userEvent.setup();
  // Setup through the client: a homebrew source, a published feat citing page 7, and a character using it.
  const source = await client.createHomebrewSource('E2E Book', ['srd-5.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Cited Feat',
    rulesFamilies: ['srd-5.1'],
    provenance: { sourceId: source.id, page: { start: 7 } },
    status: 'draft',
    summary: 'A feat that cites page 7.',
    effects: [],
  });
  const feat = (await client.publish(draft)).published;
  await client.createCharacter({ name: 'E2E Reader', rulesFamily: 'srd-5.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [feat] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Sources' }));
  const book = await screen.findByRole('listitem', { name: 'E2E Book' });
  expect(within(book).getByText('No PDF attached.')).toBeTruthy();

  // DevHost has no native Open dialog, so the browser file picker sends the bytes (a managed copy).
  await user.click(within(book).getByRole('button', { name: 'Attach PDF…' }));
  const pdf = new File([new TextEncoder().encode('%PDF-1.4\n% e2e\n%%EOF\n')], 'e2e-book.pdf', { type: 'application/pdf' });
  await user.upload(screen.getByLabelText('PDF file'), pdf);
  // The upload hashes and copies the file, then reloads every source (e2e/timeouts.setup.ts allows for that).
  await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'E2E Book' })).getByText(/PDF: e2e-book\.pdf .*copy in TomeStack.*available/)).toBeTruthy());

  // SPEC I-03: pages 3-4 become a draft reference entry (nothing is extracted; it stays inactive until published).
  const pages = within(screen.getByRole('listitem', { name: 'E2E Book' })).getByRole('group', { name: 'Import pages of E2E Book as reference' });
  await user.type(within(pages).getByRole('spinbutton', { name: 'First page' }), '3');
  await user.type(within(pages).getByRole('spinbutton', { name: 'Last page (optional)' }), '4');
  await user.type(within(pages).getByRole('textbox', { name: 'Title (optional)' }), 'E2E Chapter');
  await user.click(within(pages).getByRole('button', { name: 'Import pages' }));
  await expectStatus(/Draft reference entry "E2E Chapter" created/);
  const drafts = await client.contentBySource(source.id);
  expect(drafts.find((e) => e.name === 'E2E Chapter')?.revisions[0]).toMatchObject({ status: 'draft', provenance: { page: { start: 3, end: 4 } } });

  // The feature offers its cited page; opening needs the desktop app's viewer, which DevHost does not have.
  await user.click(screen.getByRole('button', { name: /^E2E Reader/ }));
  // The button appears once the sheet has fetched the source's attachment (e2e/timeouts.setup.ts allows for that).
  await openTab(user, await screen.findByRole('article', { name: 'E2E Reader' }), 'Features');
  const open = await screen.findByRole('button', { name: 'Open E2E Cited Feat, p. 7' });
  await user.click(open);
  expect((await screen.findByRole('alert')).textContent).toMatch(/needs the TomeStack desktop app/);

  // Removing says what breaks, and keeps the content.
  await user.click(screen.getByRole('button', { name: 'Sources' }));
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Book' })).getByRole('button', { name: 'Remove PDF…' }));
  const confirm = await screen.findByRole('alertdialog', { name: 'Remove e2e-book.pdf?' });
  // The draft reference entry cites pages in it too.
  expect(confirm.textContent).toMatch(/2 entries cite pages in it \(E2E Chapter, E2E Cited Feat\)/);
  await user.click(within(confirm).getByRole('button', { name: 'Remove PDF' }));
  await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'E2E Book' })).getByText('No PDF attached.')).toBeTruthy());
  await user.click(screen.getByRole('button', { name: /^E2E Reader/ }));
  await screen.findByRole('article', { name: 'E2E Reader' });
  await openTab(user, screen.getByRole('article', { name: 'E2E Reader' }), 'Features');
  expect(screen.queryByRole('button', { name: 'Open E2E Cited Feat, p. 7' })).toBeNull();
  expect(within(screen.getByRole('region', { name: 'Features' })).getByText('E2E Cited Feat')).toBeTruthy();
});

it('marks a source as shareable after confirming it is your own work, saves a source pack and imports it (M6 slice 1)', async () => {
  const user = userEvent.setup();
  // Setup through the client: your own source with a published feat, and a source a PDF was attached to.
  const own = await client.createHomebrewSource('E2E Own Notes', ['srd-5.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Own Feat',
    rulesFamilies: ['srd-5.1'],
    provenance: { sourceId: own.id },
    status: 'draft',
    summary: 'An original feat.',
    effects: [],
  });
  await client.publish(draft);
  const scanned = await client.createHomebrewSource('E2E Scanned Notes', ['srd-5.1']);
  await client.attachPdfData(scanned.id, 'e2e-scan.pdf', btoa('%PDF-1.4\n% e2e\n%%EOF\n'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Sources' }));
  const mine = await screen.findByRole('listitem', { name: 'E2E Own Notes' });
  expect(within(mine).getByText('Not shared.')).toBeTruthy();
  // A source a PDF was attached to is import-derived for good: it offers no way to share it.
  const theirs = screen.getByRole('listitem', { name: 'E2E Scanned Notes' });
  expect(within(theirs).getByText(/Holds material imported from a PDF: never shared/)).toBeTruthy();
  expect(within(theirs).queryByRole('button', { name: 'Mark as shareable…' })).toBeNull();

  // Marking needs the author's confirmation.
  await user.click(within(mine).getByRole('button', { name: 'Mark as shareable…' }));
  const confirm = await screen.findByRole('alertdialog', { name: 'Mark E2E Own Notes as shareable?' });
  const mark = within(confirm).getByRole<HTMLButtonElement>('button', { name: 'Mark as shareable' });
  expect(mark.disabled).toBe(true);
  await user.click(within(confirm).getByRole('checkbox', { name: /The source is my own work/ }));
  await user.click(mark);
  await expectStatus(/E2E Own Notes is marked as shareable/);
  await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'E2E Own Notes' })).getByText(/Marked as shareable/)).toBeTruthy());

  // The pack offers only shareable sources; DevHost has no Save dialog, so it downloads.
  const packForm = screen.getByRole('region', { name: 'Share sources as a pack' });
  expect(within(packForm).queryByRole('checkbox', { name: 'E2E Scanned Notes' })).toBeNull();
  await user.click(within(packForm).getByRole('checkbox', { name: 'E2E Own Notes' }));
  await user.click(within(packForm).getByRole('button', { name: 'Preview pack' }));
  const preview = await within(packForm).findByRole('region', { name: 'Source pack preview' });
  expect(preview.textContent).toMatch(/1 source, 1 published revision/);
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(preview).getByRole('button', { name: 'Save pack…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(fileName).toBe('E2E-Own-Notes-source-pack.tomestack.zip');

  // Importing it: the preview says what a source pack is; here everything is already installed.
  await user.upload(screen.getByLabelText('Package file'), new File([bytesOf(base64)], fileName, { type: 'application/zip' }));
  const importing = await screen.findByRole('region', { name: `Import ${fileName}` });
  expect(within(importing).getByText(/This is a source pack\. Its sender stated that each source is their own work/)).toBeTruthy();
  await user.click(within(importing).getByRole('button', { name: 'Apply import' }));
  await expectStatus(/unchanged/);
});

it('shares a campaign as a campaign pack that names what it leaves out, and imports it over a changed copy by choice (M6 slice 2)', async () => {
  const user = userEvent.setup();
  // Setup through the client: your own shareable homebrew, homebrew you did not mark, and a campaign allowing both and the SRD.
  async function homebrew(title: string, shareable: boolean) {
    const source = await client.createHomebrewSource(title, ['srd-5.2.1']);
    if (shareable) await client.setShareable(source.id, true, true);
    const draft = await client.saveDraft({
      contentId: crypto.randomUUID(),
      revisionId: '00000000-0000-0000-0000-000000000000',
      kind: 'feat',
      name: `${title} Feat`,
      rulesFamilies: ['srd-5.2.1'],
      provenance: { sourceId: source.id },
      status: 'draft',
      summary: 'An original feat.',
      effects: [],
    });
    await client.publish(draft);
    return source;
  }
  const harbor = await homebrew('E2E Harbor Notes', true);
  const secret = await homebrew('E2E Secret Notes', false);
  const campaign = await client.saveCampaign({
    id: '00000000-0000-0000-0000-000000000000',
    name: 'E2E Harbor Table',
    rulesFamily: 'srd-5.2.1',
    allowedSources: ['52500000-0000-4000-8000-000000000001', harbor.id, secret.id],
    houseRules: 'Original house rule: rests take a full day.',
  });

  render(<App />);
  const campaignsButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Campaigns' });
  await waitFor(() => expect(campaignsButton.disabled).toBe(false));
  await user.click(campaignsButton);
  await user.click(await screen.findByRole('button', { name: 'Share E2E Harbor Table…' }));
  const share = await screen.findByRole('region', { name: 'Share E2E Harbor Table' });
  await waitFor(() => expect(share.textContent).toMatch(/1 source carried with 1 published revision; System Reference Document 5\.2\.1 named, not copied/));
  // The guard: homebrew you did not mark as your own shareable work is only named.
  const leftOut = within(share).getByRole('region', { name: 'Left out of the campaign pack' });
  expect(leftOut.textContent).toMatch(/E2E Secret Notes .*not marked as shareable/);

  // DevHost has no Save dialog, so it downloads.
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(share).getByRole('button', { name: 'Save campaign pack…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(fileName).toBe('E2E-Harbor-Table-campaign-pack.tomestack.zip');

  // Here the campaign changes after the pack was made; importing it asks which version to keep.
  await client.saveCampaign({ ...campaign, name: 'E2E Harbor Table (local)', allowedSources: ['52500000-0000-4000-8000-000000000001', harbor.id] });
  await user.upload(screen.getByLabelText('Package file'), new File([bytesOf(base64)], fileName, { type: 'application/zip' }));
  const importing = await screen.findByRole('region', { name: `Import ${fileName}` });
  expect(within(importing).getByText(/This is a campaign pack/)).toBeTruthy();
  const apply = within(importing).getByRole<HTMLButtonElement>('button', { name: 'Apply import' });
  expect(apply.disabled).toBe(true);
  const decision = within(importing).getByRole('group', { name: 'Campaign “E2E Harbor Table” differs from yours' });
  expect(within(decision).getByRole('rowheader', { name: 'allowedSources' })).toBeTruthy();
  await user.click(within(decision).getByRole('radio', { name: 'Use the imported version' }));
  await user.click(apply);
  await expectStatus(/1 replaced.*copied to backups\/pre-import-.*\.db/);
  expect((await client.listCampaigns()).find((c) => c.id === campaign.id)?.name).toBe('E2E Harbor Table');
});

it('installs the sample extension after granting its permissions, runs its import and export hooks, and removes it (M6 slice 3)', async () => {
  const user = userEvent.setup();
  // The external sample, packed as a user would: extension.json and transforms/ from examples/extensions.
  const folder = resolve(process.cwd(), '../../examples/extensions/spell-list-and-sheet-summary');
  const files: Record<string, Uint8Array> = { 'extension.json': readFileSync(resolve(folder, 'extension.json')) };
  for (const name of readdirSync(resolve(folder, 'transforms'))) files[`transforms/${name}`] = readFileSync(resolve(folder, 'transforms', name));
  const extension = new File([zip(files)], 'spell-list-and-sheet-summary.tomestack-ext.zip', { type: 'application/zip' });
  await client.createCharacter({ name: 'E2E Ext Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 14, con: 12, int: 10, wis: 10, cha: 10 }, pins: [] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Extensions' }));
  // DevHost has no native Open dialog, so the browser picker is used.
  await user.click(await screen.findByRole('button', { name: 'Install extension…' }));
  await user.upload(screen.getByLabelText('Extension file'), extension);
  const review = await screen.findByRole('region', { name: 'Install Spell list and sheet summary (sample)?' });
  const install = within(review).getByRole<HTMLButtonElement>('button', { name: 'Install with these permissions' });
  expect(install.disabled).toBe(true); // nothing is granted until it is ticked
  const permissions = within(within(review).getByRole('group', { name: 'Permissions to grant' })).getAllByRole('checkbox');
  expect(permissions).toHaveLength(4);
  for (const permission of permissions) await user.click(permission);
  await user.click(install);
  await expectStatus(/Installed Spell list and sheet summary \(sample\) with 4 permissions/);
  const item = await screen.findByRole('listitem', { name: 'Spell list and sheet summary (sample)' });

  // Import: a CSV spell list becomes drafts in a new source; nothing is written before "Create drafts".
  await user.click(within(item).getByRole('button', { name: 'Import a CSV spell list as draft spells' }));
  const importRun = await screen.findByRole('region', { name: 'Run Import a CSV spell list as draft spells' });
  await user.clear(within(importRun).getByRole('textbox', { name: 'New source for the drafts' }));
  await user.type(within(importRun).getByRole('textbox', { name: 'New source for the drafts' }), 'E2E Imported Spells');
  await user.click(within(importRun).getByRole('button', { name: 'Choose CSV file…' }));
  const csv = readFileSync(resolve(folder, 'sample-spells.csv'));
  await user.upload(screen.getByLabelText('Import a CSV spell list as draft spells: file'), new File([csv], 'sample-spells.csv', { type: 'text/csv' }));
  const drafts = await within(importRun).findByRole('region', { name: 'Preview of Import a CSV spell list as draft spells' });
  expect(drafts.textContent).toMatch(/3 drafts in the new source E2E Imported Spells/);
  expect(drafts.textContent).toMatch(/never shared/);
  expect((await client.listSources()).some((s) => s.title === 'E2E Imported Spells')).toBe(false);
  await user.click(within(drafts).getByRole('button', { name: 'Create drafts' }));
  await expectStatus(/Created 3 drafts in the new source E2E Imported Spells/);
  const imported = (await client.listSources()).find((s) => s.title === 'E2E Imported Spells')!;
  expect(imported.importDerived).toBe(true);
  expect((await client.contentBySource(imported.id)).every((e) => e.latest.status === 'draft')).toBe(true);

  // Export: the sheet summary, previewed and then saved (DevHost downloads). No local path reaches it.
  await user.click(within(screen.getByRole('listitem', { name: 'Spell list and sheet summary (sample)' })).getByRole('button', { name: 'Export a sheet summary as Markdown' }));
  const exportRun = await screen.findByRole('region', { name: 'Run Export a sheet summary as Markdown' });
  await user.selectOptions(within(exportRun).getByRole('combobox', { name: 'Character' }), 'E2E Ext Hero');
  await user.click(within(exportRun).getByRole('button', { name: 'Preview output' }));
  const output = await within(exportRun).findByRole('region', { name: 'Preview of Export a sheet summary as Markdown' });
  expect(output.textContent).toMatch(/# E2E Ext Hero/);
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(output).getByRole('button', { name: 'Save output…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(fileName).toBe('E2E-Ext-Hero-sheet-markdown.md');
  const markdown = new TextDecoder().decode(bytesOf(base64));
  expect(markdown).toMatch(/## Sources and licenses/);
  expect(markdown.toLowerCase().replaceAll('\\', '/')).not.toContain(inject('devHost').dataDir.toLowerCase().replaceAll('\\', '/'));

  // Remove: its permissions and file go; its drafts stay.
  await user.click(within(screen.getByRole('listitem', { name: 'Spell list and sheet summary (sample)' })).getByRole('button', { name: 'Remove Spell list and sheet summary (sample)…' }));
  await user.click(within(await screen.findByRole('alertdialog', { name: 'Remove Spell list and sheet summary (sample)?' })).getByRole('button', { name: 'Remove' }));
  await expectStatus(/Removed Spell list and sheet summary \(sample\)/);
  expect(await client.listExtensions()).toEqual([]);
  expect(await client.contentBySource(imported.id)).toHaveLength(3);
});

it('exports a character for Foundry VTT and as sheet JSON after a preview, with no local path in either file (M6 slice 4)', async () => {
  const user = userEvent.setup();
  const character = await client.createCharacter({ name: 'E2E VTT Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 14, con: 12, int: 10, wis: 10, cha: 10 }, pins: [] });
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E VTT Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E VTT Hero' });
  await openTab(user, sheet, 'Manage');
  const panel = within(sheet).getByRole('region', { name: 'Export for a virtual tabletop' });
  const dataDir = inject('devHost').dataDir.toLowerCase().replaceAll('\\', '/');

  // Foundry VTT (dnd5e): the preview names the pinned release, then DevHost downloads the file.
  await user.click(within(panel).getByRole('radio', { name: /Foundry VTT \(dnd5e system\)/ }));
  await user.click(within(panel).getByRole('button', { name: 'Preview export' }));
  const preview = await within(panel).findByRole('region', { name: 'Export preview' });
  expect(preview.textContent).toMatch(/E2E-VTT-Hero\.foundry-dnd5e\.json.*checked against Foundry VTT 14\.367 with dnd5e 6\.0\.5/);
  expect(preview.textContent).toMatch(/not affiliated with Foundry Gaming LLC/);
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(preview).getByRole('button', { name: 'Save export file…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [foundryName, foundry64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(foundryName).toBe('E2E-VTT-Hero.foundry-dnd5e.json');
  const actorText = new TextDecoder().decode(bytesOf(foundry64));
  const actor = JSON.parse(actorText) as { type: string; name: string; _stats: { systemVersion: string } };
  expect([actor.type, actor.name, actor._stats.systemVersion]).toEqual(['character', 'E2E VTT Hero', '6.0.5']);
  expect(actorText.toLowerCase().replaceAll('\\\\', '/').replaceAll('\\', '/')).not.toContain(dataDir);

  // The neutral sheet JSON.
  await user.click(within(panel).getByRole('radio', { name: /TomeStack sheet \(JSON\)/ }));
  await user.click(within(panel).getByRole('button', { name: 'Preview export' }));
  await user.click(within(await within(panel).findByRole('region', { name: 'Export preview' })).getByRole('button', { name: 'Save export file…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 2));
  const [sheetName, sheet64] = vi.mocked(downloadBase64).mock.calls[downloads + 1]!;
  expect(sheetName).toBe('E2E-VTT-Hero.tomestack-sheet.json');
  const model = JSON.parse(new TextDecoder().decode(bytesOf(sheet64))) as { format: string; formatVersion: number; character: { name: string } };
  expect([model.format, model.formatVersion, model.character.name]).toEqual(['tomestack.sheet', 1, 'E2E VTT Hero']);
  expect(character.character.id).toBeTruthy();
});

it('follows the authoring guides: the extension guide, packed as written, installs, is granted and exports its features with every notice (M6 slice 5)', async () => {
  const user = userEvent.setup();
  // docs/authoring/extension.md, its tagged blocks exactly as the guide shows them.
  const guide = readFileSync(resolve(process.cwd(), '../../docs/authoring/extension.md'), 'utf8');
  const files: Record<string, Uint8Array> = {};
  for (const match of guide.matchAll(/```json tomestack-example:([^\r\n]+)\r?\n([\s\S]*?)\r?\n```/g)) {
    const name = match[1]!.trim();
    if (name === 'extension.json' || name.startsWith('transforms/')) files[name] = new TextEncoder().encode(match[2]!);
  }
  expect(Object.keys(files).sort()).toEqual(['extension.json', 'transforms/features-md.json']);
  // A class, so the file has feature lines and an SRD notice.
  await client.createCharacter({ name: 'E2E Guide Reader', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [], classes: [{ class: srd(11), level: 1 }] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Extensions' }));
  await user.click(await screen.findByRole('button', { name: 'Install extension…' }));
  await user.upload(screen.getByLabelText('Extension file'), new File([zip(files)], 'example-feature-list.tomestack-ext.zip', { type: 'application/zip' }));
  // "Tick the permissions and choose Install with these permissions."
  const review = await screen.findByRole('region', { name: 'Install Example feature list?' });
  for (const permission of within(within(review).getByRole('group', { name: 'Permissions to grant' })).getAllByRole('checkbox')) await user.click(permission);
  await user.click(within(review).getByRole('button', { name: 'Install with these permissions' }));
  await expectStatus(/Installed Example feature list with 2 permissions/);
  // "Then choose the hook (Export features as Markdown), pick a character …, choose Preview output, and Save output…"
  await user.click(within(await screen.findByRole('listitem', { name: 'Example feature list' })).getByRole('button', { name: 'Export features as Markdown' }));
  const run = await screen.findByRole('region', { name: 'Run Export features as Markdown' });
  await user.selectOptions(within(run).getByRole('combobox', { name: 'Character' }), 'E2E Guide Reader');
  await user.click(within(run).getByRole('button', { name: 'Preview output' }));
  const output = await within(run).findByRole('region', { name: 'Preview of Export features as Markdown' });
  expect(output.textContent).toMatch(/# Features of E2E Guide Reader/);
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(output).getByRole('button', { name: 'Save output…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(fileName).toBe('E2E-Guide-Reader-features-md.md');
  const text = new TextDecoder().decode(bytesOf(base64));
  expect(text).toMatch(/^- .+ \([a-z]+\), from .+$/m); // at least one feature line
  expect(text).toMatch(/## Sources and licenses\n- .+CC-BY-4\.0/);
  await client.removeExtension((await client.listExtensions()).find((x) => x.manifest.name === 'Example feature list')!.id);
});

it('reads the fixture PDF, reviews its candidates, and publishes an accepted one through the studio', async () => {
  // M4 D5 (SPEC I-02): the original fixture book, read by the real worker next to the DevHost.
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Grimoire', ['srd-5.2.1']);
  // jsdom gives import.meta.url no file scheme; the e2e run starts in src/Ui (npm --prefix).
  const book = readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-import.pdf'));
  await client.attachPdfData(source.id, 'fixture-import.pdf', book.toString('base64'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Sources' }));
  const reader = () => within(screen.getByRole('listitem', { name: 'E2E Grimoire' })).getByRole('region', { name: 'Read the text of E2E Grimoire' });
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Grimoire' })).getByRole('button', { name: 'Read the whole document' }));
  // M4 stays experimental until a real third-party PDF and the SRD measurements pass (ROADMAP M4).
  expect(within(reader()).getByRole('heading', { name: /Read the text and find candidates Experimental/ })).toBeTruthy();
  const reviewButton = await within(reader()).findByRole('button', { name: 'Review 9 candidates' }, { timeout: 30000 });
  // Page 6 has no text layer: the worker tries Windows OCR where a language is installed, and finds nothing either way.
  expect(within(reader()).getByText(/whole document: completed, 6 pages read, (1 by OCR, )?1 without text, 9 candidates\./)).toBeTruthy();

  // SPEC I-03: the read text is searchable within this source.
  await user.type(within(reader()).getByRole('textbox', { name: 'Search the text of E2E Grimoire' }), 'hookblade');
  await user.click(within(reader()).getByRole('button', { name: 'Search' }));
  expect((await within(reader()).findByRole('list', { name: 'Search results' })).textContent).toMatch(/p\. 4.*Fixture Hookblade/);

  await user.click(reviewButton);
  const panel = () => within(reader()).getByRole('region', { name: 'Candidates from E2E Grimoire' });
  const list = () => within(panel()).getByRole('list', { name: 'Candidates' });
  await within(reader()).findByRole('region', { name: 'Candidates from E2E Grimoire' });
  await waitFor(() => expect(within(list()).getAllByRole('listitem')).toHaveLength(9));
  const kind = within(panel()).getByRole('combobox', { name: 'Kind' });
  const detail = (name: string) => within(panel()).findByRole('region', { name: `Candidate: ${name}` });

  // A clean spell: excerpt, page, what was read; the check passes; accepting makes a draft.
  await user.selectOptions(kind, 'spell');
  await waitFor(() => expect(within(list()).getAllByRole('listitem')).toHaveLength(2));
  await user.click(await within(list()).findByRole('button', { name: 'Fixture Ember Lance' })); // a slow PDF worker may still be filling the list
  const ember = await detail('Fixture Ember Lance');
  await waitFor(() => expect(document.activeElement).toBe(within(ember).getByRole('heading', { name: 'Candidate: Fixture Ember Lance' })));
  expect(within(ember).getByRole('figure').textContent).toMatch(/Excerpt from p\. 2.*Fixture Ember Lance.*3d6 Fire/s);
  expect(ember.querySelector('dl[aria-label="What was read"]')!.textContent).toMatch(/level2schoolevocationcastingtimeAction/);
  await user.click(within(ember).getByRole('button', { name: 'Open page 2' }));
  expect((await screen.findByRole('alert')).textContent).toMatch(/needs the TomeStack desktop app/);
  const acceptEmber = within(ember).getByRole<HTMLButtonElement>('button', { name: 'Accept as a draft' });
  await waitFor(() => expect(acceptEmber.disabled).toBe(false));
  await user.click(acceptEmber);
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/Fixture Ember Lance is now a draft in the studio/));
  // It left the "to review" list with its details, so focus goes back to the list (WCAG 2.4.3).
  await waitFor(() => expect(document.activeElement).toBe(within(panel()).getByRole('heading', { name: 'Candidates from E2E Grimoire' })));

  // Ignore the other spell.
  await user.click(await within(list()).findByRole('button', { name: 'Fixture Frost Veil' }));
  await user.click(within(await detail('Fixture Frost Veil')).getByRole('button', { name: 'Ignore' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/Ignored Fixture Frost Veil/));

  // An unresolved reference blocks "Accept as a draft"; "Accept as reference" keeps the text and page only.
  await user.selectOptions(kind, 'feature');
  await user.click(await within(list()).findByRole('button', { name: 'Fixture Stormcall' }));
  const stormcall = await detail('Fixture Stormcall');
  expect(within(within(stormcall).getByRole('group', { name: 'Unresolved references' })).getByRole('checkbox', { name: 'Dismiss Fixture Thunder Word' })).toBeTruthy();
  await within(stormcall).findByRole('list', { name: 'Blocks accepting' });
  expect(within(stormcall).getByRole<HTMLButtonElement>('button', { name: 'Accept as a draft' }).disabled).toBe(true);
  await user.click(within(stormcall).getByRole('button', { name: 'Accept as reference' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/Fixture Stormcall is now a draft reference entry/));

  // The feat: accepted as a draft, then published in the studio, which validates it again.
  await user.selectOptions(kind, 'feat');
  await user.click(await within(list()).findByRole('button', { name: 'Fixture Keen Watcher' }));
  const feat = await detail('Fixture Keen Watcher');
  const acceptFeat = within(feat).getByRole<HTMLButtonElement>('button', { name: 'Accept as a draft' });
  await waitFor(() => expect(acceptFeat.disabled).toBe(false));
  await user.click(acceptFeat);
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/Fixture Keen Watcher is now a draft/));

  // Only drafts exist: nothing is active until it is published.
  const entries = await client.contentBySource(source.id);
  expect(entries.map((e) => [e.name, e.latest.status]).sort()).toEqual([
    ['Fixture Ember Lance', 'draft'],
    ['Fixture Keen Watcher', 'draft'],
    ['Fixture Stormcall', 'draft'],
  ]);
  expect(entries.find((e) => e.name === 'Fixture Keen Watcher')!.latest.effects.every((e) => e.automation === 'reference')).toBe(true);

  const studio = screen.getByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await user.click(studio);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Grimoire/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Grimoire/ }));
  await user.click(await screen.findByRole('button', { name: 'Edit Fixture Keen Watcher' }));
  const editor = screen.getByRole('region', { name: /^Edit Fixture Keen Watcher/ });
  await user.click(within(editor).getByRole('button', { name: 'Publish' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Published Fixture Keen Watcher.'));
  expect((await client.contentBySource(source.id)).find((e) => e.name === 'Fixture Keen Watcher')!.latest.status).toBe('published');
});

it('shows different allowed content for two campaign profiles, and records a reasoned exception', async () => {
  const user = userEvent.setup();
  render(<App />);
  const campaignsButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Campaigns' });
  await waitFor(() => expect(campaignsButton.disabled).toBe(false));
  await user.click(campaignsButton);

  async function createCampaign(name: string, sources: RegExp[]) {
    await user.click(await screen.findByRole('button', { name: 'New campaign' }));
    const form = screen.getByRole('form', { name: 'Campaign' });
    await user.type(within(form).getByRole('textbox', { name: 'Campaign name' }), name);
    await user.click(within(form).getByRole('radio', { name: /SRD 5\.2\.1/ }));
    for (const source of sources) await user.click(within(form).getByRole('checkbox', { name: source }));
    await user.click(within(form).getByRole('button', { name: 'Save campaign' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe(`Saved the campaign ${name}.`));
  }
  await createCampaign('E2E Strict', [/^System Reference Document 5\.2\.1/]);
  await createCampaign('E2E Open', [/^System Reference Document 5\.2\.1/, /^TomeStack Fixtures: 2024 Family/]);

  // The campaign is chosen on the Rules step; the backgrounds it governs are on the Background step (D32), so this flow
  // walks the steps by hand instead of through `createCharacter`.
  await user.click(screen.getByRole('button', { name: 'New character' }));
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Campaigner');
  const courier = () => screen.getByRole<HTMLInputElement>('radio', { name: /^Fixture Courier/ });
  const soldier = () => screen.getAllByRole<HTMLInputElement>('radio', { name: /^Soldier/ }).find((r) => !r.disabled);
  const toBackground = async () => {
    await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
    await user.click(screen.getByRole('radio', { name: 'Enter by hand' })); // the default method needs all six scores assigned
    for (const next of ['Next: species', 'Next: class', 'Next: background']) await user.click(screen.getByRole('button', { name: next }));
  };
  const toRules = async () => {
    for (let i = 0; i < 4; i++) await user.click(screen.getByRole('button', { name: 'Back' }));
  };

  // Profile 1: SRD only. The fixture background is listed but not allowed; the SRD one is.
  await user.selectOptions(screen.getByRole('combobox', { name: 'Campaign' }), 'E2E Strict (srd-5.2.1)');
  await toBackground();
  await waitFor(() => expect(courier().disabled).toBe(true));
  expect(courier().closest('label')!.textContent).toMatch(/not allowed in this campaign/);
  expect(soldier()).toBeTruthy();

  // Profile 2: SRD and the 2024 fixtures. The same background is allowed.
  await toRules();
  await user.selectOptions(screen.getByRole('combobox', { name: 'Campaign' }), 'E2E Open (srd-5.2.1)');
  await toBackground();
  await waitFor(() => expect(courier().disabled).toBe(false));

  // Back to profile 1, with a deliberate exception: a reason is required before outside content can be picked.
  await toRules();
  await user.selectOptions(screen.getByRole('combobox', { name: 'Campaign' }), 'E2E Strict (srd-5.2.1)');
  await toBackground();
  await waitFor(() => expect(courier().disabled).toBe(true));
  const sources = screen.getByRole('group', { name: 'Campaign sources' });
  await user.click(within(sources).getByRole('checkbox', { name: 'Use content from outside the campaign' }));
  expect(courier().disabled).toBe(true); // no reason yet
  await user.type(within(sources).getByRole('textbox', { name: /^Reason/ }), 'DM approved');
  await waitFor(() => expect(courier().disabled).toBe(false));
  await user.click(courier());
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));

  const sheet = await screen.findByRole('article', { name: 'E2E Campaigner' });
  expect(within(sheet).getByText('Campaign: E2E Strict', { selector: '.tag' })).toBeTruthy();
  const notes = within(sheet).getByRole('region', { name: 'Campaign: E2E Strict' });
  expect(notes.textContent).toMatch(/Fixture Courier.*used by exception: DM approved/);
});

it('keeps a resource linked to its recovery when the resource is renamed in the studio', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Renames', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Renames/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Renames/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Renames' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Fury');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  const name = within(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ })).getByRole('textbox', { name: 'Resource name' });
  await user.type(name, 'Fury');
  await user.click(within(editor()).getByRole('button', { name: 'Add recovery' }));
  // Renamed after the recovery points at it: the recovery must still restore this resource.
  await user.type(name, ' points');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));

  const results = await within(editor()).findByRole('region', { name: 'Check results' });
  expect(results.textContent).not.toMatch(/does not define/);
});

it('finds a problem in a source with the debugger, shows its rule, and clears it in the editor (M5 slice 2)', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Debugger', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Debugger/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Debugger/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Debugger' });

  // A draft feature whose resource nothing spends or recovers.
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Dead Well');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(within(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ })).getByRole('textbox', { name: 'Resource name' }), 'Echoes');
  await user.click(within(editor()).getByRole('button', { name: 'Save draft' }));
  await expectStatus(/Saved a draft of E2E Dead Well/);
  await user.click(within(editor()).getByRole('button', { name: 'Close editor' }));

  await user.click(await screen.findByRole('button', { name: 'Find problems in E2E Debugger' }));
  const findings = await screen.findByRole('region', { name: 'Debugger findings for E2E Debugger' });
  expect(findings.textContent).toMatch(/Warning in E2E Dead Well: Resource 'resource-1' is never spent/);

  // "Show" opens the entry and moves focus to the rule the finding is about.
  await user.click(within(findings).getByRole('button', { name: 'Show rule resource-1 of E2E Dead Well' }));
  await screen.findByRole('heading', { name: 'Edit E2E Dead Well' });
  await waitFor(() => expect(document.activeElement).toBe(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ })));

  // With the entry already open, Show keeps its unsaved edits and moves focus again (review fix).
  const description = within(editor()).getByRole('textbox', { name: /^Description/ });
  await user.type(description, 'Unsaved words');
  await user.click(within(screen.getByRole('region', { name: 'Debugger findings for E2E Debugger' })).getByRole('button', { name: 'Show rule resource-1 of E2E Dead Well' }));
  await waitFor(() => expect(document.activeElement).toBe(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ })));
  expect((within(editor()).getByRole('textbox', { name: /^Description/ }) as HTMLTextAreaElement).value).toBe('Unsaved words');

  // A recovery fixes it; the editor's own debugger checks the unsaved revision.
  await user.click(within(editor()).getByRole('button', { name: 'Add recovery' }));
  await user.click(within(editor()).getByRole('button', { name: 'Find problems' }));
  const own = await within(editor()).findByRole('region', { name: 'Debugger findings' });
  await waitFor(() => expect(own.textContent).toMatch(/The debugger found no problems/));
});

it('tries a draft class at a chosen level on a blank character without saving anything (M5 slice 3)', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Sandbox', ['srd-5.2.1']);
  const charactersBefore = (await client.listCharacters()).length;
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Sandbox/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Sandbox/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Sandbox' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New class' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Trial Class');
  await user.selectOptions(within(editor()).getByRole('combobox', { name: 'Hit die' }), 'd10');

  const sandbox = within(editor()).getByRole('region', { name: 'Try it' });
  await user.type(within(sandbox).getByRole('textbox', { name: /^Level in this class/ }), '3');
  await user.click(within(sandbox).getByRole('button', { name: 'Try it' }));
  const results = await within(sandbox).findByRole('region', { name: 'Try it results' });
  expect(results.textContent).toMatch(/Unsaved blank character at total level 3/);
  expect(results.textContent).toMatch(/Hit point maximum: 22/); // d10: 10, then 6 per level, Con +0
  expect(results.textContent).not.toMatch(/Spell save DC/); // not a caster

  // An edit hides the result until it is tried again, and nothing was saved.
  await user.selectOptions(within(editor()).getByRole('combobox', { name: 'Hit die' }), 'd6');
  expect(within(sandbox).queryByRole('region', { name: 'Try it results' })).toBeNull();
  expect((await client.listCharacters()).length).toBe(charactersBefore);
  expect(await client.contentBySource((await client.listSources()).find((s) => s.title === 'E2E Sandbox')!.id)).toEqual([]);
});

it('compares the published revision with the unsaved one by rules, text and on a character copy (M5 slice 4)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Diffs', ['srd-5.2.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Diff Feat',
    rulesFamilies: ['srd-5.2.1'],
    provenance: { sourceId: source.id },
    status: 'draft',
    summary: 'Old line',
    effects: [{ type: 'modifier', id: 'quick', operation: 'bonus', target: 'initiative', value: '1' }],
  });
  const published = (await client.publish(draft)).published;
  const hero = await client.createCharacter({ name: 'E2E Diff Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [published] });

  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Diffs/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Diffs/ }));
  await user.click(await screen.findByRole('button', { name: 'Edit E2E Diff Feat' }));

  const editor = () => screen.getByRole('region', { name: /^Edit / });
  const description = within(editor()).getByRole('textbox', { name: /^Description/ });
  await user.clear(description);
  await user.type(description, 'New line');
  const value = within(within(editor()).getByRole('group', { name: /^Rule 1: Modifier/ })).getByRole('textbox', { name: /^Value/ });
  await user.clear(value);
  await user.type(value, '3');

  const panel = within(editor()).getByRole('region', { name: 'Compare revisions' });
  await user.click(within(panel).getByRole('checkbox', { name: 'E2E Diff Hero' }));
  await user.click(within(panel).getByRole('button', { name: 'Compare' }));
  const results = await within(panel).findByRole('region', { name: 'Comparison results' });
  expect(within(results).getByRole('table', { name: 'Rule changes' }).textContent).toMatch(/quick.*changed/);
  expect(results.textContent).toMatch(/removed: Old line/);
  expect(results.textContent).toMatch(/added: New line/);
  const run = within(results).getByRole('table', { name: 'Calculated values that change for E2E Diff Hero' });
  expect(run.textContent).toMatch(/Initiative\s*1\s*3/);

  // Nothing was applied: the character still uses the published revision.
  expect((await client.getCharacter(hero.character.id)).sheet.fields.find((f) => f.field === 'initiative')?.value).toBe(1);

  // A new entry has nothing stored to compare with until its first save; then the saved draft is "From" (review fix).
  await user.click(within(editor()).getByRole('button', { name: 'Close editor' }));
  await user.click(screen.getByRole('button', { name: 'New feat' }));
  const fresh = () => screen.getByRole('region', { name: /^New / });
  expect(within(fresh()).queryByRole('region', { name: 'Compare revisions' })).toBeNull();
  await user.type(within(fresh()).getByRole('textbox', { name: 'Name' }), 'E2E Fresh Feat');
  await user.click(within(fresh()).getByRole('button', { name: 'Save draft' }));
  await expectStatus(/Saved a draft of E2E Fresh Feat/);
  const freshPanel = await within(fresh()).findByRole('region', { name: 'Compare revisions' });
  await user.click(within(freshPanel).getByRole('button', { name: 'Compare' }));
  expect((await within(freshPanel).findByRole('region', { name: 'Comparison results' })).textContent).toMatch(/The rules are the same/);
});

it('shows an entry\'s relationships as a keyboard tree and jumps from a node to its rule (M5 slice 5)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Trees', ['srd-5.2.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feature',
    name: 'E2E Tree Feature',
    rulesFamilies: ['srd-5.2.1'],
    provenance: { sourceId: source.id },
    status: 'draft',
    effects: [
      { type: 'resource', id: 'embers', resourceId: 'embers', label: 'Embers', maximum: 'PB' },
      { type: 'roll', id: 'flare', rollId: 'flare', label: 'Flare', dice: '1d6', resourceId: 'embers', timing: 'onRoll', automation: 'assisted' },
      { type: 'recovery', id: 'rekindle', resourceId: 'embers', on: 'longRest', amount: 'all', timing: 'onLongRest' },
    ],
  });
  await client.publish(draft);

  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Trees/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Trees/ }));
  await user.click(await screen.findByRole('button', { name: 'Edit E2E Tree Feature' }));
  const editor = () => screen.getByRole('region', { name: /^Edit / });

  await user.click(within(editor()).getByRole('button', { name: 'Show relationships' }));
  const tree = await within(editor()).findByRole('tree', { name: 'Relationships of E2E Tree Feature' });
  const root = within(tree).getAllByRole('treeitem')[0]!;
  expect(root.getAttribute('aria-level')).toBe('1');
  expect(root.getAttribute('aria-expanded')).toBe('true');
  expect(root.tabIndex).toBe(0);

  // Keyboard only, from the button: Tab enters the tree at its one tab stop (the root).
  await user.tab();
  expect(document.activeElement).toBe(root);
  await user.keyboard('{ArrowDown}');
  const resource = within(tree).getByRole('treeitem', { name: 'Resource: Embers (uses PB)' }); // named by its own label only
  expect(document.activeElement).toBe(resource);
  expect([resource.getAttribute('aria-level'), resource.getAttribute('aria-posinset'), resource.getAttribute('aria-setsize')]).toEqual(['2', '1', '1']);
  expect(resource.getAttribute('aria-expanded')).toBe('false');
  // Space opens and closes; Right opens, then goes to the first child.
  await user.keyboard(' ');
  expect(resource.getAttribute('aria-expanded')).toBe('true');
  await user.keyboard(' ');
  expect(resource.getAttribute('aria-expanded')).toBe('false');
  await user.keyboard('{ArrowRight}');
  expect(resource.getAttribute('aria-expanded')).toBe('true');
  await user.keyboard('{ArrowRight}');
  const roll = within(resource).getByRole('treeitem', { name: 'Roll: Flare (1d6), spends it' });
  expect((document.activeElement as HTMLElement).id).toBe(roll.id);
  expect([roll.getAttribute('aria-level'), roll.getAttribute('aria-posinset'), roll.getAttribute('aria-setsize')]).toEqual(['3', '1', '2']);
  // End goes to the last visible item (the recovery), Home to the root, Up and Down between items.
  await user.keyboard('{End}');
  expect(document.activeElement).toBe(within(resource).getByRole('treeitem', { name: 'Recovery on a long rest: all' }));
  await user.keyboard('{Home}');
  expect(document.activeElement).toBe(root);
  await user.keyboard('{ArrowDown}{ArrowDown}');
  expect((document.activeElement as HTMLElement).id).toBe(roll.id);
  await user.keyboard('{ArrowUp}');
  expect(document.activeElement).toBe(resource);
  await user.keyboard('{ArrowDown}');
  // Enter shows the rule in the editor; Left from a child goes back to its parent.
  await user.keyboard('{Enter}');
  await waitFor(() => expect(document.activeElement).toBe(within(editor()).getByRole('group', { name: /^Rule 2: Roll or action/ })));
  await user.click(within(tree).getByRole('treeitem', { name: 'Roll: Flare (1d6), spends it' }));
  await user.keyboard('{ArrowLeft}');
  expect(document.activeElement).toBe(resource);
});

it('starts homebrew from a template as an unsaved draft, publishes a stance, and a skeleton waits for its slots (M5 slice 6)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Templates', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Templates/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Templates/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Templates' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });

  // The stance: a toggle that spends a use, and a bonus only while it is on. Nothing is stored until it is saved.
  await user.selectOptions(screen.getByRole('combobox', { name: 'Start from a template' }), 'A stance you switch on and off');
  await user.click(screen.getByRole('button', { name: 'Use template' }));
  expect(await client.contentBySource(source.id)).toEqual([]);
  expect(within(editor()).getByRole('group', { name: /^Rule 3: Toggle/ })).toBeTruthy();
  expect((within(within(editor()).getByRole('group', { name: /^Rule 4: Modifier/ })).getByRole('combobox', { name: 'Applies' }) as HTMLSelectElement).value).toBe('stance');
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Stance');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await expectStatus(/Published E2E Stance/);

  // The class skeleton: its hit die, saves and subclass choice are set; its improvement slots block publishing until filled.
  await user.selectOptions(screen.getByRole('combobox', { name: 'Start from a template' }), 'A class skeleton');
  await user.click(screen.getByRole('button', { name: 'Use template' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Skeleton Class');
  expect((within(editor()).getByRole('combobox', { name: 'Hit die' }) as HTMLSelectElement).value).toBe('8');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  const results = await within(editor()).findByRole('region', { name: 'Check results' });
  expect(results.textContent).toMatch(/improvement-4' grants content but names none/);
  await user.click(within(editor()).getByRole('button', { name: 'Save draft' }));
  await expectStatus(/Saved a draft of E2E Skeleton Class/);
  expect((await client.contentBySource(source.id)).find((e) => e.name === 'E2E Skeleton Class')?.latest.status).toBe('draft');

  // Every template passes the server's checks; only the skeletons' empty slots are errors (review fix).
  for (const t of templates) {
    const draft = { ...fromTemplate(t.id, source.id, ['srd-5.2.1'], crypto.randomUUID()), name: `E2E ${t.label}`, revisionId: crypto.randomUUID() };
    expect(draft.summary).toBeUndefined(); // a template never writes the description shown on the sheet
    const report = await client.validateRevision(draft);
    expect(report.errors.filter((e) => e.code !== 'validate.grant-content-missing')).toEqual([]);
    expect(report.errors.length > 0).toBe(t.id === 'subclass-skeleton' || t.id === 'class-skeleton');
  }

  // Removing the stance's toggle turns its bonus back to always on, so nothing names a missing toggle (review fix).
  await user.selectOptions(screen.getByRole('combobox', { name: 'Start from a template' }), 'A stance you switch on and off');
  await user.click(screen.getByRole('button', { name: 'Use template' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Loose Stance');
  await user.click(within(within(editor()).getByRole('group', { name: /^Rule 3: Toggle/ })).getByRole('button', { name: 'Remove rule 3' }));
  expect(within(editor()).queryByRole('combobox', { name: 'Applies' })).toBeNull();
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  await waitFor(() => expect(within(editor()).getByRole('region', { name: 'Check results' }).textContent).toMatch(/No problems found/));
});

it('shows design feedback only once it is switched on, as hints that do not block (M5 slice 7)', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Feedback', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Feedback/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Feedback/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Feedback' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });

  // Off by default: the editor has no feedback section.
  const toggle = screen.getByRole('checkbox', { name: 'Show design feedback' });
  expect((toggle as HTMLInputElement).checked).toBe(false);
  await user.click(screen.getByRole('button', { name: 'New class' }));
  expect(within(editor()).queryByRole('region', { name: 'Design feedback' })).toBeNull();

  try {
    await user.click(toggle);
    expect(localStorage.getItem('tomestack.designFeedback')).toBe('on');
    const section = within(editor()).getByRole('region', { name: 'Design feedback' });
    await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Hinted Class');
    await user.click(within(section).getByRole('button', { name: 'Get design hints' }));
    const hints = await within(section).findByRole('region', { name: 'Design hints' });
    expect(hints.textContent).toMatch(/Class level\(s\) .* give no feature or choice, although every bundled SRD class gives one there/);

    // The choice is remembered for next time (this machine only): a fresh studio starts with it on.
    cleanup();
    render(<App />);
    const again = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
    await waitFor(() => expect(again.disabled).toBe(false));
    await user.click(again);
    expect((await screen.findByRole<HTMLInputElement>('checkbox', { name: 'Show design feedback' })).checked).toBe(true);

    // Off again: the choice is forgotten.
    await user.click(screen.getByRole('checkbox', { name: 'Show design feedback' }));
    expect(localStorage.getItem('tomestack.designFeedback')).toBeNull();
  } finally {
    localStorage.removeItem('tomestack.designFeedback'); // later flows start from the default, off
  }
});

it('takes a snapshot of a character, previews the restore, restores it and keeps an undo snapshot (M5 slice 8)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Snapshots', ['srd-5.2.1']);
  const feat = (
    await client.publish(
      await client.saveDraft({
        contentId: crypto.randomUUID(),
        revisionId: '00000000-0000-0000-0000-000000000000',
        kind: 'feat',
        name: 'E2E Snapshot Feat',
        rulesFamilies: ['srd-5.2.1'],
        provenance: { sourceId: source.id },
        status: 'draft',
        effects: [{ type: 'modifier', id: 'quick', operation: 'bonus', target: 'initiative', value: '3' }],
      }),
    )
  ).published;
  const created = (await client.createCharacter({ name: 'E2E Snapshot Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [feat] })).character;
  const hero = (await client.saveCharacter({ ...created, currency: { cp: 0, sp: 12, ep: 0, gp: 3, pp: 0 } })).character; // D21: coins the restore puts back

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Snapshot Hero/ }));
  await openTab(user, await screen.findByRole('article', { name: 'E2E Snapshot Hero' }), 'Manage');
  const panel = () => screen.getByRole('region', { name: 'Snapshots' });
  await user.type(await within(await screen.findByRole('region', { name: 'Snapshots' })).findByRole('textbox', { name: /^Snapshot name/ }), 'E2E with the feat');
  await user.click(within(panel()).getByRole('button', { name: 'Take snapshot' }));
  await expectStatus(/Took a snapshot of E2E Snapshot Hero: E2E with the feat/);

  // The character changes: the feat goes, and the coins change.
  await client.saveCharacter({ ...(await client.getCharacter(hero.id)).character, pins: [], currency: { cp: 0, sp: 0, ep: 0, gp: 40, pp: 0 } });
  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Snapshot Hero/ }));

  // ADR-014: the sheet reopens on the tab used last for this character (kept in the page's own storage).
  expect(within(await screen.findByRole('article', { name: 'E2E Snapshot Hero' })).getByRole('tab', { name: 'Manage' }).getAttribute('aria-selected')).toBe('true');
  await user.click(await within(await screen.findByRole('region', { name: 'Snapshots' })).findByRole('button', { name: 'Restore E2E with the feat…' }));
  const preview = await within(panel()).findByRole('region', { name: 'Restore E2E with the feat?' });
  await waitFor(() => expect(document.activeElement).toBe(within(preview).getByRole('heading', { name: 'Restore E2E with the feat?' })));
  expect(within(preview).getByRole('table', { name: 'Calculated values that change' }).textContent).toMatch(/Initiative\s*0\s*3/);
  // D22: the restore preview names the coin change, through the service's real field names.
  expect(preview.textContent).toMatch(/Coins go back to 3 gp, 12 sp \(now 40 gp\)\./);
  expect((await client.getCharacter(hero.id)).character.pins).toEqual([]); // the preview changed nothing

  // "Keep the current state" closes the preview and returns focus to the button that opened it (accessibility item 23).
  await user.click(within(preview).getByRole('button', { name: 'Keep the current state' }));
  const reopen = within(panel()).getByRole('button', { name: 'Restore E2E with the feat…' });
  await waitFor(() => expect(document.activeElement).toBe(reopen));
  expect(within(panel()).queryByRole('region', { name: 'Restore E2E with the feat?' })).toBeNull();
  expect((await client.getCharacter(hero.id)).character.pins).toEqual([]);

  await user.click(reopen);
  const again = await within(panel()).findByRole('region', { name: 'Restore E2E with the feat?' });
  await user.click(within(again).getByRole('button', { name: 'Restore' }));
  await expectStatus(/Restored the snapshot/);
  expect((await client.getCharacter(hero.id)).character.pins).toEqual([feat]);
  expect((await client.getCharacter(hero.id)).character.currency).toEqual({ cp: 0, sp: 12, ep: 0, gp: 3, pp: 0 });
  expect(await within(panel()).findByRole('button', { name: /^Restore Before restoring/ })).toBeTruthy(); // the undo snapshot
});

it('drops picks that do not fit when the rules family changes, in the builder and in a campaign', async () => {
  const user = userEvent.setup();
  render(<App />);
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));

  // Builder: an SRD 5.1 species, then SRD 5.2.1. The 5.1 pick is cleared and no longer listed.
  await user.click(newCharacter);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Switcher'); // the name is required to leave the Rules step
  await user.click(screen.getByRole('radio', { name: /SRD 5\.1/ }));
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  const species = screen.getByRole('group', { name: 'Species' });
  const halfOrc = () => within(species).getByRole<HTMLInputElement>('radio', { name: /^Half-Orc/ });
  await waitFor(() => expect(halfOrc().disabled).toBe(false));
  await user.click(halfOrc());
  expect(halfOrc().checked).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Back' }));
  await user.click(screen.getByRole('button', { name: 'Back' }));
  await user.click(screen.getByRole('radio', { name: /SRD 5\.2\.1/ }));
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  // D31: the 5.1 species leaves the list altogether, and the pick is cleared.
  await waitFor(() => expect(within(screen.getByRole('group', { name: 'Species' })).queryByRole('radio', { name: /^Half-Orc/ })).toBeNull());
  expect(within(screen.getByRole('group', { name: 'Species' })).getByRole<HTMLInputElement>('radio', { name: 'None' }).checked).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Cancel' }));

  // Campaign: an SRD 5.1 source, then SRD 5.2.1. The hidden 5.1 source is not saved with the campaign.
  await user.click(screen.getByRole('button', { name: 'Campaigns' }));
  await user.click(await screen.findByRole('button', { name: 'New campaign' }));
  const form = screen.getByRole('form', { name: 'Campaign' });
  await user.type(within(form).getByRole('textbox', { name: 'Campaign name' }), 'E2E Switched');
  await user.click(within(form).getByRole('radio', { name: /SRD 5\.1/ }));
  await user.click(within(form).getByRole('checkbox', { name: /^System Reference Document 5\.1/ }));
  await user.click(within(form).getByRole('radio', { name: /SRD 5\.2\.1/ }));
  await user.click(within(form).getByRole('button', { name: 'Save campaign' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Saved the campaign E2E Switched.'));
  const saved = screen.getAllByRole('listitem').find((li) => li.textContent?.startsWith('E2E Switched'))!;
  expect(saved.textContent).toMatch(/0 allowed sources/);
});

it('reaches the primary actions by keyboard alone', async () => {
  const user = userEvent.setup();
  render(<App />);
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));

  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Hide sidebar' })); // the shell's one header control comes first
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Close sidebar' })); // the sidebar's own close control
  // Owner (2026-10-06): the character list comes before the tools. Earlier tests leave characters behind in the shared
  // DevHost, so the next stop is the first of them, or "Characters" (the first tool) when the list is empty; the bounded
  // loop walks past "Characters" to "New character".
  await user.tab();
  const list = within(screen.getByRole('navigation', { name: 'Characters' })).getByRole('list', { name: 'Characters' });
  const charactersTool = within(screen.getByRole('navigation', { name: 'Characters' })).getByRole('button', { name: 'Characters' });
  expect(list.contains(document.activeElement) || document.activeElement === charactersTool).toBe(true);
  for (let i = 0; i < 200 && document.activeElement !== newCharacter; i++) await user.tab();
  expect(document.activeElement).toBe(newCharacter);
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Import package…' }));

  await user.keyboard('{Shift>}{Tab}{/Shift}{Enter}');
  expect(await screen.findByRole('heading', { name: 'New character' })).toBeTruthy();
  expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Name' }));
});

it('equips a weapon: the attack uses finesse and proficiency, rolls, and actions are grouped', async () => {
  // SPEC C-02, C-04 with the original fixtures "Fixture Duelist" (simple and martial weapons) and "Fixture Needle" (finesse).
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Blade', scores: { Strength: 14, Dexterity: 16 }, cls: /^Fixture Duelist/ });
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Acrobatics/);
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Insight/);
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const blade = await screen.findByRole('article', { name: 'E2E Blade' });

  await openTab(user, blade, 'Inventory');
  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^Fixture Needle/ })).toBeTruthy());
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Needle/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Needle' }));

  // Dex +3 (finesse beats Str +2) + PB 2 = +5; damage 1d4 + 3.
  await openTab(user, blade, 'Play');
  const actions = () => screen.getByRole('region', { name: 'Attacks and actions' });
  await waitFor(() => expect(within(actions()).getByText(/\+5 to hit, 1d4\+3 piercing/)).toBeTruthy());
  await user.click(within(actions()).getByRole('button', { name: 'Roll Fixture Needle attack' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Fixture Needle attack: \d+ \(1d20\)/));
  expect(lastRoll.textContent).toMatch(/Fixture Needle to hit \+5/);
  const reactions = within(actions()).getByRole('region', { name: 'Reactions' });
  expect(within(reactions).getByRole('button', { name: 'Roll Riposte damage (1d6)' })).toBeTruthy();
});

it('switches a toggled effect on and off, spends a chosen amount, and a long rest proposes the toggle off', async () => {
  // M3 B2 (content v6) with the original fixture feat "Fixture Radiant Stance": a stance (+2 AC, spends 1 radiance) and a
  // variable-cost surge. PB 2 at level 1, so 2 radiance.
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Stance', scores: {}, cls: /^Fixture Duelist/, other: /^Fixture Radiant Stance/ });
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Acrobatics/);
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Insight/);
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const stance = await screen.findByRole('article', { name: 'E2E Stance' });

  const armorClass = () => Number(/^(\d+)/.exec(summaryValue(stance, 'Armor Class'))![1]);
  const before = armorClass();
  const actions = () => screen.getByRole('region', { name: 'Attacks and actions' });
  await user.click(within(actions()).getByRole('checkbox', { name: /^Radiant stance/ }));
  await waitFor(() => expect(armorClass()).toBe(before + 2));
  const resources = () => screen.getByRole('region', { name: 'Resources' });
  expect(within(resources()).getByRole('heading', { name: 'Radiance: 1 of 2' })).toBeTruthy();

  // The surge spends a chosen amount (1 to the cost or what is left): here only 1 is left.
  await user.click(within(actions()).getByRole('button', { name: 'Roll Radiant surge (1d6)' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  await user.type(await within(lastRoll).findByRole('spinbutton', { name: 'Radiance to spend (1 to 1)' }), '1');
  await user.click(within(lastRoll).getByRole('button', { name: /^Spend 1 Radiance/ }));
  await waitFor(() => expect(within(resources()).getByRole('heading', { name: 'Radiance: 0 of 2' })).toBeTruthy());

  // The long rest proposes switching it off (and restores radiance).
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const rest = await screen.findByRole('region', { name: 'Long rest' });
  expect(await within(rest).findByRole('checkbox', { name: /^Radiant stance: 1 → 0/ })).toBeTruthy();
  await user.click(within(rest).getByRole('button', { name: 'Finish long rest' }));
  await waitFor(() => expect(armorClass()).toBe(before));
  expect(within(actions()).getByRole<HTMLInputElement>('checkbox', { name: /^Radiant stance/ }).checked).toBe(false);
});

it('records a gap note on a field and a feature, resolves one, and deletes one after confirming', async () => {
  // M3 B3: session feedback, stored locally and never changing the character.
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Gaps', family: 'srd-5.1', scores: {}, species: /^Fixture Quickfoot/ });
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Gaps' });
  const armorClass = summaryValue(sheet, 'Armor Class');
  await openTab(user, sheet, 'Stats');
  const armorClassHeading = screen.getByRole('heading', { name: /^Armor Class:/ }).textContent;

  await openTab(user, sheet, 'Notes');
  const gaps = () => screen.getByRole('region', { name: /^Gap notes/ });
  expect(await within(gaps()).findByText('No gap notes yet.')).toBeTruthy();
  const form = within(gaps()).getByRole('form', { name: 'New gap note' });
  const about = within(form).getByRole('combobox', { name: /^About/ });
  const text = within(form).getByRole('textbox', { name: /^What was missing or wrong/ });

  await user.selectOptions(about, within(about).getByRole('option', { name: 'Armor Class' }));
  await user.type(text, 'The table grants a cover bonus here.');
  await user.click(within(form).getByRole('button', { name: 'Save note' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Gap note saved for Armor Class.'));
  expect(within(gaps()).getByText('The table grants a cover bonus here.')).toBeTruthy();

  // M3 C5: "Report a gap" on a feature pre-fills the picker and moves focus to the note's text.
  await openTab(user, sheet, 'Features');
  const features = within(screen.getByRole('article', { name: 'E2E Gaps' })).getByRole('region', { name: 'Features' });
  await user.click(within(features).getByRole('button', { name: 'Report a gap: Fixture Quickfoot' }));
  expect((about as HTMLSelectElement).selectedOptions[0]!.textContent).toBe('Fixture Quickfoot');
  expect(document.activeElement).toBe(text);
  expect(within(sheet).getByRole('tab', { name: 'Notes' }).getAttribute('aria-selected')).toBe('true');
  await user.type(text, 'Speed bonus should apply while unarmored only.');
  await user.click(within(form).getByRole('button', { name: 'Save note' }));
  await waitFor(() => expect(within(gaps()).getByRole('heading').textContent).toBe('Gap notes: 2 open'));

  await user.click(within(gaps()).getByRole('button', { name: 'Mark resolved: Armor Class' }));
  await waitFor(() => expect(within(gaps()).getByRole('heading').textContent).toBe('Gap notes: 1 open'));
  expect(within(gaps()).getByRole('button', { name: 'Reopen: Armor Class' })).toBeTruthy();

  // "Report a gap" on a field works the same way (the field's details hold the button).
  await openTab(user, sheet, 'Stats');
  const armor = within(screen.getByRole('article', { name: 'E2E Gaps' })).getByRole('region', { name: /^Armor Class:/ });
  await user.click(within(armor).getByRole('heading'));
  await user.click(within(armor).getByRole('button', { name: 'Report a gap: Armor Class' }));
  expect((about as HTMLSelectElement).selectedOptions[0]!.textContent).toBe('Armor Class');
  expect(document.activeElement).toBe(text);
  // "Report a gap" left Notes as the remembered tab; move the memory off it so the deep link below is what selects Notes.
  await openTab(user, sheet, 'Play');

  // M3 C5: the list across characters shows the open note with its character; resolved ones only on request.
  await user.click(screen.getByRole('button', { name: 'Gap notes' }));
  const all = await screen.findByRole('region', { name: /^Gap notes of all characters/ });
  const allList = await within(all).findByRole('list', { name: 'Gap notes of all characters' });
  const mine = () => within(allList).getAllByRole('listitem').filter((li: HTMLElement) => li.textContent!.startsWith('E2E Gaps'));
  expect(mine().map((li: HTMLElement) => li.textContent)).toEqual([expect.stringContaining('Speed bonus should apply while unarmored only.')]);
  await user.click(within(all).getByRole('checkbox', { name: 'Show resolved notes' }));
  await waitFor(() => expect(mine()).toHaveLength(2));
  await user.click(within(all).getAllByRole('button', { name: 'Open E2E Gaps' })[0]!);
  await screen.findByRole('article', { name: 'E2E Gaps' });
  // "Open <character>" from the Gap notes screen opens the sheet on Notes.
  expect(within(screen.getByRole('article', { name: 'E2E Gaps' })).getByRole('tab', { name: 'Notes' }).getAttribute('aria-selected')).toBe('true');
  // The reopened sheet loads its notes again; wait for them before using them.
  await within(gaps()).findByText('Speed bonus should apply while unarmored only.');

  // Deleting asks first; "Keep note" leaves it.
  const quickfootNote = () => within(gaps()).getByText('Speed bonus should apply while unarmored only.').closest('li')!;
  await user.click(within(quickfootNote()).getByRole('button', { name: 'Delete…' }));
  await user.click(within(quickfootNote()).getByRole('button', { name: 'Keep note' }));
  await user.click(within(quickfootNote()).getByRole('button', { name: 'Delete…' }));
  await user.click(within(quickfootNote()).getByRole('button', { name: 'Delete note' }));
  await waitFor(() => expect(within(gaps()).queryByText('Speed bonus should apply while unarmored only.')).toBeNull());
  expect(within(gaps()).getByRole('heading').textContent).toBe('Gap notes: 0 open');

  // Notes never change the character's sheet.
  expect(summaryValue(screen.getByRole('article', { name: 'E2E Gaps' }), 'Armor Class')).toBe(armorClass);
  await openTab(user, screen.getByRole('article', { name: 'E2E Gaps' }), 'Stats');
  expect(screen.getByRole('heading', { name: /^Armor Class:/ }).textContent).toBe(armorClassHeading);
});

it('prints a sheet with its license notices, and gap notes only when ticked', async () => {
  // M3 C4: the printable backup. The print dialog is the browser's; here window.print is a spy.
  const print = vi.spyOn(window, 'print').mockImplementation(() => {});
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Print', family: 'srd-5.1', scores: {}, species: /^Fixture Quickfoot/ });
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Print' });

  await openTab(user, sheet, 'Notes');
  const gaps = within(sheet).getByRole('region', { name: /^Gap notes/ });
  const form = within(gaps).getByRole('form', { name: 'New gap note' });
  await user.selectOptions(within(form).getByRole('combobox', { name: /^About/ }), 'Armor Class');
  await user.type(within(form).getByRole('textbox', { name: /^What was missing or wrong/ }), 'Private note for the print test.');
  await user.click(within(form).getByRole('button', { name: 'Save note' }));
  await within(gaps).findByText('Private note for the print test.');
  const journal = within(sheet).getByRole('form', { name: 'New session note' });
  await user.type(within(journal).getByRole('textbox', { name: 'Note' }), 'Private session note for the print test.');
  await user.click(within(journal).getByRole('button', { name: 'Save session note' }));
  await within(sheet).findByText('Private session note for the print test.');

  await user.click(within(sheet).getByRole('button', { name: 'Print…' }));
  const preview = await within(sheet).findByRole('region', { name: 'Print preview' });
  expect(document.activeElement).toBe(within(preview).getByRole('heading', { name: 'Print character' })); // focus moves to the preview on open
  expect(within(preview).getByRole('heading', { name: 'E2E Print' })).toBeTruthy();
  expect(within(preview).getByRole('table', { name: 'Abilities' })).toBeTruthy();
  expect(within(preview).getByText(/^Fixture Quickfoot/)).toBeTruthy();
  // The license notices of the sources used (the fixture sources here), and the version it was printed from.
  await waitFor(() => expect(preview.querySelectorAll('.print-notice').length).toBeGreaterThan(0));
  await waitFor(() => expect(preview.textContent).toMatch(/Printed from TomeStack \d+\.\d+\.\d+/));
  // Gap notes are private: left out until ticked. No path ever appears.
  expect(preview.textContent).not.toContain('Private note for the print test.');
  expect(preview.textContent).not.toMatch(/[A-Za-z]:\\|\/Users\/|\\Users\\/);
  await user.click(within(preview).getByRole('checkbox', { name: /^Include gap notes/ }));
  expect(await within(preview).findByText(/Private note for the print test\./)).toBeTruthy();
  // Session notes are private too: absent until their own box is ticked.
  expect(preview.textContent).not.toContain('Private session note for the print test.');
  await user.click(within(preview).getByRole('checkbox', { name: 'Include session notes (private)' }));
  expect(await within(preview).findByText(/Private session note for the print test\./)).toBeTruthy();

  await user.click(within(preview).getByRole('button', { name: 'Print…' }));
  expect(print).toHaveBeenCalledTimes(1);
  await user.click(within(preview).getByRole('button', { name: 'Close print preview' }));
  expect(within(sheet).queryByRole('region', { name: 'Print preview' })).toBeNull();
  expect(document.activeElement).toBe(within(sheet).getByRole('button', { name: 'Print…' }));
  print.mockRestore();
});

it('builds a spellcaster: picks spells in the builder, casts one, rolls a spell attack and a long rest restores the slot', async () => {
  // D04 (M2 spellcasting) with the original fixture caster "Fixture Arcanist" (invented tables: 2 level 1 slots at level 1).
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Sage', family: 'srd-5.2.1', scores: { Intelligence: 16 }, cls: /^Fixture Arcanist/ });

  // Int 16 (+3) at level 1: 3 cantrips, max(1, 3 + 1) = 4 prepared. Only spells on the caster's list and castable levels.
  let picker = await screen.findByRole('group', { name: /^Fixture Arcanist spells \(0 of 3 cantrips, 0 of 4 prepared spells\)/ });
  expect(within(picker).queryByRole('checkbox', { name: /Fixture Mending Word/ })).toBeNull(); // another list
  expect(within(picker).queryByRole('checkbox', { name: /Fixture Ember Wave/ })).toBeNull(); // level 3: no slot yet
  // D30: the search hides what does not match and says how many are shown; clearing it brings everything back.
  const search = within(picker).getByRole('searchbox', { name: 'Search Fixture Arcanist spells by name' });
  await user.type(search, 'frost');
  expect(within(picker).getByText(/^1 of \d+ spells shown$/)).toBeTruthy();
  expect(within(picker).queryByRole('checkbox', { name: /^Fixture Spark/ })).toBeNull();
  await user.clear(search);
  await user.click(within(picker).getByRole('checkbox', { name: /^Fixture Spark/ }));
  picker = await screen.findByRole('group', { name: /^Fixture Arcanist spells \(1 of 3 cantrips/ });
  // R23: a chosen spell the search hides is named in the status, and the legend's count still includes it.
  const search2 = within(picker).getByRole('searchbox', { name: 'Search Fixture Arcanist spells by name' });
  await user.type(search2, 'frost');
  expect(within(picker).getByText(/^1 of \d+ spells shown, 1 chosen hidden by the search$/)).toBeTruthy();
  expect(within(picker).queryByRole('checkbox', { name: /^Fixture Spark/ })).toBeNull();
  await screen.findByRole('group', { name: /^Fixture Arcanist spells \(1 of 3 cantrips/ });
  await user.type(search2, 'zz');
  expect(within(picker).getByText(/No spells match “frostzz”/)).toBeTruthy();
  await user.clear(search2);
  expect(within(picker).getByRole('checkbox', { name: /^Fixture Spark/ })).toBeTruthy();
  await user.click(within(picker).getByRole('checkbox', { name: /^Fixture Frost Ring/ }));
  await screen.findByRole('group', { name: /^Fixture Arcanist spells \(1 of 3 cantrips, 1 of 4 prepared spells\)/ });
  await user.click(screen.getByRole('button', { name: 'Create and save' }));

  const sheet = await screen.findByRole('article', { name: 'E2E Sage' });
  await openTab(user, sheet, 'Spells');
  const spells = () => within(screen.getByRole('article', { name: 'E2E Sage' })).getByRole('region', { name: 'Spells and slots' });
  expect(within(spells()).getByRole('heading', { name: 'Fixture Arcanist (level 1, Intelligence): spell attack +5, save DC 13' })).toBeTruthy();
  expect(within(spells()).getByRole('heading', { name: 'Level 1 slots: 2 of 2' })).toBeTruthy();
  expect(within(sheet).getByRole('heading', { name: /^Spell attack bonus: \+5/ })).toBeTruthy();

  // Concentration (D19) is not driven here: the fixture pack's only concentration spell (Fixture Veil) is level 2, which this
  // level 1 character cannot prepare. The panel and its service rules are covered by ConcentrationPanel.test.tsx and PlayCommandTests.
  // Casting spends a slot (a confirmed play change); rolling a spell spends nothing.
  await user.click(within(spells()).getByRole('button', { name: 'Cast Fixture Frost Ring (spend a slot)' }));
  await waitFor(() => expect(within(spells()).getByRole('heading', { name: 'Level 1 slots: 1 of 2' })).toBeTruthy());
  await user.click(within(spells()).getByRole('button', { name: 'Roll Fixture Spark attack' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Fixture Spark \(spell attack\): \d+ \(1d20\)/));
  expect(lastRoll.textContent).toMatch(/Fixture Arcanist spell attack \+5/);
  expect(within(spells()).getByRole('heading', { name: 'Level 1 slots: 1 of 2' })).toBeTruthy();

  // The long rest proposes the slot back.
  await openTab(user, sheet, 'Play');
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const rest = await screen.findByRole('region', { name: 'Long rest' });
  expect(await within(rest).findByRole('checkbox', { name: /^Level 1 spell slots: 1 → 2/ })).toBeTruthy();
  await user.click(within(rest).getByRole('button', { name: 'Finish long rest' }));
  await openTab(user, sheet, 'Spells');
  await waitFor(() => expect(within(spells()).getByRole('heading', { name: 'Level 1 slots: 2 of 2' })).toBeTruthy());
});

it('the Backups screen says what a full backup holds, and needs the desktop app to write or restore one (M2.1)', async () => {
  const user = userEvent.setup();
  render(<App />);

  await user.click(await screen.findByRole('button', { name: 'Backups' }));
  const panel = await screen.findByRole('region', { name: 'Backups' });
  // Earlier flows in this run created characters, homebrew drafts, a campaign and a PDF copy: the counts come from the service.
  const contents = await within(panel).findByRole('list', { name: 'What the backup contains' });
  expect(contents.textContent).toMatch(/\d+ character\(s\), \d+ campaign\(s\), \d+ gap note\(s\)/);
  expect(contents.textContent).toMatch(/\d+ published and [1-9]\d* draft entries/);
  expect(contents.textContent).toMatch(/Not included: text read from PDFs/);

  // DevHost has no native Save or Open dialog, so both say what they need instead of doing nothing.
  await user.click(within(panel).getByRole('button', { name: 'Back up everything…' }));
  expect((await screen.findByRole('alert')).textContent).toMatch(/Backing up everything needs the TomeStack desktop app/);
  await user.click(within(panel).getByRole('button', { name: 'Choose a full backup…' }));
  await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/Restoring a full backup needs the TomeStack desktop app/));
});

const fighter = (n: number) => srd(408000 + n);

it('adds a homebrew Fighter subclass through the studio and plays it: the Stardust Guardian path with original content (M2.2)', async () => {
  const user = userEvent.setup();
  // An SRD 5.2.1 Fighter 3 with the Defense style and no subclass yet (the builder flows cover building one).
  await client.createCharacter({
    name: 'E2E Warden',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 16, dex: 14, con: 14, int: 10, wis: 12, cha: 8 },
    pins: [srd(1), srd(2)],
    classes: [{ class: fighter(0), level: 3 }],
    choices: [
      { source: srd(2), choiceId: 'soldier-ability-scores', selected: [srd(4)] },
      { source: fighter(0), choiceId: 'fighter-skills', selected: [fighter(800), fighter(802)] },
      { source: fighter(1), choiceId: 'fighting-style', selected: [fighter(601)] },
    ],
  });
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);

  const newSource = await screen.findByRole('form', { name: 'New homebrew source' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Source title' }), 'E2E Warden Homebrew');
  await user.click(within(newSource).getByRole('checkbox', { name: 'SRD 5.2.1 (2024 rules)' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create source' }));
  await screen.findByRole('heading', { name: 'Content in E2E Warden Homebrew' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(editor()).getByRole('group', { name });
  async function publish(name: string) {
    await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe(`Published ${name}.`));
  }

  // A limited-use feature: a resource, its recovery and a roll that spends it.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Star Shield');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(within(rule(/^Rule 1: Resource/)).getByRole('textbox', { name: 'Resource name' }), 'Star charges');
  await user.click(within(editor()).getByRole('button', { name: 'Add recovery' }));
  await user.click(within(editor()).getByRole('button', { name: 'Add roll or action' }));
  const action = rule(/^Rule 3: Roll or action/);
  await user.type(within(action).getByRole('textbox', { name: 'Roll name' }), 'Star burst');
  await user.clear(within(action).getByRole('textbox', { name: /^Dice/ }));
  await user.type(within(action).getByRole('textbox', { name: /^Dice/ }), '2d6');
  await user.selectOptions(within(action).getByRole('combobox', { name: /Uses a resource/ }), 'Star charges');
  await publish('E2E Star Shield');

  // The subclass joins the SRD Fighter's level-3 choice, with a modifier and the feature.
  await user.click(screen.getByRole('button', { name: 'New subclass' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Starward Warden');
  const offered = within(editor()).getByRole('combobox', { name: 'Offered in the choice' });
  await waitFor(() => expect(within(offered).getByRole('option', { name: /^Fighter: Level 3: Fighter Subclass/ })).toBeTruthy());
  await user.selectOptions(offered, within(offered).getByRole('option', { name: /^Fighter: Level 3: Fighter Subclass/ }));
  await user.click(within(editor()).getByRole('button', { name: 'Add modifier' })); // default: Initiative +1
  await user.click(within(editor()).getByRole('button', { name: 'Grant a feature' }));
  await user.selectOptions(within(rule(/^Rule 2: Granted feature/)).getByRole('combobox', { name: 'Feature' }), 'E2E Star Shield (published)');
  await publish('E2E Starward Warden');

  // Choose it on the sheet, next to the SRD Champion, then play.
  await user.click(screen.getByRole('button', { name: /^E2E Warden/ }));
  let sheet = await screen.findByRole('article', { name: 'E2E Warden' });
  await user.click(within(sheet).getByRole('button', { name: 'Make choices' }));
  const subclass = await screen.findByRole('group', { name: /^Fighter: choose 1/ });
  expect(await within(subclass).findByRole('checkbox', { name: /^Champion/ })).toBeTruthy();
  await pick(user, /^Fighter: choose 1/, /^E2E Starward Warden/);
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));

  sheet = await screen.findByRole('article', { name: 'E2E Warden' });
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^Initiative: \+3/ })).toBeTruthy(); // Dex +2, homebrew +1
  expect(within(sheet).getByRole('heading', { name: /^Attacks per Attack action: 1/ })).toBeTruthy(); // Extra Attack comes at 5
  await openTab(user, sheet, 'Play');
  const resources = () => within(screen.getByRole('article', { name: 'E2E Warden' })).getByRole('region', { name: 'Resources' });
  expect(within(resources()).getByRole('heading', { name: 'Star charges: 2 of 2' })).toBeTruthy();
  expect(within(resources()).getByRole('heading', { name: 'Second Wind: 2 of 2' })).toBeTruthy();
  expect(within(resources()).getByRole('heading', { name: 'Action Surge: 1 of 1' })).toBeTruthy();

  // Second Wind rolls 1d10 plus the Fighter level; spending it is a separate, confirmed press.
  await user.click(within(sheet).getByRole('button', { name: 'Roll Second Wind healing (1d10 + 3)' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Second Wind healing bonus \+3/));
  expect(lastRoll.textContent).not.toMatch(/CLASS_LEVEL/); // the label names the roll, not its formula
  await user.click(await screen.findByRole('button', { name: 'Spend 1 Second Wind (2 left)' }));
  await waitFor(() => expect(within(resources()).getByRole('heading', { name: 'Second Wind: 1 of 2' })).toBeTruthy());
  await user.click(within(screen.getByRole('article', { name: 'E2E Warden' })).getByRole('button', { name: 'Roll Star burst (2d6)' }));
  await user.click(await screen.findByRole('button', { name: 'Spend 1 Star charges (2 left)' }));
  await waitFor(() => expect(within(resources()).getByRole('heading', { name: 'Star charges: 1 of 2' })).toBeTruthy());

  // A short rest gives one Second Wind use back (2024); the homebrew charges wait for the long rest.
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const rest = await screen.findByRole('region', { name: 'Short rest' });
  expect(await within(rest).findByRole('checkbox', { name: /^Second Wind: 1 → 2/ })).toBeTruthy();
  expect(within(rest).queryByRole('checkbox', { name: /^Star charges/ })).toBeNull();
  await user.click(within(rest).getByRole('button', { name: 'Finish short rest' }));
  await expectStatus(/Short rest finished/);
  await waitFor(() => expect(within(resources()).getByRole('heading', { name: 'Second Wind: 2 of 2' })).toBeTruthy());
});

it('authors a class in the studio and builds it at levels 1, 20 and 5/3 with an SRD class (M5 exit gate, ADR-010)', async () => {
  // An original nonstandard class, built in the studio with no code edits: a d8, Int and Wis saves, a multiclass
  // prerequisite, a skill choice, a column ("Ink"), a resource that reads it, and a caster whose multiclass share is its
  // own table (two thirds). Tables are invented for testing.
  const user = userEvent.setup();
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const newSource = await screen.findByRole('form', { name: 'New homebrew source' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Source title' }), 'E2E Chronicle Homebrew');
  await user.click(within(newSource).getByRole('checkbox', { name: 'SRD 5.2.1 (2024 rules)' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create source' }));
  await screen.findByRole('heading', { name: 'Content in E2E Chronicle Homebrew' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(editor()).getByRole('group', { name });
  await user.click(screen.getByRole('button', { name: 'New class' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Chronicler');

  const basics = within(editor()).getByRole('region', { name: 'Class basics' });
  await user.selectOptions(within(basics).getByRole('combobox', { name: 'Hit die' }), 'd8');
  const saves = within(basics).getByRole('group', { name: /^Saving throw proficiencies/ });
  await user.click(within(saves).getByRole('checkbox', { name: 'Intelligence' }));
  await user.click(within(saves).getByRole('checkbox', { name: 'Wisdom' }));
  const prerequisites = within(basics).getByRole('group', { name: /^Multiclass prerequisites/ });
  await user.type(within(prerequisites).getByRole('spinbutton', { name: 'Intelligence at least' }), '13');
  const skills = within(basics).getByRole('group', { name: /^Skill choice/ });
  await user.click(within(skills).getByRole('checkbox', { name: 'History' }));
  await user.click(within(skills).getByRole('checkbox', { name: 'Arcana' }));
  await user.click(within(skills).getByRole('checkbox', { name: 'Investigation' }));
  await user.click(within(skills).getByRole('button', { name: 'Create skill choice' }));
  await waitFor(() => expect(within(skills).getByText(/Choose 2 of 3 skills/)).toBeTruthy());
  expect(within(skills).getByText(/3 new option features published, 0 reused/)).toBeTruthy(); // announced, and focus stays in the group
  expect(document.activeElement).toBe(within(skills).getByText('Skill choice (starting class only)'));
  // Removing the choice and creating it again reuses the three published features, never publishes duplicates (review fix).
  await user.click(within(skills).getByRole('button', { name: 'Remove the skill choice' }));
  await waitFor(() => expect(within(skills).getByText(/Skill choice removed/)).toBeTruthy());
  await user.click(within(skills).getByRole('checkbox', { name: 'History' }));
  await user.click(within(skills).getByRole('checkbox', { name: 'Arcana' }));
  await user.click(within(skills).getByRole('checkbox', { name: 'Investigation' }));
  await user.click(within(skills).getByRole('button', { name: 'Create skill choice' }));
  await waitFor(() => expect(within(skills).getByText(/0 new option features published, 3 reused/)).toBeTruthy());
  expect(within(skills).getByText(/Choose 2 of 3 skills/)).toBeTruthy();
  await user.click(within(within(basics).getByRole('group', { name: 'Subclass' })).getByRole('checkbox', { name: /^This class has subclasses/ }));

  await user.click(within(editor()).getByRole('button', { name: 'Add class column' }));
  const column = rule(/^Rule 1: Class column/);
  await user.type(within(column).getByRole('textbox', { name: 'Column name' }), 'Ink');
  await user.clear(within(column).getByRole('textbox', { name: /^Key/ }));
  await user.type(within(column).getByRole('textbox', { name: /^Key/ }), 'ink');
  const values = within(column).getByRole('textbox', { name: /^Values at class levels/ });
  await user.clear(values);
  await user.type(values, '2, 2, 3, 3, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9');

  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  const ink = rule(/^Rule 2: Resource/);
  await user.type(within(ink).getByRole('textbox', { name: 'Resource name' }), 'Ink');
  await user.clear(within(ink).getByRole('textbox', { name: /^Uses/ }));
  await user.type(within(ink).getByRole('textbox', { name: /^Uses/ }), 'SCALE.ink');
  await user.click(within(editor()).getByRole('button', { name: 'Add recovery' })); // all, on a long rest

  await user.click(within(editor()).getByRole('button', { name: 'Add spellcasting' }));
  const casting = rule(/^Rule 4: Spellcasting/);
  await user.type(within(casting).getByRole('textbox', { name: /^Spell list key/ }), 'e2e-chronicle');
  const slots = ['1', '2', '3', '3, 1', '3, 2', '4, 2', '4, 2, 1', '4, 3, 1', '4, 3, 2', '4, 3, 2, 1', '4, 3, 3, 1', '4, 3, 3, 2', '4, 3, 3, 2, 1', '4, 3, 3, 3, 1', '4, 3, 3, 3, 2', '4, 3, 3, 3, 2, 1', '4, 3, 3, 3, 2, 1', '4, 3, 3, 3, 3, 1', '4, 3, 3, 3, 3, 2', '4, 3, 3, 3, 3, 2, 1'];
  await user.type(within(casting).getByRole('textbox', { name: /^Spell slots/ }), slots.join('{Enter}'));
  await user.selectOptions(within(casting).getByRole('combobox', { name: /^With other casters/ }), 'Its own table of caster levels');
  const share = within(casting).getByRole('textbox', { name: /^Caster levels it adds/ });
  await user.clear(share);
  await user.type(share, '0, 1, 2, 2, 3, 4, 4, 5, 6, 6, 7, 8, 8, 9, 10, 10, 11, 12, 12, 13');

  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Published E2E Chronicler.'));

  // A homebrew subclass joins the new class's subclass choice, which declares no options of its own.
  await user.click(screen.getByRole('button', { name: 'New subclass' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Order of Quills');
  const offered = within(editor()).getByRole('combobox', { name: 'Offered in the choice' });
  await waitFor(() => expect(within(offered).getByRole('option', { name: 'E2E Chronicler: Choose a subclass' })).toBeTruthy());
  await user.selectOptions(offered, 'E2E Chronicler: Choose a subclass');
  await user.click(within(editor()).getByRole('button', { name: 'Add modifier' })); // default: Initiative +1
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Published E2E Order of Quills.'));

  // Level 1 in the builder: the class is offered like any other, with its skill choice.
  await createCharacter(user, { name: 'E2E Scribe', family: 'srd-5.2.1', scores: { Intelligence: 16 }, cls: /^E2E Chronicler/ });
  await pick(user, /^E2E Chronicler: choose 2/, /^E2E Chronicler: History/);
  await pick(user, /^E2E Chronicler: choose 2/, /^E2E Chronicler: Arcana/);
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  let sheet = await screen.findByRole('article', { name: 'E2E Scribe' });
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^History: \+5/ })).toBeTruthy(); // the chosen option grants it: Int +3, PB +2
  await openTab(user, sheet, 'Play');
  expect(within(within(sheet).getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Ink: 2 of 2' })).toBeTruthy();
  expect(within(within(sheet).getByRole('region', { name: 'Class columns' })).getByText('Ink: 2')).toBeTruthy();
  await openTab(user, sheet, 'Spells');
  expect(within(within(sheet).getByRole('region', { name: 'Spells and slots' })).getByRole('heading', { name: 'Level 1 slots: 1 of 1' })).toBeTruthy();

  // Level 20, and Chronicler 5 with the SRD Wizard 3: the same published class, no code edits.
  const options = await client.listContent('srd-5.2.1');
  const chronicler = options.find((o) => o.kind === 'class' && o.name === 'E2E Chronicler' && !o.superseded)!.reference;
  const wizard = options.find((o) => o.kind === 'class' && o.name === 'Wizard' && o.compatible && !o.superseded)!.reference;
  const order = options.find((o) => o.kind === 'subclass' && o.name === 'E2E Order of Quills' && !o.superseded)!.reference;
  const scores = { str: 10, dex: 12, con: 14, int: 16, wis: 14, cha: 10 };
  await client.createCharacter({
    name: 'E2E Scribe 20',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: scores,
    pins: [],
    classes: [{ class: chronicler, level: 20 }],
    choices: [{ source: chronicler, choiceId: 'subclass', selected: [order] }],
  });
  await client.createCharacter({
    name: 'E2E Scribe Wizard',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: scores,
    pins: [],
    classes: [{ class: chronicler, level: 5 }, { class: wizard, level: 3 }],
    choices: [],
  });
  cleanup();
  render(<App />);

  await user.click(await screen.findByRole('button', { name: /^E2E Scribe 20/ }));
  sheet = await screen.findByRole('article', { name: 'E2E Scribe 20' });
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^Hit point maximum: 143/ })).toBeTruthy(); // 8 + 19 × 5 + 20 × Con 2
  expect(within(sheet).getByRole('heading', { name: /^Initiative: \+2/ })).toBeTruthy(); // Dex +1, the homebrew subclass +1
  await openTab(user, sheet, 'Play');
  expect(within(within(sheet).getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Ink: 9 of 9' })).toBeTruthy();
  await openTab(user, sheet, 'Spells');
  expect(within(within(sheet).getByRole('region', { name: 'Spells and slots' })).getByRole('heading', { name: 'Level 7 slots: 1 of 1' })).toBeTruthy();

  await user.click(screen.getByRole('button', { name: /^E2E Scribe Wizard/ }));
  sheet = await screen.findByRole('article', { name: 'E2E Scribe Wizard' });
  await openTab(user, sheet, 'Spells');
  const spells = within(sheet).getByRole('region', { name: 'Spells and slots' });
  // Chronicler 5 counts 3 (its table) + Wizard 3: caster level 6 on the Multiclass Spellcaster table.
  expect(within(spells).getByRole('heading', { name: 'Level 1 slots: 4 of 4' })).toBeTruthy();
  expect(within(spells).getByRole('heading', { name: 'Level 3 slots: 3 of 3' })).toBeTruthy();
  await openTab(user, sheet, 'Play');
  expect(within(within(sheet).getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Ink: 4 of 4' })).toBeTruthy();
});

it('archives a character after a preview, lists it apart, and brings it back (SPEC C-08)', async () => {
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Archivist', family: 'srd-5.1', scores: {}, species: /^Fixture Quickfoot/ });
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Archivist' });
  const characters = screen.getByRole('navigation', { name: 'Characters' });
  await openTab(user, sheet, 'Manage');

  // Preview first: focus moves into it, and Escape (or "Keep it") changes nothing and returns focus.
  await user.click(within(sheet).getByRole('button', { name: 'Archive…' }));
  let confirm = await screen.findByRole('alertdialog', { name: 'Archive E2E Archivist?' });
  expect(confirm.textContent).toMatch(/Nothing is deleted/);
  await waitFor(() => expect(document.activeElement).toBe(within(confirm).getByRole('button', { name: 'Archive' })));
  await user.keyboard('{Escape}');
  expect(screen.queryByRole('alertdialog')).toBeNull();
  expect(document.activeElement).toBe(within(sheet).getByRole('button', { name: 'Archive…' }));
  await user.click(within(sheet).getByRole('button', { name: 'Archive…' }));
  confirm = await screen.findByRole('alertdialog', { name: 'Archive E2E Archivist?' });
  await user.click(within(confirm).getByRole('button', { name: 'Keep it' }));
  expect(screen.queryByRole('alertdialog')).toBeNull();
  expect(within(characters).getByRole('button', { name: /E2E Archivist/ })).toBeTruthy();

  // Confirmed: the character moves to the collapsed "Archived" list, and its sheet says so.
  await user.click(within(sheet).getByRole('button', { name: 'Archive…' }));
  confirm = await screen.findByRole('alertdialog', { name: 'Archive E2E Archivist?' });
  await user.click(within(confirm).getByRole('button', { name: 'Archive' }));
  await expectStatus(/Archived E2E Archivist/);
  const archivedList = await within(characters).findByRole('list', { name: 'Archived characters' });
  expect(within(archivedList).getByRole('button', { name: /E2E Archivist/ })).toBeTruthy();
  expect(archivedList.closest('details')!.open).toBe(false);
  await waitFor(() => expect(within(screen.getByRole('article', { name: 'E2E Archivist' })).getByRole('heading', { name: 'Archived' })).toBeTruthy());
  // The pressed button is gone; focus lands on the control that replaced it, not on <body>.
  await waitFor(() => expect(document.activeElement).toBe(within(screen.getByRole('article', { name: 'E2E Archivist' })).getByRole('button', { name: 'Unarchive' })));

  // Unarchive: back in the main list.
  await user.click(within(screen.getByRole('article', { name: 'E2E Archivist' })).getByRole('button', { name: 'Unarchive' }));
  await expectStatus(/back in the character list/);
  await waitFor(() => expect(within(characters).queryByRole('list', { name: 'Archived characters' })).toBeNull());
  expect(within(characters).getByRole('button', { name: /E2E Archivist/ })).toBeTruthy();
  await waitFor(() => expect(document.activeElement).toBe(within(screen.getByRole('article', { name: 'E2E Archivist' })).getByRole('button', { name: 'Archive…' })));
});

// ---- character import from a D&D Beyond PDF sheet (features/ddb-pdf-import.md S4) ----
// The committed sheet is invented: "Testy McFixture", an original fixture Arcanist 3 / Chanter 2 whose spell "Fixture Veil"
// is on both casters' lists. The DevHost has no Open dialog, so the file input sends the bytes (ddb.readData).

const ddbSheet = () => readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-ddb-sheet.pdf'));

async function startDdbImport(user: ReturnType<typeof userEvent.setup>) {
  const start = await screen.findByRole<HTMLButtonElement>('button', { name: 'Import from D&D Beyond PDF…' });
  await waitFor(() => expect(start.disabled).toBe(false));
  await user.click(start);
}

async function readDdbSheet(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Choose PDF…' }));
  await user.upload(screen.getByLabelText('D&D Beyond PDF file'), new File([ddbSheet()], 'fixture-ddb-sheet.pdf', { type: 'application/pdf' }));
  expect(await screen.findByText('Fixture Arcanist 3 / Fixture Chanter 2', {}, { timeout: 30000 })).toBeTruthy();
}

it('imports a D&D Beyond sheet, resolves a choice, keeps one sheet number as an override and creates the character', async () => {
  const user = userEvent.setup();
  render(<App />);
  await startDdbImport(user);
  await readDdbSheet(user);

  await user.click(screen.getByRole('button', { name: 'Next: rules' }));
  // The 2014-style layout suggests SRD 5.1; the user could pick the other family.
  expect(screen.getByRole<HTMLInputElement>('radio', { name: /SRD 5\.1/ }).checked).toBe(true);
  await user.click(screen.getByRole('radio', { name: "Use the character sheet's values" })); // D16h preset
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));

  const matches = await screen.findByRole('region', { name: 'Matches' });
  const veil = await within(matches).findByRole('combobox', { name: 'Match for Fixture Veil' });
  await user.click(within(matches).getByRole('radio', { name: 'Needs a choice' }));
  expect(within(matches).getAllByRole('rowheader').map((h) => h.textContent)).toEqual(['Fixture Veil']);
  const chanter = within(veil).getAllByRole<HTMLOptionElement>('option').find((o) => /Fixture Chanter/.test(o.textContent ?? ''))!;
  await user.selectOptions(veil, chanter);
  await waitFor(() => expect(within(matches).queryAllByRole('rowheader')).toHaveLength(0));
  // Not installed (the background, a feat, two items, the features): listed, and kept as gap notes.
  await user.click(within(matches).getByRole('radio', { name: 'Not found' }));
  expect(within(matches).getByRole('rowheader', { name: 'Fixture Archivist' })).toBeTruthy();

  // Next waits while the proposal for the latest choice is loading.
  const nextStep = async (name: string) => {
    const next = screen.getByRole<HTMLButtonElement>('button', { name });
    await waitFor(() => expect(next.disabled).toBe(false));
    await user.click(next);
  };
  await nextStep('Next: numbers');
  const armorClass = await screen.findByRole('radiogroup', { name: 'Armor Class' });
  expect(within(screen.getByRole('radiogroup', { name: 'Armor Class' })).getByRole<HTMLInputElement>('radio', { name: "Keep the sheet's number" }).checked).toBe(true); // pre-filled by the preset
  await user.click(within(armorClass).getByRole('radio', { name: "Keep the sheet's number" }));
  await nextStep('Next: summary');

  const create = await screen.findByRole<HTMLButtonElement>('button', { name: 'Create character' });
  await waitFor(() => expect(create.disabled).toBe(false));
  await user.click(create);

  await expectStatus(/Character created from the D&D Beyond sheet: 3 override\(s\), \d+ gap note\(s\)/);
  const sheet = await screen.findByRole('article', { name: 'Testy McFixture' });
  expect(summaryValue(sheet, 'Armor Class')).toContain('(overridden)');
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^Armor Class:/ }).textContent).toContain('overridden (calculated');
});

it('cancelling the D&D Beyond import at each step leaves the character list unchanged', async () => {
  const user = userEvent.setup();
  const before = (await client.listCharacters()).map((c) => c.id).sort();
  render(<App />);
  const next = ['Next: rules', 'Next: matches', 'Next: numbers', 'Next: summary'];

  for (let step = 1; step <= 5; step++) {
    await startDdbImport(user);
    await readDdbSheet(user);
    for (const label of next.slice(0, step - 1)) {
      const button = await screen.findByRole<HTMLButtonElement>('button', { name: label });
      await waitFor(() => expect(button.disabled).toBe(false));
      await user.click(button);
    }
    await screen.findByRole('heading', { name: ['Choose the sheet', 'Rules and campaign', 'Matches', 'Numbers', 'Create'][step - 1] });
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Choose PDF…' })).toBeNull());
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Create' })).toBeNull());
    await expectStatus(/Import cancelled\. Nothing was saved\./);
  }

  expect((await client.listCharacters()).map((c) => c.id).sort()).toEqual(before);
});

it('keeps the theme picked in Settings when the app is rendered again (ADR-015)', async () => {
  const user = userEvent.setup();
  const { unmount } = render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Settings' }));
  expect(document.activeElement).toBe(screen.getByRole('heading', { level: 2, name: 'Settings' }));
  await user.click(screen.getByRole('radio', { name: 'Violet' }));
  expect(document.documentElement.dataset.theme).toBe('violet');
  await user.click(screen.getByRole('radio', { name: 'Dark' }));
  expect(document.documentElement.dataset.appearance).toBe('dark');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Text size' }), '125');
  expect(document.documentElement.dataset.textSize).toBe('125');
  unmount();
  // so the final check proves the mount effect re-applied the saved preferences
  delete document.documentElement.dataset.theme;
  delete document.documentElement.dataset.appearance;
  delete document.documentElement.dataset.textSize;
  render(<App />);
  await screen.findByRole('button', { name: 'Settings' });
  expect(document.documentElement.dataset.theme).toBe('violet');
  expect(document.documentElement.dataset.appearance).toBe('dark');
  expect(document.documentElement.dataset.textSize).toBe('125');
});

it('hides and shows the sidebar from the header and by Ctrl+B, moves focus out of a hidden sidebar, and remembers the state (ADR-015)', async () => {
  const user = userEvent.setup();
  const { unmount } = render(<App />);
  const toggle = await screen.findByRole('button', { name: 'Hide sidebar' });
  expect(toggle.getAttribute('aria-expanded')).toBe('true');
  expect(screen.getByRole('navigation', { name: 'Characters' })).toBeTruthy();

  await user.click(toggle);
  expect(screen.getByRole('button', { name: 'Show sidebar' }).getAttribute('aria-expanded')).toBe('false');
  expect(screen.queryByRole('navigation', { name: 'Characters' })).toBeNull();

  await user.keyboard('{Control>}b{/Control}');
  expect(screen.getByRole('navigation', { name: 'Characters' })).toBeTruthy();

  // Another keyboard layout: Ctrl + the physical B key reports a different character, so the shortcut follows `code`.
  fireEvent.keyDown(document, { key: 'и', code: 'KeyB', ctrlKey: true });
  expect(screen.queryByRole('navigation', { name: 'Characters' })).toBeNull();
  fireEvent.keyDown(document, { key: 'и', code: 'KeyB', ctrlKey: true });
  expect(screen.getByRole('navigation', { name: 'Characters' })).toBeTruthy();

  // The sidebar's own "Close sidebar" hides it and, since focus was inside, lands focus on the header toggle (WCAG 2.4.3).
  const close = screen.getByRole('button', { name: 'Close sidebar' });
  close.focus();
  await user.click(close);
  expect(screen.queryByRole('navigation', { name: 'Characters' })).toBeNull();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Show sidebar' }));
  await user.click(screen.getByRole('button', { name: 'Show sidebar' }));
  expect(within(screen.getByRole('navigation', { name: 'Characters' })).getByRole('heading', { level: 2, name: 'Characters' })).toBeTruthy();

  // Focus inside the sidebar, then hide it by the shortcut: focus must land on the toggle, not on <body>.
  const newCharacter = screen.getByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));
  newCharacter.focus();
  await user.keyboard('{Control>}b{/Control}');
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Show sidebar' }));

  unmount();
  render(<App />);
  expect(await screen.findByRole('button', { name: 'Show sidebar' })).toBeTruthy();
  expect(screen.queryByRole('navigation', { name: 'Characters' })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Show sidebar' }));
  expect(localStorage.getItem('tomestack.sidebar')).toBeNull();

  // A focused checkbox or radio must not block the shortcut (WCAG 2.1.1); a text field keeps Ctrl+B.
  await user.click(screen.getByRole('button', { name: 'Settings' }));
  for (const control of [screen.getByRole('checkbox', { name: 'Animate dice' }), screen.getByRole('radio', { name: 'Violet' })]) {
    control.focus();
    await user.keyboard('{Control>}b{/Control}');
    expect(screen.queryByRole('navigation', { name: 'Characters' })).toBeNull();
    await user.keyboard('{Control>}b{/Control}');
    expect(screen.getByRole('navigation', { name: 'Characters' })).toBeTruthy();
  }
  await user.click(screen.getByRole('button', { name: 'New character' }));
  const name = await screen.findByRole('textbox', { name: /^Name/ });
  name.focus();
  await user.keyboard('{Control>}b{/Control}');
  expect(screen.getByRole('navigation', { name: 'Characters' })).toBeTruthy(); // ignored in a text field
});

it('opens the Characters home screen with a card per character and opens one from it (D25)', async () => {
  const user = userEvent.setup();
  render(<App />);
  const home = await screen.findByRole('region', { name: 'Characters' }); // the start screen; the sidebar's heading and list share the name
  expect(within(home).getByRole('heading', { level: 2, name: 'Characters' })).toBeTruthy();
  const cards = await within(home).findAllByRole('listitem'); // earlier tests created characters
  expect(cards.length).toBeGreaterThan(0);
  expect(within(home).getByRole('list', { name: 'Characters' })).toBeTruthy();
  const first = cards[0]!;
  const name = within(first).getByRole('button', { name: /^Open / }).textContent!.replace(/^Open /, '');
  expect(first.textContent).toMatch(/Level \d+/);
  await user.click(within(first).getByRole('button', { name: `Open ${name}` }));
  expect(await screen.findByRole('article', { name })).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Characters' }));
  const back = await screen.findByRole('region', { name: 'Characters' });
  expect(document.activeElement).toBe(within(back).getByRole('heading', { level: 2, name: 'Characters' }));
});

it('creates a character with the standard array (D32)', async () => {
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Array', family: 'srd-5.2.1', cls: /^Barbarian/ });
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Array' });
  await openTab(user, sheet, 'Stats');
  expect(within(sheet).getByRole('heading', { name: /^Strength score: 15/ })).toBeTruthy();
  expect(within(sheet).getByRole('heading', { name: /^Charisma score: 8/ })).toBeTruthy();
});

it('rolls ability scores through the dice engine and assigns them (D32)', async () => {
  const user = userEvent.setup();
  render(<App />);
  const newCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: 'New character' });
  await waitFor(() => expect(newCharacter.disabled).toBe(false));
  await user.click(newCharacter);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Rolled');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  const sets = await screen.findByRole('list', { name: 'Rolled sets' });
  const lines = within(sets).getAllByRole('listitem').map((li) => li.textContent!);
  expect(lines).toHaveLength(6);
  for (const line of lines) expect(line).toMatch(/^Roll \d: (\d+) \((\d), (\d), (\d), dropped (\d)\)$/);
  const group = screen.getByRole('group', { name: 'Assign the rolled scores' });
  for (const label of ['Strength', 'Dexterity', 'Constitution', 'Intelligence', 'Wisdom', 'Charisma'] as const) {
    const select = within(group).getByRole('combobox', { name: label });
    const free = within(select).getAllByRole('option').find((o) => !(o as HTMLOptionElement).disabled && o.textContent !== 'Choose')!;
    await user.selectOptions(select, (free as HTMLOptionElement).value);
  }
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
});
