// 4.1: axe in the installed Edge, with colour contrast on, against the real DevHost. Runs once per colour scheme (see
// vitest.a11y.config.ts). The page never holds the token: `/api/command` goes to the Vite middleware in a11y/devhostProxy.ts.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../src/styles.css';
import { App } from '../src/App';
import { startNewCharacter } from '../e2e/helpers';
import { expectNoSeriousAxe } from './axe';

vi.mock('../src/api/client', async (importOriginal) => {
  const { createHttpTransport } = await import('../src/api/transport');
  const original = await importOriginal<typeof import('../src/api/client')>();
  return { ...original, client: original.createClient(createHttpTransport('/api/command', {})) };
});

// Every screen of a test is checked before the test fails, so one run reports all violations, not just the first.
const violations: string[] = [];

afterEach(() => {
  cleanup();
  for (const key of Object.keys(localStorage)) if (key.startsWith('tomestack.') && !key.startsWith('tomestack.sheetTab.')) localStorage.removeItem(key);
  for (const key of Object.keys(document.documentElement.dataset)) delete document.documentElement.dataset[key];
  const found = violations.splice(0);
  if (found.length > 0) throw new Error(found.join('\n'));
});

/** Waits for the screen's h1 and for every "Loading" placeholder to go, so axe never sees a half-drawn page. */
async function settled(): Promise<void> {
  await screen.findByRole('heading', { level: 1 });
  await waitFor(() => expect(document.body.textContent).not.toMatch(/Loading/i));
}

async function axeScreen(label: string): Promise<void> {
  await settled();
  try {
    await expectNoSeriousAxe(document.body, `${scheme()} / ${label}`);
  } catch (error) {
    violations.push(error instanceof Error ? error.message : String(error));
  }
}

