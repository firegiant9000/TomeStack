/**
 * D40 (item 26): the "Custom accent" theme. A hue goes in; a set of colours comes out whose lightness has been moved, and
 * only the lightness, until every contrast ratio the app promises holds (Review Focus 5):
 *   - accent text on `--bg` and on `--surface` >= 4.5:1, and the focus ring (the accent) >= 3:1 on both;
 *   - `--bg` text on an accent or accent-2 fill >= 4.5:1, and in fact >= 6.1:1 (R163: that floor is reachable at every hue,
 *     because a dark enough or light enough colour always exists, so the custom accent keeps it too);
 *   - the header tint (30% light, 12% dark) keeps `--fg` >= 4.5:1 and the accent edge >= 3:1.
 * The colours are built in OKLCH (perceptual lightness, hue held), reduced in chroma only where a colour does not fit sRGB,
 * and measured after rounding to 8-bit hex with the WCAG 2.x relative-luminance formula. Pure: no DOM, no storage.
 */
export type Scheme = 'light' | 'dark';

export interface ClampedAccent {
  bg: string;
  surface: string;
  accent: string;
  accent2: string;
}

/**
 * What `Canvas` computes to for `color-scheme: dark` in Edge / WebView2 (measured in headless Edge, 2026-10-20:
 * `getComputedStyle` of `color: Canvas` gives rgb(18, 18, 18); `CanvasText` is rgb(255, 255, 255)). The dark custom theme
 * leaves `--bg` as `Canvas`, so this is the colour its accents are measured against.
 */
export const DARK_CANVAS = '#121212';
const DARK_TEXT: Rgb = [255, 255, 255];
const LIGHT_TEXT: Rgb = [0, 0, 0];

/** The hue a first-time visitor to "Custom accent" sees (OKLCH degrees; a blue). Used when the stored hue is missing or invalid. */
export const DEFAULT_ACCENT_HUE = 265;

/** --accent-2 sits this many degrees round the hue wheel from --accent (a split complement). */
const ACCENT_2_OFFSET = 150;

/** Floors. FILL is the R163 floor for `--bg` text on a filled shape; SURFACE_TEXT is text of the accent on `--surface`. */
const FILL_FLOOR = 6.1;
const SURFACE_TEXT_FLOOR = 4.5;
const HEADER_TEXT_FLOOR = 4.5;
const HEADER_EDGE_FLOOR = 3;

type Rgb = [number, number, number];

const wrapHue = (hue: number): number => (((Math.round(hue) % 360) + 360) % 360);

// --- sRGB / OKLab maths (Bjorn Ottosson's matrices) -------------------------------------------------------------------

const toLinear = (c: number) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4);
const fromLinear = (c: number) => (c <= 0.0031308 ? 12.92 * c : 1.055 * c ** (1 / 2.4) - 0.055);

/** Linear sRGB (unclamped) for an OKLCH colour. */
function oklchToLinear(l: number, c: number, hueDeg: number): Rgb {
  const h = (hueDeg * Math.PI) / 180;
  const a = c * Math.cos(h);
  const b = c * Math.sin(h);
  const l_ = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m_ = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s_ = (l - 0.0894841775 * a - 1.291485548 * b) ** 3;
  return [
    4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
    -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
    -0.0041960863 * l_ - 0.7034186147 * m_ + 1.707614701 * s_,
  ];
}

const inGamut = (rgb: Rgb) => rgb.every((v) => v >= -1e-6 && v <= 1 + 1e-6);

/** The OKLCH colour as 8-bit sRGB; chroma is reduced (lightness and hue kept) only when it does not fit. */
function oklchToRgb(l: number, c: number, hue: number): Rgb {
  let chroma = c;
  if (!inGamut(oklchToLinear(l, chroma, hue))) {
    let lo = 0;
    let hi = chroma;
    for (let i = 0; i < 24; i++) {
      const mid = (lo + hi) / 2;
      if (inGamut(oklchToLinear(l, mid, hue))) lo = mid;
      else hi = mid;
    }
    chroma = lo;
  }
  return oklchToLinear(l, chroma, hue).map((v) => Math.round(255 * fromLinear(Math.min(1, Math.max(0, v))))) as Rgb;
}

const toHex = (rgb: Rgb) => `#${rgb.map((v) => v.toString(16).padStart(2, '0')).join('')}`;

const fromHex = (hex: string): Rgb => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16)) as Rgb;

/** The OKLCH hue, in degrees 0-359.99, of a `#rrggbb` colour. */
export function hueOf(hex: string): number {
  const [r, g, b] = fromHex(hex).map((v) => toLinear(v / 255)) as Rgb;
  const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
  const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
  const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
  const a = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s;
  const bb = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s;
  return ((Math.atan2(bb, a) * 180) / Math.PI + 360) % 360;
}

// --- WCAG 2.x --------------------------------------------------------------------------------------------------------

const luminance = ([r, g, b]: Rgb) => 0.2126 * toLinear(r / 255) + 0.7152 * toLinear(g / 255) + 0.0722 * toLinear(b / 255);

