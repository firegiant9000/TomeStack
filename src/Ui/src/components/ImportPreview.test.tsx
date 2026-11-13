// @vitest-environment jsdom
// The package preview names the rules an imported campaign would play by their short label. The client is mocked; values are invented.
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import type { PackagePreview, RulesFamilyPolicy } from '../api/types';
import { RulesLabelProvider } from '../rulesLabel';
import { ImportPreview } from './ImportPreview';

vi.mock('../api/client', () => ({ client: { applyPackage: vi.fn() } }));

afterEach(cleanup);

const families = [
  { id: 'srd-5.1', displayName: 'SRD 5.1 (2014 rules)', shortLabel: '2014 rules' },
  { id: 'srd-5.2.1', displayName: 'SRD 5.2.1 (2024 rules)', shortLabel: '2024 rules' },
] as unknown as RulesFamilyPolicy[];

it('says which rules the imported campaign would play by short label, never an id or display name', () => {
  const preview: PackagePreview = {
    canApply: true,
    items: [],
    errors: [],
    warnings: [],
    campaignImpact: [{ campaignId: 'fixture-campaign', characterId: 'fixture-character', characterName: 'Fixture Wanderer', notAllowed: [], rulesFamily: 'srd-5.2.1' }],
  };
  render(
    <RulesLabelProvider families={families}>
      <ImportPreview fileName="fixture.zip" base64="" preview={preview} onApplied={() => {}} onCancel={() => {}} onError={() => {}} />
    </RulesLabelProvider>,
  );

  const section = screen.getByRole('region', { name: 'Characters affected if you use the imported campaign' });
  const line = within(section).getByRole('listitem').textContent;
  expect(line).toContain('The campaign would play 2024 rules.');
  expect(line).not.toMatch(/srd-5|SRD 5/);
});
