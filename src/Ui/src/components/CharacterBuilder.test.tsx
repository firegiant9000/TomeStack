// @vitest-environment jsdom
// The builder's spell picker (D30): the legend's counts say what is recorded, not what the search shows. The client is mocked;
// names and values are invented.
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterView, ContentOption, RulesFamilyPolicy, SpellcastingEntry } from '../api/types';
import { CharacterBuilder } from './CharacterBuilder';

vi.mock('../api/client', () => ({ client: { listCampaigns: vi.fn(), listContent: vi.fn(), preview: vi.fn(), previewChoice: vi.fn(), createCharacter: vi.fn(), saveCharacter: vi.fn(), rollDice: vi.fn() } }));

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
  expect(outsider.getAttribute('aria-disabled')).toBe('true'); // inert, never natively disabled (2.4.3)
  expect(outsider.disabled).toBe(false);
  expect(outsider.closest('label')!.textContent).toMatch(/not allowed in this campaign/);
  await user.click(outsider); // a press on an inert option picks nothing
  expect(outsider.checked).toBe(false);
  // Of the other family and outside the campaign: the family rule wins (R32), it is never listed, with or without a reason.
  expect(screen.queryByRole('radio', { name: /^Fixture Outlander/ })).toBeNull();
  expect(screen.queryByText(/Options for the other family cannot be selected/)).toBeNull();
  // An own-family option outside the campaign becomes selectable once an exception reason is given (P-01).
  await user.click(screen.getByRole('checkbox', { name: 'Use content from outside the campaign' }));
  await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'DM approved');
  await waitFor(() => expect(screen.getByRole('radio', { name: /^Fixture Outsider/ }).getAttribute('aria-disabled')).toBeNull());
  expect(screen.queryByRole('radio', { name: /^Fixture Outlander/ })).toBeNull();
  await user.clear(screen.getByRole('textbox', { name: /Reason/ }));
  await waitFor(() => expect(screen.getByRole('radio', { name: /^Fixture Outsider/ }).getAttribute('aria-disabled')).toBe('true'));
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

