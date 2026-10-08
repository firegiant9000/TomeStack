// @vitest-environment jsdom
// The builder's spell picker (D30): the legend's counts say what is recorded, not what the search shows. The client is mocked;
// names and values are invented.
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterView, ContentOption, RulesFamilyPolicy, SpellcastingEntry } from '../api/types';
import { CharacterBuilder } from './CharacterBuilder';

vi.mock('../api/client', () => ({ client: { listCampaigns: vi.fn(), listContent: vi.fn(), preview: vi.fn(), previewChoice: vi.fn(), createCharacter: vi.fn(), rollDice: vi.fn() } }));

const caster = { contentId: 'fixture-caster', revisionId: 'fixture-caster-r1' };
const spell = (id: string, name: string, level: number): ContentOption => ({
  reference: { contentId: id, revisionId: `${id}-r1` },
  kind: 'spell',
  name,
  rulesFamilies: ['srd-5.1'],
  compatible: true,
  sourceId: 'fixture-source',
  sourceTitle: 'Fixture source',
  spell: { level, lists: ['fixture-list'], concentration: false, ritual: false },
});
const spells = [spell('fixture-bolt', 'Fixture Bolt', 1), spell('fixture-glow', 'Fixture Glow', 1)];

function viewWith(recorded: Character['spells']): CharacterView {
  const character: Character = {
    id: 'fixture-2',
    schemaVersion: 7,
    name: 'Fixture Quill',
    rulesFamily: 'srd-5.1',
    level: 1,
    classes: [],
    choices: [],
    crossFamilyExceptions: [],
    baseAbilities: { str: 10, dex: 12, con: 10, int: 14, wis: 10, cha: 10 },
    pins: [],
    overrides: [],
    updatedAt: '2026-10-03T00:00:00Z',
    spells: recorded,
  };
  const entry: SpellcastingEntry = {
    content: caster,
    name: 'Fixture caster',
    effectId: 'fixture-effect',
    classLevel: 1,
    ability: 'int',
    attackBonus: 4,
    saveDc: 12,
    preparation: 'known',
    spellList: 'fixture-list',
    slotKind: 'spellSlots',
    slots: [2],
    spellsAllowed: 2,
    primary: true,
    origin: {} as SpellcastingEntry['origin'],
    spells: [],
    warnings: [],
  };
  return { character, sheet: { characterId: 'fixture-2', rulesFamily: 'srd-5.1', diagnostics: [], fields: [], spellcasting: [entry] } as CharacterView['sheet'] };
}

beforeEach(() => {
  vi.mocked(client.rollDice).mockReset();
  vi.mocked(client.listCampaigns).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue(spells);
  vi.mocked(client.preview).mockImplementation(async (draft) => viewWith(draft.spells));
});
afterEach(cleanup);

