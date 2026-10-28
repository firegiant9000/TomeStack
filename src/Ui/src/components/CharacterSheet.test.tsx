// @vitest-environment jsdom
// The sheet's tabs (ADR-014): Spells only when there is something to show, a tab that is not offered falls back to Play,
// "Report a gap" switches to Notes and focuses the note text every time it is pressed, and choosing Notes by hand keeps
// focus on the tab. The client is mocked; values are invented.
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet as SheetModel, CharacterView, DerivedValue, FeatureEntry, RollRecord } from '../api/types';
import { TomeStackError } from '../api/transport';
import { CharacterSheet } from './CharacterSheet';
import { writeChecklist } from '../levelUpChecklist';
import { RollResult } from './PlayPanels';

vi.mock('../api/client', () => ({
  client: {
    attachment: vi.fn(),
    listGapNotes: vi.fn(),
    availableUpdates: vi.fn(),
    listContent: vi.fn(),
    snapshots: vi.fn(),
    previewExport: vi.fn(),
    info: vi.fn(),
    play: vi.fn(),
    undoPlay: vi.fn(),
    roll: vi.fn(),
    rollDice: vi.fn(),
    restPreview: vi.fn(),
    rest: vi.fn(),
    saveCharacter: vi.fn(),
    getCharacter: vi.fn(),
    turnPreview: vi.fn(),
    turn: vi.fn(),
    damagePreview: vi.fn(),
  },
}));

beforeEach(() => {
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'shortRest', changes: [], manual: [], basis: 'fixture' });
  vi.mocked(client.rest).mockReset();
  vi.mocked(client.listGapNotes).mockResolvedValue([]);
  vi.mocked(client.availableUpdates).mockResolvedValue([]);
  vi.mocked(client.listContent).mockResolvedValue([]);
  vi.mocked(client.snapshots).mockResolvedValue({ items: [], hasMore: false });
});
afterEach(() => {
  cleanup();
  localStorage.clear(); // the remembered tab is per character id, and these tests share one
  delete document.documentElement.dataset.compactPlay;
});

const field = (id: string, label: string, value: number, units: string): DerivedValue => ({ field: id, label, value, computedValue: value, trace: [], warnings: [], automation: 'automatic', units });

function view(sheetOver: Partial<SheetModel> = {}): CharacterView {
  const character: Character = {
    id: 'fixture-2',
    schemaVersion: 7,
    name: 'Fixture Quill',
    rulesFamily: 'srd-5.1',
    level: 1,
    classes: [],
    choices: [],
    crossFamilyExceptions: [],
    baseAbilities: { str: 10, dex: 12, con: 10, int: 14, wis: 10, cha: 10 },
    pins: [],
    overrides: [],
    updatedAt: '2026-10-03T00:00:00Z',
  };
  const sheet: SheetModel = {
    characterId: 'fixture-2',
    rulesFamily: 'srd-5.1',
    diagnostics: [],
    fields: [
      field('ability.dex.score', 'Dexterity score', 12, 'score'),
      field('ability.dex.mod', 'Dexterity modifier', 1, 'modifier'),
      field('armorClass', 'Armor Class', 11, 'score'),
      field('initiative', 'Initiative', 1, 'modifier'),
      field('proficiencyBonus', 'Proficiency bonus', 2, 'bonus'),
      field('spellAttack', 'Spell attack bonus', 0, 'modifier'),
    ],
    hitPoints: { maximum: 8, current: 8, temporary: 0 },
    ...sheetOver,
  };
  return { character, sheet };
}

const noop = () => {};
const sheetElement = (v: CharacterView, initialTab?: Parameters<typeof CharacterSheet>[0]['initialTab'], focusChecklist?: boolean) => (
  <CharacterSheet view={v} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} initialTab={initialTab} focusChecklist={focusChecklist} />
);
const renderSheet = (v: CharacterView, initialTab?: Parameters<typeof CharacterSheet>[0]['initialTab'], focusChecklist?: boolean) => render(sheetElement(v, initialTab, focusChecklist));

function undoHarness() {
  const v = view();
  const damaged = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 3, temporary: 0 } } };
  const onChanged = vi.fn();
  const element = (shown: CharacterView) => <CharacterSheet view={shown} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />;
  return { v, damaged, onChanged, element };
}

it('offers Undo for the last play change and sends the inverse through the play command (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockResolvedValue(v);
  const { rerender } = render(element(v));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged)); // the app shows the returned view
  const undo = screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change: damage 1' });
  expect(undo.disabled).toBe(false);
  await user.click(undo);
  await waitFor(() => expect(client.play).toHaveBeenLastCalledWith('fixture-2', { action: 'setHitPoints', amount: 8 }));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true));
  // WCAG 2.4.3: the button is disabled now, so focus is on the hit points heading, not <body>.
  expect(document.activeElement?.id).toBe('hp-heading');
});

it('shows the applied state and keeps focus when a later undo step fails (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged } = undoHarness();
  const first = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 5, temporary: 0 } } };
  const onError = vi.fn();
  const play = { temporaryHitPoints: 0, resources: [], conditions: [], exhaustion: 0 };
  const before = { ...v, character: { ...v.character, play: { ...play, concentration: { spell: { contentId: 's', revisionId: 'r' }, name: 'Fixture Ward' } } } };
  const after = { ...damaged, character: { ...v.character, play } };
  vi.mocked(client.play).mockResolvedValueOnce(after).mockResolvedValueOnce(first).mockRejectedValueOnce(new Error('play.spell-not-prepared'));
  const shown = (x: CharacterView) => <CharacterSheet view={x} onChanged={onChanged} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />;
  const { rerender } = render(shown(before));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(after));
  rerender(shown(after));
  await user.click(screen.getByRole('button', { name: /^Undo last change: damage 1/ }));
  await waitFor(() => expect(onError).toHaveBeenCalled());
  expect(onChanged).toHaveBeenLastCalledWith(first);
  expect(document.activeElement).not.toBe(document.body);
  expect(document.activeElement?.id).toBe('hp-heading');
});

// D44: a form switch names the stored stamp and is undone by the service.
const formToggle = (toggleId: string, label: string, on: boolean) => ({
  content: { contentId: `c-${toggleId}`, revisionId: `r-${toggleId}` },
  contentName: 'Fixture Form Pack',
  effectId: toggleId,
  toggleId,
  label,
  on,
  resourceId: 'focus',
  group: 'forms',
  groupLabel: 'Forms',
  groupContent: 'g-forms',
  layer: 'primary' as const,
  cost: 3,
  costResourceContent: 'g-forms',
  available: true,
});
const formsView = (tideOn: boolean, over: Partial<CharacterView> = {}, stamp = '2026-10-03T00:00:00Z'): CharacterView => {
  const v = view({ toggles: [formToggle('tide', 'Fixture Tide', tideOn), formToggle('ember', 'Fixture Ember', !tideOn)] });
  return { ...v, character: { ...v.character, updatedAt: stamp }, ...over };
};

it('a form switch sends the view\'s stamp, and the latest one after an earlier play change (D44)', async () => {
  const user = userEvent.setup();
  const first = formsView(false);
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValue(first);
  render(<CharacterSheet view={first} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('radio', { name: /Fixture Tide/ }));
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.play).mock.calls[0]?.[1]).toEqual({ action: 'toggleOn', contentId: 'c-tide', toggleId: 'tide', replace: true, expectedUpdatedAt: '2026-10-03T00:00:00Z' });

  // A play change just before: its reply stamps the character, and the switch (the shown view is still the old one) names that stamp.
  vi.mocked(client.play).mockReset();
  const hit = formsView(false, {}, '2026-10-03T00:00:05Z');
  vi.mocked(client.play).mockResolvedValue(hit);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await user.click(screen.getByRole('radio', { name: /Fixture Tide/ }));
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(2));
  expect(vi.mocked(client.play).mock.calls[1]?.[1]).toMatchObject({ replace: true, expectedUpdatedAt: '2026-10-03T00:00:05Z' });
});

it('when the view offers an undo label, Undo reads "Undo switch to ..." and calls undoPlay with the view\'s stamp (D44)', async () => {
  const user = userEvent.setup();
  const offered = formsView(true, { undoLabel: 'switch to Fixture Tide' }, '2026-10-03T00:00:09Z');
  const undone = formsView(false, {}, '2026-10-03T00:00:10Z');
  const onChanged = vi.fn();
  vi.mocked(client.undoPlay).mockResolvedValue(undone);
  render(<CharacterSheet view={offered} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  const button = screen.getByRole<HTMLButtonElement>('button', { name: 'Undo switch to Fixture Tide' });
  expect(button.disabled).toBe(false);
  expect(screen.queryByRole('button', { name: /^Undo last change/ })).toBeNull(); // one Undo, not two
  await user.click(button);
  await waitFor(() => expect(client.undoPlay).toHaveBeenCalledWith('fixture-2', '2026-10-03T00:00:09Z'));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(undone));
});

it('Undo sends the stamp of the view that showed its label, not the save queue\'s latest (D44)', async () => {
  const user = userEvent.setup();
  const offered = formsView(true, { undoLabel: 'switch to Fixture Tide' }, '2026-10-03T00:00:09Z');
  const hit = formsView(true, {}, '2026-10-03T00:00:12Z'); // a play change finished: the queue's latest is now 12, the page still shows 09
  const onChanged = vi.fn();
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValue(hit);
  vi.mocked(client.undoPlay).mockReset();
  vi.mocked(client.undoPlay).mockResolvedValue(formsView(false, {}, '2026-10-03T00:00:13Z'));
  render(<CharacterSheet view={offered} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(hit));
  const button = screen.getByRole<HTMLButtonElement>('button', { name: 'Undo switch to Fixture Tide' });
  await waitFor(() => expect(button.disabled).toBe(false));
  await user.click(button);
  await waitFor(() => expect(client.undoPlay).toHaveBeenCalledTimes(1));
  expect(client.undoPlay).toHaveBeenCalledWith('fixture-2', '2026-10-03T00:00:09Z');
});

it('the service\'s Undo is disabled while a play change is in flight, so it cannot undo a newer switch (D23, D44)', async () => {
  const user = userEvent.setup();
  const offered = formsView(true, { undoLabel: 'switch to Fixture Tide' }, '2026-10-03T00:00:09Z');
  let settle: (view: CharacterView) => void = noop;
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockReturnValue(new Promise<CharacterView>((resolve) => { settle = resolve; }));
  vi.mocked(client.undoPlay).mockReset();
  render(<CharacterSheet view={offered} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('radio', { name: /Fixture Ember/ }));
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
  const button = screen.getByRole<HTMLButtonElement>('button', { name: 'Undo switch to Fixture Tide' });
  expect(button.disabled).toBe(true);
  await user.click(button);
  expect(client.undoPlay).not.toHaveBeenCalled();
  settle(formsView(false, {}, '2026-10-03T00:00:10Z'));
  await waitFor(() => expect(button.disabled).toBe(false));
});

// D43 slice 3: the combat strip's encounter and turn writes.
const upkeepValue = { content: { contentId: 'c1', revisionId: 'r1' }, effectId: 'e-up', toggleId: 'iron', at: 'turnStart', resourceContent: 'c1', resourceId: 'breath', change: 'spend', amount: 2, all: false, ifShort: 'ask', label: 'Iron upkeep' };
const combatView = (round?: number, over: Partial<CharacterView> = {}, stamp = '2026-10-03T00:00:00Z'): CharacterView => {
  const v = view({ upkeeps: [upkeepValue] as never });
  const play = round === undefined ? undefined : { temporaryHitPoints: 0, resources: [], conditions: [], exhaustion: 0, encounter: { round } };
  return { ...v, character: { ...v.character, updatedAt: stamp, play }, ...over };
};

it('Start encounter and End encounter send the stamp of the view on screen, not the save queue\'s latest (D43 S3)', async () => {
  const user = userEvent.setup();
  const shown = combatView(undefined);
  const hit = combatView(undefined, {}, '2026-10-03T00:00:05Z'); // a play change finished: the queue's latest is 05, the page still shows 00
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValue(hit);
  const onChanged = vi.fn();
  render(<CharacterSheet view={shown} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(hit));
  await user.click(screen.getByRole('button', { name: 'Start encounter' }));
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(2));
  expect(vi.mocked(client.play).mock.calls[1]?.[1]).toEqual({ action: 'startEncounter', expectedUpdatedAt: '2026-10-03T00:00:00Z' });

  cleanup();
  vi.mocked(client.play).mockClear();
  render(<CharacterSheet view={combatView(2)} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'End encounter' }));
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.play).mock.calls[0]?.[1]).toEqual({ action: 'endEncounter', expectedUpdatedAt: '2026-10-03T00:00:00Z' });
});

it('Use on an action with alsoChange sends useAction stamped with the view (D43 S8)', async () => {
  const user = userEvent.setup();
  const ref = { contentId: 'c-gather', revisionId: 'r-gather' };
  const pools = [
    { content: ref, contentName: 'Fixture Gatherer', effectId: 'e-focus', resourceId: 'focus', label: 'Fixture focus', maximum: 5, spent: 0, current: 5, trace: [], warnings: [], automation: 'automatic', recoveries: [] },
    { content: ref, contentName: 'Fixture Gatherer', effectId: 'e-charge', resourceId: 'charge', label: 'Fixture charge', maximum: 4, spent: 0, current: 4, countsUp: true, trace: [], warnings: [], automation: 'automatic', recoveries: [] },
  ];
  const gatherEffect = {
    id: 'gather', rollId: 'gather-roll', type: 'roll', automation: 'automatic', label: 'Gather', activation: 'action', resourceId: 'focus', cost: 1, available: true,
    alsoChange: [{ resourceContent: 'c-gather', resourceId: 'charge', change: 'spend', amount: 1 }],
  };
  const shown = view({ resources: pools, features: [{ content: ref, name: 'Fixture Gatherer', kind: 'feature', origin: {}, automation: 'automatic', diagnostics: [], effects: [gatherEffect] }] } as never);
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValue(shown);
  render(<CharacterSheet view={shown} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Use Gather (1 Fixture focus, +1 Fixture charge)' }));
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.play).mock.calls[0]?.[1]).toEqual({ action: 'useAction', contentId: 'c-gather', rollId: 'gather-roll', expectedUpdatedAt: '2026-10-03T00:00:00Z' });
});

it('Pay on the Last roll sends useAction stamped with the view, once (MECH-X item 1, S6)', async () => {
  const user = userEvent.setup();
  const ref = { contentId: 'c-fury', revisionId: 'r-fury' };
  const lash = { id: 'lash', rollId: 'lash-roll', type: 'roll', automation: 'automatic', label: 'Lash', dice: '2d6', cost: 3, activation: 'action', available: true, payment: { kind: 'hitPoints', payable: true } };
  const shown = view({ features: [{ content: ref, name: 'Fixture Fury', kind: 'feature', origin: {}, automation: 'automatic', diagnostics: [], effects: [lash] }] } as never);
  const record = {
    formula: '2d6', mode: 'normal', critical: false, dice: [], diceTotal: 7, expressionConstant: 0, modifiers: [], total: 7,
    provenance: { rollId: 'lash-roll', label: 'Lash', content: ref, effectId: 'lash' }, payment: { contentId: 'c-fury', useId: 'lash-roll', kind: 'hitPoints', amount: 3, mayDropToZero: false },
  } as unknown as RollRecord;
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll).mockResolvedValue(record);
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValue(shown);
  render(<CharacterSheet view={shown} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Roll Lash (2d6)' }));
  expect(client.play).not.toHaveBeenCalled(); // rolling never pays
  const pay = await screen.findByRole('button', { name: 'Pay 3 hit points' });
  await user.click(pay);
  await user.click(pay);
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.play).mock.calls[0]?.[1]).toEqual({ action: 'useAction', contentId: 'c-fury', rollId: 'lash-roll', amount: 3, expectedUpdatedAt: '2026-10-03T00:00:00Z' });
});

