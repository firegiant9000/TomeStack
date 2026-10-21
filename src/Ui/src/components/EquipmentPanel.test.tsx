// @vitest-environment jsdom
// Inventory: only equipped items apply, so the panel says how many carried items are not equipped (an imported 2014
// sheet creates every item unequipped).
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CharacterView, EquipmentEntry, ItemUseValue, PlayAction } from '../api/types';
import { EquipmentPanel } from './EquipmentPanel';
import type { SheetSave } from './sheetSave';

vi.mock('../api/client', () => ({ client: { listContent: vi.fn(), saveCharacter: vi.fn(), play: vi.fn() } }));

beforeEach(() => vi.mocked(client.listContent).mockResolvedValue([]));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

const entry = (id: string, equipped: boolean): EquipmentEntry => ({ item: { contentId: `c-${id}`, revisionId: `r-${id}` }, equipped, quantity: 1 });
// Only the fields EquipmentPanel reads; the rest of the view does not matter here.
const view = (equipment: EquipmentEntry[], itemUses?: ItemUseValue[]) => ({ character: { id: 'fixture', rulesFamily: 'srd-5.2.1', equipment }, sheet: { itemUses } }) as unknown as CharacterView;
const panel = (equipment: EquipmentEntry[]) => render(<EquipmentPanel view={view(equipment)} onChanged={() => {}} onError={() => {}} onStatus={() => {}} />);

it('says one carried item is not equipped, and what equipping does', () => {
  panel([entry('a', true), entry('b', false)]);
  expect(screen.getByText(/^1 carried item is not equipped\. Equip a weapon to list its attack on Play, or armor to use it for Armor Class\.$/)).toBeTruthy();
});

it('counts several unequipped items in the plural', () => {
  panel([entry('a', false), entry('b', false), entry('c', true)]);
  expect(screen.getByText(/^2 carried items are not equipped\./)).toBeTruthy();
});

it('says nothing about equipping when every item is equipped or nothing is carried', () => {
  panel([entry('a', true)]);
  expect(screen.queryByText(/not equipped/)).toBeNull();
  cleanup();
  panel([]);
  expect(screen.queryByText(/not equipped/)).toBeNull();
});

// G10: a toggle is worked out from the latest saved list, so two quick presses toggle twice even if the panel has not re-rendered.
const queue = (start: EquipmentEntry[]) => {
  let stored = { id: 'fixture', rulesFamily: 'srd-5.2.1', equipment: start } as unknown as CharacterView['character'];
  const saveCharacter: SheetSave = async (update) => {
    stored = update(stored);
    return { character: stored, sheet: {} } as unknown as CharacterView;
  };
  return { saveCharacter, equipment: () => stored.equipment };
};

it('toggles from the latest saved list, so two presses before a re-render end where they began (G10)', async () => {
  const user = userEvent.setup();
  const q = queue([entry('a', false)]);
  render(<EquipmentPanel view={view([entry('a', false)])} onChanged={() => {}} onError={() => {}} onStatus={() => {}} saveCharacter={q.saveCharacter} />);
  const box = () => screen.getByRole('checkbox', { name: /^Equip Item r-a/ });
  await user.click(box());
  await user.click(box()); // the view prop never changed, so the render still shows the first state
  await waitFor(() => expect(q.equipment()![0]!.equipped).toBe(false));
});

it('refuses a toggle on an item that was removed meanwhile, with its own message (G9)', async () => {
  const user = userEvent.setup();
  const q = queue([]);
  const errors: unknown[] = [];
  const statuses: string[] = [];
  render(
    <EquipmentPanel view={view([entry('a', false)])} onChanged={() => {}} onError={(e) => errors.push(e)} onStatus={(t) => statuses.push(t)} saveCharacter={q.saveCharacter} />,
  );
  const box = screen.getByRole('checkbox', { name: /^Equip Item r-a/ });
  box.focus();
  await user.click(box);
  // H2/R144: a refusal is a polite status, not an error, and focus stays on the control.
  await waitFor(() => expect(statuses).toHaveLength(1));
  expect(statuses[0]).toMatch(/removed/);
  expect(errors).toHaveLength(0);
  expect(document.activeElement).toBe(box);
  expect(q.equipment()).toEqual([]);
});

