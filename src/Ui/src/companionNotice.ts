import type { CharacterView } from './api/types';

/** MECH-X R69: the service's advisory notice when a save reset content companions' hit points (R61). Never stored. */
export const companionPrunedCode = 'companion.play-pruned';

/** The notice's words (a count, never a name), or nothing when the reply carries none. */
export function companionPrunedText(view: CharacterView): string | undefined {
  return view.notices?.find((n) => n.code === companionPrunedCode)?.message;
}
