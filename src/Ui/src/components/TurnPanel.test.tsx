// @vitest-environment jsdom
// D43 slice 3: the turn panel. The checklist comes from the unselected preview; the outcome is the current preview; a change of
// tick or choice fetches a new preview and Confirm waits for it; Confirm sends the stamp of the preview on screen. The client is
// mocked; values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { CharacterView, TurnChange, TurnChoiceOption, TurnChoicePrompt, TurnPreview } from '../api/types';
import { TurnPanel } from './TurnPanel';

vi.mock('../api/client', () => ({ client: { turnPreview: vi.fn(), turn: vi.fn() } }));

beforeEach(() => {
  vi.mocked(client.turnPreview).mockReset();
  vi.mocked(client.turn).mockReset();
});
afterEach(cleanup);

const origin = { kind: 'content', rulesFamily: 'srd-5.2.1' } as TurnChange['origin'];
const change = (id: string, label: string, over: Partial<TurnChange> = {}): TurnChange => ({ id, kind: 'spend', label, from: 0, to: 2, reason: `Fixture reason for ${id}`, origin, ...over });
const prompt: TurnChoicePrompt = { id: 'short:c1:iron-upkeep', label: 'Iron stance needs 2, has 1', contentId: 'c1', toggleId: 'iron', needs: 2, has: 1 };
const preview = (over: Partial<TurnPreview> = {}): TurnPreview => ({ at: 'turnStart', changes: [], choices: [], manual: [], prompts: [], updatedAt: 'stamp-1', ...over });
const toggles = [{ content: { contentId: 'c1', revisionId: 'r1' }, toggleId: 'iron', label: 'Fixture Iron stance' }] as never;

const round = change('round', 'Round', { kind: 'round', from: 1, to: 2 });
const upkeepSpend = change('upkeep:c1:iron-upkeep', 'Iron upkeep (Breath)');
const required = change('end:c1:flare', 'Flare ends', { kind: 'toggleEnd', required: true, from: 1, to: 0 });

function panel(over: Partial<Parameters<typeof TurnPanel>[0]> = {}) {
  const props = { characterId: 'fixture-3', at: 'turnStart' as const, version: {}, toggles, onDone: vi.fn(), onCancel: vi.fn(), onError: vi.fn(), ...over };
  return { ...render(<TurnPanel {...props} />), props };
}

it('lists changes as ticked items and required endings as checked and disabled', async () => {
  vi.mocked(client.turnPreview).mockResolvedValue(preview({ changes: [round, upkeepSpend, required] }));
  panel();
  const list = await screen.findByRole('group', { name: /These changes will be applied/ });
  const boxes = within(list).getAllByRole<HTMLInputElement>('checkbox');
  expect(boxes).toHaveLength(3);
  expect(boxes.every((b) => b.checked)).toBe(true);
  const flare = within(list).getByRole<HTMLInputElement>('checkbox', { name: /Flare ends/ });
  expect(flare.disabled).toBe(true);
  expect(list.textContent).toMatch(/Flare ends.*required/s); // text, never colour alone
  expect(within(list).getByRole<HTMLInputElement>('checkbox', { name: /Iron upkeep/ }).disabled).toBe(false);
  expect(client.turnPreview).toHaveBeenCalledWith('fixture-3', 'turnStart', [], {});
});

it('a shortfall shows End preselected and Keep it on, and Confirm sends the choice with the preview stamp', async () => {
  const user = userEvent.setup();
  vi.mocked(client.turnPreview).mockImplementation(async (_id, _at, _skip, choices) => preview({ changes: [round], choices: [prompt], updatedAt: choices[prompt.id] === 'keep' ? 'stamp-2' : 'stamp-1' }));
  vi.mocked(client.turn).mockResolvedValue({} as CharacterView);
  const { props } = panel();
  const group = await screen.findByRole('radiogroup', { name: 'Iron stance needs 2, has 1' });
  const end = within(group).getByRole<HTMLInputElement>('radio', { name: 'End Fixture Iron stance' });
  const keep = within(group).getByRole<HTMLInputElement>('radio', { name: 'Keep it on' });
  expect(end.checked).toBe(true);
  expect(keep.checked).toBe(false);
  await user.click(keep);
  const confirm = screen.getByRole('button', { name: 'Confirm start of turn' });
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  await user.click(confirm);
  await waitFor(() => expect(client.turn).toHaveBeenCalledTimes(1));
  expect(client.turn).toHaveBeenCalledWith('fixture-3', 'turnStart', 'stamp-2', [], { [prompt.id]: 'keep' });
  await waitFor(() => expect(props.onDone).toHaveBeenCalled());
});

