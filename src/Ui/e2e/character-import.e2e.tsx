// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell). Only the transport differs from the desktop
// app: HTTP to loopback instead of the WebView2 bridge, so export takes the download fallback instead of the native Save dialog.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { expectStatus, openTab, summaryValue } from './helpers';

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

// ---- character import from a character sheet PDF: a D&D Beyond export or the fillable 2014 sheet (features/ddb-pdf-import.md S4, item 14) ----
// The committed sheet is invented: "Testy McFixture", an original fixture Arcanist 3 / Chanter 2 whose spell "Fixture Veil"
// is on both casters' lists. The DevHost has no Open dialog, so the file input sends the bytes (ddb.readData).

const ddbSheet = () => readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-ddb-sheet.pdf'));

async function startDdbImport(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: '+ Add a character' }));
  const sheet = await screen.findByRole<HTMLInputElement>('radio', { name: 'From a character sheet (PDF or text)' });
  await waitFor(() => expect(sheet.disabled).toBe(false)); // needs app.info
  await user.click(sheet);
  await user.click(screen.getByRole('button', { name: 'Continue' }));
}

const wotcSheet = () => readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-wotc-sheet.pdf'));

async function readDdbSheet(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Choose PDF…' }));
  await user.upload(screen.getByLabelText('Character sheet PDF file'), new File([ddbSheet()], 'fixture-ddb-sheet.pdf', { type: 'application/pdf' }));
  expect(await screen.findByText('Fixture Arcanist 3 / Fixture Chanter 2', {}, { timeout: 30000 })).toBeTruthy();
}

// ---- item 16 (D16(c)): unmatched features that carry text become reference drafts ----

async function toDdbSummary(user: ReturnType<typeof userEvent.setup>) {
  await startDdbImport(user);
  await readDdbSheet(user);
  await nextWhenReady(user, 'Next: rules');
  await nextWhenReady(user, 'Next: matches');
  const matches = await screen.findByRole('region', { name: 'Matches' });
  const veil = await within(matches).findByRole('combobox', { name: 'Match for Fixture Veil' });
  await user.selectOptions(veil, '1'); // the option labels are the same for both casters once a character exists, so pick by position
  await nextWhenReady(user, 'Next: numbers');
  await nextWhenReady(user, 'Next: summary');
}

/** Every revision (drafts too) in the import-derived source, from the call the studio uses; 0 while the source does not exist yet. */
async function importSourceRevisions(): Promise<number> {
  const source = (await client.listSources()).find((s) => s.title === 'Characters imported from sheets');
  return source ? (await client.contentBySource(source.id)).reduce((n, entry) => n + entry.revisions.length, 0) : 0;
}

/** The Cancel path: tick Publish at the summary, then Cancel; nothing is written (no source, no draft). Unmounts when done. */
async function cancelAtSummary(user: ReturnType<typeof userEvent.setup>) {
  const sources = (await client.listSources()).map((s) => s.id).sort();
  const revisions = await importSourceRevisions();
  const { unmount } = render(<App />);
  await toDdbSummary(user);
  const section = await screen.findByRole('region', { name: 'Reference drafts' });
  await user.click(within(section).getByRole('checkbox', { name: /^Make these \d+ reference drafts available to characters$/ }));

  await user.click(screen.getByRole('button', { name: 'Cancel' }));

  await expectStatus(/Import cancelled\. Nothing was saved\./);
  expect((await client.listSources()).map((s) => s.id).sort()).toEqual(sources);
  expect(await importSourceRevisions()).toBe(revisions);
  unmount();
}

// One test, Cancel path first and then a fresh import: the service does not draft an entry the import-derived source holds
// already (R106), so the drafts exist only for the first import of this sheet. No earlier test may import
// fixture-ddb-sheet.pdf before this one (and none does: the main import test below runs after it).

