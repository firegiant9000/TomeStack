// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell). Only the transport differs from the desktop
// app: HTTP to loopback instead of the WebView2 bridge, so export takes the download fallback instead of the native Save dialog.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { appStatus, createCharacter, expectStatus, goHome, openTab } from './helpers';

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

/**
 * A row of the review panel's Entries found list. After a filter or kind change the panel says "No entries match." until the
 * new `import.candidates` reply arrives, so the list itself may not exist yet: the whole lookup is retried, never read once.
 */
const candidateButton = (panel: () => HTMLElement, name: string) =>
  waitFor(() => within(within(panel()).getByRole('list', { name: 'Entries found' })).getByRole('button', { name }), { timeout: 10000 });

it('attaches a PDF to a source, offers the cited page on a feature, and removes it after a warning', async () => {
  const user = userEvent.setup();
  // Setup through the client: a homebrew source, a published feat citing page 7, and a character using it.
  const source = await client.createHomebrewSource('E2E Book', ['srd-5.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Cited Feat',
    rulesFamilies: ['srd-5.1'],
    provenance: { sourceId: source.id, page: { start: 7 } },
    status: 'draft',
    summary: 'A feat that cites page 7.',
    effects: [],
  });
  const feat = (await client.publish(draft)).published;
  await client.createCharacter({ name: 'E2E Reader', rulesFamily: 'srd-5.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [feat] });

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const book = await screen.findByRole('listitem', { name: 'E2E Book' });
  expect(within(book).getByText('No PDF attached.')).toBeTruthy();

  // DevHost has no native Open dialog, so the browser file picker sends the bytes (a managed copy).
  await user.click(within(book).getByRole('button', { name: 'Attach PDF…' }));
  const pdf = new File([new TextEncoder().encode('%PDF-1.4\n% e2e\n%%EOF\n')], 'e2e-book.pdf', { type: 'application/pdf' });
  await user.upload(screen.getByLabelText('PDF file'), pdf);
  // The upload hashes and copies the file, then reloads every source (e2e/timeouts.setup.ts allows for that).
  await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'E2E Book' })).getByText(/PDF: e2e-book\.pdf .*copy in TomeStack.*available/)).toBeTruthy());

  // SPEC I-03: pages 3-4 become a draft reference entry (nothing is extracted; it stays inactive until published).
  const pages = within(screen.getByRole('listitem', { name: 'E2E Book' })).getByRole('group', { name: 'Import pages of E2E Book as reference' });
  await user.type(within(pages).getByRole('spinbutton', { name: 'First page' }), '3');
  await user.type(within(pages).getByRole('spinbutton', { name: 'Last page (optional)' }), '4');
  await user.type(within(pages).getByRole('textbox', { name: 'Title (optional)' }), 'E2E Chapter');
  await user.click(within(pages).getByRole('button', { name: 'Import pages' }));
  await expectStatus(/Draft reference entry "E2E Chapter" created/);
  const drafts = await client.contentBySource(source.id);
  expect(drafts.find((e) => e.name === 'E2E Chapter')?.revisions[0]).toMatchObject({ status: 'draft', provenance: { page: { start: 3, end: 4 } } });

  // The feature offers its cited page; opening needs the desktop app's viewer, which DevHost does not have.
  await user.click(screen.getByRole('button', { name: /^E2E Reader/ }));
  // The button appears once the sheet has fetched the source's attachment (e2e/timeouts.setup.ts allows for that).
  await openTab(user, await screen.findByRole('article', { name: 'E2E Reader' }), 'Features');
  const open = await screen.findByRole('button', { name: 'Open E2E Cited Feat, p. 7' });
  await user.click(open);
  expect((await screen.findByRole('alert')).textContent).toMatch(/needs the TomeStack desktop app/);

  // Removing says what breaks, and keeps the content.
  await user.click(screen.getByRole('button', { name: 'Books and PDFs' }));
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Book' })).getByRole('button', { name: 'Remove PDF…' }));
  const confirm = await screen.findByRole('alertdialog', { name: 'Remove e2e-book.pdf?' });
  // The draft reference entry cites pages in it too.
  expect(confirm.textContent).toMatch(/2 entries cite pages in it \(E2E Chapter, E2E Cited Feat\)/);
  await user.click(within(confirm).getByRole('button', { name: 'Remove PDF' }));
  await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'E2E Book' })).getByText('No PDF attached.')).toBeTruthy());
  await user.click(screen.getByRole('button', { name: /^E2E Reader/ }));
  await screen.findByRole('article', { name: 'E2E Reader' });
  await openTab(user, screen.getByRole('article', { name: 'E2E Reader' }), 'Features');
  expect(screen.queryByRole('button', { name: 'Open E2E Cited Feat, p. 7' })).toBeNull();
  expect(within(screen.getByRole('region', { name: 'Features' })).getByText('E2E Cited Feat')).toBeTruthy();
});

