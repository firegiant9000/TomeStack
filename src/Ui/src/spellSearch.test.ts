import { expect, it } from 'vitest';
import type { SpellEntry } from './api/types';
import { castingBucket, filterSpells, foldName, matchesSpell, type CastingBucket, type SpellFilter } from './spellSearch';

it('matches case- and accent-insensitively, both ways', () => {
  expect(matchesSpell('Fixture Frost Ring', 'FROST')).toBe(true);
  expect(matchesSpell('Fixture Émber Wave', 'ember')).toBe(true);
  expect(matchesSpell('Fixture Ember Wave', 'émbér')).toBe(true);
  expect(matchesSpell('Fixture Spark', 'frost')).toBe(false);
});

it('a blank or marks-only query matches everything (Review Focus 4)', () => {
  expect(matchesSpell('Fixture Spark', '')).toBe(true);
  expect(matchesSpell('Fixture Spark', '   ')).toBe(true);
  expect(matchesSpell('Fixture Spark', '\u0301\u0308')).toBe(true);
  expect(foldName('  Émber ')).toBe('ember');
});

const buckets: [string | undefined, CastingBucket][] = [
  ['1 action', 'action'],
  ['Action', 'action'],
  ['Action or Ritual', 'action'],
  ['1 action or 8 hours', 'action'],
  ['1 bonus action', 'bonusAction'],
  ['Bonus Action', 'bonusAction'],
  ['Bonus Action, which you take when something happens', 'bonusAction'],
  ['1 reaction, which you take when something happens', 'reaction'],
  ['Reaction, which you take when something happens', 'reaction'],
  ['1 minute', 'longer'],
  ['1 minute or Ritual', 'longer'],
  ['10 minutes', 'longer'],
  ['', 'longer'],
  [undefined, 'longer'],
];

it.each(buckets)('castingBucket(%j) is %s', (castingTime, bucket) => {
  expect(castingBucket(castingTime)).toBe(bucket);
});

const orig = { kind: 'content', rulesFamily: 'srd-5.2.1', sourceTitle: 'Fixture Pack' } as SpellEntry['origin'];
const sp = (name: string, level: number, extra: Partial<SpellEntry> = {}): SpellEntry => ({
  spell: { contentId: name, revisionId: name }, name, level, prepared: true, concentration: false, ritual: false, attack: 'none', origin: orig, diagnostics: [], ...extra,
});
const spells: SpellEntry[] = [
  sp('Alder Spark', 0, { school: 'Evocation', castingTime: 'Action' }),
  sp('Birch Veil', 1, { school: 'illusion', castingTime: '1 bonus action', concentration: true }),
  sp('Cedar Ward', 1, { school: 'evocation', castingTime: '1 reaction, which you take when hit', ritual: true }),
  sp('Dune Rite', 2, { school: 'Divination', castingTime: '1 minute or Ritual', ritual: true, concentration: true }),
  sp('Elm Mystery', 3),
];
const none: SpellFilter = { text: '', levels: [], schools: [], casting: [] };
const names = (f: Partial<SpellFilter>) => filterSpells(spells, { ...none, ...f }).map((s) => s.name);

it('an empty filter keeps every spell, in order', () => {
  expect(names({})).toEqual(spells.map((s) => s.name));
});

it('filters by text, level, school, casting time, concentration and ritual, each alone', () => {
  expect(names({ text: 'VEIL' })).toEqual(['Birch Veil']);
  expect(names({ levels: [0, 2] })).toEqual(['Alder Spark', 'Dune Rite']);
  expect(names({ schools: ['Illusion'] })).toEqual(['Birch Veil']);
  expect(names({ casting: ['bonusAction', 'reaction'] })).toEqual(['Birch Veil', 'Cedar Ward']);
  expect(names({ casting: ['longer'] })).toEqual(['Dune Rite', 'Elm Mystery']);
  expect(names({ concentration: true })).toEqual(['Birch Veil', 'Dune Rite']);
  expect(names({ concentration: false })).toEqual(['Alder Spark', 'Cedar Ward', 'Elm Mystery']);
  expect(names({ ritual: true })).toEqual(['Cedar Ward', 'Dune Rite']);
  expect(names({ ritual: false })).toEqual(['Alder Spark', 'Birch Veil', 'Elm Mystery']);
});

it('combines the dimensions with AND', () => {
  expect(names({ levels: [1], ritual: true })).toEqual(['Cedar Ward']);
  expect(names({ levels: [1, 2], concentration: true, casting: ['longer'] })).toEqual(['Dune Rite']);
  expect(names({ text: 'e', schools: ['evocation'], levels: [0] })).toEqual(['Alder Spark']);
  expect(names({ levels: [9] })).toEqual([]);
});

it('treats Evocation and evocation as one school', () => {
  expect(names({ schools: ['EVOCATION'] })).toEqual(['Alder Spark', 'Cedar Ward']);
  expect(names({ schools: ['evocation', 'Divination'] })).toEqual(['Alder Spark', 'Cedar Ward', 'Dune Rite']);
});

it('A_spell_with_no_school_matches_only_when_no_school_is_ticked', () => {
  expect(names({})).toContain('Elm Mystery');
  expect(names({ schools: ['Evocation'] })).not.toContain('Elm Mystery');
  expect(names({ schools: ['Evocation', 'Divination', 'Illusion'] })).not.toContain('Elm Mystery');
});

// MECH-X item 1 (R39): tags are kept apart from school; a tag filter keeps the spells that carry any ticked tag, ANDed with the rest.
const tagged: SpellEntry[] = [
  sp('Alder Spark', 0, { tags: ['time'] }),
  sp('Birch Veil', 1, { tags: ['Time', 'ward'] }),
  sp('Cedar Ward', 1),
  sp('Dune Rite', 2, { tags: ['ward'], school: 'Divination' }),
];
const taggedNames = (f: Partial<SpellFilter>) => filterSpells(tagged, { ...none, ...f }).map((s) => s.name);

it('a tag filter keeps only the spells that carry the tag, case-insensitively', () => {
  expect(taggedNames({ tags: ['time'] })).toEqual(['Alder Spark', 'Birch Veil']);
  expect(taggedNames({ tags: ['TIME', 'ward'] })).toEqual(['Alder Spark', 'Birch Veil', 'Dune Rite']);
  expect(taggedNames({ tags: [] })).toHaveLength(4);
  expect(taggedNames({})).toHaveLength(4);
});

it('a tag filter is ANDed with the other dimensions, and a spell with no tags never matches one', () => {
  expect(taggedNames({ tags: ['ward'], levels: [1] })).toEqual(['Birch Veil']);
  expect(taggedNames({ tags: ['ward'], schools: ['divination'] })).toEqual(['Dune Rite']);
  expect(taggedNames({ tags: ['nothing'] })).toEqual([]);
});
