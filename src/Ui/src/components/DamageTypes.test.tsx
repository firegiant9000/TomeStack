// @vitest-environment jsdom
// Wave 3 item 21 (D19 amended): the damage-type select beside the damage amount, the result line, the weapon pre-fill, and the
// Stats list and editor for damage defences.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import type { Character, CharacterView, DamageOutcome } from '../api/types';
import { DefencesPanel } from './DefencesPanel';
import { normalizeDamageType } from './customModel';
import { ActionsPanel, damageResultText, HitPointsPanel } from './PlayPanels';

afterEach(cleanup);

const outcome = (o: Partial<DamageOutcome>): DamageOutcome => ({ amount: 13, effect: 'none', adjusted: 13, absorbed: 0, lost: 13, warnings: [], ...o });

const base = (extra: object = {}, sheet: object = {}): CharacterView =>
  ({
    character: { id: 'fixture', name: 'Testy', ...extra },
    sheet: { characterId: 'fixture', rulesFamily: 'srd-5.2.1', diagnostics: [], fields: [], hitPoints: { maximum: 35, current: 35, temporary: 0 }, attacks: [], toggles: [], features: [], ...sheet },
  }) as unknown as CharacterView;

it('puts the damage type beside the amount with Untyped first, then the thirteen types', () => {
  render(<HitPointsPanel view={base()} act={() => {}} />);
  const select = screen.getByRole('combobox', { name: 'Damage type' });
  const options = within(select).getAllByRole('option').map((o) => o.textContent);
  expect(options[0]).toBe('Untyped');
  expect(options).toHaveLength(14);
  expect(options.slice(1, 4)).toEqual(['Acid', 'Bludgeoning', 'Cold']);
  expect((select as HTMLSelectElement).value).toBe('');
});

it('sends untyped damage with no type and typed damage with its type', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  render(<HitPointsPanel view={base()} act={act} />);

  await user.type(screen.getByRole('spinbutton', { name: 'Amount' }), '13');
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  expect(act).toHaveBeenLastCalledWith({ action: 'damage', amount: 13 });

  await user.selectOptions(screen.getByRole('combobox', { name: 'Damage type' }), 'slashing');
  await user.type(screen.getByRole('spinbutton', { name: 'Amount' }), '13');
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  expect(act).toHaveBeenLastCalledWith({ action: 'damage', amount: 13, damageType: 'slashing' });

  // The type belongs to damage only: healing sends none.
  await user.type(screen.getByRole('spinbutton', { name: 'Amount' }), '2');
  await user.click(screen.getByRole('button', { name: 'Heal' }));
  expect(act).toHaveBeenLastCalledWith({ action: 'heal', amount: 2 });
});

it.each([
  [outcome({ type: 'slashing', effect: 'halved', adjusted: 6, absorbed: 0, lost: 6 }), '13 slashing, halved: 6.'],
  [outcome({ type: 'fire', effect: 'immune', adjusted: 0, lost: 0 }), '13 fire, immune: 0.'],
  [outcome({ type: 'cold', effect: 'doubled', adjusted: 26, lost: 26 }), '13 cold, doubled: 26.'],
  [outcome({ type: 'slashing', effect: 'halvedThenDoubled', adjusted: 12, lost: 12 }), '13 slashing, halved, then doubled: 12.'],
  [outcome({ type: 'acid' }), '13 acid, no defence: 13.'],
  [outcome({}), '13 untyped: 13.'],
  [outcome({ type: 'slashing', effect: 'halved', adjusted: 6, absorbed: 5, lost: 1 }), '13 slashing, halved: 6. Temporary hit points took 5.'],
])('writes the result line in plain words (%#)', (damage, text) => {
  expect(damageResultText(damage)).toBe(text);
});

it('shows the result line and the massive-damage warning as text, and no live region', () => {
  const warning = { code: 'play.massive-damage', message: '35 damage was left over after reaching 0 hit points, which equals or exceeds the hit point maximum of 35.' };
  const view = { ...base(), damage: outcome({ type: 'slashing', effect: 'halved', adjusted: 6, warnings: [warning] }) } as CharacterView;
  const { container } = render(<HitPointsPanel view={view} act={() => {}} />);

  expect(screen.getByText('13 slashing, halved: 6.')).toBeTruthy();
  expect(screen.getByText(warning.message)).toBeTruthy();
  expect(container.querySelector('[aria-live], [role="status"], [role="alert"]')).toBeNull();
});

it('shows no result line when the view carries no damage outcome', () => {
  render(<HitPointsPanel view={base()} act={() => {}} />);
  expect(document.querySelector('.damage-result')).toBeNull();
});

const weapon = (name: string, damageType: string) => ({
  item: { contentId: `c-${name}`, revisionId: `r-${name}` },
  effectId: 'attack',
  name,
  toHit: 5,
  damage: '1d6+3',
  damageType,
  automation: 'automatic',
  category: 'martial',
  attack: 'melee',
  properties: [],
  trace: [],
  warnings: [],
});

