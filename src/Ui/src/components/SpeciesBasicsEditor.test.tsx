// @vitest-environment jsdom
// Item 12: the species editor. Ability increases follow the family's policy (never its name); traits are grants of features.
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, ContentReference, ContentRevision, Effect, RulesFamilyId, SourceRecord } from '../api/types';
import { abilityIncreases, skillGrants, type Known } from '../entryBasics';
import { SpeciesBasicsEditor } from './SpeciesBasicsEditor';

vi.mock('../api/client', () => ({ client: { saveDraft: vi.fn() } }));

const info = {
  rulesFamilies: [
    { id: 'srd-5.1', displayName: 'Fixture Older Rules', abilityIncreaseSource: 'species', backgroundGrantsFeat: false },
    { id: 'srd-5.2.1', displayName: 'Fixture Newer Rules', abilityIncreaseSource: 'background', backgroundGrantsFeat: true },
  ],
  fields: [
    { id: 'skill.athletics', label: 'Athletics' },
    { id: 'skill.stealth', label: 'Stealth' },
    { id: 'initiative', label: 'Initiative' },
  ],
} as unknown as AppInfo;
const source = { id: 'src', title: 'Fixture Book', rulesFamilies: ['srd-5.1', 'srd-5.2.1'], editionVersion: 'homebrew' } as SourceRecord;
const trait: Known = { contentId: 'f1', name: 'Fixture Keen Senses', kind: 'feature', reference: { contentId: 'f1', revisionId: 'f1r' }, draft: false, own: true };
const other: Known = { contentId: 'f2', name: 'Fixture Sure Foot', kind: 'feature', reference: { contentId: 'f2', revisionId: 'f2r' }, draft: true, own: true };

function revisionOf(families: RulesFamilyId[], effects: Effect[] = []): ContentRevision {
  return { contentId: 'sp', revisionId: 'x', kind: 'species', name: 'Fixture Folk', rulesFamilies: families, provenance: { sourceId: 'src' }, status: 'draft', effects };
}

/** Holds the revision the way the studio does, applying each update to the latest effects. */
function Harness(props: {
  initial: ContentRevision;
  known?: Known[];
  onChange?: (r: ContentRevision) => void;
  onMade?: (k: Known) => void;
  onProblem?: (field: string, problem: string | undefined) => void;
  newerPublished?: (r: ContentReference) => ContentReference | undefined;
  /** Adds a button that unticks the rules families, as the studio's family boxes do. */
  untick?: boolean;
  /** Adds a button that adds one more speed replace, as an edit in the effects list would. */
  addReplace?: boolean;
}) {
  const [revision, setRevision] = useState(props.initial);
  props.onChange?.(revision);
  return (
    <>
      {props.addReplace && (
        <button
          type="button"
          onClick={() =>
            setRevision((r) => ({ ...r, effects: [...r.effects, { type: 'modifier', id: `other-${r.effects.length}`, operation: 'replace', target: 'speed', value: '35' }] }))
          }
        >
          Add another speed replace
        </button>
      )}
      {props.untick && (
        <button type="button" onClick={() => setRevision((r) => ({ ...r, rulesFamilies: ['srd-5.2.1'] }))}>
          Untick older rules
        </button>
      )}
    <SpeciesBasicsEditor
      newerPublished={props.newerPublished ?? (() => undefined)}
      revision={revision}
      source={source}
      info={info}
      known={props.known ?? [trait, other]}
      onEffects={(update) => setRevision((r) => ({ ...r, effects: update(r.effects) }))}
      onBusy={() => {}}
      onMade={props.onMade ?? (() => {})}
      onProblem={props.onProblem ?? (() => {})}
      onError={() => {}}
    />
    </>
  );
}

let latest: ContentRevision;
beforeEach(() => {
  vi.mocked(client.saveDraft).mockReset();
});
afterEach(cleanup);

it('older rules: shows ability increases as number fields and writes one bonus modifier each', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.1'])} onChange={(r) => (latest = r)} />);
  const group = screen.getByRole('group', { name: /Ability score increases/ });
  expect(within(group).getByRole('textbox', { name: 'Strength' })).toBeTruthy();
  expect(within(group).getByRole('textbox', { name: 'Charisma' })).toBeTruthy();
  expect(screen.queryByText(/ability increases come from the background/)).toBeNull();

  await user.type(within(group).getByRole('textbox', { name: 'Strength' }), '2');
  await user.type(within(group).getByRole('textbox', { name: 'Charisma' }), '1');
  expect(abilityIncreases(latest.effects)).toEqual({ str: 2, cha: 1 });
  expect(latest.effects[0]).toMatchObject({ type: 'modifier', operation: 'bonus', target: 'ability.str.score', value: '2' });

  await user.clear(within(group).getByRole('textbox', { name: 'Strength' }));
  expect(abilityIncreases(latest.effects)).toEqual({ cha: 1 });
});

