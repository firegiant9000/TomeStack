// MECH-X item 2 (R42-R47): replaceable choices. Pure helpers; the server counts the same way (matched by source content id +
// choice id, picks by option content id, never by revision id).
import type { CharacterView, ChoiceStatus } from './api/types';

export const picks = (n: number) => `${n} pick${n === 1 ? '' : 's'}`;

/** R44: the level-up builder's offer for a `levelUp` choice that already had picks. Undefined for any other choice. */
export function replaceOffer(choice: ChoiceStatus): string | undefined {
  const r = choice.replaceable;
  if (r?.when !== 'levelUp') return undefined;
  return r.max > 1 ? `You may replace up to ${r.max} picks now.` : 'You may replace one pick now.';
}

/** R44: the hint in "Make choices", so a player can avoid the notice. */
export function replaceHint(choice: ChoiceStatus): string | undefined {
  const r = choice.replaceable;
  if (!r) return undefined;
  return r.when === 'levelUp' ? 'You may replace a pick at a level-up.' : 'You may replace a pick after a long rest.';
}

/** R44: the `longRest` choices that have picks to replace. */
export function afterLongRest(choices: ChoiceStatus[] | undefined): ChoiceStatus[] {
  return (choices ?? []).filter((c) => c.replaceable?.when === 'longRest' && c.selected.length > 0);
}

const keyOf = (contentId: string, choiceId: string) => `${contentId}\n${choiceId}`;

/** The option content ids stored per (source content id, choice id): the union over every entry of that pair (S3, I1). */
function stored(view: CharacterView): Map<string, Set<string>> {
  const found = new Map<string, Set<string>>();
  for (const entry of view.character.choices ?? []) {
    const key = keyOf(entry.source.contentId, entry.choiceId);
    const set = found.get(key) ?? new Set<string>();
    for (const s of entry.selected) set.add(s.contentId);
    found.set(key, set);
  }
  return found;
}

/** True when the choice already had a pick in `view` (the level-up builder's starting view), stored or applied. */
export function hadPicks(view: CharacterView, choice: ChoiceStatus): boolean {
  if ((stored(view).get(keyOf(choice.source.contentId, choice.choiceId))?.size ?? 0) > 0) return true;
  return (view.sheet.choices ?? []).some((c) => c.source.contentId === choice.source.contentId && c.choiceId === choice.choiceId && c.selected.length > 0);
}

/**
 * R42: for each replaceable choice on the saved sheet, how many picks of the starting view are gone. Reads the raw
 * `character.choices` of both views (S3); only the choice's name and `replaceable` come from the sheet. A first pick has
 * nothing stored, so it never counts. Choices without `replaceable` are left out (S5).
 */
export function replacedPicks(before: CharacterView, after: CharacterView): { choice: ChoiceStatus; count: number }[] {
  const was = stored(before);
  const now = stored(after);
  const seen = new Set<string>();
  const found: { choice: ChoiceStatus; count: number }[] = [];
  for (const choice of after.sheet.choices ?? []) {
    if (!choice.replaceable) continue;
    const key = keyOf(choice.source.contentId, choice.choiceId);
    if (seen.has(key)) continue;
    seen.add(key);
    const before = was.get(key);
    if (!before) continue;
    const kept = now.get(key);
    let count = 0;
    for (const id of before) if (!kept?.has(id)) count++;
    if (count > 0) found.push({ choice, count });
  }
  return found;
}

const fallback = 'A choice had picks replaced outside its window.';

/**
 * R47: the save notice worded from the sheet. The server's message holds ids and counts only. An entry is kept when a notice
 * names its source and it breaks R43 (more than `max`, or a `levelUp` choice whose level did not rise), so two replaceable
 * choices on one source never repeat a sentence (S4). A notice with no such entry gets the fallback sentence.
 */
export function noticeText(before: CharacterView, after: CharacterView): string | undefined {
  const notices = (after.notices ?? []).filter((n) => n.code === 'choice.replaced-outside-window');
  if (notices.length === 0) return undefined;
  const levelRose = after.character.level > before.character.level;
  const broken = replacedPicks(before, after).filter(({ choice, count }) => {
    const r = choice.replaceable!;
    return count > r.max || (r.when === 'levelUp' && !levelRose);
  });
  const sentences: string[] = [];
  const revisions = new Set<string>();
  for (const notice of notices) {
    const revisionId = notice.content?.revisionId ?? '';
    if (revisions.has(revisionId)) continue;
    revisions.add(revisionId);
    const mine = broken.filter(({ choice }) => choice.source.revisionId === revisionId);
    if (mine.length === 0) {
      sentences.push(fallback);
      continue;
    }
    for (const { choice, count } of mine) {
      sentences.push(
        count > choice.replaceable!.max
          ? `${choice.sourceName}: you replaced ${picks(count)}, more than the ${choice.replaceable!.max} it allows at once.`
          : `${choice.sourceName}: you replaced ${picks(count)} outside its window (at a level-up).`,
      );
    }
  }
  return sentences.join(' ');
}
