// @vitest-environment jsdom
// D43 slice 3: the combat strip above the Play columns (forms, the round, encounter and turn buttons, durations, pools) and the
// pool amount box. The client is not involved: the strip only calls `act` and `onTurn`. Values are invented.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { CharacterView, ResourceValue, ToggleValue, UpkeepValue } from '../api/types';
import { CombatStrip } from './CombatStrip';
import { ResourcesPanel } from './PlayPanels';

afterEach(cleanup);

const ref = { contentId: 'c1', revisionId: 'r1' };
const toggle = (id: string, over: Partial<ToggleValue> = {}): ToggleValue => ({
  content: ref,
  contentName: 'Fixture Stance Pack',
  effectId: `e-${id}`,
  toggleId: id,
  label: `Fixture ${id}`,
  on: false,
  available: true,
  ...over,
});
const pool = (id: string, over: Partial<ResourceValue> = {}): ResourceValue => ({
  content: ref,
  contentName: 'Fixture Pool Pack',
  effectId: `e-${id}`,
  resourceId: id,
  label: `Fixture ${id}`,
  maximum: 5,
  spent: 0,
  current: 5,
  trace: [],
  warnings: [],
  automation: 'automatic',
  recoveries: [],
  ...over,
});
const upkeep: UpkeepValue = {
  content: ref,
  effectId: 'e-up',
  toggleId: 'iron',
  at: 'turnStart',
  resourceContent: 'c1',
  resourceId: 'breath',
  change: 'spend',
  amount: 2,
  all: false,
  ifShort: 'ask',
  label: 'Iron upkeep',
};

function view(over: { toggles?: ToggleValue[]; resources?: ResourceValue[]; upkeeps?: UpkeepValue[]; round?: number } = {}): CharacterView {
  return {
    character: { id: 'fixture-3', updatedAt: '2026-10-03T00:00:00Z', play: over.round === undefined ? undefined : { encounter: { round: over.round } } },
    sheet: { characterId: 'fixture-3', toggles: over.toggles, resources: over.resources, upkeeps: over.upkeeps, fields: [], diagnostics: [] },
  } as unknown as CharacterView;
}

const strip = (v: CharacterView, extra: Partial<Parameters<typeof CombatStrip>[0]> = {}) =>
  render(<CombatStrip view={v} act={vi.fn()} busy={false} onTurn={vi.fn()} {...extra} />);

it('shows Round 1 at round 0, durations as N rounds left or track by hand, strain as built up and an allowance as 1 of 2', () => {
  const v = view({
    round: 0,
    toggles: [toggle('iron', { on: true, duration: 3, roundsLeft: 2 }), toggle('flare', { on: true, duration: 3 }), toggle('idle', { on: false, duration: 3 })],
    resources: [
      pool('strain', { countsUp: true, maximum: 5, spent: 3, current: 2, fullText: 'Fixture burst-out' }),
      pool('reactions', { allowance: true, maximum: 2, spent: 1, current: 1 }),
    ],
  });
  strip(v);
  const region = screen.getByRole('region', { name: 'Combat' });
  expect(within(region).getByText('Round 1')).toBeTruthy();
  expect(within(region).getByText(/Fixture iron: 2 rounds left/)).toBeTruthy();
  expect(within(region).getByText(/Fixture flare: duration: track by hand/)).toBeTruthy();
  expect(region.textContent).not.toMatch(/Fixture idle/); // a toggle that is off has no duration to show
  expect(within(region).getByText(/Fixture strain: built up 3 of 5/)).toBeTruthy();
  expect(within(region).getByText(/Fixture reactions: 1 of 2/)).toBeTruthy();
});

it('shows the round after it advances, and 1 round left in the singular', () => {
  strip(view({ round: 4, toggles: [toggle('iron', { on: true, duration: 3, roundsLeft: 1 })], upkeeps: [upkeep] }));
  expect(screen.getByText('Round 4')).toBeTruthy();
  expect(screen.getByText(/Fixture iron: 1 round left/)).toBeTruthy();
});

it('renders nothing for a sheet with no grouped toggle, allowance, upkeep or encounter', () => {
  strip(view({ toggles: [toggle('plain')], resources: [pool('rage')] }));
  expect(screen.queryByRole('region', { name: 'Combat' })).toBeNull();
});

