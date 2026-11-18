// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell). Only the transport differs from the desktop
// app: HTTP to loopback instead of the WebView2 bridge, so export takes the download fallback instead of the native Save dialog.
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { fromTemplate, templates } from '../src/templates';
import { expectStatus } from './helpers';

vi.mock('../src/api/client', async (importOriginal) => {
  const { inject } = await import('vitest');
  const { createHttpTransport } = await import('../src/api/transport');
  const original = await importOriginal<typeof import('../src/api/client')>();
  const { endpoint, token } = inject('devHost');
  return { ...original, client: original.createClient(createHttpTransport(endpoint, { 'X-TomeStack-Token': token })) };
});

vi.mock('../src/files', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../src/files')>()),
  downloadBase64: vi.fn(),
}));

afterEach(() => {
  cleanup();
  // One jsdom window serves the whole file: a preference or `html` attribute left by one test would leak into the next.
  // The per-character tab memory stays (ids are unique per test and the deep-link test relies on it).
  for (const key of Object.keys(localStorage)) if (key.startsWith('tomestack.') && !key.startsWith('tomestack.sheetTab.')) localStorage.removeItem(key);
  for (const key of Object.keys(document.documentElement.dataset)) delete document.documentElement.dataset[key];
});

it('finds a problem in a source with the debugger, shows its rule, and clears it in the editor (M5 slice 2)', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Debugger', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Debugger/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Debugger/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Debugger' });

  // A draft feature whose resource nothing spends or recovers.
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New feature' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Dead Well');
  await user.click(within(editor()).getByRole('button', { name: 'Add resource' }));
  await user.type(within(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ })).getByRole('textbox', { name: 'Resource name' }), 'Echoes');
  await user.click(within(editor()).getByRole('button', { name: 'Save draft' }));
  await expectStatus(/Saved a draft of E2E Dead Well/);
  await user.click(within(editor()).getByRole('button', { name: 'Close editor' }));

  await user.click(await screen.findByRole('button', { name: 'Find problems in E2E Debugger' }));
  const findings = await screen.findByRole('region', { name: 'Debugger findings for E2E Debugger' });
  expect(findings.textContent).toMatch(/Warning in E2E Dead Well: Resource 'resource-1' is never spent/);

  // "Show" opens the entry and moves focus to the rule the finding is about.
  await user.click(within(findings).getByRole('button', { name: 'Show rule resource-1 of E2E Dead Well' }));
  await screen.findByRole('heading', { name: 'Edit E2E Dead Well' });
  await waitFor(() => expect(document.activeElement).toBe(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ })));

  // With the entry already open, Show keeps its unsaved edits and moves focus again (review fix).
  const description = within(editor()).getByRole('textbox', { name: /^Description/ });
  await user.type(description, 'Unsaved words');
  await user.click(within(screen.getByRole('region', { name: 'Debugger findings for E2E Debugger' })).getByRole('button', { name: 'Show rule resource-1 of E2E Dead Well' }));
  await waitFor(() => expect(document.activeElement).toBe(within(editor()).getByRole('group', { name: /^Rule 1: Resource/ })));
  expect((within(editor()).getByRole('textbox', { name: /^Description/ }) as HTMLTextAreaElement).value).toBe('Unsaved words');

  // A recovery fixes it; the editor's own debugger checks the unsaved revision.
  await user.click(within(editor()).getByRole('button', { name: 'Add recovery' }));
  await user.click(within(editor()).getByRole('button', { name: 'Find problems' }));
  const own = await within(editor()).findByRole('region', { name: 'Debugger findings' });
  await waitFor(() => expect(own.textContent).toMatch(/The debugger found no problems/));
});

