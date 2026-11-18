// End-to-end UI flow against the real DevHost (same CommandDispatcher as the shell). Only the transport differs from the desktop
// app: HTTP to loopback instead of the WebView2 bridge, so export takes the download fallback instead of the native Save dialog.
import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, inject, it, vi } from 'vitest';
import { zip } from './zip';
import { App } from '../src/App';
import { client } from '../src/api/client';
import { downloadBase64 } from '../src/files';
import { appStatus, bytesOf, createCharacter, expectStatus, openMore, openTab, srd, startNewCharacter } from './helpers';

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

/** Add-ons live in Settings now (no sidebar entry): opens Settings and waits for the lazy Add-ons section. */
async function openAddOns(user: ReturnType<typeof userEvent.setup>): Promise<void> {
  await user.click(await screen.findByRole('button', { name: 'Settings' }));
  await screen.findByRole('heading', { name: 'Add-ons' });
}

/** Opens the "Opening this in an older TomeStack?" disclosure inside `scope`, then picks the radio for `release` (e.g. "0.7.0"). */
async function chooseOlderTarget(user: ReturnType<typeof userEvent.setup>, scope: HTMLElement, release: string): Promise<void> {
  const summary = within(scope).getByText(/^Opening this in an older TomeStack\?/);
  if (!summary.closest('details')!.open) await user.click(summary);
  await user.click(within(scope).getByRole('radio', { name: release }));
}

it('marks a source as shareable after confirming it is your own work, saves a source pack and imports it (M6 slice 1)', async () => {
  const user = userEvent.setup();
  // Setup through the client: your own source with a published feat, and a source a PDF was attached to.
  const own = await client.createHomebrewSource('E2E Own Notes', ['srd-5.1']);
  const draft = await client.saveDraft({
    contentId: crypto.randomUUID(),
    revisionId: '00000000-0000-0000-0000-000000000000',
    kind: 'feat',
    name: 'E2E Own Feat',
    rulesFamilies: ['srd-5.1'],
    provenance: { sourceId: own.id },
    status: 'draft',
    summary: 'An original feat.',
    effects: [],
  });
  await client.publish(draft);
  const scanned = await client.createHomebrewSource('E2E Scanned Notes', ['srd-5.1']);
  await client.attachPdfData(scanned.id, 'e2e-scan.pdf', btoa('%PDF-1.4\n% e2e\n%%EOF\n'));

  render(<App />);
  await user.click(await screen.findByRole('button', { name: 'Books and PDFs' }));
  const mine = await screen.findByRole('listitem', { name: 'E2E Own Notes' });
  expect(within(mine).getByText('Not shared.')).toBeTruthy();
  // A source a PDF was attached to is import-derived for good: it offers no way to share it.
  const theirs = screen.getByRole('listitem', { name: 'E2E Scanned Notes' });
  expect(within(theirs).getByText(/Holds imported material \(from a PDF or a Markdown file\): never shared/)).toBeTruthy();
  expect(within(theirs).queryByRole('button', { name: 'Mark as shareable…' })).toBeNull();

  // Marking needs the author's confirmation.
  await user.click(within(mine).getByRole('button', { name: 'Mark as shareable…' }));
  const confirm = await screen.findByRole('alertdialog', { name: 'Mark E2E Own Notes as shareable?' });
  const mark = within(confirm).getByRole<HTMLButtonElement>('button', { name: 'Mark as shareable' });
  expect(mark.disabled).toBe(true);
  await user.click(within(confirm).getByRole('checkbox', { name: /The source is my own work/ }));
  await user.click(mark);
  await expectStatus(/E2E Own Notes is marked as shareable/);
  await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'E2E Own Notes' })).getByText(/Marked as shareable/)).toBeTruthy());

  // The pack offers only shareable sources; DevHost has no Save dialog, so it downloads.
  const packForm = screen.getByRole('region', { name: 'Share sources as a TomeStack file' });
  expect(within(packForm).queryByRole('checkbox', { name: 'E2E Scanned Notes' })).toBeNull();
  await user.click(within(packForm).getByRole('checkbox', { name: 'E2E Own Notes' }));
  await user.click(within(packForm).getByRole('button', { name: 'Preview file' }));
  const preview = await within(packForm).findByRole('region', { name: 'TomeStack file preview' });
  expect(preview.textContent).toMatch(/1 source, 1 published version/);
  // L5: the preflight lists what goes (and what stays), and Save waits for it.
  const preflight = await within(packForm).findByRole('region', { name: 'Preflight: TomeStack file' });
  expect(preflight.textContent).toMatch(/Included: 1 item\. Left out: 1\./);
  expect(preflight.textContent).toMatch(/E2E Own Feat \(E2E Own Notes\): a draft; only published content is shared/); // the draft stays here
  await waitFor(() => expect(within(preview).getByRole<HTMLButtonElement>('button', { name: 'Save file…' }).disabled).toBe(false));
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(preview).getByRole('button', { name: 'Save file…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(fileName).toBe('E2E-Own-Notes-source-pack.tomestack.zip');

  // Importing it: the preview says what a source pack is; here everything is already installed.
  await user.upload(screen.getByLabelText('TomeStack file'), new File([bytesOf(base64)], fileName, { type: 'application/zip' }));
  const importing = await screen.findByRole('region', { name: 'Import a TomeStack file' });
  expect(within(importing).getByText(/This TomeStack file holds sources\. Its sender stated that each source is their own work/)).toBeTruthy();
  await user.click(within(importing).getByRole('button', { name: 'Apply import' }));
  await expectStatus(/unchanged/);

  // L5: a shareable source with nothing published blocks the pack, in words (the export would refuse it too).
  const late = await client.createHomebrewSource('E2E Late Notes', ['srd-5.1']);
  await client.setShareable(late.id, true, true);
  await user.click(screen.getByRole('button', { name: 'Books and PDFs' }));
  const lateForm = await screen.findByRole('region', { name: 'Share sources as a TomeStack file' });
  await user.click(await within(lateForm).findByRole('checkbox', { name: 'E2E Late Notes' }));
  const blocked = await within(lateForm).findByRole('region', { name: 'Preflight: TomeStack file' });
  expect(within(blocked).getByRole('alert').textContent).toMatch(/E2E Late Notes: nothing is published yet/);
});

