// @vitest-environment jsdom
// D72 (R9): explanations appear only in the running app. Print, share and export must never import the renderer or its components.
import { readdirSync, readFileSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import { expect, it } from 'vitest';

const allowed = new Set(['ExplainList.tsx', 'RollWhy.tsx', 'CharacterSheet.tsx', 'SpellsPanel.tsx', 'PlayPanels.tsx', 'SheetSummary.tsx', 'CompanionsPanel.tsx']);
const importsExplain = /from\s+['"][^'"]*\/(explain|ExplainList|RollWhy)['"]/;

function sources(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return sources(path);
    return /\.tsx?$/.test(entry.name) ? [path] : [];
  });
}

it('only the sheet components import the explain renderer, never print, share or export', () => {
  const files = sources(resolve(process.cwd(), 'src'));
  expect(files.length).toBeGreaterThan(50); // the scan found the source tree
  const offenders = files
    .filter((path) => !/\.test\.tsx?$/.test(path))
    .filter((path) => importsExplain.test(readFileSync(path, 'utf8')))
    .map((path) => basename(path))
    .filter((name) => !allowed.has(name));
  expect(offenders).toEqual([]);
  const importers = files.filter((path) => importsExplain.test(readFileSync(path, 'utf8')));
  expect(importers.length).toBeGreaterThanOrEqual(allowed.size); // the pattern still matches real imports
});
