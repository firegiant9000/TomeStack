// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell). Only the transport differs from the desktop
// app: HTTP to loopback instead of the WebView2 bridge, so export takes the download fallback instead of the native Save dialog.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { appStatus, createCharacter, expectStatus, openTab, pick, summaryValue } from './helpers';

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

it('equips a weapon: the attack uses finesse and proficiency, rolls, and actions are grouped', async () => {
  // SPEC C-02, C-04 with the original fixtures "Fixture Duelist" (simple and martial weapons) and "Fixture Needle" (finesse).
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Blade', scores: { Strength: 14, Dexterity: 16 }, cls: /^Fixture Duelist/ });
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Acrobatics/);
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Insight/);
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const blade = await screen.findByRole('article', { name: 'E2E Blade' });

  await openTab(user, blade, 'Gear');
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
  const lastRoll = screen.getByRole('status', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Fixture Needle attack: \d+ \(1d20\)/));
  expect(lastRoll.textContent).toMatch(/Fixture Needle to hit \+5/);
  const reactions = within(actions()).getByRole('region', { name: 'Reactions' });
  expect(within(reactions).getByRole('button', { name: 'Roll Riposte damage (1d6)' })).toBeTruthy();
});

it('adds an attack of your own on Inventory, rolls it on Play with advantage, and the roll shows in Last roll (item 18)', async () => {
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Sling', scores: {}, cls: /^Fixture Duelist/ });
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Acrobatics/);
  await pick(user, /^Fixture Duelist: choose 2/, /^Duelist Skill: Insight/);
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const hero = await screen.findByRole('article', { name: 'E2E Sling' });

  // Inventory: an item and an attack of your own. Neither changes a calculation.
  await openTab(user, hero, 'Gear');
  const armorClass = summaryValue(hero, 'Armor Class');
  const items = () => screen.getByRole('region', { name: 'Your own items' });
  await user.click(within(items()).getByRole('button', { name: 'Add an item of your own' }));
  await user.type(within(items()).getByRole('textbox', { name: 'Name' }), 'Testy Rope');
  await user.click(within(items()).getByRole('button', { name: 'Save item' }));
  await expectStatus(/^Added Testy Rope\.$/);
  await waitFor(() => expect(document.activeElement).toBe(within(items()).getByRole('button', { name: 'Edit Testy Rope' }))); // focus can land a tick after the status text

  const own = () => screen.getByRole('region', { name: 'Your own attacks' });
  await user.click(within(own()).getByRole('button', { name: 'Add an attack of your own' }));
  await user.type(within(own()).getByRole('textbox', { name: 'Name' }), 'Testy Sling');
  await user.clear(within(own()).getByRole('spinbutton', { name: 'To hit bonus' }));
  await user.type(within(own()).getByRole('spinbutton', { name: 'To hit bonus' }), '5');
  // The dice engine's own message for bad damage, linked to the field; nothing is saved.
  const damage = within(own()).getByRole('textbox', { name: 'Damage (optional)' });
  await user.type(damage, '5');
  await user.click(within(own()).getByRole('button', { name: 'Save attack' }));
  await waitFor(() => expect(damage.getAttribute('aria-invalid')).toBe('true'));
  expect(document.getElementById(damage.getAttribute('aria-describedby')!.split(' ').at(-1)!)?.textContent).toMatch(/dice/i);
  await user.clear(damage);
  await user.type(damage, '1d6+2');
  await user.selectOptions(within(own()).getByRole('combobox', { name: 'Damage type (optional)' }), 'Bludgeoning');
  await user.click(within(own()).getByRole('button', { name: 'Save attack' }));
  await expectStatus(/^Added Testy Sling\.$/);
  expect(summaryValue(hero, 'Armor Class')).toBe(armorClass);

  // Play: the attack is listed (the panel shows for a character with only custom attacks) and rolled with advantage.
  await openTab(user, hero, 'Play');
  const actions = () => screen.getByRole('region', { name: 'Attacks and actions' });
  expect(within(actions()).getAllByRole('button', { name: 'Roll Testy Sling attack' })).toHaveLength(1);
  await user.click(screen.getByRole('radio', { name: 'Advantage' }));
  await user.click(within(actions()).getByRole('button', { name: 'Roll Testy Sling attack' }));
  const lastRoll = screen.getByRole('status', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Testy Sling attack: \d+ \(1d20, advantage\)/));
  expect(lastRoll.textContent).toMatch(/Testy Sling to hit \+5/);
  await user.click(within(actions()).getByRole('button', { name: 'Roll Testy Sling damage' }));
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Testy Sling damage \(bludgeoning\): \d+ \(1d6\+2\)/));
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
  const lastRoll = screen.getByRole('status', { name: 'Last roll' });
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
  await openTab(user, sheet, 'Skills and saves');
  const armorClassHeading = screen.getByRole('heading', { name: /^Armor Class:/ }).textContent;

  await openTab(user, sheet, 'Features'); // gap notes end the Features tab
  const gaps = () => screen.getByRole('region', { name: /^Gap notes/ });
  expect(await within(gaps()).findByText('No gap notes yet.')).toBeTruthy();
  const form = within(gaps()).getByRole('form', { name: 'New gap note' });
  const about = within(form).getByRole('combobox', { name: /^About/ });
  const text = within(form).getByRole('textbox', { name: /^What was missing or wrong/ });

  await user.selectOptions(about, within(about).getByRole('option', { name: 'Armor Class' }));
  await user.type(text, 'The table grants a cover bonus here.');
  await user.click(within(form).getByRole('button', { name: 'Save note' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Gap note saved for Armor Class.'));
  expect(within(gaps()).getByText('The table grants a cover bonus here.')).toBeTruthy();

  // M3 C5: "Report a gap" on a feature pre-fills the picker and moves focus to the note's text.
  await openTab(user, sheet, 'Features');
  const features = within(screen.getByRole('article', { name: 'E2E Gaps' })).getByRole('region', { name: 'Features' });
  await user.click(within(features).getByRole('button', { name: 'Report a gap: Fixture Quickfoot' }));
  expect((about as HTMLSelectElement).selectedOptions[0]!.textContent).toBe('Fixture Quickfoot');
  await waitFor(() => expect(document.activeElement).toBe(text));
  expect(within(sheet).getByRole('tab', { name: 'Features' }).getAttribute('aria-selected')).toBe('true');
  await user.type(text, 'Speed bonus should apply while unarmored only.');
  await user.click(within(form).getByRole('button', { name: 'Save note' }));
  await waitFor(() => expect(within(gaps()).getByRole('heading').textContent).toBe('Gap notes: 2 open'));

  await user.click(within(gaps()).getByRole('button', { name: 'Mark resolved: Armor Class' }));
  await waitFor(() => expect(within(gaps()).getByRole('heading').textContent).toBe('Gap notes: 1 open'));
  expect(within(gaps()).getByRole('button', { name: 'Reopen: Armor Class' })).toBeTruthy();

  // "Report a gap" on a field works the same way (the field's details hold the button).
  await openTab(user, sheet, 'Skills and saves');
  const armor = within(screen.getByRole('article', { name: 'E2E Gaps' })).getByRole('region', { name: /^Armor Class:/ });
  await user.click(within(armor).getByRole('heading'));
  await user.click(within(armor).getByRole('button', { name: 'Report a gap: Armor Class' }));
  expect((about as HTMLSelectElement).selectedOptions[0]!.textContent).toBe('Armor Class');
  await waitFor(() => expect(document.activeElement).toBe(text));
  // "Report a gap" left Features as the remembered tab; move the memory off it so the deep link below is what selects Features.
  await openTab(user, sheet, 'Play');

  // M3 C5: the list across characters shows the open note with its character; resolved ones only on request.
  await user.click(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('button', { name: 'Help' }));
  const all = await screen.findByRole('region', { name: 'Gap notes' }); // a section of the Help screen; its list keeps "of all characters"
  const allList = await within(all).findByRole('list', { name: 'Gap notes of all characters' });
  const mine = () => within(allList).getAllByRole('listitem').filter((li: HTMLElement) => li.textContent!.startsWith('E2E Gaps'));
  expect(mine().map((li: HTMLElement) => li.textContent)).toEqual([expect.stringContaining('Speed bonus should apply while unarmored only.')]);
  await user.click(within(all).getByRole('checkbox', { name: 'Show resolved notes' }));
  await waitFor(() => expect(mine()).toHaveLength(2));
  await user.click(within(all).getAllByRole('button', { name: 'Open E2E Gaps' })[0]!);
  await screen.findByRole('article', { name: 'E2E Gaps' });
  // "Open <character>" from the Help screen's Gap notes section opens the sheet on Features, where the notes are.
  expect(within(screen.getByRole('article', { name: 'E2E Gaps' })).getByRole('tab', { name: 'Features' }).getAttribute('aria-selected')).toBe('true');
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
  await openTab(user, screen.getByRole('article', { name: 'E2E Gaps' }), 'Skills and saves');
  expect(screen.getByRole('heading', { name: /^Armor Class:/ }).textContent).toBe(armorClassHeading);
});

it('a roll from any tab shows in the status bar', async () => {
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Barwatch', family: 'srd-5.2.1', scores: { Intelligence: 16 }, cls: /^Fixture Arcanist/ });
  const picker = await screen.findByRole('group', { name: /^Fixture Arcanist spells \(0 of 3 cantrips, 0 of 4 prepared spells\)/ });
  await user.click(within(picker).getByRole('checkbox', { name: /^Fixture Spark/ }));
  await user.click(screen.getByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Barwatch' });

  // Gear: a weapon, so Play has an attack to roll.
  await openTab(user, sheet, 'Gear');
  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^Fixture Needle/ })).toBeTruthy());
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^Fixture Needle/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip Fixture Needle' }));

  // The last roll is the one live node inside the status bar, whichever tab rolled it.
  const bar = () => within(screen.getByRole('region', { name: 'Status' }));
  expect(screen.getByRole('region', { name: 'Status' }).classList.contains('status-bar')).toBe(true);
  const lastRoll = () => bar().getByRole('status', { name: 'Last roll' });

  await openTab(user, sheet, 'Play');
  const actions = () => screen.getByRole('region', { name: 'Attacks and actions' });
  await user.click(await within(actions()).findByRole('button', { name: 'Roll Fixture Needle attack' }));
  await waitFor(() => expect(lastRoll().textContent).toMatch(/Fixture Needle attack: \d+ \(1d20\)/));

  await openTab(user, sheet, 'Spells');
  const spells = () => within(screen.getByRole('article', { name: 'E2E Barwatch' })).getByRole('region', { name: 'Spells and slots' });
  await user.click(within(spells()).getByRole('button', { name: 'Roll Fixture Spark attack' }));
  await waitFor(() => expect(lastRoll().textContent).toMatch(/Fixture Spark \(spell attack\): \d+ \(1d20\)/));
  expect(lastRoll().textContent).not.toMatch(/Fixture Needle/);

  await openTab(user, sheet, 'Skills and saves');
  const intSave = screen.getByRole('region', { name: /^Intelligence saving throw:/ });
  await user.click(within(intSave).getByRole('heading'));
  await user.click(within(intSave).getByRole('button', { name: 'Roll Intelligence saving throw' }));
  await waitFor(() => expect(lastRoll().textContent).toMatch(/Intelligence saving throw \(d20 test\): \d+ \(1d20\)/));
  expect(lastRoll().textContent).not.toMatch(/Fixture Spark/);
});