it('says what the reply noticed after a play change (here, ending an encounter), in the status line (D43 S3)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const ended = combatView(undefined, { notices: [{ code: 'encounter.duration-by-hand', message: 'Fixture Iron stance (iron): duration: track by hand' }] });
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValue(ended);
  render(<CharacterSheet view={combatView(2)} onChanged={noop} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'End encounter' }));
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('Fixture Iron stance (iron): duration: track by hand'));
  expect(document.querySelectorAll('[role="status"]').length).toBeLessThanOrEqual(1); // no new status region of its own
});

it('Start my turn opens the turn panel, Confirm applies it, closes the panel and reports the reply\'s notices (D43 S3)', async () => {
  const user = userEvent.setup();
  const onChanged = vi.fn();
  const onStatus = vi.fn();
  const done = combatView(3, { undoLabel: 'turn start', notices: [{ code: 'turn.resource-full', message: 'Fixture burst-out' }] });
  vi.mocked(client.turnPreview).mockResolvedValue({
    at: 'turnStart', changes: [{ id: 'round', kind: 'round', label: 'Round', from: 2, to: 3, reason: 'A new round', origin: {} as never }], choices: [], manual: [], prompts: [], updatedAt: '2026-10-03T00:00:00Z',
  });
  vi.mocked(client.turn).mockResolvedValue(done);
  render(<CharacterSheet view={combatView(2)} onChanged={onChanged} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Start my turn' }));
  const confirm = await screen.findByRole('button', { name: 'Confirm start of turn' });
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Start of turn' }));
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  await user.click(confirm);
  await waitFor(() => expect(client.turn).toHaveBeenCalledWith('fixture-2', 'turnStart', '2026-10-03T00:00:00Z', [], {}));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(done));
  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Start of turn' })).toBeNull());
  expect(onStatus).toHaveBeenCalledWith('Fixture burst-out');
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Start my turn' })); // WCAG 2.4.3: back on the opener
});

it('closes an open turn panel when the encounter is no longer running (D43 S3)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.turnPreview).mockResolvedValue({ at: 'turnEnd', changes: [], choices: [], manual: [], prompts: [], updatedAt: 'x' });
  const { rerender } = render(sheetElement(combatView(2)));
  await user.click(screen.getByRole('button', { name: 'End my turn' }));
  await screen.findByRole('heading', { name: 'End of turn' });
  rerender(sheetElement(combatView(undefined)));
  expect(screen.queryByRole('heading', { name: 'End of turn' })).toBeNull();
});

it('a stale turn is shown through the usual error path and reloads the stored copy (D43 S3)', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  vi.mocked(client.getCharacter).mockResolvedValue(combatView(2, {}, '2026-10-03T00:00:09Z'));
  vi.mocked(client.turnPreview).mockResolvedValue({
    at: 'turnStart', changes: [{ id: 'round', kind: 'round', label: 'Round', from: 2, to: 3, reason: 'A new round', origin: {} as never }], choices: [], manual: [], prompts: [], updatedAt: '2026-10-03T00:00:00Z',
  });
  vi.mocked(client.turn).mockRejectedValue(new TomeStackError({ code: 'turn.preview-stale', message: 'The character changed since this preview.' }));
  render(<CharacterSheet view={combatView(2)} onChanged={noop} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Start my turn' }));
  const confirm = await screen.findByRole('button', { name: 'Confirm start of turn' });
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  await user.click(confirm);
  await waitFor(() => expect(onError).toHaveBeenCalledWith(expect.objectContaining({ code: 'turn.preview-stale' })));
  await waitFor(() => expect(client.getCharacter).toHaveBeenCalledWith('fixture-2'));
  expect(screen.getByRole('heading', { name: 'Start of turn' })).toBeTruthy(); // stays open
});

it('a switch withdraws the client Undo from before it, so only the service\'s Undo is offered (D44)', async () => {
  const user = userEvent.setup();
  const before = formsView(false);
  const damaged = { ...before, sheet: { ...before.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } };
  const switched = formsView(true, { undoLabel: 'switch to Fixture Tide' }, '2026-10-03T00:00:02Z');
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockResolvedValueOnce(switched);
  const element = (shown: CharacterView) => <CharacterSheet view={shown} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />;
  const { rerender } = render(element(before));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  rerender(element(damaged));
  await screen.findByRole('button', { name: 'Undo last change: damage 1' });
  await user.click(screen.getByRole('radio', { name: /Fixture Tide/ }));
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(2));
  rerender(element(switched));
  expect(screen.getByRole('button', { name: 'Undo switch to Fixture Tide' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: /^Undo last change/ })).toBeNull();
});

it.each([
  ['undo.stale', 'This character changed since that change, so it can\'t be undone.'],
  ['undo.nothing', 'There is nothing to undo for this character.'],
])('a refused undo (%s) shows the service\'s message and reloads the stored copy, which drops the label (D44)', async (code, message) => {
  const user = userEvent.setup();
  const offered = formsView(true, { undoLabel: 'switch to Fixture Tide' });
  const current = formsView(true, {}, '2026-10-03T00:01:00Z'); // the stored copy offers nothing
  const onChanged = vi.fn();
  const onError = vi.fn();
  const refusal = new TomeStackError({ code, message, diagnostics: [{ code, message }] }); // the wire shape: the top-level code is the specific one
  vi.mocked(client.undoPlay).mockReset();
  vi.mocked(client.undoPlay).mockRejectedValue(refusal);
  vi.mocked(client.getCharacter).mockReset();
  vi.mocked(client.getCharacter).mockResolvedValue(current);
  render(<CharacterSheet view={offered} onChanged={onChanged} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Undo switch to Fixture Tide' }));
  await waitFor(() => expect(onError).toHaveBeenCalledWith(refusal));
  expect(onError.mock.calls[0]?.[0].message).toBe(message);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(current));
  expect(client.getCharacter).toHaveBeenCalledWith('fixture-2');
  expect(screen.getByRole('radio', { name: /Fixture Tide/ }).getAttribute('aria-checked')).toBe('true'); // the view is untouched
});

it('a simple action\'s undo still uses the client inverse (D44, D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockResolvedValue(v);
  vi.mocked(client.undoPlay).mockClear();
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged));
  await user.click(screen.getByRole('button', { name: 'Undo last change: damage 1' }));
  await waitFor(() => expect(client.play).toHaveBeenLastCalledWith('fixture-2', { action: 'setHitPoints', amount: 8 }));
  expect(client.undoPlay).not.toHaveBeenCalled();
});

it('offers no Undo for play changes that overlapped, since the stored "before" is not the state they changed (D23)', async () => {
  const user = userEvent.setup();
  const { v, onChanged, element } = undoHarness();
  const hit = (current: number): CharacterView => ({ ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current, temporary: 0 } } });
  const [first, second] = [hit(7), hit(6)];
  let settleFirst: (view: CharacterView) => void = noop;
  let settleSecond: (view: CharacterView) => void = noop;
  vi.mocked(client.play)
    .mockReturnValueOnce(new Promise<CharacterView>((resolve) => { settleFirst = resolve; }))
    .mockReturnValueOnce(new Promise<CharacterView>((resolve) => { settleSecond = resolve; }));
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  settleFirst(first);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(first));
  settleSecond(second);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(second));
  rerender(element(second));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
});

const pending = () => {
  let settle: (view: CharacterView) => void = noop;
  const promise = new Promise<CharacterView>((resolve) => {
    settle = resolve;
  });
  return { promise, settle: (view: CharacterView) => settle(view) };
};

it('withdraws an offered Undo as soon as another play change starts, so the two cannot interleave (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  const next = pending();
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockReturnValueOnce(next.promise);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change: damage 1' }).disabled).toBe(false);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' })); // still in flight
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
  next.settle(damaged);
});

it('offers no Undo for a play change made while an Undo was running (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  const [undoStep, hit] = [pending(), pending()];
  const seven = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } };
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockReturnValueOnce(undoStep.promise).mockReturnValueOnce(hit.promise);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged));
  await user.click(screen.getByRole('button', { name: 'Undo last change: damage 1' }));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' })); // while the undo step is in flight
  undoStep.settle(v);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(v));
  hit.settle(seven);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(seven));
  rerender(element(seven));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
});

it('offers no Undo for a play change whose reply came after another view was shown, such as a rest (D23)', async () => {
  const user = userEvent.setup();
  const { v, onChanged, element } = undoHarness();
  const hit = pending();
  const rested = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 8, temporary: 0 } } };
  const seven = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } };
  vi.mocked(client.play).mockReturnValueOnce(hit.promise);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  rerender(element(rested)); // a rest's result lands while the damage is in flight
  hit.settle(seven);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(seven));
  rerender(element(seven));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
});

it('does not offer Undo once another view is shown, such as a rest result (D23)', async () => {
  const user = userEvent.setup();
  const { v, damaged, onChanged, element } = undoHarness();
  vi.mocked(client.play).mockResolvedValueOnce(damaged);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change: damage 1' }).disabled).toBe(false);
  rerender(element({ ...v })); // a long rest healed the character: a different view object
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo last change' }).disabled).toBe(true);
});

it('leaves the information-only toggle.group-absent out of "Content not applied" and keeps real diagnostics (D43)', () => {
  renderSheet(
    view({
      diagnostics: [
        { code: 'toggle.group-absent', message: 'Toggle joins a group whose content is not here; it is a group of one.' },
        { code: 'effect.invalid-formula', message: 'A formula does not read.' },
      ],
    }),
  );
  expect(screen.getByRole('heading', { name: 'Content not applied' })).toBeTruthy();
  expect(screen.getByText('A formula does not read.')).toBeTruthy();
  expect(screen.queryByText(/group of one/)).toBeNull();
  cleanup();
  renderSheet(view({ diagnostics: [{ code: 'toggle.group-absent', message: 'group of one' }] }));
  expect(screen.queryByRole('heading', { name: 'Content not applied' })).toBeNull();
});

it('offers a compact view on Play that is remembered as a preference (D20)', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  const toggle = screen.getByRole<HTMLInputElement>('checkbox', { name: /^Compact view/ });
  expect(toggle.checked).toBe(false);
  await user.click(toggle);
  expect(localStorage.getItem('tomestack.compactPlay')).toBe('on');
  expect(document.documentElement.dataset.compactPlay).toBe('on');
});

it('marks proficient and expert fields on Stats and lists passive scores and Speed (D24)', () => {
  const base = view().sheet.fields;
  renderSheet(
    view({
      fields: [
        ...base,
        { ...field('skill.stealth', 'Stealth', 5, 'modifier'), mark: 'expertise' },
        field('skill.perception', 'Perception', 1, 'modifier'),
        field('passive.perception', 'Passive Perception', 11, 'score'),
        field('speed', 'Speed', 30, 'feet'),
      ],
    }),
    'stats',
  );
  expect(screen.getByRole('heading', { name: 'Stealth: +5 · expertise' })).toBeTruthy();
  expect(screen.getByRole('heading', { name: 'Perception: +1' })).toBeTruthy(); // no mark when no grant
  expect(screen.getByRole('heading', { name: /^Passive Perception: 11/ })).toBeTruthy(); // unsigned
  expect(screen.getByRole('heading', { name: 'Speed: 30 ft.' })).toBeTruthy();
});

it('groups Features by what granted them, then by kind, and drops none (D25)', () => {
  const ref = (n: number) => ({ contentId: `00000000-0000-4000-8000-00000000c${n}00`, revisionId: `00000000-0000-4000-8000-00000000d${n}00` });
  const origin = { kind: 'content' as const, rulesFamily: 'srd-5.1' as const };
  const entry = (n: number, name: string, kind: FeatureEntry['kind'], grantedByName?: string): FeatureEntry => ({
    content: ref(n), name, kind, origin, automation: 'reference', effects: [], diagnostics: [], grantedBy: grantedByName ? ref(9) : undefined, grantedByName,
  });
  renderSheet(
    view({
      features: [entry(1, 'Fixture Fighter', 'class'), entry(2, 'Fixture Rage', 'feature', 'Fixture Fighter'), entry(3, 'Fixture Alert', 'feat', 'Fixture Wayfarer'), entry(4, 'Fixture Torch', 'item')],
    }),
    'features',
  );
  expect(screen.getAllByRole('heading', { level: 4 }).map((h) => h.textContent)).toEqual(['Classes', 'Items', 'From Fixture Fighter', 'From Fixture Wayfarer']);
  expect(Array.from(document.querySelectorAll('.feature .option-name')).map((n) => n.textContent)).toEqual(['Fixture Fighter', 'Fixture Torch', 'Fixture Rage', 'Fixture Alert']);
});

it('offers Spells to a caster, or when a spell field has a value, and opens on Play', () => {
  renderSheet(view());
  expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Play', 'Inventory', 'Features', 'Stats', 'Notes', 'Manage']);
  expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  cleanup();
  renderSheet(view({ spellcasting: [] , spellSlots: [{ level: 1, maximum: 2, spent: 0, remaining: 2, field: 'spellSlots.1' }], fields: [...view().sheet.fields, field('spellSlots.1', 'Level 1 spell slots', 2, 'score')] }));
  expect(screen.getByRole('tab', { name: 'Spells' })).toBeTruthy(); // a spell field with a value counts, even with no caster entry
  cleanup();
  const ref = { contentId: '00000000-0000-4000-8000-0000000000a1', revisionId: '00000000-0000-4000-8000-0000000000b1' };
  renderSheet(
    view({
      spellcasting: [
        {
          content: ref,
          name: 'Fixture Arcanist',
          effectId: 'fixture-spellcasting',
          classLevel: 1,
          ability: 'int',
          attackBonus: 4,
          saveDc: 12,
          preparation: 'prepared',
          spellList: 'fixture-list',
          slotKind: 'spellSlots',
          slots: [2],
          primary: true,
          origin: { kind: 'content', rulesFamily: 'srd-5.1', content: ref, contentName: 'Fixture Arcanist' },
          spells: [],
          warnings: [],
        },
      ],
    }),
  );
  expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Play', 'Spells', 'Inventory', 'Features', 'Stats', 'Notes', 'Manage']);
});

it('announces a Prepared toggle on a spell removed meanwhile as a polite status, not an error, and keeps focus (H2/R144)', async () => {
  const user = userEvent.setup();
  const ref = { contentId: '00000000-0000-4000-8000-0000000000a1', revisionId: '00000000-0000-4000-8000-0000000000b1' };
  const spellRef = { contentId: '00000000-0000-4000-8000-0000000000c1', revisionId: '00000000-0000-4000-8000-0000000000d1' };
  const origin = { kind: 'content' as const, rulesFamily: 'srd-5.1' as const, content: ref, contentName: 'Fixture Arcanist' };
  const v = view({
    spellcasting: [
      {
        content: ref, name: 'Fixture Arcanist', effectId: 'fixture-spellcasting', classLevel: 1, ability: 'int', attackBonus: 4, saveDc: 12,
        preparation: 'prepared', spellList: 'fixture-list', slotKind: 'spellSlots', slots: [2], primary: true, origin, warnings: [],
        spells: [{ spell: spellRef, name: 'Fixture Veil', level: 1, prepared: true, concentration: false, ritual: false, attack: 'none', origin, diagnostics: [] }],
      },
    ],
  });
  const onStatus = vi.fn();
  const onError = vi.fn();
  render(<CharacterSheet view={v} onChanged={noop} onError={onError} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} initialTab="spells" />);
  const box = screen.getByRole('checkbox', { name: 'Prepared: Fixture Veil' });
  box.focus();
  await user.click(box); // the saved character has no such spell in its list
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith(expect.stringMatching(/removed/)));
  expect(onError).not.toHaveBeenCalled();
  expect(client.saveCharacter).not.toHaveBeenCalled();
  expect(document.activeElement).toBe(box);
});

