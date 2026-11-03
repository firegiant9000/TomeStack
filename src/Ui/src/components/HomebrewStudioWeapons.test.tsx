// @vitest-environment jsdom
// MECH-X item 0: the studio weapon editor (content v12 weapon.bonus).
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

const blade: Raw = { type: 'weapon', id: 'weapon-1', category: 'simple', attack: 'melee', damage: '1d6', damageType: 'slashing', properties: ['light', 'finesse'], weaponKey: 'blade' };

/** Opens a draft of this kind with these rules. */
async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[], kind = 'item') {
  const entry = {
    contentId: 'id-m',
    name: 'Testy Blade',
    kind,
    revisions: [],
    latest: { contentId: 'id-m', revisionId: 'r-m', name: 'Testy Blade', kind, status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Blade' }));
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

it('adds a weapon to an item and edits every field', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, []);
  await user.click(screen.getByRole('button', { name: 'Add weapon' }));
  const w = rule(/^Rule 1: Weapon/);
  // The starter has no key yet, so it blocks until named.
  expect(blocked('Save draft')).toBe(true);
  await user.type(w.getByRole('textbox', { name: /^Weapon key/ }), 'testyblade');
  await user.selectOptions(w.getByRole('combobox', { name: 'Category' }), 'martial');
  await user.selectOptions(w.getByRole('combobox', { name: 'Attack' }), 'ranged');
  const damage = w.getByRole('textbox', { name: /^Damage \(/ });
  await user.clear(damage);
  await user.type(damage, '1d8+1');
  const type = w.getByRole('textbox', { name: /^Damage type/ });
  await user.clear(type);
  await user.type(type, 'piercing');
  await user.type(w.getByRole('textbox', { name: /^Properties/ }), 'light, finesse');
  await user.type(w.getByRole('textbox', { name: /^Versatile/ }), 'd10');
  await user.type(w.getByRole('textbox', { name: /^Range/ }), '80/320');
  await user.type(w.getByRole('textbox', { name: /^Mastery/ }), 'testmastery');
  await user.type(w.getByRole('textbox', { name: /^Bonus/ }), '1');
  expect(blocked('Save draft')).toBe(false);
  expect((await checked()).effect('weapon-1')).toMatchObject({
    type: 'weapon', category: 'martial', attack: 'ranged', damage: '1d8+1', damageType: 'piercing', properties: ['light', 'finesse'], versatile: 'd10', range: '80/320',
    weaponKey: 'testyblade', mastery: 'testmastery', bonus: 1,
  });
  // Every weapon field typed: it ran just past the default 5 s on a loaded machine (MECH-X item 2 gate), alone and in the full suite.
}, 20_000);

it('a blank or zero bonus sends no bonus key', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...blade, bonus: 2 }]);
  const bonus = rule(/^Rule 1: Weapon/).getByRole('textbox', { name: /^Bonus/ });
  expect((bonus as HTMLInputElement).value).toBe('2');
  await user.clear(bonus);
  expect('bonus' in (await checked()).effect('weapon-1')!).toBe(false);
  await user.type(bonus, '0');
  expect('bonus' in (await checked()).effect('weapon-1')!).toBe(false);
  expect(blocked('Save draft')).toBe(false);
});

it.each([
  ['Bonus', '11'],
  ['Bonus', '1.5'],
  ['Bonus', '-1'],
  ['Bonus', '+2'],
  ['Bonus', '2a'],
  ['Damage \\(', '1d'],
  ['Damage \\(', 'x'],
  ['Properties', 'light,,finesse'],
  ['Weapon key', ' '],
])('bad input in %s (%j) blocks Save and Publish with a linked error, and fixing it unblocks', async (label, text) => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [blade]);
  const field = rule(/^Rule 1: Weapon/).getByRole('textbox', { name: new RegExp(`^${label}`) });
  const before = (field as HTMLInputElement).value;
  await user.clear(field);
  await user.type(field, text);
  expect(field.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(field.getAttribute('aria-describedby')!)!.textContent).toBeTruthy();
  expect(blocked('Save draft')).toBe(true);
  expect(blocked('Publish')).toBe(true);
  expect(document.getElementById('editor-blocked')!.textContent).toMatch(/Fix the marked field before saving/);
  await user.clear(field);
  if (before) await user.type(field, before);
  else await user.type(field, '1');
  expect(field.hasAttribute('aria-invalid')).toBe(false);
  expect(blocked('Save draft')).toBe(false);
  expect(blocked('Publish')).toBe(false);
});

