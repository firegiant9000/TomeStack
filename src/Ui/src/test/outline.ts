import { expect } from 'vitest';

/**
 * The heading outline is continuous: walking the headings in document order, none is more than one level deeper than the one
 * before it. `from` is the level assumed above the first heading (0 for a whole screen, 1 for a panel that sits under a screen's h1).
 * The sidebar has no headings (its groups are labelled lists, owner 2026-11-13), so a whole-app walk starts at the screen's h1.
 */
export function expectContinuousOutline(root: ParentNode, from = 0): void {
  let previous = from;
  const levels: string[] = [];
  for (const heading of root.querySelectorAll<HTMLElement>('h1, h2, h3, h4, h5, h6')) {
    const level = Number(heading.tagName.slice(1));
    levels.push(`h${level} ${heading.textContent}`);
    expect(level, `"${heading.textContent}" skips a level after h${previous}; outline so far: ${levels.join(' | ')}`).toBeLessThanOrEqual(previous + 1);
    previous = level;
  }
}
