import { expect, it } from 'vitest';
import { foldName, matchesSpell } from './spellSearch';

it('matches case- and accent-insensitively, both ways', () => {
  expect(matchesSpell('Fixture Frost Ring', 'FROST')).toBe(true);
  expect(matchesSpell('Fixture Émber Wave', 'ember')).toBe(true);
  expect(matchesSpell('Fixture Ember Wave', 'émbér')).toBe(true);
  expect(matchesSpell('Fixture Spark', 'frost')).toBe(false);
});

it('a blank or marks-only query matches everything (Review Focus 4)', () => {
  expect(matchesSpell('Fixture Spark', '')).toBe(true);
  expect(matchesSpell('Fixture Spark', '   ')).toBe(true);
  expect(matchesSpell('Fixture Spark', '́̈')).toBe(true);
  expect(foldName('  Émber ')).toBe('ember');
});
