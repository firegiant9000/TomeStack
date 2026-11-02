// @vitest-environment jsdom
// D43 slice 4 (Task 4.5b): feature attacks, riders and cross-entry toggles in the studio.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: { listSources: vi.fn(), contentBySource: vi.fn(), publishManyPreview: vi.fn(), createHomebrewSource: vi.fn(), previewFormula: vi.fn(), listCharacters: vi.fn(), validateRevision: vi.fn() },
}));

const info = { rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }], fields: [{ id: 'initiative', label: 'Initiative' }] } as unknown as AppInfo;
const source = { id: 'a', title: 'Testy Alpha', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-m', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;
type Other = { name: string; contentId: string; effects: Raw[] };

const stance: Raw = { type: 'toggle', id: 'toggle-1', toggleId: 'stance', label: 'Testy Stance' };
const focus: Raw = { type: 'resource', id: 'resource-2', resourceId: 'focus', label: 'Testy Focus', maximum: '3' };
const attack: Raw = {
  type: 'attack', id: 'attack-3', attackId: 'attack1', label: 'Testy Jab', kind: 'melee', abilities: ['str'], proficient: true, damage: { count: '1', die: '6' }, damageType: 'bludgeoning',
};
const rider: Raw = { type: 'attackRider', id: 'rider-4', tags: ['all'], dice: '1d6', toggle: 'stance' };
const otherStance: Other = { name: 'Testy Other Mode', contentId: 'other-content', effects: [{ type: 'toggle', id: 't', toggleId: 'stance', label: 'Other Stance' }] };

/** Opens a draft feature with these rules; `others` are other published entries of the same source. */
async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[], others: Other[] = []) {
  const entry = {
    contentId: 'id-m',
    name: 'Testy Mode',
    kind: 'feature',
    revisions: [],
    latest: { contentId: 'id-m', revisionId: 'r-m', name: 'Testy Mode', kind: 'feature', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  const otherEntries = others.map((o) => {
    const revision = { contentId: o.contentId, revisionId: `r-${o.contentId}`, name: o.name, kind: 'feature', status: 'published', rulesFamilies: ['srd-5.1'], provenance: {}, effects: o.effects };
    return { contentId: o.contentId, name: o.name, kind: 'feature', revisions: [revision], latest: revision, latestPublished: revision } as unknown as StudioEntry;
  });
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry, ...otherEntries] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Mode' }));
  const rule = (name: RegExp) => within(screen.getByRole('group', { name }));
  const checked = async () => {
    vi.mocked(client.validateRevision).mockClear();
    await user.click(screen.getByRole('button', { name: 'Check' }));
    await waitFor(() => expect(vi.mocked(client.validateRevision).mock.calls.length).toBeGreaterThan(0));
    const sent = vi.mocked(client.validateRevision).mock.calls.at(-1)![0] as unknown as { effects: Raw[] };
    return { effect: (id: string) => sent.effects.find((e) => e.id === id), count: sent.effects.length };
  };
  return { rule, checked };
}

const saveBlocked = () => screen.getByRole('button', { name: 'Save draft' }).getAttribute('aria-disabled') === 'true';

it('Add attack adds a valid starting attack with its own key each time, and Add attack rider adds a rider', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, []);
  await user.click(screen.getByRole('button', { name: 'Add attack' }));
  await user.click(screen.getByRole('button', { name: 'Add attack' }));
  await user.click(screen.getByRole('button', { name: 'Add attack rider' }));
  expect(rule(/^Rule 1: Attack/).getByRole('textbox', { name: 'Attack name' })).toBeTruthy();
  expect(rule(/^Rule 3: Attack rider/).getByRole('textbox', { name: /^Tags/ })).toBeTruthy();
  const sent = await checked();
  expect(sent.effect('attack-1')).toMatchObject({ type: 'attack', attackId: 'attack1', kind: 'melee', abilities: ['str'], proficient: true, damage: { count: '1', die: '6' }, damageType: 'bludgeoning' });
  expect(sent.effect('attack-2')).toMatchObject({ attackId: 'attack2' });
  expect(sent.effect('attackRider-3')).toMatchObject({ type: 'attackRider', tags: ['all'], dice: '1d6' });
  expect(saveBlocked()).toBe(false);
});

