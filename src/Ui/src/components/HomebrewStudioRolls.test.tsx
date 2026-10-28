// @vitest-environment jsdom
// D43 slice 5 (Task 5.5b): the full roll editor and named values in the studio.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, SourceRecord, StudioEntry } from '../api/types';
import { HomebrewStudio } from './HomebrewStudio';

vi.mock('../api/client', () => ({
  client: { listSources: vi.fn(), contentBySource: vi.fn(), publishManyPreview: vi.fn(), createHomebrewSource: vi.fn(), previewFormula: vi.fn(), listCharacters: vi.fn(), validateRevision: vi.fn(), listContent: vi.fn(), contentRevisions: vi.fn() },
}));

const info = { rulesFamilies: [{ id: 'srd-5.1', displayName: 'SRD 5.1' }], fields: [{ id: 'initiative', label: 'Initiative' }] } as unknown as AppInfo;
const source = { id: 'a', title: 'Testy Alpha', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

beforeEach(() => {
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.contentRevisions).mockResolvedValue([]);
  vi.mocked(client.previewFormula).mockResolvedValue({ formula: '3', points: [] });
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-m', revisionId: 'x' }, errors: [], warnings: [], canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;
type Other = { name: string; contentId: string; effects: Raw[]; kind?: string };

const focus: Raw = { type: 'resource', id: 'resource-1', resourceId: 'focus', label: 'Testy Focus', maximum: '5' };
const burst: Raw = { type: 'roll', id: 'roll-2', rollId: 'roll-2', label: 'Testy Burst', dice: '2d6' };
const otherPool: Other = { name: 'Testy Other Pool', contentId: 'other-content', effects: [{ type: 'resource', id: 'r', resourceId: 'focus', label: 'Other Focus', maximum: '2' }] };

/** Opens a draft with these rules; `others` are other published entries of the same source. */
async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[], others: Other[] = [], kind = 'feature') {
  const entry = {
    contentId: 'id-m',
    name: 'Testy Mode',
    kind,
    revisions: [],
    latest: { contentId: 'id-m', revisionId: 'r-m', name: 'Testy Mode', kind, status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  const otherEntries = others.map((o) => {
    const revision = { contentId: o.contentId, revisionId: `r-${o.contentId}`, name: o.name, kind: o.kind ?? 'feature', status: 'published', rulesFamilies: ['srd-5.1'], provenance: {}, effects: o.effects };
    return { contentId: o.contentId, name: o.name, kind: o.kind ?? 'feature', revisions: [revision], latest: revision, latestPublished: revision } as unknown as StudioEntry;
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

const blocked = (name: string) => screen.getByRole('button', { name }).getAttribute('aria-disabled') === 'true';

it('the roll editor edits activation, cost, variable cost, the pool, bonus, save, area, range, targets and per-unit dice, and blocks Publish on a bad size', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [focus, burst]);
  const r = rule(/^Rule 2: Roll or action/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Activation' }), 'reaction');
  await user.selectOptions(r.getByRole('combobox', { name: /^Uses a resource/ }), 'focus');
  await user.type(r.getByRole('textbox', { name: /^Cost/ }), '2');
  await user.type(r.getByRole('textbox', { name: /^Bonus/ }), 'PB');
  await user.selectOptions(r.getByRole('combobox', { name: 'Saving throw' }), 'dex');
  const dc = r.getByRole('textbox', { name: /^Save DC/ });
  await user.clear(dc);
  await user.type(dc, '8 + PB + WIS.MOD');
  await user.selectOptions(r.getByRole('combobox', { name: 'Area shape' }), 'line');
  const size = r.getByRole('textbox', { name: /^Area size/ });
  await user.clear(size);
  await user.type(size, '100');
  await user.type(r.getByRole('textbox', { name: /^Line width/ }), '5');
  await user.type(r.getByRole('textbox', { name: /^Range/ }), 'Self');
  await user.type(r.getByRole('textbox', { name: /^Targets/ }), 'Each creature in the line');
  await user.type(r.getByRole('textbox', { name: /^Extra dice for each extra point/ }), '1d6');
  await user.type(r.getByRole('textbox', { name: /^Most extra points/ }), '3');
  expect(blocked('Publish')).toBe(false);
  expect((await checked()).effect('roll-2')).toMatchObject({
    dice: '2d6', activation: 'reaction', resourceId: 'focus', cost: '2', bonus: 'PB', save: { ability: 'dex', dc: '8 + PB + WIS.MOD' },
    area: { shape: 'line', size: 100, width: 5 }, range: 'Self', targets: 'Each creature in the line', perUnit: { dice: '1d6', maxUnits: 3 },
  });

  // A bad size reports a linked error and blocks Save and Publish; nothing wrong is handed up.
  await user.clear(size);
  await user.type(size, '0');
  expect(size.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(size.getAttribute('aria-describedby')!)!.textContent).toMatch(/whole number from 1 to 1000/);
  expect(blocked('Publish')).toBe(true);
  expect(blocked('Save draft')).toBe(true);
  expect(((await checked()).effect('roll-2') as { area: { size: number } }).area.size).toBe(100);
  await user.clear(size);
  await user.type(size, '60');
  expect(blocked('Publish')).toBe(false);
});

it('the variable-cost box clears per-unit dice, and leaving the area or save removes them', async () => {
  const user = userEvent.setup();
  const rolled: Raw = { ...burst, resourceId: 'focus', cost: '2', perUnit: { dice: '1d6' }, save: { ability: 'wis', dc: '10' }, area: { shape: 'line', size: 30, width: 5 } };
  const { rule, checked } = await openDraft(user, [focus, rolled]);
  const r = rule(/^Rule 2: Roll or action/);
  await user.click(r.getByRole('checkbox', { name: /^The player chooses how much to spend/ }));
  expect(r.queryByRole('textbox', { name: /^Extra dice/ })).toBeNull();
  await user.selectOptions(r.getByRole('combobox', { name: 'Area shape' }), 'cone');
  expect(r.queryByRole('textbox', { name: /^Line width/ })).toBeNull();
  await user.selectOptions(r.getByRole('combobox', { name: 'Saving throw' }), '');
  const sent = (await checked()).effect('roll-2') as Raw;
  expect(sent).toMatchObject({ variableCost: true, area: { shape: 'cone', size: 30 } });
  expect(sent.perUnit).toBeUndefined();
  expect(sent.save).toBeUndefined();
  expect((sent.area as Raw).width).toBeUndefined();
});

it('clearing the dice removes the key; it is never an empty string', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [focus, { ...burst, resourceId: 'focus', cost: '1' }]);
  await user.clear(rule(/^Rule 2: Roll or action/).getByRole('textbox', { name: /^Dice/ }));
  const sent = (await checked()).effect('roll-2') as Raw;
  expect(sent.dice).toBeUndefined();
  expect(JSON.stringify(sent)).not.toContain('"dice"');
});

it('clearing the dice also removes the per-unit dice, since they have nothing to add to', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [focus, { ...burst, resourceId: 'focus', cost: '1', perUnit: { dice: '1d6', maxUnits: 2 } }]);
  const r = rule(/^Rule 2: Roll or action/);
  await user.clear(r.getByRole('textbox', { name: /^Dice/ }));
  const sent = (await checked()).effect('roll-2') as Raw;
  expect(sent.dice).toBeUndefined();
  expect(sent.perUnit).toBeUndefined();
  expect((r.getByRole('textbox', { name: /^Extra dice/ }) as HTMLInputElement).value).toBe('');
});

it('pool options are keyed content::id so one id in two entries never collides, and the other entry keeps its content', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [focus, burst], [otherPool]);
  const pool = rule(/^Rule 2: Roll or action/).getByRole('combobox', { name: /^Uses a resource/ }) as HTMLSelectElement;
  expect([...pool.options].map((o) => o.value)).toEqual(['', 'focus', 'other-content::focus']);
  await user.selectOptions(pool, 'other-content::focus');
  expect((await checked()).effect('roll-2')).toMatchObject({ resourceId: 'focus', resourceContent: 'other-content' });
  await user.selectOptions(pool, 'focus');
  const own = (await checked()).effect('roll-2') as Raw;
  expect(own.resourceId).toBe('focus');
  expect(own.resourceContent).toBeUndefined();
  await user.selectOptions(pool, '');
  expect(((await checked()).effect('roll-2') as Raw).resourceId).toBeUndefined();
});

