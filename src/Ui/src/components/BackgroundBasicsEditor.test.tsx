// @vitest-environment jsdom
// Item 12: the background editor. The feature or feat and the ability increases follow the family's policy (never its name);
// the 2024 increases are made the way the bundled 5.2.1 backgrounds make them (R48).
import { act, cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect, useState } from 'react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { AppInfo, ContentReference, ContentRevision, Effect, RulesFamilyId, SourceRecord } from '../api/types';
import { increaseChoice, increaseOptions, isIncreaseChoice, skillGrants, type Known } from '../entryBasics';
import { BackgroundBasicsEditor } from './BackgroundBasicsEditor';
import type { BeforeSave } from './OriginPieces';

vi.mock('../api/client', () => ({ client: { saveDraft: vi.fn() } }));

const info = {
  rulesFamilies: [
    { id: 'srd-5.1', displayName: 'Fixture Older Rules', abilityIncreaseSource: 'species', backgroundGrantsFeat: false },
    { id: 'srd-5.2.1', displayName: 'Fixture Newer Rules', abilityIncreaseSource: 'background', backgroundGrantsFeat: true },
  ],
  fields: [
    { id: 'skill.athletics', label: 'Athletics' },
    { id: 'skill.stealth', label: 'Stealth' },
  ],
} as unknown as AppInfo;
const source = { id: 'src', title: 'Fixture Book', rulesFamilies: ['srd-5.1', 'srd-5.2.1'], editionVersion: 'homebrew' } as SourceRecord;
const known = (id: string, name: string, kind: Known['kind'], extra: Partial<Known> = {}): Known => ({
  contentId: id,
  name,
  kind,
  reference: { contentId: id, revisionId: `${id}r` },
  draft: false,
  own: true,
  ...extra,
});
const feature = known('f1', 'Fixture Shelter', 'feature');
const ownFeat = known('t1', 'Fixture Boon', 'feat');
const installedFeat = known('t2', 'Fixture Installed Boon', 'feat', { own: false, pickable: true });
const oldFeat = known('t3', 'Fixture Old Boon', 'feat', { own: false, pickable: false });

function revisionOf(families: RulesFamilyId[], effects: Effect[] = [], name = 'Fixture Wanderer'): ContentRevision {
  return { contentId: 'bg', revisionId: 'x', kind: 'background', name, rulesFamilies: families, provenance: { sourceId: 'src' }, status: 'draft', effects };
}

let latest: ContentRevision;
const hook: { current: BeforeSave | null } = { current: null };
let madeSoFar: Known[];
const register = (step: BeforeSave | null) => {
  hook.current = step;
};

function Harness(props: { initial: ContentRevision; known?: Known[]; revisions?: ContentRevision[]; onProblem?: (field: string, problem: string | undefined) => void }) {
  const [revision, setRevision] = useState(props.initial);
  const [made, setMade] = useState<Known[]>([]);
  useEffect(() => {
    latest = revision;
    madeSoFar = made;
  });
  const all = [...(props.known ?? [feature, ownFeat, installedFeat, oldFeat]), ...made];
  return (
    <BackgroundBasicsEditor
      revision={revision}
      source={source}
      info={info}
      known={all}
      revisionOf={(r: ContentReference) => props.revisions?.find((x) => x.revisionId === r.revisionId)}
      onEffects={(update) => setRevision((r) => ({ ...r, effects: update(r.effects) }))}
      onBusy={() => {}}
      onMade={(k) => setMade((m) => [...m, k])}
      onProblem={props.onProblem ?? (() => {})}
      onError={() => {}}
      registerBeforeSave={register}
    />
  );
}

beforeEach(() => {
  let n = 0;
  vi.mocked(client.saveDraft).mockReset();
  vi.mocked(client.saveDraft).mockImplementation(() => Promise.resolve({ contentId: `opt-c${++n}`, revisionId: `opt-r${n}` }));
});
afterEach(cleanup);

it('older rules: a feature picker, no feat picker, no increase fields, and notes saying why', () => {
  render(<Harness initial={revisionOf(['srd-5.1'])} />);
  expect(screen.getByRole('combobox', { name: 'Feature granted by the background' })).toBeTruthy();
  expect(screen.queryByRole('combobox', { name: 'Feat granted by the background' })).toBeNull();
  expect(screen.queryByRole('group', { name: /Ability score increases/ })).toBeNull();
  expect(screen.getByText('In Fixture Older Rules, ability increases come from the species.')).toBeTruthy();
  expect(screen.getByText('In Fixture Older Rules, a background cannot grant a feat.')).toBeTruthy();
});

