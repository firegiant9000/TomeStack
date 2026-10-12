// @vitest-environment jsdom
// D30 / R23: the polite status says how many spells the search shows and, in the builder, how many chosen ones it hides.
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { SpellSearch } from './SpellSearch';

afterEach(cleanup);

const search = (over: Partial<Parameters<typeof SpellSearch>[0]> = {}) =>
  render(<SpellSearch id="s" label="Search Fixture Arcanist spells by name" value="bolt" onChange={() => {}} shown={2} total={9} {...over} />);

it('names one chosen spell the search hides', () => {
  search({ hiddenChosen: 1 });
  expect(screen.getByText('2 of 9 spells shown, 1 chosen hidden by the search')).toBeTruthy();
});

it('names several chosen spells the search hides', () => {
  search({ hiddenChosen: 2 });
  expect(screen.getByText('2 of 9 spells shown, 2 chosen hidden by the search')).toBeTruthy();
});

it('drops the phrase when no chosen spell is hidden', () => {
  search({ hiddenChosen: 0 });
  expect(screen.getByText('2 of 9 spells shown')).toBeTruthy();
  expect(screen.queryByText(/chosen hidden/)).toBeNull();
});

it('is silent for a blank query even when chosen spells exist', () => {
  search({ value: '  ', hiddenChosen: 2 });
  expect(screen.queryByText(/spells shown/)).toBeNull();
});
