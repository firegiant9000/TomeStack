// @vitest-environment jsdom
// Item 18: the character's own hand-typed attacks are listed in "Attacks and actions" and rolled from there. There is no
// editing on Play (that is on Inventory).
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterView, CustomAttack, RollTarget } from '../api/types';
import { ActionsPanel, RollResult } from './PlayPanels';

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

// Wave 3 item 22 (R157): a toggle with an activation is listed under that group; one without stays in "Active effects".
const ref = { contentId: 'c1', revisionId: 'r1' };
const toggle = (id: string, extra: object = {}) => ({ content: ref, contentName: 'Testy Source', effectId: `e-${id}`, toggleId: id, label: `Testy ${id}`, on: false, ...extra });
const togglesView = (toggles: object[]) => ({ character: { id: 'fixture' }, sheet: { attacks: [], toggles, features: [] } }) as unknown as CharacterView;

it('lists a reaction toggle under Reactions and a bonus-action toggle under Bonus actions, and keeps an unassigned one in Active effects', () => {
  const v = togglesView([toggle('parry', { activation: 'reaction' }), toggle('dash', { activation: 'bonusAction' }), toggle('stance')]);
  render(<ActionsPanel view={v} roll={() => {}} act={() => {}} />);

  const reactions = screen.getByRole('region', { name: 'Reactions' });
  expect(within(reactions).getByRole('checkbox', { name: /Testy parry/ })).toBeTruthy();
  expect(within(reactions).getAllByRole('checkbox')).toHaveLength(1);
  const bonus = screen.getByRole('region', { name: 'Bonus actions' });
  expect(within(bonus).getByRole('checkbox', { name: /Testy dash/ })).toBeTruthy();
  const effects = screen.getByRole('group', { name: /Active effects/ });
  expect(within(effects).getAllByRole('checkbox')).toHaveLength(1);
  expect(within(effects).getByRole('checkbox', { name: /Testy stance/ })).toBeTruthy();
  expect(screen.queryByRole('region', { name: 'Actions' })).toBeNull(); // no empty group
});

