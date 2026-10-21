// @vitest-environment jsdom
// Wave 3 item 22 (R157): a resource with an activation is listed under that group in the Resources panel; one without is listed as before.
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import type { CharacterView } from '../api/types';
import { ResourcesPanel } from './PlayPanels';

afterEach(cleanup);

const resource = (id: string, extra: object = {}) => ({
  content: { contentId: 'c1', revisionId: 'r1' }, contentName: 'Testy Source', effectId: `e-${id}`, resourceId: id, label: `Testy ${id}`,
  maximum: 2, spent: 0, current: 2, trace: [], warnings: [], automation: 'automatic', recoveries: [], ...extra,
});
const view = (resources: object[]) => ({ character: { id: 'fixture' }, sheet: { resources } }) as unknown as CharacterView;

it('groups resources under Action, Bonus action and Reaction, and lists those without an activation as before', () => {
  const v = view([
    resource('plain'),
    resource('swing', { activation: 'action' }),
    resource('rush', { activation: 'bonusAction' }),
    resource('ward', { activation: 'reaction' }),
  ]);
  render(<ResourcesPanel view={v} act={() => {}} />);
  const panel = screen.getByRole('region', { name: 'Resources' });

  for (const [group, label] of [['Actions', 'swing'], ['Bonus actions', 'rush'], ['Reactions', 'ward']]) {
    const region = within(panel).getByRole('region', { name: group });
    expect(within(region).getByRole('heading', { name: new RegExp(`^Testy ${label}: 2 of 2`) })).toBeTruthy();
    expect(within(region).getAllByRole('button', { name: /^Spend 1/ })).toHaveLength(1);
  }
  expect(within(within(panel).getByRole('region', { name: 'Reactions' })).queryByRole('heading', { name: /Testy plain/ })).toBeNull();
  expect(within(panel).getByRole('heading', { name: /^Testy plain: 2 of 2/ })).toBeTruthy();
});

it('renders no group at all when no resource has an activation', () => {
  render(<ResourcesPanel view={view([resource('plain')])} act={() => {}} />);
  const panel = screen.getByRole('region', { name: 'Resources' });
  expect(within(panel).queryByRole('region')).toBeNull();
  expect(within(panel).getByRole('heading', { name: /^Testy plain: 2 of 2/ })).toBeTruthy();
});
