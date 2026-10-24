// @vitest-environment jsdom
// D43 S7 (Task 7.6a): the damage panel with responses. The preview is fetched on open and again on every selection change; Apply sends
// the selection with the stamp of the preview on screen. The client is mocked; names and numbers are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { CharacterView, DamagePreview, ResponseOffer, ResponseValue } from '../api/types';
import { DamagePanel } from './DamagePanel';
import { HitPointsPanel } from './PlayPanels';
import { SheetSummary } from './SheetSummary';

vi.mock('../api/client', () => ({ client: { damagePreview: vi.fn(), play: vi.fn() } }));

beforeEach(() => {
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.play).mockReset();
});
afterEach(cleanup);

const C1 = 'c0000000-0000-0000-0000-000000000001';
const response = (over: Partial<ResponseValue>): ResponseValue => ({
  contentId: C1,
  contentName: 'Fixture Bulwark',
  responseId: 'x',
  label: 'X',
  phase: 'damageReduction',
  available: true,
  survived: false,
  ...over,
});
const brace = response({ responseId: 'brace', label: 'Brace', amount: 3, cost: 1, resourceId: 'grit', costResourceContent: C1 });
const stand = response({ responseId: 'stand', label: 'Stand firm', phase: 'hitPointOutcome', amount: 1 });
const endure = response({ responseId: 'endure', label: 'Endure', phase: 'hitPointOutcome', amount: 2 });
const rally = response({ responseId: 'rally', label: 'Rally', phase: 'threshold', survived: true, text: 'Fixture rally text.' });
const spent = response({ responseId: 'spent', label: 'Last stand', amount: 5, cost: 4, resourceId: 'grit', costResourceContent: C1, available: false, unavailableReason: 'Needs 4 Grit, has 2.' });

const offer = (r: ResponseValue, over: Partial<ResponseOffer> = {}): ResponseOffer => ({ response: r, offered: r.available, ...(r.available ? {} : { reason: r.unavailableReason }), ...over });

const preview = (over: Partial<DamagePreview> = {}): DamagePreview => ({
  characterId: 'fixture-7',
  amount: 13,
  type: 'slashing',
  reduction: 0,
  outcome: { amount: 13, type: 'slashing', effect: 'none', adjusted: 13, absorbed: 0, lost: 13, warnings: [] },
  after: { maximum: 35, current: 22, temporary: 0 },
  concentrationEnds: false,
  offers: [],
  rolls: [],
  notes: [],
  updatedAt: 'stamp-1',
  ...over,
});

const viewWith = (responses: ResponseValue[], extra: Partial<CharacterView> = {}): CharacterView =>
  ({
    character: { id: 'fixture-7', name: 'Testy', rulesFamily: 'srd-5.2.1', updatedAt: 'stamp-1' },
    sheet: { characterId: 'fixture-7', rulesFamily: 'srd-5.2.1', diagnostics: [], fields: [], hitPoints: { maximum: 35, current: 22, temporary: 0 }, responses },
    ...extra,
  }) as unknown as CharacterView;

function panel(over: Partial<Parameters<typeof DamagePanel>[0]> = {}) {
  const props = {
    view: viewWith([brace, stand, endure, rally, spent]),
    amount: 13,
    type: 'slashing',
    onDone: vi.fn(),
    onCancel: vi.fn(),
    onError: vi.fn(),
    onTimeout: vi.fn(),
    act: vi.fn().mockResolvedValue(true),
    onRoll: vi.fn(),
    ...over,
  };
  return { ...render(<DamagePanel {...props} />), props };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}
const prompts = (container: HTMLElement) => container.querySelector<HTMLElement>('[aria-live="polite"]')!;
const lastChoices = () => vi.mocked(client.damagePreview).mock.calls.at(-1)![3];

it('with responses, damage opens the panel with steps and offers', async () => {
  vi.mocked(client.damagePreview).mockResolvedValue(
    preview({
      reduction: 3,
      outcome: { amount: 10, type: 'slashing', effect: 'halved', adjusted: 5, absorbed: 2, lost: 3, warnings: [{ code: 'play.massive-damage', message: 'Fixture massive warning.' }] },
      after: { maximum: 35, current: 19, temporary: 0 },
      concentrationDc: 10,
      offers: [offer(brace), offer(stand), offer(rally)],
    }),
  );
  panel();
  const steps = await screen.findByRole('list', { name: 'What happens' });
  const text = steps.textContent!;
  expect(text).toContain('13 slashing damage');
  expect(text).toContain('Reduced by 3: 10');
  expect(text).toContain('Halved: 5');
  expect(text).toContain('Temporary hit points took 2');
  expect(text).toContain('Hit points lost: 3');
  expect(text).toContain('Hit points after: 19 of 35');
  expect(text).toContain('Concentration save: DC 10');
  expect(text).toContain('Fixture massive warning.');
  expect(client.damagePreview).toHaveBeenCalledWith('fixture-7', 13, 'slashing', []);
  expect(screen.getByRole('checkbox', { name: /Brace/ })).toBeTruthy();
  expect(screen.getByRole('checkbox', { name: /Rally/ })).toBeTruthy();
});