it('tries a draft class at a chosen level on a blank character without saving anything (M5 slice 3)', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Sandbox', ['srd-5.2.1']);
  const charactersBefore = (await client.listCharacters()).length;
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Sandbox/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Sandbox/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Sandbox' });

  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });
  await user.click(screen.getByRole('button', { name: 'New class' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Trial Class');
  await user.selectOptions(within(editor()).getByRole('combobox', { name: 'Hit die' }), 'd10');

  const sandbox = within(editor()).getByRole('region', { name: 'Try it' });
  await user.type(within(sandbox).getByRole('textbox', { name: /^Level in this class/ }), '3');
  await user.click(within(sandbox).getByRole('button', { name: 'Try it' }));
  const results = await within(sandbox).findByRole('region', { name: 'Try it results' });
  expect(results.textContent).toMatch(/Unsaved blank character at total level 3/);
  expect(results.textContent).toMatch(/Hit point maximum: 22/); // d10: 10, then 6 per level, Con +0
  expect(results.textContent).not.toMatch(/Spell save DC/); // not a caster

  // An edit hides the result until it is tried again, and nothing was saved.
  await user.selectOptions(within(editor()).getByRole('combobox', { name: 'Hit die' }), 'd6');
  expect(within(sandbox).queryByRole('region', { name: 'Try it results' })).toBeNull();
  expect((await client.listCharacters()).length).toBe(charactersBefore);
  expect(await client.contentBySource((await client.listSources()).find((s) => s.title === 'E2E Sandbox')!.id)).toEqual([]);
});

it('compares the published revision with the unsaved one by rules, text and on a character copy (M5 slice 4)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Diffs', ['srd-5.2.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Diff Feat',
    rulesFamilies: ['srd-5.2.1'],
    provenance: { sourceId: source.id },
    status: 'draft',
    summary: 'Old line',
    effects: [{ type: 'modifier', id: 'quick', operation: 'bonus', target: 'initiative', value: '1' }],
  });
  const published = (await client.publish(draft)).published;
  const hero = await client.createCharacter({ name: 'E2E Diff Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [published] });

  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Diffs/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Diffs/ }));
  await user.click(await screen.findByRole('button', { name: 'Edit E2E Diff Feat' }));

  const editor = () => screen.getByRole('region', { name: /^Edit / });
  const description = within(editor()).getByRole('textbox', { name: /^Description/ });
  await user.clear(description);
  await user.type(description, 'New line');
  const value = within(within(editor()).getByRole('group', { name: /^Rule 1: Modifier/ })).getByRole('textbox', { name: /^Value/ });
  await user.clear(value);
  await user.type(value, '3');

  const panel = within(editor()).getByRole('region', { name: 'Compare versions' });
  await user.click(await within(panel).findByRole('checkbox', { name: 'E2E Diff Hero' })); // the character list loads after the panel opens
  await user.click(within(panel).getByRole('button', { name: 'Compare' }));
  const results = await within(panel).findByRole('region', { name: 'Comparison results' });
  expect(within(results).getByRole('table', { name: 'Rule changes' }).textContent).toMatch(/quick.*changed/);
  expect(results.textContent).toMatch(/removed: Old line/);
  expect(results.textContent).toMatch(/added: New line/);
  const run = within(results).getByRole('table', { name: 'Calculated values that change for E2E Diff Hero' });
  expect(run.textContent).toMatch(/Initiative\s*1\s*3/);

  // Nothing was applied: the character still uses the published revision.
  expect((await client.getCharacter(hero.character.id)).sheet.fields.find((f) => f.field === 'initiative')?.value).toBe(1);

  // A new entry has nothing stored to compare with until its first save; then the saved draft is "From" (review fix).
  await user.click(within(editor()).getByRole('button', { name: 'Close editor' }));
  await user.click(screen.getByRole('button', { name: 'New feat' }));
  const fresh = () => screen.getByRole('region', { name: /^New / });
  expect(within(fresh()).queryByRole('region', { name: 'Compare versions' })).toBeNull();
  await user.type(within(fresh()).getByRole('textbox', { name: 'Name' }), 'E2E Fresh Feat');
  await user.click(within(fresh()).getByRole('button', { name: 'Save draft' }));
  await expectStatus(/Saved a draft of E2E Fresh Feat/);
  const freshPanel = await within(fresh()).findByRole('region', { name: 'Compare versions' });
  await user.click(within(freshPanel).getByRole('button', { name: 'Compare' }));
  expect((await within(freshPanel).findByRole('region', { name: 'Comparison results' })).textContent).toMatch(/The rules are the same/);
});