it('an activation select lists Action once, for a roll and for an attack', async () => {
  const user = userEvent.setup();
  const attack: Raw = { type: 'attack', id: 'attack-3', attackId: 'attack1', label: 'Testy Jab', kind: 'melee', abilities: ['str'], damage: { count: '1', die: '6' }, damageType: 'bludgeoning' };
  const { rule } = await openDraft(user, [burst, attack]);
  for (const name of [/^Rule 1: Roll or action/, /^Rule 2: Attack/]) {
    const select = rule(name).getByRole('combobox', { name: 'Activation' }) as HTMLSelectElement;
    expect([...select.options].map((o) => o.textContent)).toEqual(['Action', 'Bonus action', 'Reaction', 'Other']);
  }
  // An absent activation reads as what Play does: a roll is listed under Other, an attack under Action.
  expect((rule(/^Rule 1: Roll or action/).getByRole('combobox', { name: 'Activation' }) as HTMLSelectElement).value).toBe('other');
  expect((rule(/^Rule 2: Attack/).getByRole('combobox', { name: 'Activation' }) as HTMLSelectElement).value).toBe('action');
});

it('removing a resource clears the pool, cost, variable cost and per-unit dice of a roll that used it', async () => {
  const user = userEvent.setup();
  const used: Raw = { ...burst, resourceId: 'focus', cost: '2', variableCost: true, perUnit: { dice: '1d6' } };
  const elsewhere: Raw = { ...burst, id: 'roll-3', rollId: 'roll-3', resourceId: 'focus', resourceContent: 'other-content', cost: '1' };
  const { checked } = await openDraft(user, [focus, used, elsewhere], [otherPool]);
  await user.click(screen.getByRole('button', { name: 'Remove rule 1' }));
  const after = await checked();
  const cleared = after.effect('roll-2') as Raw;
  expect(cleared.dice).toBe('2d6');
  for (const key of ['resourceId', 'resourceContent', 'cost', 'variableCost', 'perUnit']) expect(cleared[key]).toBeUndefined();
  expect(after.effect('roll-3')).toMatchObject({ resourceId: 'focus', resourceContent: 'other-content', cost: '1' });
});

