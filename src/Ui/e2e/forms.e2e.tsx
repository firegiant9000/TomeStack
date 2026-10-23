// D43 slice 1 against the real DevHost: form groups authored in the studio, the form picker on Play, and a refused switch.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';

vi.mock('../src/api/client', async (importOriginal) => {
  const { inject } = await import('vitest');
  const { createHttpTransport } = await import('../src/api/transport');
  const original = await importOriginal<typeof import('../src/api/client')>();
  const { endpoint, token } = inject('devHost');
  return { ...original, client: original.createClient(createHttpTransport(endpoint, { 'X-TomeStack-Token': token })) };
});

afterEach(cleanup);

/** Waits until the status line says `text` (the line stays on screen, so a plain find would see the previous message). */
async function expectStatus(text: RegExp): Promise<HTMLElement> {
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(text));
  return screen.getByRole('status');
}

/** Activates one tab of a character sheet (ADR-014); inactive panels are hidden. A no-op when it is already selected. */
async function openTab(user: ReturnType<typeof userEvent.setup>, sheet: HTMLElement, name: string): Promise<void> {
  const tab = within(sheet).getByRole('tab', { name });
  if (tab.getAttribute('aria-selected') !== 'true') await user.click(tab);
}

it('authors a form group and two forms in the studio, switches forms, refuses a switch without enough pool, undoes the switch, and the pool is back', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Forms Book', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Forms Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Forms Book/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Forms Book' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(within(editor()).getByRole('group', { name }));

  // A pool of 7 Focus, a group, and two forms that cost 3 each: two switches fit, a third does not.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Forms');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(rule(/^Rule 1: Resource/).getByRole('textbox', { name: 'Resource name' }), 'Focus');
  const uses = rule(/^Rule 1: Resource/).getByRole('textbox', { name: /^Uses/ });
  await user.clear(uses);
  await user.type(uses, '7');
  await user.click(within(editor()).getByRole('button', { name: 'Add form group' }));
  await user.type(rule(/^Rule 2: Form group/).getByRole('textbox', { name: /^Group name/ }), 'Forms');
  for (const [number, name] of [
    [3, 'E2E Tide'],
    [4, 'E2E Ember'],
  ] as const) {
    await user.click(within(editor()).getByRole('button', { name: 'Add toggle' }));
    const toggle = rule(new RegExp(`^Rule ${number}: Toggle`));
    await user.type(toggle.getByRole('textbox', { name: 'Toggle name' }), name);
    await user.selectOptions(toggle.getByRole('combobox', { name: 'Turning it on spends' }), 'One use of Focus');
    await user.type(toggle.getByRole('textbox', { name: /^Cost/ }), '3');
    await user.selectOptions(toggle.getByRole('combobox', { name: 'Form group' }), 'Forms');
  }
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await expectStatus(/Published E2E Forms/);

  const published = (await client.contentBySource(source.id)).find((e) => e.name === 'E2E Forms')?.latestPublished;
  expect(published).toBeTruthy();
  await client.createCharacter({
    name: 'E2E Forms Hero',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
    pins: [published!],
  });

  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Forms Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Forms Hero' });
  await openTab(user, sheet, 'Play');
  // D43 S3: the form picker lives in the combat strip, above the Play columns.
  const combat = () => screen.getByRole('region', { name: 'Combat' });
  // The group is named by the label authored in the studio (the sheet's groupLabel).
  const picker = () => within(combat()).getByRole('radiogroup', { name: 'Forms' });
  const form = (name: RegExp) => within(picker()).getByRole('radio', { name });
  const resources = () => screen.getByRole('region', { name: 'Resources' });

  // Base is chosen at first; each form names its cost and the pool it comes from.
  expect(within(picker()).getByRole('radio', { name: 'Base' }).getAttribute('aria-checked')).toBe('true');
  expect(form(/^E2E Tide.*3 Focus/)).toBeTruthy();

  await user.click(form(/^E2E Tide/));
  await waitFor(() => expect(form(/^E2E Tide/).getAttribute('aria-checked')).toBe('true'));
  expect(within(resources()).getByRole('heading', { name: 'Focus: 4 of 7' })).toBeTruthy();

  // Choosing the other form switches the first off and the second on as one change; the second spends its own cost.
  await user.click(form(/^E2E Ember/));
  await waitFor(() => expect(form(/^E2E Ember/).getAttribute('aria-checked')).toBe('true'));
  expect(form(/^E2E Tide/).getAttribute('aria-checked')).toBe('false');
  expect(within(resources()).getByRole('heading', { name: 'Focus: 1 of 7' })).toBeTruthy();

  // 1 Focus is left and Tide costs 3: the switch is refused and nothing changes.
  await user.click(form(/^E2E Tide/));
  expect((await screen.findByRole('alert')).textContent).toMatch(/1 of 7 left/);
  expect(form(/^E2E Ember/).getAttribute('aria-checked')).toBe('true');
  expect(form(/^E2E Tide/).getAttribute('aria-checked')).toBe('false');
  expect(within(resources()).getByRole('heading', { name: 'Focus: 1 of 7' })).toBeTruthy();

  // D44: the switch to Ember is undone by the service (the refused switch wrote nothing, so the offer stands): Tide is back and the
  // pool with it, exactly as before the switch.
  await user.click(await screen.findByRole('button', { name: 'Undo switch to E2E Ember' }));
  await waitFor(() => expect(form(/^E2E Tide/).getAttribute('aria-checked')).toBe('true'));
  expect(form(/^E2E Ember/).getAttribute('aria-checked')).toBe('false');
  expect(within(resources()).getByRole('heading', { name: 'Focus: 4 of 7' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: /^Undo switch/ })).toBeNull(); // one level: nothing left to undo
});

