// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../test/axe';
import { AddCharacterPanel } from './AddCharacterPanel';

afterEach(cleanup);

function setup(props: Partial<Parameters<typeof AddCharacterPanel>[0]> = {}) {
  const handlers = { onNew: vi.fn(), onSheetImport: vi.fn(), onFileImport: vi.fn(), onTrySample: vi.fn() };
  const utils = render(<AddCharacterPanel canCreate {...handlers} {...props} />);
  return { ...utils, handlers, user: userEvent.setup() };
}

const labels: [string, 'onNew' | 'onTrySample' | 'onSheetImport' | 'onFileImport'][] = [
  ['Make a new one', 'onNew'],
  ['Try the sample', 'onTrySample'],
  ['From a character sheet (PDF or text)', 'onSheetImport'],
  ['From a TomeStack file', 'onFileImport'],
];

for (const [label, handler] of labels) {
  it(`choosing "${label}" then Continue calls ${handler}`, async () => {
    const { handlers, user } = setup();
    await user.click(screen.getByRole('radio', { name: label }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(handlers[handler]).toHaveBeenCalledTimes(1);
    for (const other of Object.keys(handlers)) if (other !== handler) expect(handlers[other as keyof typeof handlers]).not.toHaveBeenCalled();
  });
}

it('has no sample choice without onTrySample', () => {
  setup({ onTrySample: undefined });
  expect(screen.queryByRole('radio', { name: 'Try the sample' })).toBeNull();
  expect(screen.getAllByRole('radio')).toHaveLength(3);
});

it('each radio is described by its description text', () => {
  setup();
  expect(screen.getByRole('radio', { name: 'Make a new one' }).getAttribute('aria-describedby')).toBeTruthy();
  const described = (name: string) => {
    const id = screen.getByRole('radio', { name }).getAttribute('aria-describedby')!;
    return document.getElementById(id)?.textContent;
  };
  expect(described('Make a new one')).toBe('Step by step: rules, class, abilities, species, background, gear, choices.');
  expect(described('Try the sample')).toBe('A ready-made 3rd-level cleric to explore.');
  expect(described('From a character sheet (PDF or text)')).toBe('A PDF or pasted text. TomeStack reads the sheet and keeps nothing of the file.');
  expect(described('From a TomeStack file')).toBe('A .zip someone exported from TomeStack.');
});

it('Continue does nothing until a choice is made', async () => {
  const { handlers, user } = setup();
  const go = screen.getByRole('button', { name: 'Continue' });
  expect(go.hasAttribute('disabled') || go.getAttribute('aria-disabled') === 'true').toBe(true);
  await user.click(go);
  for (const h of Object.values(handlers)) expect(h).not.toHaveBeenCalled();
});

it('start="import" preselects the TomeStack file choice', async () => {
  const { handlers, user } = setup({ start: 'import' });
  expect((screen.getByRole('radio', { name: 'From a TomeStack file' }) as HTMLInputElement).checked).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  expect(handlers.onFileImport).toHaveBeenCalledTimes(1);
});

it('canCreate false disables Make a new one with a visible reason, other choices still work', async () => {
  const { handlers, user } = setup({ canCreate: false });
  await user.click(screen.getByRole('radio', { name: 'From a TomeStack file' }));
  expect(screen.getByText(/still loading/i)).toBeTruthy();
  const make = screen.getByRole('radio', { name: 'Make a new one' }) as HTMLInputElement;
  expect(make.disabled).toBe(true);
  const sheet = screen.getByRole('radio', { name: 'From a character sheet (PDF or text)' }) as HTMLInputElement;
  expect(sheet.disabled).toBe(true);
  const reason = screen.getByText(/still loading/i);
  expect(reason.id).not.toBe('');
  for (const radio of [make, sheet]) {
    expect((radio.getAttribute('aria-describedby') ?? '').split(' ')).toContain(reason.id);
  }
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  expect(handlers.onFileImport).toHaveBeenCalledTimes(1);
  expect(handlers.onNew).not.toHaveBeenCalled();
});

it('arrow keys move within the group and Tab reaches Continue', async () => {
  // user-event walks a radio group with CSS.escape, which jsdom lacks; the group name needs no escaping.
  const w = window as unknown as { CSS?: { escape?: (s: string) => string } };
  w.CSS ??= {};
  w.CSS.escape ??= (s: string) => s;
  const { user } = setup();
  await user.click(screen.getByRole('radio', { name: 'Make a new one' }));
  await user.keyboard('{ArrowDown}');
  expect((screen.getByRole('radio', { name: 'Try the sample' }) as HTMLInputElement).checked).toBe(true);
  expect(document.activeElement).toBe(screen.getByRole('radio', { name: 'Try the sample' }));
  await user.keyboard('{ArrowUp}');
  expect(document.activeElement).toBe(screen.getByRole('radio', { name: 'Make a new one' }));
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Continue' }));
});

it('has no axe violations', async () => {
  const { container, user } = setup();
  await expectNoAxeViolations(container);
  await user.click(screen.getByRole('radio', { name: 'Make a new one' }));
  await expectNoAxeViolations(container);
});

it('has no axe violations when creating is unavailable', async () => {
  const { container } = setup({ canCreate: false });
  await expectNoAxeViolations(container);
});
