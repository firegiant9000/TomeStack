// @vitest-environment jsdom
// "Save for an older TomeStack": the tick that must be set before a copy that leaves things out, and the blocked hint.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { ExportChange } from '../../api/types';
import { expectNoAxeViolations } from '../../test/axe';
import { ExportConfirmation, hasBlocker, needsConfirmation } from './ExportConfirmation';

afterEach(cleanup);

const label = 'I understand this copy leaves out the items listed, and my own copy is unchanged';
const hint = 'This file is not made while something blocks it.';

function change(action: ExportChange['action']): ExportChange {
  return { entry: 'e', kind: 'content', id: 'compat-tide-stance', name: 'Compat Tide Stance', action, code: `code.${action}`, message: action };
}

it('classifies changes', () => {
  expect(needsConfirmation(undefined)).toBe(false);
  expect(needsConfirmation([])).toBe(false);
  expect(needsConfirmation([change('copied'), change('blocks')])).toBe(false);
  expect(needsConfirmation([change('dropped')])).toBe(true);
  expect(needsConfirmation([change('leftOut')])).toBe(true);
  expect(hasBlocker(undefined)).toBe(false);
  expect(hasBlocker([change('dropped'), change('copied')])).toBe(false);
  expect(hasBlocker([change('blocks')])).toBe(true);
});

it('shows the checkbox for dropped or left-out items and reports toggles', async () => {
  const onConfirmedChange = vi.fn();
  const user = userEvent.setup();
  render(<ExportConfirmation changes={[change('dropped')]} confirmed={false} onConfirmedChange={onConfirmedChange} hintId="h" />);
  const box = screen.getByRole('checkbox', { name: label }) as HTMLInputElement;
  expect(box.checked).toBe(false);
  await user.click(box);
  expect(onConfirmedChange).toHaveBeenCalledWith(true);
});

it('reflects the confirmed prop', () => {
  render(<ExportConfirmation changes={[change('leftOut')]} confirmed onConfirmedChange={() => {}} hintId="h" />);
  expect((screen.getByRole('checkbox', { name: label }) as HTMLInputElement).checked).toBe(true);
});

it('shows no checkbox for only copied or blocking changes, or none', () => {
  const { rerender } = render(<ExportConfirmation changes={[change('copied')]} confirmed={false} onConfirmedChange={() => {}} hintId="h" />);
  expect(screen.queryByRole('checkbox')).toBeNull();
  rerender(<ExportConfirmation changes={[change('blocks')]} confirmed={false} onConfirmedChange={() => {}} hintId="h" />);
  expect(screen.queryByRole('checkbox')).toBeNull();
  rerender(<ExportConfirmation confirmed={false} onConfirmedChange={() => {}} hintId="h" />);
  expect(screen.queryByRole('checkbox')).toBeNull();
});

it('shows the hint, with the given id, only when something blocks', () => {
  const { container, rerender } = render(<ExportConfirmation changes={[change('blocks')]} confirmed={false} onConfirmedChange={() => {}} hintId="export-hint" />);
  const text = container.querySelector('#export-hint');
  expect(text?.textContent).toBe(hint);
  rerender(<ExportConfirmation changes={[change('dropped')]} confirmed={false} onConfirmedChange={() => {}} hintId="export-hint" />);
  expect(screen.queryByText(hint)).toBeNull();
});

it('describes a disabled button through aria-describedby', () => {
  render(
    <>
      <ExportConfirmation changes={[change('blocks')]} confirmed={false} onConfirmedChange={() => {}} hintId="export-hint" />
      <button type="button" disabled aria-describedby="export-hint">
        Export package
      </button>
    </>,
  );
  const button = screen.getByRole('button', { name: 'Export package' });
  const describedBy = button.getAttribute('aria-describedby')!;
  expect(document.getElementById(describedBy)?.textContent).toBe(hint);
});

it('has no axe violations with the checkbox and the hint', async () => {
  const { container } = render(
    <ExportConfirmation changes={[change('dropped'), change('blocks')]} confirmed={false} onConfirmedChange={() => {}} hintId="h" />,
  );
  await expectNoAxeViolations(container);
});
