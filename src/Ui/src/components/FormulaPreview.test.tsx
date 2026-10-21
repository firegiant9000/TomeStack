// @vitest-environment jsdom
// The formula preview (D61): debounce, skipped formulas, stale replies and quiet failure. The client is mocked.
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { FormulaPreviewResult } from '../api/types';
import { FormulaPreview } from './FormulaPreview';

vi.mock('../api/client', () => ({ client: { previewFormula: vi.fn() } }));

const CAPTION = 'Preview, not a character: ability scores 10, proficiency bonus by level.';

beforeEach(() => {
  vi.useFakeTimers();
  vi.mocked(client.previewFormula).mockReset();
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

function result(values: number[], formula = 'PB'): FormulaPreviewResult {
  const levels = [1, 5, 11, 16, 20];
  return { formula, error: null, unavailable: null, points: levels.map((level, i) => ({ level, value: values[i], problem: null })) };
}

async function settle(ms = 300) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

it('shows_the_value_at_each_sample_level', async () => {
  vi.mocked(client.previewFormula).mockResolvedValue(result([2, 3, 4, 5, 6]));
  const { container } = render(<FormulaPreview id="p" formula="PB" />);
  await settle();
  expect(container.textContent).toContain('Level 1: 2 · Level 5: 3 · Level 11: 4 · Level 16: 5 · Level 20: 6');
  expect(container.textContent).toContain(CAPTION);
  expect(container.querySelector('small')?.textContent).toBe(CAPTION);
});

it('waits_300ms_before_asking', async () => {
  vi.mocked(client.previewFormula).mockResolvedValue(result([2, 3, 4, 5, 6]));
  render(<FormulaPreview id="p" formula="PB" />);
  await settle(299);
  expect(client.previewFormula).not.toHaveBeenCalled();
  await settle(1);
  expect(client.previewFormula).toHaveBeenCalledTimes(1);
});

it('forwards_floorAtZero_to_the_client', async () => {
  vi.mocked(client.previewFormula).mockResolvedValue(result([0, 0, 1, 2, 3]));
  const { rerender } = render(<FormulaPreview id="p" formula="PB - 3" floorAtZero />);
  await settle();
  expect(vi.mocked(client.previewFormula).mock.calls.at(-1)).toEqual(['PB - 3', undefined, true]);
  rerender(<FormulaPreview id="p" formula="PB - 4" />);
  await settle();
  expect(vi.mocked(client.previewFormula).mock.calls.at(-1)?.[2]).toBeFalsy();
});

it.each(['', 'all', '3'])('does_not_ask_for_empty_all_or_a_plain_number (%j)', async (formula) => {
  const { container } = render(<FormulaPreview id="p" formula={formula} />);
  await settle(1000);
  expect(client.previewFormula).not.toHaveBeenCalled();
  expect(container.textContent).toBe('');
});

it('keeps_the_newest_answer_when_replies_arrive_out_of_order', async () => {
  const resolvers: Array<(r: FormulaPreviewResult) => void> = [];
  vi.mocked(client.previewFormula).mockImplementation(() => new Promise<FormulaPreviewResult>((r) => resolvers.push(r)));
  const { container, rerender } = render(<FormulaPreview id="p" formula="PB" />);
  await settle();
  rerender(<FormulaPreview id="p" formula="PB + 1" />);
  await settle();
  expect(resolvers).toHaveLength(2);
  const [first, second] = resolvers;
  await act(async () => second!(result([3, 4, 5, 6, 7], 'PB + 1')));
  await act(async () => first!(result([2, 3, 4, 5, 6])));
  expect(container.textContent).toContain('Level 1: 3 · Level 5: 4');
  expect(container.textContent).not.toContain('Level 1: 2');
});

it('shows_the_parse_error_and_the_unavailable_reason', async () => {
  vi.mocked(client.previewFormula).mockResolvedValueOnce({ formula: 'PB +', error: { code: 'x', message: 'Unexpected end.' }, unavailable: null, points: [] });
  const { container, rerender } = render(<FormulaPreview id="p" formula="PB +" />);
  await settle();
  expect(container.textContent).toContain('Unexpected end.');
  expect(container.textContent).not.toContain('Level 1');
  vi.mocked(client.previewFormula).mockResolvedValueOnce({ formula: 'SCALE.a', error: null, unavailable: 'No such column here.', points: [] });
  rerender(<FormulaPreview id="p" formula="SCALE.a" />);
  await settle();
  expect(container.textContent).toContain('No such column here.');
  expect(container.textContent).not.toContain('Level 1');
});

it('shows_a_problem_at_one_level', async () => {
  const r = result([2, 3, 4, 5, 6]);
  r.points[1] = { level: 5, value: null, problem: 'Division by zero.' };
  vi.mocked(client.previewFormula).mockResolvedValue(r);
  const { container } = render(<FormulaPreview id="p" formula="10 / (CLASS_LEVEL - 5)" />);
  await settle();
  expect(container.textContent).toContain('Level 5: Division by zero.');
  expect(container.textContent).toContain('Level 1: 2');
});

it('a_failed_request_shows_a_quiet_note_not_an_alert', async () => {
  vi.mocked(client.previewFormula).mockRejectedValue(new Error('down'));
  const { container } = render(<FormulaPreview id="p" formula="PB" />);
  await settle();
  expect(container.textContent).toContain('Preview unavailable.');
  expect(container.querySelector('[role="alert"]')).toBeNull();
});

it('the_container_has_the_given_id', async () => {
  vi.mocked(client.previewFormula).mockResolvedValue(result([2, 3, 4, 5, 6]));
  const { container } = render(<FormulaPreview id="rule-1-value-preview" formula="PB" />);
  await settle();
  expect(container.querySelector('#rule-1-value-preview')).not.toBeNull();
});

it('reads_a_reply_whose_null_fields_are_omitted', async () => {
  const omitted = { formula: 'PB', points: [{ level: 1, value: 2 }, { level: 5, value: 3 }] } as FormulaPreviewResult;
  vi.mocked(client.previewFormula).mockResolvedValue(omitted);
  const { container } = render(<FormulaPreview id="p" formula="PB" />);
  await settle();
  expect(container.textContent).toContain('Level 1: 2 · Level 5: 3');
  expect(container.textContent).not.toContain('undefined');
  expect(container.textContent).not.toContain('null');
});

it('clears_the_result_when_the_formula_becomes_empty_and_ignores_a_late_reply', async () => {
  let resolve: (r: FormulaPreviewResult) => void = () => {};
  vi.mocked(client.previewFormula).mockImplementation(() => new Promise<FormulaPreviewResult>((r) => (resolve = r)));
  const { container, rerender } = render(<FormulaPreview id="p" formula="PB" />);
  await settle();
  rerender(<FormulaPreview id="p" formula="" />);
  await act(async () => resolve(result([2, 3, 4, 5, 6])));
  expect(container.textContent).toBe('');
});

it('keeps_its_container_for_aria_describedby_and_reserves_room_only_for_a_previewable_formula', () => {
  const { container, rerender } = render(<FormulaPreview id="p" formula="PB" />);
  expect(container.querySelector('#p')?.classList.contains('reserved')).toBe(true);
  expect(container.textContent).toBe('');
  rerender(<FormulaPreview id="p" formula="3" />);
  expect(container.querySelector('#p')).not.toBeNull();
  expect(container.querySelector('#p')?.classList.contains('reserved')).toBe(false);
});

it('shows_a_dash_for_a_point_with_neither_a_value_nor_a_problem', async () => {
  vi.mocked(client.previewFormula).mockResolvedValue({ points: [{ level: 1 }, { level: 5, value: 3 }] } as FormulaPreviewResult);
  const { container } = render(<FormulaPreview id="p" formula="PB" />);
  await settle();
  expect(container.textContent).toContain('Level 1: — · Level 5: 3');
  expect(container.textContent).not.toContain('undefined');
});