// The gate runs in srd-5.1. It publishes the placeholder class's features ("Fixture Beacon" and the like), and an installed feature of
// a class-table name in the same rules family is linked instead of becoming a stub candidate. The PDF tests import fixture-import.pdf
// into srd-5.2.1 sources and expect 19 candidates, so keeping this test's content in the other family leaves them untouched wherever
// this test sits in the file.

it('D60 gate: a Markdown class is reviewed, accepted with its confirm tick and published, then its coverage and 20-level grid open in the studio', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Gate Book', ['srd-5.1']);
  const markdown = readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/lab/placeholder-class.md'));
  await client.startMarkdownImportData(source.id, 'placeholder-class.md', markdown.toString('base64'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const reader = () => within(screen.getByRole('listitem', { name: 'E2E Gate Book' })).getByRole('region', { name: 'Read the text of E2E Gate Book' });
  await screen.findByRole('listitem', { name: 'E2E Gate Book' });
  await user.click(await within(reader()).findByRole('button', { name: /^Review \d+ entr(y|ies) found$/ }, { timeout: 30000 }));
  const panel = () => within(reader()).getByRole('region', { name: 'Entries found in E2E Gate Book' });
  await within(reader()).findByRole('region', { name: 'Entries found in E2E Gate Book' });
  await user.selectOptions(within(panel()).getByRole('combobox', { name: 'Kind' }), 'class');
  await user.click(await candidateButton(panel, 'Placeholder'));
  const details = () => within(panel()).getByRole('region', { name: 'Entry: Placeholder' });
  await waitFor(() => expect(within(details()).getByRole('region', { name: 'Check before keeping' })).toBeTruthy());

  // Keep stays held until the hit die is confirmed against the page; the proficiencies are left as reference.
  await waitFor(() => expect(within(details()).getByRole('button', { name: 'Keep, with its rules' }).getAttribute('aria-disabled')).toBe('true'));
  await user.click(await waitFor(() => within(details()).getByRole('checkbox', { name: /^Confirm the hit die/ })));
  await waitFor(() => expect(within(details()).getByRole('button', { name: 'Keep, with its rules' }).getAttribute('aria-disabled')).toBeNull());
  await user.click(within(details()).getByRole('button', { name: 'Keep, with its rules' }));
  await waitFor(() => expect(appStatus().textContent).toMatch(/Placeholder is now a draft granting 10 features in Homebrew/));
  const drafts = await client.contentBySource(source.id);
  expect(drafts).toHaveLength(11);
  expect(drafts.every((e) => e.latest.status === 'draft')).toBe(true);
  expect(drafts.find((e) => e.name === 'Placeholder')!.latest.effects.find((e) => e.type === 'hitDie')!.automation).toBe('automatic');

  // Publish all drafts of the source through the studio panel.
  await user.click(screen.getByRole('button', { name: 'Homebrew' }));
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Gate Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Gate Book/ }));
  await user.click(await screen.findByRole('button', { name: 'Make all drafts in this source available to characters…' }));
  const region = screen.getByRole('region', { name: 'Make drafts of E2E Gate Book available to characters' });
  const publish = await within(region).findByRole('button', { name: 'Make 11 drafts available to characters' });
  await waitFor(() => expect(publish.getAttribute('aria-disabled')).toBeNull());
  await user.click(publish);
  await within(region).findByText('Made 11 drafts available to characters.');
  expect((await client.contentBySource(source.id)).every((e) => e.latest.status === 'published')).toBe(true);

  // Coverage: the totals the fixture plants, and the features whose text sounds like a mechanic are flagged for hand play.
  const coverage = await screen.findByRole('region', { name: 'Coverage' });
  const totals = '1 automatic · 0 by hand (assisted) · 14 reference · 0 not understood';
  await waitFor(() => expect(within(coverage).getAllByText(totals)).toHaveLength(2)); // the source line and the class line
  const flagged = (name: string) => within(coverage).getByText(new RegExp(`^${name}:`)).closest('li')!.textContent!;
  for (const name of ['Fixture Pool', 'Fixture Lull', 'Fixture Finale']) expect(flagged(name)).toContain('May need a mechanic');
  expect(flagged('Fixture Edge')).not.toContain('May need a mechanic');
  expect(coverage.textContent).not.toContain("can't calculate this");

  // The class opens with its 20-row grid; level 20 grants the finale, and a level with no feature shows a dash.
  await user.click(await screen.findByRole('button', { name: 'Edit Placeholder' }));
  const progression = await screen.findByRole('region', { name: 'Class progression, scrollable' });
  const grid = within(progression).getByRole('table', { name: 'Class progression' });
  await waitFor(() => expect(within(grid).getAllByRole('row')).toHaveLength(21)); // header + 20 levels
  const row = (level: number) => within(grid).getByRole('rowheader', { name: String(level) }).closest('tr')!;
  expect(within(row(20)).getByRole('button', { name: 'Edit Fixture Finale' })).toBeTruthy();
  expect(within(row(1)).getByRole('button', { name: 'Edit Fixture Pool' })).toBeTruthy();
  expect(row(4).textContent).toContain('—');
});