it('an attack rule shows its fields as words and sends what was chosen', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [stance, focus, attack]);
  const a = rule(/^Rule 3: Attack/);
  const name = a.getByRole('textbox', { name: 'Attack name' });
  await user.clear(name);
  await user.type(name, 'Testy Bolt');
  await user.selectOptions(a.getByRole('combobox', { name: 'Kind' }), 'ranged');
  await user.click(a.getByRole('checkbox', { name: 'Dexterity' }));
  await user.click(a.getByRole('checkbox', { name: 'Add the proficiency bonus to the attack roll' }));
  const die = a.getByRole('textbox', { name: /^Damage die \(/ });
  await user.clear(die);
  await user.type(die, '8');
  await user.type(a.getByRole('textbox', { name: /^Damage bonus/ }), 'PB');
  await user.selectOptions(a.getByRole('combobox', { name: 'Damage type' }), 'force');
  await user.type(a.getByRole('textbox', { name: /^Range/ }), '60 ft');
  await user.type(a.getByRole('textbox', { name: /^Tags/ }), 'bolt, singleTarget');
  await user.click(a.getByRole('checkbox', { name: 'Can replace one attack of the Attack action' }));
  await user.selectOptions(a.getByRole('combobox', { name: 'Activation' }), 'bonusAction');
  await user.selectOptions(a.getByRole('combobox', { name: 'Spends from' }), 'focus');
  await user.type(a.getByRole('textbox', { name: /^Cost/ }), '2');
  await user.selectOptions(a.getByRole('combobox', { name: 'Available' }), 'While Testy Stance is on');
  expect((await checked()).effect('attack-3')).toMatchObject({
    label: 'Testy Bolt', kind: 'ranged', abilities: ['str', 'dex'], proficient: false, damage: { count: '1', die: '8' }, damageBonus: 'PB', damageType: 'force', range: '60 ft',
    tags: ['bolt', 'singleTarget'], replacesAttack: true, activation: 'bonusAction', resourceId: 'focus', cost: '2', whileToggle: { toggleId: 'stance' },
  });
});

it('a tag that breaks the rule shows a linked error and blocks saving until fixed; nothing is coerced', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [attack]);
  const a = rule(/^Rule 1: Attack/);
  const tags = a.getByRole('textbox', { name: /^Tags/ });
  await user.type(tags, 'good, Bad Tag');
  expect(tags.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(tags.getAttribute('aria-describedby')!)!.textContent).toMatch(/"Bad Tag" is not a tag/);
  expect(saveBlocked()).toBe(true);
  expect(screen.getByText(/Fix the marked field before saving/)).toBeTruthy();
  expect((await checked()).effect('attack-3')!.tags).toEqual(['good']); // the bad text was never handed up; the last valid reading stays

  await user.clear(tags);
  await user.type(tags, 'all, good');
  expect(tags.hasAttribute('aria-invalid')).toBe(false);
  expect(saveBlocked()).toBe(false);
  expect((await checked()).effect('attack-3')!.tags).toEqual(['all', 'good']);
});

it('an attack with no ability shows a linked error and blocks saving', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [attack]);
  const a = rule(/^Rule 1: Attack/);
  await user.click(a.getByRole('checkbox', { name: 'Strength' }));
  expect(a.getByText('Choose at least one ability.')).toBeTruthy();
  expect(saveBlocked()).toBe(true);
  await user.click(a.getByRole('checkbox', { name: 'Wisdom' }));
  expect(a.queryByText('Choose at least one ability.')).toBeNull();
  expect(saveBlocked()).toBe(false);
});