function scheme(): string {
  return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

async function stepHeading(title: string): Promise<void> {
  await waitFor(() => expect(document.getElementById('builder-step-heading')?.textContent).toBe(title));
}

/** Seeds one character through the UI, the way e2e/core.e2e.tsx does, and returns to home. */
async function seedCharacter(user: ReturnType<typeof userEvent.setup>, name: string): Promise<void> {
  await startNewCharacter(user);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), name);
  await user.click(screen.getByRole('radio', { name: /2014 rules/ }));
  await user.click(screen.getByRole('button', { name: 'Next: class' }));
  await user.click(await screen.findByRole('button', { name: 'Next: ability scores' }));
  await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
  await user.click(screen.getByRole('button', { name: 'Next: species' }));
  await user.click(await screen.findByRole('radio', { name: /^Fixture Quickfoot/ }));
  await user.click(screen.getByRole('button', { name: 'Next: background' }));
  await user.click(screen.getByRole('button', { name: 'Next: gear' }));
  await user.click(screen.getByRole('button', { name: /^Skip: I'll add gear myself/ }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  await screen.findByRole('article', { name });
}

/** Tabs from the top of the page, as a keyboard user does, so :focus-visible matches without a heuristic. */
async function focusSkipLink(user: ReturnType<typeof userEvent.setup>): Promise<HTMLAnchorElement> {
  await user.tab();
  const link = document.querySelector<HTMLAnchorElement>('a.skip-link');
  expect(link).not.toBeNull();
  expect(document.activeElement).toBe(link);
  return link!;
}

/** Off-screen, axe skips the skip link, so it is checked while focused (and so on screen). */
async function axeFocusedSkipLink(user: ReturnType<typeof userEvent.setup>, label: string): Promise<void> {
  const link = await focusSkipLink(user);
  try {
    await expectNoSeriousAxe(link, `${scheme()} / ${label}`);
  } catch (error) {
    violations.push(error instanceof Error ? error.message : String(error));
  }
}

describe('every screen, default theme', () => {
  it('home with no characters', async () => {
    const user = userEvent.setup();
    render(<App />);
    await axeScreen('home with no characters');
    await axeFocusedSkipLink(user, 'skip link (default)');
  });

  it('builder, each step', async () => {
    const user = userEvent.setup();
    render(<App />);
    await startNewCharacter(user);
    await axeScreen('builder: rules family');
    await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'A11y Pell');
    await user.click(screen.getByRole('radio', { name: /2014 rules/ }));
    await axeScreen('builder: rules family, 2014 picked');
    await user.click(screen.getByRole('button', { name: 'Next: class' }));
    await stepHeading('Class');
    await axeScreen('builder: class');
    await user.click(await screen.findByRole('button', { name: 'Next: ability scores' }));
    await stepHeading('Ability scores');
    await axeScreen('builder: ability scores');
    await user.click(screen.getByRole('radio', { name: 'Enter by hand' }));
    await user.click(screen.getByRole('button', { name: 'Next: species' }));
    await stepHeading('Species');
    await screen.findByRole('radio', { name: /^Fixture Quickfoot/ });
    await axeScreen('builder: species');
    await user.click(screen.getByRole('button', { name: 'Next: background' }));
    await stepHeading('Background');
    await axeScreen('builder: background');
    await user.click(screen.getByRole('button', { name: 'Next: gear' }));
    await stepHeading('Starting gear');
    await axeScreen('builder: gear');
    await user.click(screen.getByRole('button', { name: /^Skip: I'll add gear myself/ }));
    await user.click(screen.getByRole('button', { name: 'Next: choices' }));
    await screen.findByRole('button', { name: 'Create and save' });
    await axeScreen('builder: choices');
  });

  it('sheet, each tab', async () => {
    const user = userEvent.setup();
    render(<App />);
    await seedCharacter(user, 'A11y Sheet');
    const sheet = await screen.findByRole('article', { name: 'A11y Sheet' });
    const tabs = within(sheet).getAllByRole('tab');
    expect(tabs.length).toBeGreaterThan(1);
    for (const tab of tabs) {
      const name = tab.textContent?.trim() ?? 'tab';
      await user.click(tab);
      await within(sheet).findByRole('tabpanel');
      await axeScreen(`sheet: ${name}`);
    }
  });

  it('home with one character', async () => {
    const user = userEvent.setup();
    render(<App />);
    await seedCharacter(user, 'A11y Home');
    await user.click(screen.getByRole('button', { name: 'TomeStack, your characters' }));
    await waitFor(() => expect(screen.queryAllByRole('tab')).toHaveLength(0));
    await screen.findByRole('button', { name: 'Open A11y Home' });
    await axeScreen('home with characters');
  });
});

describe('home in each named theme', () => {
  it.each(['cool', 'violet', 'ember', 'rose', 'tide'])('%s', async (theme) => {
    localStorage.setItem('tomestack.theme', theme); // the key settings.ts reads; App's mount effect applies it
    const user = userEvent.setup();
    render(<App />);
    await settled();
    expect(document.documentElement.dataset.theme).toBe(theme);
    await axeScreen(`home, theme ${theme}`);
    await axeFocusedSkipLink(user, `skip link (${theme})`);
  });
});

describe('focus styles in a real browser', () => {
  it('the skip link shows an outline when focused', async () => {
    const user = userEvent.setup();
    render(<App />);
    await settled();
    const link = await focusSkipLink(user);
    expect(getComputedStyle(link).outlineStyle).not.toBe('none');
  });

  it('main takes focus with no outline, even with "always show focus"', async () => {
    localStorage.setItem('tomestack.focus', 'always'); // the key settings.ts reads
    const user = userEvent.setup();
    render(<App />);
    await settled();
    expect(document.documentElement.dataset.focus).toBe('always');
    const link = await focusSkipLink(user);
    expect(getComputedStyle(link).outlineStyle).not.toBe('none');
    const main = document.querySelector<HTMLElement>('main#main-content');
    expect(main).not.toBeNull();
    main!.focus();
    expect(document.activeElement).toBe(main);
    expect(getComputedStyle(main!).outlineStyle).toBe('none');
  });
});