it('falls back to Play when the requested tab is not offered', () => {
  renderSheet(view(), 'spells');
  expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  expect(screen.getByRole('tabpanel', { name: 'Play' })).toBeTruthy();
});

it('"Report a gap" switches to Notes, pre-fills About and focuses the text, every time it is pressed', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Stats' }));
  const ac = screen.getByRole('region', { name: /^Armor Class:/ });
  await user.click(within(ac).getByRole('heading'));
  await user.click(within(ac).getByRole('button', { name: 'Report a gap: Armor Class' }));
  expect(screen.getByRole('tab', { name: 'Notes' }).getAttribute('aria-selected')).toBe('true');
  const form = screen.getByRole('form', { name: 'New gap note' });
  const text = within(form).getByRole('textbox', { name: /^What was missing or wrong/ });
  await waitFor(() => expect(document.activeElement).toBe(text));
  expect((within(form).getByRole('combobox', { name: /^About/ }) as HTMLSelectElement).value).toBe('field:armorClass');

  // A second report, after leaving Notes and moving focus away, must move focus to the text box again.
  await user.click(screen.getByRole('tab', { name: 'Stats' }));
  text.blur();
  await user.click(within(screen.getByRole('region', { name: /^Initiative:/ })).getByRole('heading'));
  await user.click(within(screen.getByRole('region', { name: /^Initiative:/ })).getByRole('button', { name: 'Report a gap: Initiative' }));
  await waitFor(() => expect(document.activeElement).toBe(text));
  expect((within(form).getByRole('combobox', { name: /^About/ }) as HTMLSelectElement).value).toBe('field:initiative');
});

it('keeps typed text in the gap note box when switching tabs and back', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Notes' }));
  // Choosing Notes by hand keeps focus on the tab (APG); only "Report a gap" moves focus into the text box.
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Notes' }));
  const form = screen.getByRole('form', { name: 'New gap note' });
  const text = within(form).getByRole('textbox', { name: /^What was missing or wrong/ }) as HTMLTextAreaElement;
  await user.type(text, 'Fixture draft: the rule text is unclear');
  await user.click(screen.getByRole('tab', { name: 'Stats' }));
  await user.click(screen.getByRole('tab', { name: 'Notes' }));
  expect((within(screen.getByRole('form', { name: 'New gap note' })).getByRole('textbox', { name: /^What was missing or wrong/ }) as HTMLTextAreaElement).value).toBe('Fixture draft: the rule text is unclear');
});

const withDetails = (details: NonNullable<Character['details']>): CharacterView => {
  const v = view();
  return { ...v, character: { ...v.character, schemaVersion: 9, details } };
};

it('puts the Details form on Notes after session notes and gap notes, with no new tab', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  expect(screen.getAllByRole('tab').map((t) => t.textContent)).toEqual(['Play', 'Inventory', 'Features', 'Stats', 'Notes', 'Manage']);
  await user.click(screen.getByRole('tab', { name: 'Notes' }));
  const panel = screen.getByRole('tabpanel', { name: 'Notes' });
  expect(within(panel).getAllByRole('heading', { level: 3 }).map((h) => h.textContent?.replace(/: \d+ open$/, ''))).toEqual(['Session notes', 'Gap notes', 'Details']);
  expect(within(panel).getByRole('form', { name: 'Details' })).toBeTruthy();
});

it('keeps text typed in the Details form when switching tabs and back', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Notes' }));
  await user.type(within(screen.getByRole('form', { name: 'Details' })).getByRole('textbox', { name: 'Alignment' }), 'Fixture draft alignment');
  await user.click(screen.getByRole('tab', { name: 'Stats' }));
  expect(screen.queryByRole('form', { name: 'Details' })).toBeNull();
  await user.click(screen.getByRole('tab', { name: 'Notes' }));
  const form = screen.getByRole('form', { name: 'Details' });
  expect((within(form).getByRole('textbox', { name: 'Alignment' }) as HTMLInputElement).value).toBe('Fixture draft alignment');
  expect(within(form).getByRole('button', { name: 'Save details' }).getAttribute('aria-disabled')).toBeNull();
});

it('shows Languages and Tools read-only on Stats with a link into Details, and nothing when both are empty', async () => {
  const user = userEvent.setup();
  renderSheet(view(), 'stats');
  expect(screen.queryByRole('button', { name: /Edit in Notes/ })).toBeNull();
  expect(screen.queryByRole('list', { name: 'Languages' })).toBeNull();
  cleanup();
  renderSheet(withDetails({ alignment: 'Fixture neutral', languages: ['Fixture tongue', 'Fixture signs'] }), 'stats');
  expect(screen.getAllByRole('listitem').map((li) => li.textContent)).toEqual(expect.arrayContaining(['Fixture tongue', 'Fixture signs']));
  expect(within(screen.getByRole('list', { name: 'Languages' })).getAllByRole('listitem')).toHaveLength(2);
  expect(screen.queryByRole('list', { name: 'Tools and other' })).toBeNull(); // an empty list shows nothing
  await user.click(screen.getByRole('button', { name: 'Edit in Notes → Details' }));
  expect(screen.getByRole('tab', { name: 'Notes' }).getAttribute('aria-selected')).toBe('true');
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Details' })));
});

it('the Stats link moves focus to the Details heading every time, even when Notes is already the remembered tab', async () => {
  const user = userEvent.setup();
  renderSheet(withDetails({ toolsAndOther: ['Fixture kit'] }), 'stats');
  await user.click(screen.getByRole('button', { name: 'Edit in Notes → Details' }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Details' })));
  await user.click(screen.getByRole('tab', { name: 'Stats' }));
  expect(within(screen.getByRole('list', { name: 'Tools and other' })).getAllByRole('listitem')).toHaveLength(1);
  await user.click(screen.getByRole('button', { name: 'Edit in Notes → Details' }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Details' })));
});

it('opens on the remembered tab, and an explicit initialTab wins over it', () => {
  localStorage.setItem('tomestack.sheetTab.fixture-2', 'stats');
  renderSheet(view());
  expect(screen.getByRole('tab', { name: 'Stats' }).getAttribute('aria-selected')).toBe('true');
  cleanup();
  renderSheet(view(), 'notes');
  expect(screen.getByRole('tab', { name: 'Notes' }).getAttribute('aria-selected')).toBe('true');
  expect(localStorage.getItem('tomestack.sheetTab.fixture-2')).toBe('stats'); // opening on a deep link does not rewrite the memory
});

it('remembers the tab chosen, so the next opening of the same character starts there', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Manage' }));
  cleanup();
  renderSheet(view());
  expect(screen.getByRole('tab', { name: 'Manage' }).getAttribute('aria-selected')).toBe('true');
});

it('renders the print preview after the summary and before the body, matching the grid order', async () => {
  vi.mocked(client.previewExport).mockResolvedValue({ included: [] } as unknown as Awaited<ReturnType<typeof client.previewExport>>);
  vi.mocked(client.info).mockResolvedValue({ version: '0.0.0-fixture' } as unknown as Awaited<ReturnType<typeof client.info>>);
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('button', { name: 'Print…' }));
  const article = document.querySelector('article.sheet') as HTMLElement;
  expect(Array.from(article.children).map((c) => `${c.tagName.toLowerCase()}.${c.className.split(' ')[0]}`)).toEqual(['header.sheet-header', 'section.sheet-summary', 'section.print-sheet', 'div.sheet-body']);
});

it('moves focus to the print preview heading when the preview opens (investigation 2026-10-06 item 9)', async () => {
  vi.mocked(client.previewExport).mockResolvedValue({ included: [] } as unknown as Awaited<ReturnType<typeof client.previewExport>>);
  vi.mocked(client.info).mockResolvedValue({ version: '0.0.0-fixture' } as unknown as Awaited<ReturnType<typeof client.info>>);
  const user = userEvent.setup();
  renderSheet(view());
  const print = screen.getByRole('button', { name: 'Print…' });
  expect(print.getAttribute('aria-controls')).toBeNull(); // 4.1.2: only while the preview exists
  await user.click(print);
  expect(print.getAttribute('aria-controls')).toBe('print-preview');
  const preview = screen.getByRole('region', { name: 'Print preview' });
  expect(preview.id).toBe('print-preview');
  await waitFor(() => expect(document.activeElement).toBe(within(preview).getByRole('heading', { name: 'Print character' })));
});

it('moves focus to Play when the open tab stops being offered', async () => {
  const user = userEvent.setup();
  const fields = view().sheet.fields;
  const withOverride = view({ fields: fields.map((f) => (f.field === 'spellAttack' ? { ...f, override: { field: 'spellAttack', value: 3 }, value: 3 } : f)) });
  const { rerender } = renderSheet(withOverride);
  await user.click(screen.getByRole('tab', { name: 'Spells' }));
  const card = screen.getByRole('region', { name: /^Spell attack bonus:/ });
  await user.click(within(card).getByRole('heading'));
  within(card).getByRole('button', { name: 'Report a gap: Spell attack bonus' }).focus();
  expect(screen.getByRole('tabpanel', { name: 'Spells' }).contains(document.activeElement)).toBe(true);

  rerender(sheetElement(view())); // the save that removed the override: no spell field has a value any more
  expect(screen.queryByRole('tab', { name: 'Spells' })).toBeNull();
  expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Play' }));
});

it('puts lost focus on the selected tab, not always Play, when Spells appears', () => {
  const { rerender } = renderSheet(view(), 'manage');
  expect(screen.getByRole('tab', { name: 'Manage' }).getAttribute('aria-selected')).toBe('true');
  (document.activeElement as HTMLElement | null)?.blur();
  expect(document.activeElement).toBe(document.body);

  const fields = view().sheet.fields;
  rerender(sheetElement(view({ fields: fields.map((f) => (f.field === 'spellAttack' ? { ...f, override: { field: 'spellAttack', value: 3 }, value: 3 } : f)) }), 'manage'));
  expect(screen.getByRole('tab', { name: 'Spells' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Manage' }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Manage' }));
});

it('says so on the Features tab when the character has no features', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Features' }));
  expect(within(screen.getByRole('tabpanel', { name: 'Features' })).getByText('No features yet.')).toBeTruthy();
});

it('falls back from a remembered tab that is not offered, without taking focus from the heading or rewriting the memory', () => {
  localStorage.setItem('tomestack.sheetTab.fixture-2', 'spells');
  renderSheet(view());
  expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).toBe(screen.getByRole('heading', { level: 2 }));
  expect(localStorage.getItem('tomestack.sheetTab.fixture-2')).toBe('spells');
});

it('keeps the header, print preview slot and summary as direct children of the article, with everything else in one body wrapper (ADR-015)', () => {
  renderSheet(view());
  const article = screen.getByRole('article');
  const children = Array.from(article.children).map((c) => `${c.tagName.toLowerCase()}.${c.className.split(' ')[0]}`);
  expect(children).toEqual(['header.sheet-header', 'section.sheet-summary', 'div.sheet-body']);
  const body = article.querySelector('.sheet-body')!;
  expect(within(body as HTMLElement).getByRole('tablist', { name: 'Sheet sections' })).toBeTruthy();
});

const childShape = (article: HTMLElement) => Array.from(article.children).map((c) => `${c.tagName.toLowerCase()}.${c.className.split(' ')[0]}`);

it('opens a rest from the header above the body, focuses its heading, and returns focus to the opener on cancel (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  const short = screen.getByRole('button', { name: 'Short rest…' });
  expect(short.getAttribute('aria-controls')).toBeNull(); // 4.1.2: only while the panel exists
  expect(short.getAttribute('aria-expanded')).toBe('false');
  expect(within(screen.getByRole('tabpanel', { name: 'Play' })).queryByRole('button', { name: /rest…$/ })).toBeNull();
  await user.click(short);
  expect(childShape(screen.getByRole('article'))).toEqual(['header.sheet-header', 'section.sheet-summary', 'div.rest-sheet', 'div.sheet-body']);
  const panel = screen.getByRole('region', { name: 'Short rest' });
  expect(panel.parentElement!.id).toBe('rest-panel');
  expect(short.getAttribute('aria-controls')).toBe('rest-panel');
  await waitFor(() => expect(document.activeElement).toBe(within(panel).getByRole('heading', { name: 'Short rest' })));
  expect(short.getAttribute('aria-expanded')).toBe('true');
  await user.click(within(panel).getByRole('button', { name: 'Cancel rest' }));
  expect(screen.queryByRole('region', { name: 'Short rest' })).toBeNull();
  expect(document.activeElement).toBe(short);
  expect(short.getAttribute('aria-expanded')).toBe('false');
});

it('switches from a short rest to a long rest without writing (Review Focus 3)', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const panel = screen.getByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(document.activeElement).toBe(within(panel).getByRole('heading', { name: 'Long rest' })));
  expect(screen.queryByRole('region', { name: 'Short rest' })).toBeNull();
  expect(client.rest).not.toHaveBeenCalled();
});