it('Cancel writes no reference drafts; a fresh import lists them unticked and publishing makes them studio content', async () => {
  const user = userEvent.setup();
  await cancelAtSummary(user);
  const revisions = await importSourceRevisions();
  render(<App />);
  await toDdbSummary(user);

  const section = await screen.findByRole('region', { name: 'Reference drafts' });
  expect(section.textContent).toContain('Fixture Steady Breath');
  expect(section.textContent).toContain('An invented line of fixture description.');
  const tick = within(section).getByRole<HTMLInputElement>('checkbox', { name: /^Make these \d+ reference drafts available to characters$/ });
  expect(tick.checked).toBe(false);
  expect(within(section).getByRole('note').textContent).toContain('Features are kept as reference content in Homebrew; the gap notes stay on the character.');
  expect((await client.listContent('srd-5.1')).some((o) => o.name === 'Fixture Steady Breath')).toBe(false);

  await user.click(tick);
  expect(document.activeElement).toBe(tick);
  const create = await screen.findByRole<HTMLButtonElement>('button', { name: 'Create character' });
  await waitFor(() => expect(create.disabled).toBe(false));
  await user.click(create);

  await expectStatus(/Character created from the character sheet/);
  expect((await client.listContent('srd-5.1')).some((o) => o.name === 'Fixture Steady Breath')).toBe(true);
  expect(await importSourceRevisions()).toBeGreaterThan(revisions);
});

it('imports a D&D Beyond sheet, resolves a choice, keeps one sheet number as an override and creates the character', async () => {
  const user = userEvent.setup();
  render(<App />);
  await startDdbImport(user);
  await readDdbSheet(user);

  await user.click(screen.getByRole('button', { name: 'Next: rules' }));
  // The 2014-style layout suggests SRD 5.1; the user could pick the other family.
  expect(screen.getByRole<HTMLInputElement>('radio', { name: /2014 rules/ }).checked).toBe(true);
  await user.click(screen.getByRole('radio', { name: "Use the character sheet's values" })); // D16h preset
  await user.click(screen.getByRole('button', { name: 'Next: matches' }));

  const matches = await screen.findByRole('region', { name: 'Matches' });
  const veil = await within(matches).findByRole('combobox', { name: 'Match for Fixture Veil' });
  await user.click(within(matches).getByRole('radio', { name: 'Needs a choice' }));
  expect(within(matches).getAllByRole('rowheader').map((h) => h.textContent)).toEqual(['Fixture Veil']);
  // The caster names come from content.list, which can answer after the rows show (more bundled packs make it slower).
  const chanter = await waitFor(() => {
    const option = within(veil).getAllByRole<HTMLOptionElement>('option').find((o) => /Fixture Chanter/.test(o.textContent ?? ''));
    expect(option).toBeDefined();
    return option!;
  });
  await user.selectOptions(veil, chanter);
  await waitFor(() => expect(within(matches).queryAllByRole('rowheader')).toHaveLength(0));
  // Not installed (the background, a feat, two items, the features): listed, and kept as gap notes.
  await user.click(within(matches).getByRole('radio', { name: 'Not found' }));
  expect(within(matches).getByRole('rowheader', { name: 'Fixture Archivist' })).toBeTruthy();

  // Next waits while the proposal for the latest choice is loading.
  const nextStep = async (name: string) => {
    const next = screen.getByRole<HTMLButtonElement>('button', { name });
    await waitFor(() => expect(next.disabled).toBe(false));
    await user.click(next);
  };
  await nextStep('Next: numbers');
  const armorClass = await screen.findByRole('radiogroup', { name: 'Armor Class' });
  expect(within(screen.getByRole('radiogroup', { name: 'Armor Class' })).getByRole<HTMLInputElement>('radio', { name: "Keep the sheet's number" }).checked).toBe(true); // pre-filled by the preset
  await user.click(within(armorClass).getByRole('radio', { name: "Keep the sheet's number" }));
  await nextStep('Next: summary');

  const create = await screen.findByRole<HTMLButtonElement>('button', { name: 'Create character' });
  await waitFor(() => expect(create.disabled).toBe(false));
  await user.click(create);

  await expectStatus(/Character created from the character sheet: 3 numbers set by hand, \d+ gap note\(s\)/);
  const sheet = await screen.findByRole('article', { name: 'Testy McFixture' });
  expect(summaryValue(sheet, 'Armor Class')).toContain('(set by hand)');
  await openTab(user, sheet, 'Skills and saves');
  expect(within(sheet).getByRole('heading', { name: /^Armor Class:/ }).textContent).toContain('set by hand (calculated');
});

