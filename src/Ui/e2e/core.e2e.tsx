// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell). Only the transport differs from the desktop
// app: HTTP to loopback instead of the WebView2 bridge, so export takes the download fallback instead of the native Save dialog.
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { readZipEntry } from './zip';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { downloadBase64 } from '../src/files';
import { bytesOf, createCharacter, expectStatus, goHome, openMore, openTab, startNewCharacter } from './helpers';

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

it('creates a character, shows its traced sheet, overrides, exports and re-imports it', async () => {
  const user = userEvent.setup();
  render(<App />);

  // Create (the default scores: Dex 14)
  await createCharacter(user, { name: 'E2E Pell', family: 'srd-5.1', scores: {}, species: /^Fixture Quickfoot/ });
  expect(await screen.findByText('Nothing to choose at this level.')).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Create and save' }));

  // Sheet with a source-aware trace: Dex 14 + 2 (Fixture Quickfoot, species under 2014 rules) = 16 -> +3
  const sheet = await screen.findByRole('article', { name: 'E2E Pell' });
  await waitFor(() => expect(document.activeElement).toBe(within(sheet).getByRole('heading', { level: 1, name: 'E2E Pell' })));
  await openTab(user, sheet, 'Skills and saves');
  const initiative = within(sheet).getByRole('region', { name: /^Initiative:/ });
  expect(within(initiative).getByRole('heading').textContent).toContain('+3');
  expect(within(sheet).getByRole('heading', { name: /^Dexterity score: 16/ })).toBeTruthy();
  await user.click(within(initiative).getByRole('heading')); // expand the field (summary)
  const trace = within(initiative).getByRole('table', { name: 'How initiative is calculated' });
  expect(within(trace).getByText(/TomeStack Fixtures: 2014 Family, p\. 1/)).toBeTruthy();

  // Override (labeled, keeps the computed value)
  await user.type(within(initiative).getByRole('spinbutton', { name: 'Number to set by hand' }), '9');
  await user.type(within(initiative).getByRole('textbox', { name: 'Reason (optional)' }), 'Table ruling');
  await user.click(within(initiative).getByRole('button', { name: 'Set by hand' }));
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Initiative:/ }).textContent).toContain('set by hand (calculated +3)'));

  // D22: a session note on Notes is the player's journal; the share preview below must not list it.
  await openTab(user, sheet, 'Journal');
  const journal = within(sheet).getByRole('form', { name: 'New session note' });
  await user.type(within(journal).getByRole('textbox', { name: 'Note' }), 'Private session note for the share test.');
  await user.click(within(journal).getByRole('button', { name: 'Save session note' }));
  await within(sheet).findByText('Private session note for the share test.');

  // Item 17, D36: the Details form on Notes. Save is held until something changes; the details then show on Stats.
  const detailsForm = within(sheet).getByRole('form', { name: 'Details' });
  const saveDetails = within(detailsForm).getByRole('button', { name: 'Save details' });
  expect(saveDetails.getAttribute('aria-disabled')).toBe('true');
  await user.type(within(detailsForm).getByRole('textbox', { name: 'Alignment' }), 'E2E neutral');
  await user.type(within(detailsForm).getByRole('textbox', { name: 'Languages' }), 'E2E tongue{Enter}E2E signs');
  expect(saveDetails.getAttribute('aria-disabled')).toBeNull();
  await user.click(saveDetails);
  await expectStatus(/Details saved/);
  expect(saveDetails.getAttribute('aria-disabled')).toBe('true'); // nothing differs now
  expect(document.activeElement).toBe(saveDetails); // focus stays on the button
  await openTab(user, sheet, 'Skills and saves');
  expect(within(within(sheet).getByRole('list', { name: 'Languages' })).getAllByRole('listitem').map((li) => li.textContent)).toEqual(['E2E tongue', 'E2E signs']);
  await user.click(within(sheet).getByRole('button', { name: 'Edit in Journal → Details' }));
  expect(within(sheet).getByRole('tab', { name: 'Journal' }).getAttribute('aria-selected')).toBe('true');
  await waitFor(() => expect(document.activeElement).toBe(within(sheet).getByRole('heading', { name: 'Details' })));

  // Export (DevHost has no native dialog: package.saveAs -> unsupported -> download fallback)
  await openMore(user, sheet, 'Export…');
  await user.click(screen.getByRole('button', { name: 'Export TomeStack file' }));
  await waitFor(() => expect(downloadBase64).toHaveBeenCalledTimes(1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[0]!;
  expect(fileName).toBe('E2E-Pell-personal-backup.tomestack.zip'); // default purpose: personal backup (ADR-007)

  // Share: the preview says what is left out (nothing here: the fixture sources may be shared), then exports.
  await user.click(screen.getByRole('radio', { name: /Share with someone/ }));
  const leftOut = await screen.findByRole('region', { name: 'Left out of the shared TomeStack file' });
  expect(leftOut.textContent).toMatch(/Nothing is left out/);
  expect(leftOut.textContent).not.toMatch(/session note/i);
  // L5: the one sharing rule's list is there, and Export waits for it.
  const shareCheck = await screen.findByRole('region', { name: 'Preflight: shared TomeStack file' });
  expect(shareCheck.textContent).toMatch(/Nothing is left out/);
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Export TomeStack file' }).disabled).toBe(false));
  await user.click(screen.getByRole('button', { name: 'Export TomeStack file' }));
  await waitFor(() => expect(downloadBase64).toHaveBeenCalledTimes(2));
  expect(vi.mocked(downloadBase64).mock.calls[1]![0]).toBe('E2E-Pell.tomestack.zip');
  // D36 in the files themselves: the backup holds the details and the journal; the share keeps the details and drops the journal.
  const characterEntry = /^characters\/.+\.json$/;
  const backupCharacter = readZipEntry(bytesOf(base64), characterEntry)!;
  expect(backupCharacter).toContain('E2E tongue');
  expect(backupCharacter).toContain('Private session note for the share test.');
  const sharedCharacter = readZipEntry(bytesOf(vi.mocked(downloadBase64).mock.calls[1]![1]), characterEntry)!;
  expect(sharedCharacter).toContain('E2E tongue');
  expect(sharedCharacter).not.toContain('Private session note for the share test.');

  // Import: preview first, then apply. The character already exists, so the local copy is backed up.
  const file = new File([bytesOf(base64)], fileName, { type: 'application/zip' });
  await user.upload(screen.getByLabelText('TomeStack file'), file);
  const preview = await screen.findByRole('region', { name: 'Import a TomeStack file' });
  expect(within(preview).getByText(/already exists and will be replaced/)).toBeTruthy();
  await user.click(within(preview).getByRole('button', { name: 'Apply import' }));

  const status = await expectStatus(/1 replaced/);
  expect(status.getAttribute('role')).toBe('status');
  expect(status.textContent).toMatch(/backed up to backups\/pre-import-/);
  await openTab(user, await screen.findByRole('article', { name: 'E2E Pell' }), 'Skills and saves');
  await waitFor(() => expect(screen.getByRole('heading', { name: /^Initiative:/ }).textContent).toContain('set by hand (calculated +3)'));
});