it('discards the picked hit dice when switching from a short rest to a long rest', async () => {
  const user = userEvent.setup();
  renderSheet(view({ hitDice: [{ die: 8, total: 2, spent: 0, remaining: 2, classes: ['Fixture'] }] }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const picked = { kind: 'shortRest' as const, basis: 'with-die', manual: [], changes: [{ id: 'hitDie:0', kind: 'hitDie', label: 'Spend a d8', from: 3, to: 8, reason: 'Fixture', die: 8, amount: 5 }] as unknown as Awaited<ReturnType<typeof client.restPreview>>['changes'] };
  vi.mocked(client.restPreview).mockImplementation(async (_id, kind, dice = []) => (dice.length > 0 ? picked : { kind, changes: [], manual: [], basis: 'fixture' }));
  await user.type(await screen.findByRole('spinbutton', { name: 'd8 rolled at the table' }), '5');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  expect(await screen.findByRole('button', { name: /^Remove/ })).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const panel = screen.getByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(vi.mocked(client.restPreview).mock.lastCall).toEqual(['fixture-2', 'longRest', [], []]));
  expect(within(panel).queryByRole('button', { name: /^Remove/ })).toBeNull();
  expect(client.rest).not.toHaveBeenCalled();
});

it('toggles the rest panel closed from its own opener and keeps focus there (D29)', async () => {
  const user = userEvent.setup();
  renderSheet(view());
  const short = screen.getByRole('button', { name: 'Short rest…' });
  await user.click(short);
  expect(screen.getByRole('region', { name: 'Short rest' })).toBeTruthy();
  await user.click(short);
  expect(screen.queryByRole('region', { name: 'Short rest' })).toBeNull();
  expect(short.getAttribute('aria-expanded')).toBe('false');
  expect(document.activeElement).toBe(short);
});

async function startFinish(user: ReturnType<typeof userEvent.setup>, onStatus: () => void) {
  const v = view();
  let resolveRest: (value: CharacterView) => void = () => {};
  vi.mocked(client.rest).mockReturnValue(new Promise<CharacterView>((r) => (resolveRest = r)));
  render(<CharacterSheet view={v} onChanged={noop} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  const short = screen.getByRole('button', { name: 'Short rest…' });
  await user.click(short);
  const finish = await screen.findByRole('button', { name: 'Finish short rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);
  return { short, resolve: () => resolveRest(v) };
}

it('a late finish of a closed rest neither closes the open rest nor moves focus (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const { short, resolve } = await startFinish(user, onStatus);
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const longPanel = screen.getByRole('region', { name: 'Long rest' });
  await waitFor(() => expect(document.activeElement).toBe(within(longPanel).getByRole('heading', { name: 'Long rest' })));
  resolve();
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('Short rest finished: 0 changes applied.'));
  expect(screen.getByRole('region', { name: 'Long rest' })).toBeTruthy();
  expect(document.activeElement).not.toBe(short);
});

it('a late finish after the sheet is gone shows no rest-finished status', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const onChanged = vi.fn();
  const v = view();
  let resolveRest: (value: CharacterView) => void = () => {};
  vi.mocked(client.rest).mockReturnValue(new Promise<CharacterView>((r) => (resolveRest = r)));
  const { unmount } = render(<CharacterSheet view={v} onChanged={onChanged} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const finish = await screen.findByRole('button', { name: 'Finish short rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);
  unmount(); // the user opened another character, or pressed Level up
  resolveRest(v);
  await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1)); // the App decides whether the view still applies
  expect(onStatus).not.toHaveBeenCalled();
});

it('keeps Previous rolls through a rest (spec 2.4c)', async () => {
  const user = userEvent.setup();
  const v = view();
  const rested = changedView(v);
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll).mockResolvedValueOnce(rollRecord('Dexterity check', 7)).mockResolvedValueOnce(rollRecord('Dexterity check', 12));
  vi.mocked(client.rest).mockResolvedValue(rested);
  function Host() {
    const [shown, setShown] = useState(v);
    return <CharacterSheet view={shown} onChanged={setShown} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />;
  }
  render(<Host />);
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  await screen.findByText('Previous rolls (1)');
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const finish = await screen.findByRole('button', { name: 'Finish short rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);
  await waitFor(() => expect(screen.queryByRole('region', { name: 'Short rest' })).toBeNull());
  expect(screen.getByText('Previous rolls (1)')).toBeTruthy();
});

it('a late finish after the opener closed the rest does not steal focus (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const { short, resolve } = await startFinish(user, onStatus);
  await user.click(short); // Cancel rest is inert while a Finish is in flight, so the panel is closed from its opener
  expect(document.activeElement).toBe(short);
  const long = screen.getByRole('button', { name: 'Long rest…' });
  long.focus();
  resolve();
  await waitFor(() => expect(onStatus).toHaveBeenCalled());
  expect(document.activeElement).toBe(long);
});

it('fetches the rest proposal again when the character changes under an open rest (D29)', async () => {
  const user = userEvent.setup();
  const v = view();
  const { rerender } = renderSheet(v);
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  await waitFor(() => expect(client.restPreview).toHaveBeenCalledTimes(1));
  vi.mocked(client.restPreview).mockClear();
  rerender(sheetElement({ ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } }));
  await waitFor(() => expect(client.restPreview).toHaveBeenCalledTimes(1));
});

type Changes = Awaited<ReturnType<typeof client.restPreview>>['changes'];
const tickable = [{ id: 'r1', kind: 'resource', label: 'Fixture charges', from: 1, to: 2, reason: 'Fixture reason' }] as unknown as Changes;
const changedView = (v: CharacterView): CharacterView => ({ ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } });

it('keeps the proposal, and a focused control in it, while it is worked out again, and Finish waits (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  const v = view();
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'longRest', changes: tickable, manual: [], basis: 'one' });
  const { rerender } = renderSheet(v);
  await user.click(screen.getByRole('button', { name: 'Long rest…' }));
  const box = await screen.findByRole('checkbox', { name: /^Fixture charges/ });
  box.focus();
  let resolveNext: (p: Awaited<ReturnType<typeof client.restPreview>>) => void = () => {};
  vi.mocked(client.restPreview).mockReturnValue(new Promise((r) => (resolveNext = r)));
  rerender(sheetElement(changedView(v)));
  expect(await screen.findByText('Updating the proposal…')).toBeTruthy();
  expect(document.body.contains(box)).toBe(true);
  expect(document.activeElement).toBe(box);
  expect(screen.getByRole('button', { name: 'Finish long rest' }).getAttribute('aria-disabled')).toBe('true');
  resolveNext({ kind: 'longRest', changes: tickable, manual: [], basis: 'two' });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Finish long rest' }).getAttribute('aria-disabled')).toBe('false'));
  expect(screen.queryByText('Updating the proposal…')).toBeNull();
});

it('keeps the last proposal, including a picked die, when working it out again fails; Finish stays off (D29)', async () => {
  const user = userEvent.setup();
  const v = view({ hitDice: [{ die: 8, total: 2, spent: 0, remaining: 2, classes: ['Fixture'] }] });
  const picked = { kind: 'shortRest' as const, basis: 'with-die', manual: [], changes: [{ id: 'hitDie:0', kind: 'hitDie', label: 'Spend a d8', from: 3, to: 8, reason: 'Fixture', die: 8, amount: 5 }] as unknown as Changes };
  const { rerender } = renderSheet(v);
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  vi.mocked(client.restPreview).mockImplementation(async (_id, kind, dice = []) => (dice.length > 0 ? picked : { kind, changes: [], manual: [], basis: 'fixture' }));
  await user.type(await screen.findByRole('spinbutton', { name: 'd8 rolled at the table' }), '5');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  await screen.findByRole('button', { name: /^Remove/ });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Finish short rest' }).getAttribute('aria-disabled')).toBe('false'));
  vi.mocked(client.restPreview).mockRejectedValue(new Error('rest.needs-hit-points'));
  rerender(sheetElement(changedView(v)));
  expect(await screen.findByText('Could not work out this rest. Cancel and try again.')).toBeTruthy();
  expect(screen.getByRole('button', { name: /^Remove/ })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Finish short rest' }).getAttribute('aria-disabled')).toBe('true');
});

it('does not report a preview that fails after the rest was cancelled (D29)', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  let rejectPreview: (e: unknown) => void = () => {};
  vi.mocked(client.restPreview).mockReturnValue(new Promise((_, rej) => (rejectPreview = rej)));
  render(<CharacterSheet view={view()} onChanged={noop} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  await user.click(screen.getByRole('button', { name: 'Cancel rest' }));
  rejectPreview(new Error('late'));
  await new Promise((r) => setTimeout(r, 20));
  expect(onError).not.toHaveBeenCalled();
});

it('does not report a print preview lookup that fails after the preview was closed', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  let rejectExport: (e: unknown) => void = () => {};
  vi.mocked(client.previewExport).mockReturnValue(new Promise((_, rej) => (rejectExport = rej)));
  vi.mocked(client.info).mockResolvedValue({ version: '0.0.0-fixture' } as unknown as Awaited<ReturnType<typeof client.info>>);
  render(<CharacterSheet view={view()} onChanged={noop} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  const print = screen.getByRole('button', { name: 'Print…' });
  await user.click(print);
  await user.click(print);
  rejectExport(new Error('late'));
  await new Promise((r) => setTimeout(r, 20));
  expect(onError).not.toHaveBeenCalled();
});

// G6: the export dialog shows the preview's warning, for a backup on opening and for a share once chosen.
it('shows the needs-newer-build warning of the export preview, for a backup and for a share', async () => {
  const user = userEvent.setup();
  const text = 'Opening this needs TomeStack 0.7.0 or later: it holds content or a character that older versions refuse.';
  vi.mocked(client.previewExport).mockResolvedValue({
    purpose: 'share', fileName: 'x.tomestack.zip', characters: [], included: [], omitted: [], gapNotes: 0,
    warnings: [{ code: 'package.needs-newer-build', message: text }],
  } as Awaited<ReturnType<typeof client.previewExport>>);
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Manage' }));
  expect(await screen.findByText(text)).toBeTruthy();
  await user.click(screen.getByRole('radio', { name: /^Share with someone/ }));
  expect(await screen.findByText(text)).toBeTruthy();
  expect(vi.mocked(client.previewExport).mock.calls.map((c) => c[1])).toEqual(['backup', 'share']);
});

it('shows no export warning when the preview has none', async () => {
  const user = userEvent.setup();
  vi.mocked(client.previewExport).mockResolvedValue({ purpose: 'backup', fileName: 'x', characters: [], included: [], omitted: [], gapNotes: 0 } as Awaited<ReturnType<typeof client.previewExport>>);
  renderSheet(view());
  await user.click(screen.getByRole('tab', { name: 'Manage' }));
  await waitFor(() => expect(client.previewExport).toHaveBeenCalled());
  expect(screen.queryByText(/needs TomeStack/)).toBeNull();
});

it('loads the export preview only when Manage opens, and again when the character changes (G6)', async () => {
  const user = userEvent.setup();
  const first = 'Needs 0.7.0 first.';
  const second = 'Needs 0.8.0 second.';
  const preview = (message: string) =>
    ({ purpose: 'backup', fileName: 'x', characters: [], included: [], omitted: [], gapNotes: 0, warnings: [{ code: 'package.needs-newer-build', message }] }) as Awaited<ReturnType<typeof client.previewExport>>;
  vi.mocked(client.previewExport).mockReset();
  vi.mocked(client.previewExport).mockResolvedValueOnce(preview(first)).mockResolvedValueOnce(preview(second));
  const v = view();
  const { rerender } = renderSheet(v);
  await new Promise((r) => setTimeout(r, 20));
  expect(client.previewExport).not.toHaveBeenCalled();
  await user.click(screen.getByRole('tab', { name: 'Manage' }));
  expect(await screen.findByText(first)).toBeTruthy();
  expect(client.previewExport).toHaveBeenCalledTimes(1);
  rerender(sheetElement({ ...v, character: { ...v.character, updatedAt: '2026-10-03T00:00:09Z' } }));
  expect(await screen.findByText(second)).toBeTruthy();
  expect(client.previewExport).toHaveBeenCalledTimes(2);
});

it('says so in plain text when the backup export preview cannot be read (G6)', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  vi.mocked(client.previewExport).mockRejectedValue(new Error('boom'));
  render(<CharacterSheet view={view()} onChanged={noop} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('tab', { name: 'Manage' }));
  const line = await screen.findByText('Could not check whether this package needs a newer TomeStack.');
  expect(line.closest('[role="status"],[role="alert"]')).toBeNull();
  expect(onError).not.toHaveBeenCalled();
});

const dieChange = [{ id: 'hitDie:0', kind: 'hitDie', label: 'Spend a d8', from: 3, to: 8, reason: 'Fixture', die: 8, amount: 5 }] as unknown as Changes;
const d8Pool = [{ die: 8, total: 2, spent: 0, remaining: 2, classes: ['Fixture'] }];

it('keeps focus when a picked die is removed, before and after the new proposal arrives (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  renderSheet(view({ hitDice: d8Pool }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  vi.mocked(client.restPreview).mockImplementation(async (_id, kind, dice = []) => ({ kind, changes: dice.length > 0 ? dieChange : [], manual: [], basis: dice.length > 0 ? 'die' : 'none' }));
  await user.type(await screen.findByRole('spinbutton', { name: 'd8 rolled at the table' }), '5');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  const remove = await screen.findByRole('button', { name: /^Remove/ });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Finish short rest' }).getAttribute('aria-disabled')).toBe('false'));
  let resolveNext: (p: Awaited<ReturnType<typeof client.restPreview>>) => void = () => {};
  vi.mocked(client.restPreview).mockReturnValue(new Promise((r) => (resolveNext = r)));
  await user.click(remove);
  const heading = screen.getByRole('heading', { name: 'Short rest' });
  expect(document.activeElement).toBe(heading);
  expect(screen.getByRole('button', { name: 'Finish short rest' }).getAttribute('aria-disabled')).toBe('true');
  resolveNext({ kind: 'shortRest', changes: [], manual: [], basis: 'none' });
  await waitFor(() => expect(screen.queryByRole('button', { name: /^Remove/ })).toBeNull());
  expect(document.activeElement).toBe(heading);
});

