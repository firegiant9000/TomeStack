import { describe, expect, it } from 'vitest';
import type { ContentKind, ContentOption, Effect, StudioEntry } from './api/types';
import {
  abilityIncreases,
  addContentGrant,
  grantOfKind,
  increaseChoice,
  increaseChoiceId,
  increaseOptions,
  isIncreaseChoice,
  keysOfOptions,
  knownContent,
  pinnedDrafts,
  removeContentGrant,
  setAbilityIncrease,
  setIncreaseChoice,
  setKindGrant,
  setSpell,
  skillGrants,
  spellOf,
  toggleSkill,
} from './entryBasics';

const ref = (n: number) => ({ contentId: `c${n}`, revisionId: `r${n}` });

describe('species ability increases', () => {
  it('writes one bonus modifier per ability, keeps the others, and reads them back', () => {
    const other: Effect = { type: 'resource', id: 'pool', resourceId: 'pool', label: 'Fixture pool', maximum: '1' };
    let effects = setAbilityIncrease([other], 'str', 2);
    effects = setAbilityIncrease(effects, 'cha', 1);
    expect(effects).toHaveLength(3);
    expect(effects[1]).toMatchObject({ type: 'modifier', operation: 'bonus', target: 'ability.str.score', value: '2' });
    expect(abilityIncreases(effects)).toEqual({ str: 2, cha: 1 });
    expect(effects[0]).toBe(other);
  });

  it('changes an increase in place and removes it when cleared', () => {
    let effects = setAbilityIncrease([], 'dex', 1);
    effects = setAbilityIncrease(effects, 'dex', 2);
    expect(effects).toHaveLength(1);
    expect(abilityIncreases(effects)).toEqual({ dex: 2 });
    expect(setAbilityIncrease(effects, 'dex', undefined)).toEqual([]);
    expect(setAbilityIncrease(effects, 'dex', 0)).toEqual([]);
  });

  it('leaves a formula, a toggled or a non-bonus modifier in the rule list', () => {
    const formula: Effect = { type: 'modifier', id: 'f', operation: 'bonus', target: 'ability.con.score', value: 'PB' };
    const set: Effect = { type: 'modifier', id: 's', operation: 'set', target: 'ability.wis.score', value: '2' };
    const toggled: Effect = { type: 'modifier', id: 't', operation: 'bonus', target: 'ability.int.score', value: '2', toggle: 'rage' };
    expect(abilityIncreases([formula, set, toggled])).toEqual({});
  });
});

describe('skill proficiencies', () => {
  it('toggles a grant on skill.<x> and reads the list back', () => {
    let effects = toggleSkill([], 'skill.athletics');
    effects = toggleSkill(effects, 'skill.stealth');
    expect(skillGrants(effects)).toEqual(['skill.athletics', 'skill.stealth']);
    expect(effects[0]).toMatchObject({ type: 'grant', grant: 'proficiency', target: 'skill.athletics' });
    effects = toggleSkill(effects, 'skill.athletics');
    expect(skillGrants(effects)).toEqual(['skill.stealth']);
  });

  it('does not show a class-only or levelled grant as the entry proficiency', () => {
    const levelled: Effect = { type: 'grant', id: 'l', grant: 'proficiency', target: 'skill.arcana', level: 3 };
    const expertise: Effect = { type: 'grant', id: 'e', grant: 'expertise', target: 'skill.arcana' };
    expect(skillGrants([levelled, expertise])).toEqual([]);
  });
});

describe('content grants', () => {
  it('adds a grant once per content and removes it by content', () => {
    let effects = addContentGrant([], ref(1), 'trait');
    effects = addContentGrant(effects, ref(1), 'trait');
    effects = addContentGrant(effects, ref(2), 'trait');
    expect(effects.map((e) => e.id)).toEqual(['trait', 'trait-2']);
    expect(removeContentGrant(effects, 'c1').map((e) => e.id)).toEqual(['trait-2']);
  });

  const kinds: Record<string, ContentKind> = { c1: 'feature', c2: 'feat' };
  const kindOf = (r: { contentId: string }) => kinds[r.contentId];

  it('finds and replaces the grant of one kind without touching the other', () => {
    let effects = setKindGrant([], kindOf, 'feature', ref(1), 'feature');
    effects = setKindGrant(effects, kindOf, 'feat', ref(2), 'feat');
    expect(grantOfKind(effects, kindOf, 'feature')?.content).toEqual(ref(1));
    expect(grantOfKind(effects, kindOf, 'feat')?.content).toEqual(ref(2));
    effects = setKindGrant(effects, kindOf, 'feature', undefined, 'feature');
    expect(effects).toHaveLength(1);
    expect(grantOfKind(effects, kindOf, 'feat')?.content).toEqual(ref(2));
  });
});

