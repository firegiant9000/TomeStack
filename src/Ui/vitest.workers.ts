import { availableParallelism } from 'node:os';

/** Local cap on Vitest workers; an uncapped run on a many-thread machine saturates the CPU and flakes timing tests. */
export const LOCAL_MAX_WORKERS = 8;

/**
 * Worker count for Vitest: a positive-integer TOMESTACK_TEST_WORKERS wins; on CI (set, non-empty, not "false")
 * returns undefined so Vitest keeps its default; otherwise the local cap, never above the core count.
 */
export function resolveMaxWorkers(
  env: Record<string, string | undefined>,
  cores: number = availableParallelism(),
): number | undefined {
  const override = env.TOMESTACK_TEST_WORKERS;
  if (override !== undefined && /^\d+$/.test(override)) {
    const n = Number.parseInt(override, 10);
    if (n > 0) return n;
  }
  const ci = env.CI;
  if (ci !== undefined && ci !== '' && ci.toLowerCase() !== 'false') return undefined;
  return Math.min(LOCAL_MAX_WORKERS, Math.max(1, cores));
}