// R42 (follow-up F2): another option of the same choice can be ticked while an imported other-family option stays chosen.
it('previews a tick on an own-family option while a kept other-family option stays in the selection (R42)', async () => {
  const user = userEvent.setup();
  const mine = content(7, 'feature', 'Fixture Mine', 'srd-5.1');
  const imported = content(8, 'feature', 'Fixture Imported', 'srd-5.2.1');
  vi.mocked(client.listContent).mockResolvedValue([mine, imported]);
  const source = { contentId: 'fixture-feature', revisionId: 'fixture-feature-r1' };
  const answer = (selected: ContentOption[]) => {
    const v = viewWith([]);
    v.sheet.choices = [{ source, sourceName: 'Fixture feature', choiceId: 'pick', count: 2, options: [mine.reference, imported.reference], selected: selected.map((o) => o.reference), resolved: selected.length === 2 }];
    return v;
  };
  vi.mocked(client.previewChoice).mockReset();
  vi.mocked(client.previewChoice).mockResolvedValue(answer([imported, mine]));
  render(<CharacterBuilder mode={{ kind: 'choices', view: answer([imported]) }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  const picker = await screen.findByRole('group', { name: /Fixture feature: choose 2/ });
  await user.click(await within(picker).findByRole('checkbox', { name: /Fixture Mine/ }));
  await waitFor(() => expect(vi.mocked(client.previewChoice)).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.previewChoice).mock.calls[0]![3]).toEqual([imported.reference, mine.reference]); // the kept option is re-sent; the service accepts it
  await waitFor(() => expect((within(picker).getByRole('checkbox', { name: /Fixture Mine/ }) as HTMLInputElement).checked).toBe(true));
  expect((within(picker).getByRole('checkbox', { name: /Fixture Imported/ }) as HTMLInputElement).checked).toBe(true); // the kept row stays
});

// R33 (2.4.3): unticking a chosen option that is not offerable never removes the focused checkbox from the page.
const choiceView = (selected: ContentOption[], options: ContentOption[], campaignId?: string): CharacterView => {
  const view = viewWith([]);
  view.character = { ...view.character, campaignId };
  view.sheet.choices = [
    { source: { contentId: 'fixture-feature', revisionId: 'fixture-feature-r1' }, sourceName: 'Fixture feature', choiceId: 'pick', count: 2, options: options.map((o) => o.reference), selected: selected.map((o) => o.reference), resolved: false },
  ];
  return view;
};

it('keeps an unticked other-family option in the choice, focused and inert (R33, 2.4.3)', async () => {
  const user = userEvent.setup();
  const mine = content(7, 'feature', 'Fixture Mine', 'srd-5.1');
  const imported = content(8, 'feature', 'Fixture Imported', 'srd-5.2.1');
  vi.mocked(client.listContent).mockResolvedValue([mine, imported]);
  vi.mocked(client.previewChoice).mockResolvedValue(choiceView([], [mine, imported]));
  render(<CharacterBuilder mode={{ kind: 'choices', view: choiceView([imported], [mine, imported]) }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  const picker = await screen.findByRole('group', { name: /Fixture feature: choose 2/ });
  const box = (await within(picker).findByRole('checkbox', { name: /Fixture Imported/ })) as HTMLInputElement;
  await user.click(box);
  await waitFor(() => expect(vi.mocked(client.previewChoice)).toHaveBeenCalled());
  await waitFor(() => expect(box.checked).toBe(false));
  const after = within(picker).getByRole('checkbox', { name: /Fixture Imported/ }) as HTMLInputElement;
  expect(after).toBe(box); // the same element: never unmounted
  expect(document.activeElement).toBe(box);
  expect(box.disabled).toBe(false); // aria-disabled, never disabled under focus
  expect(box.getAttribute('aria-disabled')).toBe('true');
  vi.mocked(client.previewChoice).mockClear();
  await user.click(box); // inert: it cannot be ticked again
  expect(vi.mocked(client.previewChoice)).not.toHaveBeenCalled();
});

it('keeps an unticked campaign-outside option in the choice, focused and inert (R33)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listCampaigns).mockResolvedValue([{ id: 'fixture-camp', name: 'Fixture Campaign', rulesFamily: 'srd-5.1', allowedSources: ['fixture-source'] }]);
  const mine = content(7, 'feature', 'Fixture Mine', 'srd-5.1');
  const outsider = content(8, 'feature', 'Fixture Outsider', 'srd-5.1', { allowedInCampaign: false });
  vi.mocked(client.listContent).mockResolvedValue([mine, outsider]);
  vi.mocked(client.previewChoice).mockResolvedValue(choiceView([], [mine, outsider], 'fixture-camp'));
  render(<CharacterBuilder mode={{ kind: 'choices', view: choiceView([outsider], [mine, outsider], 'fixture-camp') }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  const picker = await screen.findByRole('group', { name: /Fixture feature: choose 2/ });
  const box = (await within(picker).findByRole('checkbox', { name: /Fixture Outsider/ })) as HTMLInputElement;
  await user.click(box);
  await waitFor(() => expect(box.checked).toBe(false));
  expect(within(picker).getByRole('checkbox', { name: /Fixture Outsider/ })).toBe(box);
  expect(document.activeElement).toBe(box);
  expect(box.getAttribute('aria-disabled')).toBe('true');
  expect(box.closest('li')!.textContent).toMatch(/not allowed in this campaign/);
});

// R34 (owner): spells are campaign-restricted like other content.
describe('spells and the campaign (R34)', () => {
  const campaignView = () => {
    const view = viewWith([]);
    view.character = { ...view.character, campaignId: 'fixture-camp' };
    return view;
  };
  const outsideSpell = { ...spell('fixture-hex', 'Fixture Hex', 1), allowedInCampaign: false };
  const otherFamilySpell = { ...spell('fixture-wisp', 'Fixture Wisp', 1), rulesFamilies: ['srd-5.2.1' as const], compatible: false, allowedInCampaign: false };

  beforeEach(() => {
    vi.mocked(client.listCampaigns).mockResolvedValue([{ id: 'fixture-camp', name: 'Fixture Campaign', rulesFamily: 'srd-5.1', allowedSources: ['fixture-source'] }]);
    vi.mocked(client.listContent).mockResolvedValue([...spells, outsideSpell, otherFamilySpell]);
    vi.mocked(client.preview).mockImplementation(async (draft) => ({ ...viewWith(draft.spells), character: draft }));
    vi.mocked(client.saveCharacter).mockReset();
  });

  it('lists an own-family outside spell inert with the reason, never an other-family one, and records an exception once a reason is given', async () => {
    const user = userEvent.setup();
    vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ ...viewWith(c.spells), character: c }));
    render(<CharacterBuilder mode={{ kind: 'choices', view: campaignView() }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
    const picker = await screen.findByRole('group', { name: /Fixture caster spells/ });
    const hex = (await within(picker).findByRole('checkbox', { name: /Fixture Hex/ })) as HTMLInputElement;
    expect(within(picker).queryByRole('checkbox', { name: /Fixture Wisp/ })).toBeNull();
    expect(hex.getAttribute('aria-disabled')).toBe('true');
    expect(hex.closest('li')!.textContent).toMatch(/not allowed in this campaign/);
    await user.click(hex);
    expect(vi.mocked(client.preview)).not.toHaveBeenCalled();
    expect(hex.checked).toBe(false);

    await user.click(screen.getByRole('checkbox', { name: 'Use content from outside the campaign' }));
    await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'DM approved');
    await waitFor(() => expect(hex.getAttribute('aria-disabled')).toBeNull());
    expect(within(picker).queryByRole('checkbox', { name: /Fixture Wisp/ })).toBeNull();
    await user.click(hex);
    await waitFor(() => expect(hex.checked).toBe(true));
    await user.click(screen.getByRole('button', { name: 'Save choices' }));
    await waitFor(() => expect(vi.mocked(client.saveCharacter)).toHaveBeenCalled());
    const saved = vi.mocked(client.saveCharacter).mock.calls[0]![0];
    expect(saved.campaignExceptions).toEqual([expect.objectContaining({ content: outsideSpell.reference, reason: 'DM approved' })]);
  });

  it('records no exception for a spell the campaign allows', async () => {
    const user = userEvent.setup();
    vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ ...viewWith(c.spells), character: c }));
    render(<CharacterBuilder mode={{ kind: 'choices', view: campaignView() }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
    const picker = await screen.findByRole('group', { name: /Fixture caster spells/ });
    const bolt = (await within(picker).findByRole('checkbox', { name: /Fixture Bolt/ })) as HTMLInputElement;
    expect(bolt.getAttribute('aria-disabled')).toBeNull();
    await user.click(bolt);
    await waitFor(() => expect(bolt.checked).toBe(true));
    await user.click(screen.getByRole('checkbox', { name: 'Use content from outside the campaign' }));
    await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'DM approved');
    await user.click(screen.getByRole('button', { name: 'Save choices' }));
    await waitFor(() => expect(vi.mocked(client.saveCharacter)).toHaveBeenCalled());
    expect(vi.mocked(client.saveCharacter).mock.calls[0]![0].campaignExceptions).toEqual([]);
  });
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
/** Next is aria-disabled, never natively `disabled` (2.4.3). */
const nextOff = () => {
  const button = nextSpecies();
  expect(button.disabled).toBe(false);
  return button.getAttribute('aria-disabled') === 'true';
};
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
  expect(nextOff()).toBe(true);
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
  expect(nextOff()).toBe(true);
  for (const [label, value] of arrayOrder.slice(1)) await user.selectOptions(within(group).getByRole('combobox', { name: label }), value);
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  expect(nextOff()).toBe(false);
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

it('lists a review of the draft above the choices (D32)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  vi.mocked(client.preview).mockResolvedValue(draftView());
  await toScores(user);
  await assignArray(user);
  await user.click(nextSpecies());
  await user.click(await screen.findByRole('radio', { name: /^Fixture Hillfolk/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await user.click(screen.getByRole('radio', { name: /^Fixture Warden/ }));
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  const review = await screen.findByRole('region', { name: 'Review' });
  expect(review.textContent).toMatch(/Name: Fixture New/);
  expect(review.textContent).toMatch(/Rules: SRD 5\.1/);
  expect(review.textContent).toMatch(/Campaign: none/);
  expect(review.textContent).toMatch(/Scores: Str 15, Dex 14, Con 13, Int 12, Wis 10, Cha 8 \(standard array\)/);
  expect(review.textContent).toMatch(/Species: Fixture Hillfolk/);
  expect(review.textContent).toMatch(/Class: Fixture Warden/);
  expect(review.textContent).toMatch(/Background: none/);
});

it('names the rules family, the campaign and a hand-entered method in the review, and shows no review for level-up or choices (carry 9)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listCampaigns).mockResolvedValue([{ id: 'fixture-camp', name: 'Fixture Campaign', rulesFamily: 'srd-5.2.1', allowedSources: ['fixture-source'] }]);
  vi.mocked(client.listContent).mockResolvedValue([content(1, 'species', 'Fixture Hillfolk', 'srd-5.2.1', { compatible: true })]);
  vi.mocked(client.preview).mockResolvedValue(draftView());
  renderCreate();
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Campaign' }), 'fixture-camp');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  for (const next of ['Next: species', 'Next: class', 'Next: background', 'Next: choices']) await user.click(await screen.findByRole('button', { name: next }));
  const review = await screen.findByRole('region', { name: 'Review' });
  expect(review.textContent).toMatch(/Rules: SRD 5\.2\.1/);
  expect(review.textContent).toMatch(/Campaign: Fixture Campaign/);
  expect(review.textContent).not.toMatch(/Campaign: none/);
  expect(review.textContent).toMatch(/\(enter by hand\)/);
  cleanup();
  render(<CharacterBuilder mode={{ kind: 'choices', view: viewWith([]) }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  await screen.findByRole('group', { name: 'Choices' });
  expect(screen.queryByRole('region', { name: 'Review' })).toBeNull();
  cleanup();
  render(<CharacterBuilder mode={{ kind: 'levelUp', view: viewWith([]) }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  expect(screen.getByRole('form', { name: 'Level' })).toBeTruthy();
  expect(screen.queryByRole('region', { name: 'Review' })).toBeNull();
});

it('counts point buy against 27 and blocks Next when over budget', async () => {
  const user = userEvent.setup();
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Point buy' }));
  const group = screen.getByRole('group', { name: 'Point buy' });
  expect(screen.getByText('Points left: 27 of 27')).toBeTruthy();
  expect(screen.getByText('You can still spend 27 points.')).toBeTruthy();
  expect(nextOff()).toBe(false);
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
  expect(nextOff()).toBe(true);
  expect(screen.getByText('Spend at most 27 points to continue.')).toBeTruthy();
});

it('blocks Next for a point buy score outside 8 to 15', async () => {
  const user = userEvent.setup();
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Point buy' }));
  const str = within(screen.getByRole('group', { name: 'Point buy' })).getByRole('spinbutton', { name: 'Strength' });
  await user.clear(str);
  await user.type(str, '16');
  expect(nextOff()).toBe(true);
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
  expect(nextOff()).toBe(false);
  expect(screen.getByText(/Base scores: Str 15, Dex 14/)).toBeTruthy();
});

it('rolls six sets through dice.roll with keepHighest 3 and assigns them', async () => {
  const user = userEvent.setup();
  queueSets(sixSets());
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  expect(screen.getByText('Roll the scores to continue.')).toBeTruthy();
  expect(nextOff()).toBe(true);
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
  expect(nextOff()).toBe(true);
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
  expect(nextOff()).toBe(true);
  await user.click(screen.getByRole('radio', { name: 'Standard array' }));
  expect(screen.getByText('Assigned: 0 of 6')).toBeTruthy();
  expect(nextOff()).toBe(true);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  await screen.findByText('Roll 1: 14 (6, 5, 3, dropped 2)');
  expect(screen.getByText('Assigned: 0 of 6')).toBeTruthy();
  expect(nextOff()).toBe(true);
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
  expect(nextOff()).toBe(false);
  await user.click(screen.getByRole('radio', { name: 'Standard array' }));
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  expect(screen.getByText(/Base scores: Str 8, Dex 10, Con 12, Int 13, Wis 14, Cha 15/)).toBeTruthy();
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  expect(screen.getByText('Assigned: 6 of 6')).toBeTruthy();
  expect(nextOff()).toBe(false);
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
  expect(nextOff()).toBe(false);
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

// Checkpoint B fixes G2 (2.4.3, async): Next, the roll, the preview and the commit never act on a draft that has moved on.
const pendingRolls = () => {
  const pending: Array<(value: never) => void> = [];
  vi.mocked(client.rollDice).mockImplementation(() => new Promise((resolve) => pending.push(resolve as never)));
  return pending;
};
const settleRolls = async (pending: Array<(value: never) => void>, sets: ReturnType<typeof sixSets>) => {
  for (const s of sets) {
    await waitFor(() => expect(pending.length).toBeGreaterThan(0));
    pending.shift()!(s as never);
  }
};
const rollAndAssignAll = async (user: ReturnType<typeof userEvent.setup>) => {
  queueSets(arrayTotals());
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  const group = await screen.findByRole('group', { name: 'Assign the rolled scores' });
  for (const [label, value] of [['Strength', '8'], ['Dexterity', '10'], ['Constitution', '12'], ['Intelligence', '13'], ['Wisdom', '14'], ['Charisma', '15']] as const)
    await user.selectOptions(within(group).getByRole('combobox', { name: label }), value);
};

it('keeps Next inert and focused while Roll again is in flight, and a landing roll leaves it inert (2.4.3)', async () => {
  const user = userEvent.setup();
  await toScores(user);
  await rollAndAssignAll(user);
  expect(nextOff()).toBe(false);
  const pending = pendingRolls();
  await user.click(screen.getByRole('button', { name: 'Roll again' }));
  const next = nextSpecies();
  expect(next.getAttribute('aria-disabled')).toBe('true');
  next.focus();
  await user.click(next); // pressed while rolling: nothing happens
  expect(screen.getByRole('form', { name: 'Ability scores' })).toBeTruthy();
  await settleRolls(pending, sixSets());
  expect(await screen.findByText('Assigned: 0 of 6')).toBeTruthy();
  expect(nextSpecies()).toBe(next); // the same element, never replaced or natively disabled
  expect(next.disabled).toBe(false);
  expect(next.getAttribute('aria-disabled')).toBe('true');
  expect(document.activeElement).toBe(next);
});

it('drops a roll that lands after the step was left, and the draft keeps its scores', async () => {
  const user = userEvent.setup();
  const pending = pendingRolls();
  await toScores(user);
  await user.click(screen.getByRole('radio', { name: 'Roll' }));
  await user.click(screen.getByRole('button', { name: 'Roll six scores (4d6, drop the lowest)' }));
  await user.click(screen.getByRole('button', { name: 'Back' }));
  await settleRolls(pending, sixSets());
  await new Promise((r) => setTimeout(r, 20));
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  expect(screen.queryByRole('list', { name: 'Rolled sets' })).toBeNull();
  expect(screen.getByText('Roll the scores to continue.')).toBeTruthy();
});

it('previews with the rolled assignment as the base scores (L5)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  vi.mocked(client.preview).mockResolvedValue(draftView());
  await toScores(user);
  await rollAndAssignAll(user);
  for (const next of ['Next: species', 'Next: class', 'Next: background', 'Next: choices']) await user.click(await screen.findByRole('button', { name: next }));
  await screen.findByRole('group', { name: 'Choices' });
  expect(vi.mocked(client.preview).mock.calls[0]![0].baseAbilities).toEqual({ str: 8, dex: 10, con: 12, int: 13, wis: 14, cha: 15 });
});

it('asks for a name before Next, with the reason linked to the button (carry 7)', async () => {
  const user = userEvent.setup();
  renderCreate();
  const next = screen.getByRole('button', { name: 'Next: ability scores' });
  expect(next.getAttribute('aria-disabled')).toBe('true');
  expect(next.hasAttribute('disabled')).toBe(false);
  expect(next.getAttribute('aria-describedby')).toBe('next-hint');
  expect(screen.getByText('Enter a name to continue.')).toBeTruthy();
  await user.type(screen.getByRole('textbox', { name: 'Name' }), '   ');
  expect(next.getAttribute('aria-disabled')).toBe('true');
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  expect(next.getAttribute('aria-disabled')).toBeNull();
  expect(screen.queryByText('Enter a name to continue.')).toBeNull();
});

const twoClasses = () => [content(1, 'species', 'Fixture Hillfolk', 'srd-5.1'), content(3, 'class', 'Fixture Warden', 'srd-5.1'), content(4, 'class', 'Fixture Ranger', 'srd-5.1')];
async function toBackground(user: ReturnType<typeof userEvent.setup>, cls: RegExp, extra: { onCommitted?: () => void | Promise<void>; onCancel?: () => void; onError?: (e: unknown) => void } = {}) {
  render(<CharacterBuilder mode={{ kind: 'create' }} rulesFamilies={families} onCommitted={extra.onCommitted ?? (() => {})} onCancel={extra.onCancel ?? (() => {})} onError={extra.onError ?? (() => {})} />);
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  await user.click(await screen.findByRole('radio', { name: /^Fixture Hillfolk/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await user.click(screen.getByRole('radio', { name: cls }));
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
}

it('cancels the jump to the choices when Back is pressed while the preview is pending (L2)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(twoClasses());
  const replies: Array<(view: CharacterView) => void> = [];
  vi.mocked(client.preview).mockImplementation(() => new Promise((resolve) => replies.push(resolve)));
  await toBackground(user, /^Fixture Warden/);
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await user.click(screen.getByRole('button', { name: 'Back' })); // to Class, before the preview answers
  await user.click(screen.getByRole('radio', { name: /^Fixture Ranger/ }));
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
  replies[0]!(draftView()); // the old class's reply arrives late
  await new Promise((r) => setTimeout(r, 20));
  expect(screen.getByRole('form', { name: 'Background' })).toBeTruthy();
  expect(screen.queryByRole('group', { name: 'Choices' })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  expect(replies).toHaveLength(2);
  expect(vi.mocked(client.preview).mock.calls[1]![0].classes[0]!.class.contentId).toBe('fixture-c4');
  const second = draftView();
  second.character = { ...second.character, classes: [{ class: { contentId: 'fixture-c4', revisionId: 'fixture-r4' }, level: 1 }] };
  replies[1]!(second);
  const review = await screen.findByRole('region', { name: 'Review' });
  expect(review.textContent).toMatch(/Class: Fixture Ranger/);
});

it('ignores a second Create press, keeps focus on the button when the save fails, and Cancel waits (L3, 2.4.3)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  vi.mocked(client.preview).mockResolvedValue(draftView());
  let fail: (error: unknown) => void = () => {};
  vi.mocked(client.createCharacter).mockReset();
  vi.mocked(client.createCharacter).mockImplementation(() => new Promise((_, reject) => (fail = reject)));
  const onError = vi.fn();
  const onCancel = vi.fn();
  await toBackground(user, /^Fixture Warden/, { onError, onCancel });
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  const create = await screen.findByRole('button', { name: 'Create and save' });
  await user.click(create);
  const busy = screen.getByRole('button', { name: 'Saving…' });
  expect(busy).toBe(create);
  expect(busy.getAttribute('aria-disabled')).toBe('true');
  expect(busy.hasAttribute('disabled')).toBe(false);
  await user.click(busy);
  expect(vi.mocked(client.createCharacter)).toHaveBeenCalledTimes(1);
  const cancel = screen.getByRole('button', { name: 'Cancel' });
  expect(cancel.getAttribute('aria-disabled')).toBe('true');
  await user.click(cancel);
  expect(onCancel).not.toHaveBeenCalled();
  create.focus(); // the press that started the save left the focus here
  fail(new Error('fixture failure'));
  await waitFor(() => expect(onError).toHaveBeenCalledTimes(1));
  expect(document.activeElement).toBe(create);
  expect(create.getAttribute('aria-disabled')).toBeNull();
  expect(create.textContent).toBe('Create and save');
});

it('still reports a save that lands after the builder is gone, and raises no error (R36)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  vi.mocked(client.preview).mockResolvedValue(draftView());
  const pendingCreates: Array<{ ok: (view: CharacterView) => void; fail: (error: unknown) => void }> = [];
  vi.mocked(client.createCharacter).mockReset();
  vi.mocked(client.createCharacter).mockImplementation(() => new Promise((ok, fail) => pendingCreates.push({ ok, fail })));
  const onError = vi.fn();
  const onCommitted = vi.fn();
  await toBackground(user, /^Fixture Warden/, { onError, onCommitted });
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  cleanup(); // the screen was left while the save was in flight
  pendingCreates[0]!.ok(draftView());
  await waitFor(() => expect(onCommitted).toHaveBeenCalledTimes(1)); // the save happened: the app must refresh and say so
  expect(onError).not.toHaveBeenCalled();
});

it('keeps focus on a campaign-outside choice option whose reason is cleared, and on a row when the count fills (N1, 2.4.3)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listCampaigns).mockResolvedValue([{ id: 'fixture-camp', name: 'Fixture Campaign', rulesFamily: 'srd-5.1', allowedSources: ['fixture-source'] }]);
  const mine = content(7, 'feature', 'Fixture Mine', 'srd-5.1');
  const outsider = content(8, 'feature', 'Fixture Outsider', 'srd-5.1', { allowedInCampaign: false });
  const third = content(9, 'feature', 'Fixture Third', 'srd-5.1');
  vi.mocked(client.listContent).mockResolvedValue([mine, outsider, third]);
  const view = (selected: ContentOption[]) => {
    const v = choiceView(selected, [mine, outsider, third], 'fixture-camp');
    v.sheet.choices![0]!.count = 1;
    return v;
  };
  vi.mocked(client.previewChoice).mockImplementation(async (_c, _s, _id, selected) => view([mine, outsider, third].filter((o) => selected.some((s) => s.revisionId === o.reference.revisionId))));
  render(<CharacterBuilder mode={{ kind: 'choices', view: view([]) }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  const picker = await screen.findByRole('group', { name: /Fixture feature: choose 1/ });
  const box = (name: RegExp) => within(picker).getByRole('checkbox', { name }) as HTMLInputElement;
  await within(picker).findByRole('checkbox', { name: /Fixture Outsider/ });
  // (a) give a reason, tick the outside option, clear the reason, untick it: the box stays enabled and focused.
  await user.click(screen.getByRole('checkbox', { name: 'Use content from outside the campaign' }));
  await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'DM approved');
  await user.click(box(/Fixture Outsider/));
  await waitFor(() => expect(box(/Fixture Outsider/).checked).toBe(true));
  await user.clear(screen.getByRole('textbox', { name: /Reason/ }));
  await user.click(box(/Fixture Outsider/));
  await waitFor(() => expect(box(/Fixture Outsider/).checked).toBe(false));
  expect(box(/Fixture Outsider/).disabled).toBe(false);
  expect(document.activeElement).toBe(box(/Fixture Outsider/));
  expect(box(/Fixture Outsider/).getAttribute('aria-disabled')).toBe('true');
  // (b) tick one, Tab to another row before the answer fills the count: the other row is inert, not disabled, and keeps focus.
  await user.click(box(/Fixture Mine/));
  box(/Fixture Third/).focus();
  await waitFor(() => expect(box(/Fixture Mine/).checked).toBe(true));
  expect(box(/Fixture Third/).disabled).toBe(false);
  expect(box(/Fixture Third/).getAttribute('aria-disabled')).toBe('true');
  expect(document.activeElement).toBe(box(/Fixture Third/));
});

it('ignores a stale preview reply when the Background pick changes while it is pending (N2)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue([...stepContent(), content(5, 'background', 'Fixture Acolyte', 'srd-5.1'), content(6, 'background', 'Fixture Sailor', 'srd-5.1')]);
  const replies: Array<(view: CharacterView) => void> = [];
  vi.mocked(client.preview).mockImplementation(() => new Promise((resolve) => replies.push(resolve)));
  await toBackground(user, /^Fixture Warden/);
  await user.click(await screen.findByRole('radio', { name: /^Fixture Acolyte/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await user.click(screen.getByRole('radio', { name: /^Fixture Sailor/ })); // changed before the reply
  replies[0]!(draftView());
  await new Promise((r) => setTimeout(r, 20));
  expect(screen.getByRole('form', { name: 'Background' })).toBeTruthy();
  expect(screen.queryByRole('group', { name: 'Choices' })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  expect(vi.mocked(client.preview).mock.calls[1]![0].pins.map((p) => p.contentId)).toContain('fixture-c6');
  replies[1]!(draftView());
  const review = await screen.findByRole('region', { name: 'Review' });
  expect(review.textContent).toMatch(/Background: Fixture Sailor/);
});

it('says why Next waits while Roll again is in flight (carry N4)', async () => {
  const user = userEvent.setup();
  await toScores(user);
  await rollAndAssignAll(user);
  expect(screen.queryByText('Rolling the scores…')).toBeNull();
  const pending = pendingRolls();
  await user.click(screen.getByRole('button', { name: 'Roll again' }));
  expect(screen.getByText('Rolling the scores…')).toBeTruthy();
  expect(nextSpecies().getAttribute('aria-describedby')).toBe('next-hint');
  await settleRolls(pending, sixSets());
  await waitFor(() => expect(screen.queryByText('Rolling the scores…')).toBeNull());
});

it('does not create twice while the app handles a successful create, and is usable again if the builder stays (R39)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listContent).mockResolvedValue(stepContent());
  vi.mocked(client.preview).mockResolvedValue(draftView());
  vi.mocked(client.createCharacter).mockReset();
  vi.mocked(client.createCharacter).mockResolvedValue(draftView());
  let settle: () => void = () => {};
  const onCommitted = vi.fn(() => new Promise<void>((resolve) => (settle = resolve))); // the app is still refreshing
  await toBackground(user, /^Fixture Warden/, { onCommitted });
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  const create = await screen.findByRole('button', { name: 'Create and save' });
  await user.click(create);
  await waitFor(() => expect(onCommitted).toHaveBeenCalledTimes(1));
  const again = screen.getByRole('button', { name: 'Saving…' });
  expect(again.getAttribute('aria-disabled')).toBe('true');
  await user.click(again);
  expect(vi.mocked(client.createCharacter)).toHaveBeenCalledTimes(1);
  settle(); // the app finished and the builder was not replaced: nothing stays stuck on "Saving…"
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create and save' }).getAttribute('aria-disabled')).toBeNull());
});

// R38: a level-up draft with one existing class.
const levelView = (campaignId?: string): CharacterView => {
  const view = viewWith([]);
  view.character = { ...view.character, campaignId, classes: [{ class: { contentId: 'fixture-class', revisionId: 'fixture-class-r1' }, level: 1 }], level: 1 };
  return view;
};

it('keeps a level-up preview that was asked for before the listing arrived, when the listing drops nothing (R38 c)', async () => {
  const user = userEvent.setup();
  let list: (options: ContentOption[]) => void = () => {};
  vi.mocked(client.listContent).mockImplementation(() => new Promise((resolve) => (list = resolve)));
  let answer: (view: CharacterView) => void = () => {};
  vi.mocked(client.preview).mockImplementation(() => new Promise((resolve) => (answer = resolve)));
  render(<CharacterBuilder mode={{ kind: 'levelUp', view: levelView() }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  list([]); // the first listing arrives; no pick exists to drop
  await new Promise((r) => setTimeout(r, 20));
  answer(viewWith([]));
  expect(await screen.findByRole('group', { name: 'Choices' })).toBeTruthy();
});

it('says why Next: choices waits when the picked new class needs a campaign reason (R38 d)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listCampaigns).mockResolvedValue([{ id: 'fixture-camp', name: 'Fixture Campaign', rulesFamily: 'srd-5.1', allowedSources: ['fixture-source'] }]);
  vi.mocked(client.listContent).mockResolvedValue([content(5, 'class', 'Fixture Outsider', 'srd-5.1', { allowedInCampaign: false })]);
  vi.mocked(client.preview).mockClear();
  render(<CharacterBuilder mode={{ kind: 'levelUp', view: levelView('fixture-camp') }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  await user.click(await screen.findByRole('checkbox', { name: 'Use content from outside the campaign' }));
  await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'DM approved');
  await user.click(await screen.findByRole('radio', { name: /^Fixture Outsider/ }));
  const next = screen.getByRole('button', { name: 'Next: choices' });
  expect(next.getAttribute('aria-disabled')).toBeNull();
  await user.clear(screen.getByRole('textbox', { name: /Reason/ })); // the reason goes after the class was picked
  expect(next.getAttribute('aria-disabled')).toBe('true');
  expect(next.getAttribute('aria-describedby')).toBe('next-hint');
  expect(screen.getByText('This class needs a campaign exception reason.')).toBeTruthy();
  await user.click(next);
  expect(vi.mocked(client.preview)).not.toHaveBeenCalled();
});

// R41 (follow-up F1): a pick that needs a campaign exception reason is never dropped when the reason goes. Progress waits,
// with the reason shown and linked; the player resolves it by giving a reason again or by unpicking.
const fixtureCampaign = { id: 'fixture-camp', name: 'Fixture Campaign', rulesFamily: 'srd-5.1' as const, allowedSources: ['fixture-source'] };
const reasonField = () => screen.getByRole('textbox', { name: /Reason/ });
async function allowOutside(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('checkbox', { name: 'Use content from outside the campaign' }));
  await user.type(reasonField(), 'DM approved');
}
async function toSpeciesInCampaign(user: ReturnType<typeof userEvent.setup>) {
  render(<CharacterBuilder mode={{ kind: 'create' }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Fixture New');
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Campaign' }), 'fixture-camp');
  await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
}

describe('a pick that needs a campaign exception reason after the reason is cleared (R41)', () => {
  beforeEach(() => {
    vi.mocked(client.listCampaigns).mockResolvedValue([fixtureCampaign]);
    vi.mocked(client.preview).mockClear();
    vi.mocked(client.createCharacter).mockReset();
    vi.mocked(client.saveCharacter).mockReset();
  });

  it('holds the Species step, keeps the pick and the focus, and proceeds once a reason is given again', async () => {
    const user = userEvent.setup();
    vi.mocked(client.listContent).mockResolvedValue([content(1, 'species', 'Fixture Hillfolk', 'srd-5.1'), content(2, 'species', 'Fixture Outsider', 'srd-5.1', { allowedInCampaign: false })]);
    await toSpeciesInCampaign(user);
    await allowOutside(user);
    await user.click(await screen.findByRole('radio', { name: /^Fixture Outsider/ }));
    const next = screen.getByRole('button', { name: 'Next: class' });
    expect(next.getAttribute('aria-disabled')).toBeNull();
    await user.clear(reasonField());
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.hasAttribute('disabled')).toBe(false);
    expect(next.getAttribute('aria-describedby')).toBe('next-hint');
    expect(screen.getByText('Fixture Outsider needs a campaign exception reason.')).toBeTruthy();
    expect((screen.getByRole('radio', { name: /^Fixture Outsider/ }) as HTMLInputElement).checked).toBe(true); // the pick is kept
    await user.click(next);
    expect(screen.getByRole('form', { name: 'Species' })).toBeTruthy();
    expect(document.activeElement).toBe(next);
    await user.type(reasonField(), 'DM approved');
    expect(next.getAttribute('aria-disabled')).toBeNull();
    expect(screen.queryByText(/needs a campaign exception reason/)).toBeNull();
    await user.click(next);
    expect(screen.getByRole('form', { name: 'Class' })).toBeTruthy();
  });

  it('lets the player unpick the species instead, and never blocks a pick the campaign allows', async () => {
    const user = userEvent.setup();
    vi.mocked(client.listContent).mockResolvedValue([content(1, 'species', 'Fixture Hillfolk', 'srd-5.1'), content(2, 'species', 'Fixture Outsider', 'srd-5.1', { allowedInCampaign: false })]);
    await toSpeciesInCampaign(user);
    await allowOutside(user);
    await user.click(await screen.findByRole('radio', { name: /^Fixture Outsider/ }));
    await user.clear(reasonField());
    const next = screen.getByRole('button', { name: 'Next: class' });
    expect(next.getAttribute('aria-disabled')).toBe('true');
    await user.click(screen.getByRole('radio', { name: 'None' }));
    expect(next.getAttribute('aria-disabled')).toBeNull();
    await user.click(screen.getByRole('radio', { name: /^Fixture Hillfolk/ }));
    expect(next.getAttribute('aria-disabled')).toBeNull();
    expect(screen.queryByText(/needs a campaign exception reason/)).toBeNull();
  });

  it('holds the Background step for an Other content pick, and the preview is not asked for', async () => {
    const user = userEvent.setup();
    vi.mocked(client.listContent).mockResolvedValue([...stepContent(), content(5, 'feat', 'Fixture Outside Feat', 'srd-5.1', { allowedInCampaign: false })]);
    vi.mocked(client.preview).mockResolvedValue(draftView());
    await toSpeciesInCampaign(user);
    await allowOutside(user);
    await user.click(await screen.findByRole('radio', { name: /^Fixture Hillfolk/ }));
    await user.click(screen.getByRole('button', { name: 'Next: class' }));
    await user.click(screen.getByRole('radio', { name: /^Fixture Warden/ }));
    await user.click(screen.getByRole('button', { name: 'Next: background' }));
    await user.click(await screen.findByRole('checkbox', { name: /Fixture Outside Feat/ }));
    const next = screen.getByRole('button', { name: 'Next: choices' });
    await user.clear(reasonField());
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.getAttribute('aria-describedby')).toBe('next-hint');
    expect(screen.getByText('Fixture Outside Feat needs a campaign exception reason.')).toBeTruthy();
    expect((screen.getByRole('checkbox', { name: /Fixture Outside Feat/ }) as HTMLInputElement).checked).toBe(true);
    await user.click(next);
    expect(vi.mocked(client.preview)).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(next);
    await user.type(reasonField(), 'DM approved');
    await user.click(next);
    await waitFor(() => expect(vi.mocked(client.preview)).toHaveBeenCalledTimes(1));
    expect(vi.mocked(client.preview).mock.calls[0]![0].pins.map((p) => p.contentId)).toContain('fixture-c5');
  });

  it('holds Create and save for a choice option, keeps the focus, and records the exception once a reason is given again', async () => {
    const user = userEvent.setup();
    const source = { contentId: 'fixture-feature', revisionId: 'fixture-feature-r1' };
    const mine = content(7, 'feature', 'Fixture Mine', 'srd-5.1');
    const outsider = content(8, 'feature', 'Fixture Outsider', 'srd-5.1', { allowedInCampaign: false });
    vi.mocked(client.listContent).mockResolvedValue([mine, outsider]);
    const answer = (selected: ContentOption[]) => {
      const v = choiceView(selected, [mine, outsider], 'fixture-camp');
      v.character = { ...v.character, choices: [{ source, choiceId: 'pick', selected: selected.map((o) => o.reference) }] };
      return v;
    };
    vi.mocked(client.previewChoice).mockImplementation(async (_c, _s, _id, selected) => answer([mine, outsider].filter((o) => selected.some((s) => s.revisionId === o.reference.revisionId))));
    vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ ...viewWith(c.spells), character: c }));
    render(<CharacterBuilder mode={{ kind: 'choices', view: answer([]) }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
    const picker = await screen.findByRole('group', { name: /Fixture feature: choose 2/ });
    await within(picker).findByRole('checkbox', { name: /Fixture Outsider/ });
    await allowOutside(user);
    const box = within(picker).getByRole('checkbox', { name: /Fixture Outsider/ }) as HTMLInputElement;
    await user.click(box);
    await waitFor(() => expect(box.checked).toBe(true));
    const save = screen.getByRole('button', { name: 'Save choices' });
    expect(save.getAttribute('aria-disabled')).toBeNull();
    await user.clear(reasonField());
    expect(save.getAttribute('aria-disabled')).toBe('true');
    expect(save.hasAttribute('disabled')).toBe(false);
    expect(save.getAttribute('aria-describedby')).toBe('commit-hint');
    expect(screen.getByText('Fixture Outsider needs a campaign exception reason.')).toBeTruthy();
    await user.click(save);
    expect(vi.mocked(client.saveCharacter)).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(save);
    expect(box.checked).toBe(true); // never dropped
    await user.type(reasonField(), 'DM approved again');
    expect(save.getAttribute('aria-disabled')).toBeNull();
    await user.click(save);
    await waitFor(() => expect(vi.mocked(client.saveCharacter)).toHaveBeenCalledTimes(1));
    expect(vi.mocked(client.saveCharacter).mock.calls[0]![0].campaignExceptions).toEqual([expect.objectContaining({ content: outsider.reference, reason: 'DM approved again' })]);
  });

  it('lets the player unpick the choice option to proceed', async () => {
    const user = userEvent.setup();
    const source = { contentId: 'fixture-feature', revisionId: 'fixture-feature-r1' };
    const outsider = content(8, 'feature', 'Fixture Outsider', 'srd-5.1', { allowedInCampaign: false });
    vi.mocked(client.listContent).mockResolvedValue([outsider]);
    const answer = (selected: ContentOption[]) => {
      const v = choiceView(selected, [outsider], 'fixture-camp');
      v.character = { ...v.character, choices: [{ source, choiceId: 'pick', selected: selected.map((o) => o.reference) }] };
      return v;
    };
    vi.mocked(client.previewChoice).mockImplementation(async (_c, _s, _id, selected) => answer(selected.length ? [outsider] : []));
    render(<CharacterBuilder mode={{ kind: 'choices', view: answer([]) }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
    const picker = await screen.findByRole('group', { name: /Fixture feature: choose 2/ });
    await within(picker).findByRole('checkbox', { name: /Fixture Outsider/ });
    await allowOutside(user);
    const box = within(picker).getByRole('checkbox', { name: /Fixture Outsider/ }) as HTMLInputElement;
    await user.click(box);
    await waitFor(() => expect(box.checked).toBe(true));
    await user.clear(reasonField());
    const save = screen.getByRole('button', { name: 'Save choices' });
    expect(save.getAttribute('aria-disabled')).toBe('true');
    await user.click(box); // unticking always works
    await waitFor(() => expect(box.checked).toBe(false));
    expect(within(picker).getByRole('checkbox', { name: /Fixture Outsider/ })).toBe(box);
    expect(save.getAttribute('aria-disabled')).toBeNull();
  });

  it('holds Save for a spell, keeps it ticked and focused, and records the exception once a reason is given again', async () => {
    const user = userEvent.setup();
    const outsideSpell = { ...spell('fixture-hex', 'Fixture Hex', 1), allowedInCampaign: false };
    vi.mocked(client.listContent).mockResolvedValue([...spells, outsideSpell]);
    vi.mocked(client.preview).mockImplementation(async (draft) => ({ ...viewWith(draft.spells), character: draft }));
    vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ ...viewWith(c.spells), character: c }));
    const start = viewWith([]);
    start.character = { ...start.character, campaignId: 'fixture-camp' };
    render(<CharacterBuilder mode={{ kind: 'choices', view: start }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
    const picker = await screen.findByRole('group', { name: /Fixture caster spells/ });
    const hex = (await within(picker).findByRole('checkbox', { name: /Fixture Hex/ })) as HTMLInputElement;
    await allowOutside(user);
    await user.click(hex);
    await waitFor(() => expect(hex.checked).toBe(true));
    await user.clear(reasonField());
    const save = screen.getByRole('button', { name: 'Save choices' });
    expect(save.getAttribute('aria-disabled')).toBe('true');
    expect(screen.getByText('Fixture Hex needs a campaign exception reason.')).toBeTruthy();
    await user.click(save);
    expect(vi.mocked(client.saveCharacter)).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(save);
    expect(hex.checked).toBe(true);
    await user.type(reasonField(), 'DM approved');
    await user.click(save);
    await waitFor(() => expect(vi.mocked(client.saveCharacter)).toHaveBeenCalledTimes(1));
    expect(vi.mocked(client.saveCharacter).mock.calls[0]![0].campaignExceptions).toEqual([expect.objectContaining({ content: outsideSpell.reference, reason: 'DM approved' })]);
  });

  it('does not ask for a reason for content the saved character already holds', async () => {
    const user = userEvent.setup();
    const outsideSpell = { ...spell('fixture-hex', 'Fixture Hex', 1), allowedInCampaign: false };
    vi.mocked(client.listContent).mockResolvedValue([...spells, outsideSpell]);
    const start = viewWith([{ caster: caster.contentId, spell: outsideSpell.reference, prepared: true }]);
    start.character = { ...start.character, campaignId: 'fixture-camp' };
    render(<CharacterBuilder mode={{ kind: 'choices', view: start }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
    await screen.findByRole('checkbox', { name: /Fixture Hex/ });
    expect(screen.getByRole('button', { name: 'Save choices' }).getAttribute('aria-disabled')).toBeNull();
    expect(screen.queryByText(/needs a campaign exception reason/)).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Save choices' })); // nothing new to explain
    await waitFor(() => expect(vi.mocked(client.saveCharacter)).toHaveBeenCalledTimes(1));
  });
});

// R43 (follow-up F3): level-up Next says why it waits (3.3.2, 4.1.2) and is never natively disabled.
describe('level-up Next states its reason (R43)', () => {
  beforeEach(() => {
    vi.mocked(client.listCampaigns).mockResolvedValue([]);
    vi.mocked(client.preview).mockClear();
  });

  it('at level 20 is aria-disabled with the reason linked, refuses the press and keeps the focus', async () => {
    const user = userEvent.setup();
    vi.mocked(client.listContent).mockResolvedValue([]);
    const view = levelView();
    view.character = { ...view.character, level: 20, classes: [{ class: { contentId: 'fixture-class', revisionId: 'fixture-class-r1' }, level: 20 }] };
    render(<CharacterBuilder mode={{ kind: 'levelUp', view }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
    const next = screen.getByRole('button', { name: 'Next: choices' });
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.hasAttribute('disabled')).toBe(false);
    expect(next.getAttribute('aria-describedby')).toBe('next-hint');
    expect(document.getElementById('next-hint')!.textContent).toBe('Already at level 20, the highest level.');
    await user.click(next);
    expect(vi.mocked(client.preview)).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(next);
  });

  it('with no class to gain a level in says "Choose a class to continue." until one is picked', async () => {
    const user = userEvent.setup();
    vi.mocked(client.listContent).mockResolvedValue([content(3, 'class', 'Fixture Warden', 'srd-5.1')]);
    render(<CharacterBuilder mode={{ kind: 'levelUp', view: viewWith([]) }} rulesFamilies={families} onCommitted={() => {}} onCancel={() => {}} onError={() => {}} />);
    const next = screen.getByRole('button', { name: 'Next: choices' });
    expect(next.getAttribute('aria-disabled')).toBe('true');
    expect(next.hasAttribute('disabled')).toBe(false);
    expect(next.getAttribute('aria-describedby')).toBe('next-hint');
    expect(document.getElementById('next-hint')!.textContent).toBe('Choose a class to continue.');
    await user.click(next);
    expect(vi.mocked(client.preview)).not.toHaveBeenCalled();
    await user.click(await screen.findByRole('radio', { name: /^Fixture Warden/ }));
    expect(next.getAttribute('aria-disabled')).toBeNull();
    expect(next.hasAttribute('aria-describedby')).toBe(false);
    expect(screen.queryByText('Choose a class to continue.')).toBeNull();
  });
});
