// @vitest-environment jsdom
// An import hook's "Rules" choice names the rules by their short label, never by an id. Values are invented.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { InstalledExtension, RulesFamilyPolicy } from '../api/types';
import { client } from '../api/client';
import { RulesLabelProvider } from '../rulesLabel';
import { ExtensionsPanel } from './ExtensionsPanel';

vi.mock('../api/client', () => ({ client: { listExtensions: vi.fn() } }));

afterEach(cleanup);

const families = [
  { id: 'srd-5.1', displayName: 'SRD 5.1', shortLabel: '2014 rules' },
  { id: 'srd-5.2.1', displayName: 'SRD 5.2.1', shortLabel: '2024 rules' },
] as unknown as RulesFamilyPolicy[];

const extension = {
  id: 'ext-1',
  enabled: true,
  grants: [],
  manifest: { name: 'Fixture importer', version: '1.0.0', author: 'Nobody', hooks: [{ id: 'hook-1', kind: 'import', label: 'Import fixtures', accepts: 'csv' }] },
} as unknown as InstalledExtension;

it('offers the rules of an import hook by short label, never by id', async () => {
  vi.mocked(client.listExtensions).mockResolvedValue([extension]);
  render(
    <RulesLabelProvider families={families}>
      <ExtensionsPanel characters={[]} rulesFamilies={families} onError={() => {}} onStatus={() => {}} />
    </RulesLabelProvider>,
  );

  await userEvent.setup().click(await screen.findByRole('button', { name: 'Import fixtures' }));

  expect(screen.getByRole('option', { name: '2014 rules' })).toBeTruthy();
  expect(screen.getByRole('option', { name: '2024 rules' })).toBeTruthy();
  expect(screen.queryByRole('option', { name: /srd-5\./ })).toBeNull();
  expect(screen.queryByRole('option', { name: /SRD 5\./ })).toBeNull();
});
