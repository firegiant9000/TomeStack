// @vitest-environment jsdom
// The disclosure's own name says which number it explains; the visible word stays "Why?". Names are invented.
import { render } from '@testing-library/react';
import { expect, it } from 'vitest';
import type { TraceEntry } from '../api/types';
import { ExplainList } from './ExplainList';

const steps: TraceEntry[] = [{ order: 0, operation: 'base', description: 'Fixture base', result: 10, amount: 10, origin: { kind: 'rules', rulesFamily: 'srd-5.1' } }];

it('names the number in the summary for assistive technology, not only on the list', () => {
  const { container } = render(<ExplainList label="Armor Class" steps={steps} />);
  const summary = container.querySelector('summary')!;
  expect(summary.firstChild?.textContent).toBe('Why?');
  expect(summary.textContent).toBe('Why? (Armor Class)');
  expect(summary.querySelector('.visually-hidden')?.textContent).toBe(' (Armor Class)');
});

it('leaves a summary that already names its number as it is', () => {
  const { container } = render(<ExplainList label="DC 13" steps={steps} summary="Why DC 13?" />);
  expect(container.querySelector('summary')?.textContent).toBe('Why DC 13?');
});
