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
      field('hitPoints', 'Hit point maximum', 35, 'score'),
      field('speed', 'Speed', 30, 'feet'),
    ],
    hitPoints: { maximum: 35, current: 28, temporary: 5 },
    hitDice: [{ die: 12, total: 3, spent: 1, remaining: 2, classes: ['Fixture Brute'] }],
    ...sheetOver,
  };
  return { character, sheet };
}

const dd = (term: string) => within(screen.getByRole('region', { name: 'Summary' })).getByText(term, { selector: 'dt' }).nextElementSibling!.textContent!.trim();

it('names the Spells tab in the rolling hint only when it is offered', () => {
  const { rerender } = render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} />);
  expect(screen.getByText('Rolling never spends anything. Roll a check here; a save, skill or initiative from its field on Stats; or an attack or feature on Play.')).toBeTruthy();
  rerender(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} spellsTab />);
  expect(screen.getByText('Rolling never spends anything. Roll a check here; a save, skill or initiative from its field on Stats; an attack or feature on Play; or a spell on Spells.')).toBeTruthy();
});

it('lists the core numbers as terms and definitions, with no heading or named region of its own', () => {
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} />);
  const summary = screen.getByRole('region', { name: 'Summary' });
  expect(within(summary).queryAllByRole('heading')).toHaveLength(0);
  expect(within(summary).getAllByRole('region').map((r) => r.getAttribute('aria-label'))).toEqual(['Last roll']);
  expect(dd('Proficiency bonus')).toBe('+2');
  expect(dd('Armor Class')).toBe('16 (overridden)');
  expect(dd('Initiative')).toBe('+2');
  expect(dd('Speed')).toBe('30 ft.');
  expect(dd('Hit points')).toBe('28 of 35, 5 temporary');
  expect(dd('Hit dice')).toBe('2 of 3 (d12)');
  expect(within(summary).getByRole<HTMLInputElement>('checkbox', { name: 'Heroic Inspiration' }).checked).toBe(false);
  expect(within(summary).queryByText('Conditions', { selector: 'dt' })).toBeNull();
  expect(within(summary).getByText('Strength', { selector: 'dt' })).toBeTruthy();
  expect(within(summary).getByText('+3', { selector: '.derived' })).toBeTruthy(); // the modifier is the large number (owner: modifier first by default)
  expect(within(summary).getByText('16', { selector: '.score' })).toBeTruthy();
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
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Inspiration' }).checked).toBe(true);
  expect(dd('Conditions')).toBe('Poisoned, Prone, exhaustion 2');
});

it('rolls an ability check from its button, named apart from the field card\'s "Roll … modifier"', async () => {
  const user = userEvent.setup();
  const onRoll = vi.fn();
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={onRoll} />);
  await user.click(screen.getByRole('button', { name: 'Roll Strength check (+3)' }));
  expect(onRoll).toHaveBeenCalledWith('ability.str.mod');
  // The summary's roll buttons are exactly the six ability checks, none of them the field cards' "Roll … modifier".
  const rollButtons = within(screen.getByRole('region', { name: 'Summary' })).getAllByRole('button', { name: /^Roll / });
  expect(rollButtons).toHaveLength(6);
  for (const button of rollButtons) {
    expect(button.textContent!.replace(/\s+/g, ' ').trim()).toMatch(/^Roll (Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma) check \([+-]\d+\)$/);
  }
  expect(screen.getByRole('button', { name: 'Roll Intelligence check (-1)' })).toBeTruthy();
  expect(screen.getByRole('radio', { name: 'Advantage' })).toBeTruthy(); // the roll-mode picker lives here now
});

it('toggles inspiration from the summary through the play command', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={act} onRoll={() => {}} />);
  await user.click(screen.getByRole('checkbox', { name: 'Heroic Inspiration' }));
  expect(act).toHaveBeenCalledWith({ action: 'setInspiration', amount: 1 });
});

it('adjusts hit points by one from the summary without changing the Hit points text', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={act} onRoll={() => {}} />);
  expect(dd('Hit points')).toBe('28 of 35, 5 temporary');
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  expect(act).toHaveBeenCalledWith({ action: 'damage', amount: 1 });
  await user.click(screen.getByRole('button', { name: 'Regain 1 hit point' }));
  expect(act).toHaveBeenCalledWith({ action: 'heal', amount: 1 });
});

const withHp = (current: number, temporary: number): CharacterView => {
  const view = fixtureView();
  return { ...view, sheet: { ...view.sheet, hitPoints: { maximum: 35, current, temporary } } };
};