it('shares a campaign as a campaign pack that names what it leaves out, and imports it over a changed copy by choice (M6 slice 2)', async () => {
  const user = userEvent.setup();
  // Setup through the client: your own shareable homebrew, homebrew you did not mark, and a campaign allowing both and the SRD.
  async function homebrew(title: string, shareable: boolean) {
    const source = await client.createHomebrewSource(title, ['srd-5.2.1']);
    if (shareable) await client.setShareable(source.id, true, true);
    const draft = await client.saveDraft({
      contentId: crypto.randomUUID(),
      revisionId: '00000000-0000-0000-0000-000000000000',
      kind: 'feat',
      name: `${title} Feat`,
      rulesFamilies: ['srd-5.2.1'],
      provenance: { sourceId: source.id },
      status: 'draft',
      summary: 'An original feat.',
      effects: [],
    });
    await client.publish(draft);
    return source;
  }
  const harbor = await homebrew('E2E Harbor Notes', true);
  const secret = await homebrew('E2E Secret Notes', false);
  const campaign = await client.saveCampaign({
    id: '00000000-0000-0000-0000-000000000000',
    name: 'E2E Harbor Table',
    rulesFamily: 'srd-5.2.1',
    allowedSources: ['52500000-0000-4000-8000-000000000001', harbor.id, secret.id],
    houseRules: 'Original house rule: rests take a full day.',
  });

  render(<App />);
  const campaignsButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Campaigns' });
  await waitFor(() => expect(campaignsButton.disabled).toBe(false));
  await user.click(campaignsButton);
  await user.click(await screen.findByRole('button', { name: 'Share E2E Harbor Table…' }));
  const share = await screen.findByRole('region', { name: 'Share E2E Harbor Table' });
  await waitFor(() => expect(share.textContent).toMatch(/1 source carried with 1 available version; System Reference Document 5\.2\.1 named, not copied/));
  // The guard: homebrew you did not mark as your own shareable work is only named.
  const leftOut = within(share).getByRole('region', { name: 'Left out of the TomeStack file' });
  expect(leftOut.textContent).toMatch(/E2E Secret Notes .*not marked as shareable/);

  // DevHost has no Save dialog, so it downloads.
  // L5: the preflight names the same source in words, and Save waits for it.
  const campaignCheck = await within(share).findByRole('region', { name: 'Preflight: TomeStack file' });
  expect(campaignCheck.textContent).toMatch(/E2E Secret Notes.*not marked as your own shareable work/);
  await waitFor(() => expect(within(share).getByRole<HTMLButtonElement>('button', { name: 'Save TomeStack file…' }).disabled).toBe(false));
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(share).getByRole('button', { name: 'Save TomeStack file…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(fileName).toBe('E2E-Harbor-Table-campaign-pack.tomestack.zip');

  // Here the campaign changes after the pack was made; importing it asks which version to keep.
  await client.saveCampaign({ ...campaign, name: 'E2E Harbor Table (local)', allowedSources: ['52500000-0000-4000-8000-000000000001', harbor.id] });
  await user.upload(screen.getByLabelText('TomeStack file'), new File([bytesOf(base64)], fileName, { type: 'application/zip' }));
  const importing = await screen.findByRole('region', { name: 'Import a TomeStack file' });
  expect(within(importing).getByText(/This TomeStack file holds a campaign/)).toBeTruthy();
  const apply = within(importing).getByRole<HTMLButtonElement>('button', { name: 'Apply import' });
  expect(apply.disabled).toBe(true);
  const decision = within(importing).getByRole('group', { name: 'Campaign “E2E Harbor Table” differs from yours' });
  expect(within(decision).getByRole('rowheader', { name: 'allowedSources' })).toBeTruthy();
  await user.click(within(decision).getByRole('radio', { name: 'Use the imported version' }));
  await user.click(apply);
  await expectStatus(/1 replaced.*copied to backups\/pre-import-.*\.db/);
  expect((await client.listCampaigns()).find((c) => c.id === campaign.id)?.name).toBe('E2E Harbor Table');
});

it('installs the sample extension after granting its permissions, runs its import and export hooks, and removes it (M6 slice 3)', async () => {
  const user = userEvent.setup();
  // The external sample, packed as a user would: extension.json and transforms/ from examples/extensions.
  const folder = resolve(process.cwd(), '../../examples/extensions/spell-list-and-sheet-summary');
  const files: Record<string, Uint8Array> = { 'extension.json': readFileSync(resolve(folder, 'extension.json')) };
  for (const name of readdirSync(resolve(folder, 'transforms'))) files[`transforms/${name}`] = readFileSync(resolve(folder, 'transforms', name));
  const extension = new File([zip(files)], 'spell-list-and-sheet-summary.tomestack-ext.zip', { type: 'application/zip' });
  await client.createCharacter({ name: 'E2E Ext Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 14, con: 12, int: 10, wis: 10, cha: 10 }, pins: [] });

  render(<App />);
  await openAddOns(user);
  // DevHost has no native Open dialog, so the browser picker is used.
  await user.click(await screen.findByRole('button', { name: 'Install extension…' }));
  await user.upload(screen.getByLabelText('Extension file'), extension);
  const review = await screen.findByRole('region', { name: 'Install Spell list and sheet summary (sample)?' });
  const install = within(review).getByRole<HTMLButtonElement>('button', { name: 'Install with these permissions' });
  expect(install.disabled).toBe(true); // nothing is granted until it is ticked
  const permissions = within(within(review).getByRole('group', { name: 'Permissions to grant' })).getAllByRole('checkbox');
  expect(permissions).toHaveLength(4);
  for (const permission of permissions) await user.click(permission);
  await user.click(install);
  await expectStatus(/Installed Spell list and sheet summary \(sample\) with 4 permissions/);
  const item = await screen.findByRole('listitem', { name: 'Spell list and sheet summary (sample)' });

  // Import: a CSV spell list becomes drafts in a new source; nothing is written before "Create drafts".
  await user.click(within(item).getByRole('button', { name: 'Import a CSV spell list as draft spells' }));
  const importRun = await screen.findByRole('region', { name: 'Run Import a CSV spell list as draft spells' });
  await user.clear(within(importRun).getByRole('textbox', { name: 'New source for the drafts' }));
  await user.type(within(importRun).getByRole('textbox', { name: 'New source for the drafts' }), 'E2E Imported Spells');
  await user.click(within(importRun).getByRole('button', { name: 'Choose CSV file…' }));
  const csv = readFileSync(resolve(folder, 'sample-spells.csv'));
  await user.upload(screen.getByLabelText('Import a CSV spell list as draft spells: file'), new File([csv], 'sample-spells.csv', { type: 'text/csv' }));
  const drafts = await within(importRun).findByRole('region', { name: 'Preview of Import a CSV spell list as draft spells' });
  expect(drafts.textContent).toMatch(/3 drafts in the new source E2E Imported Spells/);
  expect(drafts.textContent).toMatch(/never shared/);
  expect((await client.listSources()).some((s) => s.title === 'E2E Imported Spells')).toBe(false);
  await user.click(within(drafts).getByRole('button', { name: 'Create drafts' }));
  await expectStatus(/Created 3 drafts in the new source E2E Imported Spells/);
  const imported = (await client.listSources()).find((s) => s.title === 'E2E Imported Spells')!;
  expect(imported.importDerived).toBe(true);
  expect((await client.contentBySource(imported.id)).every((e) => e.latest.status === 'draft')).toBe(true);

  // Export: the sheet summary, previewed and then saved (DevHost downloads). No local path reaches it.
  await user.click(within(screen.getByRole('listitem', { name: 'Spell list and sheet summary (sample)' })).getByRole('button', { name: 'Export a sheet summary as Markdown' }));
  const exportRun = await screen.findByRole('region', { name: 'Run Export a sheet summary as Markdown' });
  await user.selectOptions(within(exportRun).getByRole('combobox', { name: 'Character' }), 'E2E Ext Hero');
  await user.click(within(exportRun).getByRole('button', { name: 'Preview output' }));
  const output = await within(exportRun).findByRole('region', { name: 'Preview of Export a sheet summary as Markdown' });
  expect(output.textContent).toMatch(/# E2E Ext Hero/);
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(output).getByRole('button', { name: 'Save output…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(fileName).toBe('E2E-Ext-Hero-sheet-markdown.md');
  const markdown = new TextDecoder().decode(bytesOf(base64));
  expect(markdown).toMatch(/## Sources and licenses/);
  expect(markdown.toLowerCase().replaceAll('\\', '/')).not.toContain(inject('devHost').dataDir.toLowerCase().replaceAll('\\', '/'));

  // Remove: its permissions and file go; its drafts stay.
  await user.click(within(screen.getByRole('listitem', { name: 'Spell list and sheet summary (sample)' })).getByRole('button', { name: 'Remove Spell list and sheet summary (sample)…' }));
  await user.click(within(await screen.findByRole('alertdialog', { name: 'Remove Spell list and sheet summary (sample)?' })).getByRole('button', { name: 'Remove' }));
  await expectStatus(/Removed Spell list and sheet summary \(sample\)/);
  expect(await client.listExtensions()).toEqual([]);
  expect(await client.contentBySource(imported.id)).toHaveLength(3);
});

it('exports a character for Foundry VTT and as sheet JSON after a preview, with no local path in either file (M6 slice 4)', async () => {
  const user = userEvent.setup();
  const character = await client.createCharacter({ name: 'E2E VTT Hero', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 14, con: 12, int: 10, wis: 10, cha: 10 }, pins: [] });
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E VTT Hero/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E VTT Hero' });
  await openMore(user, sheet, 'Export…');
  const panel = within(sheet).getByRole('region', { name: 'Export to another tool or device' });
  const dataDir = inject('devHost').dataDir.toLowerCase().replaceAll('\\', '/');

  // Foundry VTT (dnd5e): the preview names the pinned release, then DevHost downloads the file.
  await user.click(within(panel).getByRole('radio', { name: /Foundry VTT \(dnd5e system\)/ }));
  await user.click(within(panel).getByRole('button', { name: 'Preview export' }));
  const preview = await within(panel).findByRole('region', { name: 'Export preview' });
  expect(preview.textContent).toMatch(/E2E-VTT-Hero\.foundry-dnd5e\.json.*checked against Foundry VTT 14\.367 with dnd5e 6\.0\.6/);
  expect(preview.textContent).toMatch(/not affiliated with Foundry Gaming LLC/);
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await within(panel).findByRole('region', { name: 'Preflight: shared sheet' });
  await waitFor(() => expect(within(preview).getByRole<HTMLButtonElement>('button', { name: 'Save export file…' }).disabled).toBe(false));
  await user.click(within(preview).getByRole('button', { name: 'Save export file…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [foundryName, foundry64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(foundryName).toBe('E2E-VTT-Hero.foundry-dnd5e.json');
  const actorText = new TextDecoder().decode(bytesOf(foundry64));
  const actor = JSON.parse(actorText) as { type: string; name: string; _stats: { systemVersion: string } };
  expect([actor.type, actor.name, actor._stats.systemVersion]).toEqual(['character', 'E2E VTT Hero', '6.0.6']);
  expect(actorText.toLowerCase().replaceAll('\\\\', '/').replaceAll('\\', '/')).not.toContain(dataDir);

  // The neutral sheet JSON.
  await user.click(within(panel).getByRole('radio', { name: /TomeStack sheet \(JSON\)/ }));
  await user.click(within(panel).getByRole('button', { name: 'Preview export' }));
  const sheetPreview = await within(panel).findByRole('region', { name: 'Export preview' });
  await waitFor(() => expect(within(sheetPreview).getByRole<HTMLButtonElement>('button', { name: 'Save export file…' }).disabled).toBe(false));
  await user.click(within(sheetPreview).getByRole('button', { name: 'Save export file…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 2));
  const [sheetName, sheet64] = vi.mocked(downloadBase64).mock.calls[downloads + 1]!;
  expect(sheetName).toBe('E2E-VTT-Hero.tomestack-sheet.json');
  const model = JSON.parse(new TextDecoder().decode(bytesOf(sheet64))) as { format: string; formatVersion: number; character: { name: string } };
  expect([model.format, model.formatVersion, model.character.name]).toEqual(['tomestack.sheet', 1, 'E2E VTT Hero']);
  expect(character.character.id).toBeTruthy();
});

it('follows the authoring guides: the extension guide, packed as written, installs, is granted and exports its features with every notice (M6 slice 5)', async () => {
  const user = userEvent.setup();
  // docs/authoring/extension.md, its tagged blocks exactly as the guide shows them.
  const guide = readFileSync(resolve(process.cwd(), '../../docs/authoring/extension.md'), 'utf8');
  const files: Record<string, Uint8Array> = {};
  for (const match of guide.matchAll(/```json tomestack-example:([^\r\n]+)\r?\n([\s\S]*?)\r?\n```/g)) {
    const name = match[1]!.trim();
    if (name === 'extension.json' || name.startsWith('transforms/')) files[name] = new TextEncoder().encode(match[2]!);
  }
  expect(Object.keys(files).sort()).toEqual(['extension.json', 'transforms/features-md.json']);
  // A class, so the file has feature lines and an SRD notice.
  await client.createCharacter({ name: 'E2E Guide Reader', rulesFamily: 'srd-5.2.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [], classes: [{ class: srd(11), level: 1 }] });

  render(<App />);
  await openAddOns(user);
  await user.click(await screen.findByRole('button', { name: 'Install extension…' }));
  await user.upload(screen.getByLabelText('Extension file'), new File([zip(files)], 'example-feature-list.tomestack-ext.zip', { type: 'application/zip' }));
  // "Tick the permissions and choose Install with these permissions."
  const review = await screen.findByRole('region', { name: 'Install Example feature list?' });
  for (const permission of within(within(review).getByRole('group', { name: 'Permissions to grant' })).getAllByRole('checkbox')) await user.click(permission);
  await user.click(within(review).getByRole('button', { name: 'Install with these permissions' }));
  await expectStatus(/Installed Example feature list with 2 permissions/);
  // "Then choose the hook (Export features as Markdown), pick a character …, choose Preview output, and Save output…"
  await user.click(within(await screen.findByRole('listitem', { name: 'Example feature list' })).getByRole('button', { name: 'Export features as Markdown' }));
  const run = await screen.findByRole('region', { name: 'Run Export features as Markdown' });
  await user.selectOptions(within(run).getByRole('combobox', { name: 'Character' }), 'E2E Guide Reader');
  await user.click(within(run).getByRole('button', { name: 'Preview output' }));
  const output = await within(run).findByRole('region', { name: 'Preview of Export features as Markdown' });
  expect(output.textContent).toMatch(/# Features of E2E Guide Reader/);
  const downloads = vi.mocked(downloadBase64).mock.calls.length;
  await user.click(within(output).getByRole('button', { name: 'Save output…' }));
  await waitFor(() => expect(vi.mocked(downloadBase64).mock.calls.length).toBe(downloads + 1));
  const [fileName, base64] = vi.mocked(downloadBase64).mock.calls[downloads]!;
  expect(fileName).toBe('E2E-Guide-Reader-features-md.md');
  const text = new TextDecoder().decode(bytesOf(base64));
  expect(text).toMatch(/^- .+ \([a-z]+\), from .+$/m); // at least one feature line
  expect(text).toMatch(/## Sources and licenses\n- .+CC-BY-4\.0/);
  await client.removeExtension((await client.listExtensions()).find((x) => x.manifest.name === 'Example feature list')!.id);
});

it('shows different allowed content for two campaign profiles, and records a reasoned exception', async () => {
  const user = userEvent.setup();
  render(<App />);
  const campaignsButton = await screen.findByRole<HTMLButtonElement>('button', { name: 'Campaigns' });
  await waitFor(() => expect(campaignsButton.disabled).toBe(false));
  await user.click(campaignsButton);

  async function createCampaign(name: string, sources: RegExp[]) {
    await user.click(await screen.findByRole('button', { name: 'New campaign' }));
    const form = screen.getByRole('form', { name: 'Campaign' });
    await user.type(within(form).getByRole('textbox', { name: 'Campaign name' }), name);
    await user.click(within(form).getByRole('radio', { name: /2024 rules/ }));
    for (const source of sources) await user.click(within(form).getByRole('checkbox', { name: source }));
    await user.click(within(form).getByRole('button', { name: 'Save campaign' }));
    await waitFor(() => expect(appStatus().textContent).toBe(`Saved the campaign ${name}.`));
  }
  await createCampaign('E2E Strict', [/^System Reference Document 5\.2\.1/]);
  await createCampaign('E2E Open', [/^System Reference Document 5\.2\.1/, /^TomeStack Fixtures: 2024 Family/]);

  // The campaign is chosen on the Rules step; the backgrounds it governs are on the Background step (D32), so this flow
  // walks the steps by hand instead of through `createCharacter`.
  await startNewCharacter(user);
  await user.type(await screen.findByRole('textbox', { name: 'Name' }), 'E2E Campaigner');
  const courier = () => screen.getByRole<HTMLInputElement>('radio', { name: /^Fixture Courier/ });
  const courierOff = () => courier().getAttribute('aria-disabled') === 'true'; // inert, never natively disabled (2.4.3)
  const soldier = () => screen.getAllByRole<HTMLInputElement>('radio', { name: /^Soldier/ }).find((r) => r.getAttribute('aria-disabled') !== 'true');
  const toBackground = async () => {
    await user.click(screen.getByRole('button', { name: 'Next: class' }));
    await user.click(screen.getByRole('button', { name: 'Next: ability scores' }));
    await user.click(screen.getByRole('radio', { name: 'Enter by hand' })); // the default method needs all six scores assigned
    for (const next of ['Next: species', 'Next: background']) await user.click(screen.getByRole('button', { name: next }));
  };
  const toRules = async () => {
    for (let i = 0; i < 4; i++) await user.click(screen.getByRole('button', { name: 'Back' }));
  };

  // Profile 1: SRD only. The fixture background is listed but not allowed; the SRD one is.
  // The Campaign picker appears once the campaign list arrives, which can be after the name is typed.
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Campaign' }), 'E2E Strict (2024 rules)');
  await toBackground();
  await waitFor(() => expect(courierOff()).toBe(true));
  expect(courier().closest('label')!.textContent).toMatch(/not allowed in this campaign/);
  expect(soldier()).toBeTruthy();

  // Profile 2: SRD and the 2024 fixtures. The same background is allowed.
  await toRules();
  await user.selectOptions(screen.getByRole('combobox', { name: 'Campaign' }), 'E2E Open (2024 rules)');
  await toBackground();
  await waitFor(() => expect(courierOff()).toBe(false));

  // Back to profile 1, with a deliberate exception: a reason is required before outside content can be picked.
  await toRules();
  await user.selectOptions(screen.getByRole('combobox', { name: 'Campaign' }), 'E2E Strict (2024 rules)');
  await toBackground();
  await waitFor(() => expect(courierOff()).toBe(true));
  const sources = screen.getByRole('group', { name: 'Campaign sources' });
  await user.click(within(sources).getByRole('checkbox', { name: 'Use content from outside the campaign' }));
  expect(courierOff()).toBe(true); // no reason yet
  await user.type(within(sources).getByRole('textbox', { name: /^Reason/ }), 'DM approved');
  await waitFor(() => expect(courierOff()).toBe(false));
  await user.click(courier());
  await user.click(screen.getByRole('button', { name: 'Next: gear' }));
  await user.click(screen.getByRole('button', { name: 'Next: choices' }));
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));

  const sheet = await screen.findByRole('article', { name: 'E2E Campaigner' });
  expect(within(sheet).getByText('Campaign: E2E Strict', { selector: '.tag' })).toBeTruthy();
  const notes = within(sheet).getByRole('region', { name: 'Campaign: E2E Strict' });
  expect(notes.textContent).toMatch(/Fixture Courier.*used by exception: DM approved/);
});

it('keeps the older-version choice behind a disclosure on the sheet export, and picks a release once opened', async () => {
  const user = userEvent.setup();
  await client.createCharacter({ name: 'E2E Older Target', rulesFamily: 'srd-5.1', baseAbilities: { str: 10, dex: 10, con: 10, int: 10, wis: 10, cha: 10 }, pins: [] });
  render(<App />);
  await user.click(await screen.findByRole('button', { name: /^E2E Older Target/ }));
  const sheet = await screen.findByRole('article', { name: 'E2E Older Target' });
  await openMore(user, sheet, 'Export…');
  const panel = within(sheet).getByRole('region', { name: 'Export' });
  // D103: only the previous minor is offered, so a build with no such release (the DevHost of an unreleased minor
  // may have none) shows a plain hint and no choice at all.
  await waitFor(() => expect(within(panel).queryByText(/^This TomeStack has no older version to save for\.$|^Opening this in an older TomeStack\?/)).not.toBeNull());
  if (within(panel).queryByText('This TomeStack has no older version to save for.')) {
    expect(panel.querySelector('details')).toBeNull();
    expect(within(panel).queryByRole('group', { name: 'Which version will open this file?' })).toBeNull();
    return;
  }
  const release = within(panel).getAllByText(/^\d+\.\d+\.\d+$/, { selector: 'label' })[0]!.textContent!;
  expect(panel.querySelector('details')!.open).toBe(false);
  await chooseOlderTarget(user, panel, release);
  expect((within(panel).getByRole('radio', { name: release }) as HTMLInputElement).checked).toBe(true);
});

it('prints a sheet with its license notices, and gap notes only when ticked', async () => {
  // M3 C4: the printable backup. The print dialog is the browser's; here window.print is a spy.
  const print = vi.spyOn(window, 'print').mockImplementation(() => {});
  const user = userEvent.setup();
  render(<App />);
  await createCharacter(user, { name: 'E2E Print', family: 'srd-5.1', scores: {}, species: /^Fixture Quickfoot/ });
  await user.click(await screen.findByRole('button', { name: 'Create and save' }));
  const sheet = await screen.findByRole('article', { name: 'E2E Print' });

  await openTab(user, sheet, 'Features');
  const gaps = within(sheet).getByRole('region', { name: /^Gap notes/ });
  const form = within(gaps).getByRole('form', { name: 'New gap note' });
  await user.selectOptions(within(form).getByRole('combobox', { name: /^About/ }), 'Armor Class');
  await user.type(within(form).getByRole('textbox', { name: /^What was missing or wrong/ }), 'Private note for the print test.');
  await user.click(within(form).getByRole('button', { name: 'Save note' }));
  await within(gaps).findByText('Private note for the print test.');
  await openTab(user, sheet, 'Journal');
  const journal = within(sheet).getByRole('form', { name: 'New session note' });
  await user.type(within(journal).getByRole('textbox', { name: 'Note' }), 'Private session note for the print test.');
  await user.click(within(journal).getByRole('button', { name: 'Save session note' }));
  await within(sheet).findByText('Private session note for the print test.');
  const detailsForm = within(sheet).getByRole('form', { name: 'Details' });
  await user.type(within(detailsForm).getByRole('textbox', { name: 'Backstory' }), 'Private backstory for the print test.');
  await user.click(within(detailsForm).getByRole('button', { name: 'Save details' }));
  await expectStatus(/Details saved/);

  await openMore(user, sheet, 'Print…');
  const preview = await within(sheet).findByRole('region', { name: 'Print preview' });
  await waitFor(() => expect(document.activeElement).toBe(within(preview).getByRole('heading', { name: 'Print character' }))); // focus moves to the preview on open
  expect(within(preview).getByRole('heading', { name: 'E2E Print' })).toBeTruthy();
  expect(within(preview).getByRole('table', { name: 'Abilities' })).toBeTruthy();
  expect(within(preview).getByText(/^Fixture Quickfoot/)).toBeTruthy();
  // The license notices of the sources used (the fixture sources here), and the version it was printed from.
  await waitFor(() => expect(preview.querySelectorAll('.print-notice').length).toBeGreaterThan(0));
  await waitFor(() => expect(preview.textContent).toMatch(/Printed from TomeStack \d+\.\d+\.\d+/));
  // Gap notes are private: left out until ticked. No path ever appears.
  expect(preview.textContent).not.toContain('Private note for the print test.');
  expect(preview.textContent).not.toMatch(/[A-Za-z]:\\|\/Users\/|\\Users\\/);
  await user.click(within(preview).getByRole('checkbox', { name: /^Include gap notes/ }));
  expect(await within(preview).findByText(/Private note for the print test\./)).toBeTruthy();
  // Session notes are private too: absent until their own box is ticked.
  expect(preview.textContent).not.toContain('Private session note for the print test.');
  await user.click(within(preview).getByRole('checkbox', { name: 'Include session notes (private)' }));
  expect(await within(preview).findByText(/Private session note for the print test\./)).toBeTruthy();
  // Details have their own tick, unticked by default.
  const includeDetails = within(preview).getByRole<HTMLInputElement>('checkbox', { name: /^Include details/ });
  expect(includeDetails.checked).toBe(false);
  expect(preview.textContent).not.toContain('Private backstory for the print test.');
  await user.click(includeDetails);
  expect(await within(preview).findByText(/Private backstory for the print test\./)).toBeTruthy();

  await user.click(within(preview).getByRole('button', { name: 'Print…' }));
  expect(print).toHaveBeenCalledTimes(1);
  await user.click(within(preview).getByRole('button', { name: 'Close print preview' }));
  expect(within(sheet).queryByRole('region', { name: 'Print preview' })).toBeNull();
  await waitFor(() => expect(document.activeElement).toBe(within(sheet).getByRole('button', { name: 'More' })));
  print.mockRestore();
});