it('renders for an upkeep alone, for an allowance alone and for a running encounter alone', () => {
  strip(view({ upkeeps: [upkeep] }));
  expect(screen.getByRole('region', { name: 'Combat' })).toBeTruthy();
  cleanup();
  strip(view({ resources: [pool('reactions', { allowance: true })] }));
  expect(screen.getByRole('region', { name: 'Combat' })).toBeTruthy();
  cleanup();
  strip(view({ round: 0 }));
  expect(screen.getByRole('region', { name: 'Combat' })).toBeTruthy();
});

it('Start encounter and End encounter send their actions, and only the one that applies is offered', async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  strip(view({ upkeeps: [upkeep] }), { act });
  expect(screen.queryByRole('button', { name: 'End encounter' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Start my turn' })).toBeNull(); // turns are taken in an encounter
  await user.click(screen.getByRole('button', { name: 'Start encounter' }));
  expect(act).toHaveBeenLastCalledWith({ action: 'startEncounter' });
  cleanup();
  strip(view({ upkeeps: [upkeep], round: 2 }), { act });
  expect(screen.queryByRole('button', { name: 'Start encounter' })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'End encounter' }));
  expect(act).toHaveBeenLastCalledWith({ action: 'endEncounter' });
});

it('opens the turn panel for the start or the end of the turn, and busy disables the encounter and turn buttons', async () => {
  const user = userEvent.setup();
  const onTurn = vi.fn();
  strip(view({ upkeeps: [upkeep], round: 1 }), { onTurn });
  await user.click(screen.getByRole('button', { name: 'Start my turn' }));
  expect(onTurn).toHaveBeenLastCalledWith('turnStart');
  await user.click(screen.getByRole('button', { name: 'End my turn' }));
  expect(onTurn).toHaveBeenLastCalledWith('turnEnd');
  cleanup();
  strip(view({ upkeeps: [upkeep], round: 1 }), { onTurn, busy: true });
  for (const name of ['Start my turn', 'End my turn', 'End encounter']) expect(screen.getByRole<HTMLButtonElement>('button', { name }).disabled).toBe(true);
});

it('hosts the form picker (moved out of Attacks and actions)', () => {
  const forms = [toggle('tide', { group: 'forms', groupContent: 'g', groupLabel: 'Forms', layer: 'primary' })];
  strip(view({ toggles: forms }));
  expect(within(screen.getByRole('region', { name: 'Combat' })).getByRole('radiogroup', { name: 'Forms' })).toBeTruthy();
});

it('names the pool a full counting-up pool announces, as text', () => {
  strip(view({ round: 1, resources: [pool('strain', { countsUp: true, maximum: 5, spent: 5, current: 0, fullText: 'Fixture burst-out' })] }));
  expect(screen.getByText(/Fixture strain: built up 5 of 5/).textContent).toMatch(/full: Fixture burst-out/);
});

// The pool amount box (ResourceCard, on the Resources panel).
it("a pool's amount box spends the chosen amount, bounded to what is left, and the one-click spend stays", async () => {
  const user = userEvent.setup();
  const act = vi.fn();
  const v = view({ resources: [pool('focus', { maximum: 7, spent: 2, current: 5 })] });
  render(<ResourcesPanel view={v} act={act} />);
  const key = { contentId: 'c1', resourceId: 'focus' };
  await user.click(screen.getByRole('button', { name: 'Spend 1 Fixture focus' }));
  expect(act).toHaveBeenLastCalledWith({ action: 'spend', amount: 1, ...key });

  const box = screen.getByRole<HTMLInputElement>('spinbutton', { name: 'Amount to spend: Fixture focus' });
  const spend = screen.getByRole<HTMLButtonElement>('button', { name: 'Spend amount: Fixture focus' });
  expect(box.min).toBe('1');
  expect(box.max).toBe('5');
  await user.clear(box);
  await user.type(box, '3');
  await user.click(spend);
  expect(act).toHaveBeenLastCalledWith({ action: 'spend', amount: 3, ...key });

  act.mockClear();
  for (const bad of ['0', '6', '2.5', '']) {
    await user.clear(box);
    if (bad) await user.type(box, bad);
    expect(spend.disabled, `amount "${bad}"`).toBe(true);
    await user.click(spend);
  }
  expect(act).not.toHaveBeenCalled();
});

it('hides the amount box for a pool tracked by hand', () => {
  render(<ResourcesPanel view={view({ resources: [pool('hand', { maximum: undefined, current: undefined })] })} act={vi.fn()} />);
  expect(screen.queryByRole('spinbutton')).toBeNull();
});
