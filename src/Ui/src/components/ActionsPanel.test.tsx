// @vitest-environment jsdom
// Item 18: the character's own hand-typed attacks are listed in "Attacks and actions" and rolled from there. There is no
// editing on Play (that is on Inventory).
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterView, CustomAttack, RollTarget } from '../api/types';
import { attackPin, customPin, rollPin, togglePin, type Pin } from '../quickbar';
import { ActionsPanel, ResourcesPanel, RollResult } from './PlayPanels';

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
  expect(trace.textContent).toMatch(/Starts at 3: Dexterity modifier/); // D72: the shared sentence renderer
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

// D43 S5 Task 5.5a: action details, per-unit amounts, the record's Spend and dice-less "Use".
const focusPool = (current = 4) => ({
  content: ref, contentName: 'Testy Source', effectId: 'e-focus', resourceId: 'focus', label: 'Testy focus', maximum: 5, spent: 5 - current, current,
  trace: [], warnings: [], automation: 'automatic', recoveries: [],
});
const actionsView = (effects: object[], pool: object = focusPool()) =>
  ({ character: { id: 'fixture' }, sheet: { attacks: [], toggles: [], resources: [pool], features: [{ content: ref, name: 'Testy Source', kind: 'feature', effects }] } }) as unknown as CharacterView;
const surge = { id: 'surge', type: 'roll', label: 'Surge', dice: '2d6', activation: 'action', resourceId: 'focus', cost: 2, available: true, perUnit: { dice: '1d6', maxUnits: 2 } };

