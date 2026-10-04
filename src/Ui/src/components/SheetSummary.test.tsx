// @vitest-environment jsdom
// The summary bar: a <dl> of the core numbers (no headings, no regions named like the panels'), an overridden value
// says so, inspiration follows the family, conditions appear only when present, and the ability roll buttons roll.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { Character, CharacterSheet, CharacterView, DerivedValue, FieldOverride, PlayState } from '../api/types';
import { SheetSummary } from './SheetSummary';

afterEach(cleanup);

const field = (id: string, label: string, value: number, units: string, override?: FieldOverride): DerivedValue => ({
  field: id,
  label,
  value,
  computedValue: override ? value - 2 : value,
  trace: [],
  warnings: [],
  automation: 'automatic',
  units,
  override,
});

const abilityFields = (
  [
    ['str', 'Strength', 16],
    ['dex', 'Dexterity', 14],
    ['con', 'Constitution', 15],
    ['int', 'Intelligence', 8],
    ['wis', 'Wisdom', 12],
    ['cha', 'Charisma', 10],
  ] as const
).flatMap(([a, name, score]) => [
  field(`ability.${a}.score`, `${name} score`, score, 'score'),
  field(`ability.${a}.mod`, `${name} modifier`, Math.floor((score - 10) / 2), 'modifier'),
]);

function fixtureView(sheetOver: Partial<CharacterSheet> = {}, play?: PlayState, rulesFamily: Character['rulesFamily'] = 'srd-5.2.1'): CharacterView {
  const character: Character = {
    id: 'fixture-1',
    schemaVersion: 7,
    name: 'Fixture Pell',
    rulesFamily,
    level: 3,
    classes: [],
    choices: [],
    crossFamilyExceptions: [],
    baseAbilities: { str: 16, dex: 14, con: 15, int: 8, wis: 12, cha: 10 },
    pins: [],
    overrides: [],
    play,
    updatedAt: '2026-10-03T00:00:00Z',
  };
  const sheet: CharacterSheet = {
    characterId: 'fixture-1',
    rulesFamily,
    diagnostics: [],
    fields: [
      ...abilityFields,
      field('proficiencyBonus', 'Proficiency bonus', 2, 'bonus'),
      field('armorClass', 'Armor Class', 16, 'score', { field: 'armorClass', value: 16, reason: 'Table ruling' }),
      field('initiative', 'Initiative', 2, 'modifier'),
    ],
    hitPoints: { maximum: 35, current: 28, temporary: 5 },
    hitDice: [{ die: 12, total: 3, spent: 1, remaining: 2, classes: ['Fixture Brute'] }],
    ...sheetOver,
  };
  return { character, sheet };
}

const dd = (term: string) => within(screen.getByRole('region', { name: 'Summary' })).getByText(term, { selector: 'dt' }).nextElementSibling!.textContent!.trim();

it('lists the core numbers as terms and definitions, with no heading or named region of its own', () => {
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} />);
  const summary = screen.getByRole('region', { name: 'Summary' });
  expect(within(summary).queryAllByRole('heading')).toHaveLength(0);
  expect(within(summary).getAllByRole('region').map((r) => r.getAttribute('aria-label'))).toEqual(['Last roll']);
  expect(dd('Proficiency bonus')).toBe('+2');
  expect(dd('Armor Class')).toBe('16 (overridden)');
  expect(dd('Initiative')).toBe('+2');
  expect(dd('Hit points')).toBe('28 of 35, 5 temporary');
  expect(dd('Hit dice')).toBe('d12 2 of 3');
  expect(dd('Heroic Inspiration')).toBe('no');
  expect(within(summary).queryByText('Conditions', { selector: 'dt' })).toBeNull();
  expect(within(summary).getByText('Strength', { selector: 'dt' })).toBeTruthy();
  expect(within(summary).getByText('16', { selector: '.derived' })).toBeTruthy();
});

it('names inspiration by family and lists conditions and exhaustion when present', () => {
  render(
    <SheetSummary
      view={fixtureView({}, { temporaryHitPoints: 0, resources: [], conditions: ['poisoned', 'prone'], exhaustion: 2, inspiration: true }, 'srd-5.1')}
      rollMode="normal"
      onRollMode={() => {}}
      act={() => {}}
      onRoll={() => {}}
    />,
  );
  expect(dd('Inspiration')).toBe('yes');
  expect(dd('Conditions')).toBe('Poisoned, Prone, exhaustion 2');
});

it('rolls an ability check from its button, named apart from the field card\'s "Roll … modifier"', async () => {
  const user = userEvent.setup();
  const onRoll = vi.fn();
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={onRoll} />);
  await user.click(screen.getByRole('button', { name: 'Roll Strength check (+3)' }));
  expect(onRoll).toHaveBeenCalledWith('ability.str.mod');
  expect(screen.queryByRole('button', { name: 'Roll Strength modifier' })).toBeNull();
  expect(screen.getByRole('radio', { name: 'Advantage' })).toBeTruthy(); // the roll-mode picker lives here now
});