it('a stored value that does not parse blocks Save and Publish on load, before any edit', async () => {
  const user = userEvent.setup();
  const bad: Raw = { ...burst, area: { shape: 'sphere', size: 5000 }, resourceId: 'focus', cost: '1', perUnit: { dice: '1d20', maxUnits: 500 } };
  const { rule } = await openDraft(user, [focus, bad]);
  expect(blocked('Save draft')).toBe(true);
  expect(blocked('Publish')).toBe(true);
  const r = rule(/^Rule 2: Roll or action/);
  expect(r.getByRole('textbox', { name: /^Area size/ }).getAttribute('aria-invalid')).toBe('true');
  expect(r.getByRole('textbox', { name: /^Extra dice/ }).getAttribute('aria-invalid')).toBe('true');
  expect(r.getByRole('textbox', { name: /^Most extra points/ }).getAttribute('aria-invalid')).toBe('true');
});

it('a named value is offered on a class-like entry only, with a key and a formula', async () => {
  const user = userEvent.setup();
  const plain = await openDraft(user, []);
  expect(screen.queryByRole('button', { name: 'Add named value' })).toBeNull();
  expect(plain.rule).toBeTruthy();
  cleanup();

  const { rule, checked } = await openDraft(user, [], [], 'subclass');
  await user.click(screen.getByRole('button', { name: 'Add named value' }));
  await user.click(screen.getByRole('button', { name: 'Add named value' }));
  const v = rule(/^Rule 1: Named value/);
  const key = v.getByRole('textbox', { name: /^Key/ });
  expect((key as HTMLInputElement).value).toBe('value1');
  await user.clear(key);
  await user.type(key, 'surgeDice');
  const formula = v.getByRole('textbox', { name: /^Formula/ });
  await user.clear(formula);
  await user.type(formula, 'CLASS_LEVEL / 2');
  const sent = await checked();
  expect(sent.effect('value-1')).toMatchObject({ type: 'value', valueId: 'surgeDice', formula: 'CLASS_LEVEL / 2' });
  expect(sent.effect('value-2')).toMatchObject({ valueId: 'value2' });
  expect(blocked('Save draft')).toBe(false);

  // A key that breaks the scale-key rule is marked and blocks saving.
  await user.clear(key);
  await user.type(key, '9lives');
  expect(key.getAttribute('aria-invalid')).toBe('true');
  expect(blocked('Save draft')).toBe(true);
  await user.clear(key);
  await user.type(key, 'lives');
  expect(blocked('Save draft')).toBe(false);
});