it('imports the fillable 2014 sheet through the same entry: the layout is named, prose lands in session notes and the details fill', async () => {
  const user = userEvent.setup();
  render(<App />);
  await startDdbImport(user);
  await user.click(await screen.findByRole('button', { name: 'Choose PDF…' }));
  await user.upload(screen.getByLabelText('Character sheet PDF file'), new File([wotcSheet()], 'fixture-wotc-sheet.pdf', { type: 'application/pdf' }));
  expect(await screen.findByText('Fixture Arcanist 3', {}, { timeout: 30000 })).toBeTruthy();
  expect(screen.getByText('the fillable 2014 sheet')).toBeTruthy();

  const nextStep = async (name: string) => {
    const next = await screen.findByRole<HTMLButtonElement>('button', { name });
    await waitFor(() => expect(next.disabled).toBe(false));
    await user.click(next);
  };
  await nextStep('Next: rules');
  await nextStep('Next: matches');
  await nextStep('Next: numbers');
  await nextStep('Next: summary');
  expect(await screen.findByRole('region', { name: 'Not brought over' })).toBeTruthy();
  const create = await screen.findByRole<HTMLButtonElement>('button', { name: 'Create character' });
  await waitFor(() => expect(create.disabled).toBe(false));
  await user.click(create);

  await expectStatus(/Character created from the character sheet: \d+ numbers? set by hand, \d+ gap note\(s\)/);
  const sheet = await screen.findByRole('article', { name: 'Testy McFixture' });
  await openTab(user, sheet, 'Journal');
  expect(await within(sheet).findByText(/Testy backstory\./)).toBeTruthy();
  expect(within(within(sheet).getByRole('form', { name: 'Details' })).getByRole<HTMLInputElement>('textbox', { name: 'Alignment' }).value).toBe('Fixture Neutral');
});

it('cancelling the D&D Beyond import at each step leaves the character list unchanged', async () => {
  const user = userEvent.setup();
  const before = (await client.listCharacters()).map((c) => c.id).sort();
  render(<App />);
  const next = ['Next: rules', 'Next: matches', 'Next: numbers', 'Next: summary'];

  for (let step = 1; step <= 5; step++) {
    await startDdbImport(user);
    await readDdbSheet(user);
    for (const label of next.slice(0, step - 1)) {
      const button = await screen.findByRole<HTMLButtonElement>('button', { name: label });
      await waitFor(() => expect(button.disabled).toBe(false));
      await user.click(button);
    }
    await screen.findByRole('heading', { name: ['Choose the sheet', 'Rules and campaign', 'Matches', 'Numbers', 'Create'][step - 1] });
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Choose PDF…' })).toBeNull());
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Create' })).toBeNull());
    await expectStatus(/Import cancelled\. Nothing was saved\./);
  }

  expect((await client.listCharacters()).map((c) => c.id).sort()).toEqual(before);
});

// ---- item 19a (D37): character import from pasted or file text ----
// The text is invented. "Barbarian 1" is an installed SRD class with no choice to answer at level 1.

const fixtureText = [
  '# Testy Texter',
  'Class: Barbarian 1',
  'STR 16  DEX 14  CON 14  INT 8  WIS 9  CHA 10',
  'Gold: 12',
  'Languages: Common, Fixturish',
  'Some line the reader cannot place',
].join('\n');