it('marks "Lose" at 0 hit points and "Regain" at the maximum aria-disabled, and clicking there does nothing', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<SheetSummary view={withHp(0, 0)} rollMode="normal" onRollMode={() => {}} act={act} onRoll={() => {}} />);
  const lose = screen.getByRole<HTMLButtonElement>('button', { name: 'Lose 1 hit point' });
  expect(lose.getAttribute('aria-disabled')).toBe('true');
  expect(lose.disabled).toBe(false);
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Regain 1 hit point' }).getAttribute('aria-disabled')).toBeNull();
  await user.click(lose);
  expect(act).not.toHaveBeenCalled();
  cleanup();
  render(<SheetSummary view={withHp(35, 0)} rollMode="normal" onRollMode={() => {}} act={act} onRoll={() => {}} />);
  const regain = screen.getByRole<HTMLButtonElement>('button', { name: 'Regain 1 hit point' });
  expect(regain.getAttribute('aria-disabled')).toBe('true');
  await user.click(regain);
  expect(act).not.toHaveBeenCalled();
});

it('keeps "Lose" live at 0 hit points while temporary hit points remain', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<SheetSummary view={withHp(0, 5)} rollMode="normal" onRollMode={() => {}} act={act} onRoll={() => {}} />);
  const lose = screen.getByRole('button', { name: 'Lose 1 hit point' });
  expect(lose.getAttribute('aria-disabled')).toBeNull();
  await user.click(lose);
  expect(act).toHaveBeenCalledWith({ action: 'damage', amount: 1 });
});

it('keeps focus on "Regain" when the click reaches the maximum (WCAG 2.4.3)', async () => {
  const user = userEvent.setup();
  const props = { rollMode: 'normal' as const, onRollMode: () => {}, act: () => {}, onRoll: () => {} };
  const { rerender } = render(<SheetSummary view={withHp(34, 0)} {...props} />);
  const regain = screen.getByRole('button', { name: 'Regain 1 hit point' });
  regain.focus();
  await user.click(regain);
  rerender(<SheetSummary view={withHp(35, 0)} {...props} />);
  const after = screen.getByRole('button', { name: 'Regain 1 hit point' });
  expect(document.activeElement).toBe(after);
  expect(after.getAttribute('aria-disabled')).toBe('true');
});

/** The fixture view with one field replaced by an overridden copy. */
function overriddenView(id: string, value: number): CharacterView {
  const view = fixtureView();
  const fields = view.sheet.fields.map((f) =>
    f.field === id ? field(id, f.label, value, f.units ?? '', { field: id, value, reason: 'Fixture ruling' }) : f,
  );
  return { ...view, sheet: { ...view.sheet, fields } };
}

const strengthDd = () =>
  within(screen.getByRole('region', { name: 'Summary' })).getByText('Strength', { selector: 'dt' }).nextElementSibling!.textContent!;

it('marks an overridden ability score in text, and not the modifier', () => {
  render(<SheetSummary view={overriddenView('ability.str.score', 16)} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} />);
  expect(strengthDd()).toContain('16 (overridden)');
  expect(strengthDd()).not.toContain('modifier overridden');
});

it('marks an overridden ability modifier in text outside the button, whose name stays the contract', () => {
  render(<SheetSummary view={overriddenView('ability.str.mod', 5)} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Strength check (+5)' })).toBeTruthy();
  expect(strengthDd()).toContain('(modifier overridden)');
  expect(strengthDd()).not.toContain('16 (overridden)');
});

it('marks an overridden hit point maximum in text, before the temporary part', () => {
  const view = overriddenView('hitPoints', 40);
  const overridden = { ...view, sheet: { ...view.sheet, hitPoints: { maximum: 40, current: 28, temporary: 5 } } }; // the sheet carries the overridden maximum
  render(<SheetSummary view={overridden} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} />);
  expect(dd('Hit points')).toBe('28 of 40 (maximum overridden), 5 temporary');
});

it('styles the six ability roll buttons as buttons, not as link text (investigation 2026-10-05)', () => {
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} />);
  const rolls = screen.getAllByRole('button', { name: /^Roll / });
  expect(rolls).toHaveLength(6);
  for (const button of rolls) {
    expect(button.classList.contains('roll')).toBe(true);
    expect(button.classList.contains('link')).toBe(false);
  }
});

it('keeps one markup for both ability-box orders: modifier, then score, then the roll button (CSS reorders)', () => {
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} act={() => {}} onRoll={() => {}} />);
  const strength = within(screen.getByRole('region', { name: 'Summary' })).getByText('Strength', { selector: 'dt' }).nextElementSibling as HTMLElement;
  expect(strength.classList.contains('ability')).toBe(true);
  expect(Array.from(strength.children).map((c) => c.className || c.tagName.toLowerCase())).toEqual(['derived', 'score', 'roll']);
  expect(strength.textContent!.replace(/\s+/g, ' ').trim()).toBe('+3 16 Roll Strength check (+3)');
});
