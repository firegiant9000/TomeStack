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
type Other = { name: string; contentId: string; effects: Raw[] };

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
