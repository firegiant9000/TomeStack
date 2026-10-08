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

/** The text between the braces that follow `opener` (nested braces kept whole). */
function blockAfter(source: string, opener: string): string {
  const start = source.indexOf(opener);
  expect(start, `${opener} is in the stylesheet`).toBeGreaterThanOrEqual(0);
  let depth = 1;
  let i = start + opener.length;
  const from = i;
  for (; i < source.length && depth > 0; i++) {
    if (source[i] === '{') depth++;
    else if (source[i] === '}') depth--;
  }
  return source.slice(from, i - 1);
}

/** `--token` -> [light, dark] for every light-dark() declaration in a block. */
function tokenPairs(block: string): Record<string, [string, string]> {
  const out: Record<string, [string, string]> = {};
  for (const m of block.matchAll(/(--[\w-]+)\s*:\s*(light-dark\([^;]*\))\s*;/g)) {
    out[m[1]] = lightDarkPairs(m[2])[0];
  }
  return out;
}

const headerLight = 'color-mix(in srgb, var(--accent) 30%, var(--bg))';
const headerDark = 'color-mix(in srgb, var(--accent) 12%, var(--bg))';

it('maps each token to the light value the spec computed, per theme block (section 3 of the 2026-10-07 spec)', () => {
  expect(tokenPairs(blockAfter(css, ':root {'))).toEqual({
    '--bg': ['#eef8f0', 'Canvas'],
    '--line-strong': ['color-mix(in srgb, CanvasText 55%, Canvas)', 'color-mix(in srgb, CanvasText 50%, Canvas)'],
    '--surface': ['#cfe9d7', '#1b241c'],
    '--accent': ['#1e6a3a', '#8fe0a8'],
    '--accent-2': ['#7a5a00', '#ebd070'],
    '--warn': ['#855000', '#f0b35a'],
    '--error': ['#b3261e', '#f28b82'],
    '--header-bg': [headerLight, headerDark],
  });
  expect(tokenPairs(blockAfter(css, "html[data-theme='cool'] {"))).toEqual({
    '--bg': ['#eef3fe', 'Canvas'],
    '--surface': ['#d3ddf8', '#1e2230'],
    '--accent': ['#3449b8', '#aeb8ff'],
    '--accent-2': ['#0b625b', '#73ebdb'],
    '--header-bg': [headerLight, headerDark],
  });
  expect(tokenPairs(blockAfter(css, "html[data-theme='violet'] {"))).toEqual({
    '--bg': ['#f6f1fe', 'Canvas'],
    '--surface': ['#e2d4f7', '#211a2c'],
    '--accent': ['#6a33c4', '#d6beff'],
    '--accent-2': ['#075f78', '#86e3f5'],
    '--header-bg': [headerLight, headerDark],
  });
});

it('forced colours and print both reset --bg and --header-bg, and reach themed pages (html[data-theme])', () => {
  // The theme blocks have specificity (0,1,1): a bare :root list would leave the tinted hex values in force.
  const forced = blockAfter(css, '@media (forced-colors: active) {');
  expect(forced).toMatch(/html\[data-theme\]/);
  expect(forced).toMatch(/--bg:\s*Canvas;/);
  expect(forced).toMatch(/--header-bg:\s*Canvas;/);
  const print = blockAfter(css, '@media print {');
  const rule = print.slice(0, print.indexOf('}'));
  expect(rule).toMatch(/html\[data-theme\]/);
  expect(rule).toMatch(/--bg:\s*Canvas;/);
  expect(rule).toMatch(/--surface:\s*Canvas;/);
  expect(rule).toMatch(/--header-bg:\s*Canvas;/);
});