it('keeps fields it does not edit, in their order', async () => {
  const user = userEvent.setup();
  const stored = { ...blade, properties: ['finesse', 'light'], automation: 'assisted', text: 'Testy note', zz: { keep: 1 } };
  const { rule, checked } = await openDraft(user, [stored]);
  await user.type(rule(/^Rule 1: Weapon/).getByRole('textbox', { name: /^Bonus/ }), '3');
  const sent = (await checked()).effect('weapon-1');
  expect(sent).toMatchObject({ automation: 'assisted', text: 'Testy note', zz: { keep: 1 }, properties: ['finesse', 'light'], bonus: 3 });
});

it('has no weapon button on content that is not an item', async () => {
  const user = userEvent.setup();
  await openDraft(user, [], 'feature');
  expect(screen.queryByRole('button', { name: 'Add weapon' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Add modifier' })).toBeTruthy();
});

it('a stored legal weapon does not block Save', async () => {
  const user = userEvent.setup();
  await openDraft(user, [{ ...blade, damage: '1d8+1', versatile: 'd10', range: 'reach', bonus: '2' }]);
  expect(blocked('Save draft')).toBe(false);
  expect(blocked('Publish')).toBe(false);
});

it('an unreadable weapon shows a notice, not fields, and can still be removed', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ ...blade, properties: 'light' }]);
  const w = rule(/^Rule 1: Weapon/);
  expect(w.getByText("This weapon doesn't read, so it can't be edited here.")).toBeTruthy();
  expect(w.queryByRole('textbox', { name: /^Bonus/ })).toBeNull();
  await user.click(w.getByRole('button', { name: /^Remove rule 1/ }));
  expect(screen.queryByRole('group', { name: /^Rule 1/ })).toBeNull();
});

it('a stored non-number bonus shows as it is and blocks', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ ...blade, bonus: 'x' }]);
  const bonus = rule(/^Rule 1: Weapon/).getByRole('textbox', { name: /^Bonus/ }) as HTMLInputElement;
  expect(bonus.value).toBe('x');
  expect(bonus.getAttribute('aria-invalid')).toBe('true');
  expect(blocked('Save draft')).toBe(true);
  expect(blocked('Publish')).toBe(true);
});

it.each(['', '  ', ' 2', '1.5', '11'])('a stored string bonus %j the service cannot read shows as it is and blocks', async (stored) => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ ...blade, bonus: stored }]);
  const bonus = rule(/^Rule 1: Weapon/).getByRole('textbox', { name: /^Bonus/ }) as HTMLInputElement;
  expect(bonus.value).toBe(stored);
  expect(bonus.getAttribute('aria-invalid')).toBe('true');
  expect(blocked('Save draft')).toBe(true);
  expect(blocked('Publish')).toBe(true);
  // Clearing what is there to nothing is a way out once something was typed.
  await user.type(bonus, '3');
  await user.clear(bonus);
  expect(bonus.hasAttribute('aria-invalid')).toBe(false);
  expect(blocked('Save draft')).toBe(false);
});

it('a stored string bonus "2" shows 2, does not block, and retyping sends the number', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [{ ...blade, bonus: '2' }]);
  const bonus = rule(/^Rule 1: Weapon/).getByRole('textbox', { name: /^Bonus/ }) as HTMLInputElement;
  expect(bonus.value).toBe('2');
  expect(bonus.hasAttribute('aria-invalid')).toBe(false);
  expect(blocked('Save draft')).toBe(false);
  await user.clear(bonus);
  await user.type(bonus, '3');
  expect((await checked()).effect('weapon-1')!.bonus).toBe(3);
});

it('a differently-cased bonus key gives the notice, not fields', async () => {
  const user = userEvent.setup();
  const { rule } = await openDraft(user, [{ ...blade, Bonus: 2 }]);
  const w = rule(/^Rule 1: Weapon/);
  expect(w.getByText("This weapon doesn't read, so it can't be edited here.")).toBeTruthy();
  expect(w.queryByRole('textbox', { name: /^Bonus/ })).toBeNull();
});

it('clearing properties adds no key when the weapon had none, and writes [] when it had the key', async () => {
  const user = userEvent.setup();
  const bare: Raw = { ...blade };
  delete bare.properties;
  const none = await openDraft(user, [bare]);
  const field = none.rule(/^Rule 1: Weapon/).getByRole('textbox', { name: /^Properties/ });
  await user.type(field, 'light');
  expect((await none.checked()).effect('weapon-1')!.properties).toEqual(['light']);
  await user.clear(field);
  expect('properties' in (await none.checked()).effect('weapon-1')!).toBe(false);
  cleanup();
  const had = await openDraft(user, [blade]);
  await user.clear(had.rule(/^Rule 1: Weapon/).getByRole('textbox', { name: /^Properties/ }));
  expect((await had.checked()).effect('weapon-1')!.properties).toEqual([]);
});
