// @vitest-environment jsdom
// D43 S7 (Task 7.6b): the damage response editor in the studio.
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

async function openDraft(user: ReturnType<typeof userEvent.setup>, effects: Raw[]) {
  const entry = {
    contentId: 'id-m',
    name: 'Testy Response',
    kind: 'feature',
    revisions: [],
    latest: { contentId: 'id-m', revisionId: 'r-m', name: 'Testy Response', kind: 'feature', status: 'draft', rulesFamilies: ['srd-5.1'], provenance: {}, effects },
  } as unknown as StudioEntry;
  vi.mocked(client.contentBySource).mockImplementation((id) => Promise.resolve(id === 'a' ? [entry] : []));
  render(<HomebrewStudio info={info} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Edit Testy Response' }));
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

const grit: Raw = { type: 'resource', id: 'resource-1', resourceId: 'grit', label: 'Grit', maximum: '3' };
const heavy: Raw = { type: 'toggle', id: 'toggle-2', toggleId: 'heavy', label: 'Testy Heavy' };
const counter: Raw = { type: 'roll', id: 'roll-3', rollId: 'counter', label: 'Counter', dice: '1d8' };

async function addResponse(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Add damage response' }));
}

it('the response editor offers the phase-dependent fields and blocks HP.MAX outside a threshold', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [grit]);
  await addResponse(user);
  const r = rule(/^Rule 2: Damage response/);
  await user.type(r.getByRole('textbox', { name: 'Response name' }), 'Brace');

  // A reduction: an amount, no threshold choice and no HP.MAX hint.
  expect(r.getByRole<HTMLSelectElement>('combobox', { name: 'Phase' }).value).toBe('damageReduction');
  const amount = r.getByRole('textbox', { name: /^Damage taken off/ });
  expect(r.queryByRole('combobox', { name: 'Offered when' })).toBeNull();
  expect(r.queryByText(/HP\.MAX is the hit point maximum before this hit/)).toBeNull();

  // HP.MAX outside a threshold: the server's diagnostic is shown inside the rule (the studio's check), and nothing is parsed here.
  await user.clear(amount);
  await user.type(amount, 'HP.MAX');
  vi.mocked(client.validateRevision).mockResolvedValue({
    revision: { contentId: 'id-m', revisionId: 'x' },
    errors: [{ code: 'validate.formula-invalid', message: "Response 'response-2' amount: HP.MAX can only be read in a threshold.", effectId: 'response-2' }],
    warnings: [],
    canPublish: false,
  });
  expect((await checked()).effect('response-2')).toMatchObject({ phase: 'damageReduction', amount: 'HP.MAX' });
  expect(await r.findByText(/HP\.MAX can only be read in a threshold/)).toBeTruthy();
  expect(r.getByRole('list', { name: 'Problems with this response' })).toBeTruthy();

  // A threshold: a choice of "hit points left at or below a formula" or "I survive", and the hint beside the formula.
  await user.selectOptions(r.getByRole('combobox', { name: 'Phase' }), 'threshold');
  expect(r.queryByRole('textbox', { name: /^Damage taken off/ })).toBeNull();
  await user.selectOptions(r.getByRole('combobox', { name: 'Offered when' }), 'atOrBelow');
  const when = r.getByRole('textbox', { name: /^Hit points left at or below/ });
  expect(r.getByText(/HP\.MAX is the hit point maximum before this hit/)).toBeTruthy();
  expect(when.getAttribute('aria-describedby')).toBeTruthy();
  await user.type(when, 'floor(HP.MAX / 4)');
  expect((await checked()).effect('response-2')).toMatchObject({ phase: 'threshold', when: { atOrBelow: 'floor(HP.MAX / 4)' } });
});