it('takes a snapshot of a character, previews the restore, restores it and keeps an undo snapshot (M5 slice 8)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Snapshots', ['srd-5.2.1']);
  const feat = (
    await client.publish(
      await client.saveDraft({
        contentId: crypto.randomUUID(),
        revisionId: '00000000-0000-0000-0000-000000000000',
        kind: 'feat',
        name: 'E2E Snapshot Feat',
        rulesFamilies: ['srd-5.2.1'],
        provenance: { sourceId: source.id },
        status: 'draft',
        effects: [{ type: 'modifier', id: 'quick', operation: 'bonus', target: 'initiative', value: '3' }],
      }),
    )
  ).published;
  const created = (await client.createCharacter({ name: 'E2E Snapshot Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [feat] })).character;
  const hero = (await client.saveCharacter({ ...created, currency: { cp: 0, sp: 12, ep: 0, gp: 3, pp: 0 } })).character; // D21: coins the restore puts back

  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Snapshot Hero/ }));
  await openMore(user, await screen.findByRole('article', { name: 'E2E Snapshot Hero' }), 'Snapshots');
  const panel = () => screen.getByRole('region', { name: 'Snapshots' });
  await user.type(await within(await screen.findByRole('region', { name: 'Snapshots' })).findByRole('textbox', { name: /^Snapshot name/ }), 'E2E with the feat');
  await user.click(within(panel()).getByRole('button', { name: 'Take snapshot' }));
  await expectStatus(/Took a snapshot of E2E Snapshot Hero: E2E with the feat/);

  // The character changes: the feat goes, and the coins change.
  await client.saveCharacter({ ...(await client.getCharacter(hero.id)).character, pins: [], currency: { cp: 0, sp: 0, ep: 0, gp: 40, pp: 0 } });
  // Leave the character on a real tab (not the Play default), so the reopen below proves the tab is remembered.
  await openTab(user, await screen.findByRole('article', { name: 'E2E Snapshot Hero' }), 'Features');
  cleanup();
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Snapshot Hero/ }));

  // ADR-014: the sheet reopens on the tab used last for this character. (The retired "manage" id falling back to Play is covered
  // by sheetTab.test.ts and CharacterSheet.test.tsx.) The Snapshots panel is not kept open, so it is opened again from More.
  const reopened = await screen.findByRole('article', { name: 'E2E Snapshot Hero' });
  expect(within(reopened).getByRole('tab', { name: 'Features' }).getAttribute('aria-selected')).toBe('true');
  await openMore(user, reopened, 'Snapshots');
  await user.click(await within(await screen.findByRole('region', { name: 'Snapshots' })).findByRole('button', { name: 'Restore E2E with the feat…' }));
  const preview = await within(panel()).findByRole('region', { name: 'Restore E2E with the feat?' });
  await waitFor(() => expect(document.activeElement).toBe(within(preview).getByRole('heading', { name: 'Restore E2E with the feat?' })));
  expect(within(preview).getByRole('table', { name: 'Calculated values that change' }).textContent).toMatch(/Initiative\s*0\s*3/);
  // D22: the restore preview names the coin change, through the service's real field names.
  expect(preview.textContent).toMatch(/Coins go back to 3 gp, 12 sp \(now 40 gp\)\./);
  expect((await client.getCharacter(hero.id)).character.pins).toEqual([]); // the preview changed nothing

  // "Keep the current state" closes the preview and returns focus to the button that opened it (accessibility item 23).
  await user.click(within(preview).getByRole('button', { name: 'Keep the current state' }));
  const reopen = within(panel()).getByRole('button', { name: 'Restore E2E with the feat…' });
  await waitFor(() => expect(document.activeElement).toBe(reopen));
  expect(within(panel()).queryByRole('region', { name: 'Restore E2E with the feat?' })).toBeNull();
  expect((await client.getCharacter(hero.id)).character.pins).toEqual([]);

  await user.click(reopen);
  const again = await within(panel()).findByRole('region', { name: 'Restore E2E with the feat?' });
  await user.click(within(again).getByRole('button', { name: 'Restore' }));
  await expectStatus(/Restored the snapshot/);
  expect((await client.getCharacter(hero.id)).character.pins).toEqual([feat]);
  expect((await client.getCharacter(hero.id)).character.currency).toEqual({ cp: 0, sp: 12, ep: 0, gp: 3, pp: 0 });
  expect(await within(panel()).findByRole('button', { name: /^Restore Before restoring/ })).toBeTruthy(); // the undo snapshot
});

