// D40 / Review Focus 5: a custom accent at every hue, in both schemes, keeps its contrast. The WCAG 2.x maths here is
// written out again on purpose: the test must not share arithmetic with the code it checks.
import { expect, it } from 'vitest';
import { clampAccent, customAccentCss, DARK_CANVAS, DEFAULT_ACCENT_HUE, hueName, hueOf, type Scheme } from './accentClamp';

const rgb = (hex: string): [number, number, number] => {
  expect(hex).toMatch(/^#[0-9a-f]{6}$/);
  return [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16)) as [number, number, number];
};
const channel = (c: number) => {
  const s = c / 255;
  return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
};
const luminance = ([r, g, b]: [number, number, number]) => 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
const ratio = (a: [number, number, number], b: [number, number, number]) => {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
};
const mixRgb = (a: [number, number, number], pct: number, b: [number, number, number]): [number, number, number] =>
  a.map((v, i) => v * pct + b[i]! * (1 - pct)) as [number, number, number];

const hues = Array.from({ length: 360 }, (_, h) => h);
const schemes: Scheme[] = ['light', 'dark'];
// CanvasText / Canvas as Edge computes them (Canvas in the dark scheme is measured, see DARK_CANVAS).
type Rgb = [number, number, number];
const text: Record<Scheme, Rgb> = { light: [0, 0, 0], dark: [255, 255, 255] };
const canvas: Record<Scheme, Rgb> = { light: [255, 255, 255], dark: [18, 18, 18] };
const warn = { light: '#855000', dark: '#f0b35a' };
const error = { light: '#b3261e', dark: '#f28b82' };

it('measures dark against the canvas Edge computes for a dark colour scheme', () => {
  expect(DARK_CANVAS).toBe('#121212');
});

it.each(schemes)('keeps every Review Focus 5 ratio at hues 0 to 359 (%s)', (scheme) => {
  const failures: string[] = [];
  for (const hue of hues) {
    const c = clampAccent(hue, scheme);
    const bg = rgb(c.bg);
    const surface = rgb(c.surface);
    const check = (label: string, value: number, floor: number) => {
      if (value < floor) failures.push(`${scheme} hue ${hue}: ${label} ${value.toFixed(3)} < ${floor}`);
    };
    for (const [name, hex] of [['accent', c.accent], ['accent-2', c.accent2]] as const) {
      const fill = rgb(hex);
      check(`${name} text on --bg`, ratio(fill, bg), 4.5);
      check(`${name} text on --surface`, ratio(fill, surface), 4.5);
      check(`${name} focus ring on --bg`, ratio(fill, bg), 3);
      check(`${name} focus ring on --surface`, ratio(fill, surface), 3);
      check(`--bg text on the ${name} fill`, ratio(bg, fill), 4.5);
      // R163: the 6.1:1 fill floor is reachable at every hue, so the custom accent keeps it too.
      check(`--bg text on the ${name} fill (R163)`, ratio(bg, fill), 6.1);
    }
    // The tokens the theme does not own must still hold on the tinted layers it does own.
    check('--fg on --bg', ratio(text[scheme], bg), 4.5);
    check('--fg on --surface', ratio(text[scheme], surface), 4.5);
    const muted = mixRgb(text[scheme], 0.65, canvas[scheme]);
    check('--muted on --surface', ratio(muted, surface), 4.5);
    const line = mixRgb(text[scheme], scheme === 'light' ? 0.55 : 0.5, canvas[scheme]);
    check('--line-strong on --surface', ratio(line, surface), 3);
    check('--warn on --surface', ratio(rgb(warn[scheme]), surface), 4.5);
    check('--error on --surface', ratio(rgb(error[scheme]), surface), 4.5);
    // The header tint (30% light, 12% dark) and its accent edge.
    const header = mixRgb(rgb(c.accent), scheme === 'light' ? 0.3 : 0.12, bg);
    check('--fg on the header', ratio(text[scheme], header), 4.5);
    check('accent edge on the header', ratio(rgb(c.accent), header), 3);
  }
  expect(failures).toEqual([]);
});

it.each(schemes)('clamps lightness only: the hue survives rounding, and --accent-2 sits a fixed offset away (%s)', (scheme) => {
  const off: string[] = [];
  const wrap = (d: number) => Math.abs(((d + 540) % 360) - 180);
  for (const hue of hues) {
    const c = clampAccent(hue, scheme);
    if (wrap(hueOf(c.accent) - hue) > 2) off.push(`accent ${hue} -> ${hueOf(c.accent).toFixed(1)}`);
    if (wrap(hueOf(c.accent2) - (hue + 150)) > 2) off.push(`accent-2 ${hue} -> ${hueOf(c.accent2).toFixed(1)}`);
  }
  expect(off).toEqual([]);
});

it('keeps dark --bg as Canvas and tints only the light layers', () => {
  for (const hue of [0, 90, 180, 270]) {
    expect(clampAccent(hue, 'dark').bg).toBe(DARK_CANVAS);
    expect(clampAccent(hue, 'light').bg).not.toBe('#ffffff');
  }
});

it('is a pure function of hue and scheme, and wraps hues outside 0-359', () => {
  expect(clampAccent(200, 'light')).toEqual(clampAccent(200, 'light'));
  expect(clampAccent(560, 'dark')).toEqual(clampAccent(200, 'dark'));
  expect(clampAccent(-160, 'dark')).toEqual(clampAccent(200, 'dark'));
});

it('names every hue with a word from a small fixed table', () => {
  const words = new Set(hues.map(hueName));
  expect(words.size).toBeGreaterThanOrEqual(10);
  expect(words.size).toBeLessThanOrEqual(16);
  expect(hueName(180)).toBe('Teal');
  expect(hueName(359)).toBe('Pink');
  expect(hueName(0)).toBe('Pink');
  expect(DEFAULT_ACCENT_HUE).toBeGreaterThanOrEqual(0);
  expect(DEFAULT_ACCENT_HUE).toBeLessThan(360);
});

it('writes a rule that forced colours and print win over, with no inline style', () => {
  const css = customAccentCss(200);
  // Everything sits behind a media query that forced colours does not match, so the theme-less system colours stay in force.
  expect(css).toContain('@media (not (forced-colors: active)) {');
  // The tinted page layers are left out of print (the `screen` media type), so the print block's Canvas wins there (D27).
  expect(css).toMatch(/@media screen and \(not \(forced-colors: active\)\)\s*\{[^}]*--bg:/);
  // `not print` inside a condition is invalid CSS: Edge reads the whole query as `not all` and silently drops the rule
  // (found by running the built dist in headless Edge, not by jsdom).
  expect(css).not.toMatch(/not print/);
  const outsidePrint = css.split('@media screen and')[0] ?? '';
  expect(outsidePrint).not.toMatch(/--bg:|--surface:|--header-bg:/);
  expect(css).toContain("html[data-theme='custom']");
  expect(css).not.toMatch(/style\s*=/);
  const c = [clampAccent(200, 'light'), clampAccent(200, 'dark')];
  expect(css).toContain(`light-dark(${c[0]!.accent}, ${c[1]!.accent})`);
  expect(css).toContain(`light-dark(${c[0]!.accent2}, ${c[1]!.accent2})`);
  expect(css).toContain(`light-dark(${c[0]!.surface}, ${c[1]!.surface})`);
});