it('shows an entry\'s relationships as a keyboard tree and jumps from a node to its rule (M5 slice 5)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Trees', ['srd-5.2.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feature',
    name: 'E2E Tree Feature',
    rulesFamilies: ['srd-5.2.1'],
    provenance: { sourceId: source.id },
    status: 'draft',
    effects: [
      { type: 'resource', id: 'embers', resourceId: 'embers', label: 'Embers', maximum: 'PB' },
      { type: 'roll', id: 'flare', rollId: 'flare', label: 'Flare', dice: '1d6', resourceId: 'embers', timing: 'onRoll', automation: 'assisted' },
      { type: 'recovery', id: 'rekindle', resourceId: 'embers', on: 'longRest', amount: 'all', timing: 'onLongRest' },
    ],
  });
  await client.publish(draft);

  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Trees/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Trees/ }));
  await user.click(await screen.findByRole('button', { name: 'Edit E2E Tree Feature' }));
  const editor = () => screen.getByRole('region', { name: /^Edit / });

  await user.click(within(editor()).getByRole('button', { name: 'Show relationships' }));
  const tree = await within(editor()).findByRole('tree', { name: 'Relationships of E2E Tree Feature' });
  const root = within(tree).getAllByRole('treeitem')[0]!;
  expect(root.getAttribute('aria-level')).toBe('1');
  expect(root.getAttribute('aria-expanded')).toBe('true');
  expect(root.tabIndex).toBe(0);

  // Keyboard only, from the button: Tab enters the tree at its one tab stop (the root).
  await user.tab();
  expect(document.activeElement).toBe(root);
  await user.keyboard('{ArrowDown}');
  const resource = within(tree).getByRole('treeitem', { name: 'Resource: Embers (uses PB)' }); // named by its own label only
  expect(document.activeElement).toBe(resource);
  expect([resource.getAttribute('aria-level'), resource.getAttribute('aria-posinset'), resource.getAttribute('aria-setsize')]).toEqual(['2', '1', '1']);
  expect(resource.getAttribute('aria-expanded')).toBe('false');
  // Space opens and closes; Right opens, then goes to the first child.
  await user.keyboard(' ');
  expect(resource.getAttribute('aria-expanded')).toBe('true');
  await user.keyboard(' ');
  expect(resource.getAttribute('aria-expanded')).toBe('false');
  await user.keyboard('{ArrowRight}');
  expect(resource.getAttribute('aria-expanded')).toBe('true');
  await user.keyboard('{ArrowRight}');
  const roll = within(resource).getByRole('treeitem', { name: 'Roll: Flare (1d6), spends it' });
  expect((document.activeElement as HTMLElement).id).toBe(roll.id);
  expect([roll.getAttribute('aria-level'), roll.getAttribute('aria-posinset'), roll.getAttribute('aria-setsize')]).toEqual(['3', '1', '2']);
  // End goes to the last visible item (the recovery), Home to the root, Up and Down between items.
  await user.keyboard('{End}');
  expect(document.activeElement).toBe(within(resource).getByRole('treeitem', { name: 'Recovery on a long rest: all' }));
  await user.keyboard('{Home}');
  expect(document.activeElement).toBe(root);
  await user.keyboard('{ArrowDown}{ArrowDown}');
  expect((document.activeElement as HTMLElement).id).toBe(roll.id);
  await user.keyboard('{ArrowUp}');
  expect(document.activeElement).toBe(resource);
  await user.keyboard('{ArrowDown}');
  // Enter shows the rule in the editor; Left from a child goes back to its parent.
  await user.keyboard('{Enter}');
  await waitFor(() => expect(document.activeElement).toBe(within(editor()).getByRole('group', { name: /^Rule 2: Roll or action/ })));
  await user.click(within(tree).getByRole('treeitem', { name: 'Roll: Flare (1d6), spends it' }));
  await user.keyboard('{ArrowLeft}');
  expect(document.activeElement).toBe(resource);
});