it('reaches the primary actions by keyboard alone', async () => {
  const user = userEvent.setup();
  render(<App />);
  // user-event walks a radio group with CSS.escape, which jsdom lacks; the group name needs no escaping.
  const w = window as unknown as { CSS?: { escape?: (s: string) => string } };
  w.CSS ??= {};
  w.CSS.escape ??= (s: string) => s;
  const addCharacter = await screen.findByRole<HTMLButtonElement>('button', { name: '+ Add a character' });

  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('link', { name: 'Skip to main content' })); // the first tab stop (4.1)
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Hide sidebar' })); // the shell's first header control
  // Owner (2026-10-12): no second close control inside the sidebar; the header's "Hide sidebar" is the only one.
  expect(screen.queryByRole('button', { name: 'Close sidebar' })).toBeNull();
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'TomeStack, your characters' })); // the way home
  // Spec 3.1: "+ Add a character" heads the sidebar, ahead of the character list.
  await user.tab();
  expect(document.activeElement).toBe(addCharacter);
  await user.keyboard('{Enter}');
  expect(await screen.findByRole('heading', { level: 1, name: 'Add a character' })).toBeTruthy();

  // The chooser's radio group is a single Tab stop. Nothing is chosen yet, so Continue does nothing until a radio is ticked.
  const make = await screen.findByRole<HTMLInputElement>('radio', { name: 'Make a new one' });
  await waitFor(() => expect(make.disabled).toBe(false));
  for (let i = 0; i < 200 && document.activeElement !== make; i++) await user.tab();
  expect(document.activeElement).toBe(make);
  await user.keyboard(' ');
  expect(make.checked).toBe(true);
  await user.tab();
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Continue' }));
  await user.keyboard('{Enter}');

  expect(await screen.findByRole('heading', { name: 'New character' })).toBeTruthy();
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Name' })));
});

