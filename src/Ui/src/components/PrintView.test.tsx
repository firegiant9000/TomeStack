// @vitest-environment jsdom
// The printable backup carries details only when "Include details" is ticked (item 17, D36), like session notes. Values are invented.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet, CharacterView, DerivedValue } from '../api/types';
import { fitCombatCard, maxTickBoxes, PrintView } from './PrintView';

vi.mock('../api/client', () => ({ client: { previewExport: vi.fn(), info: vi.fn(), listGapNotes: vi.fn() } }));

beforeEach(() => {
  vi.mocked(client.previewExport).mockResolvedValue({ included: [] } as never);
  vi.mocked(client.info).mockResolvedValue({ version: '0.0.0-fixture' } as never);
});
afterEach(() => {
  cleanup();
  vi.mocked(client.listGapNotes).mockReset();
  localStorage.clear();
});

const character: Character = {
  id: 'fixture-p',
  schemaVersion: 9,
  name: 'Fixture Printer',
  rulesFamily: 'srd-5.1',
  level: 1,
  classes: [],
  choices: [],
  crossFamilyExceptions: [],
  baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
  pins: [],
  overrides: [],
  details: { alignment: 'Fixture neutral', appearance: { eyes: 'Fixture grey' }, backstory: 'Fixture long story.', languages: ['Fixture tongue', 'Fixture signs'], toolsAndOther: ['Fixture kit'] },
  notes: [{ id: 'n1', date: '2026-10-01', text: 'Fixture session text.', createdAt: '2026-10-01T20:00:00Z' }],
  updatedAt: '2026-10-16T00:00:00Z',
};
const sheet: CharacterSheet = { characterId: 'fixture-p', rulesFamily: 'srd-5.1', diagnostics: [], fields: [] };
const view: CharacterView = { character, sheet };

it('leaves details out until "Include details" is ticked, separately from the session notes tick', async () => {
  const user = userEvent.setup();
  render(<PrintView view={view} onError={() => {}} onClose={() => {}} />);
  const preview = screen.getByRole('region', { name: 'Print preview' });
  const tick = within(preview).getByRole<HTMLInputElement>('checkbox', { name: /^Include details/ });
  expect(tick.checked).toBe(false);
  expect(within(preview).queryByRole('heading', { name: 'Details' })).toBeNull();
  expect(preview.textContent).not.toContain('Fixture long story.');
  await user.click(tick);
  expect(within(preview).getByRole('heading', { name: 'Details' })).toBeTruthy();
  expect(preview.textContent).toContain('Fixture long story.');
  expect(preview.textContent).toContain('Fixture tongue, Fixture signs');
  expect(within(preview).getByText('Eyes')).toBeTruthy();
  expect(preview.textContent).not.toContain('Fixture session text.'); // the other tick is its own
  expect(within(preview).getByRole<HTMLInputElement>('checkbox', { name: /^Include session notes/ }).checked).toBe(false);
});

// P14 (D71): combat card and large print. Names and numbers are invented.
const sentinel = 'Sentinel-7Q: the lantern hums';
const df = (field: string, label: string, value: number) => ({ field, label, value, computedValue: value }) as unknown as DerivedValue;
const combatSheet = {
  ...sheet,
  fields: [
    df('armorClass', 'Armor Class', 15),
    df('hitPoints', 'Hit points', 27),
    df('speed', 'Speed', 35),
    df('initiative', 'Initiative', 2),
    df('proficiencyBonus', 'Proficiency bonus', 3),
    df('save.str', 'Strength save', 1),
    df('save.dex', 'Dexterity save', 4),
    df('save.con', 'Constitution save', 0),
    df('save.int', 'Intelligence save', -1),
    df('save.wis', 'Wisdom save', 2),
    df('save.cha', 'Charisma save', 5),
    df('passive.perception', 'Passive Perception', 13),
    df('passive.insight', 'Passive Insight', 11),
    df('passive.investigation', 'Passive Investigation', 9),
  ],
  attacks: [{ item: { revisionId: 'w1' }, effectId: 'e1', name: 'Fixture Glaive', toHit: 6, damage: '1d10+3', damageType: 'slashing', properties: [] }],
  resources: [
    { content: { revisionId: 'r1' }, resourceId: 'fixture-surge', label: 'Fixture Surge', maximum: 3, spent: 0, current: 3, recoveries: [] },
    { content: { revisionId: 'r2' }, resourceId: 'fixture-hand', label: 'Fixture Handmade', spent: 1, recoveries: [] },
    { content: { revisionId: 'r3' }, resourceId: 'fixture-twenty', label: 'Fixture Twenty', maximum: 20, spent: 0, current: 20, recoveries: [] },
    { content: { revisionId: 'r4' }, resourceId: 'fixture-pool', label: 'Fixture Pool', maximum: 21, spent: 0, current: 21, recoveries: [] },
  ],
  spellcasting: [{ content: { revisionId: 's1' }, name: 'Fixture Caster', classLevel: 5, attackBonus: 5, saveDc: 13, preparation: 'known', spells: [] }],
  spellSlots: [
    { level: 1, maximum: 4, spent: 0, remaining: 4, field: 'spellSlots.1' },
    { level: 2, maximum: 2, spent: 0, remaining: 2, field: 'spellSlots.2' },
    { level: 3, maximum: 21, spent: 0, remaining: 21, field: 'spellSlots.3' },
  ],
  pactSlots: { level: 3, maximum: 2, spent: 0, remaining: 2, field: 'pactSlots' },
  toggles: [{ content: { revisionId: 't1' }, contentName: 'Fixture', effectId: 'te', toggleId: 'fixture-stance', label: 'Fixture Stance', on: false }],
} as unknown as CharacterSheet;
const combatView: CharacterView = {
  character: { ...character, notes: [{ id: 'n2', date: '2026-10-02', text: `Session ${sentinel}`, createdAt: '2026-10-02T20:00:00Z' }] },
  sheet: combatSheet,
};

