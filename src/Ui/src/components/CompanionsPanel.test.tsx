// @vitest-environment jsdom
// Wave 3 item 23 (D39): companions on Play. One named region per companion; hit points go through play actions only; the definition
// (name, kind, Armor Class, maximum, speed, attack lines, note) is saved through the sheet's character save; attack lines roll.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect, useState } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterView, Companion, CompanionPlay, PlayAction, RollTarget } from '../api/types';
import { CompanionsPanel } from './CompanionsPanel';

vi.mock('../api/client', () => ({ client: { saveCharacter: vi.fn(), rollDice: vi.fn() } }));

const hound: Companion = {
  id: 'k1',
  name: 'Testy Hound',
  kind: 'Fixture beast',
  armorClass: 13,
  hitPointMaximum: 20,
  speed: '40 ft.',
  attacks: [
    { id: 'a1', name: 'Testy Bite', toHit: 4, damage: '1d6+2', damageType: 'piercing' },
    { id: 'a2', name: 'Testy Howl', toHit: -1 },
  ],
  note: 'Fixture note',
};
const crow: Companion = { id: 'k2', name: 'Testy Crow', hitPointMaximum: 5 };

const view = (companions: Companion[] | undefined, play?: { companions: CompanionPlay[] }): CharacterView =>
  ({ character: { id: 'fixture', rulesFamily: 'srd-5.2.1', name: 'Testy Handler', companions, play: { resources: [], conditions: [], ...play } } }) as unknown as CharacterView;

let acted: PlayAction[];
let rolled: RollTarget[];
let status: string[];
let errors: unknown[];

beforeEach(() => {
  acted = [];
  rolled = [];
  status = [];
  errors = [];
  vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ character: c }) as unknown as CharacterView);
  vi.mocked(client.rollDice).mockResolvedValue({} as never);
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

/** Holds the view like the sheet does, so a save re-renders the panel without a remount. */
function Harness({ initial }: { initial: CharacterView }) {
  const [current, setCurrent] = useState(initial);
  useEffect(() => {
    void current;
  });
  return (
    <CompanionsPanel
      view={current}
      act={(a) => acted.push(a)}
      roll={(t) => rolled.push(t)}
      onChanged={setCurrent}
      onError={(e) => errors.push(e)}
      onStatus={(t) => status.push(t)}
    />
  );
}

const show = (companions: Companion[] | undefined = [hound, crow], play?: { companions: CompanionPlay[] }) => render(<Harness initial={view(companions, play)} />);
const card = (name: string) => screen.getByRole('region', { name });
const held = (button: HTMLElement) => button.getAttribute('aria-disabled') === 'true';