it('an action shows its save DC, area, range and targets as text', () => {
  const beam = {
    id: 'beam', type: 'roll', label: 'Beam', dice: '4d6', activation: 'action', available: true,
    saveAbility: 'dex', saveDc: 13, area: { shape: 'line', size: 100, width: 5 }, range: 'Self', targets: 'Each creature in the line',
  };
  render(<ActionsPanel view={actionsView([beam])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Beam (4d6)' }).closest('li')!.textContent).toContain(
    'Save: DC 13 Dexterity · 100-ft line, 5 ft wide · Range: Self · Each creature in the line',
  );
  cleanup();
  const ball = { id: 'ball', type: 'roll', label: 'Ball', dice: '2d6', available: true, saveAbility: 'wis', area: { shape: 'sphere', size: 20 }, targets: 'Each creature' };
  render(<ActionsPanel view={actionsView([ball])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Ball (2d6)' }).closest('li')!.textContent).toContain(
    'Save: Wisdom (DC unavailable) · 20-ft sphere · Each creature',
  );
  cleanup();
  render(<ActionsPanel view={actionsView([{ id: 'plain', type: 'roll', label: 'Plain', dice: '1d4', available: true }])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Plain (1d4)' }).closest('li')!.textContent).not.toMatch(/Save|Range|undefined|\(\)/);
});

it('an action with a save trace offers "Why DC 13?", closed, ending with the DC; none without a trace (D72 X04)', () => {
  const rules = { kind: 'rulesPolicy' };
  const beam = {
    id: 'beam', type: 'roll', label: 'Beam', dice: '4d6', activation: 'action', available: true, saveAbility: 'dex', saveDc: 13,
    saveTrace: [
      { order: 1, operation: 'base', description: 'Base DC', amount: 8, result: 8, origin: rules },
      { order: 2, operation: 'add', description: 'Proficiency bonus', amount: 3, result: 11, origin: rules },
      { order: 3, operation: 'derive', description: 'Save DC', result: 13, origin: rules },
    ],
  };
  render(<ActionsPanel view={actionsView([beam])} roll={() => {}} act={() => {}} />);
  const why = screen.getByText('Why DC 13?').closest('details')!;
  expect(why.open).toBe(false);
  const items = within(why).getAllByRole('listitem', { hidden: true });
  expect(items[items.length - 1]!.textContent).toBe('Save DC: 13');
  cleanup();
  render(<ActionsPanel view={actionsView([{ ...beam, saveTrace: [] }])} roll={() => {}} act={() => {}} />);
  expect(screen.queryByText(/^Why DC/)).toBeNull();
});

it("the per-unit amount box rolls with that amount and the record's Spend spends exactly it", async () => {
  const roll = vi.fn();
  render(<ActionsPanel view={actionsView([surge])} roll={roll} act={() => {}} />);
  const box = screen.getByRole('spinbutton', { name: /Points to spend/ }) as HTMLInputElement;
  expect(box.min).toBe('2');
  expect(box.max).toBe('4');
  expect(box.value).toBe('2');
  await userEvent.click(screen.getByRole('button', { name: 'Roll Surge (2d6)' }));
  expect(roll).toHaveBeenLastCalledWith({ content: ref, effectId: 'surge', critical: false, amount: 2 });
  await userEvent.clear(box);
  await userEvent.type(box, '4');
  await userEvent.click(screen.getByRole('button', { name: 'Roll Surge (2d6)' }));
  expect(roll).toHaveBeenLastCalledWith({ content: ref, effectId: 'surge', critical: false, amount: 4 });
  cleanup();

  const act = vi.fn();
  const record = {
    formula: '2d6+1d6+1d6', mode: 'normal', critical: false, dice: [], diceTotal: 11, expressionConstant: 0, modifiers: [], total: 11,
    provenance: { rollId: 'surge', label: 'Surge', content: ref, effectId: 'surge', linkedResourceId: 'focus', linkedResourceContent: 'c1' },
    spend: { contentId: 'c1', resourceId: 'focus', amount: 4 },
  } as unknown as Parameters<typeof RollResult>[0]['record'];
  render(<RollResult record={record} resources={[focusPool(4)] as never} features={[{ content: ref, name: 'Testy Source', kind: 'feature', effects: [surge] }] as never} act={act} />);
  expect(screen.queryByRole('spinbutton')).toBeNull();
  await userEvent.click(screen.getByRole('button', { name: 'Spend 4 Testy focus (4 left)' }));
  expect(act).toHaveBeenCalledWith({ action: 'spend', amount: 4, contentId: 'c1', resourceId: 'focus' });
  cleanup();
  render(<RollResult record={record} resources={[focusPool(3)] as never} features={[]} act={act} />);
  expect((screen.getByRole('button', { name: 'Spend 4 Testy focus (3 left)' }) as HTMLButtonElement).disabled).toBe(true);
});

it('without maxUnits the per-unit box tops out at the pool maximum', () => {
  const open = { ...surge, perUnit: { dice: '1d6' } };
  render(<ActionsPanel view={actionsView([open])} roll={() => {}} act={() => {}} />);
  expect((screen.getByRole('spinbutton', { name: /Points to spend/ }) as HTMLInputElement).max).toBe('5');
});

it('an empty, fractional or out-of-range amount disables Roll and says why', async () => {
  const roll = vi.fn();
  render(<ActionsPanel view={actionsView([surge])} roll={roll} act={() => {}} />);
  const box = screen.getByRole('spinbutton', { name: /Points to spend/ });
  const button = () => screen.getByRole('button', { name: 'Roll Surge (2d6)' }) as HTMLButtonElement;
  for (const bad of ['', '1', '5', '2.5']) {
    await userEvent.clear(box);
    if (bad) await userEvent.type(box, bad);
    expect(button().disabled).toBe(true);
    expect(document.getElementById(button().getAttribute('aria-describedby')!)!.textContent).toMatch(/whole number from 2 to 4/);
  }
  await userEvent.click(button());
  expect(roll).not.toHaveBeenCalled();
  await userEvent.clear(box);
  await userEvent.type(box, '3');
  expect(button().disabled).toBe(false);
  expect(button().getAttribute('aria-describedby')).toBeNull();
});

it('a variable-cost record with a Spend keeps the chosen-amount box, capped by the amount and the pool', async () => {
  const act = vi.fn();
  const flare = { id: 'flare', type: 'roll', label: 'Flare', dice: '1d6', resourceId: 'focus', cost: 3, variableCost: true, available: true };
  const record = {
    formula: '1d6', mode: 'normal', critical: false, dice: [], diceTotal: 3, expressionConstant: 0, modifiers: [], total: 3,
    provenance: { rollId: 'flare', label: 'Flare', content: ref, effectId: 'flare', linkedResourceId: 'focus', linkedResourceContent: 'c1' },
    spend: { contentId: 'c1', resourceId: 'focus', amount: 3 },
  } as unknown as Parameters<typeof RollResult>[0]['record'];
  render(<RollResult record={record} resources={[focusPool(2)] as never} features={[{ content: ref, name: 'Testy Source', kind: 'feature', effects: [flare] }] as never} act={act} />);
  const box = screen.getByRole('spinbutton', { name: 'Testy focus to spend (1 to 2)' });
  await userEvent.type(box, '2');
  await userEvent.click(screen.getByRole('button', { name: /^Spend 2 Testy focus/ }));
  expect(act).toHaveBeenCalledWith({ action: 'spend', amount: 2, contentId: 'c1', resourceId: 'focus' });
});

it('a dice-less action is a Use button that spends its cost', async () => {
  const roll = vi.fn();
  const act = vi.fn();
  const rally = { id: 'rally', type: 'roll', label: 'Rally', activation: 'bonusAction', resourceId: 'focus', cost: 2, available: true };
  render(<ActionsPanel view={actionsView([rally])} roll={roll} act={act} />);
  expect(screen.queryByRole('button', { name: /^Roll Rally/ })).toBeNull();
  await userEvent.click(screen.getByRole('button', { name: 'Use Rally (2 Testy focus)' }));
  expect(act).toHaveBeenCalledWith({ action: 'spend', amount: 2, contentId: 'c1', resourceId: 'focus' });
  expect(roll).not.toHaveBeenCalled();
  cleanup();

  // The pool cannot pay: disabled with a reason.
  render(<ActionsPanel view={actionsView([rally], focusPool(1))} roll={roll} act={act} />);
  const use = screen.getByRole('button', { name: 'Use Rally (2 Testy focus)' }) as HTMLButtonElement;
  expect(use.disabled).toBe(true);
  expect(document.getElementById(use.getAttribute('aria-describedby')!)!.textContent).toMatch(/Testy focus/);
  cleanup();

  // Outside its form: disabled with the form's reason.
  const locked = { ...rally, available: false, unavailableReason: 'Available in Kindled mode' };
  render(<ActionsPanel view={actionsView([locked])} roll={roll} act={act} />);
  const form = screen.getByRole('button', { name: 'Use Rally (2 Testy focus)' }) as HTMLButtonElement;
  expect(form.disabled).toBe(true);
  expect(document.getElementById(form.getAttribute('aria-describedby')!)!.textContent).toMatch(/Available in Kindled mode/);
  cleanup();

  // No pool: its text only, no button.
  const note = { id: 'note', type: 'roll', label: 'Steady', text: 'You stand firm.', activation: 'action', available: true };
  render(<ActionsPanel view={actionsView([note])} roll={roll} act={act} />);
  expect(screen.queryByRole('button')).toBeNull();
  expect(screen.getByText(/You stand firm\./)).toBeTruthy();
});

it('a dice-less action whose cost formula failed is disabled as "Cost unavailable", never offered as a one-use spend', () => {
  const act = vi.fn();
  // The sheet leaves `cost` absent and adds an effect.invalid-formula diagnostic for that effect.
  const rally = { id: 'rally', type: 'roll', label: 'Rally', activation: 'bonusAction', resourceId: 'focus', automation: 'assisted', available: true };
  const base = actionsView([rally]);
  const failed = {
    ...base,
    sheet: { ...base.sheet, features: [{ ...base.sheet.features![0]!, diagnostics: [{ code: 'effect.invalid-formula', message: 'Bad cost', effectId: 'rally' }] }] },
  } as unknown as CharacterView;
  render(<ActionsPanel view={failed} roll={() => {}} act={act} />);
  const use = screen.getByRole('button', { name: /^Use Rally/ }) as HTMLButtonElement;
  expect(use.disabled).toBe(true);
  expect(use.textContent).not.toMatch(/\(1 /);
  expect(document.getElementById(use.getAttribute('aria-describedby')!)!.textContent).toMatch(/Cost unavailable/);
});

it('a dice-less action that costs nothing here says so instead of "not enough"', () => {
  const free = { id: 'rally', type: 'roll', label: 'Rally', activation: 'bonusAction', resourceId: 'focus', cost: 0, available: true };
  render(<ActionsPanel view={actionsView([free])} roll={() => {}} act={() => {}} />);
  const use = screen.getByRole('button', { name: /^Use Rally/ }) as HTMLButtonElement;
  expect(use.disabled).toBe(true);
  const reason = document.getElementById(use.getAttribute('aria-describedby')!)!.textContent!;
  expect(reason).toMatch(/Costs nothing here/);
  expect(reason).not.toMatch(/Not enough/);
});

it('the points to spend start over when the base cost changes', () => {
  const view = (cost: number) => actionsView([{ ...surge, cost }]);
  const { rerender } = render(<ActionsPanel view={view(2)} roll={() => {}} act={() => {}} />);
  expect((screen.getByRole('spinbutton', { name: /Points to spend/ }) as HTMLInputElement).value).toBe('2');
  rerender(<ActionsPanel view={view(3)} roll={() => {}} act={() => {}} />);
  expect((screen.getByRole('spinbutton', { name: /Points to spend/ }) as HTMLInputElement).value).toBe('3');
});

// D43 S5 slice review fixes.
type Rec = Parameters<typeof RollResult>[0]['record'];
const recordFor = (extra: object = {}) =>
  ({
    formula: '1d6', mode: 'normal', critical: false, dice: [], diceTotal: 3, expressionConstant: 0, modifiers: [], total: 3,
    provenance: { rollId: 'flare', label: 'Flare', content: ref, effectId: 'flare', linkedResourceId: 'focus', linkedResourceContent: 'c1' },
    ...extra,
  }) as unknown as Rec;
const failedCost = [{ code: 'effect.invalid-formula', message: 'Bad cost', effectId: 'flare' }];

it("a record with no Spend (the cost formula failed) offers no Spend button and says 'Cost unavailable', never a guessed 1", () => {
  const flare = { id: 'flare', type: 'roll', label: 'Flare', dice: '1d6', resourceId: 'focus', available: true };
  const feature = (effect: object) => [{ content: ref, name: 'Testy Source', kind: 'feature', effects: [effect], diagnostics: failedCost }] as never;
  render(<RollResult record={recordFor()} resources={[focusPool(4)] as never} features={feature(flare)} act={() => {}} />);
  expect(screen.queryByRole('button', { name: /^Spend/ })).toBeNull();
  expect(screen.getByText('Cost unavailable')).toBeTruthy();
  cleanup();

  render(<RollResult record={recordFor()} resources={[focusPool(4)] as never} features={feature({ ...flare, variableCost: true })} act={() => {}} />);
  expect(screen.queryByRole('spinbutton')).toBeNull();
  expect(screen.queryByRole('button', { name: /^Spend/ })).toBeNull();
  expect(screen.getByText('Cost unavailable')).toBeTruthy();
});

it('an untracked pool shows no Points to spend box and rolls without an amount', async () => {
  const roll = vi.fn();
  render(<ActionsPanel view={actionsView([surge], { ...focusPool(), current: undefined })} roll={roll} act={() => {}} />);
  expect(screen.queryByRole('spinbutton', { name: /Points to spend/ })).toBeNull();
  await userEvent.click(screen.getByRole('button', { name: 'Roll Surge (2d6)' }));
  expect(roll).toHaveBeenLastCalledWith({ content: ref, effectId: 'surge', critical: false });
});

it("the record's Spend can be used once per record, and a new record enables it again", async () => {
  const act = vi.fn();
  const first = recordFor({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2 } });
  const flare = { id: 'flare', type: 'roll', label: 'Flare', dice: '1d6', resourceId: 'focus', cost: 2, available: true };
  const features = [{ content: ref, name: 'Testy Source', kind: 'feature', effects: [flare] }] as never;
  const { rerender } = render(<RollResult record={first} resources={[focusPool(4)] as never} features={features} act={act} />);
  const spend = () => screen.getByRole('button', { name: /^Spend 2 Testy focus/ }) as HTMLButtonElement;
  await userEvent.click(spend());
  expect(spend().disabled).toBe(true);
  await userEvent.click(spend());
  expect(act).toHaveBeenCalledTimes(1);
  rerender(<RollResult record={recordFor({ spend: { contentId: 'c1', resourceId: 'focus', amount: 2 } })} resources={[focusPool(4)] as never} features={features} act={act} />);
  expect(spend().disabled).toBe(false);
});

it('a chosen-amount Spend is also once per record', async () => {
  const act = vi.fn();
  const flare = { id: 'flare', type: 'roll', label: 'Flare', dice: '1d6', resourceId: 'focus', cost: 3, variableCost: true, available: true };
  render(
    <RollResult
      record={recordFor({ spend: { contentId: 'c1', resourceId: 'focus', amount: 3 } })}
      resources={[focusPool(4)] as never}
      features={[{ content: ref, name: 'Testy Source', kind: 'feature', effects: [flare] }] as never}
      act={act}
    />,
  );
  await userEvent.type(screen.getByRole('spinbutton'), '2');
  await userEvent.click(screen.getByRole('button', { name: /^Spend 2 Testy focus/ }));
  expect(act).toHaveBeenCalledTimes(1);
  await userEvent.type(screen.getByRole('spinbutton'), '2');
  expect((screen.getByRole('button', { name: /^Spend 2 Testy focus/ }) as HTMLButtonElement).disabled).toBe(true);
});

it('the Points to spend box is aria-invalid and described by its reason only while its value is invalid', async () => {
  render(<ActionsPanel view={actionsView([surge])} roll={() => {}} act={() => {}} />);
  const box = screen.getByRole('spinbutton', { name: /Points to spend/ });
  expect(box.getAttribute('aria-invalid')).toBeNull();
  expect(box.getAttribute('aria-describedby')).toBeNull();
  await userEvent.clear(box);
  await userEvent.type(box, '5');
  expect(box.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(box.getAttribute('aria-describedby')!)!.textContent).toMatch(/whole number from 2 to 4/);
  await userEvent.clear(box);
  await userEvent.type(box, '3');
  expect(box.getAttribute('aria-invalid')).toBeNull();
  expect(box.getAttribute('aria-describedby')).toBeNull();
});

// D43 S8: charging. Gather spends focus and builds charge; Release draws the built-up charge down.
const chargePool = (spent: number, maximum = 4) => ({
  content: ref, contentName: 'Testy Source', effectId: 'e-charge', resourceId: 'charge', label: 'Testy charge', maximum, spent, current: maximum - spent, countsUp: true,
  trace: [], warnings: [], automation: 'automatic', recoveries: [],
});
const chargingView = (effects: object[], pools: object[]) =>
  ({ character: { id: 'fixture' }, sheet: { attacks: [], toggles: [], resources: pools, features: [{ content: ref, name: 'Testy Source', kind: 'feature', effects }] } }) as unknown as CharacterView;
const gather = {
  id: 'gather', rollId: 'gather-roll', type: 'roll', label: 'Gather', activation: 'action', resourceId: 'focus', cost: 1, available: true,
  alsoChange: [{ resourceContent: 'c1', resourceId: 'charge', change: 'spend', amount: 1 }],
};
const release = {
  id: 'release', rollId: 'release-roll', type: 'roll', label: 'Release', dice: '2d6', activation: 'action', resourceId: 'charge', cost: 0, drawsDown: true,
  available: true, perUnit: { dice: '1d6' },
};

it('Use on an action with alsoChange sends useAction with the basis', async () => {
  const act = vi.fn();
  render(<ActionsPanel view={chargingView([gather], [focusPool(4), chargePool(0)])} roll={() => {}} act={act} />);
  await userEvent.click(screen.getByRole('button', { name: 'Use Gather (1 Testy focus, +1 Testy charge)' }));
  expect(act).toHaveBeenCalledWith({ action: 'useAction', contentId: 'c1', rollId: 'gather-roll' });
  expect(act).not.toHaveBeenCalledWith(expect.objectContaining({ action: 'spend' }));
});

it('a dice-less Use with no alsoChange and no drawsDown still sends spend', async () => {
  const act = vi.fn();
  const plain = { id: 'plain', rollId: 'plain-roll', type: 'roll', label: 'Plain', activation: 'action', resourceId: 'focus', cost: 1, available: true };
  render(<ActionsPanel view={chargingView([plain], [focusPool(4)])} roll={() => {}} act={act} />);
  await userEvent.click(screen.getByRole('button', { name: 'Use Plain (1 Testy focus)' }));
  expect(act).toHaveBeenCalledWith({ action: 'spend', amount: 1, contentId: 'c1', resourceId: 'focus' });
});

it('a regain entry reads with a minus sign', () => {
  const calm = { ...gather, id: 'calm', label: 'Calm', alsoChange: [{ resourceContent: 'c1', resourceId: 'charge', change: 'regain', amount: 2 }] };
  render(<ActionsPanel view={chargingView([calm], [focusPool(4), chargePool(3)])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Use Calm (1 Testy focus, −2 Testy charge)' })).toBeTruthy();
});

it('Use is disabled with a linked reason when the charge is full, the focus is short or an amount is unavailable', () => {
  render(<ActionsPanel view={chargingView([gather], [focusPool(4), chargePool(4)])} roll={() => {}} act={() => {}} />);
  const button = screen.getByRole('button', { name: /^Use Gather/ }) as HTMLButtonElement;
  expect(button.disabled).toBe(true);
  expect(document.getElementById(button.getAttribute('aria-describedby')!)!.textContent).toMatch(/Testy charge is full/);
  cleanup();

  render(<ActionsPanel view={chargingView([gather], [focusPool(0), chargePool(0)])} roll={() => {}} act={() => {}} />);
  expect((screen.getByRole('button', { name: /^Use Gather/ }) as HTMLButtonElement).disabled).toBe(true);
  cleanup();

  const unknown = { ...gather, alsoChange: [{ resourceContent: 'c1', resourceId: 'charge', change: 'spend', amount: null }] };
  render(<ActionsPanel view={chargingView([unknown], [focusPool(4), chargePool(0)])} roll={() => {}} act={() => {}} />);
  const b = screen.getByRole('button', { name: /^Use Gather/ }) as HTMLButtonElement;
  expect(b.disabled).toBe(true);
  expect(document.getElementById(b.getAttribute('aria-describedby')!)!.textContent).toMatch(/Cost unavailable/);
});

it("release's amount box is limited to the built-up charge", async () => {
  const roll = vi.fn();
  render(<ActionsPanel view={chargingView([release], [chargePool(2)])} roll={roll} act={() => {}} />);
  const box = screen.getByRole('spinbutton', { name: /Points to spend/ }) as HTMLInputElement;
  expect(box.max).toBe('2');
  await userEvent.clear(box);
  await userEvent.type(box, '2');
  expect((screen.getByRole('button', { name: 'Roll Release (2d6)' }) as HTMLButtonElement).disabled).toBe(false);
  await userEvent.clear(box);
  await userEvent.type(box, '3');
  expect((screen.getByRole('button', { name: 'Roll Release (2d6)' }) as HTMLButtonElement).disabled).toBe(true);
});

it("the record's Spend for a drawsDown release sends useAction with the record's amount, never a plain spend", async () => {
  const act = vi.fn();
  const rec = recordFor({
    provenance: { rollId: 'release-roll', label: 'Release', content: ref, effectId: 'release', linkedResourceId: 'charge', linkedResourceContent: 'c1' },
    spend: { contentId: 'c1', resourceId: 'charge', amount: 2, drawsDown: true },
  });
  const features = [{ content: ref, name: 'Testy Source', kind: 'feature', effects: [release] }] as never;
  render(<RollResult record={rec} resources={[chargePool(3)] as never} features={features} act={act} />);
  await userEvent.click(screen.getByRole('button', { name: /^Spend 2 Testy charge/ }));
  expect(act).toHaveBeenCalledTimes(1);
  expect(act).toHaveBeenCalledWith({ action: 'useAction', contentId: 'c1', rollId: 'release-roll', amount: 2 });
  cleanup();

  // Bounded by what has built up, not by what is left under the maximum.
  render(<RollResult record={rec} resources={[chargePool(1)] as never} features={features} act={act} />);
  expect((screen.getByRole('button', { name: /^Spend 2 Testy charge/ }) as HTMLButtonElement).disabled).toBe(true);
});

it("a compound roll with no rollId is never spent as a plain spend: Use and the record's Spend are held with a reason", async () => {
  const act = vi.fn();
  const noId = { ...gather, rollId: undefined };
  render(<ActionsPanel view={chargingView([noId], [focusPool(4), chargePool(0)])} roll={() => {}} act={act} />);
  const use = screen.getByRole('button', { name: /^Use Gather/ }) as HTMLButtonElement;
  expect(use.disabled).toBe(true);
  expect(document.getElementById(use.getAttribute('aria-describedby')!)!.textContent).toMatch(/Can't be used here/);
  await userEvent.click(use);
  cleanup();

  const noRoll = { ...release, rollId: undefined };
  const base = {
    provenance: { rollId: undefined, label: 'Release', content: ref, effectId: 'release', linkedResourceId: 'charge', linkedResourceContent: 'c1' },
    spend: { contentId: 'c1', resourceId: 'charge', amount: 2, drawsDown: true },
  };
  render(<RollResult record={recordFor(base)} resources={[chargePool(3)] as never} features={[{ content: ref, name: 'Testy Source', kind: 'feature', effects: [noRoll] }] as never} act={act} />);
  const spendBtn = screen.getByRole('button', { name: /^Spend 2 Testy charge/ }) as HTMLButtonElement;
  expect(spendBtn.disabled).toBe(true);
  expect(document.getElementById(spendBtn.getAttribute('aria-describedby')!)!.textContent).toMatch(/Can't be used here/);
  cleanup();

  // The effect cannot be found at all: still held, never a plain spend.
  render(<RollResult record={recordFor(base)} resources={[chargePool(3)] as never} features={[]} act={act} />);
  expect((screen.getByRole('button', { name: /^Spend 2 Testy charge/ }) as HTMLButtonElement).disabled).toBe(true);
  expect(act).not.toHaveBeenCalled();
});

it("a refused useAction gives the record's Spend back", async () => {
  const act = vi.fn().mockResolvedValueOnce(false).mockResolvedValueOnce(true);
  const rec = recordFor({
    provenance: { rollId: 'release-roll', label: 'Release', content: ref, effectId: 'release', linkedResourceId: 'charge', linkedResourceContent: 'c1' },
    spend: { contentId: 'c1', resourceId: 'charge', amount: 2, drawsDown: true },
  });
  render(<RollResult record={rec} resources={[chargePool(3)] as never} features={[{ content: ref, name: 'Testy Source', kind: 'feature', effects: [release] }] as never} act={act} />);
  const spendBtn = () => screen.getByRole('button', { name: /^Spend 2 Testy charge/ }) as HTMLButtonElement;
  await userEvent.click(spendBtn());
  await waitFor(() => expect(spendBtn().disabled).toBe(false));
  await userEvent.click(spendBtn());
  expect(act).toHaveBeenCalledTimes(2);
  expect(spendBtn().disabled).toBe(true);
});

// Slice review fixes.
it('a pool another pool change names that the sheet does not track holds Use with that reason', () => {
  render(<ActionsPanel view={chargingView([gather], [focusPool(4)])} roll={() => {}} act={() => {}} />);
  const use = screen.getByRole('button', { name: /^Use Gather/ }) as HTMLButtonElement;
  expect(use.disabled).toBe(true);
  expect(document.getElementById(use.getAttribute('aria-describedby')!)!.textContent).toMatch(/^ · charge is not tracked on this sheet\.$/);
  cleanup();

  const untracked = { ...chargePool(0), current: undefined };
  render(<ActionsPanel view={chargingView([gather], [focusPool(4), untracked])} roll={() => {}} act={() => {}} />);
  const again = screen.getByRole('button', { name: /^Use Gather/ }) as HTMLButtonElement;
  expect(again.disabled).toBe(true);
  expect(document.getElementById(again.getAttribute('aria-describedby')!)!.textContent).toMatch(/^ · Testy charge is not tracked on this sheet\.$/);
});

it('the sign follows the pool direction: a spend on a normal pool reads minus, a regain on it plus', () => {
  const swap = (change: string) => ({ ...gather, alsoChange: [{ resourceContent: 'c1', resourceId: 'focus', change, amount: 2 }] });
  const normal = { ...gather, resourceId: 'charge', alsoChange: [{ resourceContent: 'c1', resourceId: 'focus', change: 'spend', amount: 2 }] };
  render(<ActionsPanel view={chargingView([normal], [focusPool(4), chargePool(0)])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Use Gather (1 Testy charge, −2 Testy focus)' })).toBeTruthy();
  cleanup();
  render(<ActionsPanel view={chargingView([{ ...normal, alsoChange: swap('regain').alsoChange }], [focusPool(4), chargePool(0)])} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Use Gather (1 Testy charge, +2 Testy focus)' })).toBeTruthy();
});

it('a compound use whose own cost is 0 is still usable', async () => {
  const act = vi.fn();
  const free = { ...gather, cost: 0 };
  render(<ActionsPanel view={chargingView([free], [focusPool(4), chargePool(0)])} roll={() => {}} act={act} />);
  const use = screen.getByRole('button', { name: /^Use Gather/ }) as HTMLButtonElement;
  expect(use.disabled).toBe(false);
  await userEvent.click(use);
  expect(act).toHaveBeenCalledWith({ action: 'useAction', contentId: 'c1', rollId: 'gather-roll' });
});

it("the record's Spend for a compound roll with a cost of 0 is still offered", async () => {
  const act = vi.fn();
  const free = { ...gather, cost: 0, dice: '1d6' };
  const rec = recordFor({
    provenance: { rollId: 'gather-roll', label: 'Gather', content: ref, effectId: 'gather', linkedResourceId: 'focus', linkedResourceContent: 'c1' },
    spend: { contentId: 'c1', resourceId: 'focus', amount: 0 },
  });
  render(<RollResult record={rec} resources={[focusPool(4), chargePool(0)] as never} features={[{ content: ref, name: 'Testy Source', kind: 'feature', effects: [free] }] as never} act={act} />);
  const spendBtn = screen.getByRole('button', { name: /^Spend 0 Testy focus/ }) as HTMLButtonElement;
  expect(spendBtn.disabled).toBe(false);
  await userEvent.click(spendBtn);
  expect(act).toHaveBeenCalledWith({ action: 'useAction', contentId: 'c1', rollId: 'gather-roll' });
});

it("a content roll's record whose effect left the sheet holds the Spend, never a plain spend", () => {
  const act = vi.fn();
  const rec = recordFor({
    provenance: { rollId: 'gather-roll', label: 'Gather', content: ref, effectId: 'gather', linkedResourceId: 'focus', linkedResourceContent: 'c1' },
    spend: { contentId: 'c1', resourceId: 'focus', amount: 1 },
  });
  render(<RollResult record={rec} resources={[focusPool(4)] as never} features={[]} act={act} />);
  const spendBtn = screen.getByRole('button', { name: /^Spend 1 Testy focus/ }) as HTMLButtonElement;
  expect(spendBtn.disabled).toBe(true);
  expect(document.getElementById(spendBtn.getAttribute('aria-describedby')!)!.textContent).toMatch(/Can't be used here/);
  expect(act).not.toHaveBeenCalled();
});

it('a drawsDown dice-less Use is disabled with its reason when less has built up than it costs', () => {
  const draw = { id: 'vent', rollId: 'vent-roll', type: 'roll', label: 'Vent', activation: 'action', resourceId: 'charge', cost: 2, drawsDown: true, available: true };
  render(<ActionsPanel view={chargingView([draw], [chargePool(1)])} roll={() => {}} act={() => {}} />);
  const use = screen.getByRole('button', { name: 'Use Vent (2 Testy charge)' }) as HTMLButtonElement;
  expect(use.disabled).toBe(true);
  expect(document.getElementById(use.getAttribute('aria-describedby')!)!.textContent).toMatch(/Not enough Testy charge built up \(1\)/);
  cleanup();
  render(<ActionsPanel view={chargingView([draw], [chargePool(2)])} roll={() => {}} act={() => {}} />);
  expect((screen.getByRole('button', { name: 'Use Vent (2 Testy charge)' }) as HTMLButtonElement).disabled).toBe(false);
});

// D73: the "Usable now" filter, the reasons under rows and the Pin buttons.
const usableNow = () => screen.getByRole('checkbox', { name: 'Usable now' });
const wave = { id: 'wave', type: 'roll', label: 'Wave', dice: '2d6', activation: 'action', resourceId: 'focus', cost: 2, available: true };
const rally = { id: 'rally', type: 'roll', label: 'Rally', activation: 'bonusAction', resourceId: 'focus', cost: 2, available: true };
const waveFeature = { content: ref, name: 'Testy Source', kind: 'feature', effects: [wave] };
const wavePin = () => rollPin(waveFeature as never, wave as never);

it('Usable now is off by default and every row shows', () => {
  render(<ActionsPanel view={actionsView([wave, rally], focusPool(1))} roll={() => {}} act={() => {}} />);
  expect((usableNow() as HTMLInputElement).checked).toBe(false);
  expect(screen.getByRole('button', { name: /^Roll Wave/ })).toBeTruthy();
  expect(screen.getByRole('button', { name: /^Use Rally/ })).toBeTruthy();
});

it('a form-only roll shows its reason, the filter hides it, and it returns when the form is on (no remount)', async () => {
  const formOnly = { id: 'flash', type: 'roll', label: 'Flash', dice: '1d6', activation: 'action', available: false, unavailableReason: 'Available in Fixture Tide Form' };
  const other = { ...wave, id: 'other', label: 'Other', resourceId: undefined };
  const props = { roll: () => {}, act: () => {} };
  const { rerender } = render(<ActionsPanel view={actionsView([formOnly, other])} {...props} />);
  expect(screen.getByRole('button', { name: /^Roll Flash/ }).closest('li')!.textContent).toMatch(/Available in Fixture Tide Form/);
  await userEvent.click(usableNow());
  expect(screen.queryByRole('button', { name: /^Roll Flash/ })).toBeNull();
  expect(screen.getByRole('button', { name: /^Roll Other/ })).toBeTruthy();
  rerender(<ActionsPanel view={actionsView([{ ...formOnly, available: true }, other])} {...props} />);
  expect(screen.getByRole('button', { name: /^Roll Flash/ })).toBeTruthy();
});

it('a roll with dice the pool cannot pay stays enabled, says why, is described by it, and the filter hides it', async () => {
  render(<ActionsPanel view={actionsView([wave], focusPool(1))} roll={() => {}} act={() => {}} />);
  const button = screen.getByRole('button', { name: /^Roll Wave/ }) as HTMLButtonElement;
  expect(button.disabled).toBe(false);
  expect(button.closest('li')!.textContent).toMatch(/Needs 2 Testy focus \(1 left\)/);
  expect(document.getElementById(button.getAttribute('aria-describedby')!)!.textContent).toMatch(/Needs 2 Testy focus \(1 left\)/);
  await userEvent.click(usableNow());
  expect(screen.queryByRole('button', { name: /^Roll Wave/ })).toBeNull();
  expect(screen.getByText('Nothing is usable right now.')).toBeTruthy();
});

it('an allowance pool at 0 loses its group under the filter, and nothing else left says so', async () => {
  const reaction = { id: 'guard', type: 'roll', label: 'Guard', resourceId: 'react', cost: 1, available: true };
  const pool = { ...focusPool(0), resourceId: 'react', label: 'Reaction', maximum: 1, spent: 1, allowance: true };
  render(<ActionsPanel view={actionsView([reaction], pool)} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('region', { name: 'Reaction' })).toBeTruthy();
  await userEvent.click(usableNow());
  expect(screen.queryByRole('region', { name: 'Reaction' })).toBeNull();
  expect(screen.getByText('Nothing is usable right now.')).toBeTruthy();
});

it('a dice-less Use of an untracked pool is by hand and stays under the filter', async () => {
  render(<ActionsPanel view={actionsView([rally], { ...focusPool(), current: undefined })} roll={() => {}} act={() => {}} />);
  await userEvent.click(usableNow());
  expect(screen.getByRole('button', { name: /^Use Rally/ })).toBeTruthy();
  expect(screen.queryByText('Nothing is usable right now.')).toBeNull();
});

it('a pinned row that is not usable stays listed under the filter so it can be unpinned', async () => {
  const onPin = vi.fn();
  render(<ActionsPanel view={actionsView([wave], focusPool(1))} roll={() => {}} act={() => {}} pins={[wavePin()]} onPin={onPin} />);
  await userEvent.click(usableNow());
  await userEvent.click(screen.getByRole('button', { name: 'Pin Wave to the quickbar' }));
  expect(onPin).toHaveBeenCalledWith(wavePin());
});

it('Pin calls onPin with the row pin, a pinned row is aria-pressed Unpin, and no onPin means no Pin buttons', async () => {
  const onPin = vi.fn();
  const { rerender } = render(<ActionsPanel view={actionsView([wave])} roll={() => {}} act={() => {}} onPin={onPin} />);
  const pin = screen.getByRole('button', { name: 'Pin Wave to the quickbar' });
  expect(pin.getAttribute('aria-pressed')).toBe('false');
  expect(pin.textContent).toBe('Pin');
  await userEvent.click(pin);
  expect(onPin).toHaveBeenCalledWith(wavePin());
  rerender(<ActionsPanel view={actionsView([wave])} roll={() => {}} act={() => {}} onPin={onPin} pins={[wavePin()]} />);
  const pinned = screen.getByRole('button', { name: 'Pin Wave to the quickbar' });
  expect(pinned.getAttribute('aria-pressed')).toBe('true');
  expect(pinned.textContent).toBe('Unpin');
  rerender(<ActionsPanel view={actionsView([wave])} roll={() => {}} act={() => {}} />);
  expect(screen.queryByRole('button', { name: /to the quickbar$/ })).toBeNull();
});

it('Pin buttons cover attacks, custom attacks and toggles, and an auto toggle gets none', async () => {
  const onPin = vi.fn();
  const v = {
    character: { id: 'fixture', customAttacks: [sling] },
    sheet: { attacks: [weaponRow], toggles: [toggle('stance'), toggle('grit', { auto: true, on: true })], features: [] },
  } as unknown as CharacterView;
  render(<ActionsPanel view={v} roll={() => {}} act={() => {}} onPin={onPin} />);
  await userEvent.click(screen.getByRole('button', { name: 'Pin Fixture Needle to the quickbar' }));
  expect(onPin).toHaveBeenLastCalledWith(attackPin(weaponRow as never));
  await userEvent.click(screen.getByRole('button', { name: 'Pin Testy Sling to the quickbar' }));
  expect(onPin).toHaveBeenLastCalledWith(customPin(sling));
  await userEvent.click(screen.getByRole('button', { name: 'Pin Testy stance to the quickbar' }));
  expect(onPin).toHaveBeenLastCalledWith(togglePin(toggle('stance') as never));
  expect(screen.queryByRole('button', { name: 'Pin Testy grit to the quickbar' })).toBeNull();
});

it('a toggle that cannot be turned on shows its reason, stays enabled, and the filter hides it unless it is on', async () => {
  const v = togglesView([toggle('stance', { available: false, unavailableReason: 'Available in Fixture Tide Form' })]);
  render(<ActionsPanel view={v} roll={() => {}} act={() => {}} />);
  const box = screen.getByRole('checkbox', { name: /Testy stance/ }) as HTMLInputElement;
  expect(box.disabled).toBe(false);
  expect(document.getElementById(box.getAttribute('aria-describedby')!)!.textContent).toMatch(/Available in Fixture Tide Form/);
  await userEvent.click(usableNow());
  expect(screen.queryByRole('checkbox', { name: /Testy stance/ })).toBeNull();
});

it('when Usable now hides the row just used, focus moves to the filter checkbox, not <body>', async () => {
  const props = { roll: () => {}, act: () => {} };
  const { rerender } = render(<ActionsPanel view={actionsView([rally], focusPool(2))} {...props} />);
  await userEvent.click(usableNow());
  await userEvent.click(screen.getByRole('button', { name: /^Use Rally/ })); // focuses the button, as the real click does
  rerender(<ActionsPanel view={actionsView([rally], focusPool(0))} {...props} />);
  expect(screen.queryByRole('button', { name: /^Use Rally/ })).toBeNull();
  expect(document.activeElement).toBe(usableNow());
  expect(document.querySelector('[aria-live="polite"]')!.textContent).toBe('Nothing is usable right now.');
  expect(document.querySelector('[role="status"]')).toBeNull(); // the page has one status line
});

it('focus still returns to the filter when the reply comes after an intermediate render of the same view', async () => {
  const props = { roll: () => {}, act: () => {} };
  const { rerender } = render(<ActionsPanel view={actionsView([rally], focusPool(2))} {...props} />);
  await userEvent.click(usableNow());
  await userEvent.click(screen.getByRole('button', { name: /^Use Rally/ }));
  rerender(<ActionsPanel view={actionsView([rally], focusPool(2))} {...props} />); // the sheet's in-flight render: nothing has changed yet
  expect(document.activeElement).toBe(screen.getByRole('button', { name: /^Use Rally/ }));
  rerender(<ActionsPanel view={actionsView([rally], focusPool(0))} {...props} />);
  expect(document.activeElement).toBe(usableNow());
});

it('a row that stays listed keeps focus through further renders', async () => {
  const props = { roll: () => {}, act: () => {} };
  const { rerender } = render(<ActionsPanel view={actionsView([rally], focusPool(4))} {...props} />);
  await userEvent.click(usableNow());
  await userEvent.click(screen.getByRole('button', { name: /^Use Rally/ }));
  for (const pool of [4, 3, 2]) rerender(<ActionsPanel view={actionsView([rally], focusPool(pool))} {...props} />);
  expect(document.activeElement).toBe(screen.getByRole('button', { name: /^Use Rally/ }));
});

it('a later render with no action never moves focus, even after a row button was focused and then lost to <body>', async () => {
  const props = { roll: () => {}, act: () => {} };
  const { rerender } = render(<ActionsPanel view={actionsView([rally], focusPool(2))} {...props} />);
  await userEvent.click(usableNow());
  const use = screen.getByRole('button', { name: /^Use Rally/ });
  use.focus();
  use.blur();
  expect(document.activeElement).toBe(document.body);
  rerender(<ActionsPanel view={actionsView([rally], focusPool(0))} {...props} />);
  expect(screen.queryByRole('button', { name: /^Use Rally/ })).toBeNull();
  expect(document.activeElement).toBe(document.body);
});

it('a row that stayed listed does not pull focus later: a click elsewhere in the panel disarms the refocus', async () => {
  const props = { roll: () => {}, act: () => {} };
  const { rerender } = render(<ActionsPanel view={actionsView([rally], focusPool(4))} {...props} />);
  await userEvent.click(usableNow());
  await userEvent.click(screen.getByRole('button', { name: /^Use Rally/ })); // arms; the row stays listed
  rerender(<ActionsPanel view={actionsView([rally], focusPool(3))} {...props} />);
  await userEvent.click(screen.getByRole('heading', { name: 'Attacks and actions' })); // focus goes to <body>
  expect(document.activeElement).toBe(document.body);
  rerender(<ActionsPanel view={actionsView([rally], focusPool(0))} {...props} />); // another action empties the pool
  expect(screen.queryByRole('button', { name: /^Use Rally/ })).toBeNull();
  expect(document.activeElement).toBe(document.body);
});

it('a focus move to another control in the panel disarms the refocus as well', async () => {
  const props = { roll: () => {}, act: () => {} };
  const { rerender } = render(<ActionsPanel view={actionsView([rally], focusPool(4))} {...props} />);
  await userEvent.click(usableNow());
  await userEvent.click(screen.getByRole('button', { name: /^Use Rally/ }));
  screen.getByRole('checkbox', { name: /^Critical hit/ }).focus(); // keyboard move, no pointer
  screen.getByRole('checkbox', { name: /^Critical hit/ }).blur();
  rerender(<ActionsPanel view={actionsView([rally], focusPool(0))} {...props} />);
  expect(document.activeElement).toBe(document.body);
});

it('when the row drops while focus is outside the panel, focus stays there', async () => {
  const props = { roll: () => {}, act: () => {} };
  const ui = (pool: number) => (
    <>
      <ActionsPanel view={actionsView([rally], focusPool(pool))} {...props} />
      <button type="button">Elsewhere</button>
    </>
  );
  const { rerender } = render(ui(2));
  await userEvent.click(usableNow());
  await userEvent.click(screen.getByRole('button', { name: /^Use Rally/ }));
  await userEvent.click(screen.getByRole('button', { name: 'Elsewhere' }));
  rerender(ui(0));
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Elsewhere' }));
});

it('with 8 pins an unpinned row cannot be pinned (aria-disabled, no call, described) but a pinned one can be unpinned', async () => {
  const eight: Pin[] = Array.from({ length: 8 }, (_, i) => ({ kind: 'custom', attackId: `k${i}` }));
  const onPin = vi.fn();
  render(<ActionsPanel view={actionsView([wave])} roll={() => {}} act={() => {}} onPin={onPin} pins={eight} />);
  const blocked = screen.getByRole('button', { name: 'Pin Wave to the quickbar' });
  expect(blocked.getAttribute('aria-disabled')).toBe('true');
  expect(document.getElementById(blocked.getAttribute('aria-describedby')!)!.textContent?.trim()).toBe('The quickbar holds 8 actions.');
  await userEvent.click(blocked);
  expect(onPin).not.toHaveBeenCalled();
  cleanup();
  render(<ActionsPanel view={actionsView([wave])} roll={() => {}} act={() => {}} onPin={onPin} pins={[...eight.slice(0, 7), wavePin()]} />);
  const own = screen.getByRole('button', { name: 'Pin Wave to the quickbar' });
  expect(own.getAttribute('aria-disabled')).toBeNull();
  await userEvent.click(own);
  expect(onPin).toHaveBeenCalledTimes(1);
});

// MECH-X item 1 (R36, R37, R38, R40): hit-point and slot payments and cast-spell rows on the Actions panel.
const payRef = { contentId: 'c-fury', revisionId: 'r-fury' };
const payFeature = (effects: object[]) => ({ content: payRef, name: 'Fixture Fury', kind: 'feature', origin: {}, automation: 'automatic', diagnostics: [], effects });
const payView = (sheet: object) => ({ character: { id: 'fixture' }, sheet: { attacks: [], toggles: [], features: [], resources: [], ...sheet } }) as unknown as CharacterView;
const payToggle = (extra: object) => ({ content: payRef, contentName: 'Fixture Fury', effectId: 'e-rage', toggleId: 'rage', label: 'Fixture rage', on: false, available: true, cost: 3, ...extra });

it('a toggle row says what it costs in hit points or a slot, and an off toggle that cannot pay has its switch held with the reason (R40)', () => {
  const hp = payToggle({ payment: { kind: 'hitPoints', payable: true } });
  const slot = payToggle({ toggleId: 'ward', effectId: 'e-ward', label: 'Fixture ward', cost: undefined, payment: { kind: 'slot', payable: false, reason: 'No spell slot is left.', minSlotLevel: 2 } });
  render(<ActionsPanel view={payView({ toggles: [hp, slot] })} roll={() => {}} act={() => {}} />);
  expect(screen.getByText(/Costs 3 hit points/)).toBeTruthy();
  expect(screen.getByText(/Costs a level 2\+ spell slot/)).toBeTruthy();
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: /Fixture rage/ }).disabled).toBe(false);
  const ward = screen.getByRole<HTMLInputElement>('checkbox', { name: /Fixture ward/ });
  expect(ward.disabled).toBe(true);
  expect(screen.getByText('No spell slot is left.')).toBeTruthy();
});

it('an ON paying toggle stays switchable off even when it could not be paid now (B1)', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const on = payToggle({ on: true, payment: { kind: 'hitPoints', payable: false, reason: 'Needs more than 3 current hit points.' } });
  render(<ActionsPanel view={payView({ toggles: [on] })} roll={() => {}} act={act} />);
  const box = screen.getByRole<HTMLInputElement>('checkbox', { name: /Fixture rage/ });
  expect(box.disabled).toBe(false);
  await user.click(box);
  expect(act).toHaveBeenCalledExactlyOnceWith({ action: 'toggleOff', contentId: 'c-fury', toggleId: 'rage' });
});

it('switching a payable paying toggle on sends toggleOn; a paying toggle cannot be pinned (S4)', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const pin = vi.fn();
  render(<ActionsPanel view={payView({ toggles: [payToggle({ payment: { kind: 'hitPoints', payable: true } })] })} roll={() => {}} act={act} onPin={pin} />);
  await user.click(screen.getByRole('checkbox', { name: /Fixture rage/ }));
  expect(act).toHaveBeenCalledExactlyOnceWith({ action: 'toggleOn', contentId: 'c-fury', toggleId: 'rage' });
  expect(screen.queryByRole('button', { name: /^Pin Fixture rage/ })).toBeNull();
});

it('a paying roll with dice that cannot be paid shows its reason, and Roll still rolls (rolling never pays)', async () => {
  const user = userEvent.setup();
  const roll = vi.fn();
  const effect = { id: 'lash', rollId: 'lash-roll', type: 'roll', automation: 'automatic', label: 'Lash', dice: '2d6', cost: 5, available: true, payment: { kind: 'hitPoints', payable: false, reason: 'Needs more than 5 current hit points.' } };
  render(<ActionsPanel view={payView({ features: [payFeature([effect])] })} roll={roll} act={() => {}} />);
  expect(screen.getByText(/Needs more than 5 current hit points\./)).toBeTruthy();
  expect(screen.getByText(/Costs 5 hit points/)).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Roll Lash (2d6)' }));
  expect(roll).toHaveBeenCalledExactlyOnceWith({ content: payRef, effectId: 'lash', critical: false });
});

it('a dice-less paying roll is a Use button that sends useAction with its rollId; held with the reason when it cannot pay', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const ok = { id: 'surge', rollId: 'surge-roll', type: 'roll', automation: 'automatic', label: 'Surge', cost: 2, available: true, payment: { kind: 'hitPoints', payable: true } };
  const broke = { id: 'drain', rollId: 'drain-roll', type: 'roll', automation: 'automatic', label: 'Drain', cost: 9, available: true, payment: { kind: 'hitPoints', payable: false, reason: 'Needs more than 9 current hit points.' } };
  render(<ActionsPanel view={payView({ features: [payFeature([ok, broke])] })} roll={() => {}} act={act} />);
  await user.click(screen.getByRole('button', { name: 'Use Surge (2 hit points)' }));
  expect(act).toHaveBeenCalledExactlyOnceWith({ action: 'useAction', contentId: 'c-fury', rollId: 'surge-roll' });
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Use Drain (9 hit points)' }).disabled).toBe(true);
  expect(screen.getByText(/Needs more than 9 current hit points\./)).toBeTruthy();
});

it('a hit-point per-unit roll asks how many points and rolls with that amount, even with no pool', async () => {
  const user = userEvent.setup();
  const roll = vi.fn();
  const effect = { id: 'burn', rollId: 'burn-roll', type: 'roll', automation: 'automatic', label: 'Burn', dice: '1d6', cost: 2, available: true, perUnit: { dice: '1d6', maxUnits: 3 }, payment: { kind: 'hitPoints', payable: true } };
  render(<ActionsPanel view={payView({ features: [payFeature([effect])] })} roll={roll} act={() => {}} />);
  const box = screen.getByRole('spinbutton', { name: /Points to spend/ });
  await user.clear(box);
  await user.type(box, '4');
  await user.click(screen.getByRole('button', { name: 'Roll Burn (1d6)' }));
  expect(roll).toHaveBeenCalledExactlyOnceWith({ content: payRef, effectId: 'burn', critical: false, amount: 4 });
});

it('a paying feature attack says what it costs and shows its reason when it cannot pay', () => {
  const lash = {
    item: payRef, name: 'Fixture Lash', effectId: 'lash', source: 'feature', attack: 'melee', toHit: 4, damage: '1d8', damageType: 'fire', properties: [], trace: [], warnings: [],
    automation: 'automatic', available: true, replacesAttack: false, cost: 4, payment: { kind: 'hitPoints', payable: false, reason: 'Needs more than 4 current hit points.' },
  };
  render(<ActionsPanel view={payView({ attacks: [lash] })} roll={() => {}} act={() => {}} />);
  expect(screen.getByText(/Needs more than 4 current hit points\./)).toBeTruthy();
  expect(screen.getByText(/Costs 4 hit points/)).toBeTruthy();
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Roll Fixture Lash attack' }).disabled).toBe(false);
});

it('a cast-spell roll says which spell it casts, its level and the numbers (R38)', () => {
  const cast = (extra: object) => ({ spell: { contentId: 's1', revisionId: 'sr1' }, name: 'Fixture Spark', level: 2, attack: 'none', ...extra });
  const row = (id: string, label: string, castsSpell: object) => ({ id, rollId: `${id}-roll`, type: 'roll', automation: 'automatic', label, available: true, castsSpell });
  const withDc = row('spark', 'Spark', cast({ dice: '2d8', save: 'dex', saveDc: 14 }));
  const byHand = row('glow', 'Glow', cast({ name: 'Fixture Glow', level: 1, dice: '1d6', attack: 'ranged' }));
  const atk = row('bolt', 'Bolt', cast({ name: 'Fixture Bolt', dice: '1d10', attack: 'ranged', attackBonus: 6 }));
  render(<ActionsPanel view={payView({ features: [payFeature([withDc, byHand, atk])] })} roll={() => {}} act={() => {}} />);
  expect(screen.getByText(/Casts Fixture Spark \(level 2\) · DC 14/)).toBeTruthy();
  expect(screen.getByText(/Casts Fixture Glow \(level 1\) · numbers by hand/)).toBeTruthy();
  expect(screen.getByText(/Casts Fixture Bolt \(level 2\) · \+6 to hit/)).toBeTruthy();
  // The spell's own dice are what rolls, so the row is a Roll button.
  expect(screen.getByRole('button', { name: 'Roll Spark (2d8)' })).toBeTruthy();
});


// MECH-X item 3: a weapon carried as several copies gets one row per copy (the service sends `entryIndex`); the rows are told apart.
it('labels attack rows of copies apart, and leaves a single weapon unlabelled', () => {
  const item = { contentId: 'c-blade', revisionId: 'r-blade' };
  const own = (equipped: boolean) => ({ item, equipped, quantity: 1 });
  const row = (entryIndex?: number) => ({ item, effectId: 'e1', name: 'Fixture Blade', toHit: 5, damage: '1d8', damageType: 'slashing', automation: 'automatic', category: 'martial', attack: 'melee', properties: [], trace: [], warnings: [], source: 'weapon', ...(entryIndex === undefined ? {} : { entryIndex }) });
  const copies = { character: { id: 'fixture', equipment: [own(true), own(true)] }, sheet: { attacks: [row(0), row(1)], toggles: [], features: [] } } as unknown as CharacterView;
  render(<ActionsPanel view={copies} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Fixture Blade (copy 1 of 2) attack' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Roll Fixture Blade (copy 2 of 2) damage (1d8)' })).toBeTruthy();
  cleanup();
  const single = { character: { id: 'fixture', equipment: [own(true)] }, sheet: { attacks: [row()], toggles: [], features: [] } } as unknown as CharacterView;
  render(<ActionsPanel view={single} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Fixture Blade attack' })).toBeTruthy();
});

it('numbers copies as the Inventory does, counting an unequipped copy between two equipped ones', () => {
  const item = { contentId: 'c-blade', revisionId: 'r-blade' };
  const own = (equipped: boolean) => ({ item, equipped, quantity: 1 });
  const row = (entryIndex: number) => ({ item, effectId: 'e1', name: 'Fixture Blade', toHit: 5, damage: '1d8', damageType: 'slashing', automation: 'automatic', category: 'martial', attack: 'melee', properties: [], trace: [], warnings: [], source: 'weapon', entryIndex });
  const v = { character: { id: 'fixture', equipment: [own(true), own(false), own(true)] }, sheet: { attacks: [row(0), row(2)], toggles: [], features: [] } } as unknown as CharacterView;
  render(<ActionsPanel view={v} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('button', { name: 'Roll Fixture Blade (copy 1 of 3) attack' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Roll Fixture Blade (copy 3 of 3) attack' })).toBeTruthy();
});

// MECH-X item 4: a content companion's rows are on its card; the lists that show cards skip them, every lookup keeps them.
it('Actions and Resources panels hide companion rows but a roll that spends a companion pool still finds it', () => {
  const content = { contentId: 'c-charm', revisionId: 'r-charm' };
  const attack = (name: string, extra: object = {}) => ({ item: content, name, effectId: name, source: 'feature', attack: 'melee', toHit: 4, damage: '1d6', damageType: 'piercing', properties: [], trace: [], warnings: [], automation: 'automatic', available: true, replacesAttack: false, ...extra });
  const pool = (id: string, extra: object = {}) => ({ content, contentName: 'Testy Charm', effectId: `e-${id}`, resourceId: id, label: `Testy ${id}`, maximum: 2, spent: 0, current: 2, trace: [], warnings: [], automation: 'automatic', recoveries: [], ...extra });
  const v = {
    character: { id: 'fixture' },
    sheet: {
      // The plain attack spends a pool that belongs to a companion: it must still name it.
      attacks: [attack('Pack Claw', { companionId: 'g1' }), attack('Plain Lash', { resourceId: 'howl', costResourceContent: 'c-charm', cost: 1 })],
      resources: [pool('howl', { companionId: 'g1' }), pool('guard')],
      toggles: [],
      features: [],
    },
  } as unknown as CharacterView;
  render(
    <>
      <ActionsPanel view={v} roll={() => {}} act={() => {}} />
      <ResourcesPanel view={v} act={() => {}} />
    </>,
  );
  const actions = screen.getByRole('region', { name: 'Attacks and actions' });
  expect(within(actions).queryByRole('button', { name: 'Roll Pack Claw attack' })).toBeNull();
  expect(within(actions).getByRole('button', { name: 'Roll Plain Lash attack' })).toBeTruthy();
  expect(actions.textContent).toMatch(/uses 1 Testy howl \(spend it from the roll\)/);
  const resources = screen.getByRole('region', { name: 'Resources' });
  expect(within(resources).queryByRole('heading', { name: /Testy howl/ })).toBeNull();
  expect(within(resources).getByRole('heading', { name: /Testy guard/ })).toBeTruthy();
});

it('does not make an allowance group of a companion allowance pool', () => {
  const content = { contentId: 'c-charm', revisionId: 'r-charm' };
  const pool = (id: string, extra: object = {}) => ({ content, contentName: 'Testy Charm', effectId: `e-${id}`, resourceId: id, label: `Testy ${id}`, maximum: 1, spent: 0, current: 1, trace: [], warnings: [], automation: 'automatic', recoveries: [], allowance: true, ...extra });
  const rollFeature = (resourceId: string) => ({
    content,
    name: 'Testy Charm',
    kind: 'feature',
    diagnostics: [],
    effects: [{ id: `r-${resourceId}`, type: 'roll', automation: 'automatic', label: `Roll ${resourceId}`, dice: '1d6', resourceId, cost: 1, activation: 'action', available: true }],
  });
  const v = { character: { id: 'fixture' }, sheet: { attacks: [], toggles: [], features: [rollFeature('mine'), rollFeature('pet')], resources: [pool('mine'), pool('pet', { companionId: 'g1' })] } } as unknown as CharacterView;
  render(<ActionsPanel view={v} roll={() => {}} act={() => {}} />);
  expect(screen.getByRole('region', { name: 'Testy mine' })).toBeTruthy();
  expect(screen.queryByRole('region', { name: 'Testy pet' })).toBeNull();
});
