// @vitest-environment jsdom
// Item 12: the spell editor writes the `spell` effect. Its lists use item 10's key normaliser and datalist.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect, useState } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, ContentRevision, Effect, SourceRecord } from '../api/types';
import { spellOf } from '../entryBasics';
import { SpellBasicsEditor } from './SpellBasicsEditor';

vi.mock('../api/client', () => ({ client: { spellListKeys: vi.fn() } }));

const info = { rulesFamilies: [], fields: [] } as unknown as AppInfo;
const source = { id: 'src', title: 'Fixture Book', rulesFamilies: ['srd-5.1'], editionVersion: 'homebrew' } as SourceRecord;

const revisionOf = (effects: Effect[] = []): ContentRevision => ({
  contentId: 'sp', revisionId: 'x', kind: 'spell', name: 'Fixture Spark', rulesFamilies: ['srd-5.1'], provenance: { sourceId: 'src' }, status: 'draft', effects,
});

let latest: ContentRevision;
function Harness(props: { initial: ContentRevision; onProblem?: (field: string, problem: string | undefined) => void; onError?: (e: unknown) => void }) {
  const [revision, setRevision] = useState(props.initial);
  useEffect(() => {
    latest = revision;
  });
  return (
    <SpellBasicsEditor
      revision={revision}
      source={source}
      info={info}
      known={[]}
      onEffects={(update) => setRevision((r) => ({ ...r, effects: update(r.effects) }))}
      onBusy={() => {}}
      onMade={() => {}}
      onProblem={props.onProblem ?? (() => {})}
      onError={props.onError ?? (() => {})}
    />
  );
}

beforeEach(() => {
  vi.mocked(client.spellListKeys).mockReset();
  vi.mocked(client.spellListKeys).mockResolvedValue([
    { key: 'wizard', spells: 3 },
    { key: 'cleric', spells: 0 },
  ]);
});
afterEach(cleanup);

it('shows a spell with no data as a level 1 spell on no list', async () => {
  render(<Harness initial={revisionOf()} />);
  expect((await screen.findByRole('combobox', { name: 'Level' })) as HTMLSelectElement).toHaveProperty('value', '1');
  expect(screen.getByRole('checkbox', { name: 'Ritual' })).toHaveProperty('checked', false);
  expect(screen.getByRole('checkbox', { name: 'Concentration' })).toHaveProperty('checked', false);
  expect(screen.getByText('On no spell list yet: no caster can choose it.')).toBeTruthy();
});

it('reads an existing spell back into every field', async () => {
  const spell: Effect = {
    type: 'spell', id: 'spell', level: 0, school: 'evocation', castingTime: '1 action', range: '60 feet', components: 'V, S', duration: 'Instantaneous',
    concentration: true, ritual: true, lists: ['wizard'], text: 'A fixture bolt.',
  };
  render(<Harness initial={revisionOf([spell])} />);
  expect((await screen.findByRole('combobox', { name: 'Level' })) as HTMLSelectElement).toHaveProperty('value', '0');
  expect(screen.getByRole('combobox', { name: 'Level' }).textContent).toContain('Cantrip');
  expect(screen.getByRole('combobox', { name: 'School' })).toHaveProperty('value', 'evocation');
  expect(screen.getByRole('textbox', { name: 'Casting time' })).toHaveProperty('value', '1 action');
  expect(screen.getByRole('textbox', { name: 'Range' })).toHaveProperty('value', '60 feet');
  expect(screen.getByRole('textbox', { name: 'Components' })).toHaveProperty('value', 'V, S');
  expect(screen.getByRole('textbox', { name: 'Duration' })).toHaveProperty('value', 'Instantaneous');
  expect(screen.getByRole('textbox', { name: 'Spell text' })).toHaveProperty('value', 'A fixture bolt.');
  expect(screen.getByRole('checkbox', { name: 'Concentration' })).toHaveProperty('checked', true);
  expect(screen.getByRole('checkbox', { name: 'Ritual' })).toHaveProperty('checked', true);
  expect(within(screen.getByRole('list', { name: 'Spell lists' })).getByText('wizard')).toBeTruthy();
});

