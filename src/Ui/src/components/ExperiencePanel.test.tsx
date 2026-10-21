// @vitest-environment jsdom
// Wave 3 item 27: optional experience points on Stats. Off until "Track XP" is ticked; "Level up available" is display only.
// The client is mocked; values are invented.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterSheet, CharacterView } from '../api/types';
import { ExperiencePanel } from './ExperiencePanel';

vi.mock('../api/client', () => ({ client: { saveCharacter: vi.fn(), listContent: vi.fn() } }));

beforeEach(() => vi.mocked(client.saveCharacter).mockReset());
afterEach(cleanup);

const character: Character = {
  id: 'fixture-xp',
  schemaVersion: 9,
  name: 'Fixture Tally',
  rulesFamily: 'srd-5.2.1',
  level: 3,
  classes: [],
  choices: [],
  crossFamilyExceptions: [],
  baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
  pins: [],
  overrides: [],
  details: { alignment: 'Fixture neutral' },
  updatedAt: '2026-10-20T00:00:00Z',
};
const sheet: CharacterSheet = { characterId: 'fixture-xp', rulesFamily: 'srd-5.2.1', diagnostics: [], fields: [] };
const viewOf = (xp?: number, extra: Partial<CharacterSheet> = {}): CharacterView => ({
  character: { ...character, ...(xp === undefined ? {} : { xp }) },
  sheet: { ...sheet, ...extra },
});

const echo = () => vi.mocked(client.saveCharacter).mockImplementation(async (c) => ({ character: c, sheet }));

it('is off by default: the checkbox is unticked and there is no number field', () => {
  render(<ExperiencePanel view={viewOf()} onChanged={() => {}} onError={() => {}} />);
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Track XP' }).checked).toBe(false);
  expect(screen.queryByRole('spinbutton', { name: 'Experience points' })).toBeNull();
  expect(screen.queryByText('Level up available')).toBeNull();
});

it('ticking Track XP saves a total of 0 through the character save and keeps the other fields', async () => {
  echo();
  const user = userEvent.setup();
  const onChanged = vi.fn();
  render(<ExperiencePanel view={viewOf()} onChanged={onChanged} onError={() => {}} />);
  await user.click(screen.getByRole('checkbox', { name: 'Track XP' }));
  const sent = vi.mocked(client.saveCharacter).mock.lastCall![0];
  expect(sent.xp).toBe(0);
  expect(sent.details).toEqual(character.details);
  expect(onChanged).toHaveBeenCalled();
});

it('shows the stored total in a labelled number field while tracked, with the next threshold in grouped digits', () => {
  render(<ExperiencePanel view={viewOf(1200, { xpForNextLevel: 2700 })} onChanged={() => {}} onError={() => {}} />);
  expect(screen.getByRole<HTMLInputElement>('checkbox', { name: 'Track XP' }).checked).toBe(true);
  expect(screen.getByRole<HTMLInputElement>('spinbutton', { name: 'Experience points' }).value).toBe('1200');
  expect(screen.getByText('Next level at 2,700')).toBeTruthy();
  expect(screen.queryByText('Level up available')).toBeNull();
});

it('shows Level up available as plain text (no live region) when the sheet says so', () => {
  render(<ExperiencePanel view={viewOf(2700, { xpForNextLevel: 2700, levelUpAvailable: true })} onChanged={() => {}} onError={() => {}} />);
  const hint = screen.getByText('Level up available');
  expect(hint.closest('[aria-live], [role="status"], [role="alert"]')).toBeNull();
});

it('saves an edited total and keeps focus on the section afterwards', async () => {
  echo();
  const user = userEvent.setup();
  render(<ExperiencePanel view={viewOf(1200)} onChanged={() => {}} onError={() => {}} />);
  const box = screen.getByRole('spinbutton', { name: 'Experience points' });
  await user.clear(box);
  await user.type(box, '2700');
  await user.click(screen.getByRole('button', { name: 'Save experience points' }));
  expect(vi.mocked(client.saveCharacter).mock.lastCall![0].xp).toBe(2700);
  expect(document.activeElement?.id).toBe('experience-heading');
});

it('unticking Track XP saves no total (the stored one is dropped)', async () => {
  echo();
  const user = userEvent.setup();
  render(<ExperiencePanel view={viewOf(1200)} onChanged={() => {}} onError={() => {}} />);
  await user.click(screen.getByRole('checkbox', { name: 'Track XP' }));
  const sent = vi.mocked(client.saveCharacter).mock.lastCall![0];
  expect(sent.xp).toBeUndefined();
  expect('xp' in sent && sent.xp !== undefined).toBe(false);
});

it.each([['-1'], ['1000001'], ['12.5'], ['']])('refuses %j in text and does not save', async (typed) => {
  const user = userEvent.setup();
  render(<ExperiencePanel view={viewOf(1200)} onChanged={() => {}} onError={() => {}} />);
  const box = screen.getByRole('spinbutton', { name: 'Experience points' });
  await user.clear(box);
  if (typed) await user.type(box, typed);
  await user.click(screen.getByRole('button', { name: 'Save experience points' }));
  expect(client.saveCharacter).not.toHaveBeenCalled();
  expect(screen.getByText('Experience points must be a whole number from 0 to 1,000,000.')).toBeTruthy();
  expect(box.getAttribute('aria-invalid')).toBe('true');
});

it('re-seeds the field when the stored total changes (a restore keeps the character id)', () => {
  const { rerender } = render(<ExperiencePanel view={viewOf(1200)} onChanged={() => {}} onError={() => {}} />);
  rerender(<ExperiencePanel view={viewOf(300)} onChanged={() => {}} onError={() => {}} />);
  expect(screen.getByRole<HTMLInputElement>('spinbutton', { name: 'Experience points' }).value).toBe('300');
});