it('reads the fixture PDF, reviews its candidates, and publishes an accepted one through the studio', async () => {
  // M4 D5 (SPEC I-02): the original fixture book, read by the real worker next to the DevHost.
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Grimoire', ['srd-5.2.1']);
  // jsdom gives import.meta.url no file scheme; the e2e run starts in src/Ui (npm --prefix).
  const book = readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-import.pdf'));
  await client.attachPdfData(source.id, 'fixture-import.pdf', book.toString('base64'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const reader = () => within(screen.getByRole('listitem', { name: 'E2E Grimoire' })).getByRole('region', { name: 'Read the text of E2E Grimoire' });
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Grimoire' })).getByRole('button', { name: 'Read the whole document' }));
  // M4 stays experimental until a real third-party PDF and the SRD measurements pass (ROADMAP M4).
  expect(within(reader()).getByRole('heading', { name: /Find entries in this PDF Experimental/ })).toBeTruthy();
  const reviewButton = await within(reader()).findByRole('button', { name: 'Review 19 entries found' }, { timeout: 30000 });
  // Page 6 (of 7) has no text layer: the worker tries Windows OCR where a language is installed, and finds nothing either way.
  expect(within(reader()).getByText(/whole document: completed, 7 pages read, (1 by OCR, )?1 without text, 19 entries found\./)).toBeTruthy();

  // SPEC I-03: the read text is searchable within this source.
  await user.type(within(reader()).getByRole('textbox', { name: 'Search the text of E2E Grimoire' }), 'hookblade');
  await user.click(within(reader()).getByRole('button', { name: 'Search' }));
  expect((await within(reader()).findByRole('list', { name: 'Search results' })).textContent).toMatch(/p\. 4.*Fixture Hookblade/);

  await user.click(reviewButton);
  const panel = () => within(reader()).getByRole('region', { name: 'Entries found in E2E Grimoire' });
  const list = () => within(panel()).getByRole('list', { name: 'Entries found' });
  await within(reader()).findByRole('region', { name: 'Entries found in E2E Grimoire' });
  await waitFor(() => expect(within(list()).getAllByRole('listitem')).toHaveLength(19));
  const kind = within(panel()).getByRole('combobox', { name: 'Kind' });
  const detail = (name: string) => within(panel()).findByRole('region', { name: `Entry: ${name}` });

  // A clean spell: excerpt, page, what was read; the check passes; keeping makes a draft.
  await user.selectOptions(kind, 'spell');
  await waitFor(() => expect(within(list()).getAllByRole('listitem')).toHaveLength(2));
  await user.click(await candidateButton(panel, 'Fixture Ember Lance')); // a slow PDF worker may still be filling the list
  const ember = await detail('Fixture Ember Lance');
  await waitFor(() => expect(document.activeElement).toBe(within(ember).getByRole('heading', { name: 'Entry: Fixture Ember Lance' })));
  expect(within(ember).getByRole('figure').textContent).toMatch(/Excerpt from p\. 2.*Fixture Ember Lance.*3d6 Fire/s);
  expect(ember.querySelector('dl[aria-label="What was read"]')!.textContent).toMatch(/level2schoolevocationcastingtimeAction/);
  await user.click(within(ember).getByRole('button', { name: 'Open page 2' }));
  expect((await screen.findByRole('alert')).textContent).toMatch(/needs the TomeStack desktop app/);
  const acceptEmber = within(ember).getByRole('button', { name: 'Keep, with its rules' });
  await waitFor(() => expect(acceptEmber.getAttribute('aria-disabled')).toBeNull());
  await user.click(acceptEmber);
  await waitFor(() => expect(appStatus().textContent).toMatch(/Fixture Ember Lance is now a draft in Homebrew/));
  // It left the "to review" list with its details, so focus goes back to the list (WCAG 2.4.3).
  await waitFor(() => expect(document.activeElement).toBe(within(panel()).getByRole('heading', { name: 'Entries found in E2E Grimoire' })));
  // The review says what happens next and offers the studio (item 2); the flow below still goes by the sidebar.
  expect(within(panel()).getByRole('button', { name: 'Open E2E Grimoire in Homebrew' })).toBeTruthy();

  // Ignore the other spell.
  await user.click(await candidateButton(panel, 'Fixture Frost Veil'));
  await user.click(within(await detail('Fixture Frost Veil')).getByRole('button', { name: 'Ignore' }));
  await waitFor(() => expect(appStatus().textContent).toMatch(/Ignored Fixture Frost Veil/));

  // An unresolved reference blocks "Keep, with its rules"; "Keep as text only" keeps the text and page only.
  await user.selectOptions(kind, 'feature');
  await user.click(await candidateButton(panel, 'Fixture Stormcall'));
  const stormcall = await detail('Fixture Stormcall');
  expect(within(within(stormcall).getByRole('group', { name: 'Unresolved references' })).getByRole('checkbox', { name: 'Dismiss Fixture Thunder Word' })).toBeTruthy();
  await within(stormcall).findByRole('list', { name: 'Blocks keeping' });
  expect(within(stormcall).getByRole('button', { name: 'Keep, with its rules' }).getAttribute('aria-disabled')).toBe('true');
  await user.click(within(stormcall).getByRole('button', { name: 'Keep as text only' }));
  await waitFor(() => expect(appStatus().textContent).toMatch(/Fixture Stormcall is now a draft reference entry/));

  // The feat: kept as a draft, then published in the studio, which validates it again.
  await user.selectOptions(kind, 'feat');
  await user.click(await candidateButton(panel, 'Fixture Keen Watcher'));
  const feat = await detail('Fixture Keen Watcher');
  const acceptFeat = within(feat).getByRole('button', { name: 'Keep, with its rules' });
  await waitFor(() => expect(acceptFeat.getAttribute('aria-disabled')).toBeNull());
  await user.click(acceptFeat);
  await waitFor(() => expect(appStatus().textContent).toMatch(/Fixture Keen Watcher is now a draft/));

  // Only drafts exist: nothing is active until it is published.
  const entries = await client.contentBySource(source.id);
  expect(entries.map((e) => [e.name, e.latest.status]).sort()).toEqual([
    ['Fixture Ember Lance', 'draft'],
    ['Fixture Keen Watcher', 'draft'],
    ['Fixture Stormcall', 'draft'],
  ]);
  expect(entries.find((e) => e.name === 'Fixture Keen Watcher')!.latest.effects.every((e) => e.automation === 'reference')).toBe(true);

  const studio = screen.getByRole<HTMLButtonElement>('button', { name: 'Homebrew' });
  await user.click(studio);
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Grimoire/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Grimoire/ }));
  await user.click(await screen.findByRole('button', { name: 'Edit Fixture Keen Watcher' }));
  const editor = screen.getByRole('region', { name: /^Edit Fixture Keen Watcher/ });
  await user.click(within(editor).getByRole('button', { name: 'Make available to characters' }));
  await waitFor(() => expect(appStatus().textContent).toBe('Made Fixture Keen Watcher available to characters.'));
  expect((await client.contentBySource(source.id)).find((e) => e.name === 'Fixture Keen Watcher')!.latest.status).toBe('published');
});

