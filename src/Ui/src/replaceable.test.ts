// MECH-X item 2: replaceable choices. Offers, hints, the long-rest list, what a save replaced, and the notice's words.
import { describe, expect, it } from 'vitest';
import type { CharacterView, ChoiceStatus, ContentReference, Diagnostic } from './api/types';
import { afterLongRest, noticeText, replaceHint, replaceOffer, replacedPicks } from './replaceable';

const ref = (contentId: string, revisionId = 'r1'): ContentReference => ({ contentId, revisionId });
const choice = (over: Partial<ChoiceStatus> = {}): ChoiceStatus => ({
  source: ref('cls'),
  sourceName: 'Fixture Class',
  choiceId: 'technique',
  count: 3,
  options: [],
  selected: [],
  resolved: false,
  ...over,
});
const entry = (source: ContentReference, choiceId: string, picks: string[]) => ({ source, choiceId, selected: picks.map((p) => ref(p)) });
const view = (level: number, choices: { source: ContentReference; choiceId: string; selected: ContentReference[] }[], sheetChoices: ChoiceStatus[] = [], notices?: Diagnostic[]): CharacterView =>
  ({ character: { id: 'fixture', level, choices }, sheet: { characterId: 'fixture', choices: sheetChoices }, notices }) as unknown as CharacterView;
const notice = (content: ContentReference = ref('cls')): Diagnostic => ({ code: 'choice.replaced-outside-window', message: "Choice 'technique' had 1 pick(s) replaced outside its window.", content });

describe('offers and hints', () => {
  it('words the level-up offer by max', () => {
    expect(replaceOffer(choice({ replaceable: { when: 'levelUp', max: 1 } }))).toBe('You may replace one pick now.');
    expect(replaceOffer(choice({ replaceable: { when: 'levelUp', max: 3 } }))).toBe('You may replace up to 3 picks now.');
  });
  it('offers nothing for a long-rest or a plain choice', () => {
    expect(replaceOffer(choice({ replaceable: { when: 'longRest', max: 1 } }))).toBeUndefined();
    expect(replaceOffer(choice())).toBeUndefined();
  });
  it('words the hint by window, whatever the max', () => {
    expect(replaceHint(choice({ replaceable: { when: 'levelUp', max: 1 } }))).toBe('You may replace a pick at a level-up.');
    expect(replaceHint(choice({ replaceable: { when: 'longRest', max: 3 } }))).toBe('You may replace a pick after a long rest.');
    expect(replaceHint(choice())).toBeUndefined();
  });
});

describe('afterLongRest', () => {
  it('keeps long-rest choices that have picks', () => {
    const a = choice({ choiceId: 'a', replaceable: { when: 'longRest', max: 1 }, selected: [ref('x')] });
    const empty = choice({ choiceId: 'b', replaceable: { when: 'longRest', max: 1 } });
    const level = choice({ choiceId: 'c', replaceable: { when: 'levelUp', max: 1 }, selected: [ref('x')] });
    const plain = choice({ choiceId: 'd', selected: [ref('x')] });
    expect(afterLongRest([a, empty, level, plain])).toEqual([a]);
    expect(afterLongRest(undefined)).toEqual([]);
  });
});

describe('replacedPicks', () => {
  const rep = choice({ replaceable: { when: 'levelUp', max: 1 } });
  it('counts picks that are gone, matched by content id', () => {
    const before = view(2, [entry(ref('cls', 'r1'), 'technique', ['a', 'b'])]);
    const after = view(3, [entry(ref('cls', 'r2'), 'technique', ['a', 'c'])], [rep]);
    expect(replacedPicks(before, after)).toEqual([{ choice: rep, count: 1 }]);
  });
  it('does not count a first pick, growth or a re-pin to a newer revision', () => {
    const sheet = [rep];
    expect(replacedPicks(view(2, []), view(3, [entry(ref('cls'), 'technique', ['a'])], sheet))).toEqual([]);
    expect(replacedPicks(view(2, [entry(ref('cls'), 'technique', ['a'])]), view(3, [entry(ref('cls'), 'technique', ['a', 'b'])], sheet))).toEqual([]);
    const before = view(2, [{ source: ref('cls', 'r1'), choiceId: 'technique', selected: [ref('a', 'o1')] }]);
    const after = view(3, [{ source: ref('cls', 'r2'), choiceId: 'technique', selected: [ref('a', 'o2')] }], sheet);
    expect(replacedPicks(before, after)).toEqual([]);
  });
  it('counts a cleared choice', () => {
    expect(replacedPicks(view(2, [entry(ref('cls'), 'technique', ['a'])]), view(3, [], [rep]))).toEqual([{ choice: rep, count: 1 }]);
  });
  it('unions the entries of one source and choice, so an unchanged orphan cancels out', () => {
    const before = view(2, [entry(ref('cls', 'r1'), 'technique', ['a']), entry(ref('cls', 'r0'), 'technique', ['b'])]);
    const after = view(3, [entry(ref('cls', 'r2'), 'technique', ['a', 'b'])], [rep]);
    expect(replacedPicks(before, after)).toEqual([]);
  });
  it('ignores a choice with no replaceable (S5) and reads raw picks, not the applied list (S3)', () => {
    const plain = choice();
    expect(replacedPicks(view(2, [entry(ref('cls'), 'technique', ['a'])]), view(3, [entry(ref('cls'), 'technique', ['b'])], [plain]))).toEqual([]);
    // 'z' was dropped from the applied list of the sheet, but the stored pick is still there: no replacement
    const before = view(2, [entry(ref('cls'), 'technique', ['a', 'z'])], [choice({ selected: [ref('a')] })]);
    const after = view(3, [entry(ref('cls'), 'technique', ['a', 'z'])], [{ ...rep, selected: [ref('a')] }]);
    expect(replacedPicks(before, after)).toEqual([]);
  });
  it('tolerates a view with no choices', () => {
    const bare = { character: { id: 'f', level: 1 }, sheet: { characterId: 'f' } } as unknown as CharacterView;
    expect(replacedPicks(bare, bare)).toEqual([]);
  });
});

