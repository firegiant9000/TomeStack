// @vitest-environment jsdom
// D43 Slice 1: the form picker. One radio group per form group (Base first), overlays as checkboxes.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { ToggleValue } from '../api/types';
import { FormGroups } from './FormGroups';

afterEach(cleanup);

const GROUP = '6f0dc000-0000-4000-8000-000000000001';
const toggle = (toggleId: string, label: string, extra: Partial<ToggleValue> = {}): ToggleValue => ({
  content: { contentId: `c-${toggleId}`, revisionId: `r-${toggleId}` },
  contentName: 'Fixture Form Pack',
  effectId: toggleId,
  toggleId,
  label,
  on: false,
  resourceId: 'focus',
  group: 'forms',
  groupContent: GROUP,
  layer: 'primary',
  cost: 5,
  costResourceContent: GROUP,
  available: true,
  ...extra,
});

const toggles = (over: Partial<Record<string, Partial<ToggleValue>>> = {}): ToggleValue[] => [
  toggle('tide', 'Fixture Tide Form', over.tide),
  toggle('ember', 'Fixture Ember Form', { cost: 3, ...over.ember }),
  toggle('surge', 'Fixture Surge Overlay', { layer: 'overlay', cost: 1, ...over.surge }),
  toggle('bound', 'Fixture Bound Overlay', { layer: 'overlay', cost: undefined, available: false, unavailableReason: 'Needs Fixture Tide Form.', ...over.bound }),
];

it('renders one radio group per form group with Base first, overlays as checkboxes, and costs as text', () => {
  render(<FormGroups toggles={toggles()} act={vi.fn()} busy={false} />);
  const group = screen.getByRole('radiogroup', { name: 'Forms' });
  const radios = screen.getAllByRole('radio');
  expect(screen.getByRole('radio', { name: 'Base' })).toBe(radios[0]);
  expect(group.contains(radios[0] ?? null)).toBe(true);
  expect(radios).toHaveLength(3);
  expect((radios[0] as HTMLInputElement).checked).toBe(true);
  expect(screen.getByRole('radio', { name: /Fixture Tide Form.*5 Focus/ })).toBeTruthy();
  expect(screen.getByRole('radio', { name: /Fixture Ember Form.*3 Focus/ })).toBeTruthy();
  expect(screen.getByRole('checkbox', { name: /Fixture Surge Overlay.*1 Focus/ })).toBeTruthy();
});

it('choosing another form sends toggleOn with replace', async () => {
  const act = vi.fn();
  const user = userEvent.setup();
  const view = render(<FormGroups toggles={toggles()} act={act} busy={false} />);
  // With none on, a plain toggleOn.
  await user.click(screen.getByRole('radio', { name: /Fixture Tide Form/ }));
  expect(act).toHaveBeenLastCalledWith({ action: 'toggleOn', contentId: 'c-tide', toggleId: 'tide' });
  // With Tide on, choosing Ember replaces; choosing Base switches the current one off.
  view.rerender(<FormGroups toggles={toggles({ tide: { on: true } })} act={act} busy={false} />);
  await user.click(screen.getByRole('radio', { name: /Fixture Ember Form/ }));
  expect(act).toHaveBeenLastCalledWith({ action: 'toggleOn', contentId: 'c-ember', toggleId: 'ember', replace: true });
  await user.click(screen.getByRole('radio', { name: /^Base/ }));
  expect(act).toHaveBeenLastCalledWith({ action: 'toggleOff', contentId: 'c-tide', toggleId: 'tide' });
  // An overlay is a plain toggle.
  await user.click(screen.getByRole('checkbox', { name: /Fixture Surge Overlay/ }));
  expect(act).toHaveBeenLastCalledWith({ action: 'toggleOn', contentId: 'c-surge', toggleId: 'surge' });
});

it('an unavailable overlay is disabled and says why', () => {
  render(<FormGroups toggles={toggles()} act={vi.fn()} busy={false} />);
  const bound = screen.getByRole('checkbox', { name: /Fixture Bound Overlay/ });
  expect((bound as HTMLInputElement).disabled).toBe(true);
  expect(document.getElementById(bound.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Needs Fixture Tide Form.');
  expect((screen.getByRole('checkbox', { name: /Fixture Surge Overlay/ }) as HTMLInputElement).disabled).toBe(false);
});

it('disables every control while busy and leaves ungrouped toggles to the caller', () => {
  render(<FormGroups toggles={[...toggles(), toggle('plain', 'Fixture Plain', { group: undefined, groupContent: undefined, layer: undefined })]} act={vi.fn()} busy />);
  for (const control of [...screen.getAllByRole('radio'), ...screen.getAllByRole('checkbox')]) expect((control as HTMLInputElement).disabled).toBe(true);
  expect(screen.queryByText('Fixture Plain')).toBeNull();
});