it('says when concentration ends', async () => {
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ concentrationEnds: true }));
  panel();
  expect((await screen.findByRole('list', { name: 'What happens' })).textContent).toContain('Concentration ends');
});

it('survival choices are one radio group with None', async () => {
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ offers: [offer(brace), offer(stand), offer(endure)] }));
  panel();
  const group = await screen.findByRole('radiogroup', { name: 'Survive at 0 hit points' });
  const radios = within(group).getAllByRole<HTMLInputElement>('radio');
  expect(radios.map((r) => r.labels![0]!.textContent)).toEqual([expect.stringContaining('None'), expect.stringContaining('Stand firm'), expect.stringContaining('Endure')]);
  expect(radios[0]!.checked).toBe(true);
  expect(screen.queryByRole('radio', { name: /Brace/ })).toBeNull(); // a reduction is a checkbox
});

it('choosing a survival re-previews and offers the survived threshold', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockImplementation(async (_id, _a, _t, choices) =>
    choices.some((c) => c.responseId === 'stand') ? preview({ offers: [offer(stand), offer(rally)] }) : preview({ offers: [offer(stand), offer({ ...rally }, { offered: false, reason: 'Needs a survival first.' })] }),
  );
  const { container } = panel();
  await user.click(await screen.findByRole('radio', { name: /Stand firm/ }));
  await waitFor(() => expect(lastChoices()).toEqual([{ contentId: C1, responseId: 'stand' }]));
  const rallyBox = await screen.findByRole<HTMLInputElement>('checkbox', { name: /Rally/ });
  await waitFor(() => expect(rallyBox.disabled).toBe(false));
  await waitFor(() => expect(prompts(container).textContent).toContain('Rally is available'));
});

it('an unaffordable response is disabled with its reason', async () => {
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ offers: [offer(spent)] }));
  panel();
  const box = await screen.findByRole<HTMLInputElement>('checkbox', { name: /Last stand/ });
  expect(box.disabled).toBe(true);
  const describedBy = box.getAttribute('aria-describedby')!;
  expect(document.getElementById(describedBy)!.textContent).toBe('Needs 4 Grit, has 2.');
});

it('Apply sends the preview basis and the selection, then closes', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ offers: [offer(brace)] }));
  const reply = { character: { updatedAt: 'stamp-3' }, notices: [] } as unknown as CharacterView;
  vi.mocked(client.play).mockResolvedValue(reply);
  const { props } = panel();
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
  expect(client.play).toHaveBeenCalledWith('fixture-7', {
    action: 'damage',
    amount: 13,
    damageType: 'slashing',
    responses: [{ contentId: C1, responseId: 'brace' }],
    expectedUpdatedAt: 'stamp-1',
  });
  await waitFor(() => expect(props.onDone).toHaveBeenCalledWith(reply));
  expect(props.onCancel).toHaveBeenCalled(); // nothing to prompt: the panel closes
});

it('Apply with nothing chosen is the one-step damage through act, so Undo works as for any plain hit', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ type: undefined }));
  const { props } = panel({ type: '', amount: 1 });
  const apply = await screen.findByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(props.act).toHaveBeenCalledWith({ action: 'damage', amount: 1 }));
  expect(client.play).not.toHaveBeenCalled();
  expect(client.damagePreview).toHaveBeenCalledWith('fixture-7', 1, undefined, []);
  await waitFor(() => expect(props.onCancel).toHaveBeenCalled());
});

it('a refused one-step damage keeps the panel open and previews again', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValue(preview());
  const { props } = panel({ act: vi.fn().mockResolvedValue(false) });
  const apply = await screen.findByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(client.damagePreview).toHaveBeenCalledTimes(2));
  expect(props.onCancel).not.toHaveBeenCalled();
});