it('newer rules: a feat picker and the three-ability increases, no feature picker', () => {
  render(<Harness initial={revisionOf(['srd-5.2.1'])} />);
  expect(screen.getByRole('combobox', { name: 'Feat granted by the background' })).toBeTruthy();
  expect(screen.queryByRole('combobox', { name: 'Feature granted by the background' })).toBeNull();
  const group = screen.getByRole('group', { name: 'Ability score increases (Fixture Newer Rules)' });
  expect(within(group).getAllByRole('checkbox')).toHaveLength(6);
});

it('both families: both pickers and the increases, each labelled by the family name', () => {
  render(<Harness initial={revisionOf(['srd-5.1', 'srd-5.2.1'])} />);
  expect(screen.getByRole('group', { name: 'Feature (Fixture Older Rules)' })).toBeTruthy();
  expect(screen.getByRole('group', { name: 'Feat (Fixture Newer Rules)' })).toBeTruthy();
  expect(screen.getByRole('group', { name: 'Ability score increases (Fixture Newer Rules)' })).toBeTruthy();
});

it('ticks skills as grant effects and picks a feature as a grant of that feature', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.1'])} />);
  await user.click(screen.getByRole('checkbox', { name: 'Stealth' }));
  expect(skillGrants(latest.effects)).toEqual(['skill.stealth']);

  await user.selectOptions(screen.getByRole('combobox', { name: 'Feature granted by the background' }), 'Fixture Shelter');
  expect(latest.effects.at(-1)).toEqual({ type: 'grant', id: 'feature', grant: 'content', content: feature.reference });

  await user.selectOptions(screen.getByRole('combobox', { name: 'Feature granted by the background' }), 'None');
  expect(latest.effects).toHaveLength(1);
});

it('offers feats from this source and published installed feats, not old or incompatible ones', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.2.1'])} />);
  const pick = screen.getByRole('combobox', { name: 'Feat granted by the background' });
  expect(within(pick).getAllByRole('option').map((o) => o.textContent)).toEqual(['None', 'Fixture Boon', 'Fixture Installed Boon']);
  await user.selectOptions(pick, 'Fixture Installed Boon');
  expect(latest.effects).toEqual([{ type: 'grant', id: 'feat', grant: 'content', content: installedFeat.reference }]);
  expect((pick as HTMLSelectElement).value).toBe('t2');
});

it('reads back a feature and a feat grant by what they are, and keeps a grant of something else in the rule list', () => {
  const other = known('o1', 'Fixture Other', 'item');
  const effects: Effect[] = [
    { type: 'grant', id: 'a', grant: 'content', content: feature.reference },
    { type: 'grant', id: 'b', grant: 'content', content: ownFeat.reference },
    { type: 'grant', id: 'c', grant: 'content', content: other.reference },
  ];
  render(<Harness initial={revisionOf(['srd-5.1', 'srd-5.2.1'], effects)} known={[feature, ownFeat, other]} />);
  expect((screen.getByRole('combobox', { name: 'Feature granted by the background' }) as HTMLSelectElement).value).toBe('f1');
  expect((screen.getByRole('combobox', { name: 'Feat granted by the background' }) as HTMLSelectElement).value).toBe('t1');
});

it('makes a new feature draft and grants it', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.1'])} />);
  await user.type(screen.getByRole('textbox', { name: 'Feature name' }), 'Fixture Roof Overhead');
  await user.click(screen.getByRole('button', { name: 'Create feature and add it' }));
  expect(client.saveDraft).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ kind: 'feature', name: 'Fixture Roof Overhead', rulesFamilies: ['srd-5.1'] }));
  expect(latest.effects).toEqual([{ type: 'grant', id: 'feature', grant: 'content', content: { contentId: 'opt-c1', revisionId: 'opt-r1' } }]);
  expect(madeSoFar.map((m) => m.name)).toEqual(['Fixture Roof Overhead']);
});

async function tick(user: ReturnType<typeof userEvent.setup>, ...names: string[]) {
  for (const name of names) await user.click(screen.getByRole('checkbox', { name }));
}