it('shows a Companions section with an empty line and the Add button when there are none', () => {
  show([]);
  expect(screen.getByRole('heading', { name: 'Companions' })).toBeTruthy();
  expect(screen.getByText('No companions yet.')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Add a companion' })).toBeTruthy();
});

it('shows one named region per companion with kind, Armor Class, hit points, speed, attack lines and note', () => {
  show([hound, crow], { companions: [{ id: 'k1', current: 13, temporary: 4 }] });
  const dog = within(card('Testy Hound'));
  expect(dog.getByText('Fixture beast')).toBeTruthy();
  expect(dog.getByText('Armor Class 13')).toBeTruthy();
  expect(dog.getByText('Hit points: 13 of 20, 4 temporary')).toBeTruthy();
  expect(dog.getByText('Speed 40 ft.')).toBeTruthy();
  expect(dog.getByText('Testy Bite: +4 to hit, 1d6+2 piercing')).toBeTruthy();
  expect(dog.getByText('Fixture note')).toBeTruthy();
  // No recorded hit points: at the maximum. No Armor Class or speed written: nothing shown for them.
  const bird = within(card('Testy Crow'));
  expect(bird.getByText('Hit points: 5 of 5')).toBeTruthy();
  expect(bird.queryByText(/Armor Class/)).toBeNull();
  expect(bird.queryByText(/Speed/)).toBeNull();
});

it('sends damage, heal and set as play actions for that companion, clears the amount and keeps focus on the pressed button', async () => {
  const user = userEvent.setup();
  show([hound, crow], { companions: [{ id: 'k1', current: 13, temporary: 4 }] });
  const dog = within(card('Testy Hound'));

  await user.type(dog.getByRole('spinbutton', { name: 'Amount' }), '6');
  await user.click(dog.getByRole('button', { name: /^Take damage/ }));
  expect(acted).toEqual([{ action: 'companionDamage', companionId: 'k1', amount: 6 }]);
  expect((dog.getByRole('spinbutton', { name: 'Amount' }) as HTMLInputElement).value).toBe('');
  expect(document.activeElement).toBe(dog.getByRole('button', { name: /^Take damage/ }));

  await user.type(dog.getByRole('spinbutton', { name: 'Amount' }), '3');
  await user.click(dog.getByRole('button', { name: /^Heal/ }));
  await user.type(dog.getByRole('spinbutton', { name: 'Amount' }), '9');
  await user.click(dog.getByRole('button', { name: /^Set hit points/ }));
  await user.type(dog.getByRole('spinbutton', { name: 'Amount' }), '2');
  await user.click(dog.getByRole('button', { name: /^Set temporary hit points/ }));
  expect(acted.slice(1)).toEqual([
    { action: 'companionHeal', companionId: 'k1', amount: 3 },
    { action: 'companionSetHitPoints', companionId: 'k1', amount: 9 },
    { action: 'companionSetTemporaryHitPoints', companionId: 'k1', amount: 2 }, // only the temporary hit points: the server keeps the stored current
  ]);
});

it('points every held button at "Enter an amount first." while the amount is blank', () => {
  show();
  const dog = within(card('Testy Hound'));
  for (const name of [/^Take damage/, /^Heal/, /^Set hit points/, /^Set temporary hit points/]) {
    const button = dog.getByRole('button', { name });
    expect(held(button)).toBe(true);
    expect(document.getElementById(button.getAttribute('aria-describedby')!)?.textContent).toBe('Enter an amount first.');
  }
});

it('refuses a press with no amount, a bad amount, or a current above the maximum, with a linked reason and focus kept', async () => {
  const user = userEvent.setup();
  show();
  const dog = within(card('Testy Hound'));
  const damage = dog.getByRole('button', { name: /^Take damage/ });

  expect(held(damage)).toBe(true);
  expect(damage.hasAttribute('disabled')).toBe(false);
  await user.click(damage);
  expect(acted).toEqual([]);

  const amount = dog.getByRole('spinbutton', { name: 'Amount' });
  await user.type(amount, '-3');
  expect(held(dog.getByRole('button', { name: /^Heal/ }))).toBe(true);
  expect(document.getElementById(amount.getAttribute('aria-describedby')!)?.textContent).toBe('Enter a whole number from 0 to 10000.');
  await user.clear(amount);

  await user.type(amount, '21');
  const set = dog.getByRole('button', { name: /^Set hit points/ });
  expect(held(set)).toBe(true);
  expect(held(dog.getByRole('button', { name: /^Take damage/ }))).toBe(false); // damage and heal accept any amount in range
  expect(document.getElementById(set.getAttribute('aria-describedby')!)?.textContent).toBe('Current hit points cannot be above the maximum (20).');
  await user.click(set);
  expect(acted).toEqual([]);
  expect(document.activeElement).toBe(set);
});

it('rolls an attack line and its damage, and offers no damage button for a line without damage', async () => {
  const user = userEvent.setup();
  show();
  const dog = within(card('Testy Hound'));
  await user.click(dog.getByRole('button', { name: /^Roll Testy Bite attack/ }));
  await user.click(dog.getByRole('button', { name: /^Roll Testy Bite damage/ }));
  await user.click(dog.getByRole('button', { name: /^Roll Testy Howl attack/ }));
  expect(dog.queryByRole('button', { name: /Roll Testy Howl damage/ })).toBeNull();
  expect(rolled).toEqual([
    { companionAttack: { companionId: 'k1', attackId: 'a1' }, mode: 'normal' },
    { companionAttack: { companionId: 'k1', attackId: 'a1' }, damage: true, critical: false },
    { companionAttack: { companionId: 'k1', attackId: 'a2' }, mode: 'normal' },
  ]);
});

it('doubles the dice of a companion damage roll when its own critical box is ticked', async () => {
  const user = userEvent.setup();
  show();
  await user.click(screen.getByRole('checkbox', { name: 'Companion critical hit (double the damage dice)' }));
  await user.click(within(card('Testy Hound')).getByRole('button', { name: /^Roll Testy Bite damage/ }));
  expect(rolled).toEqual([{ companionAttack: { companionId: 'k1', attackId: 'a1' }, damage: true, critical: true }]);
});

it('adds a companion with an attack line, keeps the other character fields, and moves focus to its Edit button', async () => {
  const user = userEvent.setup();
  show([crow]);
  await user.click(screen.getByRole('button', { name: 'Add a companion' }));
  expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Name' }));
  await user.type(screen.getByRole('textbox', { name: 'Name' }), '  Testy Mule ');
  await user.type(screen.getByRole('textbox', { name: 'Kind (optional)' }), 'Fixture mount');
  await user.type(screen.getByRole('spinbutton', { name: 'Armor Class (optional)' }), '10');
  await user.clear(screen.getByRole('spinbutton', { name: 'Hit point maximum' }));
  await user.type(screen.getByRole('spinbutton', { name: 'Hit point maximum' }), '11');
  await user.click(screen.getByRole('button', { name: 'Add an attack line' }));
  await user.type(screen.getByRole('textbox', { name: 'Attack 1 name' }), 'Testy Kick');
  await user.clear(screen.getByRole('spinbutton', { name: 'Attack 1 to hit bonus' }));
  await user.type(screen.getByRole('spinbutton', { name: 'Attack 1 to hit bonus' }), '2');
  await user.type(screen.getByRole('textbox', { name: 'Attack 1 damage (optional)' }), '1d4');
  await user.click(screen.getByRole('button', { name: 'Save companion' }));

  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledOnce());
  const sent = vi.mocked(client.saveCharacter).mock.calls[0]![0] as Character;
  expect(sent.name).toBe('Testy Handler');
  expect(sent.companions).toHaveLength(2);
  const added = sent.companions![1]!;
  expect(added).toMatchObject({ name: 'Testy Mule', kind: 'Fixture mount', armorClass: 10, hitPointMaximum: 11 });
  expect(added.speed).toBeUndefined();
  expect(added.id).toMatch(/^[0-9a-f-]{36}$/);
  expect(added.attacks).toEqual([expect.objectContaining({ name: 'Testy Kick', toHit: 2, damage: '1d4' })]);
  expect(client.rollDice).toHaveBeenCalledWith('1d4', undefined, 'Dice'); // the dice engine owns the damage grammar
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit Testy Mule' })));
  expect(status).toEqual(['Added Testy Mule.']);
});