it('keeps a focused checkbox mounted and focused when a die is added (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'shortRest', changes: tickable, manual: [], basis: 'base' });
  renderSheet(view({ hitDice: d8Pool }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const box = await screen.findByRole('checkbox', { name: /^Fixture charges/ });
  await user.type(screen.getByRole('spinbutton', { name: 'd8 rolled at the table' }), '5');
  let resolveNext: (p: Awaited<ReturnType<typeof client.restPreview>>) => void = () => {};
  vi.mocked(client.restPreview).mockReturnValue(new Promise((r) => (resolveNext = r)));
  box.focus();
  fireEvent.click(screen.getByRole('button', { name: 'Add d8' }));
  expect(await screen.findByText('Updating the proposal…')).toBeTruthy();
  expect(document.body.contains(box)).toBe(true);
  expect(document.activeElement).toBe(box);
  resolveNext({ kind: 'shortRest', changes: [...tickable, ...dieChange], manual: [], basis: 'two' });
  await waitFor(() => expect(screen.queryByText('Updating the proposal…')).toBeNull());
  expect(document.activeElement).toBe(box);
});

const twoDice = (dice: { die: number; roll: number }[]) =>
  dice.map((d, i) => ({ id: `hitDie:${i}`, kind: 'hitDie', label: 'Spend a d8', from: 3, to: 3 + d.roll, reason: `Fixture roll ${d.roll}`, die: 8, amount: d.roll }));
const previewByDice = (_id: string, kind: 'shortRest' | 'longRest', dice: { die: number; roll: number }[] = []) =>
  Promise.resolve({ kind, changes: twoDice(dice), manual: [], basis: `dice-${dice.length}` } as unknown as Awaited<ReturnType<typeof client.restPreview>>);

it('keeps focus on the Add button after a die is added, and Add does nothing while it has no valid number (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.restPreview).mockImplementation(previewByDice);
  renderSheet(view({ hitDice: d8Pool }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const add = await screen.findByRole('button', { name: 'Add d8' });
  expect(add.getAttribute('aria-disabled')).toBe('true');
  await user.click(add);
  expect(screen.queryByRole('button', { name: /^Remove/ })).toBeNull();
  await user.type(screen.getByRole('spinbutton', { name: 'd8 rolled at the table' }), '5');
  await user.click(add);
  expect(document.activeElement).toBe(add);
  expect(add.getAttribute('aria-disabled')).toBe('true');
  expect(await screen.findByRole('button', { name: 'Remove d8 (5)' })).toBeTruthy();
  expect(document.activeElement).toBe(add);
});

it('keeps focus on Roll after the last die of the pool is rolled, and neither button acts with no dice left (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.restPreview).mockImplementation(previewByDice);
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll).mockResolvedValue(rollRecord('Hit die', 4, 8));
  renderSheet(view({ hitDice: [{ die: 8, total: 2, spent: 0, remaining: 1, classes: ['Fixture'] }] }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const roll = await screen.findByRole('button', { name: 'Roll a d8' });
  await user.click(roll);
  await screen.findByRole('button', { name: 'Remove d8 (4)' });
  expect(roll.getAttribute('aria-disabled')).toBe('true');
  expect(document.activeElement).toBe(roll);
  await user.click(roll);
  await user.type(screen.getByRole('spinbutton', { name: 'd8 rolled at the table' }), '5');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  await new Promise((r) => setTimeout(r, 20));
  expect(client.roll).toHaveBeenCalledTimes(1);
  expect(screen.getAllByRole('button', { name: /^Remove/ })).toHaveLength(1);
});

it('names each stale row\'s Remove button from the proposal that row belongs to (D29, 4.1.2)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.restPreview).mockImplementation(previewByDice);
  renderSheet(view({ hitDice: d8Pool }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const input = await screen.findByRole('spinbutton', { name: 'd8 rolled at the table' });
  await user.type(input, '5');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  await screen.findByRole('button', { name: 'Remove d8 (5)' });
  await user.type(input, '3');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  await screen.findByRole('button', { name: 'Remove d8 (3)' });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Finish short rest' }).getAttribute('aria-disabled')).toBe('false'));
  vi.mocked(client.restPreview).mockReturnValue(new Promise(() => {}));
  await user.click(screen.getByRole('button', { name: 'Remove d8 (5)' }));
  expect(await screen.findByText('Updating the proposal…')).toBeTruthy();
  const rows = within(screen.getByRole('list', { name: 'Hit dice to spend' })).getAllByRole('listitem');
  expect(rows).toHaveLength(2);
  for (const row of rows) {
    const rolled = /Fixture roll (\d)/.exec(row.textContent ?? '')![1];
    const remove = within(row).getByRole('button');
    expect(remove.textContent).toBe(`Remove d8 (${rolled})`);
    expect(remove.textContent).not.toContain('undefined');
  }
});

it('does nothing when Finish or a stale Remove is pressed while the proposal is being updated (D29)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.restPreview).mockImplementation(previewByDice);
  renderSheet(view({ hitDice: d8Pool }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  await user.type(await screen.findByRole('spinbutton', { name: 'd8 rolled at the table' }), '5');
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  await screen.findByRole('button', { name: 'Remove d8 (5)' });
  await user.type(screen.getByRole('spinbutton', { name: 'd8 rolled at the table' }), '3');
  await waitFor(() => expect(screen.getByRole('button', { name: 'Finish short rest' }).getAttribute('aria-disabled')).toBe('false'));
  vi.mocked(client.restPreview).mockReturnValue(new Promise(() => {}));
  await user.click(screen.getByRole('button', { name: 'Add d8' }));
  expect(await screen.findByText('Updating the proposal…')).toBeTruthy();
  const calls = vi.mocked(client.restPreview).mock.calls.length;
  await user.click(screen.getByRole('button', { name: 'Finish short rest' }));
  expect(client.rest).not.toHaveBeenCalled();
  const remove = screen.getByRole('button', { name: 'Remove d8 (5)' });
  expect(remove.getAttribute('aria-disabled')).toBe('true');
  await user.click(remove);
  expect(screen.getAllByRole('button', { name: /^Remove/ })).toHaveLength(1);
  expect(vi.mocked(client.restPreview).mock.calls.length).toBe(calls);
});

it('closes the print preview from its own button and keeps focus there (4.1.2)', async () => {
  vi.mocked(client.previewExport).mockResolvedValue({ included: [] } as unknown as Awaited<ReturnType<typeof client.previewExport>>);
  vi.mocked(client.info).mockResolvedValue({ version: '0.0.0-fixture' } as unknown as Awaited<ReturnType<typeof client.info>>);
  const user = userEvent.setup();
  renderSheet(view());
  const print = screen.getByRole('button', { name: 'Print…' });
  await user.click(print);
  expect(screen.getByRole('region', { name: 'Print preview' })).toBeTruthy();
  await user.click(print);
  expect(screen.queryByRole('region', { name: 'Print preview' })).toBeNull();
  expect(print.getAttribute('aria-expanded')).toBe('false');
  expect(document.activeElement).toBe(print);
});

it('returns focus to the opener and reports the result when a rest finishes (D29, 2.4.3)', async () => {
  const user = userEvent.setup();
  const v = view();
  const onChanged = vi.fn();
  const onStatus = vi.fn();
  vi.mocked(client.restPreview).mockResolvedValue({
    kind: 'longRest',
    changes: [{ id: 'hp', kind: 'hitPoints', label: 'Hit points', from: 3, to: 8, reason: 'Fixture reason' }] as unknown as Awaited<ReturnType<typeof client.restPreview>>['changes'],
    manual: [],
    basis: 'fixture',
  });
  vi.mocked(client.rest).mockResolvedValue(v);
  render(<CharacterSheet view={v} onChanged={onChanged} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  const long = screen.getByRole('button', { name: 'Long rest…' });
  await user.click(long);
  const finish = await screen.findByRole('button', { name: 'Finish long rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('Long rest finished: 1 change applied.'));
  expect(client.rest).toHaveBeenCalledWith('fixture-2', 'longRest', 'fixture', [], [], []);
  expect(onChanged).toHaveBeenCalledWith(v);
  expect(screen.queryByRole('region', { name: 'Long rest' })).toBeNull();
  expect(document.activeElement).toBe(long);
});

const rollRecord = (label: string, total: number, sides = 20): RollRecord => ({
  formula: `1d${sides}`, mode: 'normal', critical: false, dice: [{ term: 0, sides, value: total, kept: true, fromCritical: false }],
  diceTotal: total, expressionConstant: 0, modifiers: [], total, provenance: { rollId: 'fixture', label },
});

const modeReason = (mode: 'advantage' | 'disadvantage', source: string, against?: string[], automatic = !against) => ({
  mode, source, content: { contentId: 'c1', revisionId: 'r1' }, against, automatic,
});
const statsWith = (id: string, label: string, value: number, units: string, mode?: DerivedValue['mode']): CharacterView => {
  const v = view();
  return { ...v, sheet: { ...v.sheet, fields: [...v.sheet.fields, { ...field(id, label, value, units), mode }] } };
};

it('a save with automatic advantage says so on its button and in its accessible name', async () => {
  const user = userEvent.setup();
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll).mockResolvedValue(rollRecord('Wisdom save', 12));
  const mode = { automatic: 'advantage' as const, reasons: [modeReason('advantage', 'Fixture Swift Instinct'), modeReason('advantage', 'Fixture Light Step')] };
  renderSheet(statsWith('save.wis', 'Wisdom save', 1, 'modifier', mode), 'stats');
  await user.click(screen.getByRole('heading', { name: 'Wisdom save: +1' })); // the card's body is a closed <details>
  await user.click(screen.getByRole('heading', { name: 'Initiative: +1' }));
  const name = 'Roll Wisdom save (advantage: Fixture Swift Instinct, Fixture Light Step)';
  const button = screen.getByRole('button', { name });
  expect(button.textContent).toBe(name);
  await user.click(button);
  expect(vi.mocked(client.roll)).toHaveBeenCalledWith('fixture-2', { field: 'save.wis', mode: 'normal' });
  expect(screen.getByRole('button', { name: 'Roll Initiative' })).toBeTruthy(); // a field with no reason is unchanged
});

it('a field card shows a closed "Why?" above its trace table, with the proficiency sentence (D72 X04)', async () => {
  const user = userEvent.setup();
  const rules = { kind: 'rulesPolicy', rulesFamily: 'srd-5.2.1' } as const;
  const v = statsWith('save.wis', 'Wisdom save', 4, 'modifier');
  const saved = v.sheet.fields.find((f) => f.field === 'save.wis')!;
  saved.trace = [
    { order: 1, operation: 'base', description: 'Wisdom modifier', amount: 1, result: 1, origin: rules },
    { order: 2, operation: 'add', description: 'Proficiency bonus', amount: 3, result: 4, origin: rules },
  ];
  renderSheet(v, 'stats');
  await user.click(screen.getByRole('heading', { name: 'Wisdom save: +4' }));
  const why = screen.getByText('Why?').closest('details')!;
  expect(why.open).toBe(false);
  expect(why.compareDocumentPosition(screen.getByRole('table', { name: /How wisdom save is calculated/ })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  const list = within(why).getByRole('list', { hidden: true, name: 'Why Wisdom save' });
  expect(list.textContent).toMatch(/\+3 Proficiency bonus/);
  expect(why.closest('[aria-live]')).toBeNull();
});

it('a field with an empty trace shows no "Why?"', async () => {
  const user = userEvent.setup();
  renderSheet(statsWith('save.dex', 'Dexterity save', 1, 'modifier'), 'stats');
  await user.click(screen.getByRole('heading', { name: 'Dexterity save: +1' }));
  expect(screen.queryByText('Why?')).toBeNull();
});

it('advantage and disadvantage together read as normal with the reason', async () => {
  const user = userEvent.setup();
  const mode = { automatic: 'normal' as const, reasons: [modeReason('advantage', 'Fixture Swift Instinct'), modeReason('disadvantage', 'Fixture Heavy Armor')] };
  renderSheet(statsWith('save.dex', 'Dexterity save', 1, 'modifier', mode), 'stats');
  await user.click(screen.getByRole('heading', { name: 'Dexterity save: +1' }));
  expect(screen.getByRole('button', { name: 'Roll Dexterity save (normal: advantage and disadvantage cancel)' })).toBeTruthy();
});

it('an assisted mode is a note, and the button stays normal', async () => {
  const user = userEvent.setup();
  const mode = {
    automatic: 'normal' as const,
    reasons: [modeReason('advantage', 'Fixture Swift Instinct', ['paralyzed', 'stunned']), modeReason('disadvantage', 'Fixture Dark Fear', undefined, false)],
  };
  renderSheet(statsWith('save.wis', 'Wisdom save', 1, 'modifier', mode), 'stats');
  await user.click(screen.getByRole('heading', { name: 'Wisdom save: +1' }));
  expect(screen.getByRole('button', { name: 'Roll Wisdom save' })).toBeTruthy();
  expect(screen.getByText('Advantage against paralyzed, stunned (Fixture Swift Instinct)')).toBeTruthy();
  expect(screen.getByText('Disadvantage (Fixture Dark Fear), not applied automatically')).toBeTruthy();
});

it('two assisted reasons that would cancel leave the button as it was and keep both notes', async () => {
  const user = userEvent.setup();
  const mode = { automatic: 'normal' as const, reasons: [modeReason('advantage', 'Fixture A', undefined, false), modeReason('disadvantage', 'Fixture B', undefined, false)] };
  renderSheet(statsWith('save.wis', 'Wisdom save', 1, 'modifier', mode), 'stats');
  await user.click(screen.getByRole('heading', { name: 'Wisdom save: +1' }));
  expect(screen.getByRole('button', { name: 'Roll Wisdom save' })).toBeTruthy();
  expect(screen.getByText('Advantage (Fixture A), not applied automatically')).toBeTruthy();
  expect(screen.getByText('Disadvantage (Fixture B), not applied automatically')).toBeTruthy();
});

it('an assisted reason beside an automatic one of the same mode is a note, not a source', async () => {
  const user = userEvent.setup();
  const mode = { automatic: 'advantage' as const, reasons: [modeReason('advantage', 'Fixture A'), modeReason('advantage', 'Fixture B', undefined, false)] };
  renderSheet(statsWith('save.wis', 'Wisdom save', 1, 'modifier', mode), 'stats');
  await user.click(screen.getByRole('heading', { name: 'Wisdom save: +1' }));
  expect(screen.getByRole('button', { name: 'Roll Wisdom save (advantage: Fixture A)' })).toBeTruthy();
  expect(screen.getByText('Advantage (Fixture B), not applied automatically')).toBeTruthy();
});

it('fly speed shows only when above 0', () => {
  renderSheet(statsWith('speed.fly', 'Fly speed', 0, 'feet'), 'stats');
  expect(screen.queryByRole('heading', { name: /Fly speed/ })).toBeNull();
  cleanup();
  renderSheet(statsWith('speed.fly', 'Fly speed', 30, 'feet'), 'stats');
  expect(screen.getByRole('heading', { name: 'Fly speed: 30 ft.' })).toBeTruthy();
});

it('an override of 0 keeps the fly speed card and its Remove override button', async () => {
  const user = userEvent.setup();
  const v = statsWith('speed.fly', 'Fly speed', 0, 'feet');
  const overridden: CharacterView = {
    ...v,
    sheet: {
      ...v.sheet,
      fields: v.sheet.fields.map((f) =>
        f.field === 'speed.fly' ? { ...f, value: 0, computedValue: 30, override: { field: 'speed.fly', value: 0, reason: 'Fixture grounded' } } : f),
    },
  };
  renderSheet(overridden, 'stats');
  await user.click(screen.getByRole('heading', { name: /Fly speed/ }));
  expect(screen.getByRole('button', { name: /Remove override/ })).toBeTruthy();
});

it('the player picker says that automatic modes are combined with its choice', () => {
  renderSheet(view());
  const group = screen.getByRole('group', { name: 'd20 rolls' });
  const described = document.getElementById(group.getAttribute('aria-describedby')!);
  expect(described?.textContent).toBe('Automatic modes from the sheet are combined with this choice; advantage and disadvantage cancel.');
});

describe('a roll record Spend', () => {
  const spendRecord: RollRecord = { ...rollRecord('Fixture Strike', 12), spend: { contentId: 'c1', resourceId: 'pool', amount: 1 } };
  const pool = [
    {
      content: { contentId: 'c1', revisionId: 'r1' }, contentName: 'Fixture', effectId: 'e1', resourceId: 'pool', label: 'Fixture pool',
      maximum: 3, spent: 0, current: 3, trace: [], warnings: [], automation: 'automatic' as const, recoveries: [],
    },
  ];

  it('a refused record Spend can be retried', async () => {
    const user = userEvent.setup();
    const act = vi.fn().mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    render(<RollResult record={spendRecord} resources={pool} features={[]} act={act} />);
    const button = screen.getByRole<HTMLButtonElement>('button', { name: 'Spend 1 Fixture pool (3 left)' });
    await user.click(button);
    await waitFor(() => expect(act).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(button.disabled).toBe(false));
    await user.click(button);
    await waitFor(() => expect(act).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(button.disabled).toBe(true)); // spent for good once it worked
  });

  it('a rejecting act also leaves the record Spend usable', async () => {
    const user = userEvent.setup();
    const act = vi.fn().mockRejectedValue(new Error('fixture refusal'));
    render(<RollResult record={spendRecord} resources={pool} features={[]} act={act} />);
    const button = screen.getByRole<HTMLButtonElement>('button', { name: 'Spend 1 Fixture pool (3 left)' });
    await user.click(button);
    await waitFor(() => expect(act).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(button.disabled).toBe(false));
  });

  it('a roll record names its automatic mode', () => {
    const record: RollRecord = { ...rollRecord('Wisdom save', 14), mode: 'advantage', automaticMode: 'advantage', modeSources: ['Fixture Swift Instinct', 'Fixture Light Step'] };
    render(<RollResult record={record} resources={[]} features={[]} act={() => {}} />);
    expect(screen.getByText(/Wisdom save: 14/).closest('p')!.textContent).toBe('Wisdom save: 14 (1d20, advantage from Fixture Swift Instinct, Fixture Light Step)');
  });
});

describe('a record Spend after a failed play change', () => {
  const pool = {
    content: { contentId: 'c1', revisionId: 'r1' }, contentName: 'Fixture', effectId: 'e1', resourceId: 'pool', label: 'Fixture pool',
    maximum: 3, spent: 0, current: 3, trace: [], warnings: [], automation: 'automatic' as const, recoveries: [],
  };
  async function pressSpend(failure: Error) {
    const user = userEvent.setup();
    vi.mocked(client.roll).mockReset();
    vi.mocked(client.roll).mockResolvedValue({ ...rollRecord('Dexterity check', 9), spend: { contentId: 'c1', resourceId: 'pool', amount: 1 } });
    vi.mocked(client.play).mockReset();
    vi.mocked(client.play).mockRejectedValue(failure);
    const v = view();
    renderSheet({ ...v, sheet: { ...v.sheet, resources: [pool] } });
    await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
    const spend = await screen.findByRole<HTMLButtonElement>('button', { name: 'Spend 1 Fixture pool (3 left)' });
    await user.click(spend);
    await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
    return spend;
  }

  it('a timeout (outcome unknown) keeps the record Spend used', async () => {
    const spend = await pressSpend(new TomeStackError({ code: 'timeout', message: 'No response to play.' }));
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(spend.disabled).toBe(true);
  });

  it('a service refusal gives the record Spend back', async () => {
    const spend = await pressSpend(new TomeStackError({ code: 'character.stale', message: 'stale' }));
    await waitFor(() => expect(spend.disabled).toBe(false));
  });
});

it('moves the replaced Last roll into Previous rolls (D28)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll).mockResolvedValueOnce(rollRecord('Dexterity check', 9)).mockResolvedValueOnce(rollRecord('Dexterity check', 15));
  renderSheet(view());
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  expect(screen.queryByText(/^Previous rolls/)).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  const log = await screen.findByText('Previous rolls (1)');
  expect(within(screen.getByRole('region', { name: 'Last roll' })).getByText(/Dexterity check: 15/)).toBeTruthy();
  expect(log.closest('details')!.textContent).toMatch(/Dexterity check: 9/);
});

it('rolls typed dice into Last roll, labelled Dice, and moves the field roll into the log (item 8)', async () => {
  const user = userEvent.setup();
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll).mockResolvedValueOnce(rollRecord('Dexterity check', 9));
  vi.mocked(client.rollDice).mockReset();
  vi.mocked(client.rollDice).mockResolvedValueOnce(rollRecord('Dice', 11, 6));
  renderSheet(view());
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  await user.type(screen.getByRole('textbox', { name: 'Dice to roll' }), '2d6+3');
  await user.click(screen.getByRole('button', { name: 'Roll dice' }));
  expect(vi.mocked(client.rollDice)).toHaveBeenCalledWith('2d6+3', undefined, 'Dice');
  const log = await screen.findByText('Previous rolls (1)');
  expect(within(screen.getByRole('region', { name: 'Last roll' })).getByText(/Dice: 11/)).toBeTruthy();
  expect(log.closest('details')!.textContent).toMatch(/Dexterity check: 9/);
});

it('a refused dice roll changes neither Last roll nor the log, and the sheet shows no banner for it', async () => {
  const user = userEvent.setup();
  const onError = vi.fn();
  vi.mocked(client.rollDice).mockReset();
  vi.mocked(client.rollDice).mockRejectedValueOnce(new TomeStackError({ code: 'bad-request', message: 'Too many dice.', diagnostics: [{ code: 'dice.too-many-dice', message: 'Too many dice.' }] }));
  render(<CharacterSheet view={view()} onChanged={noop} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.type(screen.getByRole('textbox', { name: 'Dice to roll' }), '1000d6{Enter}');
  expect(await screen.findByText('Too many dice.')).toBeTruthy();
  expect(onError).not.toHaveBeenCalled();
  expect(screen.queryByText(/^Previous rolls/)).toBeNull();
  expect(vi.mocked(client.rollDice)).toHaveBeenCalledWith('1000d6', undefined, 'Dice');
});

it('logs a rest hit die without showing it, and keeps the log newest first by time when the Last roll is replaced (D28)', async () => {
  const user = userEvent.setup();
  let clock = 1_760_000_000_000;
  vi.spyOn(Date, 'now').mockImplementation(() => (clock += 60_000));
  vi.mocked(client.roll).mockReset();
  vi.mocked(client.roll)
    .mockResolvedValueOnce(rollRecord('Roll A', 9))
    .mockResolvedValueOnce(rollRecord('Hit die H', 5, 8))
    .mockResolvedValueOnce(rollRecord('Roll B', 15));
  vi.mocked(client.restPreview).mockResolvedValue({ kind: 'shortRest', changes: [], manual: [], basis: 'fixture' });
  renderSheet(view({ hitDice: [{ die: 8, total: 2, spent: 0, remaining: 2, classes: ['Fixture'] }] }));
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  await user.click(await screen.findByRole('button', { name: 'Roll a d8' }));
  const lastRoll = screen.getByRole('region', { name: 'Last roll' });
  expect(within(lastRoll).getByText(/Roll A: 9/)).toBeTruthy();
  expect(within(lastRoll).queryByText(/Hit die H/)).toBeNull();
  expect((await screen.findByText('Previous rolls (1)')).closest('details')!.textContent).toMatch(/Hit die H: 5/);
  await user.click(screen.getByRole('button', { name: 'Roll Dexterity check (+1)' }));
  const log = (await screen.findByText('Previous rolls (2)')).closest('details')!;
  expect(within(lastRoll).getByText(/Roll B: 15/)).toBeTruthy();
  const items = within(log).getAllByRole('listitem').map((li) => li.textContent ?? '');
  expect(items[0]).toMatch(/Hit die H: 5/);
  expect(items[1]).toMatch(/Roll A: 9/);
  vi.mocked(Date.now).mockRestore();
});

it('says why "Level up" is unavailable at level 20, and does nothing when pressed (R43)', async () => {
  const user = userEvent.setup();
  const onLevelUp = vi.fn();
  const v = view();
  v.character = { ...v.character, level: 20 };
  render(<CharacterSheet view={v} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={onLevelUp} onMakeChoices={noop} onArchiveChanged={noop} />);
  const button = screen.getByRole('button', { name: 'Level up' });
  expect(button.getAttribute('aria-disabled')).toBe('true'); // never native disabled: it stays reachable with its reason
  expect(button.hasAttribute('disabled')).toBe(false);
  expect(button.getAttribute('aria-describedby')).toBe('level-up-hint');
  expect(document.getElementById('level-up-hint')!.textContent).toBe('Already at level 20, the highest level.');
  await user.click(button);
  expect(onLevelUp).not.toHaveBeenCalled();
  cleanup();
  render(<CharacterSheet view={view()} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={onLevelUp} onMakeChoices={noop} onArchiveChanged={noop} />);
  const open = screen.getByRole('button', { name: 'Level up' });
  expect(open.getAttribute('aria-disabled')).toBeNull();
  expect(open.hasAttribute('aria-describedby')).toBe(false);
  expect(document.getElementById('level-up-hint')).toBeNull();
  await user.click(open);
  expect(onLevelUp).toHaveBeenCalledTimes(1);
});

// R161/R182: character.save is compare-and-set on updatedAt.
function withUpdatedAt(v: CharacterView, updatedAt: string, gp = 0): CharacterView {
  return { ...v, character: { ...v.character, updatedAt, currency: { cp: 0, sp: 0, ep: 0, gp, pp: 0 } } };
}

it('feeds the updatedAt of each successful save to the next queued save, so two quick edits both save (R182)', async () => {
  const user = userEvent.setup();
  const v = view();
  vi.mocked(client.saveCharacter).mockReset();
  vi.mocked(client.saveCharacter)
    .mockResolvedValueOnce(withUpdatedAt(v, '2026-10-03T00:00:05.1234567+00:00', 20))
    .mockResolvedValueOnce(withUpdatedAt(v, '2026-10-03T00:00:09.7654321+00:00', 30));
  renderSheet(v, 'inventory');
  const gold = screen.getByRole('spinbutton', { name: 'Gold (gp)' });
  await user.clear(gold);
  await user.type(gold, '20');
  await user.click(screen.getByRole('button', { name: 'Save currency' }));
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.saveCharacter).mock.calls[0]![0].updatedAt).toBe('2026-10-03T00:00:00Z'); // the version the sheet showed
  await user.clear(screen.getByRole('spinbutton', { name: 'Gold (gp)' }));
  await user.type(screen.getByRole('spinbutton', { name: 'Gold (gp)' }), '30');
  await user.click(screen.getByRole('button', { name: 'Save currency' }));
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(2));
  expect(vi.mocked(client.saveCharacter).mock.calls[1]![0].updatedAt).toBe('2026-10-03T00:00:05.1234567+00:00'); // verbatim, never reparsed
});

it('shows the refusal, brings in the current copy and leaves focus where it was when a save is stale (R182)', async () => {
  const user = userEvent.setup();
  const v = view();
  const current = withUpdatedAt(v, '2026-10-03T00:01:00Z', 7);
  const refusal = new TomeStackError({ code: 'character.stale', message: "This character changed since you opened it. Your change wasn't saved; the current copy is shown." });
  vi.mocked(client.saveCharacter).mockReset();
  vi.mocked(client.saveCharacter).mockRejectedValue(refusal);
  vi.mocked(client.getCharacter).mockResolvedValue(current);
  const onError = vi.fn();
  const onChanged = vi.fn();
  render(<CharacterSheet view={v} onChanged={onChanged} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} initialTab="inventory" />);
  await user.clear(screen.getByRole('spinbutton', { name: 'Gold (gp)' }));
  await user.type(screen.getByRole('spinbutton', { name: 'Gold (gp)' }), '20');
  const save = screen.getByRole('button', { name: 'Save currency' });
  await user.click(save);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(current));
  expect(onError).toHaveBeenCalledWith(refusal); // the app's alert shows its text
  expect(client.getCharacter).toHaveBeenCalledWith('fixture-2');
  expect(document.activeElement).toBe(save); // not <body>
  expect(screen.getByRole<HTMLInputElement>('spinbutton', { name: 'Gold (gp)' }).value).toBe('20'); // what was typed stays
});

it('sends a save started while a play reply is pending with the play reply\'s updatedAt, keeping both changes (R188)', async () => {
  const user = userEvent.setup();
  const v = view();
  const hit = pending();
  const played: CharacterView = {
    ...v,
    character: { ...v.character, updatedAt: '2026-10-03T00:00:05.5000000+00:00', play: { temporaryHitPoints: 3, resources: [], conditions: [], exhaustion: 0 } },
  };
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockReturnValueOnce(hit.promise);
  vi.mocked(client.saveCharacter).mockReset();
  vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ ...v, character: { ...c, updatedAt: '2026-10-03T00:00:09.0000000+00:00' } }));
  renderSheet(v);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' })); // its reply is held
  await user.click(screen.getByRole('tab', { name: 'Inventory' }));
  const gold = screen.getByRole('spinbutton', { name: 'Gold (gp)' });
  await user.clear(gold);
  await user.type(gold, '20');
  await user.click(screen.getByRole('button', { name: 'Save currency' }));
  expect(client.saveCharacter).not.toHaveBeenCalled(); // waits behind the play command
  hit.settle(played);
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(1));
  const sent = vi.mocked(client.saveCharacter).mock.calls[0]![0];
  expect(sent.updatedAt).toBe('2026-10-03T00:00:05.5000000+00:00'); // the stamp the play left, not the one the sheet showed
  expect(sent.play?.temporaryHitPoints).toBe(3);
  expect(sent.currency?.gp).toBe(20);
});

