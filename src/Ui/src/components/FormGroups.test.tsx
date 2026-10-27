// @vitest-environment jsdom
// D43 Slice 1: the form picker. One radio group per form group (Base first), overlays as checkboxes.
import { cleanup, render, screen, waitFor } from '@testing-library/react';
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
  groupLabel: 'Forms',
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
  expect(radios[0]?.getAttribute('aria-checked')).toBe('true');
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
  for (const radio of screen.getAllByRole('radio')) expect(radio.getAttribute('aria-disabled')).toBe('true');
  for (const box of screen.getAllByRole('checkbox')) expect((box as HTMLInputElement).disabled).toBe(true);
  expect(screen.queryByText('Fixture Plain')).toBeNull();
});

it('names the group by the sheet label, and falls back to the key in words', () => {
  const view = render(<FormGroups toggles={toggles()} act={vi.fn()} busy={false} />);
  expect(screen.getByRole('radiogroup', { name: 'Forms' })).toBeTruthy();
  view.rerender(<FormGroups toggles={toggles().map((t) => ({ ...t, groupLabel: undefined, group: 'form-kinds' }))} act={vi.fn()} busy={false} />);
  expect(screen.getByRole('radiogroup', { name: 'Form kinds' })).toBeTruthy();
});

it('shows the default cost of one use from the toggle\'s own pool, and finds the pool by content and id', () => {
  const own = { ...toggle('solo', 'Fixture Solo Form'), cost: undefined, costResourceContent: undefined, content: { contentId: 'c-own', revisionId: 'r-own' } };
  const pool = (contentId: string, label: string) => ({ content: { contentId, revisionId: 'r' }, resourceId: 'focus', label }) as never;
  render(<FormGroups toggles={[own]} act={vi.fn()} busy={false} resources={[pool('c-other', 'Wrong'), pool('c-own', 'Own Focus')]} />);
  expect(screen.getByRole('radio', { name: /Fixture Solo Form.*1 Own Focus/ })).toBeTruthy();
});

it('arrow keys only move focus; Space chooses the focused form, and Home and End skip nothing enabled', async () => {
  const act = vi.fn();
  const user = userEvent.setup();
  render(<FormGroups toggles={toggles()} act={act} busy={false} />);
  const base = screen.getByRole('radio', { name: 'Base' });
  const tide = screen.getByRole('radio', { name: /Fixture Tide Form/ });
  const ember = screen.getByRole('radio', { name: /Fixture Ember Form/ });
  expect([base, tide, ember].map((r) => r.tabIndex)).toEqual([0, -1, -1]);
  base.focus();
  await user.keyboard('{ArrowDown}');
  expect(document.activeElement).toBe(tide);
  await user.keyboard('{ArrowDown}');
  expect(document.activeElement).toBe(ember);
  await user.keyboard('{ArrowDown}');
  expect(document.activeElement).toBe(base);
  await user.keyboard('{End}');
  expect(document.activeElement).toBe(ember);
  await user.keyboard('{ArrowUp}{Home}{ArrowRight}');
  expect(document.activeElement).toBe(tide);
  expect(act).not.toHaveBeenCalled();
  await user.keyboard(' ');
  expect(act).toHaveBeenCalledTimes(1);
  expect(act).toHaveBeenLastCalledWith({ action: 'toggleOn', contentId: 'c-tide', toggleId: 'tide' });
});

it('after a refused choice the tab stop is back on the checked radio, and leaving the group resets it', async () => {
  const act = vi.fn(() => Promise.reject(new Error('refused')));
  const user = userEvent.setup();
  render(
    <>
      <FormGroups toggles={toggles({ tide: { on: true } })} act={act} busy={false} />
      <button type="button">After</button>
    </>,
  );
  const tide = screen.getByRole('radio', { name: /Fixture Tide Form/ });
  const ember = screen.getByRole('radio', { name: /Fixture Ember Form/ });
  await user.click(ember);
  expect(act).toHaveBeenCalledTimes(1);
  await waitFor(() => expect([tide.tabIndex, ember.tabIndex]).toEqual([0, -1]));
  // Arrowing to another radio moves the stop; tabbing out puts it back on the checked one.
  tide.focus();
  await user.keyboard('{ArrowDown}');
  expect(ember.tabIndex).toBe(0);
  await user.click(screen.getByRole('button', { name: 'After' }));
  expect([tide.tabIndex, ember.tabIndex]).toEqual([0, -1]);
});

it('ignores a second choice while the first is still being confirmed', async () => {
  let finish: () => void = () => undefined;
  const act = vi.fn(() => new Promise<void>((resolve) => (finish = resolve)));
  const user = userEvent.setup();
  render(<FormGroups toggles={toggles()} act={act} busy={false} />);
  await user.click(screen.getByRole('radio', { name: /Fixture Tide Form/ }));
  await user.click(screen.getByRole('radio', { name: /Fixture Ember Form/ }));
  expect(act).toHaveBeenCalledTimes(1);
  finish();
});