describe('background ability increase options (mirrors the bundled 5.2.1 backgrounds)', () => {
  it('makes seven features for three abilities: each ordered pair at +2/+1, then +1 each', () => {
    const options = increaseOptions('Fixture Scout', ['str', 'dex', 'con']);
    expect(options.map((o) => o.name)).toEqual([
      'Fixture Scout Ability Scores: Strength +2, Dexterity +1',
      'Fixture Scout Ability Scores: Strength +2, Constitution +1',
      'Fixture Scout Ability Scores: Dexterity +2, Strength +1',
      'Fixture Scout Ability Scores: Dexterity +2, Constitution +1',
      'Fixture Scout Ability Scores: Constitution +2, Strength +1',
      'Fixture Scout Ability Scores: Constitution +2, Dexterity +1',
      'Fixture Scout Ability Scores: Strength +1, Dexterity +1, Constitution +1',
    ]);
    expect(options[0]!.effects).toEqual([
      { type: 'modifier', id: 'str', operation: 'bonus', target: 'ability.str.score', value: '2' },
      { type: 'modifier', id: 'dex', operation: 'bonus', target: 'ability.dex.score', value: '1' },
    ]);
    expect(options[6]!.effects.map((e) => (e as { value: string }).value)).toEqual(['1', '1', '1']);
  });

  it('puts the abilities in sheet order whatever order they were picked in, and needs exactly three', () => {
    expect(increaseOptions('Fixture Scout', ['con', 'str', 'dex'])[0]!.name).toBe('Fixture Scout Ability Scores: Strength +2, Dexterity +1');
    expect(increaseOptions('Fixture Scout', ['str', 'dex'])).toEqual([]);
    expect(increaseOptions('Fixture Scout', ['str', 'dex', 'con', 'int'])).toEqual([]);
  });

  it('names the choice after the background and reads the abilities back from the options', () => {
    expect(increaseChoiceId('Fixture Scout')).toBe('fixture-scout-ability-scores');
    expect(increaseChoiceId('  ')).toBe('background-ability-scores');
    const options = increaseOptions('Fixture Scout', ['wis', 'int', 'cha']);
    expect(keysOfOptions(options.map((o) => o.effects))).toEqual(['int', 'wis', 'cha']);
    const choice = increaseChoice('Fixture Scout', [ref(1), ref(2)]);
    expect(choice).toMatchObject({ type: 'choice', id: 'fixture-scout-ability-scores', choiceId: 'fixture-scout-ability-scores', count: 1, options: [ref(1), ref(2)] });
    expect(isIncreaseChoice(choice)).toBe(true);
    expect(isIncreaseChoice({ type: 'choice', id: 'x', choiceId: 'skills', count: 1, options: [] })).toBe(false);
  });

  it('keeps a stored replaceable and other keys when the choice is rebuilt (MECH-X item 2, S2)', () => {
    const stored = { ...increaseChoice('Fixture Scout', [ref(1)]), replaceable: { when: 'levelUp' as const, max: 1 } };
    const rebuilt = increaseChoice('Fixture Scout', [ref(2)], stored.choiceId, stored);
    expect(rebuilt).toMatchObject({ options: [ref(2)], replaceable: { when: 'levelUp', max: 1 } });
    expect(increaseChoice('Fixture Scout', [ref(2)]).replaceable).toBeUndefined();
  });

  it('replaces the choice in place and removes it', () => {
    const other: Effect = { type: 'grant', id: 'g', grant: 'proficiency', target: 'skill.arcana' };
    let effects = setIncreaseChoice([other], increaseChoice('Fixture Scout', [ref(1)]));
    effects = setIncreaseChoice(effects, increaseChoice('Fixture Scout', [ref(2)]));
    expect(effects).toHaveLength(2);
    expect(effects.filter(isIncreaseChoice)[0]!.options).toEqual([ref(2)]);
    expect(setIncreaseChoice(effects, null)).toEqual([other]);
  });
});