it('runs a rest finish in the save queue, so a save started meanwhile names the rest\'s stamp (R188)', async () => {
  const user = userEvent.setup();
  const v = view();
  const held = pending();
  const rested: CharacterView = { ...v, character: { ...v.character, updatedAt: '2026-10-03T00:00:06.0000000+00:00' } };
  vi.mocked(client.rest).mockReturnValueOnce(held.promise);
  vi.mocked(client.saveCharacter).mockReset();
  vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ ...v, character: { ...c, updatedAt: '2026-10-03T00:00:09.0000000+00:00' } }));
  renderSheet(v);
  await user.click(screen.getByRole('button', { name: 'Short rest…' }));
  const finish = await screen.findByRole('button', { name: 'Finish short rest' });
  await waitFor(() => expect(finish.getAttribute('aria-disabled')).toBe('false'));
  await user.click(finish);
  await waitFor(() => expect(client.rest).toHaveBeenCalled());
  await user.click(screen.getByRole('tab', { name: 'Inventory' }));
  const gold = screen.getByRole('spinbutton', { name: 'Gold (gp)' });
  await user.clear(gold);
  await user.type(gold, '20');
  await user.click(screen.getByRole('button', { name: 'Save currency' }));
  expect(client.saveCharacter).not.toHaveBeenCalled();
  held.settle(rested);
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(1));
  expect(vi.mocked(client.saveCharacter).mock.calls[0]![0].updatedAt).toBe('2026-10-03T00:00:06.0000000+00:00');
});

it('keeps the same onError identity when the app passes a new onChanged, so no panel refetches (R182)', async () => {
  const v = view();
  const props = { onError: noop, onStatus: noop, onLevelUp: noop, onMakeChoices: noop, onArchiveChanged: noop, initialTab: 'inventory' as const };
  const { rerender } = render(<CharacterSheet view={v} onChanged={() => {}} {...props} />);
  await waitFor(() => expect(client.listContent).toHaveBeenCalled());
  const counts = () => [client.listContent, client.snapshots, client.availableUpdates, client.listGapNotes, client.attachment].map((f) => vi.mocked(f).mock.calls.length);
  const before = counts();
  for (let i = 0; i < 3; i += 1) rerender(<CharacterSheet view={v} onChanged={() => {}} {...props} />); // a new inline onChanged each time, as the app does
  await new Promise((resolve) => setTimeout(resolve, 50));
  expect(counts()).toEqual(before);
});

it('adopts the current copy after a stale refusal even when its stamp sorts lower than the queue latest (R182)', async () => {
  const user = userEvent.setup();
  const v = view();
  const high = withUpdatedAt(v, '2026-10-03T00:10:00Z', 20);
  const fresh = withUpdatedAt(v, '2026-10-03T00:05:00+00:00', 7); // sorts lower: a clock stepped back, and Z against +00:00
  vi.mocked(client.saveCharacter).mockReset();
  vi.mocked(client.saveCharacter)
    .mockResolvedValueOnce(high)
    .mockRejectedValueOnce(new TomeStackError({ code: 'character.stale', message: 'stale' }))
    .mockResolvedValueOnce(withUpdatedAt(v, '2026-10-03T00:06:00+00:00', 9));
  vi.mocked(client.getCharacter).mockResolvedValue(fresh);
  const onChanged = vi.fn();
  render(<CharacterSheet view={v} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} initialTab="inventory" />);
  const setGold = async (n: string) => {
    await user.clear(screen.getByRole('spinbutton', { name: 'Gold (gp)' }));
    await user.type(screen.getByRole('spinbutton', { name: 'Gold (gp)' }), n);
    await user.click(screen.getByRole('button', { name: 'Save currency' }));
  };
  await setGold('20');
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(1));
  await setGold('30');
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(fresh));
  await setGold('9');
  await waitFor(() => expect(client.saveCharacter).toHaveBeenCalledTimes(3));
  expect(vi.mocked(client.saveCharacter).mock.calls[2]![0].updatedAt).toBe('2026-10-03T00:05:00+00:00'); // not wedged on the higher stamp
});

it('keeps the strip and the Undo busy while a turn confirm is in flight, and joins notices with a separator (D43 S3)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  vi.mocked(client.turnPreview).mockResolvedValue({
    at: 'turnStart', changes: [{ id: 'round', kind: 'round', label: 'Round', from: 2, to: 3, reason: 'A new round', origin: {} as never }], choices: [], manual: [], prompts: [], updatedAt: '2026-10-03T00:00:00Z',
  });
  let settle: (v: CharacterView) => void = noop;
  vi.mocked(client.turn).mockReturnValue(new Promise<CharacterView>((resolve) => { settle = resolve; }));
  render(<CharacterSheet view={combatView(2, { undoLabel: 'switch to X' })} onChanged={noop} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Start my turn' }));
  const confirm = await screen.findByRole('button', { name: 'Confirm start of turn' });
  await waitFor(() => expect(confirm.getAttribute('aria-disabled')).toBe('false'));
  await user.click(confirm);
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'End encounter' }).disabled).toBe(true));
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Undo switch to X' }).disabled).toBe(true);
  settle(combatView(3, { notices: [{ code: 'a', message: 'First' }, { code: 'b', message: 'Second' }] }));
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('First · Second'));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'End encounter' }).disabled).toBe(false));
});

