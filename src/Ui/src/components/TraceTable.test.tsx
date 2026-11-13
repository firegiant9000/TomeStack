// @vitest-environment jsdom
// The trace names the rules behind a number by their short label. Values are invented.
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import type { RulesFamilyPolicy, TraceEntry } from '../api/types';
import { RulesLabelProvider } from '../rulesLabel';
import { TraceTable } from './TraceTable';

afterEach(cleanup);

const families = [
  { id: 'srd-5.1', displayName: 'SRD 5.1 (2014 rules)', shortLabel: '2014 rules' },
  { id: 'srd-5.2.1', displayName: 'SRD 5.2.1 (2024 rules)', shortLabel: '2024 rules' },
] as unknown as RulesFamilyPolicy[];

it('prints "Rules: 2014 rules" for a step that comes from the rules policy', () => {
  const trace: TraceEntry[] = [{ order: 1, operation: 'base', description: 'Fixture step', result: 10, origin: { kind: 'rulesPolicy', rulesFamily: 'srd-5.1' } }];
  render(
    <RulesLabelProvider families={families}>
      <TraceTable label="Fixture field" trace={trace} labels={new Map()} />
    </RulesLabelProvider>,
  );

  const cell = screen.getByRole('cell', { name: /^Rules:/ });
  expect(cell.textContent).toBe('Rules: 2014 rules');
});
