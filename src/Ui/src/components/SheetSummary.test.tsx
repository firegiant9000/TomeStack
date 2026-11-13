// @vitest-environment jsdom
// The summary bar: a <dl> of the core numbers (no headings, no regions named like the panels'), an overridden value
// says so, inspiration follows the family, conditions appear only when present, and the ability roll buttons roll.
import { act, cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { Character, CharacterSheet, CharacterView, DerivedValue, FieldOverride, PlayState, RollRecord, RulesFamilyPolicy } from '../api/types';
import { expectNoAxeViolations } from '../test/axe';
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

const policies = [
  { id: 'srd-5.1', displayName: 'SRD 5.1 (2014 rules)', inspirationName: 'Inspiration' },
  { id: 'srd-5.2.1', displayName: 'SRD 5.2.1 (2024 rules)', inspirationName: 'Heroic Inspiration' },
] as RulesFamilyPolicy[];

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

const statusBar = () => screen.getByRole('region', { name: 'Status' });
/** The text of one status-bar item's value (the item is a label and a value; a number's button holds both). */
const dd = (term: string) => within(statusBar()).getByText(term, { selector: '.status-label' }).parentElement!.querySelector('.status-value')!.textContent!.trim();

it('names the Spells tab in the rolling hint only when it is offered', () => {
  const { rerender } = render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />);
  expect(screen.getByText('Rolling never spends anything. Roll a check here; a save, skill or initiative from its field on Skills and saves; or an attack or feature on Play.')).toBeTruthy();
  rerender(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} spellsTab />);
  expect(screen.getByText('Rolling never spends anything. Roll a check here; a save, skill or initiative from its field on Skills and saves; an attack or feature on Play; or a spell on Spells.')).toBeTruthy();
  rerender(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} spellsTab hasQuickbar />);
  expect(screen.getByText('Rolling from here never spends anything; a quickbar button that says "and spend" rolls and then spends. Roll a check here; a save, skill or initiative from its field on Skills and saves; an attack or feature on Play; or a spell on Spells.')).toBeTruthy();
  rerender(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} hasQuickbar />);
  expect(screen.getByText('Rolling from here never spends anything; a quickbar button that says "and spend" rolls and then spends. Roll a check here; a save, skill or initiative from its field on Skills and saves; or an attack or feature on Play.')).toBeTruthy();
});

it('is a named Status section with the core numbers, no heading, no hit dice and no ability boxes by default', () => {
  const { container } = render(<SheetSummary view={fixtureView()} rulesFamilies={policies} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />);
  const bar = statusBar();
  expect(bar.tagName).toBe('SECTION');
  expect(bar.classList.contains('status-bar')).toBe(true);
  expect(within(bar).queryAllByRole('heading')).toHaveLength(0);
  expect(within(bar).queryAllByRole('region')).toHaveLength(0);
  expect(within(bar).getAllByRole('status').map((r) => r.getAttribute('aria-label'))).toEqual(['Last roll']);
  expect(dd('Proficiency bonus')).toBe('+2');
  expect(dd('Armor Class')).toBe('16 (set by hand)');
  expect(dd('Initiative')).toBe('+2');
  expect(dd('Speed')).toBe('30 ft.');
  expect(dd('Hit points')).toBe('28 of 35, 5 temporary');
  expect(within(bar).getByRole<HTMLInputElement>('checkbox', { name: 'Heroic Inspiration' }).checked).toBe(false);
  expect(within(bar).queryByText('Conditions', { selector: '.status-label' })).toBeNull();
  expect(within(bar).queryByText('Hit dice')).toBeNull();
  expect(container.querySelector('.summary-abilities')).toBeNull();
  expect(screen.queryByText('Strength')).toBeNull();
  expect(screen.queryByRole('button', { name: /^Roll [A-Za-z]+ check/ })).toBeNull();
});

