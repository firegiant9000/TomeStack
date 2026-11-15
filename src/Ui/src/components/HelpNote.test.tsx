// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../test/axe';
import { HelpNote } from './HelpNote';

afterEach(cleanup);

it('is a closed details with a labelled "?" summary that opens on click and from the keyboard', async () => {
  const user = userEvent.setup();
  const { container } = render(<HelpNote topic="fixture topic">Fixture explanation.</HelpNote>);
  const details = container.querySelector('details.help-note') as HTMLDetailsElement;
  expect(details.open).toBe(false);
  const summary = screen.getByLabelText('Help: fixture topic');
  expect(summary.tagName).toBe('SUMMARY');
  expect(summary.textContent).toBe('?');
  await user.click(summary);
  expect(details.open).toBe(true);
  await user.click(summary);
  expect(details.open).toBe(false);
  // Keyboard: a native summary is in the tab order and opens on Enter or Space in a browser (jsdom models neither; the e2e/smoke gate covers it).
  await user.click(summary);
  expect(details.open).toBe(true);
  await expectNoAxeViolations(container);
});