it('shows the Bloodied chip and switches an auto toggle on at half hit points, and Undo takes both away (item 20, D38)', async () => {
  const user = userEvent.setup();
  // Setup through the client: a homebrew feat (content schema v10) whose toggle switches itself while bloodied, and a character with 20 hit points.
  const source = await client.createHomebrewSource('E2E Grit Book', ['srd-5.2.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Grit',
    rulesFamilies: ['srd-5.2.1'],
    provenance: { sourceId: source.id },
    status: 'draft',
    effects: [
      { type: 'toggle', id: 'grit-toggle', toggleId: 'grit', label: 'E2E grit', autoWhen: 'bloodied' },
      { type: 'modifier', id: 'grit-ac', operation: 'bonus', target: 'armorClass', value: '2', timing: 'whileActive', toggle: 'grit' },
    ],
  });
  const feat = (await client.publish(draft)).published;
  const created = await client.createCharacter({ name: 'E2E Grit Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [feat] });
  await client.saveCharacter({ ...created.character, overrides: [{ field: 'hitPoints', value: 20 }] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Grit Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Grit Hero' });
  await openTab(user, sheet, 'Play');
  const summary = () => within(sheet).getByRole('region', { name: 'Status' });
  const effects = () => within(screen.getByRole('region', { name: 'Attacks and actions' })).getByRole('group', { name: /Active effects/ });
  const armorClass = summaryValue(sheet, 'Armor Class');
  expect(within(summary()).queryByText('Bloodied')).toBeNull();
  expect(effects().textContent).toMatch(/E2E grit.*On while bloodied \(off now\)/);
  expect(within(effects()).queryByRole('checkbox')).toBeNull(); // no switch: the sheet decides

  // Damage to exactly half (10 of 20): the chip, the toggle and its +2 Armor Class.
  const hpPanel = () => screen.getByRole('region', { name: /^Hit points:/ });
  await user.type(within(hpPanel()).getByRole('spinbutton', { name: 'Amount' }), '10');
  await user.click(within(hpPanel()).getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(within(summary()).getByText('Bloodied')).toBeTruthy());
  expect(effects().textContent).toMatch(/On while bloodied \(on now\)/);
  expect(Number(summaryValue(sheet, 'Armor Class'))).toBe(Number(armorClass) + 2);

  // Undo puts the hit points back exactly; the chip, the toggle and the bonus go with them.
  await user.click(await screen.findByRole('button', { name: 'Undo last change: damage 10' }));
  await waitFor(() => expect(within(summary()).queryByText('Bloodied')).toBeNull());
  expect(effects().textContent).toMatch(/On while bloodied \(off now\)/);
  expect(summaryValue(sheet, 'Armor Class')).toBe(armorClass);
});

it('lists a toggle and a resource with an activation under their group, and switching the toggle keeps focus on it (item 22, R157)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Reflex Book', ['srd-5.2.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Reflex',
    rulesFamilies: ['srd-5.2.1'],
    provenance: { sourceId: source.id },
    status: 'draft',
    effects: [
      { type: 'toggle', id: 'guard-toggle', toggleId: 'guard', label: 'E2E guard', activation: 'reaction' },
      { type: 'modifier', id: 'guard-ac', operation: 'bonus', target: 'armorClass', value: '2', timing: 'whileActive', toggle: 'guard' },
      { type: 'toggle', id: 'calm-toggle', toggleId: 'calm', label: 'E2E calm' },
      { type: 'resource', id: 'surge-resource', resourceId: 'surge', label: 'E2E surge', maximum: '2', activation: 'bonusAction' },
    ],
  });
  const feat = (await client.publish(draft)).published;
  await client.createCharacter({ name: 'E2E Reflex Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [feat] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Reflex Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Reflex Hero' });
  await openTab(user, sheet, 'Play');
  const actions = () => screen.getByRole('region', { name: 'Attacks and actions' });
  const armorClass = summaryValue(sheet, 'Armor Class');

  expect(within(within(actions()).getByRole('group', { name: /Active effects/ })).getByRole('checkbox', { name: /E2E calm/ })).toBeTruthy(); // no activation: stays
  expect(within(within(actions()).getByRole('group', { name: /Active effects/ })).queryByRole('checkbox', { name: /E2E guard/ })).toBeNull();
  const resources = within(sheet).getByRole('region', { name: 'Resources' });
  expect(within(within(resources).getByRole('region', { name: 'Bonus actions' })).getByRole('heading', { name: /^E2E surge: 2 of 2/ })).toBeTruthy();

  await user.click(within(within(actions()).getByRole('region', { name: 'Reactions' })).getByRole('checkbox', { name: /E2E guard/ }));
  await waitFor(() => expect(Number(summaryValue(sheet, 'Armor Class'))).toBe(Number(armorClass) + 2));
  const guard = within(within(actions()).getByRole('region', { name: 'Reactions' })).getByRole('checkbox', { name: /E2E guard/ }) as HTMLInputElement;
  expect(guard.checked).toBe(true);
  expect(document.activeElement).toBe(guard);
});

it('carries two copies of a charged wand that needs attunement: attuning applies it, charges are separate, and a long rest returns them by the rolled total (item 25)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Wand Book', ['srd-5.2.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'item',
    name: 'E2E Wand',
    rulesFamilies: ['srd-5.2.1'],
    provenance: { sourceId: source.id },
    status: 'draft',
    effects: [
      { type: 'modifier', id: 'wand-ac', operation: 'bonus', target: 'armorClass', value: '2' },
      { type: 'item', id: 'wand-use', requiresAttunement: true, charges: { max: 3, recovery: { period: 'longRest', regain: '1d3' } } },
    ],
  });
  await client.publish(draft);
  await client.createCharacter({ name: 'E2E Wand Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Wand Hero/ }));
  const hero = await screen.findByRole('article', { name: 'E2E Wand Hero' });
  await openTab(user, hero, 'Gear');
  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  const armorClass = () => Number(/^(\d+)/.exec(summaryValue(hero, 'Armor Class'))![1]);
  const before = armorClass();

  // Two copies: an item with charges is offered again once it is carried.
  for (const copies of [1, 2]) {
    await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^E2E Wand/ })).toBeTruthy());
    await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^E2E Wand/ }));
    await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
    await waitFor(() => expect(within(equipment()).getAllByRole('checkbox', { name: /^Equip E2E Wand/ })).toHaveLength(copies));
  }
  expect(within(equipment()).getAllByText('3 of 3 charges left')).toHaveLength(2); // carried: charges show on Inventory too

  // Equipped but not attuned: nothing applies, and the sheet says so. Attuning applies it once.
  await user.click(within(equipment()).getByRole('checkbox', { name: 'Equip E2E Wand (copy 1 of 2)' }));
  await within(equipment()).findByText('E2E Wand (copy 1 of 2) is not attuned, so its effects do not apply.');
  expect(armorClass()).toBe(before);
  await user.click(within(equipment()).getByRole('checkbox', { name: 'Attuned: E2E Wand (copy 1 of 2)' }));
  await waitFor(() => expect(armorClass()).toBe(before + 2));

  // Separate counts; focus stays on the button that was pressed.
  const spend = () => within(equipment()).getByRole('button', { name: 'Spend a charge: E2E Wand (copy 1 of 2)' });
  await user.click(spend());
  await user.click(spend());
  await waitFor(() => expect(within(equipment()).getByText('1 of 3 charges left')).toBeTruthy());
  expect(within(equipment()).getByText('3 of 3 charges left')).toBeTruthy();
  expect(document.activeElement).toBe(spend());

  // A long rest asks for the 1d3, takes the total as rolled at the table, and returns that many (capped at the two spent).
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const rest = await screen.findByRole('region', { name: 'Long rest' });
  await user.type(await within(rest).findByLabelText('E2E Wand: 1d3 rolled at the table (total)'), '2');
  await user.click(within(rest).getByRole('button', { name: 'Add the 1d3 total: E2E Wand' }));
  expect(await within(rest).findByRole('checkbox', { name: /^E2E Wand charges: 1 → 3/ })).toBeTruthy();
  const finish = within(rest).getByRole('button', { name: 'Finish long rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);

  await waitFor(() => expect(screen.queryByRole('region', { name: 'Long rest' })).toBeNull());
  await openTab(user, hero, 'Gear');
  await waitFor(() => expect(within(equipment()).getAllByText('3 of 3 charges left')).toHaveLength(2));
});

