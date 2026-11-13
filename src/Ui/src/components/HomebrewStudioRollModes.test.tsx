// @vitest-environment jsdom
// D43 slice 6 (Task 6.5b): the roll mode editor and the damage defence editor in the studio.
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
  vi.mocked(client.validateRevision).mockResolvedValue({ revision: { contentId: 'id-m', revisionId: 'x' }, errors: [], warnings: [], requiredSchemaVersion: 12, canPublish: true });
});
afterEach(cleanup);

type Raw = Record<string, unknown>;

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[]) {
  const entry = {
    contentId: 'id-m',
    name: 'Testy Mode',
    kind: 'feature',
    revisions: [],
    latest: { contentId: 'id-m', revisionId: 'r-m', name: 'Testy Mode', kind: 'feature', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Mode' }));
  const rule = (name: RegExp) => within(screen.getByRole('group', { name }));
  const checked = async () => {
    vi.mocked(client.validateRevision).mockClear();
    await user.click(screen.getByRole('button', { name: 'Check' }));
    await waitFor(() => expect(vi.mocked(client.validateRevision).mock.calls.length).toBeGreaterThan(0));
    const sent = vi.mocked(client.validateRevision).mock.calls.at(-1)![0] as unknown as { effects: Raw[] };
    return { effect: (id: string) => sent.effects.find((e) => e.id === id) as Raw, count: sent.effects.length };
  };
  return { rule, checked };
}

const blocked = (name: string) => screen.getByRole('button', { name }).getAttribute('aria-disabled') === 'true';

const heavy: Raw = { type: 'toggle', id: 'toggle-1', toggleId: 'heavy', label: 'Testy Heavy' };

it('the roll mode editor writes target, mode, against and toggle', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [heavy]);
  await user.click(screen.getByRole('button', { name: 'Add roll mode' }));
  const r = rule(/^Rule 2: Roll mode/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'save.dex');
  await user.selectOptions(r.getByRole('combobox', { name: 'Mode' }), 'disadvantage');
  await user.click(r.getByRole('checkbox', { name: 'Paralyzed' }));
  await user.click(r.getByRole('checkbox', { name: 'Stunned' }));
  await user.selectOptions(r.getByRole('combobox', { name: 'Only while' }), 'heavy');
  expect((await checked()).effect('rollMode-2')).toMatchObject({ type: 'rollMode', target: 'save.dex', mode: 'disadvantage', against: ['paralyzed', 'stunned'], toggle: 'heavy' });

  // The three groups: saves, skills (labelled), ability checks.
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'skill.sleightOfHand');
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'ability.str.check');
  expect((await checked()).effect('rollMode-2')).toMatchObject({ target: 'ability.str.check' });
  expect(r.getByRole('option', { name: 'Sleight of Hand' })).toBeTruthy();
  // No Normal mode is offered.
  expect(r.queryByRole('option', { name: /normal/i })).toBeNull();
  expect(blocked('Make available to characters')).toBe(false);
});

it('clearing every condition removes against', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'save.wis', mode: 'advantage', against: ['charmed'] };
  const { rule, checked } = await openDraft(user, [mode]);
  const r = rule(/^Rule 1: Roll mode/);
  expect(r.getByRole('checkbox', { name: 'Charmed' })).toHaveProperty('checked', true);
  await user.click(r.getByRole('checkbox', { name: 'Charmed' }));
  const sent = (await checked()).effect('rollMode-1');
  expect(sent.against).toBeUndefined();
  expect(sent).toMatchObject({ target: 'save.wis', mode: 'advantage' });
});

it('the roll mode editor blocks Publish on an unknown condition key', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'save.wis', mode: 'advantage', against: ['charmed', 'testyCondition'] };
  const { rule } = await openDraft(user, [mode]);
  const r = rule(/^Rule 1: Roll mode/);
  expect(blocked('Make available to characters')).toBe(true);
  expect(blocked('Save draft')).toBe(true);
  expect(r.getByText(/testyCondition/)).toBeTruthy();
  // Unchecking the unknown key fixes it.
  await user.click(r.getByRole('checkbox', { name: 'testyCondition' }));
  expect(blocked('Make available to characters')).toBe(false);
});

it('the roll mode editor blocks Publish on an unknown target', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'save.testy', mode: 'advantage' };
  const { rule } = await openDraft(user, [mode]);
  expect(blocked('Make available to characters')).toBe(true);
  await user.selectOptions(rule(/^Rule 1: Roll mode/).getByRole('combobox', { name: 'Applies to' }), 'save.con');
  expect(blocked('Make available to characters')).toBe(false);
});