it('opens Help from the sidebar with its h1 focused, lists a sheet gap note there, and Open <character> lands on Features', async () => {
  const user = userEvent.setup();
  const created = await client.createCharacter({ name: 'E2E Help Gaps', rulesFamily: 'srd-5.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [] });
  // A field every sheet has (the service refuses a field the sheet does not show).
  await client.addGapNote(created.character.id, { kind: 'field', fieldId: 'initiative' }, 'Help screen note from the sheet.');
  render(<App />);
  await user.click(within(await screen.findByRole('navigation', { name: 'Main' })).getByRole('button', { name: 'Help' }));
  const h1 = await screen.findByRole('heading', { level: 1, name: 'Help' });
  await waitFor(() => expect(document.activeElement).toBe(h1));
  const section = screen.getByRole('region', { name: 'Gap notes' });
  expect(await within(section).findByText('Help screen note from the sheet.')).toBeTruthy();
  await user.click(within(section).getAllByRole('button', { name: 'Open E2E Help Gaps' })[0]!);
  const sheet = await screen.findByRole('article', { name: 'E2E Help Gaps' });
  expect(within(sheet).getByRole('tab', { name: 'Features' }).getAttribute('aria-selected')).toBe('true');
});

it('the Backups screen says what a full backup holds, and needs the desktop app to write or restore one (M2.1)', async () => {
  const user = userEvent.setup();
  render(<App />);

  await user.click(await screen.findByRole('button', { name: 'Backups' }));
  const panel = await screen.findByRole('region', { name: 'Backups' });
  // Earlier flows in this run created characters, homebrew drafts, a campaign and a PDF copy: the counts come from the service.
  const contents = await within(panel).findByRole('list', { name: 'What the backup contains' });
  expect(contents.textContent).toMatch(/\d+ character\(s\), \d+ campaign\(s\), \d+ gap note\(s\)/);
  expect(contents.textContent).toMatch(/\d+ published and [1-9]\d* draft entries/);
  expect(contents.textContent).toMatch(/Not included: text read from PDFs/);

  // D77: daily copies are on by default; the setting is kept by the service (DevHost takes no copies itself).
  const daily = await within(panel).findByRole('checkbox', { name: 'Keep 7 daily copies' });
  expect((daily as HTMLInputElement).checked).toBe(true);
  await user.click(daily);
  await waitFor(() => expect((daily as HTMLInputElement).checked).toBe(false));
  expect((await client.dailyCopies()).on).toBe(false);
  await user.click(daily); // back on, for the flows after this one
  await waitFor(() => expect((daily as HTMLInputElement).checked).toBe(true));

  // DevHost has no native Save or Open dialog, so both say what they need instead of doing nothing.
  await user.click(within(panel).getByRole('button', { name: 'Back up everything…' }));
  expect((await screen.findByRole('alert')).textContent).toMatch(/Backing up everything needs the TomeStack desktop app/);
  await user.click(within(panel).getByRole('button', { name: 'Choose a full backup…' }));
  await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/Restoring a full backup needs the TomeStack desktop app/));
});

