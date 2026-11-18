// @vitest-environment node
import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';

describe('lint config', () => {
  it('jsx-a11y is wired into the lint config', async () => {
    const eslint = new ESLint({ cwd: process.cwd() });
    const results = await eslint.lintText('export const X = () => <img src="a.png" />;', { filePath: 'src/Probe.tsx' });
    expect(results.flatMap((r) => r.messages.map((m) => m.ruleId))).toContain('jsx-a11y/alt-text');
  });
});