async function openCombat(user: ReturnType<typeof userEvent.setup>, v: CharacterView = combatView) {
  render(<PrintView view={v} onError={() => {}} onClose={() => {}} />);
  const preview = screen.getByRole('region', { name: 'Print preview' });
  await user.click(within(preview).getByRole('radio', { name: 'Combat card' }));
  return preview;
}

it('starts on Full sheet with Large print off every time it opens', () => {
  render(<PrintView view={combatView} onError={() => {}} onClose={() => {}} />);
  expect(screen.getByRole<HTMLInputElement>('radio', { name: 'Full sheet' }).checked).toBe(true);
  expect(screen.getByRole<HTMLInputElement>('radio', { name: 'Combat card' }).checked).toBe(false);
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Large print' }).checked).toBe(false);
});

it('combat card holds AC, hit points, speed, saves, passives, attacks and resources', async () => {
  const preview = await openCombat(userEvent.setup());
  const text = preview.textContent ?? '';
  expect(within(preview).getByRole('table', { name: 'Vitals' })).toBeTruthy();
  for (const part of ['Armor Class', '15', '27', '35', 'Initiative', '+2', 'Proficiency bonus', '+3', 'Fixture Glaive', '+6', '1d10+3', 'Fixture Surge', 'Fixture Handmade'])
    expect(text).toContain(part);
  expect(within(preview).getByRole('table', { name: 'Saves' })).toBeTruthy();
  expect(within(preview).getByRole('table', { name: 'Passive scores' }).textContent).toContain('13');
  // Replaces the full layout.
  expect(within(preview).queryByRole('table', { name: 'Skills' })).toBeNull();
  expect(within(preview).queryByRole('heading', { name: 'Features' })).toBeNull();
});

it('combat card never carries note text, however the include ticks stood in full mode', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listGapNotes).mockResolvedValue([{ id: 'g1', text: `Gap ${sentinel}`, status: 'open', target: { kind: 'feature', label: 'Fixture' } }] as never);
  const { container } = render(<PrintView view={combatView} onError={() => {}} onClose={() => {}} />);
  const preview = screen.getByRole('region', { name: 'Print preview' });
  await user.click(within(preview).getByRole('checkbox', { name: /^Include gap notes/ }));
  await user.click(within(preview).getByRole('checkbox', { name: /^Include session notes/ }));
  await within(preview).findByText(/Gap Sentinel-7Q/);
  expect(container.textContent).toContain(sentinel);
  await user.click(within(preview).getByRole('radio', { name: 'Combat card' }));
  expect(container.textContent).not.toContain(sentinel);
  expect(within(preview).queryByRole('checkbox', { name: /^Include gap notes/ })).toBeNull();
  expect(within(preview).queryByRole('checkbox', { name: /^Include session notes/ })).toBeNull();
  expect(within(preview).queryByRole('checkbox', { name: /^Include details/ })).toBeNull();
});

it('draws one tick box per point of a resource and per spell slot, and a blank line for an unbounded resource', async () => {
  const preview = await openCombat(userEvent.setup());
  const row = (name: string) => within(preview).getByRole('rowheader', { name }).closest('tr')!;
  expect(row('Fixture Surge').querySelectorAll('.tick')).toHaveLength(3);
  expect(row('Fixture Handmade').querySelectorAll('.tick')).toHaveLength(0);
  expect(row('Fixture Handmade').querySelector('.blank-line')).toBeTruthy();
  expect(row('Level 1').querySelectorAll('.tick')).toHaveLength(4);
  expect(row('Level 2').querySelectorAll('.tick')).toHaveLength(2);
  expect(row('Pact Magic (level 3)').querySelectorAll('.tick')).toHaveLength(2);
});

it('lists toggles as named check boxes and conditions as a blank list', async () => {
  const preview = await openCombat(userEvent.setup());
  const toggles = within(preview).getByRole('heading', { name: 'Toggles' }).parentElement!;
  expect(toggles.textContent).toContain('Fixture Stance');
  expect(toggles.querySelectorAll('.tick')).toHaveLength(1);
  const conditions = within(preview).getByRole('heading', { name: 'Conditions' }).parentElement!;
  expect(conditions.querySelectorAll('.blank-line').length).toBeGreaterThanOrEqual(4);
  expect(preview.textContent).not.toContain('Continued on the full sheet');
});