// Wave 3 item 25 (R159, R171): charges and attunement per entry, on Inventory.
const wand = (id: string | undefined, equipped: boolean, extra: Partial<EquipmentEntry> = {}): EquipmentEntry => ({
  item: { contentId: 'c-wand', revisionId: 'r-wand' },
  equipped,
  quantity: 1,
  ...(id ? { id } : {}),
  ...extra,
});
const use = (index: number, entry: EquipmentEntry, over: Partial<ItemUseValue> = {}): ItemUseValue => ({
  item: entry.item,
  name: 'Fixture wand',
  entryKey: entry.id ?? `#${index}`,
  ...(entry.id ? { entryId: entry.id } : {}),
  entryIndex: index,
  equipped: entry.equipped,
  requiresAttunement: false,
  attuned: false,
  chargesMax: 7,
  chargesSpent: 0,
  chargesLeft: 7,
  ...over,
});
const withSpent = (spent: number): Partial<ItemUseValue> => ({ chargesSpent: spent, chargesLeft: 7 - spent });

it('shows charges as text and pips, with buttons named with the item, and spends through the play command', async () => {
  const user = userEvent.setup();
  const e = wand('11111111-1111-4111-8111-111111111111', true);
  const acts: PlayAction[] = [];
  render(<EquipmentPanel view={view([e], [use(0, e, withSpent(4))])} onChanged={() => {}} onError={() => {}} onStatus={() => {}} act={(a) => void acts.push(a)} />);

  expect(screen.getByText('3 of 7 charges left')).toBeTruthy();
  const spend = screen.getByRole('button', { name: 'Spend a charge: Item r-wand' });
  spend.focus();
  await user.click(spend);
  await user.click(screen.getByRole('button', { name: 'Regain a charge: Item r-wand' }));

  expect(acts).toEqual([
    { action: 'spendCharge', amount: 1, entryId: e.id },
    { action: 'regainCharge', amount: 1, entryId: e.id },
  ]);
});

it('refuses with aria-disabled and a polite status, and focus stays on the button', async () => {
  const user = userEvent.setup();
  const e = wand('11111111-1111-4111-8111-111111111111', false);
  const acts: PlayAction[] = [];
  const statuses: string[] = [];
  render(<EquipmentPanel view={view([e], [use(0, e, withSpent(7))])} onChanged={() => {}} onError={() => {}} onStatus={(t) => statuses.push(t)} act={(a) => void acts.push(a)} />);

  const spend = screen.getByRole('button', { name: 'Spend a charge: Item r-wand' });
  expect(spend.getAttribute('aria-disabled')).toBe('true');
  expect(spend.hasAttribute('disabled')).toBe(false);
  spend.focus();
  await user.click(spend);

  expect(acts).toEqual([]);
  expect(statuses).toEqual(['Item r-wand has no charges left.']);
  expect(document.activeElement).toBe(spend);
  expect(screen.getByText('0 of 7 charges left')).toBeTruthy();
});

it('lists two copies of one wand with separate counts, carried and equipped alike', () => {
  const a = wand('11111111-1111-4111-8111-111111111111', true);
  const b = wand('22222222-2222-4222-8222-222222222222', false);
  render(<EquipmentPanel view={view([a, b], [use(0, a, withSpent(3)), use(1, b, withSpent(1))])} onChanged={() => {}} onError={() => {}} onStatus={() => {}} />);

  expect(screen.getByText('4 of 7 charges left')).toBeTruthy();
  expect(screen.getByText('6 of 7 charges left')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Spend a charge: Item r-wand (copy 1 of 2)' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Spend a charge: Item r-wand (copy 2 of 2)' })).toBeTruthy();
});

it('names an entry without an id by its position and item (R158)', async () => {
  const user = userEvent.setup();
  const e = wand(undefined, true);
  const acts: PlayAction[] = [];
  render(<EquipmentPanel view={view([entry('a', true), e], [use(1, e)])} onChanged={() => {}} onError={() => {}} onStatus={() => {}} act={(a) => void acts.push(a)} />);

  await user.click(screen.getByRole('button', { name: 'Spend a charge: Item r-wand' }));

  expect(acts).toEqual([{ action: 'spendCharge', amount: 1, entryIndex: 1, contentId: 'c-wand' }]);
});