it('newer rules: no increase fields, and it says where the increases come from', () => {
  render(<Harness initial={revisionOf(['srd-5.2.1'])} />);
  expect(screen.queryByRole('textbox', { name: 'Strength' })).toBeNull();
  expect(screen.getByText('In Fixture Newer Rules, ability increases come from the background.')).toBeTruthy();
});

it('a species for both families shows both behaviours, each labelled by the family name', () => {
  render(<Harness initial={revisionOf(['srd-5.1', 'srd-5.2.1'])} />);
  const group = screen.getByRole('group', { name: 'Ability score increases (Fixture Older Rules)' });
  expect(within(group).getByRole('textbox', { name: 'Wisdom' })).toBeTruthy();
  expect(screen.getByText('In Fixture Newer Rules, ability increases come from the background.')).toBeTruthy();
});

it('keeps showing increases that exist when no ticked family uses them, and says they are ignored', () => {
  const increase: Effect = { type: 'modifier', id: 'a', operation: 'bonus', target: 'ability.dex.score', value: '2' };
  render(<Harness initial={revisionOf(['srd-5.2.1'], [increase])} />);
  expect(screen.getByRole('textbox', { name: 'Dexterity' })).toHaveProperty('value', '2');
  expect(screen.getByText(/ignored in Fixture Newer Rules/)).toBeTruthy();
});

it('reads an existing species back: values, skills and traits by name', () => {
  const effects: Effect[] = [
    { type: 'modifier', id: 'a', operation: 'bonus', target: 'ability.con.score', value: '2' },
    { type: 'grant', id: 's', grant: 'proficiency', target: 'skill.stealth' },
    { type: 'grant', id: 't', grant: 'content', content: trait.reference },
  ];
  render(<Harness initial={revisionOf(['srd-5.1'], effects)} />);
  expect(screen.getByRole('textbox', { name: 'Constitution' })).toHaveProperty('value', '2');
  expect(screen.getByRole('checkbox', { name: 'Stealth' })).toHaveProperty('checked', true);
  expect(screen.getByRole('checkbox', { name: 'Athletics' })).toHaveProperty('checked', false);
  const traits = screen.getByRole('list', { name: 'Traits' });
  expect(within(traits).getByText('Fixture Keen Senses')).toBeTruthy();
});

it('ticks skills as grant effects on skill.<x>', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.1'])} onChange={(r) => (latest = r)} />);
  await user.click(screen.getByRole('checkbox', { name: 'Athletics' }));
  expect(skillGrants(latest.effects)).toEqual(['skill.athletics']);
  expect(latest.effects[0]).toMatchObject({ type: 'grant', grant: 'proficiency', target: 'skill.athletics' });
});

const speedEffects = (r: ContentRevision) => r.effects.filter((e) => e.type === 'modifier' && e.target === 'speed');

it('items 12 and 24: offers a Speed (feet) field and no longer says speed is set on Stats', () => {
  render(<Harness initial={revisionOf(['srd-5.1'])} />);
  expect(screen.queryByText(/until species speed arrives/)).toBeNull();
  const speed = screen.getByRole('textbox', { name: 'Speed (feet)' });
  expect(speed).toHaveProperty('value', '');
  expect(speed.getAttribute('aria-invalid')).toBeNull();
});

it('items 12 and 24: entering 25 writes one replace modifier on speed, and clearing removes it', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.2.1'])} onChange={(r) => (latest = r)} />);
  const speed = screen.getByRole('textbox', { name: 'Speed (feet)' });
  await user.type(speed, '25');
  expect(speedEffects(latest)).toEqual([{ type: 'modifier', id: 'speed', operation: 'replace', target: 'speed', value: '25' }]);
  await user.type(speed, '5'); // 255 is out of range: nothing is written, the last good value stays
  expect(speedEffects(latest)).toEqual([{ type: 'modifier', id: 'speed', operation: 'replace', target: 'speed', value: '25' }]);
  await user.clear(speed);
  expect(speedEffects(latest)).toEqual([]);
  expect(speed.getAttribute('aria-invalid')).toBeNull();
});