// Content v12 (G7): the combat targets.
it('edits attack target with tags and ability', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'save.wis', mode: 'advantage', against: ['charmed'] };
  const { rule, checked } = await openDraft(user, [mode]);
  const r = rule(/^Rule 1: Roll mode/);
  expect(r.getByRole('group', { name: 'Combat' })).toBeTruthy();
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'attack');
  // Switching to attack defaults the tags to all; the conditions list stays (attack allows it).
  const tags = r.getByRole('textbox', { name: 'Attack tags' });
  expect(tags).toHaveProperty('value', 'all');
  await user.clear(tags);
  await user.type(tags, 'melee, ranged, melee, ');
  await user.selectOptions(r.getByRole('combobox', { name: 'Ability' }), 'dex');
  await user.selectOptions(r.getByRole('combobox', { name: 'Unless' }), 'prone');
  expect((await checked()).effect('rollMode-1')).toMatchObject({ target: 'attack', tags: ['melee', 'ranged'], ability: 'dex', unlessCondition: 'prone', against: ['charmed'] });
  await user.selectOptions(r.getByRole('combobox', { name: 'Ability' }), '');
  await user.selectOptions(r.getByRole('combobox', { name: 'Unless' }), '');
  const sent = (await checked()).effect('rollMode-1');
  expect(sent.ability).toBeUndefined();
  expect(sent.unlessCondition).toBeUndefined();
  // No tags blocks publishing.
  await user.clear(r.getByRole('textbox', { name: 'Attack tags' }));
  expect(blocked('Make available to characters')).toBe(true);
  await user.type(r.getByRole('textbox', { name: 'Attack tags' }), 'all');
  expect(blocked('Make available to characters')).toBe(false);
});

it('edits attacks against you with prevent and unless', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'initiative', mode: 'advantage' };
  const { rule, checked } = await openDraft(user, [mode]);
  const r = rule(/^Rule 1: Roll mode/);
  // Initiative: no conditions list, no tags.
  expect(r.queryByRole('group', { name: /Only against conditions/ })).toBeNull();
  expect(r.queryByRole('textbox', { name: 'Attack tags' })).toBeNull();
  await user.selectOptions(r.getByRole('combobox', { name: 'Applies to' }), 'attacksAgainst');
  await user.selectOptions(r.getByRole('combobox', { name: 'Mode' }), 'disadvantage');
  await user.click(r.getByRole('checkbox', { name: "Can't have this" }));
  await user.selectOptions(r.getByRole('combobox', { name: 'Unless' }), 'incapacitated');
  expect((await checked()).effect('rollMode-1')).toMatchObject({ target: 'attacksAgainst', mode: 'disadvantage', prevent: true, unlessCondition: 'incapacitated' });
  await user.click(r.getByRole('checkbox', { name: "Can't have this" }));
  expect((await checked()).effect('rollMode-1').prevent).toBeUndefined();
});

it('changing target clears members it no longer allows', async () => {
  const user = userEvent.setup();
  const attack: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'attack', mode: 'advantage', tags: ['melee'], ability: 'str', unlessCondition: 'prone', against: ['charmed'] };
  const prevent: Raw = { type: 'rollMode', id: 'rollMode-2', target: 'attacksAgainst', mode: 'advantage', prevent: true };
  const { rule, checked } = await openDraft(user, [attack, prevent]);
  await user.selectOptions(within(screen.getByRole('group', { name: /^Rule 1: Roll mode/ })).getByRole('combobox', { name: 'Applies to' }), 'initiative');
  await user.selectOptions(rule(/^Rule 2: Roll mode/).getByRole('combobox', { name: 'Applies to' }), 'save.dex');
  const done = await checked();
  const first = done.effect('rollMode-1');
  expect(first).toMatchObject({ target: 'initiative', unlessCondition: 'prone' }); // unless stays: any target allows it
  expect(first.tags).toBeUndefined();
  expect(first.ability).toBeUndefined();
  expect(first.against).toBeUndefined();
  const second = done.effect('rollMode-2');
  expect(second.target).toBe('save.dex');
  expect(second.prevent).toBeUndefined();
  // And into attacksAgainst: against, tags and ability go too.
  await user.selectOptions(within(screen.getByRole('group', { name: /^Rule 1: Roll mode/ })).getByRole('combobox', { name: 'Applies to' }), 'attack');
  await user.selectOptions(within(screen.getByRole('group', { name: /^Rule 1: Roll mode/ })).getByRole('combobox', { name: 'Applies to' }), 'attacksAgainst');
  const back = (await checked()).effect('rollMode-1');
  expect(back.tags).toBeUndefined();
  expect(back.ability).toBeUndefined();
});