it('authors a stance with upkeep in the studio, starts an encounter, takes turns until the pool is short, chooses End in the turn preview, undoes the turn, and ends the encounter', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Upkeep Book', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Upkeep Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Upkeep Book/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Upkeep Book' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(within(editor()).getByRole('group', { name }));

  // A pool of 7 Reserve that the stance spends 3 of each turn, and a pool of 5 Strain that counts up by 1 each turn, stance or not.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Stance Feature');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(rule(/^Rule 1: Resource/).getByRole('textbox', { name: 'Resource name' }), 'Reserve');
  const reserveUses = rule(/^Rule 1: Resource/).getByRole('textbox', { name: /^Uses/ });
  await user.clear(reserveUses);
  await user.type(reserveUses, '7');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(rule(/^Rule 2: Resource/).getByRole('textbox', { name: 'Resource name' }), 'Strain');
  const strainUses = rule(/^Rule 2: Resource/).getByRole('textbox', { name: /^Uses/ });
  await user.clear(strainUses);
  await user.type(strainUses, '5');
  await user.click(rule(/^Rule 2: Resource/).getByRole('checkbox', { name: /^Counts up/ }));
  await user.type(rule(/^Rule 2: Resource/).getByRole('textbox', { name: /^When full/ }), 'E2E strain is at its limit');
  await user.click(within(editor()).getByRole('button', { name: 'Add toggle' }));
  await user.type(rule(/^Rule 3: Toggle/).getByRole('textbox', { name: 'Toggle name' }), 'E2E Stance');
  await user.click(within(editor()).getByRole('button', { name: 'Add upkeep' }));
  await user.selectOptions(rule(/^Rule 4: Upkeep/).getByRole('combobox', { name: 'Toggle' }), 'While E2E Stance is on');
  await user.selectOptions(rule(/^Rule 4: Upkeep/).getByRole('combobox', { name: 'Pool' }), 'Reserve');
  const spend = rule(/^Rule 4: Upkeep/).getByRole('textbox', { name: /^Amount/ });
  await user.clear(spend);
  await user.type(spend, '3');
  await user.click(within(editor()).getByRole('button', { name: 'Add upkeep' }));
  await user.selectOptions(rule(/^Rule 5: Upkeep/).getByRole('combobox', { name: 'Pool' }), 'Strain');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await expectStatus(/Published E2E Stance Feature/);

  const published = (await client.contentBySource(source.id)).find((e) => e.name === 'E2E Stance Feature')?.latestPublished;
  expect(published).toBeTruthy();
  await client.createCharacter({
    name: 'E2E Upkeep Hero',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
    pins: [published!],
  });

  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Upkeep Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Upkeep Hero' });
  await openTab(user, sheet, 'Play');
  const combat = () => screen.getByRole('region', { name: 'Combat' });
  const resources = () => screen.getByRole('region', { name: 'Resources' });
  const stance = () => screen.getByRole<HTMLInputElement>('checkbox', { name: /^E2E Stance/ });
  const takeTurn = async (opens: string, panel: string, confirm: string) => {
    await user.click(within(combat()).getByRole('button', { name: opens }));
    await screen.findByRole('heading', { name: panel });
    const button = await screen.findByRole('button', { name: confirm });
    await waitFor(() => expect(button.getAttribute('aria-disabled')).not.toBe('true')); // Confirm waits for the preview of the selection
    return button;
  };

  expect(within(combat()).getByText('No encounter running')).toBeTruthy();
  await user.click(stance());
  await waitFor(() => expect(stance().checked).toBe(true));
  await user.click(within(combat()).getByRole('button', { name: 'Start encounter' }));
  await within(combat()).findByRole('button', { name: 'End encounter' });

  // Two turns fit: 3 of 7 Reserve each turn, 1 built up of Strain.
  await user.click(await takeTurn('Start my turn', 'Start of turn', 'Confirm start of turn'));
  // Round 0 already reads "Round 1", so wait for the pool the turn changed instead.
  await within(resources()).findByRole('heading', { name: 'Reserve: 4 of 7' });
  await within(resources()).findByRole('heading', { name: 'Strain: built up 1 of 5' });
  expect(within(combat()).getByText('Round 1')).toBeTruthy();
  await user.click(await takeTurn('Start my turn', 'Start of turn', 'Confirm start of turn'));
  await within(combat()).findByText('Round 2');
  await within(resources()).findByRole('heading', { name: 'Reserve: 1 of 7' });
  await within(combat()).findByText('Strain: built up 2 of 5');

  // 1 Reserve is left and the stance needs 3: the preview asks, with End already chosen. It is accepted without a click on it.
  const third = await takeTurn('Start my turn', 'Start of turn', 'Confirm start of turn');
  const choice = screen.getByRole('radiogroup', { name: /^E2E Stance needs 3, has 1/ });
  expect(within(choice).getByRole<HTMLInputElement>('radio', { name: 'End E2E Stance' }).checked).toBe(true);
  expect(within(choice).getByRole<HTMLInputElement>('radio', { name: 'Keep it on' }).checked).toBe(false);
  await user.click(third);
  await within(combat()).findByText('Round 3');
  await waitFor(() => expect(stance().checked).toBe(false));
  expect(within(resources()).getByRole('heading', { name: 'Reserve: 1 of 7' })).toBeTruthy();
  expect(within(resources()).getByRole('heading', { name: 'Strain: built up 3 of 5' })).toBeTruthy();

  // The service undoes the whole turn: the stance is on again, the round and both pools are as they were.
  await user.click(await screen.findByRole('button', { name: 'Undo turn start' }));
  await waitFor(() => expect(stance().checked).toBe(true));
  await within(combat()).findByText('Round 2');
  expect(within(resources()).getByRole('heading', { name: 'Reserve: 1 of 7' })).toBeTruthy();
  expect(within(resources()).getByRole('heading', { name: 'Strain: built up 2 of 5' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: /^Undo turn/ })).toBeNull(); // one level: nothing left to undo

  await user.click(within(combat()).getByRole('button', { name: 'End encounter' }));
  await within(combat()).findByText('No encounter running');
  expect(stance().checked).toBe(true); // the stance has no duration, so ending the encounter leaves it on
});