it('renders the six ability boxes before the bar, not inside it, only with abilityBoxesTop', () => {
  const { container } = render(<SheetSummary view={fixtureView()} abilityBoxesTop {...plain} />);
  const boxes = container.querySelector('dl.summary-abilities')!;
  expect(boxes).toBeTruthy();
  expect(statusBar().contains(boxes)).toBe(false);
  expect(boxes.compareDocumentPosition(statusBar()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  expect(within(boxes as HTMLElement).getByText('+3', { selector: '.derived' })).toBeTruthy(); // the modifier is the large number (owner: modifier first by default)
  expect(within(boxes as HTMLElement).getByText('16', { selector: '.score' })).toBeTruthy();
});

it('keeps the bar in the specified DOM order', () => {
  const view = fixtureView({}, { temporaryHitPoints: 0, resources: [], conditions: ['poisoned'], exhaustion: 0, inspiration: false });
  render(<SheetSummary view={view} rulesFamilies={policies} {...plain} />);
  const bar = statusBar();
  const at = (el: Element) => Array.from(bar.querySelectorAll('*')).indexOf(el);
  const names = [
    screen.getByRole('button', { name: 'Lose 1 hit point' }),
    screen.getByRole('button', { name: /^Armor Class/ }),
    screen.getByRole('button', { name: /^Initiative/ }),
    screen.getByRole('button', { name: /^Speed/ }),
    screen.getByText('Proficiency bonus', { selector: '.status-label' }),
    screen.getByRole('checkbox', { name: 'Heroic Inspiration' }),
    screen.getByText('Conditions', { selector: '.status-label' }),
    screen.getByRole('radio', { name: 'Advantage' }),
    screen.getByRole('form', { name: 'Roll dice' }),
    screen.getByRole('status'),
  ];
  const positions = names.map(at);
  expect(positions).toEqual([...positions].sort((a, b) => a - b));
  expect(new Set(positions).size).toBe(positions.length);
  expect(screen.getByRole('button', { name: /^Hit points 28 of 35, 5 temporary, show why$/ })).toBeTruthy();
});

it('names each number as a button with aria-expanded and aria-controls, "Armor Class 16 (set by hand), show why"', () => {
  render(<SheetSummary view={fixtureView()} {...plain} />);
  const ac = screen.getByRole('button', { name: 'Armor Class 16 (set by hand), show why' });
  expect(ac.getAttribute('aria-expanded')).toBe('false');
  expect(ac.getAttribute('aria-controls')).toBe('status-why');
  for (const name of [/^Initiative \+2, show why$/, /^Speed 30 ft\., show why$/, /^Hit points 28 of 35.*, show why$/]) {
    expect(screen.getByRole('button', { name }).getAttribute('aria-controls')).toBe('status-why');
  }
  expect(document.querySelectorAll('#status-why')).toHaveLength(1);
});

it('opens a number\'s trace in the one status-why region, switches on another number and closes on the same one', async () => {
  const user = userEvent.setup();
  const base = fixtureView();
  const traced = (f: DerivedValue): DerivedValue => ({ ...f, trace: [{ order: 1, operation: 'base', description: 'Fixture step', result: f.value, origin: { kind: 'rulesPolicy', rulesFamily: 'srd-5.2.1' } }] });
  const fields = base.sheet.fields.map((f) => (f.field === 'armorClass' || f.field === 'initiative' ? traced(f) : f));
  render(<SheetSummary view={{ ...base, sheet: { ...base.sheet, fields } }} fields={fields} {...plain} />);
  const ac = screen.getByRole('button', { name: /^Armor Class/ });
  const init = screen.getByRole('button', { name: /^Initiative/ });
  const why = () => document.getElementById('status-why')!;
  expect(why().textContent).toBe('');
  await user.click(ac);
  expect(ac.getAttribute('aria-expanded')).toBe('true');
  expect(why().querySelector('[aria-label="Why Armor Class"]')).toBeTruthy();
  await user.click(init);
  expect(ac.getAttribute('aria-expanded')).toBe('false');
  expect(init.getAttribute('aria-expanded')).toBe('true');
  expect(why().querySelector('[aria-label="Why Initiative"]')).toBeTruthy();
  expect(why().querySelector('[aria-label="Why Armor Class"]')).toBeNull();
  expect(document.querySelectorAll('#status-why')).toHaveLength(1);
  await user.click(init);
  expect(init.getAttribute('aria-expanded')).toBe('false');
  expect(why().textContent).toBe('');
});

it('always has the one last-roll status node, and it holds the roll after lastRoll changes; Why and the log sit outside it', () => {
  const record: RollRecord = {
    formula: '1d20+3', mode: 'normal', critical: false,
    dice: [{ term: 0, sides: 20, value: 14, kept: true, fromCritical: false }],
    diceTotal: 14, expressionConstant: 0, modifiers: [{ label: 'Strength modifier', amount: 3 }], total: 17,
  };
  const { rerender, container } = render(<SheetSummary view={fixtureView()} {...plain} />);
  const status = screen.getByRole('status', { name: 'Last roll' });
  expect(status.getAttribute('aria-live')).toBe('polite');
  expect(container.querySelectorAll('[role=status]')).toHaveLength(1);
  expect(status.parentElement!.classList.contains('last-roll')).toBe(true);
  expect(status.textContent).not.toContain('17');
  rerender(<SheetSummary view={fixtureView()} {...plain} lastRoll={record} rollLog={[{ at: Date.now(), record }]} />);
  expect(screen.getByRole('status', { name: 'Last roll' })).toBe(status);
  expect(container.querySelectorAll('[role=status]')).toHaveLength(1);
  expect(status.textContent).toContain('17');
  for (const el of [container.querySelector('details.roll-why')!, container.querySelector('details.roll-log')!]) {
    expect(el).toBeTruthy();
    expect(status.contains(el)).toBe(false);
    expect(el.closest('[role=status],[aria-live]')).toBeNull();
  }
  expect(within(container).getByText(/Earlier rolls/)).toBeTruthy();
});

it('with Announce each roll off there is no polite roll region in the bar', () => {
  localStorage.setItem('tomestack.announceRolls', 'off');
  try {
    const { container } = render(<SheetSummary view={fixtureView()} {...plain} />);
    expect(screen.getByRole('status', { name: 'Last roll' }).getAttribute('aria-live')).toBe('off');
    expect(container.querySelector('.last-roll [aria-live=polite]')).toBeNull(); // the dice box's own error line is a separate, empty polite region
  } finally {
    localStorage.removeItem('tomestack.announceRolls');
  }
});

it('has no axe violations with a trace open and ability boxes on top', async () => {
  const user = userEvent.setup();
  const { container } = render(<SheetSummary view={fixtureView()} abilityBoxesTop {...plain} />);
  await user.click(screen.getByRole('button', { name: /^Speed/ }));
  await expectNoAxeViolations(container);
});

it('has no axe violations in the summary bar with a roll result', async () => {
  const record: RollRecord = {
    formula: '1d20+3',
    mode: 'normal',
    critical: false,
    dice: [{ term: 0, sides: 20, value: 14, kept: true, fromCritical: false }],
    diceTotal: 14,
    expressionConstant: 0,
    modifiers: [{ label: 'Strength modifier', amount: 3 }],
    total: 17,
  };
  const { container } = render(
    <SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} lastRoll={record} rollLog={[{ at: Date.now(), record }]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />,
  );
  expect(screen.getByRole('status', { name: 'Last roll' }).textContent).toContain('17');
  await expectNoAxeViolations(container);
});

it('names inspiration by family and lists conditions and exhaustion when present', () => {
  render(
    <SheetSummary
      view={fixtureView({}, { temporaryHitPoints: 0, resources: [], conditions: ['poisoned', 'prone'], exhaustion: 2, inspiration: true }, 'srd-5.1')}
      rulesFamilies={policies}
      rollMode="normal"
      onRollMode={() => {}}
      rollLog={[]}
      act={() => {}}
      onRoll={() => {}} onRollDice={async () => {}}
    />,
  );
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Inspiration' }).checked).toBe(true);
  expect(dd('Conditions')).toBe('Poisoned, Prone, exhaustion 2');
});

it('rolls an ability check from its button, named apart from the field card\'s "Roll … modifier"', async () => {
  const user = userEvent.setup();
  const onRoll = vi.fn();
  render(<SheetSummary view={fixtureView()} abilityBoxesTop rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={onRoll} onRollDice={async () => {}} />);
  await user.click(screen.getByRole('button', { name: 'Roll Strength check (+3)' }));
  expect(onRoll).toHaveBeenCalledWith('ability.str.mod');
  // The boxes' roll buttons are exactly the six ability checks, none of them the field cards' "Roll … modifier".
  const rollButtons = screen.getAllByRole('button', { name: /^Roll [A-Za-z]+ check/ });
  expect(rollButtons).toHaveLength(6);
  for (const button of rollButtons) {
    expect(button.textContent!.replace(/\s+/g, ' ').trim()).toMatch(/^Roll (Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma) check \([+-]\d+\)$/);
  }
  expect(screen.getByRole('button', { name: 'Roll Intelligence check (-1)' })).toBeTruthy();
  expect(screen.getByRole('radio', { name: 'Advantage' })).toBeTruthy(); // the roll-mode picker lives here now
});

it('shows "Roll" on the button and keeps the full accessible name (2.5.3 label in name; D28)', () => {
  render(<SheetSummary view={fixtureView()} abilityBoxesTop rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />);
  const button = screen.getByRole('button', { name: 'Roll Strength check (+3)' });
  const visible = Array.from(button.childNodes).filter((n) => !(n instanceof HTMLElement && n.classList.contains('visually-hidden'))).map((n) => n.textContent).join('').trim();
  expect(visible).toBe('Roll');
  expect(button.querySelector('.visually-hidden')?.textContent).toBe(' Strength check (+3)');
  expect(button.className).toBe('roll');
});

it('puts the dice box beside the d20 mode, outside the fixed roll strip (item 8)', async () => {
  const user = userEvent.setup();
  const onRollDice = vi.fn().mockResolvedValue(undefined);
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={onRollDice} />);
  const summary = statusBar();
  const strip = summary.querySelector('.roll-strip')!;
  const box = within(summary).getByRole('form', { name: 'Roll dice' });
  expect(strip.contains(box)).toBe(false);
  expect(box.previousElementSibling?.classList.contains('roll-mode')).toBe(true);
  expect(strip.children[0]?.getAttribute('aria-label')).toBe('Last roll');
  expect(strip.querySelector('input, form')).toBeNull();
  expect(within(summary).queryAllByRole('region')).toHaveLength(0);
  expect(within(summary).getAllByRole('status').map((r) => r.getAttribute('aria-label'))).toEqual(['Last roll']);
  await user.type(within(box).getByRole('textbox', { name: 'Dice to roll' }), '1d4{Enter}');
  expect(onRollDice).toHaveBeenCalledWith('1d4');
});

it('toggles inspiration from the summary through the play command', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<SheetSummary view={fixtureView()} rulesFamilies={policies} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={act} onRoll={() => {}} onRollDice={async () => {}} />);
  await user.click(screen.getByRole('checkbox', { name: 'Heroic Inspiration' }));
  expect(act).toHaveBeenCalledWith({ action: 'setInspiration', amount: 1 });
});

