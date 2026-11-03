import { useEffect, useState } from 'react';
import { client } from '../api/client';
import type { DuplicateGroup, DuplicateMember, DuplicatesRequest } from '../api/types';

/**
 * Import Lab L6: the groups `content.duplicates` finds for a request, asked again when `refreshKey` changes. They are advice only
 * (a chip beside a row), so a failed or slow request shows no chip rather than an error, and nothing is ever merged.
 */
export function useDuplicates(request: DuplicatesRequest, refreshKey?: unknown): DuplicateGroup[] {
  const [groups, setGroups] = useState<DuplicateGroup[]>([]);
  const { sourceId, jobId } = request;
  useEffect(() => {
    let current = true;
    // Neither named: that would be the whole library, which no row of a list is asking about.
    if (!sourceId && !jobId) {
      void Promise.resolve().then(() => {
        if (current) setGroups([]);
      });
      return () => {
        current = false;
      };
    }
    Promise.resolve()
      .then(() => client.contentDuplicates({ sourceId, jobId }))
      .then((found) => {
        if (current) setGroups(found);
      })
      .catch(() => {
        if (current) setGroups([]);
      });
    return () => {
      current = false;
    };
  }, [sourceId, jobId, refreshKey]);
  return groups;
}

/**
 * The entry the one in `isSelf` possibly duplicates: the first other member of the first group it is in (the groups come exact first).
 * A library entry is preferred to another candidate, since the chip says where the duplicate already is.
 */
export function duplicateOf(groups: DuplicateGroup[], isSelf: (member: DuplicateMember) => boolean): DuplicateMember | undefined {
  let fallback: DuplicateMember | undefined;
  for (const group of groups) {
    if (!group.members.some(isSelf)) continue;
    const others = group.members.filter((m) => !isSelf(m));
    const library = others.find((m) => m.contentId);
    if (library) return library;
    fallback ??= others[0];
  }
  return fallback;
}