it('archives a character after a preview, lists it apart, and brings it back (SPEC C-08)', async () => {
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Archivist', family: 'srd-5.1', scores: {}, species: /^Fixture Quickfoot/ });
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Archivist' });
  const characters = screen.getByRole('navigation', { name: 'Main' });
  await openMore(user, sheet, 'Archive');

  // Preview first: focus moves into it, and Escape (or "Keep it") changes nothing and returns focus.
  await user.click(within(sheet).getByRole('button', { name: 'Archive…' }));
  let confirm = await screen.findByRole('alertdialog', { name: 'Archive E2E Archivist?' });
  expect(confirm.textContent).toMatch(/Nothing is deleted/);
  await waitFor(() => expect(document.activeElement).toBe(within(confirm).getByRole('button', { name: 'Archive' })));
  await user.keyboard('{Escape}');
  expect(screen.queryByRole('alertdialog')).toBeNull();
  await waitFor(() => expect(document.activeElement).toBe(within(sheet).getByRole('button', { name: 'Archive…' })));
  await user.click(within(sheet).getByRole('button', { name: 'Archive…' }));
  confirm = await screen.findByRole('alertdialog', { name: 'Archive E2E Archivist?' });
  await user.click(within(confirm).getByRole('button', { name: 'Keep it' }));
  expect(screen.queryByRole('alertdialog')).toBeNull();
  expect(within(characters).getByRole('button', { name: /E2E Archivist/ })).toBeTruthy();

  // Confirmed: the character moves to the collapsed "Archived" list, and its sheet says so.
  await user.click(within(sheet).getByRole('button', { name: 'Archive…' }));
  confirm = await screen.findByRole('alertdialog', { name: 'Archive E2E Archivist?' });
  await user.click(within(confirm).getByRole('button', { name: 'Archive' }));
  await expectStatus(/Archived E2E Archivist/);
  const archivedList = await within(characters).findByRole('list', { name: 'Archived characters' });
  expect(within(archivedList).getByRole('button', { name: /E2E Archivist/ })).toBeTruthy();
  expect(archivedList.closest('details')!.open).toBe(false);
  await waitFor(() => expect(within(screen.getByRole('article', { name: 'E2E Archivist' })).getByRole('heading', { name: 'Archived' })).toBeTruthy());
  // The pressed button is gone; focus lands on the control that replaced it, not on <body>.
  await waitFor(() => expect(document.activeElement).toBe(within(screen.getByRole('article', { name: 'E2E Archivist' })).getByRole('button', { name: 'Unarchive' })));

  // Unarchive: back in the main list.
  await user.click(within(screen.getByRole('article', { name: 'E2E Archivist' })).getByRole('button', { name: 'Unarchive' }));
  await expectStatus(/back in the character list/);
  await waitFor(() => expect(within(characters).queryByRole('list', { name: 'Archived characters' })).toBeNull());
  expect(within(characters).getByRole('button', { name: /E2E Archivist/ })).toBeTruthy();
  await waitFor(() => expect(document.activeElement).toBe(within(screen.getByRole('article', { name: 'E2E Archivist' })).getByRole('button', { name: 'Archive…' })));
});

it('keeps the theme picked in Settings when the app is rendered again (ADR-015)', async () => {
  const user = userEvent.setup();
  const { unmount } = render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Settings' }));
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { level: 1, name: 'Settings' })));
  await user.click(screen.getByRole('radio', { name: 'Violet' }));
  expect(document.documentElement.dataset.theme).toBe('violet');
  await user.click(screen.getByRole('radio', { name: 'Dark' }));
  expect(document.documentElement.dataset.appearance).toBe('dark');
  await user.selectOptions(screen.getByRole('combobox', { name: 'Text size' }), '125');
  expect(document.documentElement.dataset.textSize).toBe('125');
  unmount();
  // so the final check proves the mount effect re-applied the saved preferences
  delete document.documentElement.dataset.theme;
  delete document.documentElement.dataset.appearance;
  delete document.documentElement.dataset.textSize;
  render(<App />);
  await screen.findByRole('button', { name: 'Settings' });
  expect(document.documentElement.dataset.theme).toBe('violet');
  expect(document.documentElement.dataset.appearance).toBe('dark');
  expect(document.documentElement.dataset.textSize).toBe('125');
});

it('keeps the Custom accent and its hue when the app is rendered again, and a preset replaces it (D40)', async () => {
  const user = userEvent.setup();
  const { unmount } = render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Settings' }));
  await user.click(screen.getByRole('radio', { name: 'Custom accent' }));
  const slider = screen.getByRole<HTMLInputElement>('slider', { name: 'Accent hue' });
  fireEvent.change(slider, { target: { value: '120' } });
  expect(slider.getAttribute('aria-valuetext')).toBe('Lime, 120 degrees');
  expect(document.documentElement.dataset.theme).toBe('custom');
  unmount();
  delete document.documentElement.dataset.theme;
  render(<App />);
  await screen.findByRole('button', { name: 'Settings' });
  expect(document.documentElement.dataset.theme).toBe('custom');
  await user.click(screen.getByRole('button', { name: 'Settings' }));
  expect(screen.getByRole<HTMLInputElement>('slider', { name: 'Accent hue' }).value).toBe('120');
  await user.click(screen.getByRole('radio', { name: 'Tide' }));
  expect(screen.queryByRole('slider')).toBeNull();
  expect(document.documentElement.dataset.theme).toBe('tide');
});