// D43 S7 (Task 7.6a): damage routing at the sheet level. The preview stamp is the view's, as on a real server.
const braceResponse = { contentId: 'c-bulwark', contentName: 'Fixture Bulwark', responseId: 'brace', label: 'Brace', phase: 'damageReduction' as const, amount: 3, available: true, survived: false };
const respondingView = () => view({ responses: [braceResponse] });
const hitPreview = (over: object = {}) => ({
  characterId: 'fixture-2', amount: 1, reduction: 0, outcome: { amount: 1, effect: 'none' as const, adjusted: 1, absorbed: 0, lost: 1, warnings: [] },
  after: { maximum: 8, current: 7, temporary: 0 }, concentrationEnds: false, offers: [{ response: braceResponse, offered: true }], rolls: [], notes: [], updatedAt: '2026-10-03T00:00:00Z', ...over,
});

it('with damage responses, Take damage opens the damage panel; without them it applies in one step', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.damagePreview).mockResolvedValue(hitPreview({ amount: 4 }));
  const first = render(sheetElement(respondingView()));
  await user.type(screen.getByRole('spinbutton', { name: 'Amount' }), '4');
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  expect(await screen.findByRole('button', { name: 'Apply damage' })).toBeTruthy();
  expect(client.damagePreview).toHaveBeenCalledWith('fixture-2', 4, undefined, []);
  first.unmount();

  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValue(view());
  render(sheetElement(view()));
  await user.type(screen.getByRole('spinbutton', { name: 'Amount' }), '4');
  await user.click(screen.getByRole('button', { name: 'Take damage' }));
  await waitFor(() => expect(client.play).toHaveBeenCalledWith('fixture-2', { action: 'damage', amount: 4 }));
  expect(screen.queryByRole('button', { name: 'Apply damage' })).toBeNull();
});

it('each press of Lose 1 hit point reopens a fresh damage panel, also over one that shows Damage applied', async () => {
  const user = userEvent.setup();
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.damagePreview).mockResolvedValue(hitPreview());
  render(sheetElement(respondingView()));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await screen.findByRole('button', { name: 'Apply damage' });
  expect(client.damagePreview).toHaveBeenCalledTimes(1);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(client.damagePreview).toHaveBeenCalledTimes(2)); // a new panel asked again
  expect(screen.getAllByRole('button', { name: 'Apply damage' })).toHaveLength(1);
});

it('Apply with nothing chosen is plain damage: Undo is offered and restores the hit points', async () => {
  const user = userEvent.setup();
  const v = respondingView();
  const damaged = { ...v, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } };
  const onChanged = vi.fn();
  const element = (shown: CharacterView) => <CharacterSheet view={shown} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />;
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.damagePreview).mockResolvedValue(hitPreview());
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockResolvedValueOnce(damaged).mockResolvedValue(v);
  const { rerender } = render(element(v));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  const apply = await screen.findByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(client.play).toHaveBeenCalledWith('fixture-2', { action: 'damage', amount: 1 }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(damaged));
  rerender(element(damaged));
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Apply damage' })).toBeNull());
  await user.click(await screen.findByRole('button', { name: 'Undo last change: damage 1' }));
  await waitFor(() => expect(client.play).toHaveBeenLastCalledWith('fixture-2', { action: 'setHitPoints', amount: 8 }));
});

it('a timeout while applying a response reloads the stored character, closes the panel and says so', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const onChanged = vi.fn();
  const fresh = withUpdatedAt(respondingView(), '2026-10-03T00:00:09Z');
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.damagePreview).mockResolvedValue(hitPreview());
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockRejectedValue(new TomeStackError({ code: 'timeout', message: 'No answer.' }));
  vi.mocked(client.getCharacter).mockReset();
  vi.mocked(client.getCharacter).mockResolvedValue(fresh);
  render(<CharacterSheet view={respondingView()} onChanged={onChanged} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('The reply did not arrive. The sheet was reloaded; check hit points before trying again.'));
  expect(client.getCharacter).toHaveBeenCalledWith('fixture-2');
  expect(onChanged).toHaveBeenCalledWith(fresh);
  expect(client.play).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole('button', { name: 'Apply damage' })).toBeNull();
});

it('a timeout whose reload also fails says the sheet could not be reloaded', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const onError = vi.fn();
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.damagePreview).mockResolvedValue(hitPreview());
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockRejectedValue(new TomeStackError({ code: 'timeout', message: 'No answer.' }));
  vi.mocked(client.getCharacter).mockReset();
  vi.mocked(client.getCharacter).mockRejectedValue(new Error('offline'));
  render(<CharacterSheet view={respondingView()} onChanged={noop} onError={onError} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('The reply did not arrive and the sheet could not be reloaded. Reload before trying again.'));
  expect(onStatus).not.toHaveBeenCalledWith(expect.stringContaining('was reloaded'));
  expect(onError).toHaveBeenCalled();
});

it('the reload after a damage timeout waits its turn in the save queue, so a later apply is not rolled back by it', async () => {
  const user = userEvent.setup();
  const onChanged = vi.fn();
  const v = respondingView();
  const stale = v; // the stored copy as the reload read it, before panel 2's hit
  const afterPanel2 = { ...v, character: { ...v.character, updatedAt: '2026-10-03T00:00:05Z' }, sheet: { ...v.sheet, hitPoints: { maximum: 8, current: 7, temporary: 0 } } };
  const reload = pending();
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.damagePreview).mockResolvedValue(hitPreview());
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockRejectedValueOnce(new TomeStackError({ code: 'timeout', message: 'No answer.' })).mockResolvedValueOnce(afterPanel2);
  vi.mocked(client.getCharacter).mockReset();
  vi.mocked(client.getCharacter).mockReturnValue(reload.promise);
  render(<CharacterSheet view={v} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);

  // Panel 1: a response is applied, the reply never arrives (timeout), and the reload of the stored copy is on its way.
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const first = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(first.getAttribute('aria-disabled')).toBe('false'));
  await user.click(first);
  await waitFor(() => expect(client.getCharacter).toHaveBeenCalledTimes(1));

  // Panel 2: plain damage applied while that reload is still in flight.
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  const second = await screen.findByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(second.getAttribute('aria-disabled')).toBe('false'));
  await user.click(second);
  await new Promise((r) => setTimeout(r, 30)); // an unqueued apply would have finished and been shown by now

  // The late reload reply must not be shown after panel 2's apply.
  reload.settle(stale);
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(afterPanel2));
  expect(onChanged.mock.calls.at(-1)![0]).toBe(afterPanel2);
});

it('a timeout from the first damage panel after a second press reloads, says so, and leaves the new panel open', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const fresh = withUpdatedAt(respondingView(), '2026-10-03T00:00:09Z');
  let timeOut: (error: unknown) => void = noop;
  const first = new Promise<CharacterView>((_resolve, reject) => {
    timeOut = reject;
  });
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.damagePreview).mockResolvedValue(hitPreview());
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockReturnValueOnce(first);
  vi.mocked(client.getCharacter).mockReset();
  vi.mocked(client.getCharacter).mockResolvedValue(fresh);
  render(<CharacterSheet view={respondingView()} onChanged={noop} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' })); // a fresh panel replaces the one that is applying
  await waitFor(() => expect(client.damagePreview).toHaveBeenCalledTimes(3));

  timeOut(new TomeStackError({ code: 'timeout', message: 'No answer.' }));
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('The reply did not arrive. The sheet was reloaded; check hit points before trying again.'));
  expect(client.getCharacter).toHaveBeenCalledWith('fixture-2');
  expect(screen.getByRole('button', { name: 'Apply damage' })).toBeTruthy(); // the new panel stays open
});

it('the reply of a replaced damage panel does not close the new panel', async () => {
  const user = userEvent.setup();
  const onChanged = vi.fn();
  const first = pending();
  vi.mocked(client.damagePreview).mockReset();
  vi.mocked(client.damagePreview).mockResolvedValue(hitPreview());
  vi.mocked(client.play).mockReset();
  vi.mocked(client.play).mockReturnValueOnce(first.promise);
  render(<CharacterSheet view={respondingView()} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await user.click(await screen.findByRole('checkbox', { name: /Brace/ }));
  const apply = screen.getByRole('button', { name: 'Apply damage' });
  await waitFor(() => expect(apply.getAttribute('aria-disabled')).toBe('false'));
  await user.click(apply);
  await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' })); // a fresh panel replaces the one that is applying
  await waitFor(() => expect(client.damagePreview).toHaveBeenCalledTimes(3));
  first.settle(respondingView());
  await waitFor(() => expect(onChanged).toHaveBeenCalled()); // the applied damage still reaches the sheet
  expect(await screen.findByRole('button', { name: 'Apply damage' })).toBeTruthy(); // and the new panel stays open
});

// D73: the quickbar spends once. A pin whose name says "and spend" rolls, then spends through the sheet's one spender (the one the Last roll's Spend uses); any other pin only rolls.
describe('the quickbar', () => {
  const tideRef = { contentId: 'c-tide', revisionId: 'r-tide' };
  const focusPool = (current: number) => ({
    content: tideRef, contentName: 'Fixture Tide', effectId: 'e-focus', resourceId: 'focus', label: 'Focus', maximum: 5, spent: 5 - current, current,
    trace: [], warnings: [], automation: 'automatic', recoveries: [],
  });
  const waveEffect = (id: string, label: string) => ({ id, type: 'roll', automation: 'automatic', label, dice: '2d6', resourceId: 'focus', cost: 2, available: true });
  const tideView = (current = 4, stamp = '2026-10-03T00:00:00Z'): CharacterView => {
    const v = view({
      resources: [focusPool(current)],
      features: [{ content: tideRef, name: 'Fixture Tide', kind: 'feature', origin: {}, automation: 'automatic', diagnostics: [], effects: [waveEffect('wave', 'Tide Wave'), waveEffect('wave2', 'Tide Crest')] }],
    } as never);
    return { ...v, character: { ...v.character, updatedAt: stamp } };
  };
  const storeKey = 'tomestack.quickbar.fixture-2';
  const wavePin = { kind: 'roll', contentId: 'c-tide', effectId: 'wave' };
  const crestPin = { kind: 'roll', contentId: 'c-tide', effectId: 'wave2' };
  const spendAction = { action: 'spend', amount: 2, contentId: 'c-tide', resourceId: 'focus' };
  const waveRecord = (label = 'Tide Wave'): RollRecord => ({ ...rollRecord(label, 7), spend: { contentId: 'c-tide', resourceId: 'focus', amount: 2 } });
  const bar = () => within(screen.getByRole('region', { name: 'Quickbar' }));
  const barButton = (name: RegExp) => bar().getByRole<HTMLButtonElement>('button', { name });
  const spendButton = () => screen.getByRole<HTMLButtonElement>('button', { name: 'Spend 2 Focus (4 left)' });
  const pin = (...pins: object[]) => localStorage.setItem(storeKey, JSON.stringify(pins));
  const key = (code: string) => new KeyboardEvent('keydown', { code, altKey: true, bubbles: true, cancelable: true });
  const settle = async () => {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20));
    });
  };

  beforeEach(() => {
    vi.mocked(client.roll).mockReset();
    vi.mocked(client.play).mockReset();
  });

  it('a press rolls, then spends once through play, and the Last roll Spend is used', async () => {
    const user = userEvent.setup();
    pin(wavePin);
    vi.mocked(client.roll).mockResolvedValue(waveRecord());
    vi.mocked(client.play).mockResolvedValue(tideView(2));
    renderSheet(tideView());
    await user.click(barButton(/^Roll Tide Wave and spend 2 Focus$/));
    await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
    expect(client.roll).toHaveBeenCalledTimes(1);
    expect(vi.mocked(client.roll).mock.calls[0]).toEqual(['fixture-2', { content: tideRef, effectId: 'wave' }]); // a roll effect takes no mode
    expect(vi.mocked(client.play).mock.calls[0]?.[1]).toEqual(spendAction);
    await waitFor(() => expect(spendButton().disabled).toBe(true));
    await user.click(spendButton());
    await settle();
    expect(client.play).toHaveBeenCalledTimes(1);
  });

  const lashView = (current = 4): CharacterView => {
    const v = tideView(current);
    const lash = { item: tideRef, name: 'Tide Lash', effectId: 'lash', source: 'feature', automation: 'automatic', available: true, resourceId: 'focus', costResourceContent: 'c-tide', cost: 2, properties: [], trace: [], warnings: [], attack: 'melee', toHit: 5, damage: '1d6' };
    return { ...v, sheet: { ...v.sheet, attacks: [lash] } } as unknown as CharacterView;
  };
  const lashPin = { kind: 'attack', contentId: 'c-tide', effectId: 'lash' };
  const lashRecord = (): RollRecord => ({
    ...rollRecord('Tide Lash', 14),
    provenance: { rollId: 'fixture', label: 'Tide Lash', content: tideRef, effectId: 'lash' },
    spend: { contentId: 'c-tide', resourceId: 'focus', amount: 2 },
  } as RollRecord);

  it('a pinned feature attack with a cost says "and spend" and spends once', async () => {
    const user = userEvent.setup();
    pin(lashPin);
    vi.mocked(client.roll).mockResolvedValue(lashRecord());
    vi.mocked(client.play).mockResolvedValue(tideView(2));
    renderSheet(lashView());
    await user.click(barButton(/^Roll Tide Lash attack and spend 2 Focus$/));
    await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
    await settle();
    expect(vi.mocked(client.play).mock.calls[0]?.[1]).toEqual(spendAction);
    expect(client.play).toHaveBeenCalledTimes(1);
  });

  it('a pinned feature attack that costs nothing is named plainly and sends no spend', async () => {
    const user = userEvent.setup();
    pin(lashPin);
    vi.mocked(client.roll).mockResolvedValue({ ...lashRecord(), spend: { contentId: 'c-tide', resourceId: 'focus', amount: 0 } });
    const free = lashView();
    renderSheet({ ...free, sheet: { ...free.sheet, attacks: [{ ...free.sheet.attacks![0]!, cost: 0 }] } } as CharacterView);
    await user.click(barButton(/^Roll Tide Lash attack$/));
    await waitFor(() => expect(client.roll).toHaveBeenCalledTimes(1));
    await settle();
    expect(client.play).not.toHaveBeenCalled();
  });

  it('a pinned roll whose name does not say "and spend" leaves the Spend on the Last roll', async () => {
    const user = userEvent.setup();
    pin(wavePin);
    const v = tideView();
    const plain = { ...v, sheet: { ...v.sheet, features: [{ ...v.sheet.features![0]!, effects: [{ ...waveEffect('wave', 'Tide Wave'), variableCost: true }] }] } } as unknown as CharacterView;
    vi.mocked(client.roll).mockResolvedValue(waveRecord());
    renderSheet(plain);
    await user.click(barButton(/^Roll Tide Wave$/));
    await waitFor(() => expect(client.roll).toHaveBeenCalledTimes(1));
    await settle();
    expect(client.play).not.toHaveBeenCalled();
  });

  it('a roll that resolves after the sheet is gone makes no spend', async () => {
    pin(wavePin);
    let resolveRoll!: (r: RollRecord) => void;
    vi.mocked(client.roll).mockReturnValue(new Promise<RollRecord>((r) => (resolveRoll = r)));
    const { unmount } = renderSheet(tideView());
    fireEvent.click(barButton(/^Roll Tide Wave/));
    unmount();
    await act(async () => resolveRoll(waveRecord()));
    await settle();
    expect(client.play).not.toHaveBeenCalled();
  });

  it('a refused variable-cost Spend on the Last roll puts the typed amount back and gives the button back', async () => {
    const user = userEvent.setup();
    pin(wavePin);
    const v = tideView();
    const variable = { ...v, sheet: { ...v.sheet, features: [{ ...v.sheet.features![0]!, effects: [{ ...waveEffect('wave', 'Tide Wave'), variableCost: true }] }] } } as unknown as CharacterView;
    vi.mocked(client.roll).mockResolvedValue({ ...waveRecord(), provenance: { rollId: 'fixture', label: 'Tide Wave', content: tideRef, effectId: 'wave' } });
    vi.mocked(client.play).mockRejectedValue(new TomeStackError({ code: 'character.stale', message: 'The character changed.' }));
    vi.mocked(client.getCharacter).mockResolvedValue(variable);
    render(<CharacterSheet view={variable} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
    await user.click(barButton(/^Roll Tide Wave$/));
    const box = await screen.findByLabelText<HTMLInputElement>(/^Focus to spend/);
    await user.type(box, '2');
    const spend = screen.getByRole<HTMLButtonElement>('button', { name: /^Spend 2 Focus/ });
    await user.click(spend);
    await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByLabelText<HTMLInputElement>(/^Focus to spend/).value).toBe('2'));
    expect(screen.getByRole<HTMLButtonElement>('button', { name: /^Spend 2 Focus/ }).disabled).toBe(false);
  });

  it('two presses before the roll resolves, and two Alt+1 in one tick, each give one roll and one spend', async () => {
    pin(wavePin);
    let resolveRoll!: (r: RollRecord) => void;
    vi.mocked(client.roll).mockReturnValue(new Promise<RollRecord>((r) => (resolveRoll = r)));
    vi.mocked(client.play).mockResolvedValue(tideView(2));
    renderSheet(tideView());
    const button = barButton(/^Roll Tide Wave/);
    fireEvent.click(button);
    fireEvent.click(button);
    await act(async () => resolveRoll(waveRecord()));
    await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
    await settle();
    expect(client.roll).toHaveBeenCalledTimes(1);
    expect(client.play).toHaveBeenCalledTimes(1);

    // Two native keydowns before React renders in between: only the sheet's ref guard can stop the second.
    vi.mocked(client.roll).mockReset();
    vi.mocked(client.play).mockReset();
    vi.mocked(client.roll).mockResolvedValue(waveRecord());
    vi.mocked(client.play).mockResolvedValue(tideView(2));
    await act(async () => {
      document.dispatchEvent(key('Digit1'));
      document.dispatchEvent(key('Digit1'));
    });
    await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
    await settle();
    expect(client.roll).toHaveBeenCalledTimes(1);
    expect(client.play).toHaveBeenCalledTimes(1);
  });

  it('Alt+2 while the first spend is still pending runs nothing', async () => {
    pin(wavePin, crestPin);
    vi.mocked(client.roll).mockResolvedValue(waveRecord());
    let settlePlay!: (v: CharacterView) => void;
    vi.mocked(client.play).mockReturnValue(new Promise<CharacterView>((r) => (settlePlay = r)));
    renderSheet(tideView());
    await act(async () => {
      document.dispatchEvent(key('Digit1'));
    });
    await waitFor(() => expect(client.play).toHaveBeenCalledTimes(1));
    await act(async () => {
      document.dispatchEvent(key('Digit2'));
    });
    await settle();
    expect(client.roll).toHaveBeenCalledTimes(1);
    const bar = screen.getByRole('region', { name: 'Quickbar' });
    expect(bar.getAttribute('aria-busy')).toBe('true');
    expect(barButton(/^Roll Tide Wave/).getAttribute('aria-disabled')).toBeNull(); // busy does not restyle the buttons
    await act(async () => settlePlay(tideView(2)));
    await waitFor(() => expect(bar.getAttribute('aria-busy')).toBeNull());
  });

  it('a refused spend shows the error, gives the Last roll Spend back and plays once', async () => {
    const user = userEvent.setup();
    pin(wavePin);
    const refusal = new TomeStackError({ code: 'character.stale', message: 'The character changed.' });
    vi.mocked(client.roll).mockResolvedValue(waveRecord());
    vi.mocked(client.play).mockRejectedValue(refusal);
    vi.mocked(client.getCharacter).mockResolvedValue(tideView());
    const onError = vi.fn();
    render(<CharacterSheet view={tideView()} onChanged={noop} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
    await user.click(barButton(/^Roll Tide Wave/));
    await waitFor(() => expect(onError).toHaveBeenCalledWith(refusal));
    await waitFor(() => expect(spendButton().disabled).toBe(false));
    expect(client.play).toHaveBeenCalledTimes(1);
  });

  it('Undo last change after a quickbar spend sends the inverse (regain)', async () => {
    const user = userEvent.setup();
    pin(wavePin);
    const before = tideView();
    const after = tideView(2, '2026-10-03T00:00:05Z');
    vi.mocked(client.roll).mockResolvedValue(waveRecord());
    vi.mocked(client.play).mockResolvedValueOnce(after).mockResolvedValue(before);
    const onChanged = vi.fn();
    const element = (shown: CharacterView) => <CharacterSheet view={shown} onChanged={onChanged} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />;
    const { rerender } = render(element(before));
    await user.click(barButton(/^Roll Tide Wave/));
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith(after));
    rerender(element(after)); // the app shows the returned view
    await user.click(await screen.findByRole('button', { name: /^Undo last change:/ }));
    await waitFor(() => expect(client.play).toHaveBeenCalledTimes(2));
    expect(vi.mocked(client.play).mock.calls[1]?.[1]).toEqual({ ...spendAction, action: 'regain' });
  });

  it('a pin whose content is not on the sheet shows no bar and no error; pinning another keeps it after the visible ones', async () => {
    const user = userEvent.setup();
    const ghost = { kind: 'custom', attackId: 'gone' };
    pin(ghost);
    const onError = vi.fn();
    render(<CharacterSheet view={tideView()} onChanged={noop} onError={onError} onStatus={noop} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
    expect(screen.queryByRole('region', { name: 'Quickbar' })).toBeNull();
    expect(onError).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: /^Pin Tide Wave/ }));
    expect(JSON.parse(localStorage.getItem(storeKey)!)).toEqual([wavePin, ghost]);
    expect(barButton(/^Roll Tide Wave/)).toBeTruthy();
  });

  it('pins stored for another character do not show', () => {
    localStorage.setItem('tomestack.quickbar.fixture-1', JSON.stringify([wavePin]));
    renderSheet(tideView());
    expect(screen.queryByRole('region', { name: 'Quickbar' })).toBeNull();
  });
});