it('adjusts hit points by one from the summary without changing the Hit points text', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={act} onRoll={() => {}} onRollDice={async () => {}} />);
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
  render(<SheetSummary view={withHp(0, 0)} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={act} onRoll={() => {}} onRollDice={async () => {}} />);
  const lose = screen.getByRole<HTMLButtonElement>('button', { name: 'Lose 1 hit point' });
  expect(lose.getAttribute('aria-disabled')).toBe('true');
  expect(lose.disabled).toBe(false);
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Regain 1 hit point' }).getAttribute('aria-disabled')).toBeNull();
  await user.click(lose);
  expect(act).not.toHaveBeenCalled();
  cleanup();
  render(<SheetSummary view={withHp(35, 0)} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={act} onRoll={() => {}} onRollDice={async () => {}} />);
  const regain = screen.getByRole<HTMLButtonElement>('button', { name: 'Regain 1 hit point' });
  expect(regain.getAttribute('aria-disabled')).toBe('true');
  await user.click(regain);
  expect(act).not.toHaveBeenCalled();
});

it('keeps "Lose" live at 0 hit points while temporary hit points remain', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<SheetSummary view={withHp(0, 5)} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={act} onRoll={() => {}} onRollDice={async () => {}} />);
  const lose = screen.getByRole('button', { name: 'Lose 1 hit point' });
  expect(lose.getAttribute('aria-disabled')).toBeNull();
  await user.click(lose);
  expect(act).toHaveBeenCalledWith({ action: 'damage', amount: 1 });
});