it('hides and shows the sidebar from the header and by Ctrl+B, moves focus out of a hidden sidebar, and remembers the state (ADR-015)', async () => {
  const user = userEvent.setup();
  const { unmount } = render(<App />);
  const toggle = await screen.findByRole('button', { name: 'Hide sidebar' });
  expect(toggle.getAttribute('aria-expanded')).toBe('true');
  expect(screen.getByRole('navigation', { name: 'Main' })).toBeTruthy();

  await user.click(toggle);
  expect(screen.getByRole('button', { name: 'Show sidebar' }).getAttribute('aria-expanded')).toBe('false');
  expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull();

  await user.keyboard('{Control>}b{/Control}');
  expect(screen.getByRole('navigation', { name: 'Main' })).toBeTruthy();

  // Another keyboard layout: Ctrl + the physical B key reports a different character, so the shortcut follows `code`.
  fireEvent.keyDown(document, { key: 'и', code: 'KeyB', ctrlKey: true });
  expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull();
  fireEvent.keyDown(document, { key: 'и', code: 'KeyB', ctrlKey: true });
  expect(screen.getByRole('navigation', { name: 'Main' })).toBeTruthy();

  // Owner (2026-10-12): the header toggle is the only close control; the sidebar has none of its own.
  expect(screen.queryByRole('button', { name: 'Close sidebar' })).toBeNull();
  expect(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('list', { name: 'Characters' })).toBeTruthy();

  // Focus inside the sidebar, then hide it by the shortcut: focus must land on the toggle, not on <body>.
  screen.getByRole<HTMLButtonElement>('button', { name: '+ Add a character' }).focus();
  await user.keyboard('{Control>}b{/Control}');
  await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Show sidebar' })));

  unmount();
  render(<App />);
  expect(await screen.findByRole('button', { name: 'Show sidebar' })).toBeTruthy();
  expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull();
  await user.click(screen.getByRole('button', { name: 'Show sidebar' }));
  expect(localStorage.getItem('tomestack.sidebar')).toBeNull();

  // A focused checkbox or radio must not block the shortcut (WCAG 2.1.1); a text field keeps Ctrl+B.
  await user.click(screen.getByRole('button', { name: 'Settings' }));
  for (const control of [screen.getByRole('checkbox', { name: 'Animate dice' }), screen.getByRole('radio', { name: 'Violet' })]) {
    control.focus();
    await user.keyboard('{Control>}b{/Control}');
    expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull();
    await user.keyboard('{Control>}b{/Control}');
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeTruthy();
  }
  await startNewCharacter(user);
  const name = await screen.findByRole('textbox', { name: /^Name/ });
  name.focus();
  await user.keyboard('{Control>}b{/Control}');
  expect(screen.getByRole('navigation', { name: 'Main' })).toBeTruthy(); // ignored in a text field
});

it('opens the Characters home screen with a card per character and opens one from it (D25)', async () => {
  const user = userEvent.setup();
  render(<App />);
  const home = await screen.findByRole('region', { name: 'Your characters' }); // the start screen
  expect(within(home).getByRole('heading', { level: 1, name: 'Your characters' })).toBeTruthy();
  const cards = await within(home).findAllByRole('listitem'); // earlier tests created characters
  expect(cards.length).toBeGreaterThan(0);
  expect(within(home).getByRole('list', { name: 'Characters' })).toBeTruthy();
  const first = cards[0]!;
  const name = within(first).getByRole('button', { name: /^Open / }).textContent!.replace(/^Open /, '');
  expect(first.textContent).toMatch(/Level \d+/);
  await user.click(within(first).getByRole('button', { name: `Open ${name}` }));
  expect(await screen.findByRole('article', { name })).toBeTruthy();
  await goHome(user);
  const back = await screen.findByRole('region', { name: 'Your characters' });
  await waitFor(() => expect(document.activeElement).toBe(within(back).getByRole('heading', { level: 1, name: 'Your characters' })));
});
