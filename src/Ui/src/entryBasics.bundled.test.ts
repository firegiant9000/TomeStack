import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from 'vitest';
import type { Effect } from './api/types';
import { increaseChoice, increaseOptions } from './entryBasics';

interface Bundled {
  contentId: string;
  revisionId: string;
  kind: string;
  name: string;
  effects: Effect[];
}

// R48 shape fidelity: what the studio writes for a background's ability score choice is what the bundled 5.2.1 pack ships.
test('increaseOptions and increaseChoice produce the shape of the bundled Soldier background', () => {
  const pack = JSON.parse(readFileSync(resolve(process.cwd(), '../AppService/Content/srd-5.2.1.json'), 'utf8')) as { revisions: Bundled[] };
  const soldier = pack.revisions.find((r) => r.kind === 'background' && r.name === 'Soldier');
  expect(soldier).toBeDefined();
  const bundledChoice = soldier!.effects.find((e) => e.type === 'choice');
  expect(bundledChoice?.type).toBe('choice');
  if (bundledChoice?.type !== 'choice') return;
  const bundledOptions = bundledChoice.options.map((o) => pack.revisions.find((r) => r.contentId === o.contentId)!);
  expect(bundledOptions).toHaveLength(7);

  const options = increaseOptions('Soldier', ['str', 'dex', 'con']);
  expect(options.map((o) => o.name)).toEqual(bundledOptions.map((o) => o.name));
  expect(options.map((o) => o.effects)).toEqual(bundledOptions.map((o) => o.effects));

  // The ids are minted by the service, so stand-ins are used for the references.
  const refs = options.map((_, i) => ({ contentId: `c-${i}`, revisionId: `r-${i}` }));
  const choice = increaseChoice('Soldier', refs);
  expect(choice.choiceId).toBe(bundledChoice.choiceId);
  expect(choice.id).toBe(bundledChoice.id);
  expect(choice.count).toBe(bundledChoice.count);
  expect(choice.options).toHaveLength(bundledChoice.options.length);
});
