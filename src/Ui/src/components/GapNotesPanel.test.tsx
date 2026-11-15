// @vitest-environment jsdom
// Gap notes on a sheet: add, resolve, delete, and "Copy as issue text" (select-then-Ctrl+C, no clipboard route).
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { CharacterView, GapNote } from '../api/types';
import { expectNoAxeViolations } from '../test/axe';
import { GapNotesPanel } from './GapNotesPanel';

vi.mock('../api/client', () => ({
  client: { listGapNotes: vi.fn(), addGapNote: vi.fn(), setGapNoteStatus: vi.fn(), deleteGapNote: vi.fn(), info: vi.fn() },
}));

const view = {
  character: { id: 'fixture-a', name: 'Fixture Avery' },
  sheet: { features: [], fields: [{ field: 'ac', label: 'Armor Class' }] },
} as unknown as CharacterView;

const note: GapNote = {
  id: 'note-1',
  characterId: 'fixture-a',
  target: { kind: 'field', fieldId: 'ac', label: 'Armor Class' },
  text: 'The table grants a cover bonus here.',
  status: 'open',
  schemaVersion: 1,
  createdAt: '2026-11-01T00:00:00Z',
  updatedAt: '2026-11-01T00:00:00Z',
};

function renderPanel(about = '', props: { onStatus?: (t: string) => void; onError?: (e: unknown) => void } = {}) {
  return render(<GapNotesPanel view={view} onError={props.onError ?? (() => {})} onStatus={props.onStatus ?? (() => {})} about={about} onAboutChange={() => {}} />);
}

beforeEach(() => {
  vi.mocked(client.listGapNotes).mockResolvedValue([note]);
  vi.mocked(client.info).mockResolvedValue({ version: '0.8.1' } as Awaited<ReturnType<typeof client.info>>);
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

it('saves a note about the chosen field', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  vi.mocked(client.addGapNote).mockResolvedValue(note);
  renderPanel('field:ac', { onStatus });
  await screen.findByText(note.text);
  await user.type(screen.getByLabelText(/What was missing or wrong/), 'Cover is not counted.');
  await user.click(screen.getByRole('button', { name: 'Save note' }));
  await waitFor(() => expect(client.addGapNote).toHaveBeenCalledWith('fixture-a', { kind: 'field', fieldId: 'ac' }, 'Cover is not counted.'));
  await waitFor(() => expect(onStatus).toHaveBeenCalledWith('Gap note saved for Armor Class.'));
});

it('marks a note resolved', async () => {
  const user = userEvent.setup();
  vi.mocked(client.setGapNoteStatus).mockResolvedValue(undefined as never);
  renderPanel();
  await user.click(await screen.findByRole('button', { name: 'Mark resolved: Armor Class' }));
  expect(client.setGapNoteStatus).toHaveBeenCalledWith('note-1', 'resolved');
});

it('deletes a note only after confirming', async () => {
  const user = userEvent.setup();
  vi.mocked(client.deleteGapNote).mockResolvedValue(undefined as never);
  renderPanel();
  await user.click(await screen.findByRole('button', { name: 'Delete…' }));
  expect(client.deleteGapNote).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Delete note' }));
  await waitFor(() => expect(client.deleteGapNote).toHaveBeenCalledWith('note-1'));
});

it('shows the issue text selected and focused, with the hint and the warning', async () => {
  const user = userEvent.setup();
  renderPanel();
  await user.click(await screen.findByRole('button', { name: /Copy as issue text/ }));
  const box = (await screen.findByLabelText('Issue text: Armor Class')) as HTMLTextAreaElement;
  expect(box.readOnly).toBe(true);
  expect(box.value).toBe('Gap note: Armor Class\nThe table grants a cover bonus here.\n\nTomeStack 0.8.1');
  expect(box.value).not.toContain('Fixture Avery');
  expect(document.activeElement).toBe(box);
  expect(box.selectionStart).toBe(0);
  expect(box.selectionEnd).toBe(box.value.length);
  expect(screen.getByText(/press Ctrl\+C to copy it/)).toBeTruthy();
  expect(screen.getByText('This may include text from your books or homebrew; check before posting.')).toBeTruthy();
});

it('still shows the text when the version cannot be read, and Close returns focus to the button', async () => {
  const user = userEvent.setup();
  vi.mocked(client.info).mockRejectedValue(new Error('fixture failure'));
  renderPanel();
  const button = await screen.findByRole('button', { name: /Copy as issue text/ });
  await user.click(button);
  expect(((await screen.findByLabelText('Issue text: Armor Class')) as HTMLTextAreaElement).value).toContain('TomeStack unknown version');
  await user.click(screen.getByRole('button', { name: 'Close' }));
  expect(screen.queryByLabelText('Issue text: Armor Class')).toBeNull();
  expect(document.activeElement).toBe(button);
});

it('has no axe violations with the issue text open', async () => {
  const user = userEvent.setup();
  const { container } = renderPanel();
  await user.click(await screen.findByRole('button', { name: /Copy as issue text/ }));
  await screen.findByLabelText('Issue text: Armor Class');
  await expectNoAxeViolations(container);
});