it('authors a feature attack and a toggled rider, rolls damage in and out of the form', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Attack Book', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Attack Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Attack Book/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Attack Book' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(within(editor()).getByRole('group', { name }));

  // A mode, an attack tagged "blaze", and a rider on the attacks tagged "blaze" that applies only while the mode is on.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Blaze Feature');
  await user.click(within(editor()).getByRole('button', { name: 'Add toggle' }));
  await user.type(rule(/^Rule 1: Toggle/).getByRole('textbox', { name: 'Toggle name' }), 'E2E Blaze Mode');
  await user.click(within(editor()).getByRole('button', { name: 'Add attack' }));
  await user.type(rule(/^Rule 2: Attack/).getByRole('textbox', { name: 'Attack name' }), 'E2E Blaze Strike');
  await user.type(rule(/^Rule 2: Attack/).getByRole('textbox', { name: /^Tags/ }), 'blaze');
  await user.click(within(editor()).getByRole('button', { name: 'Add attack rider' }));
  const rider = rule(/^Rule 3: Attack rider/);
  const tags = rider.getByRole('textbox', { name: /^Tags/ });
  await user.clear(tags);
  await user.type(tags, 'blaze');
  const dice = rider.getByRole('textbox', { name: /^Extra damage dice/ });
  await user.clear(dice);
  await user.type(dice, '2d8');
  await user.selectOptions(rider.getByRole('combobox', { name: 'Applies' }), 'While E2E Blaze Mode is on');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await expectStatus(/Published E2E Blaze Feature/);

  const published = (await client.contentBySource(source.id)).find((e) => e.name === 'E2E Blaze Feature')?.latestPublished;
  expect(published).toBeTruthy();
  await client.createCharacter({
    name: 'E2E Blaze Hero',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
    pins: [published!],
  });

  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Blaze Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Blaze Hero' });
  await openTab(user, sheet, 'Play');
  const lastRoll = () => screen.getByRole('region', { name: 'Last roll' });
  const mode = () => screen.getByRole<HTMLInputElement>('checkbox', { name: /^E2E Blaze Mode/ });

  // Out of the form: the damage names no rider dice, and the roll shows none.
  const off = await screen.findByRole('button', { name: /^Roll E2E Blaze Strike damage/ });
  expect(off.textContent).not.toMatch(/2d8/);
  await user.click(off);
  await waitFor(() => expect(lastRoll().textContent).toMatch(/E2E Blaze Strike damage/));
  expect(lastRoll().textContent).not.toMatch(/2d8/);

  // In the form: the rider's dice are part of the formula that is shown and rolled.
  await user.click(mode());
  await waitFor(() => expect(mode().checked).toBe(true));
  const on = await screen.findByRole('button', { name: /^Roll E2E Blaze Strike damage.*2d8/ });
  await user.click(on);
  await waitFor(() => expect(lastRoll().textContent).toMatch(/E2E Blaze Strike damage.*2d8/));
});

