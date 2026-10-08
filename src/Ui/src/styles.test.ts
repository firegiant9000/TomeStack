// The light palette may change; the dark values are the owner's (2026-10-06) and must not (owner, 2026-10-07, D27).
import { readFileSync } from 'node:fs';
import { expect, it } from 'vitest';

// Comments are stripped first: they mention light-dark() in prose and would otherwise parse as (empty) pairs.
const css = readFileSync(new URL('./styles.css', import.meta.url), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

/** Every `light-dark(a, b)` in the stylesheet, as [a, b] with nested parentheses kept whole. */
function lightDarkPairs(source: string): [string, string][] {
  const pairs: [string, string][] = [];
  const re = /light-dark\(/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(source))) {
    let depth = 1;
    let i = m.index + m[0].length;
    let split = -1;
    for (; i < source.length && depth > 0; i++) {
      const c = source[i];
      if (c === '(') depth++;
      else if (c === ')') depth--;
      else if (c === ',' && depth === 1 && split < 0) split = i;
    }
    const inner = source.slice(m.index + m[0].length, i - 1);
    const at = split - (m.index + m[0].length);
    pairs.push([inner.slice(0, at).trim(), inner.slice(at + 1).trim()]);
  }
  return pairs;
}

it('keeps every dark value exactly as the owner set it on 2026-10-06 (D27: light changes only)', () => {
  const dark = lightDarkPairs(css).map(([, b]) => b);
  expect(dark.sort()).toEqual(
    [
      // forest
      '#1b241c', '#8fe0a8', '#ebd070', '#f0b35a', '#f28b82',
      // cool
      '#1e2230', '#aeb8ff', '#73ebdb',
      // violet
      '#211a2c', '#d6beff', '#86e3f5',
      // --bg (three themes) and --line-strong and --header-bg carry today's dark expressions
      'Canvas', 'Canvas', 'Canvas',
      'color-mix(in srgb, CanvasText 50%, Canvas)',
      'color-mix(in srgb, var(--accent) 12%, var(--bg))', 'color-mix(in srgb, var(--accent) 12%, var(--bg))', 'color-mix(in srgb, var(--accent) 12%, var(--bg))',
    ].sort(),
  );
});

it('declares the light values the spec computed (section 3 of the 2026-10-07 spec)', () => {
  const light = lightDarkPairs(css).map(([a]) => a);
  for (const value of ['#eef8f0', '#cfe9d7', '#1e6a3a', '#7a5a00', '#855000', '#eef3fe', '#d3ddf8', '#3449b8', '#0b625b', '#f6f1fe', '#e2d4f7', '#6a33c4', '#075f78']) {
    expect(light).toContain(value);
  }
});