it('rider dice, tags and stack group are read strictly', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [rider]);
  const r = rule(/^Rule 1: Attack rider/);
  const dice = r.getByRole('textbox', { name: /^Extra damage dice/ });
  await user.clear(dice);
  await user.type(dice, '3d7');
  expect(document.getElementById(dice.getAttribute('aria-describedby')!)!.textContent).toMatch(/is not dice/);
  expect(saveBlocked()).toBe(true);
  await user.clear(dice);
  await user.type(dice, '3d8');
  expect(saveBlocked()).toBe(false);

  const tags = r.getByRole('textbox', { name: /^Tags/ });
  await user.clear(tags);
  expect(r.getByText('at least one tag is needed')).toBeTruthy();
  expect(saveBlocked()).toBe(true);
  await user.type(tags, 'unarmed');

  const group = r.getByRole('textbox', { name: /^Stack group/ });
  await user.type(group, 'Mode Rider');
  expect(saveBlocked()).toBe(true);
  await user.clear(group);
  await user.type(group, 'modeRider');
  expect(saveBlocked()).toBe(false);
  expect((await checked()).effect('rider-4')).toMatchObject({ tags: ['unarmed'], dice: '3d8', stackGroup: 'modeRider', toggle: 'stance' });
});

it('removing a rule with a problem lifts its block', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [attack]);
  const a = rule(/^Rule 1: Attack/);
  await user.type(a.getByRole('textbox', { name: /^Tags/ }), 'Bad Tag');
  expect(saveBlocked()).toBe(true);
  await user.click(a.getByRole('button', { name: 'Remove rule 1' }));
  expect(saveBlocked()).toBe(false);
});

it("a rider's toggle select lists this revision's toggles and those of other entries, and names the entry's content", async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [stance, { ...rider, toggle: undefined }], [otherStance]);
  const select = rule(/^Rule 2: Attack rider/).getByRole('combobox', { name: 'Applies' });
  expect(within(select).getByRole('option', { name: 'While Testy Stance is on' })).toBeTruthy();
  await user.selectOptions(select, 'While Other Stance (Testy Other Mode) is on');
  expect((await checked()).effect('rider-4')).toMatchObject({ toggle: 'stance', toggleContent: 'other-content' });
  await user.selectOptions(select, 'While Testy Stance is on');
  const own = (await checked()).effect('rider-4')!;
  expect(own.toggle).toBe('stance');
  expect(own.toggleContent).toBeUndefined();
  await user.selectOptions(select, 'Always');
  const always = (await checked()).effect('rider-4')!;
  expect(always.toggle).toBeUndefined();
  expect(always.toggleContent).toBeUndefined();
});

it('a modifier can follow a toggle of another entry: toggle, toggleContent and timing are set; the own toggle clears the content', async () => {
  const user = userEvent.setup();
  const modifier: Raw = { type: 'modifier', id: 'modifier-1', operation: 'bonus', target: 'initiative', value: '1' };
  const { rule, checked } = await openDraft(user, [modifier], [otherStance]); // this revision declares no toggle at all
  const select = rule(/^Rule 1: Modifier/).getByRole('combobox', { name: 'Applies' });
  await user.selectOptions(select, 'While Other Stance (Testy Other Mode) is on');
  expect((await checked()).effect('modifier-1')).toMatchObject({ toggle: 'stance', toggleContent: 'other-content', timing: 'whileActive' });
  await user.selectOptions(select, 'Always');
  const always = (await checked()).effect('modifier-1')!;
  expect([always.toggle, always.toggleContent, always.timing]).toEqual([undefined, undefined, undefined]);
});