it('a stale reply re-previews and says the sheet changed', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ offers: [offer(brace)] }));
  vi.mocked(client.play).mockRejectedValueOnce(new TomeStackError({ code: 'character.stale', message: 'The character changed.' }));
  const { props, container, rerender } = panel();
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(prompts(container).textContent).toContain('The sheet changed. Check the damage again.'));
  expect(props.onError).toHaveBeenCalled(); // the sheet reloads the stored copy, which arrives as a new view with the new stamp
  expect(props.onDone).not.toHaveBeenCalled();
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ offers: [offer(brace)], updatedAt: 'stamp-2' }));
  const fresh = viewWith([brace, stand, endure, rally, spent]);
  fresh.character.updatedAt = 'stamp-2';
  rerender(<DamagePanel {...props} view={fresh} />);
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  vi.mocked(client.play).mockResolvedValue({} as CharacterView);
  await user.click(apply);
  await waitFor(() => expect(client.play).toHaveBeenLastCalledWith('fixture-7', expect.objectContaining({ expectedUpdatedAt: 'stamp-2' })));
});

it('a timeout on Apply sends nothing again, reloads through the sheet and does not preview for a retry', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ offers: [offer(brace)] }));
  vi.mocked(client.play).mockRejectedValueOnce(new TomeStackError({ code: 'timeout', message: 'No answer.' }));
  const { props } = panel();
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  const previews = vi.mocked(client.damagePreview).mock.calls.length;
  await user.click(apply);
  await waitFor(() => expect(props.onTimeout).toHaveBeenCalledTimes(1));
  expect(client.play).toHaveBeenCalledTimes(1);
  expect(client.damagePreview).toHaveBeenCalledTimes(previews);
  expect(props.onDone).not.toHaveBeenCalled();
  expect(props.onError).not.toHaveBeenCalled();
});

it('a preview planned from a newer stamp than the sheet shows keeps Apply disabled', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ updatedAt: 'stamp-2' }));
  panel();
  const apply = await screen.findByRole('button', { name: 'Apply damage' });
  await screen.findByRole('list', { name: 'What happens' });
  expect(apply.getAttribute('aria-disabled')).toBe('true');
  await user.click(apply);
  expect(client.play).not.toHaveBeenCalled();
});

it('an older preview reply that lands after a newer one is discarded', async () => {
  const user = userEvent.setup();
  const first = deferred<DamagePreview>();
  const second = deferred<DamagePreview>();
  const third = deferred<DamagePreview>();
  vi.mocked(client.damagePreview).mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise).mockReturnValueOnce(third.promise);
  panel({ amount: 13 });
  first.resolve(preview({ offers: [offer(brace)] }));
  const box = await screen.findByRole('checkbox', { name: /Brace/ });
  await user.click(box); // preview 2, held
  await user.click(box); // selection back to empty: preview 3, held
  third.resolve(preview({ offers: [offer(brace)], after: { maximum: 35, current: 22, temporary: 0 } }));
  await waitFor(() => expect(screen.getByRole('list', { name: 'What happens' }).textContent).toContain('Hit points after: 22 of 35'));
  // The reply for the selection that was undone lands last and must not replace what is shown.
  second.resolve(preview({ offers: [offer(brace)], after: { maximum: 35, current: 1, temporary: 0 } }));
  await Promise.resolve();
  await waitFor(() => expect(screen.getByRole('list', { name: 'What happens' }).textContent).toContain('Hit points after: 22 of 35'));
});

it('Apply is blocked while a re-preview is in flight', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValueOnce(preview({ offers: [offer(brace)] }));
  const held = deferred<DamagePreview>();
  vi.mocked(client.damagePreview).mockReturnValueOnce(held.promise);
  panel();
  const box = await screen.findByRole('checkbox', { name: /Brace/ });
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(box);
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('true'));
  await user.click(apply);
  expect(client.play).not.toHaveBeenCalled();
  held.resolve(preview({ offers: [offer(brace)] }));
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
});

it('a selection that leaves the offers is dropped and re-previewed', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockImplementation(async (_id, _a, _t, choices) =>
    choices.some((c) => c.responseId === 'stand') ? preview({ offers: [offer(stand)] }) : preview({ offers: [offer(stand), offer(rally)] }),
  );
  vi.mocked(client.play).mockResolvedValue({} as CharacterView);
  panel();
  await user.click(await screen.findByRole('checkbox', { name: /Rally/ }));
  await waitFor(() => expect(lastChoices()).toEqual([{ contentId: C1, responseId: 'rally' }]));
  await user.click(screen.getByRole('radio', { name: /Stand firm/ }));
  // With Stand firm chosen the preview no longer lists Rally: it leaves the selection and the panel previews once more.
  await waitFor(() => expect(lastChoices()).toEqual([{ contentId: C1, responseId: 'stand' }]));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(client.play).toHaveBeenCalled());
  expect(vi.mocked(client.play).mock.calls[0]![1].responses).toEqual([{ contentId: C1, responseId: 'stand' }]);
});