it('changing the phase drops the old phase\'s fields from the saved effect', async () => {
  const user = userEvent.setup();
  const reduction: Raw = { type: 'response', id: 'response-1', responseId: 'response-1', label: 'Brace', phase: 'damageReduction', amount: '2' };
  const { rule, checked } = await openDraft(user, [reduction]);
  const r = rule(/^Rule 1: Damage response/);

  await user.selectOptions(r.getByRole('combobox', { name: 'Phase' }), 'threshold');
  let sent = (await checked()).effect('response-1');
  expect(sent).toMatchObject({ phase: 'threshold', when: { survived: true } });
  expect('amount' in sent).toBe(false);

  await user.selectOptions(r.getByRole('combobox', { name: 'Offered when' }), 'atOrBelow');
  await user.type(r.getByRole('textbox', { name: /^Hit points left at or below/ }), '5');
  expect((await checked()).effect('response-1')).toMatchObject({ when: { atOrBelow: '5' } });

  await user.selectOptions(r.getByRole('combobox', { name: 'Phase' }), 'hitPointOutcome');
  sent = (await checked()).effect('response-1');
  expect(sent).toMatchObject({ phase: 'hitPointOutcome', label: 'Brace' });
  expect('when' in sent).toBe(false);
  expect(typeof sent.amount).toBe('string'); // a phase that takes an amount gets a valid start
  expect(r.getByRole('textbox', { name: /^Hit points left/ })).toBeTruthy();
});

it('the cost can name a pool and drops with it', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [grit]);
  await addResponse(user);
  const r = rule(/^Rule 2: Damage response/);
  expect(r.queryByRole('textbox', { name: /^Cost/ })).toBeNull();
  await user.selectOptions(r.getByRole('combobox', { name: 'Spends from' }), 'grit');
  expect(r.getByRole<HTMLInputElement>('textbox', { name: /^Cost/ }).value).toBe('1'); // a valid start
  await user.type(r.getByRole('textbox', { name: /^Cost/ }), '2');
  expect((await checked()).effect('response-2')).toMatchObject({ resourceId: 'grit', cost: '12' });
  await user.selectOptions(r.getByRole('combobox', { name: 'Spends from' }), '');
  const sent = (await checked()).effect('response-2');
  expect(sent.resourceId).toBeUndefined();
  expect(sent.cost).toBeUndefined();
});

it('outcome kinds show their own fields', async () => {
  const user = userEvent.setup();
  const { rule, checked } = await openDraft(user, [grit, heavy, counter]);
  await addResponse(user);
  const r = rule(/^Rule 4: Damage response/);
  await user.selectOptions(r.getByRole('combobox', { name: 'Phase' }), 'threshold');
  await user.click(r.getByRole('button', { name: 'Add outcome' }));
  const outcome = () => within(r.getByRole('group', { name: 'Outcome 1' }));

  // A pool change: the pool and the amount.
  expect(outcome().getByRole<HTMLSelectElement>('combobox', { name: 'What it does' }).value).toBe('spend');
  expect(outcome().getByRole('combobox', { name: 'Pool' })).toBeTruthy();
  await user.clear(outcome().getByRole('textbox', { name: /^Amount/ }));
  await user.type(outcome().getByRole('textbox', { name: /^Amount/ }), '2');
  expect((await checked()).effect('response-4').outcomes).toEqual([{ kind: 'spend', resourceId: 'grit', amount: '2' }]);

  await user.selectOptions(outcome().getByRole('combobox', { name: 'What it does' }), 'toggleOn');
  expect(outcome().queryByRole('combobox', { name: 'Pool' })).toBeNull();
  expect(outcome().queryByRole('textbox', { name: /^Amount/ })).toBeNull();
  await user.selectOptions(outcome().getByRole('combobox', { name: 'Toggle to turn on' }), 'heavy');
  expect((await checked()).effect('response-4').outcomes).toEqual([{ kind: 'toggleOn', toggle: { toggleId: 'heavy' } }]);

  await user.selectOptions(outcome().getByRole('combobox', { name: 'What it does' }), 'offerRoll');
  expect(outcome().queryByRole('combobox', { name: 'Toggle to turn on' })).toBeNull();
  await user.selectOptions(outcome().getByRole('combobox', { name: 'Roll to offer' }), 'counter');
  expect((await checked()).effect('response-4').outcomes).toEqual([{ kind: 'offerRoll', rollId: 'counter' }]);

  await user.selectOptions(outcome().getByRole('combobox', { name: 'What it does' }), 'temporaryHitPoints');
  expect(outcome().queryByRole('combobox', { name: 'Roll to offer' })).toBeNull();
  expect(outcome().getByRole('textbox', { name: /^Temporary hit points/ })).toBeTruthy();
  expect((await checked()).effect('response-4').outcomes).toEqual([{ kind: 'temporaryHitPoints', amount: '1' }]);

  await user.selectOptions(outcome().getByRole('combobox', { name: 'What it does' }), 'regainHitPoints');
  expect(outcome().getByRole('textbox', { name: /^Hit points regained/ })).toBeTruthy();
  expect((await checked()).effect('response-4').outcomes).toEqual([{ kind: 'regainHitPoints', amount: '1' }]);
});