it('imports a character from pasted text: the unreadable line is listed and kept as a session note, and a too-large file is refused by size', async () => {
  const user = userEvent.setup();
  render(<App />);
  await startDdbImport(user);

  await user.click(await screen.findByRole('button', { name: 'Import a character from text…' }));
  const box = await screen.findByRole<HTMLTextAreaElement>('textbox', { name: 'Character text' });
  await waitFor(() => expect(document.activeElement).toBe(box));

  // A file over the limit is refused by its size alone, before any of it is read.
  const big = new File(['x'], 'big-fixture.txt', { type: 'text/plain' });
  Object.defineProperty(big, 'size', { value: 300_000 });
  await user.upload(screen.getByLabelText('Character text file'), big);
  expect((await screen.findByRole('alert')).textContent).toContain('300,000');
  expect(box.value).toBe('');

  await user.click(box);
  await user.paste(fixtureText);
  await user.click(screen.getByRole('button', { name: 'Read the text' }));
  expect(await screen.findByText('pasted or file text')).toBeTruthy();

  await nextWhenReady(user, 'Next: rules');
  await nextWhenReady(user, 'Next: matches');
  await nextWhenReady(user, 'Next: numbers');
  await nextWhenReady(user, 'Next: summary');
  const notRead = await screen.findByRole('region', { name: 'Lines not read' });
  expect(notRead.textContent).toContain('Some line the reader cannot place');
  const tick = within(notRead).getByRole<HTMLInputElement>('checkbox', { name: 'Keep the lines that were not read as session notes' });
  expect(tick.checked).toBe(true);

  const create = await screen.findByRole<HTMLButtonElement>('button', { name: 'Create character' });
  await waitFor(() => expect(create.disabled).toBe(false));
  await user.click(create);

  await expectStatus(/Character created from the character sheet/);
  const sheet = await screen.findByRole('article', { name: 'Testy Texter' });
  await openTab(user, sheet, 'Journal');
  expect(await within(sheet).findByText(/Some line the reader cannot place/)).toBeTruthy();
});

// ---- item 19b (D37, ADR-013): a PDF with no form fields is read as text through the same entry ----
// The committed sheet is a generated text-only PDF (no form): "Testy Printout", an invented Fixture Arcanist 3.

const textSheet = () => readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-text-sheet.pdf'));

it('imports a PDF that has no form fields: its page text is read, the layout says so, and the character is created', async () => {
  const user = userEvent.setup();
  render(<App />);
  await startDdbImport(user);
  await user.click(await screen.findByRole('button', { name: 'Choose PDF…' }));
  await user.upload(screen.getByLabelText('Character sheet PDF file'), new File([textSheet()], 'fixture-text-sheet.pdf', { type: 'application/pdf' }));
  expect(await screen.findByText('Fixture Arcanist 3', {}, { timeout: 30000 })).toBeTruthy();
  expect(screen.getByText('text from the PDF')).toBeTruthy();

  await nextWhenReady(user, 'Next: rules');
  await nextWhenReady(user, 'Next: matches');
  await nextWhenReady(user, 'Next: numbers');
  await nextWhenReady(user, 'Next: summary');
  const create = await screen.findByRole<HTMLButtonElement>('button', { name: 'Create character' });
  await waitFor(() => expect(create.disabled).toBe(false));
  await user.click(create);

  await expectStatus(/Character created from the character sheet/);
  expect(await screen.findByRole('article', { name: 'Testy Printout' })).toBeTruthy();
});

// ---- item 15 (D35): a class the sheet names that TomeStack does not have, offered as a homebrew class ----
// The generated sheet names the invented "Fixture Tinkerer" 3 with hit dice 3d8; no installed content has it.

const tinkererSheet = () => readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-tinkerer-sheet.pdf'));

async function readTinkererSheet(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Choose PDF…' }));
  await user.upload(screen.getByLabelText('Character sheet PDF file'), new File([tinkererSheet()], 'fixture-tinkerer-sheet.pdf', { type: 'application/pdf' }));
  expect(await screen.findByText('Fixture Tinkerer 3', {}, { timeout: 30000 })).toBeTruthy();
}

