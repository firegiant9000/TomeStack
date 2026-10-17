// @vitest-environment jsdom
// Character details (item 17, D36): a named form on Notes. Save is held (aria-disabled, a linked reason) until something
// changed and every field is within the service's limits; over-limit input is a linked error, never cut. Values are invented.
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { Character, CharacterDetails, CharacterSheet, CharacterView } from '../api/types';
import { DetailsPanel } from './DetailsPanel';

vi.mock('../api/client', () => ({ client: { saveCharacter: vi.fn() } }));

beforeEach(() => {
  vi.mocked(client.saveCharacter).mockReset();
});
afterEach(cleanup);

const base: Character = {
  id: 'fixture-9',
  schemaVersion: 8,
  name: 'Fixture Details',
  rulesFamily: 'srd-5.1',
  level: 1,
  classes: [],
  choices: [],
  crossFamilyExceptions: [],
  baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
  pins: [],
  overrides: [],
  updatedAt: '2026-10-16T00:00:00Z',
};
const sheet: CharacterSheet = { characterId: 'fixture-9', rulesFamily: 'srd-5.1', diagnostics: [], fields: [] };
const viewOf = (over: Partial<Character> = {}): CharacterView => ({ character: { ...base, ...over }, sheet });

function setup(v: CharacterView = viewOf()) {
  const onChanged = vi.fn();
  const onError = vi.fn();
  const onStatus = vi.fn();
  render(<DetailsPanel view={v} onChanged={onChanged} onError={onError} onStatus={onStatus} />);
  const form = screen.getByRole('form', { name: 'Details' });
  const save = within(form).getByRole('button', { name: 'Save details' });
  return { form, save, onChanged, onError, onStatus };
}

const held = (el: HTMLElement) => el.getAttribute('aria-disabled') === 'true';
const reasonOf = (el: HTMLElement) => document.getElementById(el.getAttribute('aria-describedby') ?? '')?.textContent ?? '';

it('has the four groups the owner asked for, each with its fields', () => {
  const { form } = setup();
  expect(within(form).getAllByRole('group').map((g) => g.querySelector('legend')?.textContent)).toEqual(['Personality', 'Appearance', 'Background story', 'Languages and tools']);
  const personality = within(form).getByRole('group', { name: 'Personality' });
  for (const name of ['Alignment', 'Personality traits', 'Ideals', 'Bonds', 'Flaws']) expect(within(personality).getByRole('textbox', { name })).toBeTruthy();
  const look = within(form).getByRole('group', { name: 'Appearance' });
  for (const name of ['Age', 'Height', 'Weight', 'Eyes', 'Skin', 'Hair']) expect(within(look).getByRole('textbox', { name })).toBeTruthy();
  const story = within(form).getByRole('group', { name: 'Background story' });
  for (const name of ['Backstory', 'Allies and organisations']) expect(within(story).getByRole('textbox', { name })).toBeTruthy();
  const lists = within(form).getByRole('group', { name: 'Languages and tools' });
  for (const name of ['Languages', 'Tools and other']) expect(within(lists).getByRole('textbox', { name })).toBeTruthy();
});

it('holds Save while nothing changed: aria-disabled, never native disabled, with a linked reason', async () => {
  const user = userEvent.setup();
  const { save, onChanged } = setup();
  expect(save.hasAttribute('disabled')).toBe(false);
  expect(held(save)).toBe(true);
  expect(reasonOf(save)).toMatch(/nothing.*changed/i);
  await user.click(save);
  expect(client.saveCharacter).not.toHaveBeenCalled();
  expect(onChanged).not.toHaveBeenCalled();
});

it('releases Save on a change, saves the whole character with the details, and keeps focus on the button', async () => {
  const user = userEvent.setup();
  const v = viewOf({ customAttacks: [{ id: 'a1', name: 'Fixture Jab', toHit: 3 }], xp: 40 });
  const saved = viewOf({ ...v.character, details: { alignment: 'Fixture neutral', appearance: { eyes: 'Fixture grey' }, languages: ['Fixture tongue', 'Fixture signs'] } });
  vi.mocked(client.saveCharacter).mockResolvedValue(saved);
  const { form, save, onChanged, onStatus } = setup(v);
  await user.type(within(form).getByRole('textbox', { name: 'Alignment' }), '  Fixture neutral ');
  await user.type(within(form).getByRole('textbox', { name: 'Eyes' }), 'Fixture grey');
  await user.type(within(form).getByRole('textbox', { name: 'Languages' }), 'Fixture tongue{Enter}{Enter}  Fixture signs  {Enter}');
  expect(held(save)).toBe(false);
  expect(save.hasAttribute('aria-describedby')).toBe(false);
  await user.click(save);
  expect(client.saveCharacter).toHaveBeenCalledWith({
    ...v.character,
    details: { alignment: 'Fixture neutral', appearance: { eyes: 'Fixture grey' }, languages: ['Fixture tongue', 'Fixture signs'] },
  });
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(saved));
  expect(onStatus).toHaveBeenCalledWith('Details saved.');
  expect(document.activeElement).toBe(save); // aria-disabled keeps the place; focus is never dropped to <body>
});

