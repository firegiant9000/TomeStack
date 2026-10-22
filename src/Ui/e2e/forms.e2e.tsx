// D43 slice 1 against the real DevHost: form groups authored in the studio, the form picker on Play, and a refused switch.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';

vi.mock('../src/api/client', async (importOriginal) => {
  const { inject } = await import('vitest');
  const { createHttpTransport } = await import('../src/api/transport');
  const original = await importOriginal<typeof import('../src/api/client')>();
  const { endpoint, token } = inject('devHost');
  return { ...original, client: original.createClient(createHttpTransport(endpoint, { 'X-TomeStack-Token': token })) };
});

afterEach(cleanup);

/** Waits until the status line says `text` (the line stays on screen, so a plain find would see the previous message). */
async function expectStatus(text: RegExp): Promise<HTMLElement> {
  await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(text));
  return screen.getByRole('status');
}

/** Activates one tab of a character sheet (ADR-014); inactive panels are hidden. A no-op when it is already selected. */
async function openTab(user: ReturnType<typeof userEvent.setup>, sheet: HTMLElement, name: string): Promise<void> {
  const tab = within(sheet).getByRole('tab', { name });
  if (tab.getAttribute('aria-selected') !== 'true') await user.click(tab);
}

it('authors a form group and two forms in the studio, switches between them on Play, and refuses a switch without enough pool', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Forms Book', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew studio' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Homebrew source' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Forms Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Forms Book/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Forms Book' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  const rule = (name: RegExp) => within(within(editor()).getByRole('group', { name }));

  // A pool of 7 Focus, a group, and two forms that cost 3 each: two switches fit, a third does not.
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Forms');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(rule(/^Rule 1: Resource/).getByRole('textbox', { name: 'Resource name' }), 'Focus');
  const uses = rule(/^Rule 1: Resource/).getByRole('textbox', { name: /^Uses/ });
  await user.clear(uses);
  await user.type(uses, '7');
  await user.click(within(editor()).getByRole('button', { name: 'Add form group' }));
  await user.type(rule(/^Rule 2: Form group/).getByRole('textbox', { name: /^Group name/ }), 'Forms');
  for (const [number, name] of [
    [3, 'E2E Tide'],
    [4, 'E2E Ember'],
  ] as const) {
    await user.click(within(editor()).getByRole('button', { name: 'Add toggle' }));
    const toggle = rule(new RegExp(`^Rule ${number}: Toggle`));
    await user.type(toggle.getByRole('textbox', { name: 'Toggle name' }), name);
    await user.selectOptions(toggle.getByRole('combobox', { name: 'Turning it on spends' }), 'One use of Focus');
    await user.type(toggle.getByRole('textbox', { name: /^Cost/ }), '3');
    await user.selectOptions(toggle.getByRole('combobox', { name: 'Form group' }), 'Forms');
  }
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Publish' }));
  await expectStatus(/Published E2E Forms/);

  const published = (await client.contentBySource(source.id)).find((e) => e.name === 'E2E Forms')?.latestPublished;
  expect(published).toBeTruthy();
  await client.createCharacter({
    name: 'E2E Forms Hero',
    rulesFamily: 'srd-5.2.1',
    baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 },
    pins: [published!],
  });

  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Forms Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Forms Hero' });
  await openTab(user, sheet, 'Play');
  const actions = () => screen.getByRole('region', { name: 'Attacks and actions' });
  // The group is named by the label authored in the studio (the sheet's groupLabel).
  const picker = () => within(actions()).getByRole('radiogroup', { name: 'Forms' });
  const form = (name: RegExp) => within(picker()).getByRole('radio', { name });
  const resources = () => screen.getByRole('region', { name: 'Resources' });

  // Base is chosen at first; each form names its cost and the pool it comes from.
  expect(within(picker()).getByRole('radio', { name: 'Base' }).getAttribute('aria-checked')).toBe('true');
  expect(form(/^E2E Tide.*3 Focus/)).toBeTruthy();

  await user.click(form(/^E2E Tide/));
  await waitFor(() => expect(form(/^E2E Tide/).getAttribute('aria-checked')).toBe('true'));
  expect(within(resources()).getByRole('heading', { name: 'Focus: 4 of 7' })).toBeTruthy();

  // Choosing the other form switches the first off and the second on as one change; the second spends its own cost.
  await user.click(form(/^E2E Ember/));
  await waitFor(() => expect(form(/^E2E Ember/).getAttribute('aria-checked')).toBe('true'));
  expect(form(/^E2E Tide/).getAttribute('aria-checked')).toBe('false');
  expect(within(resources()).getByRole('heading', { name: 'Focus: 1 of 7' })).toBeTruthy();

  // 1 Focus is left and Tide costs 3: the switch is refused and nothing changes.
  await user.click(form(/^E2E Tide/));
  expect((await screen.findByRole('alert')).textContent).toMatch(/1 of 7 left/);
  expect(form(/^E2E Ember/).getAttribute('aria-checked')).toBe('true');
  expect(form(/^E2E Tide/).getAttribute('aria-checked')).toBe('false');
  expect(within(resources()).getByRole('heading', { name: 'Focus: 1 of 7' })).toBeTruthy();
});