async function nextWhenReady(user: ReturnType<typeof userEvent.setup>, name: string) {
  const next = await screen.findByRole<HTMLButtonElement>('button', { name });
  await waitFor(() => expect(next.disabled).toBe(false));
  await user.click(next);
}

it('creates a draft homebrew class from an uninstalled class, leaves Publish it unticked and the character gets nothing from it', async () => {
  const user = userEvent.setup();
  const before = new Set((await client.listCharacters()).map((c) => c.id));
  render(<App />);
  await startDdbImport(user);
  await readTinkererSheet(user);
  await nextWhenReady(user, 'Next: rules');
  await nextWhenReady(user, 'Next: matches');

  const matches = await screen.findByRole('region', { name: 'Matches' });
  const box = await within(matches).findByRole<HTMLInputElement>('checkbox', { name: /Create a homebrew class Fixture Tinkerer/ });
  expect(box.getAttribute('aria-disabled')).toBeNull();
  await user.click(box);
  await waitFor(() => expect(within(matches).getByRole<HTMLInputElement>('checkbox', { name: /Create a homebrew class/ }).checked).toBe(true));
  await waitFor(() => expect(within(matches).getByText(/A homebrew class is created for it/)).toBeTruthy());

  await nextWhenReady(user, 'Next: numbers');
  await nextWhenReady(user, 'Next: summary');
  const stub = await screen.findByRole('region', { name: 'Homebrew class to create' });
  for (const text of ['Fixture Tinkerer', 'd8', 'Strength', 'Characters imported from sheets']) expect(stub.textContent).toContain(text);
  expect(within(stub).getByRole<HTMLInputElement>('checkbox', { name: 'Make it available to characters' }).checked).toBe(false);
  expect(within(stub).getByRole('note').textContent).toBe('The class is a draft: the character gets nothing from it until you make it available to characters in Homebrew and accept the update on the character.');

  const create = await screen.findByRole<HTMLButtonElement>('button', { name: 'Create character' });
  await waitFor(() => expect(create.disabled).toBe(false));
  await user.click(create);

  await expectStatus(/Character created from the character sheet/);
  await screen.findByRole('article', { name: 'Testy Tinker' });
  const created = (await client.listCharacters()).find((c) => !before.has(c.id) && c.name === 'Testy Tinker')!;
  const view = await client.getCharacter(created.id);
  expect(view.sheet.diagnostics.map((d) => d.code)).toContain('content.unpublished');
  const source = (await client.listSources()).find((s) => s.title === 'Characters imported from sheets')!;
  expect(source.importDerived).toBe(true);
  expect(source.redistributable).toBe(false);
});

it('cancelling after choosing to create a homebrew class writes nothing: no character, no source', async () => {
  const user = userEvent.setup();
  const characters = (await client.listCharacters()).map((c) => c.id).sort();
  const sources = (await client.listSources()).map((s) => s.id).sort();
  render(<App />);
  await startDdbImport(user);
  await readTinkererSheet(user);
  await nextWhenReady(user, 'Next: rules');
  await nextWhenReady(user, 'Next: matches');
  const matches = await screen.findByRole('region', { name: 'Matches' });
  await user.click(await within(matches).findByRole('checkbox', { name: /Create a homebrew class Fixture Tinkerer/ }));
  await waitFor(() => expect(within(matches).getByRole<HTMLInputElement>('checkbox', { name: /Create a homebrew class/ }).checked).toBe(true));
  await nextWhenReady(user, 'Next: numbers');
  await nextWhenReady(user, 'Next: summary');
  await screen.findByRole('region', { name: 'Homebrew class to create' });

  await user.click(screen.getByRole('button', { name: 'Cancel' }));

  await expectStatus(/Import cancelled\. Nothing was saved\./);
  expect((await client.listCharacters()).map((c) => c.id).sort()).toEqual(characters);
  expect((await client.listSources()).map((s) => s.id).sort()).toEqual(sources);
  // No draft check here: no client call lists drafts without a content id, and listContent shows published content only.
  // DdbStubClassTests (cancel at every step) counts sources, revisions, characters and notes.
});