it('imports a fixture class: the table is checked, the hit die and slots are confirmed, and the class is saved with its linked features (item 11, D34)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Class Book', ['srd-5.2.1']);
  const book = readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-import.pdf'));
  await client.attachPdfData(source.id, 'fixture-import.pdf', book.toString('base64'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const reader = () => within(screen.getByRole('listitem', { name: 'E2E Class Book' })).getByRole('region', { name: 'Read the text of E2E Class Book' });
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Class Book' })).getByRole('button', { name: 'Read the whole document' }));
  await user.click(await within(reader()).findByRole('button', { name: 'Review 19 entries found' }, { timeout: 30000 }));
  const panel = () => within(reader()).getByRole('region', { name: 'Entries found in E2E Class Book' });
  await within(reader()).findByRole('region', { name: 'Entries found in E2E Class Book' });
  await user.selectOptions(within(panel()).getByRole('combobox', { name: 'Kind' }), 'class');
  await user.click(await candidateButton(panel, 'Fixture Lamplighter'));
  const lamplighter = await within(panel()).findByRole('region', { name: 'Entry: Fixture Lamplighter' });

  // The slot table and the linked features are on screen to check against the page.
  const table = await within(lamplighter).findByRole('table', { name: 'Spell slots by class level, as read from the page' });
  expect(within(table).getAllByRole('rowheader')).toHaveLength(20);
  expect(within(lamplighter).getByRole('button', { name: 'Open page 7' })).toBeTruthy();
  const linked = await within(lamplighter).findByRole('list', { name: 'Linked features' });
  await waitFor(() => expect(within(linked).getAllByRole('listitem').length).toBeGreaterThan(3));

  // The spell list name is the detector's guess: saving the edit settles it. Keep stays held until the confirmations are ticked.
  await user.click(within(lamplighter).getByRole('button', { name: 'Save edit' }));
  await waitFor(() => expect(appStatus().textContent).toMatch(/Saved your edit of Fixture Lamplighter/));
  // The details are rebuilt from the saved edit, and checked again.
  await waitFor(() => expect(within(panel()).getByRole('region', { name: 'Entry: Fixture Lamplighter' })).not.toBe(lamplighter));
  const again = within(panel()).getByRole('region', { name: 'Entry: Fixture Lamplighter' });
  await waitFor(() => expect(within(again).getByRole('region', { name: 'Check before keeping' }).textContent).not.toMatch(/unsure/));
  const accept = within(again).getByRole('button', { name: 'Keep, with its rules' });
  expect(accept.getAttribute('aria-disabled')).toBe('true');
  await user.click(within(again).getByRole('checkbox', { name: /^Confirm the hit die/ }));
  await user.click(within(again).getByRole('checkbox', { name: 'Confirm the spell slot table' }));
  await waitFor(() => expect(accept.getAttribute('aria-disabled')).toBeNull());
  await user.click(accept);
  await waitFor(() => expect(appStatus().textContent).toMatch(/Fixture Lamplighter is now a draft granting \d+ features in Homebrew/));
  const granting = Number(/granting (\d+) features/.exec(appStatus().textContent ?? '')![1]);

  const entries = await client.contentBySource(source.id);
  const cls = entries.find((e) => e.name === 'Fixture Lamplighter')!;
  expect(entries.length).toBeGreaterThan(3);
  expect(entries.every((e) => e.latest.status === 'draft')).toBe(true);
  expect(cls.latest.effects.find((e) => e.type === 'hitDie')!.automation).toBe('automatic');
  // The status counts exactly the features the class draft grants.
  const grants = cls.latest.effects.filter((e) => e.type === 'grant' && e.grant === 'content').length;
  expect(grants).toBeGreaterThan(3);
  expect(granting).toBe(grants);
});

