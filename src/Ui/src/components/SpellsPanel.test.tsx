// @vitest-environment jsdom
// Concentration (D19): "Concentrate on {spell}" is offered for every prepared concentration spell, cantrips included.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterView, SpellEntry, TraceOrigin } from '../api/types';
import { SpellsPanel } from './SpellsPanel';

afterEach(cleanup);

const origin: TraceOrigin = { kind: 'content', rulesFamily: 'srd-5.2.1', sourceTitle: 'Fixture Pack' };
const ref = (n: number) => ({ contentId: `00000000-0000-4000-8000-${n.toString().padStart(12, '0')}`, revisionId: `00000000-0000-4000-9000-${n.toString().padStart(12, '0')}` });
const spell = (n: number, name: string, level: number, concentration: boolean, prepared = true): SpellEntry => ({
  spell: ref(n), name, level, prepared, concentration, ritual: false, attack: 'none', origin, diagnostics: [],
});

function view(concentratingOn?: number): CharacterView {
  return {
    character: {
      id: 'fixture-4', schemaVersion: 8, name: 'Fixture Caster', rulesFamily: 'srd-5.2.1', level: 3, classes: [], choices: [], crossFamilyExceptions: [],
      baseAbilities: { str: 8, dex: 14, con: 12, int: 16, wis: 10, cha: 10 }, pins: [], overrides: [], updatedAt: '2026-10-06T00:00:00Z',
      play: concentratingOn === undefined ? undefined : { temporaryHitPoints: 0, resources: [], conditions: [], exhaustion: 0, concentration: { spell: ref(concentratingOn), name: 'x' } },
    },
    sheet: {
      characterId: 'fixture-4', rulesFamily: 'srd-5.2.1', diagnostics: [], fields: [],
      spellSlots: [{ level: 1, maximum: 2, spent: 0, remaining: 2, field: 'slots.1' }],
      spellcasting: [{
        content: ref(1), name: 'Fixture Arcanist', effectId: 'spellcasting', classLevel: 3, ability: 'int', attackBonus: 5, saveDc: 13, preparation: 'prepared',
        spellList: 'fixture-arcane', slotKind: 'spellSlots', slots: [2], primary: true, origin, warnings: [],
        spells: [
          spell(10, 'Fixture Hush', 0, true), // a concentration cantrip
          spell(11, 'Fixture Veil', 1, true),
          spell(12, 'Fixture Spark', 1, false),
          spell(13, 'Fixture Shroud', 1, true, false), // concentration but not prepared
        ],
      }],
    },
  } as CharacterView;
}

const panel = (v: CharacterView, act = () => {}) => render(<SpellsPanel view={v} act={act} roll={() => {}} onSave={() => {}} />);

it('offers Concentrate on for prepared concentration spells, a cantrip included, and no other', () => {
  panel(view());
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Hush' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Veil' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Concentrate on Fixture Spark' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Concentrate on Fixture Shroud' })).toBeNull();
});

it('disables the button for the spell being concentrated on and sends startConcentration otherwise', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  panel(view(10), act);
  expect((screen.getByRole('button', { name: 'Concentrate on Fixture Hush' }) as HTMLButtonElement).disabled).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Concentrate on Fixture Veil' }));
  expect(act).toHaveBeenCalledWith({ action: 'startConcentration', contentId: ref(11).contentId });
});

it('sends the chosen mode with a spell attack roll and none with its dice (R93)', async () => {
  const user = userEvent.setup();
  const roll = vi.fn();
  const v = view();
  const caster = v.sheet.spellcasting![0]!;
  caster.spells = [{ ...spell(20, 'Fixture Bolt', 0, false), attack: 'ranged', dice: '1d10' }];
  render(<SpellsPanel view={v} act={() => {}} roll={roll} rollMode="advantage" onSave={() => {}} />);
  await user.click(screen.getByRole('button', { name: 'Roll Fixture Bolt attack' }));
  expect(roll).toHaveBeenLastCalledWith({ spell: ref(20), spellAttack: true, mode: 'advantage' });
  await user.click(screen.getByRole('button', { name: 'Roll Fixture Bolt (1d10)' }));
  expect(roll).toHaveBeenLastCalledWith({ spell: ref(20) });
});

