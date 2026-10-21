// @vitest-environment jsdom
// The printable backup carries details only when "Include details" is ticked (item 17, D36), like session notes. Values are invented.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet, CharacterView } from '../api/types';
import { PrintView } from './PrintView';

vi.mock('../api/client', () => ({ client: { previewExport: vi.fn(), info: vi.fn(), listGapNotes: vi.fn() } }));

beforeEach(() => {
  vi.mocked(client.previewExport).mockResolvedValue({ included: [] } as never);
  vi.mocked(client.info).mockResolvedValue({ version: '0.0.0-fixture' } as never);
});
afterEach(cleanup);

const character: Character = {
  id: 'fixture-p',
  schemaVersion: 9,
  name: 'Fixture Printer',
  rulesFamily: 'srd-5.1',
  level: 1,
  classes: [],
  choices: [],
  crossFamilyExceptions: [],
  baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
  pins: [],
  overrides: [],
  details: { alignment: 'Fixture neutral', appearance: { eyes: 'Fixture grey' }, backstory: 'Fixture long story.', languages: ['Fixture tongue', 'Fixture signs'], toolsAndOther: ['Fixture kit'] },
  notes: [{ id: 'n1', date: '2026-10-01', text: 'Fixture session text.', createdAt: '2026-10-01T20:00:00Z' }],
  updatedAt: '2026-10-16T00:00:00Z',
};
const sheet: CharacterSheet = { characterId: 'fixture-p', rulesFamily: 'srd-5.1', diagnostics: [], fields: [] };
const view: CharacterView = { character, sheet };

it('leaves details out until "Include details" is ticked, separately from the session notes tick', async () => {
  const user = userEvent.setup();
  render(<PrintView view={view} onError={() => {}} onClose={() => {}} />);
  const preview = screen.getByRole('region', { name: 'Print preview' });
  const tick = within(preview).getByRole<HTMLInputElement>('checkbox', { name: /^Include details/ });
  expect(tick.checked).toBe(false);
  expect(within(preview).queryByRole('heading', { name: 'Details' })).toBeNull();
  expect(preview.textContent).not.toContain('Fixture long story.');
  await user.click(tick);
  expect(within(preview).getByRole('heading', { name: 'Details' })).toBeTruthy();
  expect(preview.textContent).toContain('Fixture long story.');
  expect(preview.textContent).toContain('Fixture tongue, Fixture signs');
  expect(within(preview).getByText('Eyes')).toBeTruthy();
  expect(preview.textContent).not.toContain('Fixture session text.'); // the other tick is its own
  expect(within(preview).getByRole<HTMLInputElement>('checkbox', { name: /^Include session notes/ }).checked).toBe(false);
});

it('prints no Details heading when ticked but nothing is set', async () => {
  const user = userEvent.setup();
  render(<PrintView view={{ ...view, character: { ...character, details: undefined } }} onError={() => {}} onClose={() => {}} />);
  const preview = screen.getByRole('region', { name: 'Print preview' });
  await user.click(within(preview).getByRole('checkbox', { name: /^Include details/ }));
  expect(within(preview).queryByRole('heading', { name: 'Details' })).toBeNull();
});
