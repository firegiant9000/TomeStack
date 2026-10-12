// @vitest-environment jsdom
// Inventory: only equipped items apply, so the panel says how many carried items are not equipped (an imported 2014
// sheet creates every item unequipped).
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CharacterView, EquipmentEntry } from '../api/types';
import { EquipmentPanel } from './EquipmentPanel';

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