it('keeps a school the form does not list, so a stored value is never lost', async () => {
  const spell: Effect = { type: 'spell', id: 'spell', level: 2, school: 'fixture-school' };
  render(<Harness initial={revisionOf([spell])} />);
  expect(await screen.findByRole('combobox', { name: 'School' })).toHaveProperty('value', 'fixture-school');
});

it('writes each field into the spell effect and leaves the other effects and fields alone', async () => {
  const user = userEvent.setup();
  const other: Effect = { type: 'roll', id: 'r', rollId: 'r', label: 'Fixture roll', dice: '1d4' };
  const spell: Effect = { type: 'spell', id: 'spell', level: 1, attack: 'ranged', dice: '2d6', save: 'dex' };
  render(<Harness initial={revisionOf([other, spell])} />);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Level' }), '3');
  await user.selectOptions(screen.getByRole('combobox', { name: 'School' }), 'illusion');
  await user.type(screen.getByRole('textbox', { name: 'Casting time' }), '1 bonus action');
  await user.type(screen.getByRole('textbox', { name: 'Range' }), 'Self');
  await user.type(screen.getByRole('textbox', { name: 'Components' }), 'V');
  await user.type(screen.getByRole('textbox', { name: 'Duration' }), 'Up to 1 minute');
  await user.type(screen.getByRole('textbox', { name: 'Spell text' }), 'It glows.');
  await user.click(screen.getByRole('checkbox', { name: 'Concentration' }));
  await user.click(screen.getByRole('checkbox', { name: 'Ritual' }));

  expect(latest.effects[0]).toBe(other);
  expect(spellOf(latest.effects)).toEqual({
    type: 'spell', id: 'spell', level: 3, school: 'illusion', castingTime: '1 bonus action', range: 'Self', components: 'V', duration: 'Up to 1 minute',
    text: 'It glows.', concentration: true, ritual: true, attack: 'ranged', dice: '2d6', save: 'dex',
  });
});

it('lists: previews the saved key, offers the keys in use, adds it once, and removes it', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf([{ type: 'spell', id: 'spell', level: 1, lists: [] }])} />);
  const input = await screen.findByRole('combobox', { name: 'Add a spell list' });
  await vi.waitFor(() => expect(document.querySelectorAll('datalist option')).toHaveLength(2));
  expect([...document.querySelectorAll('datalist option')].map((o) => o.getAttribute('value'))).toEqual(['wizard', 'cleric']);

  await user.type(input, 'Fixture Arcane  List');
  expect(screen.getByText('Saved as: fixture-arcane-list')).toBeTruthy();
  expect(screen.getByText(/No list has this key yet/)).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Add list' }));
  expect(spellOf(latest.effects)!.lists).toEqual(['fixture-arcane-list']);
  expect(input).toHaveProperty('value', '');

  await user.type(input, 'wizard');
  expect(screen.getByText('A list with this key is already in use by 3 spells.')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Add list' }));
  expect(spellOf(latest.effects)!.lists).toEqual(['fixture-arcane-list', 'wizard']);

  await user.type(input, 'wizard');
  const again = screen.getByRole('button', { name: 'Add list' });
  expect(again.getAttribute('aria-disabled')).toBe('true');
  expect(document.getElementById(again.getAttribute('aria-describedby') ?? '')?.textContent).toBe('The spell is already on the list wizard.');
  await user.click(again);
  expect(spellOf(latest.effects)!.lists).toEqual(['fixture-arcane-list', 'wizard']);

  await user.click(screen.getByRole('button', { name: 'Remove list fixture-arcane-list' }));
  expect(spellOf(latest.effects)!.lists).toEqual(['wizard']);
});

