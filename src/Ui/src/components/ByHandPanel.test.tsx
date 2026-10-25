// @vitest-environment jsdom
// P18 (D69): the by-hand panel lists what TomeStack will not do for the player. Names and values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AutomationStatus, CharacterSheet, FeatureEffect, FeatureEntry, MechanicEntry, MechanicsReport } from '../api/types';
import { ByHandPanel } from './ByHandPanel';

vi.mock('../api/client', () => ({ client: { characterMechanics: vi.fn() } }));

beforeEach(() => vi.resetAllMocks());
afterEach(cleanup);

const SOURCE = 'source-1';

function feature(name: string, effects: Partial<FeatureEffect>[] = [], page = 12, source: string | null = SOURCE): FeatureEntry {
  const sourceId = source ?? undefined;
  return {
    content: { contentId: `c-${name}`, revisionId: `r-${name}` },
    name,
    kind: 'feature',
    origin: { kind: 'content', rulesFamily: 'srd-5.1', sourceId, page: { start: page } },
    automation: 'reference',
    effects: effects.map((e, i) => ({ id: `e${i}`, type: 'text', automation: 'reference' as AutomationStatus, ...e })),
    diagnostics: [],
  };
}

function sheetOf(characterId: string, features: FeatureEntry[]): CharacterSheet {
  return { characterId, rulesFamily: 'srd-5.1', diagnostics: [], fields: [], features };
}

function mech(name: string, effectType: string, automation: AutomationStatus, page = 20): MechanicEntry {
  return {
    content: { contentId: `c-${name}`, revisionId: `r-${name}` },
    featureName: name,
    kind: 'feature',
    effectId: `m-${effectType}`,
    effectType,
    automation,
    manualStep: 'Do it yourself.',
    sourceId: SOURCE,
    page: { start: page },
  };
}

const report = (mechanics: MechanicEntry[], unsupported: MechanicEntry[] = []): MechanicsReport => ({ mechanics, unsupported });

const spender = feature('Fixture Surge', [{ type: 'roll', resourceId: 'pool', label: 'Surge die' }], 7);
const plain = feature('Fixture Lore');
const sheet = sheetOf('char-1', [spender, plain]);

it('shows the four groups from the report and the sheet, each row with feature and page', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(
    report(
      [mech('Fixture Echo', 'weirdo', 'reference', 31), mech('Fixture Glow', 'modifier', 'reference', 32), mech('Fixture Half', 'spell', 'assisted', 33)],
      [mech('Fixture Echo', 'weirdo', 'reference', 31)],
    ),
  );
  render(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  await screen.findByRole('heading', { name: 'Reference only' });
  const spend = screen.getByRole('region', { name: 'Spend by hand' });
  expect(within(spend).getByText(/Fixture Surge/).closest('li')?.textContent).toContain('p. 7');
  expect(within(screen.getByRole('region', { name: 'Reference only' })).getByText(/Fixture Glow/).closest('li')?.textContent).toContain('p. 32');
  expect(within(screen.getByRole('region', { name: 'Partly automatic' })).getByText(/Fixture Half/).closest('li')?.textContent).toContain('p. 33');
  expect(within(screen.getByRole('region', { name: 'Not understood' })).getByText(/Fixture Echo/).closest('li')?.textContent).toContain('p. 31');
});

it('hides empty groups', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(report([mech('Fixture Half', 'spell', 'assisted')]));
  render(<ByHandPanel characterId="char-1" sheet={sheetOf('char-1', [plain])} active />);
  await screen.findByRole('heading', { name: 'Partly automatic' });
  expect(screen.queryByRole('heading', { name: 'Spend by hand' })).toBeNull();
  expect(screen.queryByRole('heading', { name: 'Reference only' })).toBeNull();
  expect(screen.queryByRole('heading', { name: 'Not understood' })).toBeNull();
});