it('asks for exactly three abilities: another count is an error linked to the boxes, and blocks saving', async () => {
  const user = userEvent.setup();
  const onProblem = vi.fn();
  render(<Harness initial={revisionOf(['srd-5.2.1'])} onProblem={onProblem} />);
  await tick(user, 'Strength', 'Dexterity');
  const strength = screen.getByRole('checkbox', { name: 'Strength' });
  expect(strength.getAttribute('aria-invalid')).toBe('true');
  const described = (strength.getAttribute('aria-describedby') ?? '').split(' ').map((id) => document.getElementById(id)?.textContent);
  expect(described).toContain('Pick exactly three abilities, or none. Two are ticked.');
  expect(onProblem).toHaveBeenLastCalledWith('background-increases', 'Ability score increases: pick exactly three abilities, or none (two are ticked).');

  await tick(user, 'Constitution');
  expect(screen.getByRole('checkbox', { name: 'Strength' }).getAttribute('aria-invalid')).toBeNull();
  expect(onProblem).toHaveBeenLastCalledWith('background-increases', undefined);
});

it('on save makes seven option features in the source, then the choice pinning their draft references', async () => {
  const user = userEvent.setup();
  render(<Harness initial={revisionOf(['srd-5.2.1', 'srd-5.1'])} />);
  await tick(user, 'Constitution', 'Strength', 'Dexterity');
  expect(client.saveDraft).not.toHaveBeenCalled(); // picking writes nothing yet

  let result!: Awaited<ReturnType<BeforeSave>>;
  await act(async () => {
    result = await hook.current!(latest);
  });
  const names = increaseOptions('Fixture Wanderer', ['str', 'dex', 'con']).map((o) => o.name);
  expect(vi.mocked(client.saveDraft).mock.calls.map((c) => c[0].name)).toEqual(names);
  expect(vi.mocked(client.saveDraft).mock.calls[0]![0]).toMatchObject({
    kind: 'feature',
    rulesFamilies: ['srd-5.2.1', 'srd-5.1'],
    provenance: { sourceId: 'src' },
    status: 'draft',
    effects: increaseOptions('Fixture Wanderer', ['str', 'dex', 'con'])[0]!.effects,
  });
  const choice = result.revision.effects.find(isIncreaseChoice)!;
  expect(choice).toMatchObject({ choiceId: 'fixture-wanderer-ability-scores', count: 1 });
  expect(choice.options.map((o) => o.revisionId)).toEqual(['opt-r1', 'opt-r2', 'opt-r3', 'opt-r4', 'opt-r5', 'opt-r6', 'opt-r7']);
  expect(result.drafts).toHaveLength(7);
  expect(madeSoFar).toHaveLength(7);
});

it('reuses an option feature the source already has, and makes none when nothing changed', async () => {
  const user = userEvent.setup();
  const wanted = increaseOptions('Fixture Wanderer', ['str', 'dex', 'con']);
  const existing = known('e1', wanted[0]!.name, 'feature');
  const existingRevision: ContentRevision = {
    contentId: 'e1', revisionId: 'e1r', kind: 'feature', name: wanted[0]!.name, rulesFamilies: ['srd-5.2.1'], provenance: { sourceId: 'src' }, status: 'published', effects: wanted[0]!.effects,
  };
  render(<Harness initial={revisionOf(['srd-5.2.1'])} known={[existing]} revisions={[existingRevision]} />);
  await tick(user, 'Strength', 'Dexterity', 'Constitution');
  let result!: Awaited<ReturnType<BeforeSave>>;
  await act(async () => {
    result = await hook.current!(latest);
  });
  expect(client.saveDraft).toHaveBeenCalledTimes(6);
  expect(result.revision.effects.find(isIncreaseChoice)!.options[0]).toEqual(existing.reference);
  expect(result.drafts).toHaveLength(6); // the reused one is published: not a draft this save pins

  // The same picks again: the choice is already there and points at readable options, so nothing is saved.
  vi.mocked(client.saveDraft).mockClear();
  await act(async () => {
    const again = await hook.current!(result.revision);
    expect(again.revision).toBe(result.revision);
    expect(again.drafts).toEqual([]);
  });
  expect(client.saveDraft).not.toHaveBeenCalled();
});