it('outcomes can be reordered and removed, and every button has a name', async () => {
  const user = userEvent.setup();
  const response: Raw = {
    type: 'response',
    id: 'response-2',
    responseId: 'response-2',
    label: 'Rally',
    phase: 'threshold',
    when: { survived: true },
    outcomes: [
      { kind: 'regainHitPoints', amount: '1' },
      { kind: 'spend', resourceId: 'grit', amount: '2' },
      { kind: 'offerRoll', rollId: 'counter' },
    ],
  };
  const { rule, checked } = await openDraft(user, [grit, response, counter]);
  const r = rule(/^Rule 2: Damage response/);
  expect(r.getByRole('button', { name: 'Move outcome 1 up' }).getAttribute('aria-disabled')).toBe('true');
  expect(r.getByRole('button', { name: 'Move outcome 3 down' }).getAttribute('aria-disabled')).toBe('true');

  // WCAG 2.4.3: after a move the same button, now at the outcome's new place, keeps focus.
  await user.click(r.getByRole('button', { name: 'Move outcome 1 down' }));
  expect(document.activeElement).toBe(r.getByRole('button', { name: 'Move outcome 2 down' }));
  expect(((await checked()).effect('response-2').outcomes as Raw[]).map((o) => o.kind)).toEqual(['spend', 'regainHitPoints', 'offerRoll']);

  await user.click(r.getByRole('button', { name: 'Move outcome 3 up' }));
  expect(document.activeElement).toBe(r.getByRole('button', { name: 'Move outcome 2 up' }));
  expect(((await checked()).effect('response-2').outcomes as Raw[]).map((o) => o.kind)).toEqual(['spend', 'offerRoll', 'regainHitPoints']);

  // After a remove, focus goes to the next outcome's kind select, or to "Add outcome" when there is no next one.
  await user.click(r.getByRole('button', { name: 'Remove outcome 2' }));
  expect(document.activeElement).toBe(within(r.getByRole('group', { name: 'Outcome 2' })).getByRole('combobox', { name: 'What it does' }));
  expect(((await checked()).effect('response-2').outcomes as Raw[]).map((o) => o.kind)).toEqual(['spend', 'regainHitPoints']);

  await user.click(r.getByRole('button', { name: 'Remove outcome 2' }));
  expect(document.activeElement).toBe(r.getByRole('button', { name: 'Add outcome' }));
});

it('a check\'s warnings are listed apart from its errors', async () => {
  const user = userEvent.setup();
  const response: Raw = { type: 'response', id: 'response-2', responseId: 'response-2', label: 'Brace', phase: 'damageReduction', amount: '2' };
  const { rule } = await openDraft(user, [grit, response]);
  const r = rule(/^Rule 2: Damage response/);
  vi.mocked(client.validateRevision).mockResolvedValue({
    revision: { contentId: 'id-m', revisionId: 'x' },
    errors: [],
    warnings: [{ code: 'validate.toggle-content-missing', message: 'Response response-2 names a toggle of content that is not installed.', effectId: 'response-2' }],
    canPublish: true,
  });

  await user.click(screen.getByRole('button', { name: 'Check' }));

  const warnings = await r.findByRole('list', { name: 'Warnings for this response' });
  expect(within(warnings).getByText(/names a toggle of content that is not installed/)).toBeTruthy();
  expect(r.queryByRole('list', { name: 'Problems with this response' })).toBeNull();
});