it('changing a tick or a choice fetches a new preview, and Confirm is inert until it arrives', async () => {
  const user = userEvent.setup();
  let release: (p: TurnPreview) => void = () => {};
  vi.mocked(client.turnPreview).mockImplementation((_id, _at, skip) =>
    skip.length === 0 ? Promise.resolve(preview({ changes: [round, upkeepSpend] })) : new Promise<TurnPreview>((resolve) => (release = resolve)),
  );
  vi.mocked(client.turn).mockResolvedValue({} as CharacterView);
  panel();
  const confirm = await screen.findByRole('button', { name: 'Confirm start of turn' });
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  await user.click(screen.getByRole('checkbox', { name: /Iron upkeep/ }));
  await waitFor(() => expect(client.turnPreview).toHaveBeenLastCalledWith('fixture-3', 'turnStart', ['upkeep:c1:iron-upkeep'], {}));
  expect(confirm.getAttribute('aria-disabled')).toBe('true');
  await user.click(confirm);
  expect(client.turn).not.toHaveBeenCalled();
  release(preview({ changes: [round, required], updatedAt: 'stamp-3' }));
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  // The checklist still shows the unselected list (the unticked row stays to be re-ticked); the outcome shows what happens now.
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: /Iron upkeep/ }).checked).toBe(false);
  expect(within(screen.getByRole('list', { name: 'What happens' })).getByText(/Flare ends/)).toBeTruthy();
  await user.click(confirm);
  await waitFor(() => expect(client.turn).toHaveBeenCalledWith('fixture-3', 'turnStart', 'stamp-3', ['upkeep:c1:iron-upkeep'], {}));
});

it('drops a choice whose prompt is gone from the next preview', async () => {
  const user = userEvent.setup();
  vi.mocked(client.turnPreview).mockImplementation(async (_id, _at, skip) =>
    skip.length === 0 ? preview({ changes: [round, upkeepSpend], choices: [prompt] }) : preview({ changes: [round, required] }),
  );
  vi.mocked(client.turn).mockResolvedValue({} as CharacterView);
  panel();
  await user.click(await screen.findByRole('radio', { name: 'Keep it on' }));
  await waitFor(() => expect(client.turnPreview).toHaveBeenLastCalledWith('fixture-3', 'turnStart', [], { [prompt.id]: 'keep' }));
  await user.click(screen.getByRole('checkbox', { name: /Iron upkeep/ }));
  await waitFor(() => expect(screen.queryByRole('radiogroup')).toBeNull()); // the prompt vanished with the tick
  const confirm = screen.getByRole('button', { name: 'Confirm start of turn' });
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  await user.click(confirm);
  await waitFor(() => expect(client.turn).toHaveBeenCalled());
  const choices = vi.mocked(client.turn).mock.calls[0]![4];
  expect(choices).toEqual({}); // never sent: the server would refuse an unknown choice
  // Ticking it back asks again, with End preselected (the earlier "keep" was dropped, not remembered).
  expect(vi.mocked(client.turnPreview).mock.calls.at(-1)![3]).toEqual({});
});

it('a stale turn shows the service message, keeps the panel open and previews again', async () => {
  const user = userEvent.setup();
  let stamp = 1;
  vi.mocked(client.turnPreview).mockImplementation(async () => preview({ changes: [round], updatedAt: `stamp-${stamp}` }));
  vi.mocked(client.turn).mockRejectedValueOnce(new TomeStackError({ code: 'turn.preview-stale', message: 'The character changed since this preview.' }));
  const { props } = panel();
  const confirm = await screen.findByRole('button', { name: 'Confirm start of turn' });
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  stamp = 2;
  const previews = vi.mocked(client.turnPreview).mock.calls.length;
  await user.click(confirm);
  await waitFor(() => expect(props.onError).toHaveBeenCalledWith(expect.objectContaining({ code: 'turn.preview-stale' })));
  expect(props.onDone).not.toHaveBeenCalled();
  expect(screen.getByRole('heading', { name: 'Start of turn' })).toBeTruthy();
  await waitFor(() => expect(vi.mocked(client.turnPreview).mock.calls.length).toBeGreaterThan(previews));
  vi.mocked(client.turn).mockResolvedValue({} as CharacterView);
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  await user.click(confirm);
  await waitFor(() => expect(client.turn).toHaveBeenLastCalledWith('fixture-3', 'turnStart', 'stamp-2', [], {}));
});