function contrast(a: Rgb, b: Rgb): number {
  const la = luminance(a);
  const lb = luminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/** `color-mix(in srgb, a pct%, b)` on 8-bit channels, as the browser mixes the header tint. */
const mix = (a: Rgb, pct: number, b: Rgb): Rgb => a.map((v, i) => v * pct + b[i]! * (1 - pct)) as Rgb;

// --- The clamp -------------------------------------------------------------------------------------------------------

interface Layers {
  bg: Rgb;
  surface: Rgb;
  text: Rgb;
  headerPct: number;
}

/** Where the lightness search starts (a mid-weight accent), where it may end, and how far each step moves it. */
const SEARCH = {
  light: { from: 0.5, to: 0.1, step: -0.005, chroma: 0.15 },
  dark: { from: 0.82, to: 0.99, step: 0.005, chroma: 0.13 },
} as const;

function clampOne(hue: number, scheme: Scheme, layers: Layers, chroma: number): Rgb {
  const { from, to, step } = SEARCH[scheme];
  let best: Rgb = oklchToRgb(to, chroma, hue);
  for (let l = from; step < 0 ? l >= to : l <= to; l += step) {
    const candidate = oklchToRgb(l, chroma, hue);
    const header = mix(candidate, layers.headerPct, layers.bg);
    const holds =
      contrast(candidate, layers.bg) >= FILL_FLOOR &&
      contrast(candidate, layers.surface) >= SURFACE_TEXT_FLOOR &&
      contrast(layers.text, header) >= HEADER_TEXT_FLOOR &&
      contrast(candidate, header) >= HEADER_EDGE_FLOOR;
    if (holds) return candidate;
    best = candidate;
  }
  return best; // the end of the range (near black or near white) is the last resort; the test proves it is never needed
}

/** The custom accent's colours for one hue (degrees, any integer; wrapped to 0-359) in one colour scheme. */
export function clampAccent(hue: number, scheme: Scheme): ClampedAccent {
  const h = wrapHue(hue);
  const bg = scheme === 'light' ? oklchToRgb(0.975, 0.015, h) : fromHex(DARK_CANVAS);
  const surface = scheme === 'light' ? oklchToRgb(0.915, 0.035, h) : oklchToRgb(0.235, 0.02, h);
  const layers: Layers = { bg, surface, text: scheme === 'light' ? LIGHT_TEXT : DARK_TEXT, headerPct: scheme === 'light' ? 0.3 : 0.12 };
  const { chroma } = SEARCH[scheme];
  return {
    bg: toHex(bg),
    surface: toHex(surface),
    accent: toHex(clampOne(h, scheme, layers, chroma)),
    accent2: toHex(clampOne(wrapHue(h + ACCENT_2_OFFSET), scheme, layers, chroma * 0.85)),
  };
}

/**
 * The stylesheet text that applies the custom accent. It is adopted through the CSSOM (`document.adoptedStyleSheets`), not
 * written as a `style` attribute or element, so the app's CSP (`style-src 'self'`) is untouched.
 *
 * Forced colours and print must still win, and an adopted sheet sorts after the page's own sheets at equal specificity:
 *   - the accents sit in `@media (not (forced-colors: active))`, so under Windows High Contrast none of it exists and the
 *     system-colour block in styles.css rules;
 *   - the tinted page layers (`--bg`, `--surface`, `--header-bg`) sit in `@media screen and (not (forced-colors: active))` (`not print` is not valid inside a condition: Edge turns it into `not all`, verified), so the print block's
 *     `Canvas` wins on paper (D27). The accents stay in print: print forces the light scheme, and they hold >= 6.1:1 on white.
 */
export function customAccentCss(hue: number): string {
  const light = clampAccent(hue, 'light');
  const dark = clampAccent(hue, 'dark');
  const pair = (a: string, b: string) => `light-dark(${a}, ${b})`;
  return [
    '@media (not (forced-colors: active)) {',
    "  html[data-theme='custom'] {",
    `    --accent: ${pair(light.accent, dark.accent)};`,
    `    --accent-2: ${pair(light.accent2, dark.accent2)};`,
    '  }',
    '}',
    '@media screen and (not (forced-colors: active)) {',
    "  html[data-theme='custom'] {",
    `    --bg: ${pair(light.bg, 'Canvas')};`,
    `    --surface: ${pair(light.surface, dark.surface)};`,
    `    --header-bg: ${pair('color-mix(in srgb, var(--accent) 30%, var(--bg))', 'color-mix(in srgb, var(--accent) 12%, var(--bg))')};`,
    '  }',
    '}',
    '',
  ].join('\n');
}

// --- Words for the slider --------------------------------------------------------------------------------------------

/** Where each colour word starts, in OKLCH degrees (red sits near 30, yellow near 110, green near 142, blue near 264). */
const hueBands: readonly (readonly [number, string])[] = [
  [0, 'Pink'],
  [15, 'Red'],
  [45, 'Orange'],
  [75, 'Yellow'],
  [115, 'Lime'],
  [140, 'Green'],
  [170, 'Teal'],
  [200, 'Cyan'],
  [225, 'Sky blue'],
  [250, 'Blue'],
  [285, 'Purple'],
  [315, 'Magenta'],
  [345, 'Pink'],
];

/** A colour word for a hue, from a small fixed table, so the slider can say a colour and not only a number. */
export function hueName(hue: number): string {
  const h = wrapHue(hue);
  let word = 'Pink';
  for (const [from, name] of hueBands) if (h >= from) word = name;
  return word;
}