it('the ninth outcome can\'t be added and says why', async () => {
  const user = userEvent.setup();
  const outcomes = Array.from({ length: 7 }, () => ({ kind: 'temporaryHitPoints', amount: '1' }));
  const response: Raw = { type: 'response', id: 'response-2', responseId: 'response-2', label: 'Many', phase: 'threshold', when: { survived: true }, outcomes };
  const { rule, checked } = await openDraft(user, [grit, response]);
  const r = rule(/^Rule 2: Damage response/);
  const add = () => r.getByRole('button', { name: 'Add outcome' });
  expect(add().getAttribute('aria-disabled')).toBeNull();
  expect(r.queryByText(/at most 8 outcomes/)).toBeNull();

  await user.click(add()); // the eighth
  expect(r.getAllByRole('group', { name: /^Outcome \d/ })).toHaveLength(8);
  expect(add().getAttribute('aria-disabled')).toBe('true');
  const reason = r.getByText(/A response has at most 8 outcomes/);
  expect(add().getAttribute('aria-describedby')).toBe(reason.id);

  await user.click(add()); // refused
  expect(r.getAllByRole('group', { name: /^Outcome \d/ })).toHaveLength(8);
  expect(((await checked()).effect('response-2').outcomes as Raw[]).length).toBe(8);

  await user.click(r.getByRole('button', { name: 'Remove outcome 8' }));
  expect(add().getAttribute('aria-disabled')).toBeNull();
  expect(r.queryByText(/at most 8 outcomes/)).toBeNull();
});

it('edits trigger, share, dice, automatic and condition', async () => {
  const user = userEvent.setup();
  const reduction: Raw = { type: 'response', id: 'response-1', responseId: 'response-1', label: 'Brace', phase: 'damageReduction', amount: '2' };
  const { rule, checked } = await openDraft(user, [reduction]);
  const r = rule(/^Rule 1: Damage response/);

  // A trigger: a save needs an ability and a result; damage types narrow it.
  expect(r.queryByRole('combobox', { name: 'Saving throw ability' })).toBeNull();
  await user.selectOptions(r.getByRole('combobox', { name: 'Only when the hit comes from' }), 'save');
  await user.selectOptions(r.getByRole('combobox', { name: 'Saving throw ability' }), 'dex');
  await user.selectOptions(r.getByRole('combobox', { name: 'Saving throw result' }), 'success');
  await user.click(r.getByRole('checkbox', { name: 'Fire' }));
  await user.click(r.getByRole('checkbox', { name: 'Cold' }));
  await user.click(r.getByRole('checkbox', { name: 'Fire' }));
  expect((await checked()).effect('response-1')).toMatchObject({ trigger: { from: 'save', ability: 'dex', result: 'success', damageTypes: ['cold'] }, amount: '2' });

  // Leaving "save" drops the ability and result; clearing every type drops damageTypes.
  await user.selectOptions(r.getByRole('combobox', { name: 'Only when the hit comes from' }), 'fall');
  await user.click(r.getByRole('checkbox', { name: 'Cold' }));
  let sent = (await checked()).effect('response-1');
  expect(sent.trigger).toEqual({ from: 'fall' });
  await user.selectOptions(r.getByRole('combobox', { name: 'Only when the hit comes from' }), '');
  sent = (await checked()).effect('response-1');
  expect(sent.trigger).toBeUndefined();

  // Dice go with an amount; a share replaces the amount and the dice.
  await user.type(r.getByRole('textbox', { name: /^Dice to roll/ }), '1d10');
  expect((await checked()).effect('response-1')).toMatchObject({ amount: '2', dice: '1d10' });
  await user.selectOptions(r.getByRole('combobox', { name: 'Damage reduced by' }), 'half');
  sent = (await checked()).effect('response-1');
  expect(sent).toMatchObject({ share: 'half' });
  expect('amount' in sent).toBe(false);
  expect('dice' in sent).toBe(false);
  expect(r.queryByRole('textbox', { name: /^Damage taken off/ })).toBeNull();
  expect(r.queryByRole('textbox', { name: /^Dice to roll/ })).toBeNull();
  await user.selectOptions(r.getByRole('combobox', { name: 'Damage reduced by' }), 'amount');
  sent = (await checked()).effect('response-1');
  expect(sent).toMatchObject({ amount: '1' });
  expect('share' in sent).toBe(false);

  // Automatic and a blocking condition.
  await user.click(r.getByRole('checkbox', { name: /^Ticked when first offered/ }));
  await user.selectOptions(r.getByRole('combobox', { name: 'Does not apply while I am' }), 'prone');
  expect((await checked()).effect('response-1')).toMatchObject({ automatic: true, unlessCondition: 'prone' });
  await user.click(r.getByRole('checkbox', { name: /^Ticked when first offered/ }));
  await user.selectOptions(r.getByRole('combobox', { name: 'Does not apply while I am' }), '');
  sent = (await checked()).effect('response-1');
  expect(sent.automatic).toBeUndefined();
  expect(sent.unlessCondition).toBeUndefined();
});

