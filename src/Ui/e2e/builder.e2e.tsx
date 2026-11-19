// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell). Only the transport differs from the desktop
// app: HTTP to loopback instead of the WebView2 bridge, so export takes the download fallback instead of the native Save dialog.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { TomeStackError } from '../src/api/transport';
import { appStatus, createCharacter, expectStatus, fighter, goHome, openTab, pick, srd, startNewCharacter, summaryValue } from './helpers';

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

// The longest flow in the suite (create, two level-ups, play, rests): ~58 s alone locally (2026-10-29), so the 60 s
// default timed it out on a loaded machine; it gets 180 s, three times the suite's per-test budget.
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
  await openTab(user, sheet, 'Skills and saves');
  expect(within(sheet).getByRole('heading', { name: /^Strength score: 17/ })).toBeTruthy();
  expect(within(sheet).queryByRole('heading', { name: 'Choices to make' })).toBeNull();

  // A cancelled level-up draft changes nothing.
  await user.click(within(sheet).getByRole('button', { name: 'Level up' }));
  await user.click(await screen.findByRole('radio', { name: /^Barbarian 2\b/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  expect(await screen.findByText('All choices are made.')).toBeTruthy(); // level 2 offers no new choice
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  await user.click(await screen.findByRole('button', { name: 'Discard' })); // a level-up pick was made: Cancel asks first
  await expectStatus(/Draft discarded/);
  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  expect(within(sheet).getByText('Level 1')).toBeTruthy();

  // Level 2, then 3: the subclass and Primal Knowledge choices appear at level 3.
  for (const next of [2, 3]) {
    await user.click(within(await screen.findByRole('article', { name: 'E2E Brenna' })).getByRole('button', { name: 'Level up' }));
    await user.click(await screen.findByRole('radio', { name: new RegExp(`^Barbarian ${next}\\b`) }));
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

  // D74: Save level-up opens the sheet on Features with "What changed at level 3" focused (the level-2 list was overwritten).
  const changed = within(sheet).getByRole('region', { name: 'What changed at level 3' });
  await waitFor(() => expect(document.activeElement).toBe(within(changed).getByRole('heading', { name: 'What changed at level 3' })));
  expect(within(changed).getByText(/^Hit point maximum:/)).toBeTruthy();
  expect(within(changed).getByText(/^New choice: Primal Knowledge/)).toBeTruthy();
  await user.click(within(changed).getByRole('button', { name: 'Dismiss' }));
  expect(within(sheet).queryByRole('region', { name: /^What changed at level/ })).toBeNull();
  // Gone for good: leaving the sheet and opening the character again shows no panel.
  await goHome(user);
  await user.click(within(await screen.findByRole('navigation', { name: 'Main' })).getByRole('button', { name: /E2E Brenna/ }));
  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  expect(within(sheet).queryByRole('region', { name: /^What changed at level/ })).toBeNull();

  const open = within(sheet).getByRole('heading', { name: 'Choices to make' }).parentElement!;
  expect(open.textContent).toMatch(/Primal Knowledge: choose 1 \(0 chosen\)/);

  // Answer it from the sheet: Survival is already chosen elsewhere, so pick Animal Handling.
  await user.click(within(open).getByRole('button', { name: 'Choose now' }));
  await pick(user, /^Primal Knowledge: choose 1/, /^Barbarian Skill: Animal Handling/);
  expect(await screen.findByRole('checkbox', { name: /^Barbarian Skill: Animal Handling/, checked: true })).toBeTruthy();
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));

  // The M1 acceptance values for Brenna (m1-acceptance.md): AC 13, HP 35, Animal Handling +3.
  // The builder waits for the tick's preview before saving, and the saved view opens the sheet; wait for the re-render all the same.
  sheet = await screen.findByRole('article', { name: 'E2E Brenna' });
  await openTab(user, sheet, 'Skills and saves');
  expect(await within(sheet).findByRole('heading', { name: /^Armor Class: 13/ })).toBeTruthy();
  expect(await within(sheet).findByRole('heading', { name: /^Hit point maximum: 35/ })).toBeTruthy();
  expect(await within(sheet).findByRole('heading', { name: /^Animal Handling: \+3/ })).toBeTruthy();
  await waitFor(() => expect(within(sheet).queryByRole('heading', { name: 'Choices to make' })).toBeNull());

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

  await user.selectOptions(screen.getByRole('combobox', { name: 'Add condition' }), 'Poisoned');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Remove Poisoned' })).toBeTruthy());

  // A feature roll shows its record, and rolling spends nothing.
  await user.click(screen.getByRole('checkbox', { name: /Critical hit/ }));
  await user.click(screen.getByRole('button', { name: 'Roll Frenzy extra damage (Rage Damage +2: 2d6) (2d6)' }));
  const lastRoll = screen.getByRole('status', { name: 'Last roll' });
  expect(within(screen.getByRole('region', { name: 'Status' })).getByRole('status', { name: 'Last roll' })).toBe(lastRoll);
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Frenzy extra damage.*: \d+ \(2d6, critical\)/));
  expect(lastRoll.textContent).toMatch(/d6 \d \(critical\)/); // doubled dice are marked
  expect(lastRoll.textContent).toMatch(/Frenzy \(System Reference Document 5\.2\.1, p\. \d+\)/);

  // A d20 test with advantage keeps one die and drops the other.
  await user.click(screen.getByRole('radio', { name: 'Advantage' }));
  await openTab(user, sheet, 'Skills and saves');
  const strSave = screen.getByRole('region', { name: /^Strength saving throw:/ });
  await user.click(within(strSave).getByRole('heading'));
  await user.click(within(strSave).getByRole('button', { name: 'Roll Strength saving throw' }));
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Strength saving throw \(d20 test\): \d+ \(1d20, advantage\)/));
  expect(lastRoll.textContent).toMatch(/\(dropped\)/);
  expect(lastRoll.textContent).toMatch(/Strength saving throw \+5/);
  // D72: "Why?" sits beside the Last roll (not inside it) and explains the +5 in words, naming the proficiency bonus.
  const why = within(screen.getByRole('region', { name: 'Status' })).getByText('Why?').closest('details')!;
  expect(why.open).toBe(false);
  expect(lastRoll.contains(why)).toBe(false);
  await user.click(within(why).getByText('Why?'));
  expect(why.open).toBe(true);
  expect(within(why).getByRole('list', { name: 'Why this roll came out this way' }).textContent).toMatch(/proficiency bonus/i);
  // D28: the roll before it is in Earlier rolls, outside the live region.
  await user.click(within(sheet).getByText('Earlier rolls (1)'));
  expect(within(sheet).getByText('Earlier rolls (1)').closest('details')!.textContent).toMatch(/Frenzy extra damage.*: \d+ \(2d6, critical\)/);
  expect(lastRoll.textContent).not.toMatch(/Earlier rolls/);
  await openTab(user, sheet, 'Play');
  expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Rages: 2 of 3' })).toBeTruthy();

  // Features list their automation status.
  await openTab(user, sheet, 'Features');
  const features = screen.getByRole('region', { name: 'Features' });
  // C2-1: the new Barbarian's Danger Sense is a worked-out Dexterity-save advantage (it was reference text before).
  expect(within(features).getByText('Danger Sense').closest('li')!.textContent).toMatch(/Worked out for you/);

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
  const statusBar = () => screen.getByRole('region', { name: 'Status' });
  await user.click(within(statusBar()).getByRole('checkbox', { name: 'Heroic Inspiration' }));
  await waitFor(() => expect(within(statusBar()).getByRole<HTMLInputElement>('checkbox', { name: 'Heroic Inspiration' }).checked).toBe(true));
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

  // Wave 3 item 21 (D19 amended): a resistance recorded by hand on Stats halves typed damage on Play, and Undo puts it all back.
  await openTab(user, sheet, 'Skills and saves');
  const defences = () => screen.getByRole('region', { name: 'Damage resistances, immunities and vulnerabilities' });
  await user.selectOptions(within(defences()).getByRole('combobox', { name: 'Damage type' }), 'slashing');
  await user.click(within(defences()).getByRole('button', { name: 'Add' }));
  await waitFor(() => expect(within(defences()).getByRole('list', { name: 'Damage defences the sheet applies' }).textContent).toMatch(/Slashing resistance from Entered by hand/));
  await openTab(user, sheet, 'Play');
  await user.selectOptions(within(hpPanel()).getByRole('combobox', { name: 'Damage type' }), 'slashing');
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '13');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 29 of 35' })).toBeTruthy()); // 13 halved, rounded down: 6
  expect(hpPanel().textContent).toMatch(/13 slashing, halved: 6\./);
  await user.click(await screen.findByRole('button', { name: 'Undo last change: damage 13 slashing (6 taken)' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35' })).toBeTruthy());
  // Untyped damage ignores the resistance.
  await user.selectOptions(within(hpPanel()).getByRole('combobox', { name: 'Damage type' }), '');
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '4');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 31 of 35' })).toBeTruthy());
  await user.click(await screen.findByRole('button', { name: 'Undo last change: damage 4' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Hit points: 35 of 35' })).toBeTruthy());
  await openTab(user, sheet, 'Skills and saves');
  await user.click(within(defences()).getByRole('button', { name: 'Remove slashing resistance' }));
  await waitFor(() => expect(screen.queryByRole('list', { name: 'Damage defences the sheet applies' })).toBeNull());

  // Wave 3 item 27: Track XP is off by default; a total at the next level's threshold shows "Level up available" (display only).
  const xp = () => screen.getByRole('region', { name: 'Experience points' });
  expect(within(xp()).getByRole<HTMLInputElement>('checkbox', { name: 'Track XP' }).checked).toBe(false);
  await user.click(within(xp()).getByRole('checkbox', { name: 'Track XP' }));
  const xpBox = await within(xp()).findByRole<HTMLInputElement>('spinbutton', { name: 'Experience points' });
  expect(xpBox.value).toBe('0');
  expect(within(xp()).queryByText('Level up available')).toBeNull();
  const nextLevelAt = /^Next level at (\d{1,3}(,\d{3})*)$/;
  const threshold = Number((await within(xp()).findByText(nextLevelAt)).textContent!.replace(nextLevelAt, '$1').replaceAll(',', ''));
  await user.clear(xpBox);
  await user.type(xpBox, String(threshold - 1));
  await user.click(within(xp()).getByRole('button', { name: 'Save experience points' }));
  // Wait for the save to land (nothing differs, so Save is aria-disabled and focus is on the heading) before typing again:
  // a reply that arrives mid-type moves focus off the box and leaves a partial number in it.
  await waitFor(() => expect(within(xp()).getByRole('button', { name: 'Save experience points' }).getAttribute('aria-disabled')).toBe('true'));
  expect(within(xp()).getByRole<HTMLInputElement>('spinbutton', { name: 'Experience points' }).value).toBe(String(threshold - 1));
  expect(within(xp()).queryByText('Level up available')).toBeNull();
  await user.clear(within(xp()).getByRole('spinbutton', { name: 'Experience points' }));
  await user.type(within(xp()).getByRole('spinbutton', { name: 'Experience points' }), String(threshold));
  await user.click(within(xp()).getByRole('button', { name: 'Save experience points' }));
  await within(xp()).findByText('Level up available');
  await user.click(within(xp()).getByRole('checkbox', { name: 'Track XP' })); // untick: the total and the hint go
  await waitFor(() => expect(within(xp()).queryByRole('spinbutton', { name: 'Experience points' })).toBeNull());
  expect(within(xp()).queryByText('Level up available')).toBeNull();
  await openTab(user, sheet, 'Play');

  // M2 item 4: armor replaces Unarmored Defense (13); a shield adds to it. Original fixture equipment.
  await openTab(user, sheet, 'Gear');
  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^Fixture Scale Vest/ })).toBeTruthy());
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Scale Vest/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Scale Vest' }));
  await waitFor(() => expect(summaryValue(sheet, 'Armor Class')).toBe('15')); // 14 + Dex 1
  await openTab(user, sheet, 'Skills and saves');
  const ac = screen.getByRole('region', { name: /^Armor Class:/ });
  await user.click(within(ac).getByRole('heading'));
  expect(within(ac).getByRole('table').textContent).toMatch(/Unarmored Defense.*not used: it applies only while no armor is worn/);

  await openTab(user, sheet, 'Gear');
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Kite Shield/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Kite Shield' }));
  await waitFor(() => expect(summaryValue(sheet, 'Armor Class')).toBe('17'));
  await user.click(within(equipment()).getByRole('checkbox', { name: 'Equip Fixture Scale Vest' })); // take the armor off
  await waitFor(() => expect(summaryValue(sheet, 'Armor Class')).toBe('15')); // Unarmored Defense 13 + shield 2
}, 180_000);

