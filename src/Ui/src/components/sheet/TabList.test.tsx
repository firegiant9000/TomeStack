// @vitest-environment jsdom
// WAI-ARIA APG tabs: roles, one tab stop, arrow keys with wrap, Home/End, automatic activation, hidden panels.
import { useState } from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it } from 'vitest';
import { TabList, TabPanel, type TabSpec } from './TabList';

afterEach(cleanup);

type Id = 'one' | 'two' | 'three';
const tabs: readonly TabSpec<Id>[] = [
  { id: 'one', label: 'One' },
  { id: 'two', label: 'Two' },
  { id: 'three', label: 'Three' },
];

function Harness({ initial = 'one' }: { initial?: Id }) {
  const [active, setActive] = useState<Id>(initial);
  return (
    <>
      <TabList label="Fixture sections" idPrefix="fx" tabs={tabs} active={active} onActivate={setActive} />
      {tabs.map((t) => (
        <TabPanel key={t.id} idPrefix="fx" id={t.id} active={t.id === active}>
          <p>Panel {t.label}</p>
        </TabPanel>
      ))}
    </>
  );
}

it('renders APG roles, one tab stop, and hides inactive panels', () => {
  render(<Harness />);
  const list = screen.getByRole('tablist', { name: 'Fixture sections' });
  const all = screen.getAllByRole('tab');
  expect(all.map((t) => t.textContent)).toEqual(['One', 'Two', 'Three']);
  expect(list.contains(all[0]!)).toBe(true);
  expect(all[0]!.getAttribute('aria-selected')).toBe('true');
  expect(all[0]!.tabIndex).toBe(0);
  expect(all[1]!.getAttribute('aria-selected')).toBe('false');
  expect(all[1]!.tabIndex).toBe(-1);
  expect(all[0]!.getAttribute('aria-controls')).toBe('fx-panel-one');
  // The active panel is labelled by its tab; inactive panels are hidden (out of the accessibility tree) but mounted.
  const panel = screen.getByRole('tabpanel', { name: 'One' });
  expect(panel.id).toBe('fx-panel-one');
  expect(panel.tabIndex).toBe(-1);
  expect(screen.queryByRole('tabpanel', { name: 'Two' })).toBeNull();
  // (dom-accessibility-api gives a hidden element an empty name, so find it by id and check its label link directly.)
  const hiddenPanel = screen.getAllByRole('tabpanel', { hidden: true }).find((p) => p.id === 'fx-panel-two');
  expect(hiddenPanel?.hidden).toBe(true);
  expect(hiddenPanel?.getAttribute('aria-labelledby')).toBe('fx-tab-two');
  expect(screen.getByText('Panel Two')).toBeTruthy(); // still in the DOM
});

it('moves with the arrow keys (wrapping), Home and End, activating as it goes', async () => {
  const user = userEvent.setup();
  render(<Harness />);
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'One' }));
  await user.keyboard('{ArrowRight}');
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Two' }));
  expect(screen.getByRole('tab', { name: 'Two' }).getAttribute('aria-selected')).toBe('true');
  expect(screen.getByRole('tabpanel', { name: 'Two' })).toBeTruthy();
  await user.keyboard('{End}');
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Three' }));
  await user.keyboard('{ArrowRight}'); // wraps
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'One' }));
  await user.keyboard('{ArrowLeft}'); // wraps the other way
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Three' }));
  await user.keyboard('{Home}');
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'One' }));
  expect(screen.getByRole('tabpanel', { name: 'One' })).toBeTruthy();
});

function ExternalHarness() {
  const [active, setActive] = useState<Id>('one');
  return (
    <>
      <button type="button" onClick={() => setActive('three')}>
        Fixture jump
      </button>
      <TabList label="Fixture sections" idPrefix="fx" tabs={tabs} active={active} onActivate={setActive} />
      {tabs.map((t) => (
        <TabPanel key={t.id} idPrefix="fx" id={t.id} active={t.id === active}>
          <p>Panel {t.label}</p>
        </TabPanel>
      ))}
    </>
  );
}

it('does not steal focus on a later programmatic change after a key press that moved nothing', async () => {
  const user = userEvent.setup();
  render(<ExternalHarness />);
  await user.tab(); // the jump button
  await user.tab(); // tab One
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'One' }));
  await user.keyboard('{Home}'); // already first: nothing changes
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'One' }));
  await user.click(screen.getByRole('button', { name: 'Fixture jump' }));
  expect(screen.getByRole('tab', { name: 'Three' }).getAttribute('aria-selected')).toBe('true');
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Fixture jump' }));
});

it('lets modified keys through (Alt+Arrow, Ctrl+End) without moving or consuming them', async () => {
  const user = userEvent.setup();
  render(<Harness />);
  await user.tab();
  await user.keyboard('{Alt>}{ArrowRight}{/Alt}');
  await user.keyboard('{Control>}{End}{/Control}');
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'One' }));
  expect(screen.getByRole('tab', { name: 'One' }).getAttribute('aria-selected')).toBe('true');
  expect(screen.getByRole('tabpanel', { name: 'One' })).toBeTruthy();
});

it('activates on click and keeps focus on the tab', async () => {
  const user = userEvent.setup();
  render(<Harness />);
  await user.click(screen.getByRole('tab', { name: 'Three' }));
  expect(screen.getByRole('tab', { name: 'Three' }).getAttribute('aria-selected')).toBe('true');
  expect(screen.getByRole('tabpanel', { name: 'Three' })).toBeTruthy();
  expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Three' }));
});