it('sends clearing every field as no details, so the character can be v8 again', async () => {
  const user = userEvent.setup();
  const v = viewOf({ schemaVersion: 9, details: { alignment: 'Fixture neutral', languages: ['Fixture tongue'] } });
  vi.mocked(client.saveCharacter).mockResolvedValue(viewOf());
  const { form, save } = setup(v);
  expect(held(save)).toBe(true);
  expect(within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Languages' }).value).toBe('Fixture tongue');
  await user.clear(within(form).getByRole('textbox', { name: 'Alignment' }));
  await user.clear(within(form).getByRole('textbox', { name: 'Languages' }));
  await user.click(save);
  const sent = vi.mocked(client.saveCharacter).mock.calls[0]![0];
  expect('details' in sent && sent.details !== undefined).toBe(false);
});

it('refuses a text over 2,000 characters with a linked error, holds Save, and never cuts the text', async () => {
  const user = userEvent.setup();
  const { form, save } = setup();
  const backstory = within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Backstory' });
  fireEvent.change(backstory, { target: { value: 'x'.repeat(2003) } });
  expect(backstory.value.length).toBe(2003);
  expect(backstory.getAttribute('aria-invalid')).toBe('true');
  const described = (backstory.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent ?? '');
  expect(described.join(' ')).toMatch(/3 characters over the 2,000 limit/);
  expect(held(save)).toBe(true);
  expect(reasonOf(save)).toMatch(/fix/i);
  await user.click(save);
  expect(client.saveCharacter).not.toHaveBeenCalled();
  fireEvent.change(backstory, { target: { value: 'x'.repeat(2000) } });
  expect(backstory.getAttribute('aria-invalid')).toBeNull();
  expect(held(save)).toBe(false);
});

it('shows the remaining-character hint only near the limit', () => {
  const { form } = setup();
  const backstory = within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Backstory' });
  const hintText = () => (backstory.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent ?? '').join(' ');
  fireEvent.change(backstory, { target: { value: 'x'.repeat(1700) } });
  expect(hintText()).not.toMatch(/left/);
  fireEvent.change(backstory, { target: { value: 'x'.repeat(1850) } });
  expect(hintText()).toMatch(/150 characters left/);
  expect(backstory.getAttribute('aria-invalid')).toBeNull();
});

it('checks a single-line field against the same limit', () => {
  const { form, save } = setup();
  const eyes = within(form).getByRole('textbox', { name: 'Eyes' });
  fireEvent.change(eyes, { target: { value: 'y'.repeat(2001) } });
  expect(eyes.getAttribute('aria-invalid')).toBe('true');
  expect(held(save)).toBe(true);
});

it('reads one entry per line, ignores blank lines, and refuses a line over 80 characters by number, never dropping it', async () => {
  const user = userEvent.setup();
  const { form, save } = setup();
  const tools = within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Tools and other' });
  fireEvent.change(tools, { target: { value: `Fixture kit\n\n${'z'.repeat(81)}\nFixture lute` } });
  expect(tools.getAttribute('aria-invalid')).toBe('true');
  const message = (tools.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent ?? '').join(' ');
  expect(message).toMatch(/Line 3 is longer than 80 characters/);
  expect(held(save)).toBe(true);
  await user.click(save);
  expect(client.saveCharacter).not.toHaveBeenCalled();
  expect(tools.value).toContain('z'.repeat(81));
  fireEvent.change(tools, { target: { value: `Fixture kit\n${'z'.repeat(80)}` } });
  expect(tools.getAttribute('aria-invalid')).toBeNull();
  expect(held(save)).toBe(false);
});

it('refuses more than 30 entries with a linked error', () => {
  const { form, save } = setup();
  const languages = within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Languages' });
  fireEvent.change(languages, { target: { value: Array.from({ length: 31 }, (_, i) => `Fixture ${i}`).join('\n') } });
  expect(languages.getAttribute('aria-invalid')).toBe('true');
  const message = (languages.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent ?? '').join(' ');
  expect(message).toMatch(/31 entries; 30 is the most/);
  expect(held(save)).toBe(true);
  fireEvent.change(languages, { target: { value: Array.from({ length: 30 }, (_, i) => `Fixture ${i}`).join('\n') } });
  expect(held(save)).toBe(false);
});

it('keeps what was typed and reports the error when the save fails', async () => {
  const user = userEvent.setup();
  vi.mocked(client.saveCharacter).mockImplementation(() => Promise.reject(new Error('refused')));
  const { form, save, onError, onChanged } = setup();
  const flaws = within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Flaws' });
  await user.type(flaws, 'Fixture flaw');
  await user.click(save);
  await waitFor(() => expect(onError).toHaveBeenCalled());
  expect(onChanged).not.toHaveBeenCalled();
  expect(flaws.value).toBe('Fixture flaw');
  await waitFor(() => expect(held(save)).toBe(false)); // released once the failed save has finished
});

it('ignores a second press while a save runs', async () => {
  const user = userEvent.setup();
  let finish: (v: CharacterView) => void = () => {};
  vi.mocked(client.saveCharacter).mockReturnValue(new Promise((resolve) => { finish = resolve; }));
  const { form, save } = setup();
  await user.type(within(form).getByRole('textbox', { name: 'Hair' }), 'Fixture auburn');
  await user.click(save);
  await user.click(save);
  expect(client.saveCharacter).toHaveBeenCalledTimes(1);
  expect(held(save)).toBe(true);
  expect(reasonOf(save)).toMatch(/saving/i);
  finish(viewOf({ details: { appearance: { hair: 'Fixture auburn' } } }));
  // The saving reason clears once the save finishes (this harness never hands the saved view back, so the form re-seeds to the old stored one).
  await waitFor(() => expect(reasonOf(save)).not.toMatch(/saving/i));
});

// A draft saved with surrounding whitespace is stored trimmed; it must still count as untouched, so a later restore or import shows.
it.each([
  ['a list line with a trailing Enter', 'Languages', 'Fixture tongue{Enter}', { languages: ['Fixture tongue'] }, { languages: ['Fixture other'] }, 'Fixture other'],
  ['a text field with surrounding spaces', 'Ideals', '  Fixture ideal  ', { ideals: 'Fixture ideal' }, { ideals: 'Fixture changed' }, 'Fixture changed'],
] as [string, string, string, CharacterDetails, CharacterDetails, string][])('after saving %s, a later change from elsewhere shows and Save is held', async (_name, label, typed, savedDetails, laterDetails, shown) => {
  const user = userEvent.setup();
  const onChanged = vi.fn();
  const { rerender } = render(<DetailsPanel view={viewOf()} onChanged={onChanged} onError={vi.fn()} onStatus={vi.fn()} />);
  const form = screen.getByRole('form', { name: 'Details' });
  const box = within(form).getByRole<HTMLTextAreaElement>('textbox', { name: label });
  await user.type(box, typed);
  const savedView = viewOf({ details: savedDetails });
  vi.mocked(client.saveCharacter).mockResolvedValue(savedView);
  await user.click(within(form).getByRole('button', { name: 'Save details' }));
  await waitFor(() => expect(onChanged).toHaveBeenCalledWith(savedView));
  rerender(<DetailsPanel view={savedView} onChanged={onChanged} onError={vi.fn()} onStatus={vi.fn()} />);
  rerender(<DetailsPanel view={viewOf({ details: laterDetails })} onChanged={onChanged} onError={vi.fn()} onStatus={vi.fn()} />);
  expect(within(form).getByRole<HTMLTextAreaElement>('textbox', { name: label }).value).toBe(shown);
  expect(held(within(form).getByRole('button', { name: 'Save details' }))).toBe(true);
});

it('takes a saved change from elsewhere into fields the user has not edited, and keeps the ones being edited', async () => {
  const user = userEvent.setup();
  const onChanged = vi.fn();
  const { rerender } = render(<DetailsPanel view={viewOf()} onChanged={onChanged} onError={vi.fn()} onStatus={vi.fn()} />);
  const form = screen.getByRole('form', { name: 'Details' });
  await user.type(within(form).getByRole('textbox', { name: 'Bonds' }), 'Fixture bond');
  rerender(<DetailsPanel view={viewOf({ details: { ideals: 'Fixture ideal' } })} onChanged={onChanged} onError={vi.fn()} onStatus={vi.fn()} />);
  expect(within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Ideals' }).value).toBe('Fixture ideal');
  expect(within(form).getByRole<HTMLTextAreaElement>('textbox', { name: 'Bonds' }).value).toBe('Fixture bond');
});