it('authors a scaling technique with a save in the studio, rolls it with extra points, and the pool goes down by exactly that amount', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Surge Book', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Surge Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Surge Book/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Surge Book' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(within(editor()).getByRole('group', { name }));

  // A pool of 6, and a technique that costs 2, adds 1d6 for each extra point (up to 3 more) and asks a Dexterity save.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Surge Feature');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(rule(/^Rule 1: Resource/).getByRole('textbox', { name: 'Resource name' }), 'E2E Charge');
  const uses = rule(/^Rule 1: Resource/).getByRole('textbox', { name: /^Uses/ });
  await user.clear(uses);
  await user.type(uses, '6');
  await user.click(within(editor()).getByRole('button', { name: 'Add roll or action' }));
  const technique = rule(/^Rule 2: Roll or action/);
  await user.type(technique.getByRole('textbox', { name: 'Roll name' }), 'E2E Surge');
  const dice = technique.getByRole('textbox', { name: /^Dice/ });
  await user.clear(dice);
  await user.type(dice, '2d6');
  await user.selectOptions(technique.getByRole('combobox', { name: 'Uses a resource (a limited-use action)' }), 'E2E Charge');
  await user.type(technique.getByRole('textbox', { name: /^Cost/ }), '2');
  await user.type(technique.getByRole('textbox', { name: /^Extra dice for each extra point/ }), '1d6');
  await user.type(technique.getByRole('textbox', { name: /^Most extra points/ }), '3');
  await user.selectOptions(technique.getByRole('combobox', { name: 'Saving throw' }), 'Dexterity');
  await user.selectOptions(technique.getByRole('combobox', { name: 'Area shape' }), 'Cone');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await expectStatus(/Published E2E Surge Feature/);

  const published = (await client.contentBySource(source.id)).find((e) => e.name === 'E2E Surge Feature')?.latestPublished;
  expect(published).toBeTruthy();
  await client.createCharacter({
    name: 'E2E Surge Hero',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
    pins: [published!],
  });

  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Surge Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Surge Hero' });
  await openTab(user, sheet, 'Play');
  const resources = () => screen.getByRole('region', { name: 'Resources' });
  const lastRoll = () => screen.getByRole('region', { name: 'Last roll' });
  expect(within(resources()).getByRole('heading', { name: 'E2E Charge: 6 of 6' })).toBeTruthy();

  // The action shows its save, and its points box starts at the cost and stops at the cost plus three.
  const roll = await screen.findByRole('button', { name: 'Roll E2E Surge (2d6)' });
  expect(roll.closest('li')!.textContent).toMatch(/Save: DC \d+ Dexterity/);
  const points = screen.getByRole<HTMLInputElement>('spinbutton', { name: /^Points to spend/ });
  expect(points.min).toBe('2');
  expect(points.max).toBe('5');
  await user.clear(points);
  await user.type(points, '4');
  await user.click(roll);
  await waitFor(() => expect(lastRoll().textContent).toMatch(/E2E Surge/));

  // Rolling spends nothing; the record's Spend takes exactly the rolled amount.
  expect(within(resources()).getByRole('heading', { name: 'E2E Charge: 6 of 6' })).toBeTruthy();
  await user.click(await within(lastRoll()).findByRole('button', { name: /^Spend 4 E2E Charge/ }));
  await waitFor(() => expect(within(resources()).getByRole('heading', { name: 'E2E Charge: 2 of 6' })).toBeTruthy());
});