it('sends the play command itself when no sheet is around the panel', async () => {
  const user = userEvent.setup();
  const e = wand('11111111-1111-4111-8111-111111111111', true);
  const changed: unknown[] = [];
  const next = { character: { id: 'fixture', equipment: [] } } as unknown as CharacterView;
  vi.mocked(client.play).mockResolvedValue(next);
  render(<EquipmentPanel view={view([e], [use(0, e)])} onChanged={(v) => changed.push(v)} onError={() => {}} onStatus={() => {}} />);

  await user.click(screen.getByRole('button', { name: 'Spend a charge: Item r-wand' }));

  await waitFor(() => expect(changed).toEqual([next]));
  expect(vi.mocked(client.play)).toHaveBeenCalledWith('fixture', { action: 'spendCharge', amount: 1, entryId: e.id });
});

it('offers attunement only on equipped entries of attunement items, says when one is not attuned, and saves it on that entry only', async () => {
  const user = userEvent.setup();
  const a = wand('11111111-1111-4111-8111-111111111111', true);
  const b = wand('22222222-2222-4222-8222-222222222222', true);
  const c = wand('33333333-3333-4333-8333-333333333333', false);
  const q = queue([a, b, c]);
  const needs = { requiresAttunement: true, chargesMax: undefined, chargesLeft: undefined };
  render(
    <EquipmentPanel
      view={view([a, b, c], [use(0, a, needs), use(1, b, needs), use(2, c, needs)])}
      onChanged={() => {}}
      onError={() => {}}
      onStatus={() => {}}
      saveCharacter={q.saveCharacter}
    />,
  );

  expect(screen.getAllByRole('checkbox', { name: /^Attuned:/ })).toHaveLength(2); // the carried copy has none
  expect(screen.getAllByText(/is not attuned, so its effects do not apply\./)).toHaveLength(2);
  await user.click(screen.getByRole('checkbox', { name: 'Attuned: Item r-wand (copy 2 of 3)' }));

  await waitFor(() => expect(q.equipment()!.map((e) => e.attuned)).toEqual([undefined, true, undefined]));
});

it('warns in text when more than three items are attuned, and never refuses', () => {
  const entries = ['1', '2', '3', '4'].map((n) => wand(`${n.repeat(8)}-1111-4111-8111-111111111111`, true, { attuned: true }));
  const uses = entries.map((e, i) => use(i, e, { requiresAttunement: true, attuned: true, chargesMax: undefined, chargesLeft: undefined }));
  const { rerender } = render(<EquipmentPanel view={view(entries, uses)} onChanged={() => {}} onError={() => {}} onStatus={() => {}} />);
  expect(screen.getByText(/^4 items are attuned\. The rules allow attuning to 3 at a time; TomeStack does not stop you\.$/)).toBeTruthy();

  rerender(<EquipmentPanel view={view(entries.slice(0, 3), uses.slice(0, 3))} onChanged={() => {}} onError={() => {}} onStatus={() => {}} />);
  expect(screen.queryByText(/items are attuned/)).toBeNull();
});

it('offers a charged or attunement item again to carry a second copy, and a plain one only once', async () => {
  const e = wand('11111111-1111-4111-8111-111111111111', false);
  const plain = entry('p', false);
  vi.mocked(client.listContent).mockResolvedValue([
    { kind: 'item', name: 'Fixture wand', reference: e.item, compatible: true, superseded: false, sourceTitle: 'Fixture', page: '' },
    { kind: 'item', name: 'Fixture cup', reference: plain.item, compatible: true, superseded: false, sourceTitle: 'Fixture', page: '' },
  ] as never);
  render(<EquipmentPanel view={view([e, plain], [use(0, e)])} onChanged={() => {}} onError={() => {}} onStatus={() => {}} />);

  await waitFor(() => expect(screen.getByRole('option', { name: /Fixture wand/ })).toBeTruthy());
  expect(screen.queryByRole('option', { name: /Fixture cup/ })).toBeNull();
});
