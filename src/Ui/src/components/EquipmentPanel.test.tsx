// @vitest-environment jsdom
// Inventory: only equipped items apply, so the panel says how many carried items are not equipped (an imported 2014
// sheet creates every item unequipped).
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CharacterView, EquipmentEntry } from '../api/types';
import { EquipmentPanel } from './EquipmentPanel';
import type { SheetSave } from './sheetSave';

vi.mock('../api/client', () => ({ client: { listContent: vi.fn(), saveCharacter: vi.fn() } }));

beforeEach(() => vi.mocked(client.listContent).mockResolvedValue([]));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

const entry = (id: string, equipped: boolean): EquipmentEntry => ({ item: { contentId: `c-${id}`, revisionId: `r-${id}` }, equipped, quantity: 1 });
// Only the fields EquipmentPanel reads; the rest of the view does not matter here.
const view = (equipment: EquipmentEntry[]) => ({ character: { id: 'fixture', rulesFamily: 'srd-5.2.1', equipment } }) as unknown as CharacterView;
const panel = (equipment: EquipmentEntry[]) => render(<EquipmentPanel view={view(equipment)} onChanged={() => {}} onError={() => {}} />);

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
  render(<EquipmentPanel view={view([entry('a', false)])} onChanged={() => {}} onError={() => {}} saveCharacter={q.saveCharacter} />);
  const box = () => screen.getByRole('checkbox', { name: /^Equip Item r-a/ });
  await user.click(box());
  await user.click(box()); // the view prop never changed, so the render still shows the first state
  await waitFor(() => expect(q.equipment()![0]!.equipped).toBe(false));
});

it('refuses a toggle on an item that was removed meanwhile, with its own message (G9)', async () => {
  const user = userEvent.setup();
  const q = queue([]);
  const errors: unknown[] = [];
  render(<EquipmentPanel view={view([entry('a', false)])} onChanged={() => {}} onError={(e) => errors.push(e)} saveCharacter={q.saveCharacter} />);
  await user.click(screen.getByRole('checkbox', { name: /^Equip Item r-a/ }));
  await waitFor(() => expect(errors).toHaveLength(1));
  expect((errors[0] as Error).message).toMatch(/removed/);
  expect(q.equipment()).toEqual([]);
});
