// @vitest-environment jsdom
// Item 18: "Your own items" and "Your own attacks" on Inventory. Add, edit and remove; a held Save with a linked reason; the
// dice engine's own message for bad damage; focus never lost; typing kept while a save is in flight and when the stored value changes.
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect, useState } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { Character, CharacterView, CustomAttack, CustomItem } from '../api/types';
import { CustomAttacksPanel, CustomItemsPanel } from './CustomEntries';

vi.mock('../api/client', () => ({ client: { saveCharacter: vi.fn(), rollDice: vi.fn() } }));

const view = (extra: Partial<Character> = {}) => ({ character: { id: 'fixture', rulesFamily: 'srd-5.2.1', name: 'Testy Hero', ...extra } }) as unknown as CharacterView;

let latest: CharacterView;
let status: string[];
let errors: unknown[];

beforeEach(() => {
  status = [];
  errors = [];
  vi.mocked(client.saveCharacter).mockImplementation(async (c) => view(c as Partial<Character>));
  vi.mocked(client.rollDice).mockResolvedValue({} as never);
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

/** Holds the view like the sheet does, so a save re-renders the panel without a remount. */
function Harness({ initial, which }: { initial: CharacterView; which: 'items' | 'attacks' }) {
  const [current, setCurrent] = useState(initial);
  useEffect(() => {
    latest = current;
  });
  const props = { view: current, onChanged: setCurrent, onError: (e: unknown) => errors.push(e), onStatus: (t: string) => status.push(t) };
  return which === 'items' ? <CustomItemsPanel {...props} /> : <CustomAttacksPanel {...props} />;
}

const rope: CustomItem = { id: 'i1', name: 'Testy Rope', quantity: 2, note: 'Fixture note', equipped: false };
const lamp: CustomItem = { id: 'i2', name: 'Testy Lamp', quantity: 1, equipped: true };
const sling: CustomAttack = { id: 'a1', name: 'Testy Sling', toHit: 5, damage: '1d6+2', damageType: 'bludgeoning' };
const poke: CustomAttack = { id: 'a2', name: 'Testy Poke', toHit: 0 };

const items = (extra: Partial<Character> = {}) => render(<Harness initial={view(extra)} which="items" />);
const attacks = (extra: Partial<Character> = {}) => render(<Harness initial={view(extra)} which="attacks" />);
const held = (button: HTMLElement) => button.getAttribute('aria-disabled') === 'true';

it('says in one line that custom items never count toward a calculation', () => {
  items();
  expect(screen.getByText('Things you carry that are not in the rules content. They never count toward Armor Class or any calculation.')).toBeTruthy();
  expect(screen.getByText('No items of your own yet.')).toBeTruthy();
});

it('adds an item with a new id, keeps the other character fields, and moves focus to the new Edit button', async () => {
  const user = userEvent.setup();
  items({ customItems: [rope], xp: 300 });
  await user.click(screen.getByRole('button', { name: 'Add an item of your own' }));
  expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Name' }));
  await user.type(screen.getByRole('textbox', { name: 'Name' }), '  Testy Chalk ');
  await user.clear(screen.getByRole('spinbutton', { name: 'Quantity' }));
  await user.type(screen.getByRole('spinbutton', { name: 'Quantity' }), '3');
  await user.click(screen.getByRole('button', { name: 'Save item' }));

  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledOnce());
  const sent = vi.mocked(client.saveCharacter).mock.calls[0]![0] as Character;
  expect(sent.xp).toBe(300); // a save spreads the loaded character
  expect(sent.customItems).toHaveLength(2);
  const added = sent.customItems![1]!;
  expect(added).toMatchObject({ name: 'Testy Chalk', quantity: 3, equipped: false });
  expect(added.note).toBeUndefined();
  expect(added.id).toMatch(/^[0-9a-f-]{36}$/);
  expect(added.id).not.toBe('00000000-0000-0000-0000-000000000000');
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit Testy Chalk' })));
  expect(status).toEqual(['Added Testy Chalk.']);
});

it('holds Save with a linked reason until the name is filled, and refuses a press while held', async () => {
  const user = userEvent.setup();
  items();
  await user.click(screen.getByRole('button', { name: 'Add an item of your own' }));
  const save = screen.getByRole('button', { name: 'Save item' });
  expect(held(save)).toBe(true);
  expect(save.hasAttribute('disabled')).toBe(false);
  expect(document.getElementById(save.getAttribute('aria-describedby')!)?.textContent).toBe('Save is held until the item has a name.');
  await user.click(save);
  expect(client.saveCharacter).not.toHaveBeenCalled();
});

it('links an out-of-range quantity and an over-long note to their fields and holds Save', async () => {
  const user = userEvent.setup();
  items();
  await user.click(screen.getByRole('button', { name: 'Add an item of your own' }));
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Testy Chalk');
  const quantity = screen.getByRole('spinbutton', { name: 'Quantity' });
  await user.clear(quantity);
  await user.type(quantity, '10000');
  expect(quantity.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(quantity.getAttribute('aria-describedby')!)?.textContent).toBe('The quantity must be a whole number from 1 to 9999.');
  const note = screen.getByRole('textbox', { name: 'Note (optional)' });
  await user.click(note);
  await user.paste('x'.repeat(201));
  expect(note.getAttribute('aria-invalid')).toBe('true');
  expect(held(screen.getByRole('button', { name: 'Save item' }))).toBe(true);
});

it('edits an item (Save held until something changes), and focus returns to its Edit button', async () => {
  const user = userEvent.setup();
  items({ customItems: [rope, lamp] });
  await user.click(screen.getByRole('button', { name: 'Edit Testy Rope' }));
  expect(held(screen.getByRole('button', { name: 'Save item' }))).toBe(true);
  expect(screen.getByText('Nothing has changed yet.')).toBeTruthy();
  const name = screen.getByRole('textbox', { name: 'Name' });
  expect((name as HTMLInputElement).value).toBe('Testy Rope');
  await user.type(name, ' Long');
  await user.click(screen.getByRole('checkbox', { name: 'Equipped (a reminder only)' }));
  await user.click(screen.getByRole('button', { name: 'Save item' }));
  await waitFor(() => expect(latest.character.customItems![0]).toMatchObject({ id: 'i1', name: 'Testy Rope Long', equipped: true }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit Testy Rope Long' })));
  expect(latest.character.customItems![1]).toEqual(lamp);
});

it('cancel returns focus to the opener, and an unsaved draft is dropped', async () => {
  const user = userEvent.setup();
  items({ customItems: [rope] });
  await user.click(screen.getByRole('button', { name: 'Edit Testy Rope' }));
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'zzz');
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit Testy Rope' }));
  expect(client.saveCharacter).not.toHaveBeenCalled();
});

it('removes an item without asking, saves, and moves focus to the next item, then the previous one, then Add', async () => {
  const user = userEvent.setup();
  items({ customItems: [rope, lamp] });
  await user.click(screen.getByRole('button', { name: 'Remove Testy Rope' }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit Testy Lamp' })));
  expect(latest.character.customItems).toEqual([lamp]);
  expect(status).toEqual(['Removed Testy Rope.']);
  await user.click(screen.getByRole('button', { name: 'Remove Testy Lamp' }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Add an item of your own' })));
  expect(screen.queryByRole('dialog')).toBeNull();
});

it('keeps the text typed while a save is in flight and leaves the form open on the saved item', async () => {
  const user = userEvent.setup();
  let finish: (v: CharacterView) => void = () => {};
  vi.mocked(client.saveCharacter).mockImplementationOnce(
    (c) => new Promise<CharacterView>((resolve) => (finish = () => resolve(view(c as Partial<Character>)))),
  );
  items();
  await user.click(screen.getByRole('button', { name: 'Add an item of your own' }));
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Testy Chalk');
  await user.click(screen.getByRole('button', { name: 'Save item' }));
  await user.type(screen.getByRole('textbox', { name: 'Note (optional)' }), 'typed meanwhile');
  await act(async () => finish(view()));
  await waitFor(() => expect(latest.character.customItems).toHaveLength(1));
  expect((screen.getByRole('textbox', { name: 'Note (optional)' }) as HTMLInputElement).value).toBe('typed meanwhile');
  expect(screen.getByRole('form', { name: 'Edit Testy Chalk' })).toBeTruthy();
});

it('re-seeds an open edit from a changed stored item without remounting, keeping fields the user typed', async () => {
  const user = userEvent.setup();
  function Swap() {
    const [c, setC] = useState(view({ customItems: [rope] }));
    return (
      <>
        <button type="button" onClick={() => setC(view({ customItems: [{ ...rope, quantity: 7, note: 'From a restore' }] }))}>
          Restore
        </button>
        <CustomItemsPanel view={c} onChanged={setC} onError={() => {}} onStatus={() => {}} />
      </>
    );
  }
  render(<Swap />);
  await user.click(screen.getByRole('button', { name: 'Edit Testy Rope' }));
  const name = screen.getByRole('textbox', { name: 'Name' });
  await user.type(name, ' edited');
  await user.click(screen.getByRole('button', { name: 'Restore' }));
  expect(screen.getByRole('textbox', { name: 'Name' })).toBe(name); // same element: no remount
  expect((name as HTMLInputElement).value).toBe('Testy Rope edited');
  expect((screen.getByRole('spinbutton', { name: 'Quantity' }) as HTMLInputElement).value).toBe('7');
  expect((screen.getByRole('textbox', { name: 'Note (optional)' }) as HTMLInputElement).value).toBe('From a restore');
});

it('holds Add at the cap with its reason', () => {
  const many = Array.from({ length: 50 }, (_, i) => ({ id: `i${i}`, name: `Testy ${i}`, quantity: 1, equipped: false }));
  items({ customItems: many });
  const add = screen.getByRole('button', { name: 'Add an item of your own' });
  expect(held(add)).toBe(true);
  expect(document.getElementById(add.getAttribute('aria-describedby')!)?.textContent).toBe('You can keep up to 50 items of your own.');
});

it('adds an attack with its damage type and the dice engine accepting the damage', async () => {
  const user = userEvent.setup();
  attacks();
  await user.click(screen.getByRole('button', { name: 'Add an attack of your own' }));
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Testy Sling');
  await user.clear(screen.getByRole('spinbutton', { name: 'To hit bonus' }));
  await user.type(screen.getByRole('spinbutton', { name: 'To hit bonus' }), '5');
  await user.type(screen.getByRole('textbox', { name: 'Damage (optional)' }), '1d6+2');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Damage type (optional)' }), 'Bludgeoning');
  await user.click(screen.getByRole('button', { name: 'Save attack' }));
  await waitFor(() => expect(latest.character.customAttacks).toHaveLength(1));
  expect(client.rollDice).toHaveBeenCalledWith('1d6+2', undefined, 'Dice');
  expect(latest.character.customAttacks![0]).toMatchObject({ name: 'Testy Sling', toHit: 5, damage: '1d6+2', damageType: 'bludgeoning' });
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Edit Testy Sling' })));
  expect(screen.getByText(/Testy Sling: \+5 to hit, 1d6\+2 bludgeoning/)).toBeTruthy();
});

it("shows the dice engine's own message for bad damage, linked to the field, saves nothing and keeps focus there", async () => {
  const user = userEvent.setup();
  vi.mocked(client.rollDice).mockRejectedValue(new TomeStackError({ code: 'validation', message: 'Refused.', diagnostics: [{ code: 'dice.no-dice', message: "'5' has no dice to roll." }] }));
  attacks();
  await user.click(screen.getByRole('button', { name: 'Add an attack of your own' }));
  await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Testy Sling');
  const damage = screen.getByRole('textbox', { name: 'Damage (optional)' });
  await user.type(damage, '5');
  await user.click(screen.getByRole('button', { name: 'Save attack' }));
  await waitFor(() => expect(damage.getAttribute('aria-invalid')).toBe('true'));
  expect(document.getElementById(damage.getAttribute('aria-describedby')!.split(' ').at(-1)!)?.textContent).toBe("'5' has no dice to roll.");
  expect(client.saveCharacter).not.toHaveBeenCalled();
  expect(document.activeElement).toBe(damage);
  await user.type(damage, 'd4'); // typing clears the refusal
  expect(damage.getAttribute('aria-invalid')).toBeNull();
});

it('an attack with no damage skips the dice check; the to-hit bonus has its range linked', async () => {
  const user = userEvent.setup();
  attacks({ customAttacks: [poke] });
  await user.click(screen.getByRole('button', { name: 'Edit Testy Poke' }));
  const toHit = screen.getByRole('spinbutton', { name: 'To hit bonus' });
  await user.clear(toHit);
  await user.type(toHit, '31');
  expect(toHit.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(toHit.getAttribute('aria-describedby')!.split(' ').at(-1)!)?.textContent).toBe('The to-hit bonus must be a whole number from -20 to +30.');
  await user.clear(toHit);
  await user.type(toHit, '-2');
  await user.click(screen.getByRole('button', { name: 'Save attack' }));
  await waitFor(() => expect(latest.character.customAttacks![0]).toMatchObject({ id: 'a2', toHit: -2 }));
  expect(client.rollDice).not.toHaveBeenCalled();
});

it('removes an attack and lands focus on a control that exists', async () => {
  const user = userEvent.setup();
  attacks({ customAttacks: [sling] });
  await user.click(screen.getByRole('button', { name: 'Remove Testy Sling' }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Add an attack of your own' })));
  expect(latest.character.customAttacks ?? []).toEqual([]);
  expect(within(screen.getByRole('region', { name: 'Your own attacks' })).getByText('No attacks of your own yet.')).toBeTruthy();
});

// Fix round 1: Remove must never drop focus to <body>, whichever controls are on screen.
const panels = [
  { which: 'items', two: () => ({ customItems: [rope, lamp] }), key: 'customItems', editA: 'Edit Testy Rope', removeB: 'Remove Testy Lamp', removeA: 'Remove Testy Rope', add: 'Add an item of your own', heading: 'Your own items' },
  { which: 'attacks', two: () => ({ customAttacks: [sling, poke] }), key: 'customAttacks', editA: 'Edit Testy Sling', removeB: 'Remove Testy Poke', removeA: 'Remove Testy Sling', add: 'Add an attack of your own', heading: 'Your own attacks' },
] as const;

for (const p of panels) {
  it(`${p.which}: removing another row while one is being edited keeps focus in the open form`, async () => {
    const user = userEvent.setup();
    render(<Harness initial={view(p.two())} which={p.which} />);
    await user.click(screen.getByRole('button', { name: p.editA }));
    await user.click(screen.getByRole('button', { name: p.removeB }));
    await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledOnce());
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Name' })));
    expect(document.activeElement).not.toBe(document.body);
  });

  it(`${p.which}: removing the only row while the Add form is open keeps focus in the form`, async () => {
    const user = userEvent.setup();
    const one = p.which === 'items' ? { customItems: [rope] } : { customAttacks: [sling] };
    render(<Harness initial={view(one)} which={p.which} />);
    await user.click(screen.getByRole('button', { name: p.add }));
    await user.click(screen.getByRole('button', { name: p.which === 'items' ? 'Remove Testy Rope' : 'Remove Testy Sling' }));
    await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledOnce());
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Name' })));
    expect(document.activeElement).not.toBe(document.body);
  });

  it(`${p.which}: Remove is held with a linked reason while a save is in flight, and a press is refused`, async () => {
    const user = userEvent.setup();
    let finish: () => void = () => {};
    vi.mocked(client.saveCharacter).mockImplementationOnce(
      (c) => new Promise<CharacterView>((resolve) => (finish = () => resolve(view(c as Partial<Character>)))),
    );
    render(<Harness initial={view(p.two())} which={p.which} />);
    await user.click(screen.getByRole('button', { name: p.removeA }));
    const other = screen.getByRole('button', { name: p.removeB });
    expect(other.getAttribute('aria-disabled')).toBe('true');
    expect(other.hasAttribute('disabled')).toBe(false);
    expect(document.getElementById(other.getAttribute('aria-describedby')!)?.textContent).toBe('Wait for the current change to finish.');
    await user.click(other);
    expect(client.saveCharacter).toHaveBeenCalledOnce();
    await act(async () => finish());
    await waitFor(() => expect(latest.character[p.key]).toHaveLength(1));
    expect(screen.getByRole('button', { name: p.removeB }).getAttribute('aria-disabled')).toBeNull();
  });
}