it('adds a companion on Play, damages it, undoes that exactly, and rolls its attack (item 23, D39)', async () => {
  const user = userEvent.setup();
  await client.createCharacter({ name: 'E2E Handler', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [] });
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Handler/ }));
  const hero = await screen.findByRole('article', { name: 'E2E Handler' });
  await openTab(user, hero, 'Play');

  const companions = () => screen.getByRole('region', { name: 'Companions' });
  await user.click(within(companions()).getByRole('button', { name: 'Add a companion' }));
  await user.type(within(companions()).getByRole('textbox', { name: 'Name' }), 'Testy Hound');
  await user.clear(within(companions()).getByRole('spinbutton', { name: 'Hit point maximum' }));
  await user.type(within(companions()).getByRole('spinbutton', { name: 'Hit point maximum' }), '20');
  await user.click(within(companions()).getByRole('button', { name: 'Add an attack line' }));
  await user.type(within(companions()).getByRole('textbox', { name: 'Attack 1 name' }), 'Testy Bite');
  await user.clear(within(companions()).getByRole('spinbutton', { name: 'Attack 1 to hit bonus' }));
  await user.type(within(companions()).getByRole('spinbutton', { name: 'Attack 1 to hit bonus' }), '4');
  await user.type(within(companions()).getByRole('textbox', { name: 'Attack 1 damage (optional)' }), '1d6+2');
  await user.click(within(companions()).getByRole('button', { name: 'Save companion' }));
  await expectStatus(/^Added Testy Hound\.$/);

  // Damage goes through the play command; the card shows it and Undo puts the hit points back exactly.
  const dog = () => screen.getByRole('region', { name: 'Testy Hound' });
  await user.type(within(dog()).getByRole('spinbutton', { name: 'Amount' }), '8');
  await user.click(within(dog()).getByRole('button', { name: /^Take damage/ }));
  await waitFor(() => expect(within(dog()).getByText('Hit points: 12 of 20')).toBeTruthy());
  expect(document.activeElement).toBe(within(dog()).getByRole('button', { name: /^Take damage/ }));
  await user.click(await screen.findByRole('button', { name: 'Undo last change: companion damage 8' }));
  await waitFor(() => expect(within(dog()).getByText('Hit points: 20 of 20')).toBeTruthy());

  // Set temporary hit points, then damage takes them first.
  await user.type(within(dog()).getByRole('spinbutton', { name: 'Amount' }), '5');
  await user.click(within(dog()).getByRole('button', { name: /^Set temporary hit points/ }));
  await waitFor(() => expect(within(dog()).getByText('Hit points: 20 of 20, 5 temporary')).toBeTruthy());
  await user.type(within(dog()).getByRole('spinbutton', { name: 'Amount' }), '7');
  await user.click(within(dog()).getByRole('button', { name: /^Take damage/ }));
  await waitFor(() => expect(within(dog()).getByText('Hit points: 18 of 20')).toBeTruthy());

  // Its attack rolls through the dice engine and shows in Last roll like an attack of your own.
  await user.click(within(dog()).getByRole('button', { name: /^Roll Testy Bite attack/ }));
  const lastRoll = screen.getByRole('status', { name: 'Last roll' });
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Testy Hound: Testy Bite attack: \d+ \(1d20\)/));
  expect(lastRoll.textContent).toMatch(/Testy Bite to hit \+4/);
  await user.click(within(dog()).getByRole('button', { name: /^Roll Testy Bite damage/ }));
  await waitFor(() => expect(lastRoll.textContent).toMatch(/Testy Hound: Testy Bite damage: \d+ \(1d6\+2\)/));
});