it('a key that cannot be saved is marked invalid and linked to its reason; the add is refused with focus kept', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf([{ type: 'spell', id: 'spell', level: 1, lists: [] }])} />);
  const input = await screen.findByRole('combobox', { name: 'Add a spell list' });
  await user.type(input, 'bad_key!');
  expect(input.getAttribute('aria-invalid')).toBe('true');
  const reason = document.getElementById(input.getAttribute('aria-describedby') ?? '');
  expect(reason?.textContent).toContain('A spell list key is 1 to 60 letters, digits or hyphens.');
  const add = screen.getByRole('button', { name: 'Add list' });
  expect(add.getAttribute('aria-disabled')).toBe('true');
  expect(add).toHaveProperty('disabled', false);
  add.focus();
  await user.click(add);
  expect(spellOf(latest.effects)!.lists).toEqual([]);
  expect(document.activeElement).toBe(add);
});

it('a typed key that was never added blocks saving, with a reason that names the field', async () => {
  const user = userEvent.setup();
  const onProblem = vi.fn();
  render(<Harness initial={revisionOf([{ type: 'spell', id: 'spell', level: 1, lists: [] }])} onProblem={onProblem} />);
  const input = await screen.findByRole('combobox', { name: 'Add a spell list' });
  await user.type(input, 'wizard');
  expect(onProblem).toHaveBeenLastCalledWith('spell-lists', 'Spell lists: add the typed list key with Add list, or clear it.');
  await user.clear(input);
  expect(onProblem).toHaveBeenLastCalledWith('spell-lists', undefined);
});

it('reports a failed key lookup through onError and still lets lists be typed', async () => {
  vi.mocked(client.spellListKeys).mockRejectedValue(new Error('x'));
  const onError = vi.fn();
  render(<Harness initial={revisionOf()} onError={onError} />);
  await vi.waitFor(() => expect(onError).toHaveBeenCalledTimes(1));
  expect(screen.getByRole('combobox', { name: 'Add a spell list' })).toBeTruthy();
});

it('has no inline style and no second status region', async () => {
  const { container } = render(<Harness initial={revisionOf()} />);
  await screen.findByRole('combobox', { name: 'Level' });
  expect(container.querySelectorAll('[style]').length).toBe(0);
  expect(container.querySelectorAll('[role="status"]').length).toBe(0);
});

// MECH-X item 1 (T5, R39): spell tags use the strict tag reading; `all` is refused.
it('tags: writes the typed keys as the spell tags and removes the key when emptied', async () => {
  const user = userEvent.setup();
  const spell: Effect = { type: 'spell', id: 'spell', level: 1, school: 'evocation', lists: [] };
  render(<Harness initial={revisionOf([spell])} />);
  const field = await screen.findByRole('textbox', { name: /^Tags/ });
  await user.type(field, 'time, ritualLike');
  expect(spellOf(latest.effects)).toMatchObject({ tags: ['time', 'ritualLike'], school: 'evocation' });
  await user.clear(field);
  expect('tags' in spellOf(latest.effects)!).toBe(false);
});

it('tags: an empty token or all blocks saving, never dropped, and the bad text is not written', async () => {
  const user = userEvent.setup();
  const problems: Record<string, string | undefined> = {};
  const spell: Effect = { type: 'spell', id: 'spell', level: 1, lists: [], tags: ['time'] };
  render(<Harness initial={revisionOf([spell])} onProblem={(field, problem) => { problems[field] = problem; }} />);
  const field = await screen.findByRole('textbox', { name: /^Tags/ });
  await user.clear(field);
  await user.type(field, 'a,,b');
  expect(problems['spell-tags']).toMatch(/empty/);
  expect(field.getAttribute('aria-invalid')).toBe('true');
  await user.clear(field);
  await user.type(field, 'all');
  expect(problems['spell-tags']).toMatch(/"all"/);
  expect(spellOf(latest.effects)).not.toMatchObject({ tags: ['all'] });
  await user.clear(field);
  await user.type(field, 'time');
  expect(problems['spell-tags']).toBeUndefined();
});