// D43 slice 8: a roll can also change other pools, and draw down a pool that counts up.
const strain: Raw = { type: 'resource', id: 'resource-3', resourceId: 'strain', label: 'Testy Strain', maximum: '4', counts: 'up' };
const charge: Raw = { type: 'resource', id: 'resource-4', resourceId: 'charge', label: 'Testy Charge', maximum: '6', counts: 'up' };
const gather: Raw = { ...burst, id: 'roll-5', rollId: 'roll-5', label: 'Testy Gather', resourceId: 'focus', cost: '1' };

it('the roll editor edits alsoChange rows and blocks drawsDown on a pool that doesn\'t count up', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [focus, strain, gather]);
  const r = rule(/^Rule 3: Roll or action/);

  // The roll's own pool is not offered to change; the rows save in the stored shape.
  expect(r.queryByRole('group', { name: 'Also changes' })).not.toBeNull();
  await user.click(r.getByRole('button', { name: 'Add change' }));
  const pool = r.getByRole('combobox', { name: 'Change 1: pool' }) as HTMLSelectElement;
  expect(document.activeElement).toBe(pool); // Add puts focus on the new row's pool
  expect([...pool.options].map((o) => o.value)).toEqual(['', 'strain']);
  await user.selectOptions(pool, 'strain');
  await user.selectOptions(r.getByRole('combobox', { name: 'Change 1: spend or regain' }), 'regain');
  const amount = r.getByRole('textbox', { name: /^Change 1: amount/ });
  await user.clear(amount);
  await user.type(amount, '2');
  expect((await checked()).effect('roll-5')).toMatchObject({ alsoChange: [{ resourceId: 'strain', change: 'regain', amount: '2' }] });

  // At most four rows: the fifth cannot be added, and says why.
  for (let i = 0; i < 3; i++) await user.click(r.getByRole('button', { name: 'Add change' }));
  const add = r.getByRole('button', { name: 'Add change' });
  expect(add.getAttribute('aria-disabled')).toBe('true');
  expect(document.getElementById(add.getAttribute('aria-describedby')!)!.textContent).toMatch(/at most 4/);
  await user.click(add);
  expect(r.queryByRole('combobox', { name: 'Change 5: pool' })).toBeNull();
  expect(((await checked()).effect('roll-5') as { alsoChange: unknown[] }).alsoChange).toHaveLength(4);

  // Removing a row keeps focus in the list; removing the last one saves the key as absent.
  await user.click(r.getByRole('button', { name: 'Remove change 1' }));
  expect(document.activeElement).toBe(r.getByRole('combobox', { name: 'Change 1: pool' }));
  for (let i = 0; i < 3; i++) await user.click(r.getByRole('button', { name: 'Remove change 1' }));
  expect(document.activeElement).toBe(r.getByRole('button', { name: 'Add change' }));
  expect(((await checked()).effect('roll-5') as Raw).alsoChange).toBeUndefined();

  // A pool that does not count up cannot be drawn down, and the reason is linked.
  const box = r.getByRole('checkbox', { name: /^Draws down built-up charge/ }) as HTMLInputElement;
  expect(box.disabled).toBe(true);
  expect(document.getElementById(box.getAttribute('aria-describedby')!)!.textContent).toBe('Only for a pool that counts up');
  expect(blocked('Save draft')).toBe(false);
});

