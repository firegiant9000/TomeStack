// @vitest-environment jsdom
// The builder's spell picker (D30): the legend's counts say what is recorded, not what the search shows. The client is mocked;
// names and values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterView, ContentOption, SpellcastingEntry } from '../api/types';
import { CharacterBuilder } from './CharacterBuilder';

vi.mock('../api/client', () => ({ client: { listCampaigns: vi.fn(), listContent: vi.fn(), preview: vi.fn(), previewChoice: vi.fn() } }));

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
