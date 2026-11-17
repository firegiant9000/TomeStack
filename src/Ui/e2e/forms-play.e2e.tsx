// D43 forms and MECH-X items 1 and 2 against the real DevHost: survival and threshold hits, charges, pinned rolls, hit-point rolls and replaceable choices.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';
import type { ContentReference, ContentRevision } from '../src/api/types';
import { expectStatus, openTab } from './helpers';

vi.mock('../src/api/client', async (importOriginal) => {
  const { inject } = await import('vitest');
  const { createHttpTransport } = await import('../src/api/transport');
  const original = await importOriginal<typeof import('../src/api/client')>();
  const { endpoint, token } = inject('devHost');
  return { ...original, client: original.createClient(createHttpTransport(endpoint, { 'X-TomeStack-Token': token })) };
});

afterEach(cleanup);

// ---- D43 S7: damage responses against the real DevHost ----

const fixtureFolder = () => resolve(process.cwd(), '../../tests/RulesFixtures');

/** The content ids of the responses pack, in the order they can be published (the pool and the form come first). */
const responseContentOrder = ['70', '74', '71', '72', '73', '75', '76', '77'].map((n) => `6f0dc000-0000-4000-8000-0000000000${n}`);

let responsesPack: Promise<ContentReference[]> | undefined;

/**
 * Installs the responses fixture pack (`tests/RulesFixtures/fixture-pack-responses.json`) as one homebrew source, because the
 * DevHost seeds only its older fixture packs. A draft gets a new revision id, so the class's grants of the six features (which
 * name their revision ids) are left out and the character pins the features itself, as the fixture character does. Content ids
 * stay, so the features' pool and form references still resolve. Once per run.
 */
function installResponsesPack(): Promise<ContentReference[]> {
  responsesPack ??= publishFixturePack('fixture-pack-responses.json', 'E2E Responses Book', responseContentOrder);
  return responsesPack;
}

/** Publishes a fixture pack's revisions, in the given content order, into a new homebrew source; the class's grants are left out. */
async function publishFixturePack(file: string, title: string, order: string[]): Promise<ContentReference[]> {
  const pack = JSON.parse(readFileSync(resolve(fixtureFolder(), file), 'utf8')) as { revisions: ContentRevision[] };
  const source = await client.createHomebrewSource(title, ['srd-5.2.1']);
  const published: ContentReference[] = [];
  for (const contentId of order) {
    const revision = pack.revisions.find((r) => r.contentId === contentId)!;
    // A choice's options name revision ids that a draft does not keep either, so the class's choices are left out too.
    const effects = revision.kind === 'class' ? revision.effects.filter((e) => e.type !== 'grant' && e.type !== 'choice') : revision.effects;
    const draft = await client.saveDraft({ ...revision, status: 'draft', effects, provenance: { ...revision.provenance, sourceId: source.id } });
    const result = await client.publish(draft);
    expect(result.report.errors).toEqual([]);
    published.push(result.published);
  }
  return published;
}

/** A level 8 Fixture Bulwark with the fixture character's scores, the six response features and the guard form pinned. */
async function createBulwark(name: string): Promise<string> {
  const pins = await installResponsesPack();
  const fixture = JSON.parse(readFileSync(resolve(fixtureFolder(), 'characters/pm-bulwark-l8.json'), 'utf8')) as { baseAbilities: Record<string, number> };
  const created = await client.createCharacter({
    name,
    rulesFamily: 'srd-5.2.1',
    baseAbilities: fixture.baseAbilities as never,
    pins,
    classes: [{ class: pins[0]!, level: 8 }],
  });
  return created.character.id;
}

async function openBulwark(user: ReturnType<typeof userEvent.setup>, name: string): Promise<HTMLElement> {
  render(<App />);
  await user.click(await screen.findByRole('button', { name: new RegExp(`^${name}`) }));
  const sheet = await screen.findByRole('article', { name });
  await openTab(user, sheet, 'Play');
  return sheet;
}

const hitPointsOf = async (id: string) => (await client.getCharacter(id)).sheet.hitPoints!;

