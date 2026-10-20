// @vitest-environment jsdom
// Item 18: the character's own hand-typed attacks are listed in "Attacks and actions" and rolled from there. There is no
// editing on Play (that is on Inventory).
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterView, CustomAttack, RollTarget } from '../api/types';
import { ActionsPanel } from './PlayPanels';

afterEach(cleanup);

const sling: CustomAttack = { id: 'a1', name: 'Testy Sling', toHit: 5, damage: '1d6+2', damageType: 'bludgeoning', note: 'A fixture note' };
const poke: CustomAttack = { id: 'a2', name: 'Testy Poke', toHit: -1 };

// Only what ActionsPanel reads: no weapons, features or toggles, so the panel exists only because of the custom attacks.
const view = (customAttacks?: CustomAttack[]) =>
  ({ character: { id: 'fixture', customAttacks }, sheet: { attacks: [], toggles: [], features: [] } }) as unknown as CharacterView;

const render_ = (customAttacks: CustomAttack[] | undefined, roll: (t: RollTarget) => void = () => {}, mode: 'normal' | 'advantage' = 'normal') =>
  render(<ActionsPanel view={view(customAttacks)} roll={roll} act={() => {}} rollMode={mode} />);

it('shows the panel for a character with only custom attacks, and nothing for one with none', () => {
  render_([sling]);
  const panel = screen.getByRole('region', { name: 'Attacks and actions' });
  expect(within(panel).getByRole('listitem').textContent).toMatch(/Testy Sling: \+5 to hit, 1d6\+2 bludgeoning · A fixture note/);
  cleanup();
  render_(undefined);
  expect(screen.queryByRole('region', { name: 'Attacks and actions' })).toBeNull();
});

it('rolls the attack under the chosen mode and the damage with the critical box, by the attack id', async () => {
  const roll = vi.fn();
  render_([sling], roll, 'advantage');
  await userEvent.click(screen.getByRole('button', { name: 'Roll Testy Sling attack' }));
  expect(roll).toHaveBeenLastCalledWith({ customAttack: 'a1', mode: 'advantage' });

  await userEvent.click(screen.getByRole('button', { name: 'Roll Testy Sling damage' }));
  expect(roll).toHaveBeenLastCalledWith({ customAttack: 'a1', damage: true, critical: false });

  await userEvent.click(screen.getByRole('checkbox', { name: /^Critical hit/ }));
  await userEvent.click(screen.getByRole('button', { name: 'Roll Testy Sling damage' }));
  expect(roll).toHaveBeenLastCalledWith({ customAttack: 'a1', damage: true, critical: true });
});

it('sends the chosen mode with a weapon attack roll and none with its damage (R93)', async () => {
  const roll = vi.fn();
  const item = { contentId: 'c-needle', revisionId: 'r-needle' };
  const weapon = { item, name: 'Fixture Needle', toHit: 5, damage: '1d4+3', damageType: 'piercing', automation: 'automatic', category: 'martial', attack: 'melee', properties: [], trace: [], warnings: [] };
  const v = { character: { id: 'fixture' }, sheet: { attacks: [weapon], toggles: [], features: [] } } as unknown as CharacterView;
  render(<ActionsPanel view={v} roll={roll} act={() => {}} rollMode="disadvantage" />);
  await userEvent.click(screen.getByRole('button', { name: 'Roll Fixture Needle attack' }));
  expect(roll).toHaveBeenLastCalledWith({ weapon: item, mode: 'disadvantage' });
  await userEvent.click(screen.getByRole('button', { name: /^Roll Fixture Needle damage/ }));
  expect(roll).toHaveBeenLastCalledWith({ weapon: item, damage: true, critical: false });
});

it('offers a damage button only for an attack that has damage, and each Roll name matches one button', () => {
  render_([sling, poke]);
  expect(screen.queryByRole('button', { name: 'Roll Testy Poke damage' })).toBeNull();
  expect(screen.getAllByRole('button', { name: /^Roll Testy Poke attack$/ })).toHaveLength(1);
  expect(screen.getAllByRole('button', { name: /^Roll Testy Sling (attack|damage)$/ })).toHaveLength(2);
  expect(screen.queryByRole('button', { name: /^Edit|^Remove/ })).toBeNull(); // no editing on Play
});

it('shows an auto toggle as text, "On while bloodied", with no switch, and keeps the switch for a manual one (D38)', () => {
  const ref = { contentId: 'c1', revisionId: 'r1' };
  const toggles = [
    { content: ref, contentName: 'Testy Grit', effectId: 'e1', toggleId: 'grit', label: 'Testy grit', on: true, auto: true },
    { content: ref, contentName: 'Testy Grit', effectId: 'e2', toggleId: 'stance', label: 'Testy stance', on: false },
  ];
  const v = { character: { id: 'fixture' }, sheet: { attacks: [], toggles, features: [] } } as unknown as CharacterView;
  render(<ActionsPanel view={v} roll={() => {}} act={() => {}} />);

  const effects = screen.getByRole('group', { name: /Active effects/ });
  expect(within(effects).getAllByRole('checkbox')).toHaveLength(1); // only the manual toggle has a switch
  expect(within(effects).getByRole('checkbox', { name: /Testy stance/ })).toBeTruthy();
  expect(within(effects).queryByRole('checkbox', { name: /Testy grit/ })).toBeNull();
  expect(effects.textContent).toMatch(/Testy grit/);
  expect(effects.textContent).toMatch(/On while bloodied \(on now\)/);
});
