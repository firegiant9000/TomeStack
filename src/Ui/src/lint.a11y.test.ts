// @vitest-environment node
import { fileURLToPath } from 'node:url';
import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';

// The src/Ui folder, so the real eslint.config.js loads whatever folder Vitest starts in.
const uiRoot = fileURLToPath(new URL('..', import.meta.url));

describe('lint config', () => {
  // Loading ESLint and every plugin takes seconds; under the full suite's load it passed 5 s (2026-11-18).
  it('jsx-a11y is wired into the lint config', { timeout: 30_000 }, async () => {
    const eslint = new ESLint({ cwd: uiRoot });
    const results = await eslint.lintText('export const X = () => <img src="a.png" />;', { filePath: 'src/Probe.tsx' });
    expect(results.flatMap((r) => r.messages.map((m) => m.ruleId))).toContain('jsx-a11y/alt-text');
  });
});