// Typing is one character at a time, so "121" passes through the valid 12 and keeps it; it never stores 121.
it.each([
  ['4', 0],
  ['121', 12],
  ['2.5', 0],
  ['x', 0],
] as [string, number][])('items 12 and 24: refuses %s with visible linked text, blocks saving and stores nothing out of range', async (text, stored) => {
  const user = userEvent.setup();
  const onProblem = vi.fn();
  const lastFor = (field: string) => onProblem.mock.calls.filter((c) => c[0] === field).at(-1)?.[1];
  render(<Harness initial={revisionOf(['srd-5.1'])} onChange={(r) => (latest = r)} onProblem={onProblem} />);
  const speed = screen.getByRole('textbox', { name: 'Speed (feet)' });
  await user.type(speed, text);
  expect(speed).toHaveProperty('value', text);
  expect(speed.getAttribute('aria-invalid')).toBe('true');
  const described = (speed.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent);
  expect(described).toContain('Speed: a whole number of feet from 5 to 120, or empty for none.');
  expect(lastFor('species-speed')).toBe('Speed: a whole number of feet from 5 to 120, or empty for none.');
  expect(speedEffects(latest).map((e) => (e as { value: string }).value)).toEqual(stored === 0 ? [] : [String(stored)]);

  await user.clear(speed);
  await user.type(speed, '30');
  expect(speed.getAttribute('aria-invalid')).toBeNull();
  expect(lastFor('species-speed')).toBeUndefined();
  expect(speedEffects(latest)).toMatchObject([{ operation: 'replace', value: '30' }]);
});

it('items 12 and 24: a draft holding a speed replace of 40 shows 40 and edits it in place', () => {
  const effects: Effect[] = [
    { type: 'modifier', id: 'pace', operation: 'replace', target: 'speed', value: '40' },
    { type: 'grant', id: 's', grant: 'proficiency', target: 'skill.stealth' },
  ];
  render(<Harness initial={revisionOf(['srd-5.1'], effects)} onChange={(r) => (latest = r)} />);
  const speed = screen.getByRole('textbox', { name: 'Speed (feet)' });
  expect(speed).toHaveProperty('value', '40');
  expect(screen.queryByText(/other speed rules/)).toBeNull();
  fireEvent.change(speed, { target: { value: '35' } });
  expect(latest.effects[0]).toEqual({ type: 'modifier', id: 'pace', operation: 'replace', target: 'speed', value: '35' });
  expect(latest.effects).toHaveLength(2);
});

it('items 12 and 24: a speed bonus is left untouched, the field stays blank and a replace is added beside it', async () => {
  const user = userEvent.setup();
  const bonus: Effect = { type: 'modifier', id: 'fast', operation: 'bonus', target: 'speed', value: '10' };
  render(<Harness initial={revisionOf(['srd-5.1'], [bonus])} onChange={(r) => (latest = r)} />);
  const speed = screen.getByRole('textbox', { name: 'Speed (feet)' });
  expect(speed).toHaveProperty('value', '');
  expect(screen.getByText('This species has other speed rules; edit them in the effects list.')).toBeTruthy();
  await user.type(speed, '25');
  expect(latest.effects[0]).toBe(bonus);
  expect(speedEffects(latest)).toHaveLength(2);
  expect(latest.effects[1]).toMatchObject({ operation: 'replace', value: '25' });
});

it('review fix: an invalid speed draft is dropped, with its problem, when the field turns read-only', async () => {
  const user = userEvent.setup();
  const onProblem = vi.fn();
  const lastFor = (field: string) => onProblem.mock.calls.filter((c) => c[0] === field).at(-1)?.[1];
  render(<Harness initial={revisionOf(['srd-5.1'])} onProblem={onProblem} addReplace />);
  const speed = screen.getByRole('textbox', { name: 'Speed (feet)' });
  await user.type(speed, '4');
  expect(speed.getAttribute('aria-invalid')).toBe('true');
  expect(lastFor('species-speed')).toBeTruthy();
  const add = screen.getByRole('button', { name: 'Add another speed replace' });
  await user.click(add); // one replace of 35 is still the field's own
  expect(speed).toHaveProperty('readOnly', false);
  expect(speed.getAttribute('aria-invalid')).toBe('true');
  await user.click(add); // two replaces: not the field's
  expect(speed).toHaveProperty('readOnly', true);
  expect(speed.getAttribute('aria-invalid')).toBeNull();
  expect(speed).toHaveProperty('value', '');
  expect(lastFor('species-speed')).toBeUndefined();
});