it('says everything is calculated when nothing is listed', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(report([mech('Fixture Calc', 'modifier', 'automatic')]));
  render(<ByHandPanel characterId="char-1" sheet={sheetOf('char-1', [plain])} active />);
  expect(await screen.findByText('Everything on this sheet is calculated.')).toBeTruthy();
  expect(screen.getByRole('heading', { name: "TomeStack won't do these for you" })).toBeTruthy();
});

it('has the intro, lists as ul, a consistent heading level and no live region', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(report([mech('Fixture Half', 'spell', 'assisted')]));
  const { container } = render(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  await screen.findByRole('heading', { name: 'Partly automatic' });
  expect(screen.getByText('TomeStack never acts on its own; these need you.')).toBeTruthy();
  expect(screen.getByRole('heading', { name: "TomeStack won't do these for you" }).tagName).toBe('H3');
  expect(screen.getByRole('heading', { name: 'Partly automatic' }).tagName).toBe('H4');
  expect(container.querySelectorAll('ul').length).toBe(2);
  expect(container.querySelector('[aria-live], [role="status"], [role="alert"], output')).toBeNull();
  expect(container.textContent?.toLowerCase()).not.toContain('trigger');
});

it('Shows_a_plain_line_when_the_report_fails', async () => {
  vi.mocked(client.characterMechanics).mockRejectedValue(new Error('boom'));
  render(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  expect(await screen.findByText("Couldn't list what needs you by hand.")).toBeTruthy();
  expect(screen.getByRole('heading', { name: "TomeStack won't do these for you" })).toBeTruthy();
  expect(screen.queryByRole('heading', { name: 'Spend by hand' })).toBeNull();
  expect(screen.queryByText('Everything on this sheet is calculated.')).toBeNull();
});

it('No_sources_makes_no_call', () => {
  render(<ByHandPanel characterId="char-1" sheet={sheetOf('char-1', [])} active />);
  expect(client.characterMechanics).not.toHaveBeenCalled();
  expect(screen.getByText('Everything on this sheet is calculated.')).toBeTruthy();
});

it('sends every distinct source of the sheet once', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(report([]));
  const s = sheetOf('char-1', [feature('A', [], 1, 'src-b'), feature('B', [], 1, 'src-a'), feature('C', [], 1, 'src-b'), feature('D', [], 1, null)]);
  render(<ByHandPanel characterId="char-1" sheet={s} active />);
  await waitFor(() => expect(client.characterMechanics).toHaveBeenCalledTimes(1));
  expect(client.characterMechanics).toHaveBeenCalledWith('char-1', ['src-a', 'src-b']);
});

it('Unsupported_rows_appear_once', async () => {
  const odd = mech('Fixture Odd', 'weirdo', 'reference');
  vi.mocked(client.characterMechanics).mockResolvedValue(report([odd, mech('Fixture Glow', 'modifier', 'reference')], [{ ...odd }]));
  render(<ByHandPanel characterId="char-1" sheet={sheetOf('char-1', [plain])} active />);
  await screen.findByRole('heading', { name: 'Not understood' });
  expect(screen.getAllByText(/Fixture Odd/)).toHaveLength(1);
  expect(within(screen.getByRole('region', { name: 'Not understood' })).getByText(/Fixture Odd/)).toBeTruthy();
});

it('Spend_by_hand_rows_appear_once', async () => {
  // The same roll (feature "Fixture Surge", effect e0) also comes back from the report as assisted.
  const asRoll: MechanicEntry = { ...mech('Fixture Surge', 'roll', 'assisted', 7), effectId: 'e0' };
  vi.mocked(client.characterMechanics).mockResolvedValue(report([asRoll, mech('Fixture Half', 'spell', 'assisted')]));
  render(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  await screen.findByRole('heading', { name: 'Partly automatic' });
  expect(screen.getAllByText(/Fixture Surge/)).toHaveLength(1);
  expect(within(screen.getByRole('region', { name: 'Spend by hand' })).getByText(/Fixture Surge/)).toBeTruthy();
});

it('Pure_text_features_are_not_listed', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(
    report([mech('Fixture Prose', 'text', 'reference'), mech('Fixture Glow', 'modifier', 'reference')]),
  );
  render(<ByHandPanel characterId="char-1" sheet={sheetOf('char-1', [plain])} active />);
  await screen.findByText(/Fixture Glow/);
  expect(screen.queryByText(/Fixture Prose/)).toBeNull();
});

