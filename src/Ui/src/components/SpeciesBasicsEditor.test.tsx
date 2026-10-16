// @vitest-environment jsdom
// Item 12: the species editor. Ability increases follow the family's policy (never its name); traits are grants of features.
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, ContentRevision, Effect, RulesFamilyId, SourceRecord } from '../api/types';
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
function Harness(props: { initial: ContentRevision; known?: Known[]; onChange?: (r: ContentRevision) => void; onMade?: (k: Known) => void; onProblem?: () => void }) {
  const [revision, setRevision] = useState(props.initial);
  props.onChange?.(revision);
  return (
    <SpeciesBasicsEditor
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
  expect(within(group).getByRole('spinbutton', { name: 'Strength' })).toBeTruthy();
  expect(within(group).getByRole('spinbutton', { name: 'Charisma' })).toBeTruthy();
  expect(screen.queryByText(/ability increases come from the background/)).toBeNull();

  await user.type(within(group).getByRole('spinbutton', { name: 'Strength' }), '2');
  await user.type(within(group).getByRole('spinbutton', { name: 'Charisma' }), '1');
  expect(abilityIncreases(latest.effects)).toEqual({ str: 2, cha: 1 });
  expect(latest.effects[0]).toMatchObject({ type: 'modifier', operation: 'bonus', target: 'ability.str.score', value: '2' });

  await user.clear(within(group).getByRole('spinbutton', { name: 'Strength' }));
  expect(abilityIncreases(latest.effects)).toEqual({ cha: 1 });
});

it('newer rules: no increase fields, and it says where the increases come from', () => {
  render(<Harness initial={revisionOf(['srd-5.2.1'])} />);
  expect(screen.queryByRole('spinbutton', { name: 'Strength' })).toBeNull();
  expect(screen.getByText('In Fixture Newer Rules, ability increases come from the background.')).toBeTruthy();
});

it('a species for both families shows both behaviours, each labelled by the family name', () => {
  render(<Harness initial={revisionOf(['srd-5.1', 'srd-5.2.1'])} />);
  const group = screen.getByRole('group', { name: 'Ability score increases (Fixture Older Rules)' });
  expect(within(group).getByRole('spinbutton', { name: 'Wisdom' })).toBeTruthy();
  expect(screen.getByText('In Fixture Newer Rules, ability increases come from the background.')).toBeTruthy();
});

it('keeps showing increases that exist when no ticked family uses them, and says they are ignored', () => {
  const increase: Effect = { type: 'modifier', id: 'a', operation: 'bonus', target: 'ability.dex.score', value: '2' };
  render(<Harness initial={revisionOf(['srd-5.2.1'], [increase])} />);
  expect(screen.getByRole('spinbutton', { name: 'Dexterity' })).toHaveProperty('value', '2');
  expect(screen.getByText(/ignored in Fixture Newer Rules/)).toBeTruthy();
});

it('reads an existing species back: values, skills and traits by name', () => {
  const effects: Effect[] = [
    { type: 'modifier', id: 'a', operation: 'bonus', target: 'ability.con.score', value: '2' },
    { type: 'grant', id: 's', grant: 'proficiency', target: 'skill.stealth' },
    { type: 'grant', id: 't', grant: 'content', content: trait.reference },
  ];
  render(<Harness initial={revisionOf(['srd-5.1'], effects)} />);
  expect(screen.getByRole('spinbutton', { name: 'Constitution' })).toHaveProperty('value', '2');
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

it('says speed is set on Stats, and offers no speed field', () => {
  render(<Harness initial={revisionOf(['srd-5.1'])} />);
  expect(screen.getByText("Speed: set it on the character's Stats until species speed arrives.")).toBeTruthy();
  expect(screen.queryByRole('spinbutton', { name: /speed/i })).toBeNull();
  expect(screen.queryByRole('textbox', { name: /speed/i })).toBeNull();
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

it('has no inline style and no second status region', () => {
  const { container } = render(<Harness initial={revisionOf(['srd-5.1'])} />);
  expect(container.querySelectorAll('[style]').length).toBe(0);
  expect(container.querySelectorAll('[role="status"]').length).toBe(0);
});