it('drops the Active effects group when every toggle has an activation, and its legend does not claim a switch for switchless rows', () => {
  render(<ActionsPanel view={togglesView([toggle('parry', { activation: 'reaction' })])} roll={() => {}} act={() => {}} />);
  expect(screen.queryByRole('group', { name: /Active effects/ })).toBeNull();
  cleanup();
  render(<ActionsPanel view={togglesView([toggle('a', { auto: true, on: true }), toggle('b')])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('group', { name: /Active effects/ }).querySelector('legend')!.textContent).not.toMatch(/switching one/);
});

it('keeps an auto toggle with an activation switchless, "On while bloodied", inside its group', () => {
  render(<ActionsPanel view={togglesView([toggle('grit', { auto: true, on: true, activation: 'reaction' })])} roll={() => {}} act={() => {}} />);
  const reactions = screen.getByRole('region', { name: 'Reactions' });
  expect(within(reactions).queryByRole('checkbox')).toBeNull();
  expect(reactions.textContent).toMatch(/Testy grit/);
  expect(reactions.textContent).toMatch(/On while bloodied \(on now\)/);
});

it('switches a grouped toggle with the same command as an ungrouped one', async () => {
  const act = vi.fn();
  render(<ActionsPanel view={togglesView([toggle('parry', { activation: 'reaction' })])} roll={() => {}} act={act} />);
  await userEvent.click(within(screen.getByRole('region', { name: 'Reactions' })).getByRole('checkbox', { name: /Testy parry/ }));
  expect(act).toHaveBeenCalledWith({ action: 'toggleOn', contentId: 'c1', toggleId: 'parry' });
});

// D43 slice 3: an action that spends an allowance pool is listed under the pool's name, apart from Reactions.
it("groups actions spending an allowance under the pool's name, apart from Reactions", () => {
  const pool = {
    content: ref, contentName: 'Testy Source', effectId: 'e-pool', resourceId: 'react', label: 'Testy reaction pool', maximum: 2, spent: 0, current: 2,
    trace: [], warnings: [], automation: 'automatic', recoveries: [], allowance: true,
  };
  const other = { ...pool, effectId: 'e-other', resourceId: 'rage', label: 'Testy rage', allowance: false };
  const feature = {
    content: ref, name: 'Testy Feature', kind: 'feat',
    effects: [
      { id: 'riposte', type: 'roll', label: 'Riposte', dice: '1d6', activation: 'reaction', resourceId: 'react' },
      { id: 'slam', type: 'roll', label: 'Slam', dice: '1d8', activation: 'action', resourceId: 'rage' },
      { id: 'quip', type: 'roll', label: 'Quip', dice: '1d4', activation: 'reaction' },
    ],
  };
  const v = {
    character: { id: 'fixture' },
    sheet: { attacks: [], resources: [pool, other], features: [feature], toggles: [toggle('parry', { activation: 'reaction' }), toggle('dodge', { activation: 'reaction', resourceId: 'react' })] },
  } as unknown as CharacterView;
  render(<ActionsPanel view={v} roll={() => {}} act={() => {}} />);

  const grouped = screen.getByRole('region', { name: 'Testy reaction pool' });
  expect(within(grouped).getByRole('button', { name: /Roll Riposte/ })).toBeTruthy();
  expect(within(grouped).getByRole('checkbox', { name: /Testy dodge/ })).toBeTruthy();
  expect(grouped.textContent).toMatch(/1 of 2|2 of 2/); // the pool's state is shown beside its name, as text
  const reactions = screen.getByRole('region', { name: 'Reactions' });
  expect(within(reactions).getByRole('button', { name: /Roll Quip/ })).toBeTruthy();
  expect(within(reactions).getByRole('checkbox', { name: /Testy parry/ })).toBeTruthy();
  expect(within(reactions).queryByRole('button', { name: /Roll Riposte/ })).toBeNull();
  expect(within(reactions).queryByRole('checkbox', { name: /Testy dodge/ })).toBeNull();
  // A pool that is not an allowance does not move its action out of the activation group.
  expect(within(screen.getByRole('region', { name: 'Actions' })).getByRole('button', { name: /Roll Slam/ })).toBeTruthy();
});

it('shows Attacks and actions only when something other than the moved form picker is on it', () => {
  const forms = [toggle('tide', { group: 'forms', groupContent: 'g', groupLabel: 'Forms', layer: 'primary' })];
  render(<ActionsPanel view={togglesView(forms)} roll={() => {}} act={() => {}} />);
  expect(screen.queryByRole('region', { name: 'Attacks and actions' })).toBeNull(); // the form picker lives in the combat strip now
});

it('lists an explicit "other" toggle under Other like a roll, and shows the panel for grouped toggles alone', () => {
  render(<ActionsPanel view={togglesView([toggle('misc', { activation: 'other' })])} roll={() => {}} act={() => {}} />);
  expect(within(screen.getByRole('region', { name: 'Other' })).getByRole('checkbox', { name: /Testy misc/ })).toBeTruthy();
});

// D43 slice 4: attacks a feature gives are listed with the weapon rows and rolled by content and effect id.
const origin = { kind: 'content', ruleset: 'srd-5.2.1' };
const weaponRow = {
  item: { contentId: 'c-needle', revisionId: 'r-needle' }, name: 'Fixture Needle', effectId: 'needle', toHit: 5, damage: '1d4+3', damageType: 'piercing',
  automation: 'automatic', category: 'martial', attack: 'melee', properties: [], trace: [], warnings: [], source: 'weapon', replacesAttack: false, available: true,
};
const featureAttack = (extra: object = {}) => ({
  item: ref, name: 'Testy strike', effectId: 'strike', toHit: 6, damage: '1d8+4d8+5', damageType: 'bludgeoning', automation: 'automatic', attack: 'melee',
  ability: 'dex', properties: [], range: undefined, proficient: true, source: 'feature', replacesAttack: false, available: true, warnings: [], origin,
  trace: [
    { order: 1, operation: 'base', description: 'Dexterity modifier', amount: 3, result: 3, origin },
    { order: 2, operation: 'add', description: 'Fixture Blazing Mode rider: 4d8 added to the damage', result: 6, origin },
  ],
  riders: [{ content: ref, effectId: 'blazing-rider', dice: '4d8', toHit: 0, damageBonus: 0 }],
  ...extra,
});
const attacksView = (attacks: object[], features: object[] = []) =>
  ({ character: { id: 'fixture' }, sheet: { attacks, toggles: [], features } }) as unknown as CharacterView;

it('lists a feature attack with the weapon rows, rolls it by content and effect id, and says nothing of a weapon', async () => {
  const roll = vi.fn();
  render(<ActionsPanel view={attacksView([weaponRow, featureAttack()])} roll={roll} act={() => {}} rollMode="advantage" />);
  const actions = screen.getByRole('region', { name: 'Actions' });
  expect(within(actions).getByRole('button', { name: 'Roll Fixture Needle attack' })).toBeTruthy();

  await userEvent.click(within(actions).getByRole('button', { name: 'Roll Testy strike attack' }));
  expect(roll).toHaveBeenLastCalledWith({ content: ref, effectId: 'strike', mode: 'advantage' });
  await userEvent.click(within(actions).getByRole('button', { name: /^Roll Testy strike damage/ }));
  expect(roll).toHaveBeenLastCalledWith({ content: ref, effectId: 'strike', damage: true, critical: false });
  await userEvent.click(screen.getByRole('checkbox', { name: /^Critical hit/ }));
  await userEvent.click(within(actions).getByRole('button', { name: /^Roll Testy strike damage/ }));
  expect(roll).toHaveBeenLastCalledWith({ content: ref, effectId: 'strike', damage: true, critical: true });

  const item = within(actions).getByRole('button', { name: 'Roll Testy strike attack' }).closest('li')!;
  expect(item.textContent).toMatch(/Melee attack/);
  expect(item.textContent).not.toMatch(/weapon/i);
  expect(item.textContent).not.toMatch(/undefined|null/);
});

it('shows the riders in the attack trace', () => {
  render(<ActionsPanel view={attacksView([featureAttack()])} roll={() => {}} act={() => {}} />);
  const trace = screen.getByRole('list', { name: 'How the Testy strike attack is calculated' });
  expect(trace.textContent).toMatch(/Fixture Blazing Mode rider: 4d8 added to the damage/);
  expect(trace.textContent).not.toMatch(/\+0 = /); // a step with no amount does not claim one
});

it('says a replacing attack replaces one attack of the Attack action', () => {
  render(<ActionsPanel view={attacksView([featureAttack({ replacesAttack: true }), featureAttack({ name: 'Testy jab', effectId: 'jab' })])} roll={() => {}} act={() => {}} />);
  const strike = screen.getByRole('button', { name: 'Roll Testy strike attack' }).closest('li')!;
  const jab = screen.getByRole('button', { name: 'Roll Testy jab attack' }).closest('li')!;
  expect(strike.textContent).toMatch(/replaces one attack of the Attack action/);
  expect(jab.textContent).not.toMatch(/replaces one attack/);
});

it('lists a bonus-action feature attack under Bonus actions, and a ranged one as a ranged attack with its range', () => {
  const bolt = featureAttack({ name: 'Testy bolt', effectId: 'bolt', activation: 'bonusAction', attack: 'ranged', range: '60 ft', damageType: 'force' });
  render(<ActionsPanel view={attacksView([weaponRow, bolt])} roll={() => {}} act={() => {}} />);
  const bonus = screen.getByRole('region', { name: 'Bonus actions' });
  const item = within(bonus).getByRole('button', { name: 'Roll Testy bolt attack' }).closest('li')!;
  expect(item.textContent).toMatch(/Ranged attack/);
  expect(item.textContent).toMatch(/range 60 ft/);
  expect(within(bonus).queryByRole('button', { name: /Fixture Needle/ })).toBeNull();
  expect(within(screen.getByRole('region', { name: 'Actions' })).getByRole('button', { name: 'Roll Fixture Needle attack' })).toBeTruthy();
});

it('shows the panel for a character whose only action is a feature attack', () => {
  render(<ActionsPanel view={attacksView([featureAttack()])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('region', { name: 'Attacks and actions' })).toBeTruthy();
});

it('disables an unavailable feature attack and shows its reason as text the buttons are described by', () => {
  const locked = featureAttack({ available: false, unavailableReason: 'Available in Kindled mode', damage: '' });
  render(<ActionsPanel view={attacksView([locked])} roll={() => {}} act={() => {}} />);
  const attack = screen.getByRole('button', { name: 'Roll Testy strike attack' }) as HTMLButtonElement;
  expect(attack.disabled).toBe(true);
  expect((screen.getByRole('button', { name: /^Roll Testy strike damage/ }) as HTMLButtonElement).disabled).toBe(true);
  expect(attack.closest('li')!.textContent).toMatch(/Available in Kindled mode/);
  const id = attack.getAttribute('aria-describedby')!;
  expect(document.getElementById(id)!.textContent).toMatch(/Available in Kindled mode/);
});

it('an unavailable attack with no damage dice says no empty dice: no double space, no empty brackets', () => {
  const locked = featureAttack({ available: false, unavailableReason: 'Available in Kindled mode', damage: '' });
  render(<ActionsPanel view={attacksView([locked])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Testy strike damage' })).toBeTruthy(); // no "()" in the name
  const summary = screen.getByText('Testy strike').closest('summary')!;
  expect(summary.textContent).toBe('Testy strike: +6 to hit, bludgeoning');
  // An available attack still names its dice.
  cleanup();
  render(<ActionsPanel view={attacksView([featureAttack()])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Testy strike damage (1d8+4d8+5)' })).toBeTruthy();
  expect(screen.getByText('Testy strike').closest('summary')!.textContent).toBe('Testy strike: +6 to hit, 1d8+4d8+5 bludgeoning');
});

it("names the pool in a feature attack's cost line, and spends the attack's cost from its roll record", async () => {
  const pool = { content: ref, contentName: 'Testy Source', effectId: 'e-focus', resourceId: 'focus', label: 'Testy focus', maximum: 5, spent: 1, current: 4, trace: [], warnings: [], automation: 'automatic', recoveries: [] };
  const bolt = featureAttack({ name: 'Testy bolt', effectId: 'bolt', resourceId: 'focus', costResourceContent: 'c1', cost: 2 });
  const v = { character: { id: 'fixture' }, sheet: { attacks: [bolt], resources: [pool], toggles: [], features: [] } } as unknown as CharacterView;
  render(<ActionsPanel view={v} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Testy bolt attack' }).closest('li')!.textContent).toMatch(/uses 2 Testy focus/);
  cleanup();

  const act = vi.fn();
  const record = {
    formula: '1d20', mode: 'normal', critical: false, dice: [], diceTotal: 11, expressionConstant: 0, modifiers: [], total: 17,
    provenance: { rollId: 'attack.attack', label: 'Testy bolt attack', content: ref, effectId: 'bolt', linkedResourceId: 'focus', linkedResourceContent: 'c1' },
  } as unknown as Parameters<typeof RollResult>[0]['record'];
  render(<RollResult record={record} resources={[pool] as never} features={[]} attacks={[bolt] as never} act={act} />);
  await userEvent.click(screen.getByRole('button', { name: /^Spend 2 Testy focus/ }));
  expect(act).toHaveBeenCalledWith({ action: 'spend', amount: 2, contentId: 'c1', resourceId: 'focus' });
});

it("an action outside its form is disabled and says 'Available in Kindled mode'", async () => {
  const roll = vi.fn();
  const feature = {
    content: ref, name: 'Testy Kindled', kind: 'feature',
    effects: [
      { id: 'flash', type: 'roll', label: 'Flash step', dice: '1d6', activation: 'action', available: false, unavailableReason: 'Available in Kindled mode' },
      { id: 'sprint', type: 'roll', label: 'Sprint', dice: '1d4', activation: 'action', available: true },
    ],
  };
  render(<ActionsPanel view={attacksView([], [feature])} roll={roll} act={() => {}} />);
  const flash = screen.getByRole('button', { name: /Roll Flash step/ }) as HTMLButtonElement;
  expect(flash.disabled).toBe(true);
  expect(flash.closest('li')!.textContent).toMatch(/Available in Kindled mode/);
  expect(document.getElementById(flash.getAttribute('aria-describedby')!)!.textContent).toMatch(/Available in Kindled mode/);
  await userEvent.click(flash);
  expect(roll).not.toHaveBeenCalled();
  const sprint = screen.getByRole('button', { name: /Roll Sprint/ }) as HTMLButtonElement;
  expect(sprint.disabled).toBe(false);
  expect(sprint.getAttribute('aria-describedby')).toBeNull();
});
