// @vitest-environment jsdom
// Import Lab (D62 L1): the per-source Coverage view. Names and values are invented.
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CoverageCounts, CoverageReport, EffectCoverage, FeatureCoverage } from '../api/types';
import { CoveragePanel } from './CoveragePanel';

vi.mock('../api/client', () => ({ client: { contentCoverage: vi.fn() } }));

beforeEach(() => vi.resetAllMocks());
afterEach(cleanup);

const counts = (automatic = 0, assisted = 0, reference = 0, unsupported = 0): CoverageCounts => ({ automatic, assisted, reference, unsupported });

function effect(type: string, over: Partial<EffectCoverage> = {}): EffectCoverage {
  return { effectId: `e-${type}`, type, author: 'automatic', ceiling: 'automatic', effective: 'automatic', unsupported: false, ...over };
}

function feature(name: string, totals: CoverageCounts, over: Partial<FeatureCoverage> = {}): FeatureCoverage {
  return {
    contentId: `c-${name}`, revisionId: `r-${name}`, name, kind: 'feature', status: 'published', totals, missingMechanic: false,
    effects: [effect('modifier')], ...over,
  };
}

const wizard = feature('Fixture Surge', counts(2, 1), { effects: [effect('modifier'), effect('roll'), effect('rollMode', { effective: 'assisted' })] });
const odd = feature('Fixture Odd', counts(0, 0, 0, 1), {
  effects: [effect('fixture-unknown', { author: 'reference', ceiling: null, effective: null, unsupported: true })],
});
const prose = feature('Fixture Prose', counts(0, 0, 1), { missingMechanic: true, effects: [effect('text', { effectId: null, author: 'reference', effective: 'reference' })] });

function reportOf(over: Partial<CoverageReport['sources'][number]> = {}, includeDrafts = false): CoverageReport {
  const source = {
    sourceId: 's1', name: 'Fixture Book', totals: counts(7, 2, 0, 1),
    classes: [
      {
        contentId: 'cls', name: 'Fixture Warden', totals: counts(2, 1),
        levels: [{ level: 1, features: [wizard] }, { level: 3, features: [odd] }],
      },
    ],
    other: [prose],
    ...over,
  };
  return { scope: { sourceId: 's1', includeDrafts }, totals: source.totals, sources: [source] };
}

it('shows the totals as one line of text', async () => {
  vi.mocked(client.contentCoverage).mockResolvedValue(reportOf());
  render(<CoveragePanel sourceId="s1" />);
  expect(await screen.findByText('7 automatic · 2 by hand (assisted) · 0 reference · 1 not understood')).toBeTruthy();
  expect(client.contentCoverage).toHaveBeenCalledWith({ sourceId: 's1', includeDrafts: false });
});

it('lists a heading per class, a list per level, then the features', async () => {
  vi.mocked(client.contentCoverage).mockResolvedValue(reportOf());
  const { container } = render(<CoveragePanel sourceId="s1" />);
  await screen.findByRole('heading', { name: 'Fixture Warden' });
  expect(screen.getByRole('heading', { name: 'Fixture Warden' }).tagName).toBe('H4');
  expect(screen.getByRole('heading', { name: 'Coverage' }).tagName).toBe('H3');
  const levels = screen.getAllByRole('heading', { level: 5 }).map((h) => h.textContent);
  expect(levels).toEqual(['Level 1', 'Level 3']);
  const first = within(screen.getByRole('region', { name: 'Level 1' }));
  expect(first.getByRole('list')).toBeTruthy();
  expect(first.getByText(/Fixture Surge/).closest('li')?.textContent).toContain('2 automatic · 1 by hand (assisted)');
  expect(container.querySelector('[aria-live], [role="status"], [role="alert"], output')).toBeNull();
});

it('names an unsupported effect and says TomeStack cannot calculate it', async () => {
  vi.mocked(client.contentCoverage).mockResolvedValue(reportOf());
  render(<CoveragePanel sourceId="s1" />);
  const row = (await screen.findByText(/Fixture Odd/)).closest('li')!;
  expect(row.textContent).toContain('fixture-unknown');
  expect(row.textContent).toContain("TomeStack can't calculate this");
});

it('warns of a missing mechanic as advice', async () => {
  vi.mocked(client.contentCoverage).mockResolvedValue(reportOf());
  render(<CoveragePanel sourceId="s1" />);
  const row = (await screen.findByText(/Fixture Prose/)).closest('li')!;
  expect(row.textContent).toContain('May need a mechanic: by hand today');
  expect(within(screen.getByRole('region', { name: 'Other content' })).getByText(/Fixture Prose/)).toBeTruthy();
});

it('Show drafts asks again with includeDrafts and labels the draft row without counting it', async () => {
  const draft = feature('Fixture Surge', counts(1), { status: 'draft', revisionId: 'r-draft' });
  vi.mocked(client.contentCoverage)
    .mockResolvedValueOnce(reportOf())
    .mockResolvedValueOnce(
      reportOf({ classes: [{ contentId: 'cls', name: 'Fixture Warden', totals: counts(2, 1), levels: [{ level: 1, features: [wizard, draft] }] }] }, true),
    );
  render(<CoveragePanel sourceId="s1" />);
  const box = await screen.findByRole('checkbox', { name: 'Show drafts' });
  expect((box as HTMLInputElement).checked).toBe(false);
  fireEvent.click(box);
  await waitFor(() => expect(client.contentCoverage).toHaveBeenLastCalledWith({ sourceId: 's1', includeDrafts: true }));
  const rows = await screen.findAllByText(/Fixture Surge/);
  expect(rows).toHaveLength(2);
  expect(rows.filter((r) => r.closest('li')?.textContent?.includes('Draft'))).toHaveLength(1);
  expect(screen.getByText(/Totals count published content only/)).toBeTruthy();
});

it('says so plainly when the report fails or the source has nothing', async () => {
  vi.mocked(client.contentCoverage).mockRejectedValueOnce(new Error('boom'));
  const { unmount } = render(<CoveragePanel sourceId="s1" />);
  expect(await screen.findByText("Couldn't load the coverage.")).toBeTruthy();
  unmount();
  vi.mocked(client.contentCoverage).mockResolvedValueOnce(reportOf({ totals: counts(), classes: [], other: [] }));
  render(<CoveragePanel sourceId="s1" />);
  expect(await screen.findByText('Nothing in this source yet.')).toBeTruthy();
});

it('ignores a late reply for an earlier source', async () => {
  let finishFirst: (r: CoverageReport) => void = () => {};
  vi.mocked(client.contentCoverage)
    .mockReturnValueOnce(new Promise((resolve) => { finishFirst = resolve; }))
    .mockResolvedValueOnce(reportOf({ sourceId: 's2', name: 'Fixture Second', totals: counts(1) , classes: [], other: [prose] }));
  const { rerender } = render(<CoveragePanel sourceId="s1" />);
  rerender(<CoveragePanel sourceId="s2" />);
  await screen.findByText('1 automatic · 0 by hand (assisted) · 0 reference · 0 not understood');
  finishFirst(reportOf());
  await new Promise((r) => setTimeout(r, 10));
  expect(screen.queryByText('7 automatic · 2 by hand (assisted) · 0 reference · 1 not understood')).toBeNull();
});