describe('what the editors can pick and what is still a draft', () => {
  const rev = (contentId: string, revisionId: string, status: 'draft' | 'published') =>
    ({ contentId, revisionId, status, kind: 'feature', name: contentId, effects: [] }) as unknown as StudioEntry['latest'];
  const entry = (contentId: string, name: string, kind: ContentKind, revisions: StudioEntry['latest'][]): StudioEntry => ({
    contentId,
    name,
    kind,
    revisions,
    latest: revisions.at(-1)!,
    latestPublished: revisions.filter((r) => r.status === 'published').at(-1),
  });

  it('lists the source entries first (pointing at the newest published revision, else the draft), then made ones, then installed', () => {
    const entries = [
      entry('a', 'Fixture Alpha', 'feature', [rev('a', 'a1', 'published'), rev('a', 'a2', 'draft')]),
      entry('b', 'Fixture Beta', 'feat', [rev('b', 'b1', 'draft')]),
    ];
    const installed = [
      { reference: { contentId: 'a', revisionId: 'a1' }, kind: 'feature', name: 'Fixture Alpha' },
      { reference: { contentId: 'z', revisionId: 'z1' }, kind: 'feat', name: 'Fixture Zed' },
    ] as ContentOption[];
    const made = [{ contentId: 'm', name: 'Fixture Made', kind: 'feature' as const, reference: { contentId: 'm', revisionId: 'm1' }, draft: true, own: true }];
    const known = knownContent(entries, installed, made);
    expect(known.map((k) => [k.contentId, k.reference.revisionId, k.draft, k.own])).toEqual([
      ['a', 'a1', false, true],
      ['b', 'b1', true, true],
      ['m', 'm1', true, true],
      ['z', 'z1', false, false],
    ]);
  });

  it('finds the drafts a grant or a choice option points at', () => {
    const drafts = new Set(['d1', 'd2']);
    const effects: Effect[] = [
      { type: 'grant', id: 'g', grant: 'content', content: { contentId: 'x', revisionId: 'd1' } },
      { type: 'grant', id: 'h', grant: 'content', content: { contentId: 'y', revisionId: 'p1' } },
      { type: 'choice', id: 'c', choiceId: 'c', count: 1, options: [{ contentId: 'w', revisionId: 'd2' }, { contentId: 'v', revisionId: 'p2' }] },
    ];
    expect(pinnedDrafts(effects, drafts).map((r) => r.revisionId)).toEqual(['d1', 'd2']);
    expect(pinnedDrafts(effects, new Set())).toEqual([]);
  });
});

describe('spell data', () => {
  it('creates the spell effect with defaults, then changes only the fields given', () => {
    const effects = setSpell([], { level: 3, school: 'evocation', lists: ['fixture-list'] });
    expect(effects).toHaveLength(1);
    expect(spellOf(effects)).toMatchObject({ type: 'spell', id: 'spell', level: 3, school: 'evocation', lists: ['fixture-list'], concentration: false, ritual: false });
    const next = setSpell(effects, { ritual: true, castingTime: '1 action' });
    expect(spellOf(next)).toMatchObject({ level: 3, ritual: true, castingTime: '1 action', lists: ['fixture-list'] });
  });

  it('keeps fields the form does not show, and drops an empty text field instead of saving an empty string', () => {
    const existing: Effect = { type: 'spell', id: 'spell', level: 1, attack: 'ranged', dice: '2d6', save: 'dex', range: '30 feet' };
    const next = setSpell([existing], { range: '  ' });
    expect(spellOf(next)).toEqual({ type: 'spell', id: 'spell', level: 1, attack: 'ranged', dice: '2d6', save: 'dex' });
  });
});
