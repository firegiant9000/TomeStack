// @vitest-environment jsdom
// "Save for an older TomeStack": the target radios shared by the four export panels.
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import type { PackageTargets } from '../../api/types';
import { expectNoAxeViolations } from '../../test/axe';
import { TargetFieldset } from './TargetFieldset';

afterEach(cleanup);

const targets: PackageTargets = {
  current: '0.8.0',
  currentRow: '0.8.0',
  previousMinor: '0.7.0',
  targets: [
    { release: '0.7.0', characterSchema: 10, contentSchema: 11 },
    { release: '0.6.0', characterSchema: 9, contentSchema: 10 },
    { release: '0.5.0', characterSchema: 8, contentSchema: 9 },
  ],
};

it('lists the current row first and checks it by default', () => {
  render(<TargetFieldset targets={targets} value={null} onChange={() => {}} />);
  const group = screen.getByRole('group', { name: 'Which version will open this file?' });
  expect(group).toBeTruthy();
  const radios = screen.getAllByRole('radio') as HTMLInputElement[];
  expect(radios.map((r) => r.labels?.[0]?.textContent)).toEqual(['0.8.0 or newer (everything)', '0.7.0', '0.6.0', '0.5.0']);
  expect(radios.map((r) => r.checked)).toEqual([true, false, false, false]);
  expect(new Set(radios.map((r) => r.name)).size).toBe(1);
});

it('checks the radio for the chosen release', () => {
  render(<TargetFieldset targets={targets} value="0.5.0" onChange={() => {}} />);
  expect((screen.getAllByRole('radio') as HTMLInputElement[]).map((r) => r.checked)).toEqual([false, false, false, true]);
});

it('reports the release, and null for the first radio', async () => {
  const onChange = vi.fn();
  const user = userEvent.setup();
  const { rerender } = render(<TargetFieldset targets={targets} value={null} onChange={onChange} />);
  await user.click(screen.getByRole('radio', { name: '0.6.0' }));
  expect(onChange).toHaveBeenLastCalledWith('0.6.0');
  rerender(<TargetFieldset targets={targets} value="0.6.0" onChange={onChange} />);
  await user.click(screen.getByRole('radio', { name: '0.8.0 or newer (everything)' }));
  expect(onChange).toHaveBeenLastCalledWith(null);
});

const detailsOf = (container: HTMLElement) => container.querySelector('details') as HTMLDetailsElement;

it('sits behind a closed disclosure by default', () => {
  const { container } = render(<TargetFieldset targets={targets} value={null} onChange={() => {}} />);
  expect(detailsOf(container).open).toBe(false);
  expect(screen.getByText('Opening this in an older TomeStack?').tagName).toBe('SUMMARY');
  expect(detailsOf(container).contains(screen.getByRole('group', { name: 'Which version will open this file?' }))).toBe(true);
});

it('opens the disclosure when an older version is selected', () => {
  const { container } = render(<TargetFieldset targets={targets} value="0.6.0" onChange={() => {}} />);
  expect(detailsOf(container).open).toBe(true);
  expect(screen.getByText('Opening this in an older TomeStack? (0.6.0)').tagName).toBe('SUMMARY');
});

it('opens the disclosure when an older version is picked, and lets the user close it', async () => {
  const onChange = vi.fn();
  const user = userEvent.setup();
  const { container, rerender } = render(<TargetFieldset targets={targets} value={null} onChange={onChange} />);
  await user.click(screen.getByRole('radio', { name: '0.7.0' }));
  expect(onChange).toHaveBeenLastCalledWith('0.7.0');
  rerender(<TargetFieldset targets={targets} value="0.7.0" onChange={onChange} />);
  expect(detailsOf(container).open).toBe(true);
  await user.click(screen.getByText('Opening this in an older TomeStack? (0.7.0)'));
  expect(detailsOf(container).open).toBe(false);
});

const hint = 'Only the version before this one is listed. To open the file in an older TomeStack, update that TomeStack first.';

it('explains that only the previous version is listed', () => {
  render(<TargetFieldset targets={targets} value={null} onChange={() => {}} />);
  expect(screen.getByText(hint)).toBeTruthy();
});

it('renders nothing until the service has answered', () => {
  const { container } = render(<TargetFieldset targets={null} value={null} onChange={() => {}} />);
  expect(container.firstChild).toBeNull();
});

it('says there is no older version when the list is empty', () => {
  const { container } = render(<TargetFieldset targets={{ ...targets, targets: [] }} value={null} onChange={() => {}} />);
  const only = container.firstElementChild;
  expect(container.childElementCount).toBe(1);
  expect(only?.tagName).toBe('P');
  expect(only?.className).toBe('hint');
  expect(only?.textContent).toBe('This TomeStack has no older version to save for.');
  expect(screen.queryAllByRole('radio')).toHaveLength(0);
  expect(screen.queryByRole('group')).toBeNull();
  expect(screen.queryByText(hint)).toBeNull();
});

it('has no axe violations', async () => {
  const { container } = render(<TargetFieldset targets={targets} value={null} onChange={() => {}} />);
  await expectNoAxeViolations(container);
});