describe('D74 level-up checklist', () => {
  const storeKey = 'tomestack.levelUpChecklist.fixture-2';
  const levelView = (level: number, sheetOver: Partial<SheetModel> = {}) => {
    const v = view(sheetOver);
    return { ...v, character: { ...v.character, level } };
  };
  const seed = (level = 1) => writeChecklist('fixture-2', { level, changes: [{ kind: 'hitPoints', before: 8, after: 12 }] });
  const region = () => screen.queryByRole('region', { name: /^What changed at level/ });
  const clientCalls = () => Object.values(client).reduce((n, fn) => n + vi.mocked(fn).mock.calls.length, 0);

  it('focuses the panel heading, not the sheet heading, when asked on open', () => {
    seed();
    renderSheet(levelView(1), 'features', true);
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'What changed at level 1' }));
    expect(document.activeElement).not.toBe(document.getElementById('sheet-heading'));
  });

  it('keeps the sheet heading focus when the panel is not asked to take it, and when focusChecklist has no list to show', () => {
    seed();
    renderSheet(levelView(1), 'features');
    expect(document.activeElement).toBe(document.getElementById('sheet-heading'));
    cleanup();
    localStorage.clear();
    renderSheet(levelView(1), 'features', true);
    expect(document.activeElement).toBe(document.getElementById('sheet-heading'));
  });

  it('opens on Features when asked to focus the panel, whatever the deep link or memory says, and writes no memory', () => {
    seed();
    localStorage.setItem('tomestack.sheetTab.fixture-2', 'stats');
    renderSheet(levelView(1), 'play', true);
    expect(screen.getByRole('tab', { name: 'Features' }).getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'What changed at level 1' }));
    expect(localStorage.getItem('tomestack.sheetTab.fixture-2')).toBe('stats');
  });

  it('focusChecklist with no stored list leaves the tab and the sheet heading focus as before', () => {
    renderSheet(levelView(1), 'play', true);
    expect(screen.getByRole('tab', { name: 'Play' }).getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(document.getElementById('sheet-heading'));
  });

  it('a Spells row has no button when the Spells tab is not offered', () => {
    writeChecklist('fixture-2', { level: 1, changes: [{ kind: 'slots', level: 1, before: 0, after: 2 }] });
    renderSheet(levelView(1), 'features');
    expect(screen.getByText('Level 1 spell slots: 0 → 2')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Show on Spells/ })).toBeNull();
  });

  it('shows no region without a stored list, or when the stored list is for another level', () => {
    renderSheet(levelView(1), 'features');
    expect(region()).toBeNull();
    cleanup();
    seed(2);
    renderSheet(levelView(1), 'features');
    expect(region()).toBeNull();
  });

  it('Dismiss removes the key and the region, focuses the Features tab, calls no client method, and stays gone on remount', async () => {
    const user = userEvent.setup();
    seed();
    renderSheet(levelView(1), 'features');
    await act(async () => {}); // let the mount effects settle before counting calls
    const before = clientCalls();
    await user.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(localStorage.getItem(storeKey)).toBeNull();
    expect(region()).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Features' }));
    expect(clientCalls()).toBe(before);
    cleanup();
    renderSheet(levelView(1), 'features');
    expect(region()).toBeNull();
  });

  it('Show on Play selects and focuses the Play tab and remembers it', async () => {
    const user = userEvent.setup();
    seed();
    renderSheet(levelView(1), 'features');
    await user.click(screen.getByRole('button', { name: 'Show on Play' }));
    const play = screen.getByRole('tab', { name: 'Play' });
    expect(play.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(play);
    expect(localStorage.getItem('tomestack.sheetTab.fixture-2')).toBe('play');
  });

  it('Show choices focuses the choices heading and changes no tab', async () => {
    const user = userEvent.setup();
    const choices = [{ source: { contentId: 'fixture.feature', revisionId: 'r1' }, sourceName: 'Fixture feature', choiceId: 'pick', count: 1, options: [], selected: [], resolved: false }] as unknown as SheetModel['choices'];
    writeChecklist('fixture-2', { level: 1, changes: [{ kind: 'choice', contentId: 'fixture.feature', choiceId: 'pick' }] });
    renderSheet(levelView(1, { choices }), 'features');
    await user.click(screen.getByRole('button', { name: 'Show choices' }));
    expect(document.activeElement).toBe(document.getElementById('choices-heading'));
    expect(screen.getByRole('tab', { name: 'Features' }).getAttribute('aria-selected')).toBe('true');
    expect(localStorage.getItem('tomestack.sheetTab.fixture-2')).toBeNull();
  });

  it('keeps the region when the view is replaced (a rest or spend)', () => {
    seed();
    const { rerender } = renderSheet(levelView(1), 'features');
    rerender(sheetElement({ ...levelView(1), sheet: { ...view().sheet, hitPoints: { maximum: 8, current: 3, temporary: 0 } } }, 'features'));
    expect(region()).not.toBeNull();
  });

  it('shows the list when a save lands on the open sheet and the level changes, without moving focus', () => {
    seed(2);
    const { rerender } = renderSheet(levelView(1), 'features');
    expect(region()).toBeNull();
    const focused = document.activeElement;
    rerender(sheetElement(levelView(2), 'features', true));
    expect(region()).not.toBeNull();
    expect(document.activeElement).toBe(focused);
  });
});

// MECH-X item 2: after a long rest the sheet names the choices whose picks may be replaced (page state only).
describe('after your long rest', () => {
  const picked = (when: 'levelUp' | 'longRest', selected: number) => ({
    source: { contentId: 'fixture-src', revisionId: 'fixture-src-r1' },
    sourceName: 'Fixture Repertoire',
    choiceId: 'technique',
    count: 2,
    options: [],
    selected: Array.from({ length: selected }, (_, i) => ({ contentId: `p${i}`, revisionId: `p${i}-r1` })),
    resolved: selected === 2,
    replaceable: { when, max: 1 },
  });
  async function finishRest(user: ReturnType<typeof userEvent.setup>, kind: 'Short' | 'Long', rested: CharacterView, onMakeChoices = noop) {
    vi.mocked(client.restPreview).mockResolvedValue({ kind: kind === 'Long' ? 'longRest' : 'shortRest', changes: [], manual: [], basis: 'fixture' });
    vi.mocked(client.rest).mockResolvedValue(rested);
    render(<CharacterSheet view={view({ choices: [picked('longRest', 1)] })} onChanged={noop} onError={noop} onStatus={noop} onLevelUp={noop} onMakeChoices={onMakeChoices} onArchiveChanged={noop} />);
    await user.click(screen.getByRole('button', { name: `${kind} rest…` }));
    const finish = await screen.findByRole('button', { name: `Finish ${kind.toLowerCase()} rest` });
    await waitFor(() => expect(finish.getAttribute('aria-disabled')).not.toBe('true'));
    await user.click(finish);
  }

  it('shows a labelled region with Make choices and Dismiss; Dismiss removes it and focuses the sheet heading', async () => {
    const user = userEvent.setup();
    const onMakeChoices = vi.fn();
    await finishRest(user, 'Long', view({ choices: [picked('longRest', 1)] }), onMakeChoices);
    const region = await screen.findByRole('region', { name: 'After your long rest' });
    expect(within(region).getByText('You may replace one pick of: Fixture Repertoire.')).toBeTruthy();
    await user.click(within(region).getByRole('button', { name: 'Make choices' }));
    expect(onMakeChoices).toHaveBeenCalledTimes(1);
    await user.click(within(region).getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByRole('region', { name: 'After your long rest' })).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Fixture Quill' }));
  });

  it('shows nothing after a short rest, or when no longRest choice has picks', async () => {
    const user = userEvent.setup();
    await finishRest(user, 'Short', view({ choices: [picked('longRest', 1)] }));
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Short rest' })).toBeNull());
    expect(screen.queryByRole('region', { name: 'After your long rest' })).toBeNull();
    cleanup();
    await finishRest(user, 'Long', view({ choices: [picked('longRest', 0), picked('levelUp', 1)] }));
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Long rest' })).toBeNull());
    expect(screen.queryByRole('region', { name: 'After your long rest' })).toBeNull();
  });
});

it('does not echo the save notice through the status line when a play reply carries it (MECH-X item 2)', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  const v = view();
  const reply: CharacterView = { ...v, notices: [{ code: 'choice.replaced-outside-window', message: "Choice 'technique' had 2 pick(s) replaced outside its window." }, { code: 'resource.full', message: 'Fixture pool is full.' }] };
  vi.mocked(client.play).mockResolvedValue(reply);
  render(<CharacterSheet view={v} onChanged={noop} onError={noop} onStatus={onStatus} onLevelUp={noop} onMakeChoices={noop} onArchiveChanged={noop} />);
  await user.click(screen.getByRole('button', { name: 'Lose 1 hit point' }));
  await waitFor(() => expect(onStatus).toHaveBeenCalled());
  expect(onStatus).toHaveBeenCalledWith('Fixture pool is full.'); // other notices still say
});
