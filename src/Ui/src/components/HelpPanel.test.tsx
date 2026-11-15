// @vitest-environment jsdom
// Help: one h1 and an h2 per section; the guides, the keyboard shortcuts, every character's gap notes and the glossary.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { GapNoteListing } from '../api/types';
import { expectNoAxeViolations } from '../test/axe';
import { HelpPanel } from './HelpPanel';

vi.mock('../api/client', () => ({ client: { listAllGapNotes: vi.fn(), setGapNoteStatus: vi.fn() } }));

const note: GapNoteListing = {
  characterName: 'Fixture Avery',
  note: {
    id: 'note-1',
    characterId: 'fixture-a',
    target: { kind: 'field', fieldId: 'ac', label: 'Armor Class' },
    text: 'The table grants a cover bonus here.',
    status: 'open',
    schemaVersion: 1,
    createdAt: '2026-11-01T00:00:00Z',
    updatedAt: '2026-11-01T00:00:00Z',
  },
};

beforeEach(() => {
  vi.mocked(client.listAllGapNotes).mockResolvedValue([note]);
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

it('has one h1 and the sections in order as h2', async () => {
  render(<HelpPanel onError={() => {}} onOpenCharacter={() => {}} />);
  expect(screen.getAllByRole('heading', { level: 1 }).map((h) => h.textContent)).toEqual(['Help']);
  expect(screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)).toEqual([
    'Getting started',
    "The sheet's six tabs",
    '"Why?" and how numbers are worked out',
    'Backups and moving to another computer',
    "What the sheet can't do yet",
    'Keyboard shortcuts',
    'Gap notes',
    'Glossary',
  ]);
  await screen.findByText('The table grants a cover bonus here.');
});

it('moves focus to the h1 on mount', async () => {
  render(<HelpPanel onError={() => {}} onOpenCharacter={() => {}} />);
  expect(document.activeElement).toBe(screen.getByRole('heading', { level: 1, name: 'Help' }));
  await screen.findByText('The table grants a cover bonus here.');
});

it('shows the guides under their sections, with the six tabs and the More list', async () => {
  render(<HelpPanel onError={() => {}} onOpenCharacter={() => {}} />);
  const started = screen.getByRole('region', { name: 'Getting started' });
  expect(within(started).getByRole('heading', { level: 3, name: 'What TomeStack is' })).toBeTruthy();
  expect(started.textContent).not.toContain('Manage');
  const tabs = screen.getByRole('region', { name: "The sheet's six tabs" });
  expect(within(tabs).getAllByRole('listitem').map((li) => li.textContent?.split(':')[0])).toEqual(['Play', 'Spells', 'Gear', 'Features', 'Skills and saves', 'Journal']);
  expect(tabs.textContent).toContain('Archive');
  const gaps = screen.getByRole('region', { name: "What the sheet can't do yet" });
  expect(within(gaps).getByRole('heading', { level: 3, name: 'Spells' })).toBeTruthy();
  await screen.findByText('The table grants a cover bonus here.');
});

it('lists the keyboard shortcuts', async () => {
  render(<HelpPanel onError={() => {}} onOpenCharacter={() => {}} />);
  const section = screen.getByRole('region', { name: 'Keyboard shortcuts' });
  expect(within(section).getByText('Ctrl+B', { selector: 'dt' }).nextElementSibling!.textContent).toContain('sidebar');
  await screen.findByText('The table grants a cover bonus here.');
});

it('embeds every character’s gap notes, and Open <character> goes to that character', async () => {
  const user = userEvent.setup();
  const onOpen = vi.fn();
  render(<HelpPanel onError={() => {}} onOpenCharacter={onOpen} />);
  const section = screen.getByRole('region', { name: 'Gap notes' });
  expect(await within(section).findByText('The table grants a cover bonus here.')).toBeTruthy();
  expect(within(section).queryAllByRole('heading', { level: 1 })).toHaveLength(0);
  await user.click(within(section).getByRole('button', { name: 'Open Fixture Avery' }));
  expect(onOpen).toHaveBeenCalledWith('fixture-a');
});

it('defines exactly the glossary terms', async () => {
  render(<HelpPanel onError={() => {}} onOpenCharacter={() => {}} />);
  const section = screen.getByRole('region', { name: 'Glossary' });
  const terms = Array.from(section.querySelectorAll('dt')).map((dt) => dt.textContent);
  expect(terms).toEqual(['Draft', 'version', 'rules (2014/2024)', 'campaign', 'TomeStack file', 'Why?', 'You apply this', 'Text only']);
  for (const dd of Array.from(section.querySelectorAll('dd'))) expect((dd.textContent ?? '').length).toBeGreaterThan(10);
  await screen.findByText('The table grants a cover bonus here.');
});

it('reports a failed gap notes read', async () => {
  const onError = vi.fn();
  vi.mocked(client.listAllGapNotes).mockRejectedValue(new Error('fixture failure'));
  render(<HelpPanel onError={onError} onOpenCharacter={() => {}} />);
  await waitFor(() => expect(onError).toHaveBeenCalled());
});

it('reaches the first interactive control with Tab', async () => {
  const user = userEvent.setup();
  render(<HelpPanel onError={() => {}} onOpenCharacter={() => {}} />);
  await screen.findByText('The table grants a cover bonus here.');
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('checkbox', { name: 'Show resolved notes' }));
});

it('has no axe violations', async () => {
  const { container } = render(<HelpPanel onError={() => {}} onOpenCharacter={() => {}} />);
  await screen.findByText('The table grants a cover bonus here.');
  await expectNoAxeViolations(container);
});