it('a roll can be made available only in a form, in this revision or another entry', async () => {
  const user = userEvent.setup();
  const roll: Raw = { type: 'roll', id: 'roll-2', rollId: 'roll-2', label: 'Flash', dice: '1d6', timing: 'onRoll', automation: 'assisted' };
  const { rule, checked } = await openDraft(user, [stance, roll], [otherStance]);
  const select = rule(/^Rule 2: Roll or action/).getByRole('combobox', { name: 'Available' });
  await user.selectOptions(select, 'While Testy Stance is on');
  expect((await checked()).effect('roll-2')).toMatchObject({ whileToggle: { toggleId: 'stance' } });
  await user.selectOptions(select, 'While Other Stance (Testy Other Mode) is on');
  expect((await checked()).effect('roll-2')).toMatchObject({ whileToggle: { contentId: 'other-content', toggleId: 'stance' } });
  await user.selectOptions(select, 'Always');
  expect((await checked()).effect('roll-2')!.whileToggle).toBeUndefined();
});

it('a modifier can raise the maximum of a pool of this revision, offered only for a bonus', async () => {
  const user = userEvent.setup();
  const modifier: Raw = { type: 'modifier', id: 'modifier-2', operation: 'bonus', target: 'initiative', value: '1' };
  const { rule, checked } = await openDraft(user, [focus, modifier]);
  const m = rule(/^Rule 2: Modifier/);
  await user.selectOptions(m.getByRole('combobox', { name: 'Changes' }), 'Maximum of Testy Focus');
  expect((await checked()).effect('modifier-2')).toMatchObject({ target: 'resource.focus', operation: 'bonus' });
  await user.selectOptions(m.getByRole('combobox', { name: 'Changes' }), 'Initiative');
  await user.selectOptions(m.getByRole('combobox', { name: 'How' }), 'set');
  expect(m.queryByRole('option', { name: 'Maximum of Testy Focus' })).toBeNull();
  await user.selectOptions(m.getByRole('combobox', { name: 'How' }), 'bonus');
  expect(m.getByRole('option', { name: 'Maximum of Testy Focus' })).toBeTruthy();
});

it('removing a toggle removes the riders that followed it, and clears what named it, keeping other entries with the same id', async () => {
  const user = userEvent.setup();
  const effects: Raw[] = [
    stance, // rule 1
    { ...attack, id: 'attack-2', whileToggle: { toggleId: 'stance' } }, // 2
    { ...attack, id: 'attack-3', attackId: 'attack2', whileToggle: { contentId: 'other-content', toggleId: 'stance' } }, // 3
    rider, // 4: follows this stance
    { ...rider, id: 'rider-5', toggleContent: 'other-content' }, // 5: another entry's stance
    { ...rider, id: 'rider-6', toggle: undefined }, // 6: always on
    { type: 'roll', id: 'roll-7', rollId: 'roll-7', label: 'Flash', dice: '1d6', timing: 'onRoll', automation: 'assisted', whileToggle: { toggleId: 'stance' } },
    { type: 'modifier', id: 'modifier-8', operation: 'bonus', target: 'initiative', value: '1', timing: 'whileActive', toggle: 'stance' },
    { type: 'modifier', id: 'modifier-9', operation: 'bonus', target: 'initiative', value: '1', timing: 'whileActive', toggle: 'stance', toggleContent: 'other-content' },
  ];
  const { rule, checked } = await openDraft(user, effects, [otherStance]);
  await user.click(rule(/^Rule 1: Toggle/).getByRole('button', { name: 'Remove rule 1' }));
  const sent = await checked();
  expect(sent.effect('rider-4')).toBeUndefined();
  expect(sent.effect('rider-5')).toMatchObject({ toggle: 'stance', toggleContent: 'other-content' });
  expect(sent.effect('rider-6')).toBeTruthy();
  expect(sent.effect('attack-2')!.whileToggle).toBeUndefined();
  expect(sent.effect('attack-3')).toMatchObject({ whileToggle: { contentId: 'other-content', toggleId: 'stance' } });
  expect(sent.effect('roll-7')!.whileToggle).toBeUndefined();
  const own = sent.effect('modifier-8')!;
  expect([own.toggle, own.timing]).toEqual([undefined, undefined]);
  expect(sent.effect('modifier-9')).toMatchObject({ toggle: 'stance', toggleContent: 'other-content', timing: 'whileActive' });
  expect(sent.count).toBe(7); // the toggle and the rider that followed it are gone
});

