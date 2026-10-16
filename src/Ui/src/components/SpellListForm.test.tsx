// @vitest-environment jsdom
// Item 10: the key form shared by "Add to spell list…" on candidates and on studio spells.
import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import { SpellListForm } from './SpellListForm';

vi.mock('../api/client', () => ({ client: { spellListKeys: vi.fn() } }));

beforeEach(() => {
  vi.mocked(client.spellListKeys).mockResolvedValue([
    { key: 'fixture-seer', spells: 3 },
    { key: 'wizard', spells: 214 },
  ]);
});
afterEach(cleanup);

function renderForm(over: Partial<Parameters<typeof SpellListForm>[0]> = {}) {
  const props = { subject: '2 spells', onSubmit: vi.fn().mockResolvedValue(undefined), onCancel: vi.fn(), onError: vi.fn(), ...over };
  render(<SpellListForm {...props} />);
  return props;
}

const held = (el: HTMLElement) => el.getAttribute('aria-disabled') === 'true';

it('offers the existing keys with their spell counts, and takes focus on the key box', async () => {
  renderForm();
  const input = screen.getByRole('combobox', { name: 'Spell list key' });
  expect(document.activeElement).toBe(input);
  await vi.waitFor(() => expect(document.querySelectorAll('datalist option')).toHaveLength(2));
  const options = [...document.querySelectorAll('datalist option')].map((o) => [o.getAttribute('value'), o.textContent]);
  expect(options).toEqual([
    ['fixture-seer', 'fixture-seer (3 spells)'],
    ['wizard', 'wizard (214 spells)'],
  ]);
  expect(input.getAttribute('list')).toBe(document.querySelector('datalist')!.id);
});

it('shows the normalised key before saving and sends that key', async () => {
  const user = userEvent.setup();
  const props = renderForm();
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), '  Fixture   Seer ');
  expect(screen.getByText('Saved as: fixture-seer')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Add 2 spells to this list' }));
  expect(props.onSubmit).toHaveBeenCalledExactlyOnceWith('fixture-seer');
});

it('says whether the key matches a list in use or is new, so a typo is noticed', async () => {
  const user = userEvent.setup();
  renderForm();
  await vi.waitFor(() => expect(document.querySelectorAll('datalist option')).toHaveLength(2));
  const input = screen.getByRole('combobox', { name: 'Spell list key' });
  await user.type(input, 'Wizard');
  expect(screen.getByText('A list with this key is already in use by 214 spells.')).toBeTruthy();
  await user.clear(input);
  await user.type(input, 'wizzard');
  expect(screen.getByText('No list has this key yet. A caster uses it only if its class names exactly this key.')).toBeTruthy();
});

it('holds the button, with a linked visible reason, for an empty or invalid key, and keeps focus', async () => {
  const user = userEvent.setup();
  const props = renderForm();
  const button = screen.getByRole('button', { name: 'Add 2 spells to this list' });
  expect(held(button)).toBe(true);
  expect(button).toHaveProperty('disabled', false);
  expect(document.getElementById(button.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Type a spell list key.');
  await user.click(button);
  expect(props.onSubmit).not.toHaveBeenCalled();

  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), "Seer's");
  expect(document.getElementById(button.getAttribute('aria-describedby') ?? '')?.textContent).toBe('A spell list key is 1 to 60 letters, digits or hyphens.');
  expect(screen.queryByText(/^Saved as:/)).toBeNull();
  button.focus();
  await user.click(button);
  expect(document.activeElement).toBe(button);
  expect(props.onSubmit).not.toHaveBeenCalled();
});

it('holds the button for the reason the panel gives (an unsaved edit, a bulk accept)', async () => {
  const user = userEvent.setup();
  const props = renderForm({ held: true, heldReasonId: 'unsaved-1' });
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  const button = screen.getByRole('button', { name: 'Add 2 spells to this list' });
  expect(held(button)).toBe(true);
  expect(button.getAttribute('aria-describedby')).toBe('unsaved-1');
  await user.click(button);
  expect(props.onSubmit).not.toHaveBeenCalled();
});

it('ignores a second press while the first is in flight', async () => {
  const user = userEvent.setup();
  let finish: () => void = () => {};
  const props = renderForm({ onSubmit: vi.fn().mockReturnValue(new Promise<void>((resolve) => (finish = resolve))) });
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  const button = screen.getByRole('button', { name: 'Add 2 spells to this list' });
  await user.click(button);
  await user.click(button);
  expect(props.onSubmit).toHaveBeenCalledTimes(1);
  expect(held(button)).toBe(true);
  await act(async () => finish());
});

it('reads the keys once even when the parent passes a new onError on every render', async () => {
  const user = userEvent.setup();
  vi.mocked(client.spellListKeys).mockClear();
  const view = render(<SpellListForm subject="2 spells" onSubmit={vi.fn()} onCancel={vi.fn()} onError={() => {}} />);
  await vi.waitFor(() => expect(document.querySelectorAll('datalist option')).toHaveLength(2));
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wiz');
  view.rerender(<SpellListForm subject="2 spells" onSubmit={vi.fn()} onCancel={vi.fn()} onError={() => {}} />);
  view.rerender(<SpellListForm subject="3 spells" onSubmit={vi.fn()} onCancel={vi.fn()} onError={() => {}} />);
  expect(client.spellListKeys).toHaveBeenCalledTimes(1);
  expect((screen.getByRole('combobox', { name: 'Spell list key' }) as HTMLInputElement).value).toBe('wiz');
});

it('cancels, and reports a failed key list without blocking the form', async () => {
  const user = userEvent.setup();
  vi.mocked(client.spellListKeys).mockImplementation(() => Promise.reject(new Error('no')));
  const props = renderForm();
  await vi.waitFor(() => expect(props.onError).toHaveBeenCalled());
  await user.type(screen.getByRole('combobox', { name: 'Spell list key' }), 'wizard');
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(props.onCancel).toHaveBeenCalledTimes(1);
});
