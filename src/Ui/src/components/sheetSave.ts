import { useCallback, useEffect, useRef } from 'react';
import { client } from '../api/client';
import type { Character, CharacterView } from '../api/types';

/**
 * R92/R132: how a sheet panel saves the character. The panel passes an updater, `(latest) => next`, applied to the latest
 * saved character, never a spread of its own props snapshot. Two panels that save close together therefore both keep their
 * change: the second does not write the first one's field back to its old value.
 */
export type SheetSave = (update: (latest: Character) => Character) => Promise<CharacterView>;

/**
 * G9/G10: an updater throws this when the entry its change is about is no longer in the latest saved character (removed by a
 * restore, an import or another panel). Nothing is saved; the panel says so with its own message, not "Saved".
 */
export class EntryGone extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'EntryGone';
  }
}

/** The save for a panel mounted on its own (tests, or no sheet around it): one save from the character it was given. */
export const directSave = (character: Character): SheetSave => (update) => client.saveCharacter(update(character));

/**
 * The sheet container's save: every save runs after the one before it finishes, and each starts from the character that
 * save returned. Each caller still gets its own result (or its own failure), so each panel keeps its own status and focus.
 */
export type QueuedSheetSave = {
  save: SheetSave;
  /**
   * R182: takes this view as the queue's latest and shown copy, unconditionally. For the copy read after a `character.stale`
   * refusal: it is the stored truth even when its stamp sorts lower than the queue's (a clock stepped back, or "Z" against "+00:00"),
   * so no later save is wedged on a version the store no longer has.
   */
  adopt: (view: CharacterView) => void;
  /**
   * R188: runs a command that is not a sheet save (a play action, an Undo, a rest finish) in the same order as the saves. It starts
   * after every earlier entry has finished, and its reply's character becomes the queue's latest, so a save started meanwhile
   * names the stamp the command left. A failed command changes nothing in the queue.
   */
  run: <T>(command: () => Promise<T>, characterOf: (reply: T) => Character | undefined) => Promise<T>;
};

export function useSheetSave(view: CharacterView): QueuedSheetSave {
  const latest = useRef(view.character);
  const pending = useRef(0);
  const tail = useRef<Promise<unknown>>(Promise.resolve());

  const shown = useRef(view.character);

  // R137: the newest view the sheet was given is always kept. A view shown by another route (a restore, a rest, a reload)
  // that arrives while saves are queued is picked up when the next one starts, if it is newer than the last save's result.
  useEffect(() => {
    shown.current = view.character;
    if (pending.current === 0 && (view.character.updatedAt ?? '') >= (latest.current.updatedAt ?? '')) latest.current = view.character;
  }, [view.character]);

  const adopt = useCallback((fresh: CharacterView) => {
    latest.current = fresh.character;
    shown.current = fresh.character;
  }, []);

  const run = useCallback(<T,>(command: () => Promise<T>, characterOf: (reply: T) => Character | undefined): Promise<T> => {
    pending.current += 1;
    const step = async () => {
      const reply = await command();
      // R188: the command stamped the character itself, so the next save must name that stamp, not the one before it.
      const next = characterOf(reply);
      if (next) {
        latest.current = next;
        shown.current = next;
      }
      return reply;
    };
    const result = tail.current.then(step);
    tail.current = result.then(
      () => undefined,
      () => undefined,
    );
    return result.finally(() => {
      pending.current -= 1;
    });
  }, []);

  const save = useCallback<SheetSave>((update) => {
    pending.current += 1;
    const step = async () => {
      // Ties go to the queue's own latest. R182: character.save is compare-and-set on updatedAt, so each save names the version it
      // edits: the queue's latest is always the previous save's returned character, and its updatedAt is passed on verbatim. A newer
      // shown view replaces it; a save that is refused as stale rejects here and the sheet reads the current copy.
      if ((shown.current.updatedAt ?? '') > (latest.current.updatedAt ?? '')) latest.current = shown.current;
      const saved = await client.saveCharacter(update(latest.current));
      latest.current = saved.character;
      return saved;
    };
    const result = tail.current.then(step);
    tail.current = result.then(
      () => undefined,
      () => undefined,
    );
    return result.finally(() => {
      pending.current -= 1;
    });
  }, []);

  return { save, adopt, run };
}