// The sheet holds the type in one state shared by both panels; this harness does the same.
function Both({ view }: { view: CharacterView }) {
  const [type, setType] = useState('');
  return (
    <>
      <HitPointsPanel view={view} act={() => {}} damageType={type} onDamageType={setType} />
      <ActionsPanel view={view} roll={() => {}} act={() => {}} onDamageType={setType} />
    </>
  );
}

it('pre-fills the damage type from a weapon damage roll, normalised, and leaves it untyped when it does not map', async () => {
  const user = userEvent.setup();
  const view = base({ customAttacks: [{ id: 'a1', name: 'Testy Sling', toHit: 3, damage: '1d4', damageType: 'COLD' }] }, { attacks: [weapon('Fixture Blade', 'Slashing'), weapon('Fixture Odd', 'fire or cold')] });
  render(<Both view={view} />);
  const select = () => screen.getByRole('combobox', { name: 'Damage type' }) as HTMLSelectElement;

  await user.click(screen.getByRole('button', { name: /^Roll Fixture Blade damage/ }));
  expect(select().value).toBe('slashing');

  await user.click(screen.getByRole('button', { name: /^Roll Fixture Odd damage/ }));
  expect(select().value).toBe('');

  await user.click(screen.getByRole('button', { name: 'Roll Testy Sling damage' }));
  expect(select().value).toBe('cold');

  // The player can still change it.
  await user.selectOptions(select(), 'fire');
  expect(select().value).toBe('fire');
});

it('maps a free-text damage type to one of the thirteen, or to nothing', () => {
  expect(normalizeDamageType('Slashing')).toBe('slashing');
  expect(normalizeDamageType('FORCE')).toBe('force');
  expect(normalizeDamageType('slashing or piercing')).toBe('');
  expect(normalizeDamageType(' fire')).toBe('');
  expect(normalizeDamageType(undefined)).toBe('');
});

// ---- Stats: the defences list and the manual editor ---------------------------------------------------------------------

const defenceView = () =>
  base(
    { damageDefences: [{ type: 'slashing', kind: 'resistance' }] },
    {
      damageDefences: [
        { type: 'fire', kind: 'immunity', sources: [{ name: 'Testy Hide', content: { contentId: 'c', revisionId: 'r' }, effectId: 'g' }] },
        { type: 'slashing', kind: 'resistance', sources: [{ name: 'Entered by hand' }, { name: 'Testy Hide', content: { contentId: 'c', revisionId: 'r' }, effectId: 'g2' }] },
      ],
    },
  );

it('lists the sheet defences with their sources, read-only', () => {
  render(<DefencesPanel view={defenceView()} onChanged={() => {}} onError={() => {}} />);
  const list = screen.getByRole('list', { name: 'Damage defences the sheet applies' });
  const items = within(list).getAllByRole('listitem').map((li) => li.textContent);
  expect(items).toEqual(['Fire immunity from Testy Hide', 'Slashing resistance from Entered by hand, Testy Hide']);
});

it('says so when there are none', () => {
  render(<DefencesPanel view={base()} onChanged={() => {}} onError={() => {}} />);
  expect(screen.queryByRole('list', { name: 'Damage defences the sheet applies' })).toBeNull();
  expect(screen.getByText(/^None\./)).toBeTruthy();
});

it('adds a defence through the sheet save, moving focus to the heading', async () => {
  const user = userEvent.setup();
  const view = defenceView();
  const onChanged = vi.fn();
  const save = vi.fn(async (update: (c: Character) => Character) => ({ ...view, character: update(view.character) }));
  render(<DefencesPanel view={view} onChanged={onChanged} onError={() => {}} saveCharacter={save} />);

  await user.selectOptions(screen.getByRole('combobox', { name: 'Damage type' }), 'cold');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Kind' }), 'vulnerability');
  await user.click(screen.getByRole('button', { name: 'Add' }));

  expect(onChanged.mock.calls[0]?.[0].character.damageDefences).toEqual([
    { type: 'slashing', kind: 'resistance' },
    { type: 'cold', kind: 'vulnerability' },
  ]);
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Damage resistances, immunities and vulnerabilities' }));
});

it('removes a defence of its own, and refuses a duplicate before it is sent', async () => {
  const user = userEvent.setup();
  const view = defenceView();
  const onChanged = vi.fn();
  const save = vi.fn(async (update: (c: Character) => Character) => ({ ...view, character: update(view.character) }));
  render(<DefencesPanel view={view} onChanged={onChanged} onError={() => {}} saveCharacter={save} />);

  // Acid is the first type and Resistance the first kind: pick the recorded pair to make it a duplicate.
  await user.selectOptions(screen.getByRole('combobox', { name: 'Damage type' }), 'slashing');
  expect((screen.getByRole('button', { name: 'Add' }) as HTMLButtonElement).disabled).toBe(true);
  expect(screen.getByText('Already recorded.')).toBeTruthy();

  await user.click(screen.getByRole('button', { name: 'Remove slashing resistance' }));
  expect(onChanged.mock.calls[0]?.[0].character.damageDefences).toEqual([]);
});
