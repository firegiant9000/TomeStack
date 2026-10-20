import { useCallback, useEffect, useRef } from 'react';
import { client } from '../api/client';
import type { Character, CharacterView } from '../api/types';

/**
 * R92/R132: how a sheet panel saves the character. The panel passes an updater, `(latest) => next`, applied to the latest
 * saved character, never a spread of its own props snapshot. Two panels that save close together therefore both keep their
 * change: the second does not write the first one's field back to its old value.
 */
export type SheetSave = (update: (latest: Character) => Character) => Promise<CharacterView>;

/** The save for a panel mounted on its own (tests, or no sheet around it): one save from the character it was given. */
export const directSave = (character: Character): SheetSave => (update) => client.saveCharacter(update(character));

/**
 * The sheet container's save: every save runs after the one before it finishes, and each starts from the character that
 * save returned. Each caller still gets its own result (or its own failure), so each panel keeps its own status and focus.
 */
export function useSheetSave(view: CharacterView): SheetSave {
  const latest = useRef(view.character);
  const pending = useRef(0);
  const tail = useRef<Promise<unknown>>(Promise.resolve());

  // A view shown by another route (a restore, a rest, a reload) becomes the latest, but never while a save is queued or
  // running and never an older one than a save already returned (the prop can lag the queue by a render).
  useEffect(() => {
    if (pending.current === 0 && (view.character.updatedAt ?? '') >= (latest.current.updatedAt ?? '')) latest.current = view.character;
  }, [view.character]);

  return useCallback<SheetSave>((update) => {
    pending.current += 1;
    const run = async () => {
      const saved = await client.saveCharacter(update(latest.current));
      latest.current = saved.character;
      return saved;
    };
    const result = tail.current.then(run);
    tail.current = result.then(
      () => undefined,
      () => undefined,
    );
    return result.finally(() => {
      pending.current -= 1;
    });
  }, []);
}
