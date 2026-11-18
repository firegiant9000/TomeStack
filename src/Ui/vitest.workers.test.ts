import { describe, expect, it } from 'vitest';
import { LOCAL_MAX_WORKERS, resolveMaxWorkers } from './vitest.workers';

const MANY = 24;

describe('resolveMaxWorkers', () => {
  it('caps local runs', () => {
    expect(LOCAL_MAX_WORKERS).toBe(8);
    expect(resolveMaxWorkers({}, MANY)).toBe(8);
  });

  it('never raises workers above the core count', () => {
    expect(resolveMaxWorkers({}, 4)).toBe(4);
    expect(resolveMaxWorkers({}, 1)).toBe(1);
  });

  it('leaves the Vitest default on CI', () => {
    expect(resolveMaxWorkers({ CI: 'true' }, MANY)).toBeUndefined();
    expect(resolveMaxWorkers({ CI: '1' }, MANY)).toBeUndefined();
  });

  it('treats CI=false or empty as not CI', () => {
    expect(resolveMaxWorkers({ CI: 'false' }, MANY)).toBe(8);
    expect(resolveMaxWorkers({ CI: 'FALSE' }, MANY)).toBe(8);
    expect(resolveMaxWorkers({ CI: '' }, MANY)).toBe(8);
  });

  it('honours a positive integer override, even on CI or above the core count', () => {
    expect(resolveMaxWorkers({ TOMESTACK_TEST_WORKERS: '4' }, MANY)).toBe(4);
    expect(resolveMaxWorkers({ CI: 'true', TOMESTACK_TEST_WORKERS: '3' }, MANY)).toBe(3);
    expect(resolveMaxWorkers({ TOMESTACK_TEST_WORKERS: '12' }, 4)).toBe(12);
  });

  it.each(['abc', '0', '-3', '2.5'])('ignores the junk override %s', (junk) => {
    expect(resolveMaxWorkers({ TOMESTACK_TEST_WORKERS: junk }, MANY)).toBe(8);
    expect(resolveMaxWorkers({ CI: 'true', TOMESTACK_TEST_WORKERS: junk }, MANY)).toBeUndefined();
  });
});