const largeVar = () => document.documentElement.style.getPropertyValue('--print-font-size');

it.each([
  ['100', '150%'],
  ['150', '150%'],
  ['175', '175%'],
  ['200', '200%'],
])('Large print at text size %s sets --print-font-size to %s and marks the sheet', async (size, expected) => {
  localStorage.setItem('tomestack.textSize', size);
  const user = userEvent.setup();
  render(<PrintView view={view} onError={() => {}} onClose={() => {}} />);
  const preview = screen.getByRole('region', { name: 'Print preview' });
  expect(largeVar()).toBe('');
  expect(preview.classList.contains('print-large')).toBe(false);
  await user.click(within(preview).getByRole('checkbox', { name: 'Large print' }));
  expect(largeVar()).toBe(expected);
  expect(preview.classList.contains('print-large')).toBe(true);
  await user.click(within(preview).getByRole('checkbox', { name: 'Large print' }));
  expect(largeVar()).toBe('');
  expect(preview.classList.contains('print-large')).toBe(false);
});

it('shows a dash, not 0, for a field the sheet does not have', async () => {
  const preview = await openCombat(userEvent.setup(), { ...combatView, sheet: { ...combatSheet, fields: [] } });
  const vitals = within(preview).getByRole('table', { name: 'Vitals' });
  expect(vitals.textContent).not.toContain('0 ft.');
  expect(within(vitals).getAllByText('—')).toHaveLength(5); // AC, maximum, speed, initiative, PB
  expect(within(preview).getAllByText('—')).toHaveLength(14); // plus six saves and three passives
});

it('caps tick boxes at maxTickBoxes: a bigger pool prints its size and one write-in box', async () => {
  const preview = await openCombat(userEvent.setup());
  const row = (name: string) => within(preview).getByRole('rowheader', { name }).closest('tr')!;
  expect(maxTickBoxes).toBe(20);
  expect(row('Fixture Twenty').querySelectorAll('.tick')).toHaveLength(20);
  expect(row('Fixture Pool').querySelectorAll('.tick')).toHaveLength(1);
  expect(row('Fixture Pool').textContent).toContain('21 uses');
  expect(row('Level 3').querySelectorAll('.tick')).toHaveLength(1);
  expect(row('Level 3').textContent).toContain('21 slots');
});

it('removes --print-font-size when the preview closes', async () => {
  const user = userEvent.setup();
  const { unmount } = render(<PrintView view={view} onError={() => {}} onClose={() => {}} />);
  await user.click(screen.getByRole('checkbox', { name: 'Large print' }));
  expect(largeVar()).toBe('150%');
  unmount();
  expect(largeVar()).toBe('');
});

it('fitCombatCard keeps everything when it all fits, exact fit included', () => {
  const h = { core: 400, conditions: 100, toggles: 100, spells: 200 };
  expect(fitCombatCard(h, 800, 30)).toEqual({ conditions: true, toggles: true, spells: true, continued: false });
  expect(fitCombatCard(h, 799, 30)).toEqual({ conditions: false, toggles: true, spells: true, continued: true });
});

it('fitCombatCard drops the conditions list first, then toggles, then spell slots last, and says continued', () => {
  const h = { core: 400, conditions: 100, toggles: 100, spells: 200 };
  // Without conditions: 700 plus the continued line (30).
  expect(fitCombatCard(h, 730, 30)).toEqual({ conditions: false, toggles: true, spells: true, continued: true });
  expect(fitCombatCard(h, 729, 30)).toEqual({ conditions: false, toggles: false, spells: true, continued: true });
  expect(fitCombatCard(h, 630, 30)).toEqual({ conditions: false, toggles: false, spells: true, continued: true });
  expect(fitCombatCard(h, 629, 30)).toEqual({ conditions: false, toggles: false, spells: false, continued: true });
});

it('fitCombatCard never drops the core: too tall alone still prints, with the continued line', () => {
  const h = { core: 1200, conditions: 100, toggles: 100, spells: 200 };
  expect(fitCombatCard(h, 960, 30)).toEqual({ conditions: false, toggles: false, spells: false, continued: true });
});

it('fitCombatCard does not say continued for a section that is not there', () => {
  expect(fitCombatCard({ core: 400, conditions: 100, toggles: 0, spells: 0 }, 500, 30)).toEqual({ conditions: true, toggles: true, spells: true, continued: false });
  expect(fitCombatCard({ core: 400, conditions: 100, toggles: 0, spells: 0 }, 499, 30)).toEqual({ conditions: false, toggles: true, spells: true, continued: true });
});

it('prints no Details heading when ticked but nothing is set', async () => {
  const user = userEvent.setup();
  render(<PrintView view={{ ...view, character: { ...character, details: undefined } }} onError={() => {}} onClose={() => {}} />);
  const preview = screen.getByRole('region', { name: 'Print preview' });
  await user.click(within(preview).getByRole('checkbox', { name: /^Include details/ }));
  expect(within(preview).queryByRole('heading', { name: 'Details' })).toBeNull();
});