it('holds Save with a linked reason until the companion has a name, and explains a bad maximum', async () => {
  const user = userEvent.setup();
  show();
  await user.click(screen.getByRole('button', { name: 'Add a companion' }));
  const save = screen.getByRole('button', { name: 'Save companion' });
  expect(held(save)).toBe(true);
  expect(document.getElementById(save.getAttribute('aria-describedby')!)?.textContent).toBe('Save is held until the companion has a name.');
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Testy Mule');
  await user.clear(screen.getByRole('spinbutton', { name: 'Hit point maximum' }));
  await user.type(screen.getByRole('spinbutton', { name: 'Hit point maximum' }), '10001');
  const maximum = screen.getByRole('spinbutton', { name: 'Hit point maximum' });
  expect(maximum.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(maximum.getAttribute('aria-describedby')!.split(' ').pop()!)?.textContent).toBe('The hit point maximum must be a whole number from 0 to 10000.');
  await user.click(save);
  expect(client.saveCharacter).not.toHaveBeenCalled();
});

it('edits a companion and removes one; the definition goes through the character save and hit points are never sent with it', async () => {
  const user = userEvent.setup();
  show([hound, crow], { companions: [{ id: 'k1', current: 13, temporary: 4 }] });
  await user.click(screen.getByRole('button', { name: 'Edit Testy Hound' }));
  const armor = screen.getByRole('spinbutton', { name: 'Armor Class (optional)' });
  await user.clear(armor);
  await user.type(armor, '15');
  await user.click(screen.getByRole('button', { name: 'Save companion' }));
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledOnce());
  const edited = (vi.mocked(client.saveCharacter).mock.calls[0]![0] as Character).companions!;
  expect(edited[0]).toMatchObject({ id: 'k1', name: 'Testy Hound', armorClass: 15, hitPointMaximum: 20 });
  expect(edited[0]!.attacks).toHaveLength(2);
  expect(acted).toEqual([]);

  await user.click(await screen.findByRole('button', { name: 'Remove Testy Crow' }));
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(2));
  expect((vi.mocked(client.saveCharacter).mock.calls[1]![0] as Character).companions!.map((c) => c.id)).toEqual(['k1']);
  expect(status.at(-1)).toBe('Removed Testy Crow.');
});

it('surfaces the limit of ten companions in text and holds Add', async () => {
  const user = userEvent.setup();
  const many = Array.from({ length: 10 }, (_, i): Companion => ({ id: `k${i}`, name: `Testy Pet ${i}`, hitPointMaximum: 3 }));
  show(many);
  const add = screen.getByRole('button', { name: 'Add a companion' });
  expect(held(add)).toBe(true);
  expect(document.getElementById(add.getAttribute('aria-describedby')!)?.textContent).toBe('You can keep up to 10 companions.');
  await user.click(add);
  expect(screen.queryByRole('textbox', { name: 'Name' })).toBeNull();
});

it('holds Add an attack line at five lines, in text', async () => {
  const user = userEvent.setup();
  const armed: Companion = { ...hound, attacks: Array.from({ length: 5 }, (_, i) => ({ id: `a${i}`, name: `Testy Claw ${i}`, toHit: 1 })) };
  show([armed]);
  await user.click(screen.getByRole('button', { name: 'Edit Testy Hound' }));
  const add = screen.getByRole('button', { name: 'Add an attack line' });
  expect(held(add)).toBe(true);
  expect(document.getElementById(add.getAttribute('aria-describedby')!)?.textContent).toBe('A companion can have up to 5 attack lines.');
});