it('lists nothing but the sentence when the report holds only pure text', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(report([mech('Fixture Prose', 'text', 'reference')]));
  render(<ByHandPanel characterId="char-1" sheet={sheetOf('char-1', [plain])} active />);
  expect(await screen.findByText('Everything on this sheet is calculated.')).toBeTruthy();
});

it('No_call_while_another_tab_is_active', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(report([mech('Fixture Half', 'spell', 'assisted')]));
  const { rerender } = render(<ByHandPanel characterId="char-1" sheet={sheet} active={false} />);
  expect(client.characterMechanics).not.toHaveBeenCalled();
  rerender(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  await screen.findByRole('heading', { name: 'Partly automatic' });
  expect(client.characterMechanics).toHaveBeenCalledTimes(1);
});

it('fetches again when the sheet object changes', async () => {
  vi.mocked(client.characterMechanics).mockResolvedValue(report([]));
  const { rerender } = render(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  await waitFor(() => expect(client.characterMechanics).toHaveBeenCalledTimes(1));
  rerender(<ByHandPanel characterId="char-1" sheet={{ ...sheet }} active />);
  await waitFor(() => expect(client.characterMechanics).toHaveBeenCalledTimes(2));
});

it('ignores_a_late_reply_for_an_earlier_character', async () => {
  let finishFirst: (r: MechanicsReport) => void = () => {};
  vi.mocked(client.characterMechanics)
    .mockReturnValueOnce(new Promise((resolve) => { finishFirst = resolve; }))
    .mockResolvedValueOnce(report([mech('Fixture Second', 'spell', 'assisted')]));
  const { rerender } = render(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  rerender(<ByHandPanel characterId="char-2" sheet={sheetOf('char-2', [plain])} active />);
  await screen.findByText(/Fixture Second/);
  finishFirst(report([mech('Fixture First', 'spell', 'assisted')]));
  await new Promise((r) => setTimeout(r, 10));
  expect(screen.queryByText(/Fixture First/)).toBeNull();
  expect(screen.getByText(/Fixture Second/)).toBeTruthy();
});

it('ignores_a_late_reply_for_an_earlier_sheet', async () => {
  let finishFirst: (r: MechanicsReport) => void = () => {};
  vi.mocked(client.characterMechanics)
    .mockReturnValueOnce(new Promise((resolve) => { finishFirst = resolve; }))
    .mockResolvedValueOnce(report([mech('Fixture Newer', 'spell', 'assisted')]));
  const { rerender } = render(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  rerender(<ByHandPanel characterId="char-1" sheet={{ ...sheet }} active />);
  await screen.findByText(/Fixture Newer/);
  finishFirst(report([mech('Fixture Older', 'spell', 'assisted')]));
  await new Promise((r) => setTimeout(r, 10));
  expect(screen.queryByText(/Fixture Older/)).toBeNull();
});

it('hides a result computed for an earlier sheet while the new one loads', async () => {
  vi.mocked(client.characterMechanics)
    .mockResolvedValueOnce(report([mech('Fixture Old', 'spell', 'assisted')]))
    .mockReturnValueOnce(new Promise(() => {}));
  const { rerender } = render(<ByHandPanel characterId="char-1" sheet={sheet} active />);
  await screen.findByText(/Fixture Old/);
  rerender(<ByHandPanel characterId="char-2" sheet={sheetOf('char-2', [plain])} active />);
  expect(screen.queryByText(/Fixture Old/)).toBeNull();
});
