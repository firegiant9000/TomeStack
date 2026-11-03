// @vitest-environment jsdom
// Import Lab (D62 L2): the read-only class progression grid in the studio. Names and values are invented.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { ClassLevelRow, ClassTableView } from '../api/types';
import { ClassGrid } from './ClassGrid';

vi.mock('../api/client', () => ({ client: { contentClassTable: vi.fn() } }));

beforeEach(() => vi.resetAllMocks());
afterEach(cleanup);

function row(level: number, over: Partial<ClassLevelRow> = {}): ClassLevelRow {
  return { level, proficiencyBonus: 2 + Math.floor((level - 1) / 4), features: [], choices: [], scales: {}, ...over };
}

function view(over: Partial<ClassTableView> = {}, rows: Partial<Record<number, Partial<ClassLevelRow>>> = {}): ClassTableView {
  return { columns: [], rows: Array.from({ length: 20 }, (_, i) => row(i + 1, rows[i + 1])), ...over };
}

it('shows a captioned table with column headers and one row per level', async () => {
  vi.mocked(client.contentClassTable).mockResolvedValue(view());
  render(<ClassGrid revisionId="r1" onEditFeature={() => {}} />);
  const table = await screen.findByRole('table', { name: 'Class progression' });
  expect(table.querySelector('caption')?.textContent).toBe('Class progression');
  const headers = within(table).getAllByRole('columnheader');
  expect(headers.length).toBeGreaterThan(2);
  expect(headers.every((h) => h.getAttribute('scope') === 'col')).toBe(true);
  expect(within(table).getAllByRole('row')).toHaveLength(21); // header + 20 levels
  expect(client.contentClassTable).toHaveBeenCalledWith('r1');
});

it('shows the proficiency bonus and one column per scale', async () => {
  vi.mocked(client.contentClassTable).mockResolvedValue(
    view({ columns: ['Fixture Ink'] }, { 1: { scales: { 'Fixture Ink': '2' } }, 5: { scales: { 'Fixture Ink': '' } } }),
  );
  render(<ClassGrid revisionId="r1" onEditFeature={() => {}} />);
  const table = await screen.findByRole('table', { name: 'Class progression' });
  expect(within(table).getByRole('columnheader', { name: 'Fixture Ink' })).toBeTruthy();
  expect(within(table).getByRole('columnheader', { name: 'Proficiency bonus' })).toBeTruthy();
  const first = within(table).getAllByRole('row')[1]!;
  expect(within(first).getByRole('rowheader').textContent).toBe('1');
  expect(within(first).getAllByRole('cell').map((c) => c.textContent)).toContain('2');
  const fifth = within(table).getAllByRole('row')[5]!;
  expect(within(fifth).getAllByRole('cell').at(-1)!.textContent).toBe('—'); // an empty scale cell reads like its siblings
});

it('opens a feature through its Edit button', async () => {
  vi.mocked(client.contentClassTable).mockResolvedValue(
    view({}, { 3: { features: [{ contentId: 'c-pool', name: 'Fixture Pool' }, { contentId: 'c-aura', name: 'Fixture Aura' }] } }),
  );
  const onEdit = vi.fn();
  render(<ClassGrid revisionId="r1" onEditFeature={onEdit} />);
  const third = (await screen.findAllByRole('row'))[3]!;
  await userEvent.click(within(third).getByRole('button', { name: 'Edit Fixture Pool' }));
  expect(onEdit).toHaveBeenCalledWith('c-pool');
  expect(within(third).getByRole('button', { name: 'Edit Fixture Aura' })).toBeTruthy();
});

it('lists choice options and slots as text', async () => {
  vi.mocked(client.contentClassTable).mockResolvedValue(
    view({}, { 2: { choices: [{ contentId: 'c-opt', name: 'Fixture Option' }], slots: [2, 1], pactSlots: 2, pactLevel: 1 } }),
  );
  render(<ClassGrid revisionId="r1" onEditFeature={() => {}} />);
  const second = (await screen.findAllByRole('row'))[2]!;
  expect(within(second).getByText('Fixture Option')).toBeTruthy();
  expect(within(second).getByText('2 / 1')).toBeTruthy();
  expect(within(second).getByText('2 at spell level 1')).toBeTruthy();
});

it('says a draft is not yet published', async () => {
  vi.mocked(client.contentClassTable).mockResolvedValue(view());
  render(<ClassGrid revisionId="r1" draft onEditFeature={() => {}} />);
  expect(await screen.findByText('Draft: not yet published')).toBeTruthy();
});

it('does not say it for a published class', async () => {
  vi.mocked(client.contentClassTable).mockResolvedValue(view());
  render(<ClassGrid revisionId="r1" onEditFeature={() => {}} />);
  await screen.findByRole('table');
  expect(screen.queryByText('Draft: not yet published')).toBeNull();
});

it('scrolls inside a named, focusable region and has no live region', async () => {
  vi.mocked(client.contentClassTable).mockResolvedValue(view());
  const { container } = render(<ClassGrid revisionId="r1" onEditFeature={() => {}} />);
  const region = await screen.findByRole('region', { name: 'Class progression, scrollable' });
  expect(region.getAttribute('tabindex')).toBe('0');
  expect(within(region).getByRole('table')).toBeTruthy();
  expect(container.querySelector('[aria-live], [role="status"], [role="alert"], output')).toBeNull();
});

it('says so when the progression cannot be read', async () => {
  vi.mocked(client.contentClassTable).mockRejectedValue(new Error('no'));
  render(<ClassGrid revisionId="r1" onEditFeature={() => {}} />);
  expect(await screen.findByText("Couldn't load the class progression.")).toBeTruthy();
  expect(screen.queryByRole('table')).toBeNull();
});

it('asks again for another revision and ignores a late reply for the old one', async () => {
  let release: (v: ClassTableView) => void = () => {};
  vi.mocked(client.contentClassTable)
    .mockReturnValueOnce(new Promise<ClassTableView>((r) => { release = r; }))
    .mockResolvedValueOnce(view({}, { 1: { features: [{ contentId: 'c-new', name: 'Fixture New' }] } }));
  const { rerender } = render(<ClassGrid revisionId="r1" onEditFeature={() => {}} />);
  rerender(<ClassGrid revisionId="r2" onEditFeature={() => {}} />);
  await screen.findByRole('button', { name: 'Edit Fixture New' });
  release(view({}, { 1: { features: [{ contentId: 'c-old', name: 'Fixture Old' }] } }));
  await waitFor(() => expect(client.contentClassTable).toHaveBeenCalledTimes(2));
  expect(screen.queryByRole('button', { name: 'Edit Fixture Old' })).toBeNull();
});