it('drawsDown is saved on a pool that counts up, absent when unticked, and changing the pool resets it and the rows', async () => {
  const user = userEvent.setup();
  const release: Raw = { ...gather, resourceId: 'strain', cost: '1', alsoChange: [{ resourceId: 'charge', change: 'spend', amount: '1' }] };
  const { rule, checked } = await openDraft(user, [focus, strain, charge, release]);
  const r = rule(/^Rule 4: Roll or action/);
  const box = r.getByRole('checkbox', { name: /^Draws down built-up charge/ }) as HTMLInputElement;
  expect(box.disabled).toBe(false);
  await user.click(box);
  expect(((await checked()).effect('roll-5') as Raw).drawsDown).toBe(true);
  await user.click(box);
  const off = (await checked()).effect('roll-5') as Raw;
  expect(off.drawsDown).toBeUndefined();
  expect(JSON.stringify(off)).not.toContain('drawsDown');
  await user.click(box);
  await user.selectOptions(r.getByRole('combobox', { name: /^Uses a resource/ }), 'focus');
  const reset = (await checked()).effect('roll-5') as Raw;
  expect(reset.drawsDown).toBeUndefined();
  expect(reset.alsoChange).toBeUndefined();
  await user.selectOptions(r.getByRole('combobox', { name: /^Uses a resource/ }), '');
  expect(((await checked()).effect('roll-5') as Raw).alsoChange).toBeUndefined();
});

it('removing a pool drops the alsoChange rows that name it, and a stored drawsDown on a normal pool blocks Save on load', async () => {
  const user = userEvent.setup();
  const rows = [
    { resourceId: 'strain', change: 'spend', amount: '1' },
    { resourceId: 'focus', change: 'regain', amount: '1' },
    { resourceId: 'strain', resourceContent: 'other-content', change: 'spend', amount: '1' },
  ];
  const { checked } = await openDraft(user, [focus, strain, { ...gather, resourceId: 'charge', alsoChange: rows }, charge], [{ ...otherPool, effects: [{ ...strain, id: 'r2' }] }]);
  await user.click(screen.getByRole('button', { name: 'Remove rule 2' }));
  expect(((await checked()).effect('roll-5') as { alsoChange: unknown[] }).alsoChange).toEqual([rows[1], rows[2]]);
  cleanup();

  const stored = await openDraft(user, [focus, { ...gather, drawsDown: true }]);
  expect(blocked('Save draft')).toBe(true);
  expect(blocked('Publish')).toBe(true);
  const box = stored.rule(/^Rule 2: Roll or action/).getByRole('checkbox', { name: /^Draws down built-up charge/ });
  expect(box.getAttribute('aria-invalid')).toBe('true');
});