it('selects a proposed effect of a fixture class and sees its lines highlighted on the stored page text (D64)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Span Book', ['srd-5.2.1']);
  const book = readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-import.pdf'));
  await client.attachPdfData(source.id, 'fixture-import.pdf', book.toString('base64'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const reader = () => within(screen.getByRole('listitem', { name: 'E2E Span Book' })).getByRole('region', { name: 'Read the text of E2E Span Book' });
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Span Book' })).getByRole('button', { name: 'Read the whole document' }));
  await user.click(await within(reader()).findByRole('button', { name: 'Review 19 entries found' }, { timeout: 30000 }));
  const panel = () => within(reader()).getByRole('region', { name: 'Entries found in E2E Span Book' });
  await within(reader()).findByRole('region', { name: 'Entries found in E2E Span Book' });
  await user.selectOptions(within(panel()).getByRole('combobox', { name: 'Kind' }), 'class');
  await user.click(await candidateButton(panel, 'Fixture Lamplighter'));
  const lamplighter = await within(panel()).findByRole('region', { name: 'Entry: Fixture Lamplighter' });

  // Before a choice the whole excerpt shows, as always.
  expect(lamplighter.querySelector('pre.excerpt')).not.toBeNull();
  const effects = within(lamplighter).getByRole('list', { name: /^Proposed effects/ });
  await user.click(within(effects).getByRole('button', { name: /^The hit die/ }));
  const region = await within(lamplighter).findByRole('region', { name: /^The hit die/ });
  expect(within(region).getByText(/^Lines \d+–\d+$/)).toBeTruthy();
  expect(region.querySelector('mark')!.textContent!.length).toBeGreaterThan(0);
  expect(lamplighter.querySelector('pre.excerpt')).toBeNull();
});