it('keeps focus on "Regain" when the click reaches the maximum (WCAG 2.4.3)', async () => {
  const user = userEvent.setup();
  const props = { rollMode: 'normal' as const, onRollMode: () => {}, rollLog: [], act: () => {}, onRoll: () => {}, onRollDice: async () => {} };
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

const strengthDd = () => screen.getByText('Strength', { selector: 'dt' }).nextElementSibling!.textContent!;

it('marks an overridden ability score in text, and not the modifier', () => {
  render(<SheetSummary view={overriddenView('ability.str.score', 16)} abilityBoxesTop rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />);
  expect(strengthDd()).toContain('16 (set by hand)');
  expect(strengthDd()).not.toContain('modifier set by hand');
});

it('marks an overridden ability modifier in text outside the button, whose name stays the contract', () => {
  render(<SheetSummary view={overriddenView('ability.str.mod', 5)} abilityBoxesTop rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Strength check (+5)' })).toBeTruthy();
  expect(strengthDd()).toContain('(modifier set by hand)');
  expect(strengthDd()).not.toContain('16 (set by hand)');
});

it('marks an overridden hit point maximum in text, before the temporary part', () => {
  const view = overriddenView('hitPoints', 40);
  const overridden = { ...view, sheet: { ...view.sheet, hitPoints: { maximum: 40, current: 28, temporary: 5 } } }; // the sheet carries the overridden maximum
  render(<SheetSummary view={overridden} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />);
  expect(dd('Hit points')).toBe('28 of 40 (maximum set by hand), 5 temporary');
});

it('styles the six ability roll buttons as buttons, not as link text (investigation 2026-10-05)', () => {
  render(<SheetSummary view={fixtureView()} abilityBoxesTop rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />);
  const rolls = screen.getAllByRole('button', { name: /^Roll [A-Za-z]+ check/ });
  expect(rolls).toHaveLength(6);
  for (const button of rolls) {
    expect(button.classList.contains('roll')).toBe(true);
    expect(button.classList.contains('link')).toBe(false);
  }
});

it('keeps one markup for both ability-box orders: modifier, then score, then the roll button (CSS reorders)', () => {
  render(<SheetSummary view={fixtureView()} abilityBoxesTop rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />);
  const strength = screen.getByText('Strength', { selector: 'dt' }).nextElementSibling as HTMLElement;
  expect(strength.classList.contains('ability')).toBe(true);
  expect(Array.from(strength.children).map((c) => c.className || c.tagName.toLowerCase())).toEqual(['derived', 'score', 'roll']);
  expect(strength.textContent!.replace(/\s+/g, ' ').trim()).toBe('+3 16 Roll Strength check (+3)');
});

const reason = (mode: 'advantage' | 'disadvantage', source: string, against?: string[]) => ({
  mode, source, content: { contentId: 'c1', revisionId: 'r1' }, against, automatic: !against,
});
const withMode = (id: string, mode: NonNullable<DerivedValue['mode']>): CharacterView => {
  const v = fixtureView();
  return { ...v, sheet: { ...v.sheet, fields: v.sheet.fields.map((f) => (f.field === id ? { ...f, mode } : f)) } };
};
const plain = { rollMode: 'normal' as const, onRollMode: () => {}, rollLog: [], act: () => {}, onRoll: () => {}, onRollDice: async () => {} };

it('a check with automatic advantage says so on its button and in its accessible name', async () => {
  const user = userEvent.setup();
  const onRoll = vi.fn();
  const mode = { automatic: 'advantage' as const, reasons: [reason('advantage', 'Fixture Swift Instinct'), reason('advantage', 'Fixture Light Step')] };
  render(<SheetSummary view={withMode('ability.dex.mod', mode)} abilityBoxesTop {...plain} onRoll={onRoll} />);
  const button = screen.getByRole('button', { name: 'Roll (advantage: Fixture Swift Instinct, Fixture Light Step) Dexterity check (+2)' });
  expect(button.textContent!.replace(/\s+/g, ' ').trim()).toBe('Roll (advantage: Fixture Swift Instinct, Fixture Light Step) Dexterity check (+2)');
  expect(button.querySelector('.visually-hidden')?.textContent).toBe(' Dexterity check (+2)');
  await user.click(button);
  expect(onRoll).toHaveBeenCalledWith('ability.dex.mod');
  expect(screen.getByRole('button', { name: 'Roll Strength check (+3)' })).toBeTruthy(); // the others are unchanged
});

it('advantage and disadvantage together read as normal with the reason, on the summary button', () => {
  const mode = { automatic: 'normal' as const, reasons: [reason('advantage', 'Fixture Swift Instinct'), reason('disadvantage', 'Fixture Heavy Armor')] };
  render(<SheetSummary view={withMode('ability.dex.mod', mode)} abilityBoxesTop {...plain} />);
  expect(screen.getByRole('button', { name: /^Roll \(normal: advantage and disadvantage cancel\) Dexterity check/ })).toBeTruthy();
});

it('an assisted mode leaves the summary button as it was', () => {
  const mode = { automatic: 'normal' as const, reasons: [reason('advantage', 'Fixture Swift Instinct', ['paralyzed'])] };
  render(<SheetSummary view={withMode('ability.dex.mod', mode)} abilityBoxesTop {...plain} />);
  expect(screen.getByRole('button', { name: 'Roll Dexterity check (+2)' })).toBeTruthy();
});

// Content v12 (G7): attacks other creatures make against the character, shown beside Armor Class.
const against = (attacksAgainst?: NonNullable<CharacterSheet['attacksAgainst']>): CharacterView => {
  const v = fixtureView();
  return { ...v, sheet: { ...v.sheet, attacksAgainst } };
};

it('shows attacks against you with prevent wording', () => {
  const mode = {
    automatic: 'advantage' as const,
    reasons: [
      reason('advantage', 'Fixture Open Guard'),
      reason('advantage', 'Fixture Soft Spot'),
      { ...reason('disadvantage', 'Fixture Steady Ward'), prevent: true },
    ],
  };
  render(<SheetSummary view={against(mode)} {...plain} />);
  expect(dd('Attacks against you')).toBe("have advantage (Fixture Open Guard, Fixture Soft Spot); can't have disadvantage (Fixture Steady Ward)");
  expect(dd('Armor Class')).toBe('16 (set by hand)'); // the Armor Class value is untouched
});

it('says have disadvantage, and can not have advantage, with their sources', () => {
  const mode = {
    automatic: 'disadvantage' as const,
    reasons: [reason('disadvantage', 'Fixture Blur'), { ...reason('advantage', 'Fixture Steady Ward'), prevent: true }],
  };
  render(<SheetSummary view={against(mode)} {...plain} />);
  expect(dd('Attacks against you')).toBe("have disadvantage (Fixture Blur); can't have advantage (Fixture Steady Ward)");
});

it('shows only the prevent part when the automatic mode is normal', () => {
  const mode = { automatic: 'normal' as const, reasons: [{ ...reason('advantage', 'Fixture Steady Ward'), prevent: true }] };
  render(<SheetSummary view={against(mode)} {...plain} />);
  expect(dd('Attacks against you')).toBe("can't have advantage (Fixture Steady Ward)");
});

it('shows the cancel when automatic advantage and disadvantage against you cancel', () => {
  const mode = { automatic: 'normal' as const, reasons: [reason('advantage', 'Fixture Open Guard'), reason('disadvantage', 'Fixture Blur')] };
  render(<SheetSummary view={against(mode)} {...plain} />);
  expect(dd('Attacks against you')).toBe('normal (advantage and disadvantage cancel: Fixture Open Guard, Fixture Blur)');
});

it('shows an assisted reason against you with its note (it used to hide the line)', () => {
  const mode = { automatic: 'normal' as const, reasons: [{ ...reason('advantage', 'Fixture Open Guard'), automatic: false }] };
  render(<SheetSummary view={against(mode)} {...plain} />);
  expect(dd('Attacks against you')).toBe('advantage (Fixture Open Guard), not applied automatically');
});

it('shows an assisted prevent reason against you with its note', () => {
  const mode = { automatic: 'normal' as const, reasons: [{ ...reason('advantage', 'Fixture Steady Ward'), automatic: false, prevent: true }] };
  render(<SheetSummary view={against(mode)} {...plain} />);
  expect(dd('Attacks against you')).toBe("can't have advantage (Fixture Steady Ward), not applied automatically");
});

it('no attacks-against line without reasons', () => {
  render(<SheetSummary view={against(undefined)} {...plain} />);
  expect(screen.queryByText('Attacks against you')).toBeNull();
});

it('fly speed shows only when above 0', () => {
  const base = fixtureView();
  const flying = (n: number): CharacterView => ({ ...base, sheet: { ...base.sheet, fields: [...base.sheet.fields, field('speed.fly', 'Fly speed', n, 'feet')] } });
  const { rerender } = render(<SheetSummary view={flying(0)} {...plain} />);
  expect(dd('Speed')).toBe('30 ft.');
  rerender(<SheetSummary view={flying(30)} {...plain} />);
  expect(dd('Speed')).toBe('30 ft., Fly 30 ft.');
  const overridden = { ...field('speed.fly', 'Fly speed', 0, 'feet'), computedValue: 30, override: { field: 'speed.fly', value: 0 } };
  rerender(<SheetSummary view={{ ...base, sheet: { ...base.sheet, fields: [...base.sheet.fields, overridden] } }} {...plain} />);
  expect(dd('Speed')).toBe('30 ft., Fly 0 ft.'); // an override of 0 stays visible
});

it('summary lists fly, climb and swim in that order', () => {
  const base = fixtureView();
  const withFields = (...extra: ReturnType<typeof field>[]): CharacterView => ({ ...base, sheet: { ...base.sheet, fields: [...base.sheet.fields, ...extra] } });
  const { rerender } = render(<SheetSummary view={withFields(field('speed.climb', 'Climb speed', 0, 'feet'), field('speed.swim', 'Swim speed', 0, 'feet'))} {...plain} />);
  expect(dd('Speed')).toBe('30 ft.');
  rerender(<SheetSummary view={withFields(field('speed.swim', 'Swim speed', 20, 'feet'), field('speed.climb', 'Climb speed', 25, 'feet'), field('speed.fly', 'Fly speed', 30, 'feet'))} {...plain} />);
  expect(dd('Speed')).toBe('30 ft., Fly 30 ft., Climb 25 ft., Swim 20 ft.');
  const overridden = { ...field('speed.climb', 'Climb speed', 0, 'feet'), computedValue: 30, override: { field: 'speed.climb', value: 0 } };
  rerender(<SheetSummary view={withFields(overridden)} {...plain} />);
  expect(dd('Speed')).toBe('30 ft., Climb 0 ft.');
});

it('shows a "Bloodied" chip as text when the sheet says bloodied, in the hit points box, with no live region (D38)', () => {
  const base = fixtureView();
  const summary = (bloodied?: boolean) => (
    <SheetSummary view={{ ...base, sheet: { ...base.sheet, hitPoints: { maximum: 35, current: bloodied ? 17 : 18, temporary: 0 }, bloodied } }} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />
  );
  const { rerender } = render(summary(undefined));
  const region = statusBar();
  expect(within(region).queryByText('Bloodied')).toBeNull();

  rerender(summary(true));
  const chip = within(region).getByText('Bloodied');
  expect(chip.closest('.status-hp')!.querySelector('.status-label')!.textContent).toBe('Hit points');
  expect(chip.closest('[aria-live]')).toBeNull();
  expect(chip.getAttribute('role')).toBeNull();
  expect(dd('Hit points')).toBe('17 of 35'); // the Hit points text itself is unchanged
});

it('puts the Why? disclosure in the roll strip, outside the Last roll live region', () => {
  const record: RollRecord = {
    formula: '1d20+3',
    mode: 'normal',
    critical: false,
    dice: [{ term: 0, sides: 20, value: 14, kept: true, fromCritical: false }],
    diceTotal: 14,
    expressionConstant: 0,
    modifiers: [{ label: 'Strength modifier', amount: 3 }],
    total: 17,
  };
  const { container } = render(
    <SheetSummary view={fixtureView()} rollMode="normal" onRollMode={() => {}} lastRoll={record} rollLog={[]} act={() => {}} onRoll={() => {}} onRollDice={async () => {}} />,
  );
  const why = container.querySelector('details.roll-why') as HTMLElement;
  expect(why).toBeTruthy();
  expect(why.closest('.roll-strip')).toBeTruthy();
  expect(screen.getByRole('status', { name: 'Last roll' }).contains(why)).toBe(false);
  expect(why.closest('[role=status],[aria-live]')).toBeNull();
});

it('puts a quickbar slot inside the bar, after the numbers, before the roll controls and outside the roll strip (D73)', () => {
  const { container } = render(
    <SheetSummary
      view={fixtureView()}
      rollMode="normal"
      onRollMode={() => {}}
      rollLog={[]}
      act={() => {}}
      onRoll={() => {}}
      onRollDice={async () => {}}
      quickbar={
        <section className="quickbar" aria-label="Quickbar">
          <button type="button">Fixture</button>
        </section>
      }
    />,
  );
  const bar = screen.getByRole('region', { name: 'Quickbar' });
  expect(bar.closest('.status-bar')).toBeTruthy();
  expect(bar.closest('.roll-strip')).toBeNull();
  expect(bar.parentElement).toBe(container.querySelector('.status-bar')); // a direct child: the grid's `quick` row
  expect(container.querySelector('.status-line')!.nextElementSibling).toBe(bar);
  expect(bar.nextElementSibling!.classList.contains('roll-controls')).toBe(true);
});

// Task 3.3: the observer behind the sticky bar (jsdom has no layout, so ResizeObserver and the height are stubbed).
function stubObserver(height: number, viewport: number) {
  const instances: { callback: () => void; disconnect: ReturnType<typeof vi.fn> }[] = [];
  class FakeObserver {
    callback: () => void;
    disconnect = vi.fn();
    constructor(callback: () => void) {
      this.callback = callback;
      instances.push(this);
    }
    observe() {}
    unobserve() {}
  }
  vi.stubGlobal('ResizeObserver', FakeObserver);
  vi.stubGlobal('innerHeight', viewport);
  const heightSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect');
  const set = (h: number) => heightSpy.mockReturnValue({ height: h } as DOMRect);
  set(height);
  return { instances, set };
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  document.documentElement.style.removeProperty('--status-bar-height');
  document.documentElement.style.removeProperty('--status-bar-pin');
});
const rootVar = (name: string) => document.documentElement.style.getPropertyValue(name);

it('sets --status-bar-height from the bar, flags data-tall over a third of the viewport, and cleans up on unmount', () => {
  const { instances, set } = stubObserver(120, 600);
  const { unmount } = render(<SheetSummary view={fixtureView()} {...plain} />);
  const bar = statusBar();
  expect(document.documentElement.style.getPropertyValue('--status-bar-height')).toBe('120px');
  expect(bar.hasAttribute('data-tall')).toBe(false);
  set(250); // over 600 / 3
  act(() => instances[0]!.callback());
  expect(document.documentElement.style.getPropertyValue('--status-bar-height')).toBe('250px');
  expect(bar.hasAttribute('data-tall')).toBe(true);
  set(100);
  act(() => instances[0]!.callback());
  expect(bar.hasAttribute('data-tall')).toBe(false);
  unmount();
  expect(instances[0]!.disconnect).toHaveBeenCalled();
  expect(rootVar('--status-bar-height')).toBe('');
  expect(rootVar('--status-bar-pin')).toBe('');
});

it('--status-bar-pin is the height while pinned and 0px while tall; --status-bar-height keeps the full height', () => {
  const { instances, set } = stubObserver(120, 600);
  render(<SheetSummary view={fixtureView()} {...plain} />);
  expect(rootVar('--status-bar-pin')).toBe('120px');
  set(250);
  act(() => instances[0]!.callback());
  expect(rootVar('--status-bar-height')).toBe('250px');
  expect(rootVar('--status-bar-pin')).toBe('0px');
});

it('without ResizeObserver the bar is still measured once and on window resize', () => {
  vi.stubGlobal('ResizeObserver', undefined);
  vi.stubGlobal('innerHeight', 600);
  const heightSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect');
  heightSpy.mockReturnValue({ height: 120 } as DOMRect);
  render(<SheetSummary view={fixtureView()} {...plain} />);
  expect(rootVar('--status-bar-height')).toBe('120px');
  expect(rootVar('--status-bar-pin')).toBe('120px');
  heightSpy.mockReturnValue({ height: 300 } as DOMRect);
  act(() => {
    window.dispatchEvent(new Event('resize'));
  });
  expect(statusBar().hasAttribute('data-tall')).toBe(true);
});

it('a window resize re-evaluates data-tall even when the bar itself did not resize, and the listener goes on unmount', () => {
  stubObserver(150, 600);
  const { unmount } = render(<SheetSummary view={fixtureView()} {...plain} />);
  const bar = statusBar();
  expect(bar.hasAttribute('data-tall')).toBe(false);
  vi.stubGlobal('innerHeight', 300); // 150 > 300 / 3
  act(() => {
    window.dispatchEvent(new Event('resize'));
  });
  expect(bar.hasAttribute('data-tall')).toBe(true);
  expect(rootVar('--status-bar-pin')).toBe('0px');
  unmount();
  vi.stubGlobal('innerHeight', 900);
  window.dispatchEvent(new Event('resize'));
  expect(rootVar('--status-bar-pin')).toBe('');
});