it('keeps unknown members across an edit', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'attack', mode: 'advantage', tags: ['melee'], ability: 'testyAbility', unlessCondition: 'testyCondition', futureMember: { keep: true } };
  const { rule, checked } = await openDraft(user, [mode]);
  const r = rule(/^Rule 1: Roll mode/);
  // The stored values the selects cannot offer are shown, not dropped, and block publishing until fixed.
  expect(r.getByRole('option', { name: 'testyAbility (removed)' })).toBeTruthy();
  expect(r.getByRole('option', { name: 'testyCondition (removed)' })).toBeTruthy();
  expect(blocked('Make available to characters')).toBe(true);
  await user.selectOptions(r.getByRole('combobox', { name: 'Mode' }), 'disadvantage');
  expect((await checked()).effect('rollMode-1')).toMatchObject({ mode: 'disadvantage', tags: ['melee'], ability: 'testyAbility', unlessCondition: 'testyCondition', futureMember: { keep: true } });
});

it('reports and clears a member that doesn\'t apply to the target', async () => {
  const user = userEvent.setup();
  const a: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'initiative', mode: 'advantage', against: ['charmed'] };
  const b: Raw = { type: 'rollMode', id: 'rollMode-2', target: 'save.dex', mode: 'advantage', ability: 'dex' };
  const c: Raw = { type: 'rollMode', id: 'rollMode-3', target: 'attack', mode: 'advantage', tags: ['all'], prevent: true };
  const { rule, checked } = await openDraft(user, [a, b, c]);
  expect(blocked('Make available to characters')).toBe(true);
  await user.click(rule(/^Rule 1: Roll mode/).getByRole('button', { name: 'Remove against' }));
  expect(blocked('Make available to characters')).toBe(true);
  await user.click(rule(/^Rule 2: Roll mode/).getByRole('button', { name: 'Remove ability' }));
  expect(blocked('Make available to characters')).toBe(true);
  await user.click(rule(/^Rule 3: Roll mode/).getByRole('button', { name: 'Remove prevent' }));
  expect(blocked('Make available to characters')).toBe(false);
  const done = await checked();
  expect(done.effect('rollMode-1').against).toBeUndefined();
  expect(done.effect('rollMode-2').ability).toBeUndefined();
  expect(done.effect('rollMode-3').prevent).toBeUndefined();
});

it('reports stored tags that are blank or repeated', async () => {
  const user = userEvent.setup();
  const mode: Raw = { type: 'rollMode', id: 'rollMode-1', target: 'attack', mode: 'advantage', tags: ['melee', 'melee', ' '] };
  const { rule, checked } = await openDraft(user, [mode]);
  expect(blocked('Make available to characters')).toBe(true);
  await user.type(rule(/^Rule 1: Roll mode/).getByRole('textbox', { name: 'Attack tags' }), 'x');
  expect(blocked('Make available to characters')).toBe(false);
  expect((await checked()).effect('rollMode-1').tags).toEqual(['melee', 'x']);
});

it('a damage defence grant can apply only while a form is on', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [heavy]);
  await user.click(screen.getByRole('button', { name: 'Add damage defence' }));
  const r = rule(/^Rule 2: Damage defence/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Kind' }), 'damageImmunity');
  await user.selectOptions(r.getByRole('combobox', { name: 'Damage type' }), 'fire');
  await user.selectOptions(r.getByRole('combobox', { name: 'Only while' }), 'heavy');
  expect((await checked()).effect('grant-2')).toMatchObject({ type: 'grant', grant: 'damageImmunity', damageType: 'fire', whileToggle: { toggleId: 'heavy' } });
  await user.selectOptions(r.getByRole('combobox', { name: 'Only while' }), '');
  const cleared = (await checked()).effect('grant-2');
  expect(cleared.whileToggle).toBeUndefined();
  expect(cleared).toMatchObject({ grant: 'damageImmunity', damageType: 'fire' });
});
