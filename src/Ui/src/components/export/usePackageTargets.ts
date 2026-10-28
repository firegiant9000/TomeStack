import { useEffect, useState } from 'react';
import { client } from '../../api/client';
import type { PackageTargets } from '../../api/types';

/**
 * The releases a copy can be made for, loaded once per mount. A service that cannot say (an older one, or a failed call) leaves this
 * null: the target control stays hidden and the panel is as it was. The failure is deliberately not reported.
 */
export function usePackageTargets(): PackageTargets | null {
  const [targets, setTargets] = useState<PackageTargets | null>(null);
  useEffect(() => {
    let current = true;
    void (async () => {
      try {
        const loaded = await client.packageTargets();
        if (current) setTargets(loaded ?? null);
      } catch {
        // No target control without the list.
      }
    })();
    return () => {
      current = false;
    };
  }, []);
  return targets;
}