it.each([
  ['a formula', [{ type: 'modifier', id: 'a', operation: 'replace', target: 'speed', value: 'PB + 20' }]],
  ['a stored 0', [{ type: 'modifier', id: 'a', operation: 'replace', target: 'speed', value: '0' }]],
  ['a stored 200', [{ type: 'modifier', id: 'a', operation: 'replace', target: 'speed', value: '200' }]],
  ['a stored 007', [{ type: 'modifier', id: 'a', operation: 'replace', target: 'speed', value: '007' }]],
  ['a stored 4', [{ type: 'modifier', id: 'a', operation: 'replace', target: 'speed', value: '4' }]],
  ['several replaces', [
    { type: 'modifier', id: 'a', operation: 'replace', target: 'speed', value: '25' },
    { type: 'modifier', id: 'b', operation: 'replace', target: 'speed', value: '35' },
  ]],
] as [string, Effect[]][])('items 12 and 24: %s on speed is not claimed, stays as it is, and the field cannot add another', async (_name, effects) => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.1'], effects)} onChange={(r) => (latest = r)} />);
  const speed = screen.getByRole('textbox', { name: 'Speed (feet)' });
  expect(speed).toHaveProperty('value', '');
  expect(speed).toHaveProperty('readOnly', true);
  expect(screen.getByText('This species has other speed rules; edit them in the effects list.')).toBeTruthy();
  await user.type(speed, '25');
  expect(latest.effects).toEqual(effects);
});

it('adds an existing feature as a trait grant, removes it, and offers each feature once', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.1'])} onChange={(r) => (latest = r)} />);
  const pick = screen.getByRole('combobox', { name: 'Add an existing feature as a trait' });
  await user.selectOptions(pick, 'Fixture Keen Senses');
  await user.click(screen.getByRole('button', { name: 'Add trait' }));
  expect(latest.effects).toEqual([{ type: 'grant', id: 'trait', grant: 'content', content: trait.reference }]);
  expect(within(pick).queryByRole('option', { name: /Keen Senses/ })).toBeNull();

  await user.click(screen.getByRole('button', { name: 'Remove trait Fixture Keen Senses' }));
  expect(latest.effects).toEqual([]);
});

