// @vitest-environment jsdom
// The label for a rules family comes from the service (`shortLabel`), never from a map in the UI.
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import type { RulesFamilyPolicy } from './api/types';
import { RulesLabelProvider, useRulesLabel } from './rulesLabel';

afterEach(cleanup);

const policy = (id: string, displayName: string, shortLabel?: string) => ({ id, displayName, shortLabel }) as unknown as RulesFamilyPolicy;

function Probe({ id }: { id: string }) {
  const label = useRulesLabel();
  return <span data-testid="out">{label(id)}</span>;
}

const families = [policy('srd-5.1', 'SRD 5.1', '2014 rules'), policy('srd-5.2.1', 'SRD 5.2.1', '2024 rules')];

it('shows the shortLabel the service sent', () => {
  render(
    <RulesLabelProvider families={families}>
      <Probe id="srd-5.1" />
    </RulesLabelProvider>,
  );
  expect(screen.getByTestId('out').textContent).toBe('2014 rules');
});

it('shows the id for a family the build does not know', () => {
  render(
    <RulesLabelProvider families={families}>
      <Probe id="srd-9.9" />
    </RulesLabelProvider>,
  );
  expect(screen.getByTestId('out').textContent).toBe('srd-9.9');
});

it('falls back to displayName when shortLabel is missing', () => {
  render(
    <RulesLabelProvider families={[policy('srd-5.1', 'SRD 5.1')]}>
      <Probe id="srd-5.1" />
    </RulesLabelProvider>,
  );
  expect(screen.getByTestId('out').textContent).toBe('SRD 5.1');
});

it('shows the id with no provider, and with families not yet loaded', () => {
  render(<Probe id="srd-5.1" />);
  expect(screen.getByTestId('out').textContent).toBe('srd-5.1');
  cleanup();
  render(
    <RulesLabelProvider families={undefined}>
      <Probe id="srd-5.1" />
    </RulesLabelProvider>,
  );
  expect(screen.getByTestId('out').textContent).toBe('srd-5.1');
});