it('a turn with nothing to change says so and offers Close, not Confirm', async () => {
  const user = userEvent.setup();
  vi.mocked(client.turnPreview).mockResolvedValue(preview());
  const { props } = panel({ at: 'turnEnd' });
  expect(await screen.findByText('Nothing changes this turn')).toBeTruthy();
  expect(screen.queryByRole('button', { name: /^Confirm/ })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Close' }));
  expect(props.onCancel).toHaveBeenCalled();
  expect(client.turn).not.toHaveBeenCalled();
});

it('shows full-pool prompts and do-by-hand lines as text, and the end-of-turn name', async () => {
  vi.mocked(client.turnPreview).mockResolvedValue(
    preview({
      at: 'turnEnd',
      changes: [upkeepSpend],
      prompts: [{ id: 'full:strain', text: 'Fixture burst-out', origin }],
      manual: [{ code: 'turn.settle-by-hand', message: 'Fixture Iron stance: short 1 Breath, settle by hand' }],
    }),
  );
  panel({ at: 'turnEnd' });
  expect(await screen.findByRole('heading', { name: 'End of turn' })).toBeTruthy();
  expect(await screen.findByText('Fixture burst-out')).toBeTruthy();
  expect(screen.getByText(/short 1 Breath, settle by hand/)).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Confirm end of turn' })).toBeTruthy();
});

it('refetches when the sheet version changes, and runs Confirm through the sheet queue when one is given', async () => {
  const user = userEvent.setup();
  vi.mocked(client.turnPreview).mockResolvedValue(preview({ changes: [round] }));
  vi.mocked(client.turn).mockResolvedValue({} as CharacterView);
  const run = vi.fn((command: () => Promise<CharacterView>) => command());
  const { rerender, props } = panel({ run });
  const confirm = await screen.findByRole('button', { name: 'Confirm start of turn' });
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  const before = vi.mocked(client.turnPreview).mock.calls.length;
  rerender(<TurnPanel {...props} version={{}} />);
  await waitFor(() => expect(vi.mocked(client.turnPreview).mock.calls.length).toBeGreaterThan(before));
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  await user.click(confirm);
  await waitFor(() => expect(run).toHaveBeenCalledTimes(1));
  const _typecheck: TurnChoiceOption = 'end'; // the wire values are lower case
  expect(_typecheck).toBe('end');
});

// Fix round 1: the default End is sent explicitly (the service refuses a prompt with no choice), by the outcome preview and the confirm alike.
it('confirming with the untouched default End sends that choice explicitly, and the outcome preview carries it too', async () => {
  const user = userEvent.setup();
  vi.mocked(client.turnPreview).mockResolvedValue(preview({ changes: [round], choices: [prompt] }));
  vi.mocked(client.turn).mockResolvedValue({} as CharacterView);
  panel();
  await screen.findByRole('radiogroup', { name: prompt.label });
  const confirm = screen.getByRole('button', { name: 'Confirm start of turn' });
  await waitFor(() => expect(vi.mocked(client.turnPreview).mock.calls.at(-1)![3]).toEqual({ [prompt.id]: 'end' }));
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  expect(vi.mocked(client.turnPreview).mock.calls[0]![3]).toEqual({}); // the checklist fetch stays unselected
  await user.click(confirm);
  await waitFor(() => expect(client.turn).toHaveBeenCalledWith('fixture-3', 'turnStart', 'stamp-1', [], { [prompt.id]: 'end' }));
});

it('does not sit on "Working out" when only the checklist fetch fails', async () => {
  vi.mocked(client.turnPreview).mockRejectedValueOnce(new Error('boom')).mockResolvedValue(preview({ changes: [round] }));
  const { props } = panel();
  await waitFor(() => expect(props.onError).toHaveBeenCalled());
  expect(await screen.findByText(/Could not work out this turn/)).toBeTruthy();
  expect(screen.queryByText(/Working out what this turn changes/)).toBeNull();
});

it('keeps the polite live paragraph mounted while empty and fills it while updating', async () => {
  const user = userEvent.setup();
  let release: (p: TurnPreview) => void = () => {};
  vi.mocked(client.turnPreview).mockImplementation((_id, _at, skip) =>
    skip.length === 0 ? Promise.resolve(preview({ changes: [round, upkeepSpend] })) : new Promise<TurnPreview>((resolve) => (release = resolve)),
  );
  const { container } = panel();
  await screen.findByRole('checkbox', { name: /Iron upkeep/ });
  const live = container.querySelector('[aria-live="polite"]')!;
  await waitFor(() => expect(live.textContent).toBe(''));
  await user.click(screen.getByRole('checkbox', { name: /Iron upkeep/ }));
  await waitFor(() => expect(live.textContent).toMatch(/Updating the turn/));
  expect(container.querySelector('[aria-live="polite"]')).toBe(live); // same node
  release(preview({ changes: [round] }));
  await waitFor(() => expect(live.textContent).toBe(''));
});