it('a hit that would drop the character to 0 offers survival; choosing it leaves 1 HP and Undo restores the exact state', async () => {
  const user = userEvent.setup();
  const id = await createBulwark('E2E Bulwark Stand');
  const { maximum } = await hitPointsOf(id);
  await client.play(id, { action: 'setHitPoints', amount: 5 });
  const before = (await client.getCharacter(id)).character.play;
  await openBulwark(user, 'E2E Bulwark Stand');

  await user.type(await screen.findByRole('spinbutton', { name: 'Amount' }), '20');
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  const group = await screen.findByRole('radiogroup', { name: 'Survive at 0 hit points' });
  // The survival that needs the surge form is listed but cannot be chosen, and says why; the others are open.
  const stand = within(group).getByRole<HTMLInputElement>('radio', { name: /^Last stand/ });
  expect(within(group).getByRole<HTMLInputElement>('radio', { name: /^Endure/ }).disabled).toBe(true);
  expect(within(group).getByRole<HTMLInputElement>('radio', { name: 'None' }).checked).toBe(true);
  await user.click(stand);
  await screen.findByText(`Hit points after: 1 of ${maximum}`);
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await screen.findByRole('heading', { name: `Hit points: 1 of ${maximum}` });
  expect(await hitPointsOf(id)).toMatchObject({ current: 1, maximum });

  // The service undoes the whole hit: the stored play state (hit points, the spent pool) is as it was before.
  await user.click(await screen.findByRole('button', { name: /^Undo damage 20/ }));
  await screen.findByRole('heading', { name: 'Hit points: 5 of ' + maximum });
  expect((await client.getCharacter(id)).character.play).toEqual(before);
});

it('a hit to a quarter of the maximum offers the threshold, which turns the form on', async () => {
  const user = userEvent.setup();
  const id = await createBulwark('E2E Bulwark Rally');
  const { maximum } = await hitPointsOf(id);
  const quarter = Math.floor(maximum / 4);
  await openBulwark(user, 'E2E Bulwark Rally');
  const forms = () => screen.getByRole('radiogroup', { name: 'Forms' });
  expect(within(forms()).getByRole('radio', { name: /^Surge form/ }).getAttribute('aria-checked')).toBe('false');

  // A hit that is not enough: the threshold is not offered.
  await user.type(await screen.findByRole('spinbutton', { name: 'Amount' }), '1');
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  const first = await screen.findByRole('group', { name: 'Responses' });
  expect(within(first).queryByRole('checkbox', { name: /^Rally/ })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Cancel' }));

  // A hit that leaves exactly a quarter of the maximum: the threshold is offered; choosing it turns the surge form on.
  await user.type(await screen.findByRole('spinbutton', { name: 'Amount' }), String(maximum - quarter));
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  const responses = await screen.findByRole('group', { name: 'Responses' });
  const rally = await within(responses).findByRole<HTMLInputElement>('checkbox', { name: /^Rally/ });
  await waitFor(() => expect(rally.disabled).toBe(false));
  await user.click(rally);
  // The surge form raises Constitution, so the maximum after the hit is the form's, not the one the threshold was read against.
  await screen.findByText(new RegExp(`^Hit points after: ${quarter} of \\d+`));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(within(forms()).getByRole('radio', { name: /^Surge form/ }).getAttribute('aria-checked')).toBe('true'));
  expect((await hitPointsOf(id)).current).toBe(quarter);
});

it('plain damage from the summary routes through the panel', async () => {
  const user = userEvent.setup();
  const id = await createBulwark('E2E Bulwark Plain');
  const { maximum } = await hitPointsOf(id);
  await openBulwark(user, 'E2E Bulwark Plain');

  // The summary's quick button opens the panel (the character has responses); with nothing chosen Apply is plain damage.
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await screen.findByRole('heading', { name: 'Take 1 untyped damage' });
  expect(await hitPointsOf(id)).toMatchObject({ current: maximum }); // nothing is written by opening it
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await screen.findByRole('heading', { name: `Hit points: ${maximum - 1} of ${maximum}` });
  expect(screen.queryByRole('button', { name: 'Apply damage' })).toBeNull();
  expect(await hitPointsOf(id)).toMatchObject({ current: maximum - 1, maximum });
});

// ---- D43 S8: charging against the real DevHost ----

let chargingPack: Promise<ContentReference[]> | undefined;