it('keeps the picker legend counting what is recorded when the search hides the chosen spell (D30)', async () => {
  const user = userEvent.setup();
  render(<CharacterBuilder mode={{ kind: 'choices', view: viewWith([]) }} rulesFamilies={[]} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  const picker = await screen.findByRole('group', { name: /Fixture caster spells/ });
  await user.click(await within(picker).findByRole('checkbox', { name: /Fixture Bolt/ }));
  await waitFor(() => expect(screen.getByRole('group', { name: /Fixture caster spells \(1 of 2 known spells\)/ })).toBeTruthy());
  await user.type(screen.getByRole('searchbox', { name: 'Search Fixture caster spells by name' }), 'glow');
  expect(screen.queryByRole('checkbox', { name: /Fixture Bolt/ })).toBeNull(); // the chosen spell is hidden by the search
  expect(screen.getByRole('group', { name: /Fixture caster spells \(1 of 2 known spells\)/ })).toBeTruthy();
});

// D31: the builder never offers the other rules family; campaign-outside content stays listed and disabled (P-01).
const policy = (id: RulesFamilyPolicy['id'], displayName: string): RulesFamilyPolicy => ({
  id,
  displayName,
  abilityIncreaseSource: id === 'srd-5.1' ? 'species' : 'background',
  backgroundGrantsFeat: id !== 'srd-5.1',
  longRestExhaustionNeedsFoodAndDrink: id === 'srd-5.1',
});
const families = [policy('srd-5.1', 'SRD 5.1'), policy('srd-5.2.1', 'SRD 5.2.1')];
const content = (n: number, kind: ContentOption['kind'], name: string, family: 'srd-5.1' | 'srd-5.2.1', over: Partial<ContentOption> = {}): ContentOption => ({
  reference: { contentId: `fixture-c${n}`, revisionId: `fixture-r${n}` },
  kind,
  name,
  rulesFamilies: [family],
  compatible: family === 'srd-5.1',
  sourceId: 'fixture-source',
  sourceTitle: 'Fixture pack',
  ...over,
});

it('lists no option of the other family, and keeps a campaign-outside option listed but disabled (D31, P-01)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listCampaigns).mockResolvedValue([{ id: 'fixture-camp', name: 'Fixture Campaign', rulesFamily: 'srd-5.1', allowedSources: ['fixture-source'] }]);
  vi.mocked(client.listContent).mockResolvedValue([
    content(1, 'species', 'Fixture Hillfolk', 'srd-5.1'),
    content(2, 'species', 'Fixture Dunefolk', 'srd-5.2.1'),
    content(3, 'class', 'Fixture Warden', 'srd-5.1'),
    content(4, 'class', 'Fixture Outsider', 'srd-5.1', { allowedInCampaign: false }),
    content(5, 'class', 'Fixture Outlander', 'srd-5.2.1', { allowedInCampaign: false }),
    content(6, 'feat', 'Fixture Other Feat', 'srd-5.2.1'),
  ]);
  render(<CharacterBuilder mode={{ kind: 'create' }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Campaign' }), 'fixture-camp');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  expect(await screen.findByRole('radio', { name: /^Fixture Hillfolk/ })).toBeTruthy();
  expect(screen.queryByRole('radio', { name: /^Fixture Dunefolk/ })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await waitFor(() => expect(screen.getByRole('radio', { name: /^Fixture Outsider/ })).toBeTruthy());
  const outsider = screen.getByRole('radio', { name: /^Fixture Outsider/ }) as HTMLInputElement;
  expect(outsider.disabled).toBe(true);
  expect(outsider.closest('label')!.textContent).toMatch(/not allowed in this campaign/);
  // Outside the campaign and of the other family: still listed (P-01), with the campaign reason.
  const outlander = screen.getByRole('radio', { name: /^Fixture Outlander/ }) as HTMLInputElement;
  expect(outlander.disabled).toBe(true);
  expect(outlander.closest('label')!.textContent).toMatch(/not allowed in this campaign/);
  expect(screen.queryByText(/Options for the other family cannot be selected/)).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
  expect(screen.queryByText(/Fixture Other Feat/)).toBeNull();
});

it('keeps a checked option of the other family in a choice, and omits an unchecked one (D31)', async () => {
  const source = { contentId: 'fixture-feature', revisionId: 'fixture-feature-r1' };
  const mine = content(7, 'feature', 'Fixture Mine', 'srd-5.1');
  const imported = content(8, 'feature', 'Fixture Imported', 'srd-5.2.1');
  const absent = content(9, 'feature', 'Fixture Absent', 'srd-5.2.1');
  vi.mocked(client.listContent).mockResolvedValue([mine, imported, absent]);
  const view = viewWith([]);
  view.sheet.choices = [
    { source, sourceName: 'Fixture feature', choiceId: 'pick', count: 2, options: [mine.reference, imported.reference, absent.reference], selected: [imported.reference], resolved: false },
  ];
  render(<CharacterBuilder mode={{ kind: 'choices', view }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  const picker = await screen.findByRole('group', { name: /Fixture feature: choose 2/ });
  await within(picker).findByRole('checkbox', { name: /Fixture Mine/ });
  const kept = within(picker).getByRole('checkbox', { name: /Fixture Imported/ }) as HTMLInputElement;
  expect(kept.checked).toBe(true);
  expect(within(picker).queryByRole('checkbox', { name: /Fixture Absent/ })).toBeNull();
});

// D32: the create flow in six steps.
const draftView = (): CharacterView =>
  ({
    character: { id: 'd', schemaVersion: 8, name: 'Fixture New', rulesFamily: 'srd-5.1', level: 1, classes: [], choices: [], crossFamilyExceptions: [], baseAbilities: { str: 15, dex: 14, con: 13, int: 12, wis: 10, cha: 8 }, pins: [], overrides: [], updatedAt: '2026-10-07T00:00:00Z' },
    sheet: { characterId: 'd', rulesFamily: 'srd-5.1', diagnostics: [], fields: [], choices: [] },
  }) as never;
const stepContent = () => [content(1, 'species', 'Fixture Hillfolk', 'srd-5.1'), content(3, 'class', 'Fixture Warden', 'srd-5.1')];
const renderCreate = () => render(<CharacterBuilder mode={{ kind: 'create' }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);

it('walks six steps with the heading focused on each, and reaches the choices (D32)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  vi.mocked(client.preview).mockResolvedValue(draftView());
  renderCreate();
  expect(screen.getByText('Step 1 of 6. Nothing is saved until you press Create and save.')).toBeTruthy();
  expect(document.activeElement).not.toBe(screen.getByRole('heading', { name: 'New character' })); // never on first mount
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'New character' }));
  expect(screen.getByRole('form', { name: 'Ability scores' })).toBeTruthy();
  expect(screen.getByRole('heading', { name: 'Ability scores' })).toBeTruthy();
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  expect(screen.getByRole('group', { name: 'Base ability scores' })).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  await user.click(await screen.findByRole('radio', { name: /^Fixture Hillfolk/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await user.click(screen.getByRole('radio', { name: /^Fixture Warden/ }));
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  expect(await screen.findByRole('group', { name: 'Choices' })).toBeTruthy();
  expect(screen.getByText('Step 6 of 6. Nothing is saved until you press Create and save.')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Back' }));
  expect(screen.getByRole('heading', { name: 'Background' })).toBeTruthy();
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'New character' }));
});

it('moves focus to the heading when Back returns to the Rules step (R5, 2.4.3)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  renderCreate();
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('button', { name: 'Back' }));
  expect(screen.getByRole('form', { name: 'Rules' })).toBeTruthy();
  expect((screen.getByRole('textbox', { name: 'Name' }) as HTMLInputElement).value).toBe('Fixture New');
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'New character' }));
});