it('clearing the amount clears the dice, and a loaded automatic effect with a pool can be unticked', async () => {
  const user = userEvent.setup();
  const a: Raw = { type: 'response', id: 'response-1', responseId: 'response-1', label: 'Brace', phase: 'damageReduction', amount: '2', dice: '1d10', automatic: true, resourceId: 'grit' };
  const { rule, checked } = await openDraft(user, [grit, a]);
  const r = rule(/^Rule 2: Damage response/);
  const auto = r.getByRole('checkbox', { name: /^Ticked when first offered/ }) as HTMLInputElement;
  expect(auto.disabled).toBe(false);
  await user.click(auto);
  expect((await checked()).effect('response-1').automatic).toBeUndefined();
  expect((r.getByRole('checkbox', { name: /^Ticked when first offered/ }) as HTMLInputElement).disabled).toBe(true);
  await user.clear(r.getByRole('textbox', { name: /^Damage taken off/ }));
  const sent = (await checked()).effect('response-1');
  expect(sent.dice).toBeUndefined();
});

it('keeps supersedes and unknown members across an edit', async () => {
  const user = userEvent.setup();
  const response: Raw = {
    type: 'response', id: 'response-1', responseId: 'response-1', label: 'Brace', phase: 'damageReduction', amount: '2',
    supersedes: { content: '11111111-1111-1111-1111-111111111111', responseId: 'older' },
    trigger: { from: 'attack', damageTypes: ['fire', 'bogus'] },
    futureMember: { keep: true },
  };
  const { rule, checked } = await openDraft(user, [response]);
  const r = rule(/^Rule 1: Damage response/);
  expect(r.getByText('Supersedes older')).toBeTruthy();
  // A value no list offers is shown as removable, not hidden.
  const bogus = r.getByRole('checkbox', { name: 'bogus (removed)' });
  expect((bogus as HTMLInputElement).checked).toBe(true);

  await user.type(r.getByRole('textbox', { name: 'Response name' }), '!');
  await user.selectOptions(r.getByRole('combobox', { name: 'Phase' }), 'threshold');
  const sent = (await checked()).effect('response-1');
  expect(sent).toMatchObject({ label: 'Brace!', supersedes: { content: '11111111-1111-1111-1111-111111111111', responseId: 'older' }, futureMember: { keep: true } });

  await user.click(r.getByRole('checkbox', { name: 'bogus (removed)' }));
  expect((await checked()).effect('response-1').trigger).toEqual({ from: 'attack', damageTypes: ['fire'] });
});

it('leaving damage reduction clears share, dice and automatic', async () => {
  const user = userEvent.setup();
  const a: Raw = { type: 'response', id: 'response-1', responseId: 'response-1', label: 'Brace', phase: 'damageReduction', amount: '2', dice: '1d10', automatic: true };
  const b: Raw = { type: 'response', id: 'response-2', responseId: 'response-2', label: 'Soak', phase: 'damageReduction', share: 'all', unlessCondition: 'prone' };
  const { rule, checked } = await openDraft(user, [a, b]);
  await user.selectOptions(rule(/^Rule 1: Damage response/).getByRole('combobox', { name: 'Phase' }), 'hitPointOutcome');
  await user.selectOptions(rule(/^Rule 2: Damage response/).getByRole('combobox', { name: 'Phase' }), 'threshold');
  const done = await checked();
  for (const id of ['response-1', 'response-2']) {
    const sent = done.effect(id);
    for (const key of ['share', 'dice', 'automatic']) expect(key in sent).toBe(false);
  }
  expect(done.effect('response-2')).toMatchObject({ unlessCondition: 'prone' });
});