it('filters a caster’s spells by name, keeps the Prepared and Concentrate names of the rows shown, and says how many (D30)', async () => {
  const user = userEvent.setup();
  panel(view());
  const search = screen.getByRole('searchbox', { name: /^Search .* spells by name$/ });
  await user.type(search, 'VEIL');
  expect(screen.getByText('1 of 4 spells shown')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Veil' })).toBeTruthy();
  expect(screen.getByRole('checkbox', { name: 'Prepared: Fixture Veil' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Concentrate on Fixture Hush' })).toBeNull();
  await user.type(search, 'zz');
  expect(screen.getByText(/No spells match “VEILzz”/)).toBeTruthy();
  await user.clear(search);
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Hush' })).toBeTruthy();
  expect(screen.queryByText(/spells shown/)).toBeNull();
  expect(document.activeElement).toBe(search);
});

it('gives two casters their own search box and status; typing in one filters only that caster', async () => {
  const user = userEvent.setup();
  const v = view();
  const first = v.sheet.spellcasting![0]!;
  const second = {
    ...first, name: 'Fixture Warden', content: ref(2), primary: false,
    spells: [spell(20, 'Fixture Thorn', 1, false), spell(21, 'Fixture Mist', 1, false), spell(22, 'Fixture Gale', 2, false)],
  };
  v.sheet.spellcasting = [first, second];
  panel(v);
  const arcanist = screen.getByRole('searchbox', { name: 'Search Fixture Arcanist spells by name' });
  const warden = screen.getByRole('searchbox', { name: 'Search Fixture Warden spells by name' });
  expect(arcanist.id).not.toBe(warden.id);
  await user.type(warden, 'thorn');
  expect(screen.getByText('1 of 3 spells shown')).toBeTruthy();
  expect(screen.getByRole('list', { name: 'Fixture Warden spells' }).querySelectorAll('.option-name')).toHaveLength(1);
  expect(screen.getByRole('list', { name: 'Fixture Arcanist spells' }).querySelectorAll('.option-name')).toHaveLength(4);
  expect((arcanist as HTMLInputElement).value).toBe('');
  expect(screen.queryByText('1 of 4 spells shown')).toBeNull();
  await user.type(arcanist, 'veil');
  expect(screen.getByText('1 of 4 spells shown')).toBeTruthy();
  expect(screen.getByText('1 of 3 spells shown')).toBeTruthy();
  expect((warden as HTMLInputElement).value).toBe('thorn');
});

it('a marks-only query filters nothing and announces nothing (D30)', async () => {
  const user = userEvent.setup();
  panel(view());
  await user.type(screen.getByRole('searchbox', { name: /^Search .* spells by name$/ }), '\u0301');
  expect(screen.queryByText(/spells shown/)).toBeNull();
  expect(screen.getByRole('button', { name: 'Concentrate on Fixture Hush' })).toBeTruthy();
});

it('works out the prepared flag from the latest saved list, and refuses a spell that was removed meanwhile (G10)', async () => {
  const user = userEvent.setup();
  const onSave = vi.fn();
  const v = view();
  render(<SpellsPanel view={v} act={() => {}} roll={() => {}} onSave={onSave} />);
  await user.click(screen.getByRole('checkbox', { name: 'Prepared: Fixture Veil' })); // shown prepared
  const update = onSave.mock.calls[0]![0] as (latest: CharacterView['character']) => CharacterView['character'];
  const entry = (prepared: boolean) => ({ caster: ref(1).contentId, spell: ref(11), prepared });
  // The latest saved list has it unprepared (changed elsewhere meanwhile): the press toggles that, not the stale render.
  expect(update({ ...v.character, spells: [entry(false)] }).spells).toEqual([entry(true)]);
  expect(() => update({ ...v.character, spells: [] })).toThrow(/removed/);
});

// P09 (D70): filters per caster. Cantrips: Glint, Hush (ritual), Whisper (ritual, no school); then Veil (1), Chant (2, ritual).
function filterView(): CharacterView {
  const v = view();
  v.sheet.spellcasting![0]!.spells = [
    { ...spell(30, 'Fixture Glint', 0, false), school: 'Evocation', castingTime: 'Action' },
    { ...spell(31, 'Fixture Hush', 0, false), school: 'illusion', castingTime: '1 bonus action', ritual: true },
    { ...spell(32, 'Fixture Whisper', 0, false), ritual: true },
    { ...spell(33, 'Fixture Veil', 1, true), school: 'Illusion', castingTime: '1 reaction, which you take when hit' },
    { ...spell(34, 'Fixture Chant', 2, false), school: 'Divination', castingTime: '10 minutes', ritual: true },
  ];
  return v;
}
const openFilters = async (user: ReturnType<typeof userEvent.setup>, name = 'Fixture Arcanist') => {
  await user.click(screen.getByRole('button', { name: `Filters: ${name} spells` }));
  return within(screen.getByRole('group', { name: `Filter ${name} spells` }));
};
const listed = (name = 'Fixture Arcanist') =>
  [...screen.getByRole('list', { name: `${name} spells` }).querySelectorAll('.option-name')].map((e) => e.textContent);

it('the Filters button sets aria-expanded and shows the group only while open', async () => {
  const user = userEvent.setup();
  panel(filterView());
  const button = screen.getByRole('button', { name: 'Filters: Fixture Arcanist spells' });
  expect(button.getAttribute('aria-expanded')).toBe('false');
  expect(screen.queryByRole('group', { name: 'Filter Fixture Arcanist spells' })).toBeNull();
  await user.click(button);
  expect(button.getAttribute('aria-expanded')).toBe('true');
  expect(screen.getByRole('group', { name: 'Filter Fixture Arcanist spells' })).toBeTruthy();
  await user.click(button);
  expect(button.getAttribute('aria-expanded')).toBe('false');
});

it('the Filters button name announces the on state only while a filter is active', async () => {
  const user = userEvent.setup();
  panel(filterView());
  expect(screen.queryByRole('button', { name: /Filters:.*, on$/ })).toBeNull();
  expect(screen.getByRole('button', { name: 'Filters: Fixture Arcanist spells' })).toBeTruthy();
  const group = await openFilters(user);
  await user.click(group.getByRole('checkbox', { name: 'Cantrip' }));
  expect(screen.getByRole('button', { name: 'Filters: Fixture Arcanist spells, on' })).toBeTruthy();
  await user.click(group.getByRole('checkbox', { name: 'Cantrip' }));
  expect(screen.queryByRole('button', { name: /Filters:.*, on$/ })).toBeNull();
});

it('ticking Cantrip and Ritual Yes leaves exactly the matching spells, and says how many', async () => {
  const user = userEvent.setup();
  panel(filterView());
  const group = await openFilters(user);
  await user.click(group.getByRole('checkbox', { name: 'Cantrip' }));
  await user.selectOptions(group.getByRole('combobox', { name: 'Ritual' }), 'Yes');
  expect(listed()).toEqual(['Fixture Hush', 'Fixture Whisper']);
  expect(screen.getByText('2 of 5 spells shown')).toBeTruthy();
});

it('filters by school (case-insensitively, offered once), casting time and concentration', async () => {
  const user = userEvent.setup();
  panel(filterView());
  const group = await openFilters(user);
  const school = within(group.getByRole('group', { name: 'School' }));
  expect(school.getAllByRole('checkbox').map((c) => c.parentElement?.textContent)).toEqual(['Divination', 'Evocation', 'illusion']);
  await user.click(school.getByRole('checkbox', { name: 'illusion' }));
  expect(listed()).toEqual(['Fixture Hush', 'Fixture Veil']);
  await user.click(group.getByRole('checkbox', { name: 'Reaction' }));
  expect(listed()).toEqual(['Fixture Veil']);
  await user.selectOptions(group.getByRole('combobox', { name: 'Concentration' }), 'No');
  expect(screen.getByText('No spells match these filters. Clear filters to see all 5.')).toBeTruthy();
  expect(screen.queryByRole('list', { name: 'Fixture Arcanist spells' })).toBeNull();
});

it('Clear filters restores every spell', async () => {
  const user = userEvent.setup();
  panel(filterView());
  const group = await openFilters(user);
  await user.click(group.getByRole('checkbox', { name: 'Level 2' }));
  expect(listed()).toEqual(['Fixture Chant']);
  await user.click(group.getByRole('button', { name: 'Clear filters' }));
  expect(listed()).toHaveLength(5);
  expect((group.getByRole('checkbox', { name: 'Level 2' }) as HTMLInputElement).checked).toBe(false);
  expect(screen.queryByText(/spells shown/)).toBeNull();
});

it('announces a filter-only change in the one polite region, with no second region', async () => {
  const user = userEvent.setup();
  const { container } = panel(filterView());
  const group = await openFilters(user);
  expect(container.querySelectorAll('[aria-live]')).toHaveLength(1);
  await user.click(group.getByRole('checkbox', { name: 'Cantrip' }));
  expect(container.querySelector('[aria-live="polite"]')!.textContent).toBe('3 of 5 spells shown');
  expect(container.querySelectorAll('[aria-live]')).toHaveLength(1);
});

it('The_count_region_changes_only_when_the_count_does', async () => {
  const user = userEvent.setup();
  const { container } = panel(filterView());
  const region = container.querySelector('[aria-live="polite"]')!;
  const changes: string[] = [];
  new MutationObserver(() => changes.push(region.textContent ?? '')).observe(region, { childList: true, characterData: true, subtree: true });
  const search = screen.getByRole('searchbox');
  await user.type(search, 'fixture'); // every keystroke matches all five, so the text is set once and then left alone
  expect(changes).toEqual(['5 of 5 spells shown']);
  await user.type(search, ' v'); // narrows to Veil only
  expect(changes).toEqual(['5 of 5 spells shown', '1 of 5 spells shown']);
});

it('keeps each caster’s filters of its own', async () => {
  const user = userEvent.setup();
  const v = filterView();
  const first = v.sheet.spellcasting![0]!;
  v.sheet.spellcasting = [
    first,
    { ...first, name: 'Fixture Warden', content: ref(2), primary: false, spells: [{ ...spell(40, 'Fixture Thorn', 1, false), school: 'Conjuration' }, spell(41, 'Fixture Gale', 2, false)] },
  ];
  panel(v);
  const arcanist = await openFilters(user);
  await user.click(arcanist.getByRole('checkbox', { name: 'Cantrip' }));
  expect(listed()).toHaveLength(3);
  expect(listed('Fixture Warden')).toHaveLength(2);
  const warden = await openFilters(user, 'Fixture Warden');
  await user.click(warden.getByRole('checkbox', { name: 'Level 2' }));
  expect(listed('Fixture Warden')).toEqual(['Fixture Gale']);
  expect(listed()).toHaveLength(3);
  expect((arcanist.getByRole('checkbox', { name: 'Cantrip' }) as HTMLInputElement).checked).toBe(true);
  expect((warden.getByRole('checkbox', { name: 'Cantrip' }) as HTMLInputElement).checked).toBe(false);
});