it('starts homebrew from a template as an unsaved draft, publishes a stance, and a skeleton waits for its slots (M5 slice 6)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Templates', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Templates/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Templates/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Templates' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });

  // The stance: a toggle that spends a use, and a bonus only while it is on. Nothing is stored until it is saved.
  await user.selectOptions(screen.getByRole('combobox', { name: 'Start from a template' }), 'A stance you switch on and off');
  await user.click(screen.getByRole('button', { name: 'Use template' }));
  expect(await client.contentBySource(source.id)).toEqual([]);
  expect(within(editor()).getByRole('group', { name: /^Rule 3: Toggle/ })).toBeTruthy();
  expect((within(within(editor()).getByRole('group', { name: /^Rule 4: Modifier/ })).getByRole('combobox', { name: 'Applies' }) as HTMLSelectElement).value).toBe('stance');
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Stance');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  expect((await within(editor()).findByRole('region', { name: 'Check results' })).textContent).toMatch(/No problems found/);
  await user.click(within(editor()).getByRole('button', { name: 'Make available to characters' }));
  await expectStatus(/Made E2E Stance available to characters/);

  // The class skeleton: its hit die, saves and subclass choice are set; its improvement slots block publishing until filled.
  await user.selectOptions(screen.getByRole('combobox', { name: 'Start from a template' }), 'A class skeleton');
  await user.click(screen.getByRole('button', { name: 'Use template' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Skeleton Class');
  expect((within(editor()).getByRole('combobox', { name: 'Hit die' }) as HTMLSelectElement).value).toBe('8');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  const results = await within(editor()).findByRole('region', { name: 'Check results' });
  expect(results.textContent).toMatch(/improvement-4' grants content but names none/);
  await user.click(within(editor()).getByRole('button', { name: 'Save draft' }));
  await expectStatus(/Saved a draft of E2E Skeleton Class/);
  expect((await client.contentBySource(source.id)).find((e) => e.name === 'E2E Skeleton Class')?.latest.status).toBe('draft');

  // Every template passes the server's checks; only the skeletons' empty slots are errors (review fix).
  for (const t of templates) {
    const draft = { ...fromTemplate(t.id, source.id, ['srd-5.2.1'], crypto.randomUUID()), name: `E2E ${t.label}`, revisionId: crypto.randomUUID() };
    expect(draft.summary).toBeUndefined(); // a template never writes the description shown on the sheet
    const report = await client.validateRevision(draft);
    expect(report.errors.filter((e) => e.code !== 'validate.grant-content-missing')).toEqual([]);
    expect(report.errors.length > 0).toBe(t.id === 'subclass-skeleton' || t.id === 'class-skeleton');
  }

  // Removing the stance's toggle turns its bonus back to always on, so nothing names a missing toggle (review fix).
  await user.selectOptions(screen.getByRole('combobox', { name: 'Start from a template' }), 'A stance you switch on and off');
  await user.click(screen.getByRole('button', { name: 'Use template' }));
  await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Loose Stance');
  await user.click(within(within(editor()).getByRole('group', { name: /^Rule 3: Toggle/ })).getByRole('button', { name: 'Remove rule 3' }));
  // D43 S4: the source's other entries (E2E Stance above) offer their toggles, so the choice stays, and it reads "Always".
  expect((within(editor()).getByRole('combobox', { name: 'Applies' }) as HTMLSelectElement).value).toBe('');
  await user.click(within(editor()).getByRole('button', { name: 'Check' }));
  await waitFor(() => expect(within(editor()).getByRole('region', { name: 'Check results' }).textContent).toMatch(/No problems found/));
});

it('shows design feedback only once it is switched on, as hints that do not block (M5 slice 7)', async () => {
  const user = userEvent.setup();
  await client.createHomebrewSource('E2E Feedback', ['srd-5.2.1']);
  render(<App />);
  const studioButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await waitFor(() => expect(studioButton.disabled).toBe(false));
  await user.click(studioButton);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Feedback/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Feedback/ }));
  await screen.findByRole('heading', { name: 'Content in E2E Feedback' });
  const editor = () => screen.getByRole('region', { name: /^New |^Edit / });

  // Off by default: the editor has no feedback section.
  const toggle = screen.getByRole('checkbox', { name: 'Show design feedback' });
  expect((toggle as HTMLInputElement).checked).toBe(false);
  await user.click(screen.getByRole('button', { name: 'New class' }));
  expect(within(editor()).queryByRole('region', { name: 'Design feedback' })).toBeNull();

  try {
    await user.click(toggle);
    expect(localStorage.getItem('tomestack.designFeedback')).toBe('on');
    const section = within(editor()).getByRole('region', { name: 'Design feedback' });
    await user.type(within(editor()).getByRole('textbox', { name: 'Name' }), 'E2E Hinted Class');
    await user.click(within(section).getByRole('button', { name: 'Get design hints' }));
    const hints = await within(section).findByRole('region', { name: 'Design hints' });
    expect(hints.textContent).toMatch(/Class level\(s\) .* give no feature or choice, although every bundled SRD class gives one there/);

    // The choice is remembered for next time (this machine only): a fresh studio starts with it on.
    cleanup();
    render(<App />);
    const again = await screen.findByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
    await waitFor(() => expect(again.disabled).toBe(false));
    await user.click(again);
    expect((await screen.findByRole<HTMLInputElement>('checkbox', { name: 'Show design feedback' })).checked).toBe(true);

    // Off again: the choice is forgotten.
    await user.click(screen.getByRole('checkbox', { name: 'Show design feedback' }));
    expect(localStorage.getItem('tomestack.designFeedback')).toBeNull();
  } finally {
    localStorage.removeItem('tomestack.designFeedback'); // later flows start from the default, off
  }
});