it('reads the three abilities back from the options of an existing choice', () => {
  const options = increaseOptions('Fixture Wanderer', ['wis', 'int', 'cha']);
  const revisions: ContentRevision[] = options.map((o, i) => ({
    contentId: `c${i}`, revisionId: `r${i}`, kind: 'feature', name: o.name, rulesFamilies: ['srd-5.2.1'], provenance: { sourceId: 'src' }, status: 'published', effects: o.effects,
  }));
  const choice = increaseChoice('Fixture Wanderer', revisions.map((r) => ({ contentId: r.contentId, revisionId: r.revisionId })));
  render(<Harness initial={revisionOf(['srd-5.2.1'], [choice])} revisions={revisions} />);
  expect(screen.getByRole('checkbox', { name: 'Intelligence' })).toHaveProperty('checked', true);
  expect(screen.getByRole('checkbox', { name: 'Wisdom' })).toHaveProperty('checked', true);
  expect(screen.getByRole('checkbox', { name: 'Charisma' })).toHaveProperty('checked', true);
  expect(screen.getByRole('checkbox', { name: 'Strength' })).toHaveProperty('checked', false);
});

it('unticking all three removes the choice on save; an untouched unreadable choice is left alone', async () => {
  const user = userEvent.setup();
  const options = increaseOptions('Fixture Wanderer', ['str', 'dex', 'con']);
  const revisions: ContentRevision[] = options.map((o, i) => ({
    contentId: `c${i}`, revisionId: `r${i}`, kind: 'feature', name: o.name, rulesFamilies: ['srd-5.2.1'], provenance: { sourceId: 'src' }, status: 'published', effects: o.effects,
  }));
  const choice = increaseChoice('Fixture Wanderer', revisions.map((r) => ({ contentId: r.contentId, revisionId: r.revisionId })));
  const view = render(<Harness initial={revisionOf(['srd-5.2.1'], [choice])} revisions={revisions} />);
  await tick(user, 'Strength', 'Dexterity', 'Constitution');
  let result!: Awaited<ReturnType<BeforeSave>>;
  await act(async () => {
    result = await hook.current!(latest);
  });
  expect(result.revision.effects.filter(isIncreaseChoice)).toEqual([]);
  expect(client.saveDraft).not.toHaveBeenCalled();
  view.unmount();

  render(<Harness initial={revisionOf(['srd-5.2.1'], [choice])} revisions={[]} />);
  await act(async () => {
    result = await hook.current!(latest);
  });
  expect(result.revision.effects.find(isIncreaseChoice)).toEqual(choice);
});

it('renaming the background remakes the options under the new name', async () => {
  const options = increaseOptions('Fixture Wanderer', ['str', 'dex', 'con']);
  const revisions: ContentRevision[] = options.map((o, i) => ({
    contentId: `c${i}`, revisionId: `r${i}`, kind: 'feature', name: o.name, rulesFamilies: ['srd-5.2.1'], provenance: { sourceId: 'src' }, status: 'published', effects: o.effects,
  }));
  const choice = increaseChoice('Fixture Wanderer', revisions.map((r) => ({ contentId: r.contentId, revisionId: r.revisionId })));
  render(<Harness initial={revisionOf(['srd-5.2.1'], [choice], 'Fixture Traveller')} revisions={revisions} />);
  let result!: Awaited<ReturnType<BeforeSave>>;
  await act(async () => {
    result = await hook.current!(latest);
  });
  expect(vi.mocked(client.saveDraft).mock.calls[0]![0].name).toBe('Fixture Traveller Ability Scores: Strength +2, Dexterity +1');
  expect(result.revision.effects.find(isIncreaseChoice)!.choiceId).toBe('fixture-traveller-ability-scores');
});

it('older rules only: saving leaves the effects alone and makes nothing', async () => {
  render(<Harness initial={revisionOf(['srd-5.1'])} />);
  let result!: Awaited<ReturnType<BeforeSave>>;
  await act(async () => {
    result = await hook.current!(latest);
  });
  expect(result.revision).toBe(latest);
  expect(client.saveDraft).not.toHaveBeenCalled();
});

it('needs a name before it can make the options', async () => {
  const user = userEvent.setup();
  const onProblem = vi.fn();
  render(<Harness initial={revisionOf(['srd-5.2.1'], [], '  ')} onProblem={onProblem} />);
  await tick(user, 'Strength', 'Dexterity', 'Constitution');
  expect(onProblem).toHaveBeenLastCalledWith('background-increases', 'Ability score increases: name the background first, the options are named after it.');
});

it('has no inline style and no second status region', () => {
  const { container } = render(<Harness initial={revisionOf(['srd-5.1', 'srd-5.2.1'])} />);
  expect(container.querySelectorAll('[style]').length).toBe(0);
  expect(container.querySelectorAll('[role="status"]').length).toBe(0);
});
