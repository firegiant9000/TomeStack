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

  await user.click(r.getByRole('button', { name: 'Move outcome 1 down' }));
  expect(((await checked()).effect('response-2').outcomes as Raw[]).map((o) => o.kind)).toEqual(['spend', 'regainHitPoints', 'offerRoll']);

  await user.click(r.getByRole('button', { name: 'Move outcome 3 up' }));
  expect(((await checked()).effect('response-2').outcomes as Raw[]).map((o) => o.kind)).toEqual(['spend', 'offerRoll', 'regainHitPoints']);

  await user.click(r.getByRole('button', { name: 'Remove outcome 2' }));
  expect(((await checked()).effect('response-2').outcomes as Raw[]).map((o) => o.kind)).toEqual(['spend', 'regainHitPoints']);
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