it("removing a pool clears an attack's pool and cost, and removes a modifier that raised its maximum", async () => {
  const user = userEvent.setup();
  const effects: Raw[] = [
    focus, // 1
    { ...attack, id: 'attack-2', resourceId: 'focus', cost: '2' },
    { ...attack, id: 'attack-3', attackId: 'attack2', resourceId: 'focus', resourceContent: 'other-content', cost: '1' }, // another content's pool
    { type: 'modifier', id: 'modifier-4', operation: 'bonus', target: 'resource.focus', value: '1' },
    { type: 'modifier', id: 'modifier-5', operation: 'bonus', target: 'initiative', value: '1' },
  ];
  const { rule, checked } = await openDraft(user, effects);
  await user.click(rule(/^Rule 1: Resource/).getByRole('button', { name: 'Remove rule 1' }));
  const sent = await checked();
  const cleared = sent.effect('attack-2')!;
  expect([cleared.resourceId, cleared.cost]).toEqual([undefined, undefined]);
  expect(sent.effect('attack-3')).toMatchObject({ resourceId: 'focus', resourceContent: 'other-content', cost: '1' });
  expect(sent.effect('modifier-4')).toBeUndefined();
  expect(sent.effect('modifier-5')).toBeTruthy();
  expect(sent.count).toBe(3);
});

// MECH-X item 1 (T5): an attack pays with hit points or a spell slot.
it('an attack pays with hit points or a slot, drops its pool keys, and keeps what the editor does not touch', async () => {
  const user = userEvent.setup();
  const paid: Raw = { ...attack, id: 'attack-2', resourceId: 'focus', cost: '2', automation: 'manual', text: 'Fixture note', flair: [1] };
  const { rule, checked } = await openDraft(user, [focus, paid]);
  const a = rule(/^Rule 2: Attack/);
  await user.selectOptions(a.getByRole('combobox', { name: 'Pays with' }), 'hitPoints');
  expect(a.queryByRole('combobox', { name: 'Spends from' })).toBeNull();
  let sent = (await checked()).effect('attack-2') as Raw;
  expect(sent.payment).toEqual({ kind: 'hitPoints' });
  expect(sent.resourceId).toBeUndefined();
  expect(sent).toMatchObject({ cost: '2', automation: 'manual', text: 'Fixture note', flair: [1] });
  await user.click(a.getByRole('checkbox', { name: 'May drop to 0 hit points' }));
  expect(((await checked()).effect('attack-2') as Raw).payment).toEqual({ kind: 'hitPoints', mayDropToZero: true });

  await user.selectOptions(a.getByRole('combobox', { name: 'Pays with' }), 'slot');
  await user.selectOptions(a.getByRole('combobox', { name: 'Lowest slot level' }), '3');
  sent = (await checked()).effect('attack-2') as Raw;
  expect(sent.payment).toEqual({ kind: 'slot', minSlotLevel: 3 });
  expect(sent.cost).toBeUndefined();
  expect(sent).toMatchObject({ automation: 'manual', flair: [1] });

  await user.selectOptions(a.getByRole('combobox', { name: 'Pays with' }), 'resource');
  expect('payment' in ((await checked()).effect('attack-2') as Raw)).toBe(false);
  expect(a.getByRole('combobox', { name: 'Spends from' })).toBeTruthy();
});

it('a stored attack payment of an unknown kind shows read-only and blocks Save', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ ...attack, payment: { kind: 'mystery' } }]);
  expect(rule(/^Rule 1: Attack/).getByText(/This payment doesn't read/)).toBeTruthy();
  expect(rule(/^Rule 1: Attack/).queryByRole('combobox', { name: 'Pays with' })).toBeNull();
  await waitFor(() => expect(saveBlocked()).toBe(true));
});