it('drops picks that do not fit when the rules family changes, in the builder and in a campaign', async () => {
  const user = userEvent.setup();
  render(<App />);

  // Builder: an SRD 5.1 species, then SRD 5.2.1. The 5.1 pick is cleared and no longer listed.
  await startNewCharacter(user);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Switcher'); // the name is required to leave the Rules step
  await user.click(screen.getByRole('radio', { name: /2014 rules/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  const species = screen.getByRole('group', { name: 'Species' });
  const halfOrc = () => within(species).getByRole<HTMLInputElement>('radio', { name: /^Half-Orc/ });
  await waitFor(() => expect(halfOrc().getAttribute('aria-disabled')).toBeNull());
  await user.click(halfOrc());
  expect(halfOrc().checked).toBe(true);
  for (let i = 0; i < 3; i++) await user.click(screen.getByRole('button', { name: 'Back' })); // species, scores, class, rules
  await user.click(screen.getByRole('radio', { name: /2024 rules/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  // D31: the 5.1 species leaves the list altogether, and the pick is cleared. Wait for the loaded list ("None" present):
  // while the options are still loading there are no radios at all, so "no Half-Orc" alone passes too early.
  await waitFor(() => {
    const list = within(screen.getByRole('group', { name: 'Species' }));
    expect(list.getByRole<HTMLInputElement>('radio', { name: 'None' }).checked).toBe(true);
    expect(list.queryByRole('radio', { name: /^Half-Orc/ })).toBeNull();
  });
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  await user.click(await screen.findByRole('button', { name: 'Discard' })); // the rules family was changed: Cancel asks first

  // Campaign: an SRD 5.1 source, then SRD 5.2.1. The hidden 5.1 source is not saved with the campaign.
  await user.click(screen.getByRole('button', { name: 'Campaigns' }));
  await user.click(await screen.findByRole('button', { name: 'New campaign' }));
  const form = screen.getByRole('form', { name: 'Campaign' });
  await user.type(within(form).getByRole('textbox', { name: 'Campaign name' }), 'E2E Switched');
  await user.click(within(form).getByRole('radio', { name: /2014 rules/ }));
  await user.click(within(form).getByRole('checkbox', { name: /^System Reference Document 5\.1/ }));
  await user.click(within(form).getByRole('radio', { name: /2024 rules/ }));
  await user.click(within(form).getByRole('button', { name: 'Save campaign' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Saved the campaign E2E Switched.'));
  const saved = screen.getAllByRole('listitem').find((li) => li.textContent?.startsWith('E2E Switched'))!;
  expect(saved.textContent).toMatch(/0 allowed sources/);
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
  const lastRoll = screen.getByRole('status', { name: 'Last roll' });
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

it('creates a character with the standard array (D32)', async () => {
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Array', family: 'srd-5.2.1', cls: /^Barbarian/ });
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Array' });
  await openTab(user, sheet, 'Skills and saves');
  expect(within(sheet).getByRole('heading', { name: /^Strength score: 15/ })).toBeTruthy();
  expect(within(sheet).getByRole('heading', { name: /^Charisma score: 8/ })).toBeTruthy();
});

it('rolls ability scores through the dice engine and assigns them (D32)', async () => {
  const user = userEvent.setup();
  render(<App />);
  await startNewCharacter(user);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Rolled');
  await user.click(screen.getByRole('radio', { name: /2024 rules/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await user.click(await screen.findByRole('radio', { name: /^Barbarian/ }));
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  const sets = await screen.findByRole('list', { name: 'Rolled sets' });
  const lines = within(sets).getAllByRole('listitem').map((li) => li.textContent!);
  expect(lines).toHaveLength(6);
  // Each line's total is the sum of the three kept dice (the fourth is dropped).
  const totals: number[] = [];
  for (const line of lines) {
    const m = /^Roll \d: (\d+) \((\d), (\d), (\d), dropped (\d)\)$/.exec(line);
    expect(m, line).not.toBeNull();
    const [total, a, b, c, dropped] = m!.slice(1).map(Number) as [number, number, number, number, number];
    expect(total).toBe(a + b + c);
    expect(dropped).toBeLessThanOrEqual(Math.min(a, b, c));
    totals.push(total);
  }
  const group = screen.getByRole('group', { name: 'Assign the rolled scores' });
  const assigned = new Map<string, number>();
  for (const label of ['Strength', 'Dexterity', 'Constitution', 'Intelligence', 'Wisdom', 'Charisma'] as const) {
    const select = within(group).getByRole('combobox', { name: label });
    const free = within(select).getAllByRole('option').find((o) => !(o as HTMLOptionElement).disabled && o.textContent !== 'Choose')!;
    await user.selectOptions(select, (free as HTMLOptionElement).value);
    assigned.set(label, Number((free as HTMLOptionElement).value));
  }
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  expect([...assigned.values()].sort((x, y) => x - y)).toEqual([...totals].sort((x, y) => x - y)); // every rolled total used once
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
  await user.click(screen.getByRole('button', { name: 'Next: gear' }));
  await user.click(screen.getByRole('button', { name: /^Skip: I'll add gear myself/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Rolled' });
  await openTab(user, sheet, 'Skills and saves');
  // The sheet's base scores are the assigned rolled totals (increases come from content the draft does not pin).
  for (const [label, value] of assigned) expect(within(sheet).getByRole('heading', { name: new RegExp(`^${label} score: ${value}\\b`) })).toBeTruthy();
});

it('previews a Fighter 3 at level 5 from the level-up step: Extra Attack shows and nothing is saved (D82)', async () => {
  const user = userEvent.setup();
  const created = await client.createCharacter({
    name: 'E2E Previewer',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 16, dex: 14, con: 14, int: 10, wis: 12, cha: 8 },
    pins: [srd(1), srd(2)],
    classes: [{ class: fighter(0), level: 3 }],
    choices: [{ source: srd(2), choiceId: 'soldier-ability-scores', selected: [srd(4)] }],
  });
  const before = (await client.getCharacter(created.character.id)).character;
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Previewer/ }));
  await user.click(within(await screen.findByRole('article', { name: 'E2E Previewer' })).getByRole('button', { name: 'Level up' }));

  const form = await screen.findByRole('form', { name: 'Preview at level' });
  const level = within(form).getByRole('spinbutton', { name: 'Level to preview' });
  await user.clear(level);
  await user.type(level, '5');
  await user.click(within(form).getByRole('button', { name: 'Preview' }));
  const preview = await screen.findByRole('region', { name: 'Preview at level 5' });
  expect(within(preview).getByText('Extra Attack')).toBeTruthy();
  await user.click(within(preview).getByRole('button', { name: 'Close preview' }));
  expect(screen.queryByRole('region', { name: 'Preview at level 5' })).toBeNull();

  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  await expectStatus(/Draft discarded/);
  const after = (await client.getCharacter(created.character.id)).character;
  expect(after.level).toBe(3);
  expect(after).toEqual(before); // the store received no write
});

it('levels a Cleric into Wizard: what each class gives, the prerequisite warning that never blocks, and the new class on the sheet (D99)', async () => {
  const user = userEvent.setup();
  const listed = await client.listContent('srd-5.2.1');
  const named = (kind: string, name: RegExp) => listed.find((o) => o.compatible && !o.superseded && o.kind === kind && name.test(o.name))!.reference; // the list holds both families
  const wizard = named('class', /^Wizard/);
  const created = await client.createCharacter({
    name: 'E2E Multiclasser',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 10, dex: 12, con: 14, int: 8, wis: 15, cha: 10 },
    pins: [named('species', /^Dwarf/), named('background', /^Soldier/)],
    classes: [{ class: named('class', /^Cleric/), level: 1 }],
  });
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Multiclasser/ }));
  await user.click(within(await screen.findByRole('article', { name: 'E2E Multiclasser' })).getByRole('button', { name: 'Level up' }));

  // The character's own class says what it gives at its next level; the new class waits behind "Another class…".
  expect(await screen.findByRole('radio', { name: /^Cleric 2 — what you get: / })).toBeTruthy();
  await user.click(await screen.findByRole('radio', { name: 'Another class…' }));
  const select = await screen.findByRole('combobox', { name: 'Class to start' });
  await user.selectOptions(select, await within(select).findByRole('option', { name: 'Wizard' }));
  expect(await screen.findByText('Wizard — needs Intelligence 13 (you have 8). You can still take it.')).toBeTruthy();

  const next = screen.getByRole('button', { name: 'Next: choices' });
  expect(next.getAttribute('aria-disabled')).toBeNull(); // advice, never a block
  await user.click(next);
  await user.click(await screen.findByRole('button', { name: 'Save level-up' }));

  const sheet = await screen.findByRole('article', { name: 'E2E Multiclasser' });
  expect(within(sheet).getByText('Level 2')).toBeTruthy();
  const saved = (await client.getCharacter(created.character.id)).character;
  expect(saved.classes.map((c) => [c.class.contentId, c.level])).toContainEqual([wizard.contentId, 1]);
});

it('tries the sample from the chooser, plays it and removes it (D95)', async () => {
  const user = userEvent.setup();
  // The DevHost is shared, so Home is not empty here (the welcome card is covered by HomePanel's tests): the sample is reached
  // through the chooser, which offers it while no sample exists. A leftover sample from a failed run is removed first.
  for (const c of await client.listCharacters()) {
    if (c.origin !== 'sample') continue;
    try {
      await client.deleteSample(c.id);
    } catch (error) {
      // Archiving it would hide "Try the sample" in the chooser, so the test cannot go on either way.
      if (error instanceof TomeStackError && error.code === 'character.sample-has-snapshots') {
        throw new Error('A leftover sample with snapshots is in the DevHost data; remove it by hand.', { cause: error });
      }
      throw error;
    }
  }

  render(<App />);
  await user.click(await screen.findByRole('button', { name: '+ Add a character' }));
  const choice = await screen.findByRole<HTMLInputElement>('radio', { name: /^Try the sample/ });
  await user.click(choice);
  await user.click(screen.getByRole('button', { name: 'Continue' }));

  const sheet = await screen.findByRole('article', { name: 'Wren Ashdown' });
  expect(within(sheet).getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  await waitFor(() => expect(summaryValue(sheet, 'Armor Class')).toMatch(/^16/));
  expect(within(sheet).getByText('This is a sample character.')).toBeTruthy();

  // Remove sample asks first: focus goes to Keep, Keep puts it back on Remove sample and leaves the character alone.
  await user.click(within(sheet).getByRole('button', { name: 'Remove sample' }));
  const dialog = await within(sheet).findByRole('alertdialog', { name: 'Remove Wren Ashdown?' });
  expect(within(dialog).getByText('Remove Wren Ashdown? This cannot be undone.')).toBeTruthy();
  await waitFor(() => expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Keep' })));
  await user.click(within(dialog).getByRole('button', { name: 'Keep' }));
  expect(within(sheet).queryByRole('alertdialog')).toBeNull();
  expect(document.activeElement).toBe(within(sheet).getByRole('button', { name: 'Remove sample' }));

  await user.click(within(sheet).getByRole('button', { name: 'Remove sample' }));
  await user.click(within(await within(sheet).findByRole('alertdialog')).getByRole('button', { name: 'Remove' }));

  // Back home, the sample is gone from the sidebar and from the service.
  await waitFor(() => expect(screen.queryByRole('article', { name: 'Wren Ashdown' })).toBeNull());
  await waitFor(() => expect(screen.queryByRole('button', { name: /^Wren Ashdown/ })).toBeNull());
  expect((await client.listCharacters()).some((c) => c.origin === 'sample')).toBe(false);
});

it('builds a 2014 Cleric with its kit: seven steps, option a gives AC 16 with the shield, Mace on Play, the kit on Gear (Wave 3, D97)', async () => {
  const user = userEvent.setup();
  render(<App />);
  let preview = '';
  await createCharacter(user, {
    name: 'E2E Kitted',
    family: 'srd-5.1',
    scores: { Dexterity: 10 }, // scale mail 14 + 0 Dex + shield 2
    cls: /^Cleric/,
    kit: true,
    beforeChoices: async () => {
      // The Gear step shows the live Armor Class for the kit as chosen (option a for every pick).
      await waitFor(() => expect(screen.getByText(/^AC with this gear: 16/)).toBeTruthy());
      preview = screen.getByText(/^AC with this gear:/).textContent ?? '';
    },
  });
  expect(preview).toMatch(/16/);
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));

  const sheet = await screen.findByRole('article', { name: 'E2E Kitted' });
  await waitFor(() => expect(summaryValue(sheet, 'Armor Class')).toMatch(/^16/));
  await openTab(user, sheet, 'Play');
  expect(within(sheet).getAllByText(/Mace/).length).toBeGreaterThan(0);
  await openTab(user, sheet, 'Gear');
  const equipment = screen.getByRole('region', { name: 'Equipment' });
  for (const item of [/Mace/, /Scale mail/, /Shield/]) expect(within(equipment).getAllByText(item).length).toBeGreaterThan(0);
}, 180_000);

it('a 2024 class taking gold has coins and no kit items (Wave 3, D97)', async () => {
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, {
    name: 'E2E Goldtaker',
    family: 'srd-5.2.1',
    cls: /^Barbarian/,
    background: /^Soldier/,
    kit: true,
    beforeChoices: async () => {
      // Option b of the Barbarian's kit is gold only, and so is the Soldier's.
      const classKit = await screen.findByRole('region', { name: /^From your class/ });
      await user.click(within(classKit).getByRole('radio', { name: /Instead: 75 gp/ }));
      const backgroundKit = screen.getByRole('region', { name: /^From your background/ });
      await user.click(within(backgroundKit).getByRole('radio', { name: /Instead: 50 gp/ }));
    },
  });
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));

  const sheet = await screen.findByRole('article', { name: 'E2E Goldtaker' });
  await openTab(user, sheet, 'Gear');
  const equipment = screen.getByRole('region', { name: 'Equipment' });
  expect(within(equipment).queryAllByRole('checkbox', { name: /^Equip / })).toHaveLength(0);
  const gold = await screen.findByRole<HTMLInputElement>('spinbutton', { name: 'Gold (gp)' });
  expect(gold.value).toBe('125');
}, 180_000);