it('imports a fixture Markdown class into a source with no PDF: pages only, candidates to review, and the stored page text opens in the app (D67)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Markdown Book', ['srd-5.2.1']);
  const markdown = readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/lab/placeholder-class.md'));
  const job = await client.startMarkdownImportData(source.id, 'placeholder-class.md', markdown.toString('base64'));
  expect(job.format).toBe('markdown');

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const reader = () => within(screen.getByRole('listitem', { name: 'E2E Markdown Book' })).getByRole('region', { name: 'Read the text of E2E Markdown Book' });
  // No PDF: the Markdown import is offered, the PDF reads are not.
  expect(within(await screen.findByRole('listitem', { name: 'E2E Markdown Book' })).getByRole('button', { name: 'Import a Markdown file…' })).toBeTruthy();
  expect(within(reader()).queryByRole('button', { name: 'Read the whole document' })).toBeNull();

  const review = await within(reader()).findByRole('button', { name: /^Review \d+ entr(y|ies) found$/ }, { timeout: 30000 });
  expect(Number(/\d+/.exec(review.textContent ?? '')?.[0])).toBeGreaterThan(0);
  expect(within(reader()).getByText(/^Markdown file: completed, \d+ pages? read/)).toBeTruthy();
  expect(within(reader()).queryByRole('button', { name: 'Resume' })).toBeNull();

  // The candidates list opens and holds a row for each candidate found (awaited: the list appears with the first reply).
  await user.click(review);
  const panel = () => within(reader()).getByRole('region', { name: 'Entries found in E2E Markdown Book' });
  await within(reader()).findByRole('region', { name: 'Entries found in E2E Markdown Book' });
  const rows = await waitFor(() => within(within(panel()).getByRole('list', { name: 'Entries found' })).getAllByRole('listitem'));
  expect(rows.length).toBeGreaterThan(0);
  expect(within(rows[0]!).getByRole('button')).toBeTruthy();

  // The read text is searchable, and "Open page" shows what was stored, since there is no PDF to open.
  await user.type(within(reader()).getByRole('textbox', { name: 'Search the text of E2E Markdown Book' }), 'Placeholder Class');
  await user.click(within(reader()).getByRole('button', { name: 'Search' }));
  const results = await within(reader()).findByRole('list', { name: 'Search results' });
  await user.click(within(results).getAllByRole('button', { name: /^Open page \d+$/ })[0]!);
  const stored = await within(reader()).findByRole('region', { name: /^Stored text of page \d+ of E2E Markdown Book$/ });
  expect(stored.textContent).toContain('Placeholder Class');
  expect(await client.attachment(source.id)).toBeUndefined(); // import pages only: nothing was attached
});

