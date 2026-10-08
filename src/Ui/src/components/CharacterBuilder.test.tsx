// @vitest-environment jsdom
// The builder's spell picker (D30): the legend's counts say what is recorded, not what the search shows. The client is mocked;
// names and values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
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
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Campaign' }), 'fixture-camp');
  expect(await screen.findByRole('radio', { name: /^Fixture Hillfolk/ })).toBeTruthy();
  await waitFor(() => expect(screen.getByRole('radio', { name: /^Fixture Outsider/ })).toBeTruthy());
  expect(screen.queryByRole('radio', { name: /^Fixture Dunefolk/ })).toBeNull();
  expect(screen.queryByText(/Fixture Other Feat/)).toBeNull();
  const outsider = screen.getByRole('radio', { name: /^Fixture Outsider/ }) as HTMLInputElement;
  expect(outsider.disabled).toBe(true);
  expect(outsider.closest('label')!.textContent).toMatch(/not allowed in this campaign/);
  // Outside the campaign and of the other family: still listed (P-01), with the campaign reason.
  const outlander = screen.getByRole('radio', { name: /^Fixture Outlander/ }) as HTMLInputElement;
  expect(outlander.disabled).toBe(true);
  expect(outlander.closest('label')!.textContent).toMatch(/not allowed in this campaign/);
  expect(screen.queryByText(/Options for the other family cannot be selected/)).toBeNull();
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
