import type { DuplicateMember } from '../api/types';

/**
 * Import Lab L6: "Possible duplicate of {name} ({source})". Words, not colour alone, and only a pointer: the two entries stay as
 * they are, and nothing here merges or removes either (design 9.2).
 */
export function DuplicateChip({ other }: { other: DuplicateMember | undefined }) {
  if (!other) return null;
  return (
    <span className="tag duplicate-chip">
      Possible duplicate of {other.name} ({other.sourceName})
    </span>
  );
}