it('a feat with automatic advantage rolls two dice on a Dex save, and turning on a disadvantage form cancels it', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Mode Book', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Mode Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Mode Book/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Mode Book' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(within(editor()).getByRole('group', { name }));

  // A feat with advantage on Dexterity saves.
  await user.click(screen.getByRole('button', { name: 'New feat' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Swift Feat');
  await user.click(within(editor()).getByRole('button', { name: 'Add roll mode' }));
  await user.selectOptions(rule(/^Rule 1: Roll mode/).getByRole('combobox', { name: 'Applies to' }), 'save.dex');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await expectStatus(/Published E2E Swift Feat/);

  // A feature with a form that gives disadvantage on Dexterity saves while it is on. (ability.dex.check would not reach
  // save.dex, so the cancelling form targets the save itself.)
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Heavy Feature');
  await user.click(within(editor()).getByRole('button', { name: 'Add toggle' }));
  await user.type(rule(/^Rule 1: Toggle/).getByRole('textbox', { name: 'Toggle name' }), 'E2E Heavy Form');
  await user.click(within(editor()).getByRole('button', { name: 'Add roll mode' }));
  const heavy = rule(/^Rule 2: Roll mode/);
  await user.selectOptions(heavy.getByRole('combobox', { name: 'Applies to' }), 'save.dex');
  await user.selectOptions(heavy.getByRole('combobox', { name: 'Mode' }), 'disadvantage');
  await user.selectOptions(heavy.getByRole('combobox', { name: 'Only while' }), 'While E2E Heavy Form is on');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await expectStatus(/Published E2E Heavy Feature/);

  const entries = await client.contentBySource(source.id);
  const pins = ['E2E Swift Feat', 'E2E Heavy Feature'].map((name) => entries.find((e) => e.name === name)?.latestPublished);
  expect(pins.every(Boolean)).toBe(true);
  await client.createCharacter({
    name: 'E2E Mode Hero',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
    pins: pins.map((p) => p!),
  });

  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Mode Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Mode Hero' });
  await openTab(user, sheet, 'Stats');
  await user.click(await screen.findByRole('heading', { name: /^Dexterity saving throw: / })); // the card's body is a closed <details>
  const lastRoll = () => screen.getByRole('region', { name: 'Last roll' });
  const diceRolled = () => (lastRoll().textContent!.match(/d20 \d+/g) ?? []).length;

  // Automatic advantage from the feat: the button names it, and the roll uses two d20.
  const advantage = await screen.findByRole('button', { name: /^Roll Dexterity saving throw \(advantage: .*E2E Swift Feat/ });
  await user.click(advantage);
  await waitFor(() => expect(lastRoll().textContent).toMatch(/Dexterity saving throw/));
  expect(diceRolled()).toBe(2);

  // Turn the form on: advantage and disadvantage cancel, and the roll uses one d20.
  await openTab(user, sheet, 'Play');
  const form = await screen.findByRole<HTMLInputElement>('checkbox', { name: /^E2E Heavy Form/ });
  await user.click(form);
  await waitFor(() => expect(form.checked).toBe(true));
  await openTab(user, sheet, 'Stats');
  const cancelled = await screen.findByRole('button', { name: 'Roll Dexterity saving throw (normal: advantage and disadvantage cancel)' });
  await user.click(cancelled);
  await waitFor(() => expect(diceRolled()).toBe(1));
});

