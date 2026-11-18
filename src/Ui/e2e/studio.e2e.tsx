// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell). Only the transport differs from the desktop
// app: HTTP to loopback instead of the WebView2 bridge, so export takes the download fallback instead of the native Save dialog.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { appStatus, createCharacter, expectStatus, fighter, goHome, openMore, openTab, pick, srd, summaryValue } from './helpers';

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

/** Opens the studio on the named homebrew source (item 12 flows). */
async function openStudioOn(user: ReturnType<typeof userEvent.setup>, title: string) {
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: new RegExp(`^${title}`) })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: new RegExp(`^${title}`) }));
  await screen.findByRole('heading', { name: `Content in ${title}` });
}

const newestOf = async (sourceId: string, name: string) => (await client.contentBySource(sourceId)).find((e) => e.name === name)?.latest;

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
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);

  // A homebrew source for 2024 rules.
  const newSource = await screen.findByRole('form', { name: 'New book or notebook' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Title' }), 'E2E Homebrew');
  await user.click(within(newSource).getByRole('checkbox', { name: '2024 rules' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create' }));
  await screen.findByRole('heading', { name: 'Content in E2E Homebrew' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(editor()).getByRole('group', { name });
  async function publish(name: string) {
    await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
    await waitFor(() => expect(appStatus().textContent).toBe(`Made ${name} available to characters.`));
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
  const offered = within(editor()).getByRole('combobox', { name: 'Subclass of' });
  await waitFor(() => expect(within(offered).getByRole('option', { name: /^Barbarian: Level 3: Barbarian Subclass/ })).toBeTruthy());
  await user.selectOptions(offered, within(offered).getByRole('option', { name: /^Barbarian: Level 3: Barbarian Subclass/ }));
  await user.click(within(editor()).getByRole('button', { name: 'Add modifier' })); // default: Initiative +1
  await user.click(within(editor()).getByRole('button', { name: 'Grant a feature' }));
  await user.selectOptions(within(rule(/^Rule 2: Granted feature/)).getByRole('combobox', { name: 'Feature' }), 'E2E Storm Ward (published)');
  await user.click(within(editor()).getByRole('button', { name: 'Grant a feature' }));
  await user.selectOptions(within(rule(/^Rule 3: Granted feature/)).getByRole('combobox', { name: 'Feature' }), 'E2E Sky Lore (published)');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect(await within(editor()).findByText('No problems found. It can be made available to characters.')).toBeTruthy();
  await publish('Path of the E2E Storm');

  // Play it: the homebrew subclass is an option of the SRD choice.
  await user.click(screen.getByRole('button', { name: /^E2E Storm/ }));
  let sheet = await screen.findByRole('article', { name: 'E2E Storm' });
  await user.click(within(sheet).getByRole('button', { name: 'Choose now' }));
  await pick(user, /^Barbarian: choose 1/, /^Path of the E2E Storm/);
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));
  sheet = await screen.findByRole('article', { name: 'E2E Storm' });
  await openTab(user, sheet, 'Skills and saves');
  expect(within(sheet).getByRole('heading', { name: /^Initiative: \+2/ })).toBeTruthy(); // Dex +1, homebrew +1
  await openTab(user, sheet, 'Play');
  const resources = within(sheet).getByRole('region', { name: 'Resources' });
  expect(within(resources).getByRole('heading', { name: 'Storm charges: 2 of 2' })).toBeTruthy();
  await openTab(user, sheet, 'Features');
  expect(within(within(sheet).getByRole('region', { name: 'Features' })).getByText('E2E Sky Lore').closest('li')!.textContent).toMatch(/Text only/);
  await openTab(user, sheet, 'Play');
  await user.click(within(sheet).getByRole('button', { name: 'Roll Storm bolt (1d8 + 2)' }));
  await user.click(await screen.findByRole('button', { name: 'Spend 1 Storm charges (2 left)' }));
  await waitFor(() => expect(within(screen.getByRole('region', { name: 'Resources' })).getByRole('heading', { name: 'Storm charges: 1 of 2' })).toBeTruthy());

  // Republish with +3 initiative: the character keeps +2 until the reviewed update is applied.
  await user.click(screen.getByRole('button', { name: 'Homebrew' }));
  // The studio opens on the first source, and the other e2e file creates its own source in the same DevHost: pick ours.
  const ownSource = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await user.selectOptions(ownSource, within(ownSource).getByRole('option', { name: /^E2E Homebrew/ }));
  await user.click(await screen.findByRole('button', { name: 'Edit Path of the E2E Storm' }));
  const modifier = rule(/^Rule 1: Modifier/);
  await user.clear(within(modifier).getByRole('textbox', { name: /^Value/ }));
  await user.type(within(modifier).getByRole('textbox', { name: /^Value/ }), '3');
  await publish('Path of the E2E Storm');
  expect(await screen.findByRole('button', { name: 'Review update for E2E Storm' })).toBeTruthy();

  // M3 C7: the sheet offers the new revision too, and nothing changes until the reviewed update is applied.
  await user.click(screen.getByRole('button', { name: /^E2E Storm/ }));
  sheet = await screen.findByRole('article', { name: 'E2E Storm' });
  await openTab(user, sheet, 'Skills and saves');
  expect(within(sheet).getByRole('heading', { name: /^Initiative: \+2/ })).toBeTruthy();
  await openMore(user, sheet, 'Updates');
  const updates = await within(sheet).findByRole('region', { name: 'Updates available' });
  // The setup pins the M1 SRD Barbarian revision, so the newer bundled revision is offered as well.
  expect(within(updates).getByRole('button', { name: 'Review update: Barbarian' }).closest('li')!.textContent).toMatch(/bundled/);
  expect(within(updates).getByRole('button', { name: 'Review update: Path of the E2E Storm' }).closest('li')!.textContent).toMatch(/your source/);
  await user.click(within(updates).getByRole('button', { name: 'Review update: Path of the E2E Storm' }));
  const review = await within(updates).findByRole('region', { name: 'Update E2E Storm: Path of the E2E Storm' });
  const values = await within(review).findByRole('table', { name: 'Calculated values that change' });
  expect(values.textContent).toMatch(/Initiative24/);
  await user.click(within(review).getByRole('button', { name: 'Apply update' }));
  await waitFor(() => expect(appStatus().textContent).toMatch(/Updated E2E Storm: Path of the E2E Storm/));
  await waitFor(() => expect(summaryValue(screen.getByRole('article', { name: 'E2E Storm' }), 'Initiative')).toBe('+4'));
  const remaining = () => within(within(screen.getByRole('article', { name: 'E2E Storm' })).getByRole('region', { name: 'Updates available' }));
  await waitFor(() => expect(remaining().queryByRole('button', { name: 'Review update: Path of the E2E Storm' })).toBeNull());
  expect(remaining().getByRole('button', { name: 'Review update: Barbarian' })).toBeTruthy(); // still only offered

  // Import Lab (D62 L1): the studio's Coverage view for this source counts what the engine does with the rules just authored.
  await user.click(screen.getByRole('button', { name: 'Homebrew' }));
  const coverageSource = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await user.selectOptions(coverageSource, within(coverageSource).getByRole('option', { name: /^E2E Homebrew/ }));
  const coverage = await screen.findByRole('region', { name: 'Coverage' });
  const totals = await within(coverage).findByText(/^\d+ automatic · \d+ by hand \(assisted\) · \d+ reference · \d+ not understood$/);
  expect(totals.textContent).toMatch(/ · 0 not understood$/);
  expect(within(coverage).getByText(/E2E Sky Lore/).closest('li')!.textContent).toContain('1 reference');
  expect(within(coverage).getByText(/E2E Storm Ward/).closest('li')!.textContent).toMatch(/^E2E Storm Ward: 2 automatic · 1 by hand \(assisted\) · 0 reference/); // resource and recovery; the roll that spends it is assisted
  expect(coverage.querySelector('[role="status"], [role="alert"], [aria-live], output')).toBeNull(); // no live region of its own

  // Import Lab (D62 L2): a class of this source opens with its 20-row progression grid once a draft is saved.
  await user.click(screen.getByRole('button', { name: 'New class' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Warden');
  expect(within(editor()).getByText('Save a draft of this class to see its progression.')).toBeTruthy();
  await user.click(within(editor()).getByRole('button', { name: 'Save draft' }));
  const progression = await within(editor()).findByRole('region', { name: 'Class progression, scrollable' });
  await waitFor(() => expect(within(within(progression).getByRole('table', { name: 'Class progression' })).getAllByRole('row')).toHaveLength(21)); // header + 20 levels
  expect(within(editor()).getByText('Draft: not yet published')).toBeTruthy();
});

it('authors a +1 weapon in the studio, equips it and rolls it, and "Why?" names the weapon bonus (MECH-X item 0)', async () => {
  const user = userEvent.setup();
  await client.createCharacter({ name: 'E2E Plus Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 14, dex: 12, con: 12, int: 10, wis: 10, cha: 10 }, pins: [] });
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);

  const newSource = await screen.findByRole('form', { name: 'New book or notebook' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Title' }), 'E2E Plus Book');
  await user.click(within(newSource).getByRole('checkbox', { name: '2024 rules' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create' }));
  await screen.findByRole('heading', { name: 'Content in E2E Plus Book' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New item' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Plus Blade');
  await user.click(within(editor()).getByRole('button', { name: 'Add weapon' }));
  const weapon = within(within(editor()).getByRole('group', { name: /^Rule 1: Weapon/ }));
  await user.type(weapon.getByRole('textbox', { name: /^Weapon key/ }), 'blade');
  await user.clear(weapon.getByRole('textbox', { name: /^Damage \(/ }));
  await user.type(weapon.getByRole('textbox', { name: /^Damage \(/ }), '1d6');
  await user.clear(weapon.getByRole('textbox', { name: /^Damage type/ }));
  await user.type(weapon.getByRole('textbox', { name: /^Damage type/ }), 'slashing');
  await user.type(weapon.getByRole('textbox', { name: /^Bonus/ }), '1');
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Made E2E Plus Blade available to characters.'));

  // Carry and equip it; both rolls name the weapon bonus under "Why?".
  await user.click(screen.getByRole('button', { name: /^E2E Plus Hero/ }));
  const hero = await screen.findByRole('article', { name: 'E2E Plus Hero' });
  await openTab(user, hero, 'Gear');
  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^E2E Plus Blade/ })).toBeTruthy());
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^E2E Plus Blade/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip E2E Plus Blade' }));

  await openTab(user, hero, 'Play');
  const actions = () => screen.getByRole('region', { name: 'Attacks and actions' });
  const whyList = async () => {
    const why = within(screen.getByRole('region', { name: 'Status' })).getByText('Why?').closest('details')!;
    if (!why.open) await user.click(within(why).getByText('Why?'));
    return within(why).getByRole('list', { name: 'Why this roll came out this way' }).textContent ?? '';
  };
  await waitFor(() => expect(within(actions()).getByRole('button', { name: 'Roll E2E Plus Blade attack' })).toBeTruthy());
  await user.click(within(actions()).getByRole('button', { name: 'Roll E2E Plus Blade attack' }));
  await waitFor(() => expect(screen.getByRole('status', { name: 'Last roll' }).textContent).toMatch(/E2E Plus Blade attack: \d+ \(1d20\)/));
  expect(await whyList()).toMatch(/\+1 to hit \(weapon\)/);
  await user.click(within(actions()).getByRole('button', { name: /^Roll E2E Plus Blade damage/ }));
  await waitFor(() => expect(screen.getByRole('status', { name: 'Last roll' }).textContent).toMatch(/E2E Plus Blade damage \(slashing\): \d+/));
  expect(await whyList()).toMatch(/\+1 damage \(weapon\)/);
});

it('item mods: a published mod attached to one copy changes only that copy\'s attack (MECH-X item 3)', async () => {
  const user = userEvent.setup();
  await client.createCharacter({ name: 'E2E Mod Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 14, dex: 12, con: 12, int: 10, wis: 10, cha: 10 }, pins: [] });
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);

  const newSource = await screen.findByRole('form', { name: 'New book or notebook' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Title' }), 'E2E Mod Book');
  await user.click(within(newSource).getByRole('checkbox', { name: '2024 rules' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create' }));
  await screen.findByRole('heading', { name: 'Content in E2E Mod Book' });

  // The host: a plain blade. The mod: item content with an item mod effect (applies to a weapon, +2).
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New item' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Mod Blade');
  await user.click(within(editor()).getByRole('button', { name: 'Add weapon' }));
  const weapon = within(within(editor()).getByRole('group', { name: /^Rule 1: Weapon/ }));
  await user.type(weapon.getByRole('textbox', { name: /^Weapon key/ }), 'modblade');
  await user.clear(weapon.getByRole('textbox', { name: /^Damage \(/ }));
  await user.type(weapon.getByRole('textbox', { name: /^Damage \(/ }), '1d6');
  await user.clear(weapon.getByRole('textbox', { name: /^Damage type/ }));
  await user.type(weapon.getByRole('textbox', { name: /^Damage type/ }), 'slashing');
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Made E2E Mod Blade available to characters.'));

  await user.click(screen.getByRole('button', { name: 'New item' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Keen Rune');
  await user.click(within(editor()).getByRole('button', { name: 'Add mod' }));
  const mod = within(within(editor()).getByRole('group', { name: /^Rule 1: Item mod/ }));
  expect((mod.getByRole('combobox', { name: 'Applies to' }) as HTMLSelectElement).value).toBe('weapon');
  await user.clear(mod.getByRole('textbox', { name: /^Bonus/ }));
  await user.type(mod.getByRole('textbox', { name: /^Bonus/ }), '2');
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Made E2E Keen Rune available to characters.'));

  // Carry and equip the blade, then attach the mod to a new copy: one plain copy and one modded copy.
  await user.click(screen.getByRole('button', { name: /^E2E Mod Hero/ }));
  const hero = await screen.findByRole('article', { name: 'E2E Mod Hero' });
  await openTab(user, hero, 'Gear');
  const equipment = () => screen.getByRole('region', { name: 'Equipment' });
  await waitFor(() => expect(within(equipment()).getByRole('option', { name: /^E2E Mod Blade/ })).toBeTruthy());
  await user.selectOptions(within(equipment()).getByRole('combobox', { name: 'Add an item' }), within(equipment()).getByRole('option', { name: /^E2E Mod Blade/ }));
  await user.click(within(equipment()).getByRole('button', { name: 'Add' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip E2E Mod Blade' }));

  const attach = await within(equipment()).findByRole('combobox', { name: 'Attach a mod to E2E Mod Blade' });
  await waitFor(() => expect(within(attach).getAllByRole('option').some((o) => o.textContent?.startsWith('E2E Keen Rune'))).toBe(true));
  await user.selectOptions(attach, within(attach).getAllByRole('option').find((o) => o.textContent?.startsWith('E2E Keen Rune'))!);
  await user.click(within(equipment()).getByRole('button', { name: 'Attach to a new copy of E2E Mod Blade' }));
  await user.click(await within(equipment()).findByRole('checkbox', { name: 'Equip E2E Mod Blade (copy 2 of 2)' }));
  await within(equipment()).findByText(/^Mods on E2E Mod Blade \(copy 2 of 2\): E2E Keen Rune/);

  // Play: two attack rows; only the modded copy carries the +2, and its trace names the mod.
  await openTab(user, hero, 'Play');
  const row = async (copy: number) => {
    const item = await waitFor(() => {
      const found = within(screen.getByRole('region', { name: 'Attacks and actions' })).getAllByRole('listitem').find((li) => li.textContent?.includes(`E2E Mod Blade (copy ${copy} of 2): `));
      expect(found).toBeTruthy();
      return found!;
    });
    return item.textContent ?? '';
  };
  const toHit = (text: string) => Number(/([+-]\d+) to hit/.exec(text)![1]);
  const first = await row(1);
  const second = await row(2);
  expect(toHit(second)).toBe(toHit(first) + 2);
  expect(second).toMatch(/E2E Keen Rune: \+2 to hit \(mod\)/);
  expect(first).not.toMatch(/E2E Keen Rune/);
});

it('content companions: a published feature\'s companion is a card with its own pool (MECH-X item 4)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Bonded Book', ['srd-5.2.1']);
  await openStudioOn(user, 'E2E Bonded Book');

  // One feature: a pool, an attack, and a companion that lists both (the checkboxes name them by label).
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'Fixture Warden Bond');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  const pool = within(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ }));
  await user.type(pool.getByRole('textbox', { name: 'Resource name' }), 'Fixture Charges');
  await user.clear(pool.getByRole('textbox', { name: /^Uses/ }));
  await user.type(pool.getByRole('textbox', { name: /^Uses/ }), '3');
  await user.click(within(editor()).getByRole('button', { name: 'Add attack' }));
  await user.type(within(within(editor()).getByRole('group', { name: /^Rule 2: Attack/ })).getByRole('textbox', { name: 'Attack name' }), 'Fixture Bite');
  await user.click(within(editor()).getByRole('button', { name: 'Add companion' }));
  const companion = within(within(editor()).getByRole('group', { name: /^Rule 3: Companion/ }));
  await user.type(companion.getByRole('textbox', { name: 'Label' }), 'Fixture Warden');
  await user.clear(companion.getByRole('textbox', { name: 'Armor Class formula' }));
  await user.type(companion.getByRole('textbox', { name: 'Armor Class formula' }), '15');
  await user.clear(companion.getByRole('textbox', { name: 'Hit point maximum formula' }));
  await user.type(companion.getByRole('textbox', { name: 'Hit point maximum formula' }), '20');
  await user.click(companion.getByRole('checkbox', { name: 'Fixture Bite' }));
  await user.click(companion.getByRole('checkbox', { name: 'Fixture Charges' }));
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Made Fixture Warden Bond available to characters.'));

  // The server stored a v12 revision with the companion and both lists.
  const published = (await newestOf(source.id, 'Fixture Warden Bond'))!;
  expect(published.schemaVersion).toBe(12);
  expect(published.effects.find((e) => e.type === 'companion')).toMatchObject({ label: 'Fixture Warden', armorClass: '15', hitPointMaximum: '20' });

  await client.createCharacter({ name: 'E2E Bonded Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 14, dex: 12, con: 12, int: 10, wis: 10, cha: 10 }, pins: [published] });
  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Bonded Hero/ }));
  const hero = await screen.findByRole('article', { name: 'E2E Bonded Hero' });
  await openTab(user, hero, 'Play');

  // The card shows Armor Class and hit points, calculated (no Edit or Remove), with its attack and pool inside it.
  const card = () => screen.getByRole('region', { name: 'Fixture Warden' });
  await waitFor(() => expect(within(card()).getByText('Armor Class 15')).toBeTruthy());
  expect(within(card()).getByText('Hit points: 20 of 20')).toBeTruthy();
  expect(within(card()).queryByRole('button', { name: /^Edit / })).toBeNull();
  expect(within(card()).queryByRole('button', { name: /^Remove / })).toBeNull();
  expect(card().textContent).toMatch(/From Fixture Warden Bond/);
  expect(within(card()).getByRole('button', { name: 'Roll Fixture Bite attack' })).toBeTruthy();

  // "Take damage" goes through the play command and lowers the hit points.
  await user.type(within(card()).getByRole('spinbutton', { name: 'Amount' }), '3');
  await user.click(within(card()).getByRole('button', { name: /^Take damage/ }));
  await waitFor(() => expect(within(card()).getByText('Hit points: 17 of 20')).toBeTruthy());

  // "Spend" on its own pool lowers the pool.
  expect(within(card()).getByRole('heading', { name: /^Fixture Charges: 3 of 3/ })).toBeTruthy();
  await user.click(within(card()).getByRole('button', { name: 'Spend 1 Fixture Charges' }));
  await waitFor(() => expect(within(card()).getByRole('heading', { name: /^Fixture Charges: 2 of 3/ })).toBeTruthy());

  // The attack and the pool are drawn on the card only: not in the Actions or Resources panels.
  const attackRolls = screen.queryAllByRole('button', { name: /^Roll Fixture Bite attack/ });
  expect(attackRolls).toHaveLength(1);
  expect(card().contains(attackRolls[0]!)).toBe(true);
  const poolHeadings = screen.queryAllByRole('heading', { name: /^Fixture Charges/ });
  expect(poolHeadings).toHaveLength(1);
  expect(card().contains(poolHeadings[0]!)).toBe(true);
});

it('authors a species with a Strength increase and a skill in the studio, publishes it, and the builder offers it (item 12)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Folk Book', ['srd-5.1']);
  await openStudioOn(user, 'E2E Folk Book');

  await user.click(screen.getByRole('button', { name: 'New species' }));
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Folk');
  await user.type(within(editor()).getByRole('textbox', { name: 'Strength' }), '2');
  await user.click(within(editor()).getByRole('checkbox', { name: 'Athletics' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Speed (feet)' }), '25'); // items 12, 24
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(async () => expect((await newestOf(source.id, 'E2E Folk'))?.status).toBe('published'));
  // The speed is a content v10 replace modifier, so publishing it writes a v10 revision.
  const published = (await newestOf(source.id, 'E2E Folk'))!;
  expect(published.effects.filter((e) => e.type === 'modifier' && e.target === 'speed')).toMatchObject([{ operation: 'replace', value: '25' }]);
  expect(published.schemaVersion).toBe(10);

  await goHome(user);
  await createCharacter(user, { name: 'E2E Folk Child', family: 'srd-5.1', species: /^E2E Folk/ });
  expect(await screen.findByRole('heading', { name: /^Choices/ })).toBeTruthy();
});

it('authors a newer-rules background with three ability increases: the option features are drafts, Publish all makes them live, and the builder offers it (item 12)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Wanderer Book', ['srd-5.2.1']);
  await openStudioOn(user, 'E2E Wanderer Book');

  await user.click(screen.getByRole('button', { name: 'New background' }));
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Wanderer');
  for (const name of ['Strength', 'Dexterity', 'Constitution']) await user.click(within(editor()).getByRole('checkbox', { name }));
  await user.click(within(editor()).getByRole('button', { name: 'Save draft' }));
  await waitFor(async () => expect((await newestOf(source.id, 'E2E Wanderer'))?.status).toBe('draft'));
  const entries = await client.contentBySource(source.id);
  expect(entries.filter((e) => e.kind === 'feature' && e.name.startsWith('E2E Wanderer Ability Scores'))).toHaveLength(7);

  // R61: Publish is not held; a linked note says the entry points at drafts and that Publish all publishes them together.
  const publish = await within(editor()).findByRole('button', { name: 'Make available to characters' });
  await waitFor(() => expect(publish.getAttribute('aria-disabled')).toBeNull());
  const note = document.getElementById('editor-pins');
  expect(note?.textContent).toBe('Points at 7 drafts not yet available to characters. Making all drafts available makes them available together and re-points this entry.');
  expect((publish.getAttribute('aria-describedby') ?? '').split(' ')).toContain('editor-pins');
  await user.click(await screen.findByRole('button', { name: 'Make all drafts in this source available to characters…' }));
  const region = screen.getByRole('region', { name: 'Make drafts of E2E Wanderer Book available to characters' });
  const publishAll = await within(region).findByRole('button', { name: 'Make 8 drafts available to characters' });
  await waitFor(() => expect(publishAll.getAttribute('aria-disabled')).toBeNull());
  await user.click(publishAll);
  await within(region).findByText('Made 8 drafts available to characters.');
  expect((await newestOf(source.id, 'E2E Wanderer'))?.status).toBe('published');
  // The open editor showed the superseded draft: it closes with a status instead of saving over the published version.
  await waitFor(() => expect(screen.queryByRole('region', { name: /^New |^Edit / })).toBeNull());

  await goHome(user);
  await createCharacter(user, { name: 'E2E Wanderer Child', family: 'srd-5.2.1', background: /^E2E Wanderer/ });
  expect(await screen.findByRole('heading', { name: /^Choices/ })).toBeTruthy();
});

it('authors a cantrip on a new spell list in the studio, publishes it, and the list shows in the spell list keys (item 12)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Spark Book', ['srd-5.2.1']);
  await openStudioOn(user, 'E2E Spark Book');

  await user.click(screen.getByRole('button', { name: 'New spell' }));
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Spark');
  await user.selectOptions(within(editor()).getByRole('combobox', { name: 'Level' }), '0');
  await user.type(within(editor()).getByRole('combobox', { name: 'Add a spell list' }), 'E2E Spark List');
  expect(within(editor()).getByText('Saved as: e2e-spark-list')).toBeTruthy();
  await user.click(within(editor()).getByRole('button', { name: 'Add list' }));
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(async () => expect((await newestOf(source.id, 'E2E Spark'))?.status).toBe('published'));

  const keys = await client.spellListKeys();
  expect(keys.find((k) => k.key === 'e2e-spark-list')?.spells).toBe(1);
});

it('keeps a resource linked to its recovery when the resource is renamed in the studio', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Renames', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
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
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);

  const newSource = await screen.findByRole('form', { name: 'New book or notebook' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Title' }), 'E2E Warden Homebrew');
  await user.click(within(newSource).getByRole('checkbox', { name: '2024 rules' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create' }));
  await screen.findByRole('heading', { name: 'Content in E2E Warden Homebrew' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(editor()).getByRole('group', { name });
  async function publish(name: string) {
    await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
    await waitFor(() => expect(appStatus().textContent).toBe(`Made ${name} available to characters.`));
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
  const offered = within(editor()).getByRole('combobox', { name: 'Subclass of' });
  await waitFor(() => expect(within(offered).getByRole('option', { name: /^Fighter: Level 3: Fighter Subclass/ })).toBeTruthy());
  await user.selectOptions(offered, within(offered).getByRole('option', { name: /^Fighter: Level 3: Fighter Subclass/ }));
  await user.click(within(editor()).getByRole('button', { name: 'Add modifier' })); // default: Initiative +1
  await user.click(within(editor()).getByRole('button', { name: 'Grant a feature' }));
  await user.selectOptions(within(rule(/^Rule 2: Granted feature/)).getByRole('combobox', { name: 'Feature' }), 'E2E Star Shield (published)');
  await publish('E2E Starward Warden');

  // Choose it on the sheet, next to the SRD Champion, then play.
  await user.click(screen.getByRole('button', { name: /^E2E Warden/ }));
  let sheet = await screen.findByRole('article', { name: 'E2E Warden' });
  await user.click(within(sheet).getByRole('button', { name: 'Choose now' }));
  const subclass = await screen.findByRole('group', { name: /^Fighter: choose 1/ });
  expect(await within(subclass).findByRole('checkbox', { name: /^Champion/ })).toBeTruthy();
  await pick(user, /^Fighter: choose 1/, /^E2E Starward Warden/);
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));

  sheet = await screen.findByRole('article', { name: 'E2E Warden' });
  await openTab(user, sheet, 'Skills and saves');
  expect(within(sheet).getByRole('heading', { name: /^Initiative: \+3/ })).toBeTruthy(); // Dex +2, homebrew +1
  expect(within(sheet).getByRole('heading', { name: /^Attacks per Attack action: 1/ })).toBeTruthy(); // Extra Attack comes at 5
  await openTab(user, sheet, 'Play');
  const resources = () => within(screen.getByRole('article', { name: 'E2E Warden' })).getByRole('region', { name: 'Resources' });
  expect(within(resources()).getByRole('heading', { name: 'Star charges: 2 of 2' })).toBeTruthy();
  expect(within(resources()).getByRole('heading', { name: 'Second Wind: 2 of 2' })).toBeTruthy();
  expect(within(resources()).getByRole('heading', { name: 'Action Surge: 1 of 1' })).toBeTruthy();

  // Second Wind rolls 1d10 plus the Fighter level; spending it is a separate, confirmed press.
  await user.click(within(sheet).getByRole('button', { name: 'Roll Second Wind healing (1d10 + 3)' }));
  const lastRoll = screen.getByRole('status', { name: 'Last roll' });
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
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const newSource = await screen.findByRole('form', { name: 'New book or notebook' });
  await user.type(within(newSource).getByRole('textbox', { name: 'Title' }), 'E2E Chronicle Homebrew');
  await user.click(within(newSource).getByRole('checkbox', { name: '2024 rules' }));
  await user.click(within(newSource).getByRole('button', { name: 'Create' }));
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
  // D61: the formula's preview, worked out by the real service against this draft (proficiency bonus 6 at level 20).
  await user.type(within(ink).getByRole('textbox', { name: /^Uses/ }), 'PB');
  expect((await within(ink).findByText(/Level 20: 6/)).textContent).toMatch(/^Level 1: 2 · .* · Level 20: 6$/);
  expect(within(ink).getByRole('textbox', { name: /^Uses/ }).getAttribute('aria-describedby')).toMatch(/^rule-resource-\d+-maximum-preview$/);
  await user.clear(within(ink).getByRole('textbox', { name: /^Uses/ }));
  await user.type(within(ink).getByRole('textbox', { name: /^Uses/ }), 'SCALE.ink');
  expect(await within(ink).findByText(/Level 1: 2 · .* · Level 20: 9$/)).toBeTruthy(); // the draft's own column
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

  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Made E2E Chronicler available to characters.'));

  // A homebrew subclass joins the new class's subclass choice, which declares no options of its own.
  await user.click(screen.getByRole('button', { name: 'New subclass' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Order of Quills');
  const offered = within(editor()).getByRole('combobox', { name: 'Subclass of' });
  await waitFor(() => expect(within(offered).getByRole('option', { name: 'E2E Chronicler: Choose a subclass' })).toBeTruthy());
  await user.selectOptions(offered, 'E2E Chronicler: Choose a subclass');
  await user.click(within(editor()).getByRole('button', { name: 'Add modifier' })); // default: Initiative +1
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Made E2E Order of Quills available to characters.'));

  // Level 1 in the builder: the class is offered like any other, with its skill choice.
  await createCharacter(user, { name: 'E2E Scribe', family: 'srd-5.2.1', scores: { Intelligence: 16 }, cls: /^E2E Chronicler/ });
  await pick(user, /^Choose 2 skills/, /^E2E Chronicler: History/);
  await pick(user, /^Choose 2 skills/, /^E2E Chronicler: Arcana/);
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  let sheet = await screen.findByRole('article', { name: 'E2E Scribe' });
  await openTab(user, sheet, 'Skills and saves');
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

  await user.click(await screen.findByRole('button', { name: /^E2E Scribe 20 / }));
  sheet = await screen.findByRole('article', { name: 'E2E Scribe 20' });
  await openTab(user, sheet, 'Skills and saves');
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