it('keeps the species and class picks across Back and forth', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  renderCreate();
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  await user.click(await screen.findByRole('radio', { name: /^Fixture Hillfolk/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await user.click(screen.getByRole('radio', { name: /^Fixture Warden/ }));
  await user.click(screen.getByRole('button', { name: 'Back' }));
  expect((screen.getByRole('radio', { name: /^Fixture Hillfolk/ }) as HTMLInputElement).checked).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  expect((screen.getByRole('radio', { name: /^Fixture Warden/ }) as HTMLInputElement).checked).toBe(true);
});

// D32: standard array, point buy and rolled scores.
async function toScores(user: ReturnType<typeof userEvent.setup>, onError: (error: unknown) => void = () => {}) {
  render(<CharacterBuilder mode={{ kind: 'create' }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={onError} />);
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
}
const nextSpecies = () => screen.getByRole('button', { name: 'Next: species' }) as HTMLButtonElement;
const arrayOrder = [['Strength', '15'], ['Dexterity', '14'], ['Constitution', '13'], ['Intelligence', '12'], ['Wisdom', '10'], ['Charisma', '8']] as const;
async function assignArray(user: ReturnType<typeof userEvent.setup>) {
  const group = screen.getByRole('group', { name: 'Assign the standard array' });
  for (const [label, value] of arrayOrder) await user.selectOptions(within(group).getByRole('combobox', { name: label }), value);
}
const rollSet = (values: number[], dropped: number) => ({
  formula: '4d6',
  mode: 'normal',
  critical: false,
  dice: values.map((v, i) => ({ term: 0, sides: 6, value: v, kept: i !== dropped, fromCritical: false })),
  diceTotal: 0,
  expressionConstant: 0,
  modifiers: [],
  total: values.reduce((a, b) => a + b, 0) - values[dropped]!,
  provenance: { rollId: 'dice', label: 'Ability score roll' },
});
const sixSets = () => [rollSet([6, 5, 3, 2], 3), rollSet([4, 4, 4, 1], 3), rollSet([6, 6, 6, 6], 0), rollSet([2, 2, 2, 2], 0), rollSet([5, 4, 3, 2], 3), rollSet([3, 3, 3, 1], 3)];
const queueSets = (sets: ReturnType<typeof sixSets>) => {
  for (const s of sets) vi.mocked(client.rollDice).mockResolvedValueOnce(s as never);
};

it('assigns the standard array once each and refuses a duplicate value (Review Focus 6)', async () => {
  const user = userEvent.setup();
  await toScores(user);
  const group = screen.getByRole('group', { name: 'Assign the standard array' });
  expect(nextSpecies().disabled).toBe(true);
  expect(screen.getByText('Assign all six scores to continue.')).toBeTruthy();
  expect(nextSpecies().getAttribute('aria-describedby')).toBe('next-hint');
  expect(screen.getByText('Assigned: 0 of 6')).toBeTruthy();
  await user.selectOptions(within(group).getByRole('combobox', { name: 'Strength' }), '15');
  expect((within(within(group).getByRole('combobox', { name: 'Dexterity' })).getByRole('option', { name: '15 (used)' }) as HTMLOptionElement).disabled).toBe(true);
  // A stale select can still send a used value: the second select is reset and nothing is assigned twice.
  const dex = within(group).getByRole('combobox', { name: 'Dexterity' }) as HTMLSelectElement;
  fireEvent.change(dex, { target: { value: '15' } });
  expect(dex.value).toBe('');
  expect(within(dex).getByRole('option', { name: 'Choose' }).textContent).toBe('Choose');
  expect(screen.getByText('Assigned: 1 of 6')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(true);
  for (const [label, value] of arrayOrder.slice(1)) await user.selectOptions(within(group).getByRole('combobox', { name: label }), value);
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(false);
  expect(screen.queryByText('Assign all six scores to continue.')).toBeNull();
  expect(screen.getByText(/Base scores: Str 15, Dex 14, Con 13, Int 12, Wis 10, Cha 8/)).toBeTruthy();
});

it('uses the assigned scores for the draft, not the legacy defaults', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  vi.mocked(client.preview).mockResolvedValue(draftView());
  await toScores(user);
  await assignArray(user);
  for (const next of ['Next: species', 'Next: class', 'Next: background', 'Next: choices']) await user.click(await screen.findByRole('button', { name: next }));
  expect(vi.mocked(client.preview).mock.calls[0]![0].baseAbilities).toEqual({ str: 15, dex: 14, con: 13, int: 12, wis: 10, cha: 8 });
});

it('counts point buy against 27 and blocks Next when over budget', async () => {
  const user = userEvent.setup();
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Point buy' }));
  const group = screen.getByRole('group', { name: 'Point buy' });
  expect(screen.getByText('Points left: 27 of 27')).toBeTruthy();
  expect(screen.getByText('You can still spend 27 points.')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(false);
  for (const label of ['Strength', 'Dexterity', 'Constitution'] as const) {
    const input = within(group).getByRole('spinbutton', { name: label });
    await user.clear(input);
    await user.type(input, '15');
  }
  expect(screen.getByText('Points left: 0 of 27')).toBeTruthy();
  expect(screen.queryByText(/You can still spend/)).toBeNull();
  const wis = within(group).getByRole('spinbutton', { name: 'Wisdom' });
  await user.clear(wis);
  await user.type(wis, '9');
  expect(screen.getByText('Points left: -1 of 27')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(true);
  expect(screen.getByText('Spend at most 27 points to continue.')).toBeTruthy();
});

it('blocks Next for a point buy score outside 8 to 15', async () => {
  const user = userEvent.setup();
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Point buy' }));
  const str = within(screen.getByRole('group', { name: 'Point buy' })).getByRole('spinbutton', { name: 'Strength' });
  await user.clear(str);
  await user.type(str, '16');
  expect(nextSpecies().disabled).toBe(true);
});

it('keeps focus on the method radios, resets point buy to all 8, and keeps the last scores when going to Enter by hand', async () => {
  const user = userEvent.setup();
  await toScores(user);
  await assignArray(user);
  const pointBuy = screen.getByRole('radio', { name: 'Point buy' });
  await user.click(pointBuy);
  expect(document.activeElement).toBe(pointBuy);
  expect(screen.getByText(/Base scores: Str 8, Dex 8, Con 8, Int 8, Wis 8, Cha 8/)).toBeTruthy();
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  expect((within(screen.getByRole('group', { name: 'Base ability scores' })).getByRole('spinbutton', { name: 'Strength' }) as HTMLInputElement).value).toBe('8');
  // Back to the array: the assignment made earlier is still there and gives the scores again.
  await user.click(screen.getByRole('radio', { name: 'Standard array' }));
  expect((within(screen.getByRole('group', { name: 'Assign the standard array' })).getByRole('combobox', { name: 'Strength' }) as HTMLSelectElement).value).toBe('15');
  expect(nextSpecies().disabled).toBe(false);
  expect(screen.getByText(/Base scores: Str 15, Dex 14/)).toBeTruthy();
});

it('rolls six sets through dice.roll with keepHighest 3 and assigns them', async () => {
  const user = userEvent.setup();
  queueSets(sixSets());
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  expect(screen.getByText('Roll the scores to continue.')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(true);
  const status = screen.getByRole('status');
  expect(status.textContent).toBe('');
  const button = screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' });
  await user.click(button);
  expect(vi.mocked(client.rollDice)).toHaveBeenCalledTimes(6);
  expect(vi.mocked(client.rollDice).mock.calls[0]).toEqual(['4d6', 3, 'Ability score roll 1']);
  expect(vi.mocked(client.rollDice).mock.calls[5]).toEqual(['4d6', 3, 'Ability score roll 6']);
  expect(await screen.findByText('Roll 1: 14 (6, 5, 3, dropped 2)')).toBeTruthy();
  expect(screen.getByRole('status').textContent).toBe('Six scores rolled.');
  expect(document.activeElement).toBe(button);
  expect(button.textContent).toBe('Roll again');
  const dice = within(screen.getByRole('list', { name: 'Rolled sets' })).getAllByRole('listitem')[0]!.querySelectorAll('.dice.dice-still[aria-hidden="true"] .die[data-sides="6"]');
  expect([...dice].map((d) => [d.getAttribute('data-value'), d.getAttribute('data-kept')])).toEqual([['6', 'true'], ['5', 'true'], ['3', 'true'], ['2', 'false']]);
  const group = screen.getByRole('group', { name: 'Assign the rolled scores' });
  await user.selectOptions(within(group).getByRole('combobox', { name: 'Strength' }), '18');
  expect(screen.getByText(/Base scores: Str 18/)).toBeTruthy();
  expect(screen.getByText('Assigned: 1 of 6')).toBeTruthy();
});

it('ignores a second press while rolling', async () => {
  const user = userEvent.setup();
  const pending: Array<(value: never) => void> = [];
  vi.mocked(client.rollDice).mockImplementation(() => new Promise((resolve) => pending.push(resolve as never)));
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  const button = screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' });
  await user.click(button);
  expect(button.getAttribute('aria-disabled')).toBe('true');
  expect(button.hasAttribute('disabled')).toBe(false);
  await user.click(button);
  expect(vi.mocked(client.rollDice)).toHaveBeenCalledTimes(1);
  expect(document.activeElement).toBe(button);
  for (const s of sixSets()) {
    await waitFor(() => expect(pending.length).toBeGreaterThan(0));
    pending.shift()!(s as never);
  }
  expect(await screen.findByText('Roll 1: 14 (6, 5, 3, dropped 2)')).toBeTruthy();
  expect(button.getAttribute('aria-disabled')).toBeNull();
});

it('reports a roll error and never leaves a partial pool assignable; a failed re-roll keeps the earlier set', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  vi.mocked(client.rollDice).mockResolvedValueOnce(rollSet([6, 5, 3, 2], 3) as never).mockResolvedValueOnce(rollSet([4, 4, 4, 1], 3) as never).mockRejectedValueOnce(new Error('fixture failure'));
  await toScores(user, onError);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  const button = screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' });
  await user.click(button);
  await waitFor(() => expect(onError).toHaveBeenCalledTimes(1));
  expect(screen.queryByRole('list', { name: 'Rolled sets' })).toBeNull();
  expect(screen.queryByRole('group', { name: 'Assign the rolled scores' })).toBeNull();
  expect(screen.getByRole('status').textContent).toBe('');
  expect(nextSpecies().disabled).toBe(true);
  expect(document.activeElement).toBe(button);
  // A full roll, then a failure part-way: the complete set stays, with its assignment.
  queueSets(sixSets());
  await user.click(button);
  await screen.findByText('Roll 1: 14 (6, 5, 3, dropped 2)');
  await user.selectOptions(within(screen.getByRole('group', { name: 'Assign the rolled scores' })).getByRole('combobox', { name: 'Strength' }), '18');
  vi.mocked(client.rollDice).mockResolvedValueOnce(rollSet([1, 1, 1, 1], 0) as never).mockRejectedValueOnce(new Error('fixture failure'));
  await user.click(screen.getByRole('button', { name: 'Roll again' }));
  await waitFor(() => expect(onError).toHaveBeenCalledTimes(2));
  expect(screen.getByText('Roll 1: 14 (6, 5, 3, dropped 2)')).toBeTruthy();
  expect(screen.getAllByRole('listitem')).toHaveLength(6);
  expect((within(screen.getByRole('group', { name: 'Assign the rolled scores' })).getByRole('combobox', { name: 'Strength' }) as HTMLSelectElement).value).toBe('18');
});

it('a new roll clears the rolled assignment; switching methods drops an assignment the new pool cannot hold', async () => {
  const user = userEvent.setup();
  queueSets(sixSets());
  await toScores(user);
  await assignArray(user);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  // Nothing is rolled yet, so the empty pool cannot hold the array assignment: the switch itself clears it.
  expect(nextSpecies().disabled).toBe(true);
  await user.click(screen.getByRole('radio', { name: 'Standard array' }));
  expect(screen.getByText('Assigned: 0 of 6')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(true);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  await screen.findByText('Roll 1: 14 (6, 5, 3, dropped 2)');
  expect(screen.getByText('Assigned: 0 of 6')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(true);
});

// Six sets whose totals are the standard array (15, 14, 13, 12, 10, 8): 4d6 drop lowest, the dropped die is a 1.
const arrayTotals = () => [rollSet([6, 5, 4, 1], 3), rollSet([6, 5, 3, 1], 3), rollSet([5, 5, 3, 1], 3), rollSet([5, 4, 3, 1], 3), rollSet([4, 3, 3, 1], 3), rollSet([4, 2, 2, 1], 3)];

it('keeps a complete rolled assignment across a switch to the array and back, and applies it', async () => {
  const user = userEvent.setup();
  queueSets(arrayTotals());
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  const group = await screen.findByRole('group', { name: 'Assign the rolled scores' });
  for (const [label, value] of [['Strength', '8'], ['Dexterity', '10'], ['Constitution', '12'], ['Intelligence', '13'], ['Wisdom', '14'], ['Charisma', '15']] as const)
    await user.selectOptions(within(group).getByRole('combobox', { name: label }), value);
  expect(nextSpecies().disabled).toBe(false);
  await user.click(screen.getByRole('radio', { name: 'Standard array' }));
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  expect(screen.getByText(/Base scores: Str 8, Dex 10, Con 12, Int 13, Wis 14, Cha 15/)).toBeTruthy();
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(false);
  expect(screen.getByText(/Base scores: Str 8, Dex 10, Con 12, Int 13, Wis 14, Cha 15/)).toBeTruthy();
});

it('lets a duplicated rolled total be assigned as often as it was rolled, and no more', async () => {
  const user = userEvent.setup();
  queueSets([rollSet([4, 4, 4, 1], 3), rollSet([4, 4, 4, 1], 3), rollSet([6, 6, 6, 6], 0), rollSet([2, 2, 2, 2], 0), rollSet([5, 4, 2, 1], 3), rollSet([3, 3, 3, 1], 3)]);
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  const group = await screen.findByRole('group', { name: 'Assign the rolled scores' });
  const select = (label: string) => within(group).getByRole('combobox', { name: label }) as HTMLSelectElement;
  await user.selectOptions(select('Strength'), '12');
  await user.selectOptions(select('Dexterity'), '12');
  expect(select('Strength').value).toBe('12');
  expect(select('Dexterity').value).toBe('12');
  expect(screen.getByText('Assigned: 2 of 6')).toBeTruthy();
  fireEvent.change(select('Constitution'), { target: { value: '12' } });
  expect(select('Constitution').value).toBe('');
  expect(screen.getByText('Assigned: 2 of 6')).toBeTruthy();
});

it('keeps an assignment made in the array when a roll arrives late', async () => {
  const user = userEvent.setup();
  const pending: Array<(value: never) => void> = [];
  vi.mocked(client.rollDice).mockImplementation(() => new Promise((resolve) => pending.push(resolve as never)));
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  await user.click(screen.getByRole('radio', { name: 'Standard array' }));
  await assignArray(user);
  for (const s of sixSets()) {
    await waitFor(() => expect(pending.length).toBeGreaterThan(0));
    pending.shift()!(s as never);
  }
  await new Promise((r) => setTimeout(r, 20));
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  expect(nextSpecies().disabled).toBe(false);
  expect(screen.getByText(/Base scores: Str 15, Dex 14/)).toBeTruthy();
});

it('shows no points figure while a point buy score is out of range', async () => {
  const user = userEvent.setup();
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Point buy' }));
  const str = within(screen.getByRole('group', { name: 'Point buy' })).getByRole('spinbutton', { name: 'Strength' });
  await user.clear(str);
  await user.type(str, '16');
  expect(screen.queryByText(/Points left/)).toBeNull();
  expect(screen.queryByText(/You can still spend/)).toBeNull();
  expect(screen.getByText('Keep every score between 8 and 15 to continue.')).toBeTruthy();
  await user.clear(str);
  await user.type(str, '9');
  expect(screen.getByText('Points left: 26 of 27')).toBeTruthy();
});

it('does not report a roll error after the step has gone', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  let fail: (error: unknown) => void = () => {};
  vi.mocked(client.rollDice).mockImplementation(() => new Promise((_, reject) => (fail = reject)));
  await toScores(user, onError);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  await user.click(screen.getByRole('button', { name: 'Back' }));
  fail(new Error('fixture failure'));
  await new Promise((r) => setTimeout(r, 20));
  expect(onError).not.toHaveBeenCalled();
});

it('shows the few-options hint only once the listing has loaded (Task 11 review)', async () => {
  const user = userEvent.setup();
  let release: (options: ContentOption[]) => void = () => {};
  vi.mocked(client.listContent).mockImplementation(() => new Promise((resolve) => (release = resolve)));
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(nextSpecies());
  expect(screen.getByRole('form', { name: 'Species' })).toBeTruthy();
  expect(screen.queryByText(/installed for/)).toBeNull();
  release([content(1, 'species', 'Fixture Hillfolk', 'srd-5.1')]);
  expect(await screen.findByText(/1 species is installed for SRD 5\.1/)).toBeTruthy();
});

it('shows no few-options hint after a failed listing', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockRejectedValue(new Error('fixture failure'));
  const onError = vi.fn();
  await toScores(user, onError);
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(nextSpecies());
  await waitFor(() => expect(onError).toHaveBeenCalled());
  expect(screen.queryByText(/installed for/)).toBeNull();
});