it('accepts two fixture candidates in bulk, publishes both through the studio panel, and the builder offers them (item 9, D33)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Bulk Book', ['srd-5.2.1']);
  const book = readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-import.pdf'));
  await client.attachPdfData(source.id, 'fixture-import.pdf', book.toString('base64'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const reader = () => within(screen.getByRole('listitem', { name: 'E2E Bulk Book' })).getByRole('region', { name: 'Read the text of E2E Bulk Book' });
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Bulk Book' })).getByRole('button', { name: 'Read the whole document' }));
  await user.click(await within(reader()).findByRole('button', { name: 'Review 19 entries found' }, { timeout: 30000 }));
  const panel = () => within(reader()).getByRole('region', { name: 'Entries found in E2E Bulk Book' });
  await within(reader()).findByRole('region', { name: 'Entries found in E2E Bulk Book' });
  await waitFor(() => expect(within(within(panel()).getByRole('list', { name: 'Entries found' })).getAllByRole('listitem')).toHaveLength(19));

  // Nothing is selected yet: the bulk buttons are held with their reason.
  const bulk = within(panel()).getByRole('button', { name: 'Keep selected, with their rules' });
  expect(bulk.getAttribute('aria-disabled')).toBe('true');
  await user.click(within(panel()).getByRole('checkbox', { name: 'Select Fixture Ember Lance' }));
  await user.click(within(panel()).getByRole('checkbox', { name: 'Select Fixture Keen Watcher' }));
  expect(within(panel()).getByText('2 selected', { selector: '[aria-live="polite"]' })).toBeTruthy();
  await user.click(bulk);
  await waitFor(() => expect(appStatus().textContent).toMatch(/^Kept 2\./));
  const drafts = (await client.contentBySource(source.id)).filter((e) => e.latest.status === 'draft').map((e) => e.name).sort();
  expect(drafts).toEqual(['Fixture Ember Lance', 'Fixture Keen Watcher']);

  await user.click(screen.getByRole('button', { name: 'Homebrew' }));
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Bulk Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Bulk Book/ }));
  await user.click(await screen.findByRole('button', { name: 'Make all drafts in this source available to characters…' }));
  const region = screen.getByRole('region', { name: 'Make drafts of E2E Bulk Book available to characters' });
  await waitFor(() => expect(document.activeElement).toBe(within(region).getByRole('heading', { name: 'Make drafts of E2E Bulk Book available to characters' })));
  const publish = await within(region).findByRole('button', { name: 'Make 2 drafts available to characters' });
  await waitFor(() => expect(publish.getAttribute('aria-disabled')).toBeNull());
  await user.click(publish);

  const result = await within(region).findByText('Made 2 drafts available to characters.');
  await waitFor(() => expect(document.activeElement).toBe(result)); // an effect moves focus to the result, so it can land after the text
  expect((await client.contentBySource(source.id)).map((e) => e.latest.status)).toEqual(['published', 'published']);
  // A second press would find nothing pending: the button is gone with the drafts.
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Make all drafts in this source available to characters…' })).toBeNull());
  expect((await client.listContent('srd-5.2.1')).some((o) => o.name === 'Fixture Ember Lance')).toBe(true);
});