/** The charging pack (`fixture-pack-charging.json`): the class with its two pools, then the feature that gathers and releases. Once per run. */
function installChargingPack(): Promise<ContentReference[]> {
  chargingPack ??= publishFixturePack('fixture-pack-charging.json', 'E2E Charging Book', ['80', '81'].map((n) => `6f0dc000-0000-4000-8000-0000000000${n}`));
  return chargingPack;
}

it('gather twice, release with the charge, then end the encounter with nothing left to clear', async () => {
  const user = userEvent.setup();
  const pins = await installChargingPack();
  const fixture = JSON.parse(readFileSync(resolve(fixtureFolder(), 'characters/pm-charger-l5.json'), 'utf8')) as { baseAbilities: Record<string, number> };
  await client.createCharacter({ name: 'E2E Charger', rulesFamily: 'srd-5.2.1', baseAbilities: fixture.baseAbilities as never, pins, classes: [{ class: pins[0]!, level: 5 }] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Charger/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Charger' });
  await openTab(user, sheet, 'Play');
  const resources = () => screen.getByRole('region', { name: 'Resources' });
  const lastRoll = () => screen.getByRole('status', { name: 'Last roll' });
  expect(within(resources()).getByRole('heading', { name: 'Focus: 9 of 9' })).toBeTruthy();
  await user.click(await screen.findByRole('button', { name: 'Start encounter' }));
  await screen.findByRole('button', { name: 'End encounter' });

  // Each Gather spends 1 Focus and builds 1 Charge in one change, which Undo takes back as one.
  const gather = () => screen.getByRole('button', { name: /^Use Gather/ });
  await user.click(gather());
  await waitFor(() => expect(within(resources()).getByRole('heading', { name: 'Charge: built up 1 of 4' })).toBeTruthy());
  await user.click(gather());
  await waitFor(() => expect(within(resources()).getByRole('heading', { name: 'Charge: built up 2 of 4' })).toBeTruthy());
  expect(within(resources()).getByRole('heading', { name: 'Focus: 7 of 9' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Undo use Gather' })).toBeTruthy();

  // Release with 2 points rolls the base dice and 2 extra; rolling changes nothing, the record's Spend draws the charge down.
  const points = await screen.findByRole<HTMLInputElement>('spinbutton', { name: /^Points to spend/ });
  expect(points.max).toBe('2');
  await user.clear(points);
  await user.type(points, '2');
  await user.click(screen.getByRole('button', { name: /^Roll Release/ }));
  await waitFor(() => expect(lastRoll().textContent).toMatch(/Release/));
  expect(lastRoll().textContent).toMatch(/2d8\s?\+\s?2d8/);
  expect(within(resources()).getByRole('heading', { name: 'Charge: built up 2 of 4' })).toBeTruthy();
  await user.click(await within(lastRoll()).findByRole('button', { name: /^Spend 2 Charge/ }));
  await waitFor(() => expect(within(resources()).getByRole('heading', { name: 'Charge: built up 0 of 4' })).toBeTruthy());

  // Nothing is built up, so End encounter is one click with no clear choices.
  await user.click(screen.getByRole('button', { name: 'End encounter' }));
  expect(screen.queryByRole('group', { name: 'End encounter' })).toBeNull();
  await screen.findByRole('button', { name: 'Start encounter' });
});

// ---- D73: the quickbar and "Usable now" against the real DevHost ----

const fixtureCharacterAbilities = (file: string) => (JSON.parse(readFileSync(resolve(fixtureFolder(), file), 'utf8')) as { baseAbilities: Record<string, number> }).baseAbilities;

let waveDetailsPack: Promise<ContentReference[]> | undefined;

/** The action-details pack (`fixture-pack-action-details.json`): the class with its pool, the Wisdom focus feature, then the one with the per-unit roll. Once per run. */
function installWaveDetailsPack(): Promise<ContentReference[]> {
  waveDetailsPack ??= publishFixturePack('fixture-pack-action-details.json', 'E2E Wave Book', ['50', '52', '53'].map((n) => `6f0dc000-0000-4000-8000-0000000000${n}`));
  return waveDetailsPack;
}

let modesPack: Promise<ContentReference[]> | undefined;

/** The feature-attacks pack (`fixture-pack-feature-attacks.json`): the class with the "Modes" group, then the mode feature that owns a roll only that mode allows. Once per run. */
function installModesPack(): Promise<ContentReference[]> {
  modesPack ??= publishFixturePack('fixture-pack-feature-attacks.json', 'E2E Modes Book', ['40', '42'].map((n) => `6f0dc000-0000-4000-8000-0000000000${n}`));
  return modesPack;
}

it('pins a per-unit roll, presses it with Alt+1: it rolls and spends once, Undo restores the pool, and the pin survives opening the character again (D73)', async () => {
  const user = userEvent.setup();
  const pins = await installWaveDetailsPack();
  await client.createCharacter({
    name: 'E2E Quickbar Wave',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: fixtureCharacterAbilities('characters/pm-wave-adept-l5.json') as never,
    pins,
    classes: [{ class: pins[0]!, level: 5 }],
  });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Quickbar Wave/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Quickbar Wave' });
  await openTab(user, sheet, 'Play');
  const resources = () => screen.getByRole('region', { name: 'Resources' });
  const lastRoll = () => screen.getByRole('status', { name: 'Last roll' });
  const quickbar = () => screen.getByRole('region', { name: 'Quickbar' });
  const focus = async () => Number(/^Focus: (\d+) of 9$/.exec((await within(resources()).findByRole('heading', { name: /^Focus: \d+ of 9$/ })).textContent!)![1]);
  expect(await focus()).toBe(9);
  expect(screen.queryByRole('region', { name: 'Quickbar' })).toBeNull();

  await user.click(await screen.findByRole('button', { name: 'Pin Tide wave to the quickbar' }));
  const pinned = await within(quickbar()).findByRole('button', { name: /^Roll Tide wave/ });
  expect(pinned.getAttribute('aria-keyshortcuts')).toBe('Alt+1');

  // Alt+1 (a button named "and spend") rolls and then makes that spend once, through the same play change, so nothing waits on a click.
  await user.keyboard('{Alt>}1{/Alt}');
  await waitFor(() => expect(lastRoll().textContent).toMatch(/Tide wave/));
  const spendButton = await within(lastRoll()).findByRole('button', { name: /^Spend \d+ Focus/ });
  const spent = Number(/^Spend (\d+) Focus/.exec(spendButton.getAttribute('aria-label') ?? spendButton.textContent!)![1]);
  expect(spent).toBe(2); // the roll's base cost (R13: a pinned per-unit roll spends no extra units)
  await waitFor(async () => expect(await focus()).toBe(9 - spent));
  expect(within(lastRoll()).getByRole<HTMLButtonElement>('button', { name: /^Spend \d+ Focus/ }).disabled).toBe(true); // spent once; the record cannot spend again
  await waitFor(() => expect(quickbar().getAttribute('aria-busy')).toBeNull()); // `finally` clears busy after the pool renders

  // The service undoes the spend: the pool is back.
  await user.click(await screen.findByRole('button', { name: /^Undo/ }));
  await waitFor(async () => expect(await focus()).toBe(9));

  // Leaving the sheet and opening the character again (a fresh mount reads the page storage): the pin is still there.
  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Quickbar Wave/ }));
  await screen.findByRole('article', { name: 'E2E Quickbar Wave' });
  expect(await within(quickbar()).findByRole('button', { name: /^Roll Tide wave/ })).toBeTruthy();
});

it('"Usable now" leaves out a mode-only roll while the mode is off and lists it again when the mode is switched on, with no reload (D73)', async () => {
  const user = userEvent.setup();
  const pins = await installModesPack();
  await client.createCharacter({
    name: 'E2E Usable Modes',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: fixtureCharacterAbilities('characters/pm-adept-l5.json') as never,
    pins,
    classes: [{ class: pins[0]!, level: 1 }],
  });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Usable Modes/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Usable Modes' });
  await openTab(user, sheet, 'Play');
  const roll = () => screen.queryByRole('button', { name: /^Roll Flash step/ });
  const modes = () => within(screen.getByRole('region', { name: 'Combat' })).getByRole('radiogroup', { name: 'Modes' });

  // Listed with its reason while the filter is off; the form is off.
  await screen.findByRole('button', { name: /^Roll Flash step/ });
  const filter = screen.getByRole<HTMLInputElement>('checkbox', { name: 'Usable now' });
  expect(filter.checked).toBe(false);
  await user.click(filter);
  await waitFor(() => expect(roll()).toBeNull());

  // Switching the mode on lists it at once.
  await user.click(within(modes()).getByRole('radio', { name: /^Kindled mode/ }));
  await waitFor(() => expect(within(modes()).getByRole('radio', { name: /^Kindled mode/ }).getAttribute('aria-checked')).toBe('true'));
  await waitFor(() => expect(roll()).not.toBeNull());
  expect(filter.checked).toBe(true);
});


it('authors a hit-point roll in the studio, uses it and undoes it (MECH-X item 1)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Rite Book', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Rite Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Rite Book/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Rite Book' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(within(editor()).getByRole('group', { name }));

  // A roll that pays 2 hit points and has no pool at all.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Blood Rite Feature');
  await user.click(within(editor()).getByRole('button', { name: 'Add roll or action' }));
  const rite = rule(/^Rule 1: Roll or action/);
  await user.type(rite.getByRole('textbox', { name: 'Roll name' }), 'E2E Blood Rite');
  const dice = rite.getByRole('textbox', { name: /^Dice/ });
  await user.clear(dice);
  await user.type(dice, '1d6');
  await user.selectOptions(rite.getByRole('combobox', { name: 'Pays with' }), 'Hit points');
  await user.type(rite.getByRole('textbox', { name: /^Hit points paid/ }), '2');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await expectStatus(/Made E2E Blood Rite Feature available to characters/);

  const published = (await client.contentBySource(source.id)).find((e) => e.name === 'E2E Blood Rite Feature')?.latestPublished;
  expect(published).toBeTruthy();
  const created = await client.createCharacter({
    name: 'E2E Rite Hero',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
    pins: [published!],
  });
  await client.saveCharacter({ ...created.character, overrides: [{ field: 'hitPoints', value: 20 }] });

  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Rite Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Rite Hero' });
  await openTab(user, sheet, 'Play');
  const lastRoll = () => screen.getByRole('status', { name: 'Last roll' });
  await waitFor(() => expect(screen.getByRole('region', { name: /^Hit points: 20 of 20/ })).toBeTruthy());

  // Rolling pays nothing; the record's "Pay 2 hit points" takes exactly two as one change.
  await user.click(await screen.findByRole('button', { name: /^Roll E2E Blood Rite/ }));
  await waitFor(() => expect(lastRoll().textContent).toMatch(/E2E Blood Rite/));
  expect(screen.getByRole('region', { name: /^Hit points: 20 of 20/ })).toBeTruthy();
  await user.click(await within(lastRoll()).findByRole('button', { name: /^Pay 2 hit points/ }));
  await waitFor(() => expect(screen.getByRole('region', { name: /^Hit points: 18 of 20/ })).toBeTruthy());

  // The service undoes the payment: the hit points are back.
  await user.click(await screen.findByRole('button', { name: 'Undo use E2E Blood Rite' }));
  await waitFor(() => expect(screen.getByRole('region', { name: /^Hit points: 20 of 20/ })).toBeTruthy());
});

it('a replaceable choice offers a swap at level-up and notes one made outside it (MECH-X item 2)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Swap Book', ['srd-5.2.1']);
  const id = (n: number) => `e2e5a000-0000-4000-8000-${String(n).padStart(12, '0')}`;
  const draftOf = (n: number, name: string, effects: ContentRevision['effects']): ContentRevision => ({
    contentId: id(n),
    revisionId: id(n + 1000),
    schemaVersion: 12,
    kind: 'feature',
    name,
    rulesFamilies: ['srd-5.2.1'],
    provenance: { sourceId: source.id },
    status: 'draft',
    effects,
  });
  const publishOne = async (revision: ContentRevision): Promise<ContentReference> => {
    const result = await client.publish(await client.saveDraft(revision));
    expect(result.report.errors).toEqual([]);
    return result.published;
  };

  // Original options first (a draft's options name published revisions), then one feature with two replaceable choices:
  // a level-up one (count 1) and a long-rest one (count 2, at most 1 swapped at once).
  const upOptions = [await publishOne(draftOf(1, 'Fixture Stance One', [])), await publishOne(draftOf(2, 'Fixture Stance Two', []))];
  const restOptions: ContentReference[] = [];
  for (const [n, name] of [[3, 'Fixture Drill One'], [4, 'Fixture Drill Two'], [5, 'Fixture Drill Three'], [6, 'Fixture Drill Four']] as const) {
    restOptions.push(await publishOne(draftOf(n, name, [])));
  }
  const feature = await publishOne(
    draftOf(10, 'E2E Swap Feature', [
      { type: 'choice', id: 'swap-up', choiceId: 'swap-up', count: 1, options: upOptions, replaceable: { when: 'levelUp' } },
      { type: 'choice', id: 'swap-rest', choiceId: 'swap-rest', count: 2, options: restOptions, replaceable: { when: 'longRest', max: 1 } },
    ] as ContentRevision['effects']),
  );

  // A Fighter 1 with every other choice made, and the two picks of each choice.
  const srd = (n: number) => ({ contentId: `52c00000-0000-4000-8000-${String(n).padStart(12, '0')}`, revisionId: `52e00000-0000-4000-8000-${String(n).padStart(12, '0')}` });
  await client.createCharacter({
    name: 'E2E Swapper',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 16, dex: 14, con: 14, int: 10, wis: 12, cha: 8 },
    pins: [srd(1), srd(2), feature],
    classes: [{ class: srd(408000), level: 1 }],
    choices: [
      { source: srd(2), choiceId: 'soldier-ability-scores', selected: [srd(4)] },
      { source: srd(408000), choiceId: 'fighter-skills', selected: [srd(408800), srd(408802)] },
      { source: srd(408001), choiceId: 'fighting-style', selected: [srd(408601)] },
      { source: feature, choiceId: 'swap-up', selected: [upOptions[0]!] },
      { source: feature, choiceId: 'swap-rest', selected: [restOptions[0]!, restOptions[1]!] },
    ],
  });
  const pickIn = async (legend: RegExp, option: RegExp) => {
    const group = await screen.findByRole('group', { name: legend });
    await user.click(await within(group).findByRole('checkbox', { name: option }));
  };

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Swapper/ }));
  let sheet = await screen.findByRole('article', { name: 'E2E Swapper' });

  // Level up: the Choices step offers the swap; replacing the one pick is inside the window, so there is no notice.
  await user.click(within(sheet).getByRole('button', { name: 'Level up' }));
  await user.click(await screen.findByRole('radio', { name: /^Fighter 2\b/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  const upGroup = await screen.findByRole('group', { name: /^E2E Swap Feature: choose 1/ });
  expect(within(upGroup).getByText('You may replace one pick now.')).toBeTruthy();
  await pickIn(/^E2E Swap Feature: choose 1/, /^Fixture Stance One/); // untick the old pick
  await pickIn(/^E2E Swap Feature: choose 1/, /^Fixture Stance Two/);
  await user.click(await screen.findByRole('button', { name: 'Save level-up' }));

  sheet = await screen.findByRole('article', { name: 'E2E Swapper' });
  const changed = within(sheet).getByRole('region', { name: 'What changed at level 2' });
  expect(within(changed).getByText('Replaced 1 pick: E2E Swap Feature')).toBeTruthy();
  expect(screen.queryByText(/outside its window|more than the/)).toBeNull();

  // Long rest: the preview and the after-rest panel name the long-rest choice; swapping both picks exceeds the one it allows.
  await openTab(user, sheet, 'Play');
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const rest = await screen.findByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(rest.textContent).toMatch(/you may replace one pick of: E2E Swap Feature/));
  // That line comes from the sheet, not the proposal; Finish does nothing until the proposal arrives.
  const finish = within(rest).getByRole('button', { name: 'Finish long rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);
  const after = await screen.findByRole('region', { name: 'After your long rest' }, { timeout: 5000 });
  expect(after.textContent).toMatch(/You may replace one pick of: E2E Swap Feature\./);
  await user.click(within(after).getByRole('button', { name: 'Make choices' }));

  const legend = /^E2E Swap Feature: choose 2/;
  await pickIn(legend, /^Fixture Drill One/);
  await pickIn(legend, /^Fixture Drill Two/);
  await pickIn(legend, /^Fixture Drill Three/);
  await pickIn(legend, /^Fixture Drill Four/);
  await user.click(await screen.findByRole('button', { name: 'Save choices' }));
  await expectStatus(/E2E Swap Feature: you replaced 2 picks, more than the 1 it allows at once\./);
});