it('refuses "Add trait" with a linked reason, keeps focus, and never uses native disabled', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.1'])} onChange={(r) => (latest = r)} />);
  const add = screen.getByRole('button', { name: 'Add trait' });
  expect(add.getAttribute('aria-disabled')).toBe('true');
  expect(add).toHaveProperty('disabled', false);
  expect(document.getElementById(add.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Choose a feature first.');
  add.focus();
  await user.click(add);
  expect(latest.effects).toEqual([]);
  expect(document.activeElement).toBe(add);
});

it('makes a new feature draft in the source and grants it', async () => {
  const user = userEvent.setup();
  const onMade = vi.fn();
  vi.mocked(client.saveDraft).mockResolvedValue({ contentId: 'new-c', revisionId: 'new-r' });
  render(<Harness initial={revisionOf(['srd-5.1', 'srd-5.2.1'])} onChange={(r) => (latest = r)} onMade={onMade} />);
  await user.type(screen.getByRole('textbox', { name: 'Trait name' }), 'Fixture Dark Sight');
  await user.type(screen.getByRole('textbox', { name: 'Trait description' }), 'Sees a little in dim light.');
  await user.click(screen.getByRole('button', { name: 'Create trait and add it' }));

  expect(client.saveDraft).toHaveBeenCalledExactlyOnceWith(
    expect.objectContaining({
      kind: 'feature',
      name: 'Fixture Dark Sight',
      summary: 'Sees a little in dim light.',
      rulesFamilies: ['srd-5.1', 'srd-5.2.1'],
      provenance: { sourceId: 'src' },
      status: 'draft',
      effects: [],
    }),
  );
  expect(latest.effects).toEqual([{ type: 'grant', id: 'trait', grant: 'content', content: { contentId: 'new-c', revisionId: 'new-r' } }]);
  expect(onMade).toHaveBeenCalledWith(expect.objectContaining({ contentId: 'new-c', kind: 'feature', draft: true }));
});

it('refuses "Create trait" until it has a name, with a linked reason', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.1'])} />);
  const create = screen.getByRole('button', { name: 'Create trait and add it' });
  expect(create.getAttribute('aria-disabled')).toBe('true');
  expect(document.getElementById(create.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Name the feature first.');
  await user.click(create);
  expect(client.saveDraft).not.toHaveBeenCalled();
});

it('item 8: accepts 1 to 9, marks anything else invalid with a linked message, blocks saving, and never clamps', async () => {
  const user = userEvent.setup();
  const onProblem = vi.fn();
  const lastFor = (field: string) => onProblem.mock.calls.filter((c) => c[0] === field).at(-1)?.[1];
  render(<Harness initial={revisionOf(['srd-5.1'])} onChange={(r) => (latest = r)} onProblem={onProblem} />);
  const strength = screen.getByRole('textbox', { name: 'Strength' });
  await user.type(strength, '7');
  expect(abilityIncreases(latest.effects)).toEqual({ str: 7 });
  expect(strength.getAttribute('aria-invalid')).toBeNull();

  await user.clear(strength);
  await user.type(strength, '12');
  expect(strength).toHaveProperty('value', '12'); // shown as typed, not clamped
  expect(strength.getAttribute('aria-invalid')).toBe('true');
  const message = document.getElementById(strength.getAttribute('aria-describedby') ?? '');
  expect(message?.textContent).toBe('Strength: an increase is a whole number from 1 to 9, or empty for none.');
  expect(lastFor('species-increase-str')).toEqual('Strength increase: a whole number from 1 to 9, or empty for none.');
  expect(abilityIncreases(latest.effects)).toEqual({ str: 1 }); // the last valid text was "1"; "12" changed nothing

  await user.clear(strength);
  await user.type(strength, '9');
  expect(strength.getAttribute('aria-invalid')).toBeNull();
  expect(lastFor('species-increase-str')).toEqual(undefined);
  expect(abilityIncreases(latest.effects)).toEqual({ str: 9 });
});

it('round 2: an invalid increase stops blocking once the fieldset is hidden by unticking the family', async () => {
  const user = userEvent.setup();
  const onProblem = vi.fn();
  const lastFor = (field: string) => onProblem.mock.calls.filter((c) => c[0] === field).at(-1)?.[1];
  render(<Harness initial={revisionOf(['srd-5.1'])} onProblem={onProblem} untick />);
  await user.type(screen.getByRole('textbox', { name: 'Strength' }), 'x');
  expect(lastFor('species-increase-str')).toMatch(/^Strength increase/);
  await user.click(screen.getByRole('button', { name: 'Untick older rules' }));
  expect(screen.queryByRole('textbox', { name: 'Strength' })).toBeNull();
  expect(lastFor('species-increase-str')).toBeUndefined();
});

it('item 8: typing a minus sign in the middle of an edit keeps the increase and marks the field', async () => {
  const user = userEvent.setup();
  const effects: Effect[] = [{ type: 'modifier', id: 'a', operation: 'bonus', target: 'ability.str.score', value: '2' }];
  render(<Harness initial={revisionOf(['srd-5.1'], effects)} onChange={(r) => (latest = r)} />);
  const strength = screen.getByRole('textbox', { name: 'Strength' });
  await user.type(strength, '-');
  expect(abilityIncreases(latest.effects)).toEqual({ str: 2 });
  expect(strength.getAttribute('aria-invalid')).toBe('true');
});

it('R62: a trait that points at a superseded draft says so, and only the per-trait button re-points it', async () => {
  const user = userEvent.setup();
  const published: ContentReference = { contentId: 'f1', revisionId: 'f1-published' };
  const effects: Effect[] = [{ type: 'grant', id: 't', grant: 'content', content: { contentId: 'f1', revisionId: 'f1-old-draft' } }];
  render(
    <Harness
      initial={revisionOf(['srd-5.1'], effects)}
      onChange={(r) => (latest = r)}
      newerPublished={(r) => (r.revisionId === 'f1-old-draft' ? published : undefined)}
    />,
  );
  expect(screen.getByText('A newer published version of Fixture Keen Senses exists.')).toBeTruthy();
  expect(latest.effects).toEqual(effects); // nothing re-pointed silently

  const use = screen.getByRole('button', { name: 'Use the published version of Fixture Keen Senses' });
  expect(use.getAttribute('aria-describedby')).toBeTruthy();
  await user.click(use);
  expect(latest.effects).toEqual([{ type: 'grant', id: 't', grant: 'content', content: published }]);
  expect(screen.queryByText(/A newer published version/)).toBeNull();
});

it('has no inline style and no second status region', () => {
  const { container } = render(<Harness initial={revisionOf(['srd-5.1'])} />);
  expect(container.querySelectorAll('[style]').length).toBe(0);
  expect(container.querySelectorAll('[role="status"]').length).toBe(0);
});
