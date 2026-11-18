import { describe, expect, it } from 'vitest';
import { LOCAL_MAX_WORKERS, resolveMaxWorkers } from './vitest.workers';

describe('resolveMaxWorkers', () => {
  it('caps local runs', () => {
    expect(LOCAL_MAX_WORKERS).toBe(8);
    expect(resolveMaxWorkers({})).toBe(8);
  });

  it('leaves the Vitest default on CI', () => {
    expect(resolveMaxWorkers({ CI: 'true' })).toBeUndefined();
    expect(resolveMaxWorkers({ CI: '1' })).toBeUndefined();
  });

  it('treats CI=false or empty as not CI', () => {
    expect(resolveMaxWorkers({ CI: 'false' })).toBe(8);
    expect(resolveMaxWorkers({ CI: 'FALSE' })).toBe(8);
    expect(resolveMaxWorkers({ CI: '' })).toBe(8);
  });

  it('honours a positive integer override, even on CI', () => {
    expect(resolveMaxWorkers({ TOMESTACK_TEST_WORKERS: '4' })).toBe(4);
    expect(resolveMaxWorkers({ CI: 'true', TOMESTACK_TEST_WORKERS: '3' })).toBe(3);
  });

  it.each(['abc', '0', '-3', '2.5'])('ignores the junk override %s', (junk) => {
    expect(resolveMaxWorkers({ TOMESTACK_TEST_WORKERS: junk })).toBe(8);
    expect(resolveMaxWorkers({ CI: 'true', TOMESTACK_TEST_WORKERS: junk })).toBeUndefined();
  });
});