it('adds the wizard list to a fixture spell candidate in bulk, accepts and publishes it, and the builder offers it to a wizard (item 10)', async () => {
  const user = userEvent.setup();
  const source = await client.createHomebrewSource('E2E Lists Book', ['srd-5.2.1']);
  const book = readFileSync(resolve(process.cwd(), '../../tests/RulesFixtures/pdf/fixture-import.pdf'));
  await client.attachPdfData(source.id, 'fixture-import.pdf', book.toString('base64'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const reader = () => within(screen.getByRole('listitem', { name: 'E2E Lists Book' })).getByRole('region', { name: 'Read the text of E2E Lists Book' });
  await user.click(within(await screen.findByRole('listitem', { name: 'E2E Lists Book' })).getByRole('button', { name: 'Read the whole document' }));
  await user.click(await within(reader()).findByRole('button', { name: 'Review 19 entries found' }, { timeout: 30000 }));
  const panel = () => within(reader()).getByRole('region', { name: 'Entries found in E2E Lists Book' });
  await within(reader()).findByRole('region', { name: 'Entries found in E2E Lists Book' });
  await waitFor(() => expect(within(within(panel()).getByRole('list', { name: 'Entries found' })).getAllByRole('listitem')).toHaveLength(19));

  // The fixture spell is level 2, which a level 1 wizard has no slot for; the reviewer lowers it to level 1 first (setup, through the API).
  const [job] = await client.listImports(source.id);
  const ember = (await client.candidates(job!.id, { kind: 'spell' })).items.find((c) => c.candidate.proposedName === 'Fixture Ember Lance')!;
  const effects = ember.candidate.proposedEffects.map((e) => (e.type === 'spell' ? { ...e, level: 1 } : e));
  await client.editCandidate(ember.id, { effects, expected: ember.etag });

  // Nothing is ticked: the list button is held with its reason.
  const open = within(panel()).getByRole('button', { name: 'Add to a spell list…' });
  expect(open.getAttribute('aria-disabled')).toBe('true');
  await user.click(within(panel()).getByRole('checkbox', { name: 'Select Fixture Ember Lance' }));
  await user.click(within(panel()).getByRole('button', { name: 'Add 1 spell to a spell list…' }));
  const key = within(panel()).getByRole('combobox', { name: 'Spell list key' });
  await waitFor(() => expect(document.activeElement).toBe(key));
  await user.type(key, ' Wizard');
  expect(within(panel()).getByText('Saved as: wizard')).toBeTruthy();
  await user.click(within(panel()).getByRole('button', { name: 'Add 1 spell to this list' }));
  await waitFor(() => expect(appStatus().textContent).toMatch(/^Added the spell list wizard to 1 spell/));
  const stored = (await client.candidates(job!.id, { kind: 'spell' })).items.find((c) => c.id === ember.id)!;
  const lists = (stored.edited ?? stored.candidate).proposedEffects.flatMap((e) => (e.type === 'spell' ? (e.lists as string[]) : []));
  expect(lists).toContain('wizard');

  // Keep it as a draft (the bulk button), then publish through the studio panel.
  const row = within(panel()).getByRole<HTMLInputElement>('checkbox', { name: 'Select Fixture Ember Lance' });
  if (!row.checked) await user.click(row);
  const accept = within(panel()).getByRole('button', { name: 'Keep selected, with their rules' });
  await waitFor(() => expect(accept.getAttribute('aria-disabled')).toBeNull());
  await user.click(accept);
  await waitFor(() => expect(appStatus().textContent).toMatch(/^Kept 1\./));

  await user.click(screen.getByRole('button', { name: 'Homebrew' }));
  const sourceSelect = await screen.findByRole('combobox', { name: 'Book or notebook' });
  await waitFor(() => expect(within(sourceSelect).getByRole('option', { name: /^E2E Lists Book/ })).toBeTruthy());
  await user.selectOptions(sourceSelect, within(sourceSelect).getByRole('option', { name: /^E2E Lists Book/ }));
  await user.click(await screen.findByRole('button', { name: 'Make all drafts in this source available to characters…' }));
  const region = screen.getByRole('region', { name: 'Make drafts of E2E Lists Book available to characters' });
  const publish = await within(region).findByRole('button', { name: 'Make 1 draft available to characters' });
  await waitFor(() => expect(publish.getAttribute('aria-disabled')).toBeNull());
  await user.click(publish);
  await within(region).findByText('Made 1 draft available to characters.');

  // The published spell is on the wizard list, so the builder offers it to a wizard.
  const keys = await client.spellListKeys();
  expect(keys.find((k) => k.key === 'wizard')!.spells).toBeGreaterThan(0);
  await goHome(user);
  await createCharacter(user, { name: 'E2E Listed Wizard', family: 'srd-5.2.1', cls: /^Wizard/ });
  const picker = await screen.findByRole('group', { name: /^Spellcasting spells/ }); // the SRD wizard's rule is named "Spellcasting"
  expect(within(picker).getByRole('checkbox', { name: /^Fixture Ember Lance/ })).toBeTruthy();
});