it('a one-outcome note disables Apply and shows the note', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValue(
    preview({ offers: [offer(brace)], notes: [{ code: 'damage.one-outcome', message: 'Only one hit point outcome can be chosen.' }] }),
  );
  panel();
  expect(await screen.findByText('Only one hit point outcome can be chosen.')).toBeTruthy();
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  expect(apply.getAttribute('aria-disabled')).toBe('true');
  await user.click(apply);
  expect(client.play).not.toHaveBeenCalled();
});

it('an offered roll after apply is a button that rolls it, and skipped responses are named', async () => {
  const user = userEvent.setup();
  const content = { contentId: C1, revisionId: 'r1' };
  vi.mocked(client.damagePreview).mockResolvedValue(preview({ offers: [offer(brace)], rolls: [{ content, effectId: 'fx-roll', rollId: 'recover', label: 'Recovery die' }] }));
  const reply = {
    character: { updatedAt: 'stamp-3' },
    notices: [
      { code: 'damage.roll-offered', message: 'Roll recover of content c0 is offered.', content, effectId: 'fx-roll' },
      { code: 'damage.responses-skipped', message: 'ids only', content, effectId: 'brace' },
    ],
  } as unknown as CharacterView;
  vi.mocked(client.play).mockResolvedValue(reply);
  const { props, container } = panel();
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  const roll =await screen.findByRole('button', { name: 'Roll Recovery die' });
  expect(props.onCancel).not.toHaveBeenCalled(); // the prompts stay until the player closes the panel
  expect(prompts(container).contains(roll)).toBe(true);
  expect(prompts(container).textContent).toContain('Brace');
  await user.click(roll);
  expect(props.onRoll).toHaveBeenCalledWith({ content, effectId: 'fx-roll' });
  await user.click(screen.getByRole('button', { name: 'Close' }));
  expect(props.onCancel).toHaveBeenCalled();
});

it('Cancel discards without sending', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockResolvedValue(preview());
  const { props } = panel();
  await user.click(await screen.findByRole('button', { name: 'Cancel' }));
  expect(props.onCancel).toHaveBeenCalled();
  expect(client.play).not.toHaveBeenCalled();
});

// Routing.
it('with no damage responses, damage applies in one step as before', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const onDamage = vi.fn();
  const view = viewWith([]);
  delete (view.sheet as { responses?: unknown }).responses;
  render(<HitPointsPanel view={view} act={act} onDamage={onDamage} />);
  await user.type(screen.getByRole('spinbutton', { name: 'Amount' }), '13');
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  expect(act).toHaveBeenCalledWith({ action: 'damage', amount: 13 });
  expect(onDamage).not.toHaveBeenCalled();
});

it('with responses, Take damage opens the panel with the amount and type instead of applying', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const onDamage = vi.fn();
  render(<HitPointsPanel view={viewWith([brace])} act={act} onDamage={onDamage} />);
  await user.type(screen.getByRole('spinbutton', { name: 'Amount' }), '13');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Damage type' }), 'slashing');
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  expect(onDamage).toHaveBeenCalledWith(13, 'slashing');
  expect(act).not.toHaveBeenCalled();
});

it("the summary's Lose 1 hit point opens the panel when the sheet has responses", async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const onDamage = vi.fn();
  const view = viewWith([brace]);
  view.sheet.fields = [];
  render(<SheetSummary view={view} rollMode="normal" onRollMode={() => {}} rollLog={[]} act={act} onDamage={onDamage} onRoll={() => {}} onRollDice={async () => {}} />);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  expect(onDamage).toHaveBeenCalledWith(1, '');
  expect(act).not.toHaveBeenCalled();
});

it('skipped responses after a plain damage are named', () => {
  const content = { contentId: C1, revisionId: 'r1' };
  const view = viewWith([brace, rally], {
    damage: { amount: 4, effect: 'none', adjusted: 4, absorbed: 0, lost: 4, warnings: [] },
    notices: [
      { code: 'damage.responses-skipped', message: 'ids only', content, effectId: 'brace' },
      { code: 'damage.roll-offered', message: 'not for this panel', content, effectId: 'fx' },
    ],
  });
  render(<HitPointsPanel view={view} act={() => {}} />);
  expect(screen.getByText('Available and not chosen: Brace.')).toBeTruthy();
});