it('a roll that also changes a pool or draws down cannot be variable-cost, as with per-unit dice', async () => {
  const user = userEvent.setup();
  const variable: Raw = { ...gather, resourceId: 'charge', variableCost: true };
  const { rule, checked } = await openDraft(user, [focus, strain, charge, variable]);
  const r = rule(/^Rule 4: Roll or action/);
  const box = () => r.getByRole('checkbox', { name: /^The player chooses how much to spend/ }) as HTMLInputElement;

  // Drawing down clears the variable cost.
  expect(box().checked).toBe(true);
  await user.click(r.getByRole('checkbox', { name: /^Draws down built-up charge/ }));
  expect(((await checked()).effect('roll-5') as Raw).variableCost).toBeUndefined();
  expect(r.queryByRole('checkbox', { name: /^The player chooses how much to spend/ })).toBeNull();

  // Untick, tick the variable cost, then add a change: the change clears it again.
  await user.click(r.getByRole('checkbox', { name: /^Draws down built-up charge/ }));
  expect(box().checked).toBe(false);
  await user.click(box());
  await user.click(r.getByRole('button', { name: 'Add change' }));
  expect(((await checked()).effect('roll-5') as Raw).variableCost).toBeUndefined();
  expect(r.queryByRole('checkbox', { name: /^The player chooses how much to spend/ })).toBeNull();
});

it('a named value has no Automation choice: it is always calculated', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ type: 'value', id: 'value-1', valueId: 'surgeDice', formula: '1' }, { ...burst, id: 'roll-2' }], [], 'subclass');
  expect(rule(/^Rule 1: Named value/).queryByRole('combobox', { name: 'Automation' })).toBeNull();
  expect(rule(/^Rule 2: Roll or action/).getByRole('combobox', { name: 'Automation' })).toBeTruthy();
});

// MECH-X item 1 (T5): pay with hit points or a spell slot, cast a spell without a slot.
const fancy: Raw = { ...burst, resourceId: 'focus', cost: '2', drawsDown: true, alsoChange: [{ resourceId: 'focus', change: 'spend', amount: '1' }], variableCost: true, automation: 'manual', text: 'Fixture note', flair: { keep: 1 } };

it('a roll pays with hit points: the pool keys go, the cost and unrelated keys stay, and a pool again removes payment', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [focus, fancy]);
  const r = rule(/^Rule 2: Roll or action/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Pays with' }), 'hitPoints');
  expect(r.queryByRole('combobox', { name: /^Uses a resource/ })).toBeNull();
  const sent = (await checked()).effect('roll-2') as Raw;
  expect(sent.payment).toEqual({ kind: 'hitPoints' });
  for (const key of ['resourceId', 'resourceContent', 'drawsDown', 'alsoChange', 'variableCost']) expect(sent[key]).toBeUndefined();
  expect(sent).toMatchObject({ cost: '2', dice: '2d6', automation: 'manual', text: 'Fixture note', flair: { keep: 1 } });
  expect((r.getByRole('textbox', { name: /^Hit points paid/ }) as HTMLInputElement).value).toBe('2');

  await user.click(r.getByRole('checkbox', { name: 'May drop to 0 hit points' }));
  expect(((await checked()).effect('roll-2') as Raw).payment).toEqual({ kind: 'hitPoints', mayDropToZero: true });

  await user.selectOptions(r.getByRole('combobox', { name: 'Pays with' }), 'resource');
  const back = (await checked()).effect('roll-2') as Raw;
  expect('payment' in back).toBe(false);
  expect(back).toMatchObject({ cost: '2', automation: 'manual', flair: { keep: 1 } });
  expect(r.getByRole('combobox', { name: /^Uses a resource/ })).toBeTruthy();
});

it('a roll pays a spell slot: cost and per-unit dice go, the lowest level is written only above 1', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [focus, { ...fancy, perUnit: { dice: '1d6' } }]);
  const r = rule(/^Rule 2: Roll or action/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Pays with' }), 'slot');
  const sent = (await checked()).effect('roll-2') as Raw;
  expect(sent.payment).toEqual({ kind: 'slot' });
  for (const key of ['resourceId', 'cost', 'perUnit', 'drawsDown', 'alsoChange', 'variableCost']) expect(sent[key]).toBeUndefined();
  expect(sent).toMatchObject({ text: 'Fixture note', flair: { keep: 1 } });
  expect(r.queryByRole('checkbox', { name: 'May drop to 0 hit points' })).toBeNull();

  await user.selectOptions(r.getByRole('combobox', { name: 'Lowest slot level' }), '3');
  expect(((await checked()).effect('roll-2') as Raw).payment).toEqual({ kind: 'slot', minSlotLevel: 3 });
  await user.selectOptions(r.getByRole('combobox', { name: 'Lowest slot level' }), '');
  expect(((await checked()).effect('roll-2') as Raw).payment).toEqual({ kind: 'slot' });
});