describe('noticeText', () => {
  const rep = choice({ replaceable: { when: 'levelUp', max: 1 } });
  it('is undefined with no notice', () => {
    expect(noticeText(view(2, []), view(2, [], [rep]))).toBeUndefined();
    expect(noticeText(view(2, []), view(2, [], [rep], [{ code: 'resource.full', message: 'x' }]))).toBeUndefined();
  });
  it('words a replacement at a level that did not rise', () => {
    const before = view(2, [entry(ref('cls'), 'technique', ['a'])]);
    const after = view(2, [entry(ref('cls'), 'technique', ['b'])], [rep], [notice()]);
    expect(noticeText(before, after)).toBe('Fixture Class: you replaced 1 pick outside its window (at a level-up).');
  });
  it('words a replacement of more than the max', () => {
    const many = choice({ replaceable: { when: 'longRest', max: 1 } });
    const before = view(2, [entry(ref('cls'), 'technique', ['a', 'b'])]);
    const after = view(2, [entry(ref('cls'), 'technique', ['c', 'd'])], [many], [notice()]);
    expect(noticeText(before, after)).toBe('Fixture Class: you replaced 2 picks, more than the 1 it allows at once.');
  });
  it('uses the fallback sentence when no replaced choice matches', () => {
    expect(noticeText(view(2, []), view(2, [], [rep], [notice(ref('cls', 'other'))]))).toBe('A choice had picks replaced outside its window.');
  });
  it('S4: two choices on one source, one swapped, give one sentence', () => {
    const one = choice({ choiceId: 'one', replaceable: { when: 'levelUp', max: 1 } });
    const two = choice({ choiceId: 'two', replaceable: { when: 'levelUp', max: 1 } });
    const before = view(2, [entry(ref('cls'), 'one', ['a']), entry(ref('cls'), 'two', ['x'])]);
    const after = view(2, [entry(ref('cls'), 'one', ['b']), entry(ref('cls'), 'two', ['x'])], [one, two], [notice()]);
    expect(noticeText(before, after)).toBe('Fixture Class: you replaced 1 pick outside its window (at a level-up).');
  });
  it('S4: a level-up swap while the level rose is not worded', () => {
    const before = view(2, [entry(ref('cls'), 'technique', ['a'])]);
    const after = view(3, [entry(ref('cls'), 'technique', ['b'])], [rep], [notice()]);
    expect(noticeText(before, after)).toBe('A choice had picks replaced outside its window.');
  });
  it('joins several with a space', () => {
    const a = choice({ source: ref('s1'), sourceName: 'First', replaceable: { when: 'levelUp', max: 1 } });
    const b = choice({ source: ref('s2'), sourceName: 'Second', replaceable: { when: 'levelUp', max: 1 } });
    const before = view(2, [entry(ref('s1'), 'technique', ['a']), entry(ref('s2'), 'technique', ['a'])]);
    const after = view(2, [entry(ref('s1'), 'technique', ['b']), entry(ref('s2'), 'technique', ['b'])], [a, b], [notice(ref('s1')), notice(ref('s2'))]);
    expect(noticeText(before, after)).toBe('First: you replaced 1 pick outside its window (at a level-up). Second: you replaced 1 pick outside its window (at a level-up).');
  });
});
