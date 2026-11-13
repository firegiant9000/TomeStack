// @vitest-environment jsdom
// "Try it" names the rules by their short label, never by an id. Values are invented.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { ContentRevision, RulesFamilyPolicy } from '../api/types';
import { client } from '../api/client';
import { RulesLabelProvider } from '../rulesLabel';
import { SandboxPanel } from './SandboxPanel';

vi.mock('../api/client', () => ({ client: { listCharacters: vi.fn(), sandbox: vi.fn() } }));

afterEach(cleanup);

const families = [
  { id: 'srd-5.1', displayName: 'SRD 5.1', shortLabel: '2014 rules' },
  { id: 'srd-5.2.1', displayName: 'SRD 5.2.1', shortLabel: '2024 rules' },
] as unknown as RulesFamilyPolicy[];

const revision = { kind: 'feat', rulesFamilies: ['srd-5.1', 'srd-5.2.1'] } as unknown as ContentRevision;

function renderPanel() {
  vi.mocked(client.listCharacters).mockResolvedValue([]);
  render(
    <RulesLabelProvider families={families}>
      <SandboxPanel revision={revision} prepare={(r) => r} onError={() => {}} />
    </RulesLabelProvider>,
  );
}

it("offers the blank character's rules by short label, never by id", () => {
  renderPanel();

  expect(screen.getByRole('option', { name: '2014 rules' })).toBeTruthy();
  expect(screen.getByRole('option', { name: '2024 rules' })).toBeTruthy();
  expect(screen.queryByRole('option', { name: /srd-5\./ })).toBeNull();
  expect(screen.queryByRole('option', { name: /SRD 5\./ })).toBeNull();
});

it('names the rules of the unsaved result by short label, never by id', async () => {
  renderPanel();
  vi.mocked(client.sandbox).mockResolvedValue({
    view: { character: { level: 3, rulesFamily: 'srd-5.2.1' }, sheet: { fields: [], diagnostics: [] } },
    changes: [],
    validation: { errors: [] },
  } as never);

  await userEvent.setup().click(screen.getByRole('button', { name: 'Try it' }));

  const results = await screen.findByRole('region', { name: 'Try it results' });
  expect(results.textContent).toContain('(2024 rules)');
  expect(results.textContent).not.toContain('srd-5.2.1');
  expect(results.textContent).not.toContain('SRD 5.2.1');
});