it('a roll casts a spell of the source, and the picker is off while it pays a slot', async () => {
  const user = userEvent.setup();
  const spark: Other = { name: 'Fixture Spark', contentId: 'spark-id', kind: 'spell', effects: [{ type: 'spell', id: 'spell', level: 2 }] };
  const { rule, checked } = await openDraft(user, [burst], [spark]);
  const r = rule(/^Rule 1: Roll or action/);
  const picker = r.getByRole('combobox', { name: /^Casts a spell/ }) as HTMLSelectElement;
  expect([...picker.options].map((o) => o.textContent)).toEqual(['No spell', 'Fixture Spark (level 2)']);
  await user.selectOptions(picker, 'spark-id');
  expect(((await checked()).effect('roll-2') as Raw).castsSpell).toBe('spark-id');
  // With a spell chosen, a slot cannot be chosen (the validator refuses both).
  expect((r.getByRole('option', { name: 'A spell slot' }) as HTMLOptionElement).disabled).toBe(true);
  await user.selectOptions(picker, '');
  expect(((await checked()).effect('roll-2') as Raw).castsSpell).toBeUndefined();
  await user.selectOptions(r.getByRole('combobox', { name: 'Pays with' }), 'slot');
  expect((r.getByRole('combobox', { name: /^Casts a spell/ }) as HTMLSelectElement).disabled).toBe(true);
  expect(r.getByText(/cannot also cast a spell without one/)).toBeTruthy();
});

it('a stored payment of an unknown kind shows read-only and blocks Save', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ ...burst, payment: { kind: 'mystery' } }]);
  const r = rule(/^Rule 1: Roll or action/);
  expect(r.getByText(/This payment doesn't read/)).toBeTruthy();
  expect(r.queryByRole('combobox', { name: 'Pays with' })).toBeNull();
  await waitFor(() => expect(blocked('Save draft')).toBe(true));
});

it('a toggle pays with hit points or a slot and keeps what the editor does not touch', async () => {
  const user = userEvent.setup();
  const toggle: Raw = { type: 'toggle', id: 'toggle-2', toggleId: 'stance', label: 'Testy Stance', resourceId: 'focus', cost: '1', text: 'Fixture note', flair: 7 };
  const { rule, checked } = await openDraft(user, [focus, toggle]);
  const t = rule(/^Rule 2: Toggle/);
  await user.selectOptions(t.getByRole('combobox', { name: 'Pays with' }), 'hitPoints');
  expect(t.queryByRole('combobox', { name: 'Turning it on spends' })).toBeNull();
  let sent = (await checked()).effect('toggle-2') as Raw;
  expect(sent.payment).toEqual({ kind: 'hitPoints' });
  expect(sent.resourceId).toBeUndefined();
  expect(sent).toMatchObject({ cost: '1', text: 'Fixture note', flair: 7 });
  expect(t.getByRole('textbox', { name: /^Hit points paid/ })).toBeTruthy();

  await user.selectOptions(t.getByRole('combobox', { name: 'Pays with' }), 'slot');
  await user.selectOptions(t.getByRole('combobox', { name: 'Lowest slot level' }), '3');
  sent = (await checked()).effect('toggle-2') as Raw;
  expect(sent.payment).toEqual({ kind: 'slot', minSlotLevel: 3 });
  expect(sent.cost).toBeUndefined();
  expect(sent).toMatchObject({ text: 'Fixture note', flair: 7 });

  await user.selectOptions(t.getByRole('combobox', { name: 'Pays with' }), 'resource');
  expect('payment' in ((await checked()).effect('toggle-2') as Raw)).toBe(false);
  expect(t.getByRole('combobox', { name: 'Turning it on spends' })).toBeTruthy();
});
