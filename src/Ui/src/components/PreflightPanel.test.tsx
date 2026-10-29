// @vitest-environment jsdom
// Import Lab L5: every share export dialog shows what is included, left out (in words) and blocking before Save, and Save stays
// off while anything blocks. The client is mocked; all names are invented.
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { PreflightReport, PreflightRow, SourceRecord } from '../api/types';
import { CampaignsPanel } from './CampaignsPanel';
import { ExportPanel } from './CharacterSheet';
import { PreflightPanel } from './PreflightPanel';
import { SourcesPanel } from './SourcesPanel';
import { VttExportPanel } from './VttExportPanel';

vi.mock('../api/client', () => ({
  client: {
    packagePreflight: vi.fn(),
    listSources: vi.fn(),
    attachment: vi.fn(),
    sourcePackPreview: vi.fn(),
    listCampaigns: vi.fn(),
    saveCampaign: vi.fn(),
    campaignPackPreview: vi.fn(),
    previewExport: vi.fn(),
    previewVttExport: vi.fn(),
  },
}));
vi.mock('./ImportPanel', () => ({ ImportPanel: () => null }));

const zero = '00000000-0000-0000-0000-000000000000';
const row = (name: string, sourceName: string, reason?: string, over: Partial<PreflightRow> = {}): PreflightRow => ({
  contentId: `c-${name}`,
  revisionId: `r-${name}`,
  name,
  sourceName,
  redistributable: true,
  importDerived: null,
  origin: 'local',
  reason,
  ...over,
});
const clear: PreflightReport = { included: [row('Lantern Step', 'Lantern Notes')], omitted: [], blocking: [], notices: ['Lantern Notes (Test author): Personal homebrew'] };
const blocked: PreflightReport = {
  included: [row('Lantern Step', 'Lantern Notes')],
  omitted: [row('Ink Trick', 'Ink Imports', 'preflight.import-derived'), row('Plain Draft', 'Lantern Notes', 'preflight.draft')],
  blocking: [row('Borrowed Rules', 'Borrowed Rules', 'preflight.not-redistributable', { revisionId: zero })],
  notices: [],
};

const source: SourceRecord = {
  id: 'src-1',
  title: 'Lantern Notes',
  publisher: 'Test author',
  rulesFamilies: ['srd-5.1'],
  editionVersion: '1',
  license: 'Personal homebrew',
  redistributable: true,
  origin: 'local',
  shareConfirmedAt: '2026-10-01T00:00:00Z',
};

beforeEach(() => {
  vi.mocked(client.packagePreflight).mockReset();
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.attachment).mockResolvedValue(undefined as never);
});
afterEach(cleanup);

it('says each omitted and blocking row in words, never as a code', async () => {
  vi.mocked(client.packagePreflight).mockResolvedValue(blocked);
  render(<PreflightPanel kind="package" selection={['ch-1']} label="shared package" onReport={() => {}} onError={() => {}} />);

  const region = await screen.findByRole('region', { name: 'Preflight: shared package' });
  expect(vi.mocked(client.packagePreflight)).toHaveBeenCalledWith({ kind: 'package', selection: ['ch-1'] });
  expect(within(region).getByRole('alert').textContent).toMatch(/Borrowed Rules: not marked as your own shareable work/);
  expect(region.textContent).toMatch(/Ink Trick \(Ink Imports\): holds imported material \(from a PDF or a Markdown file\), so it is never shared/);
  expect(region.textContent).toMatch(/Plain Draft \(Lantern Notes\): a draft; only published content is shared/);
  expect(region.textContent).not.toMatch(/preflight\./);
  expect(within(region).getByText('Included (1)')).toBeTruthy();
});

it('says a missing, bundled and received source each in its own words', async () => {
  vi.mocked(client.packagePreflight).mockResolvedValue({
    included: [],
    omitted: [],
    blocking: [
      row('Gone Notes', 'Gone Notes', 'preflight.source-missing'),
      row('Core Rules', 'Core Rules', 'preflight.bundled'),
      row('Friend Notes', 'Friend Notes', 'preflight.received'),
    ],
    notices: [],
  });
  render(<PreflightPanel kind="sourcePack" selection={['a']} label="source pack" onReport={() => {}} onError={() => {}} />);

  const text = (await screen.findByRole('alert')).textContent ?? '';
  expect(text).toMatch(/Gone Notes: this source is not installed here/);
  expect(text).toMatch(/Core Rules: comes with TomeStack/);
  expect(text).toMatch(/Friend Notes: came from someone else/);
  expect(text).not.toMatch(/not yours to share|preflight\./);
});

it('asks again when the sheet is previewed a second time', async () => {
  const user = userEvent.setup();
  vi.mocked(client.previewVttExport).mockImplementation((_id, target, purpose) =>
    Promise.resolve({ token: 'tok', target, fileName: 'Pell.sheet.json', bytes: 10, adapterVersion: '1', purpose, dropped: [], notices: [], differences: [], warnings: [] }),
  );
  vi.mocked(client.packagePreflight).mockResolvedValue(blocked);
  render(<VttExportPanel characterId="ch-1" onError={() => {}} onStatus={() => {}} />);

  await user.click(screen.getByRole('radio', { name: /TomeStack sheet \(JSON\)/ }));
  await user.click(screen.getByRole('button', { name: 'Preview export' }));
  const first = await screen.findByRole('region', { name: 'Export preview' });
  await within(first).findByRole('region', { name: 'Preflight: shared sheet' });
  expect(within(first).getByRole<HTMLButtonElement>('button', { name: 'Save export file…' }).disabled).toBe(true);
  expect(vi.mocked(client.packagePreflight)).toHaveBeenCalledTimes(1);

  // The library changed in between: the second preview asks again and Save follows the new answer.
  vi.mocked(client.packagePreflight).mockResolvedValue(clear);
  await user.click(screen.getByRole('button', { name: 'Preview export' }));
  await waitFor(() => expect(vi.mocked(client.packagePreflight)).toHaveBeenCalledTimes(2));
  await waitFor(() => expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Save export file…' }).disabled).toBe(false));
});

it('says in words that the check failed and keeps Save off', async () => {
  vi.mocked(client.packagePreflight).mockRejectedValue(new Error('boom'));
  const onReport = vi.fn();
  const onError = vi.fn();
  render(<PreflightPanel kind="package" selection={['ch-1']} label="shared package" onReport={onReport} onError={onError} />);

  expect((await screen.findByRole('alert')).textContent).toMatch(/Could not check what may be shared, so Save stays off/);
  expect(screen.queryByText(/Checking what may be shared/)).toBeNull();
  expect(onError).toHaveBeenCalledTimes(1);
  expect(onReport).toHaveBeenCalledTimes(1);
  expect(onReport).toHaveBeenCalledWith(undefined);
});

it('the source-pack export waits for the preflight and stays off while a source blocks it', async () => {
  const user = userEvent.setup();
  vi.mocked(client.sourcePackPreview).mockResolvedValue({ fileName: 'Lantern-Notes-source-pack.tomestack.zip', sources: [], revisions: 1, drafts: 0, warnings: [] });
  vi.mocked(client.packagePreflight).mockResolvedValue(blocked);
  render(<SourcesPanel onError={() => {}} onStatus={() => {}} onOpenStudio={() => {}} />);

  const form = await screen.findByRole('region', { name: 'Share sources as a pack' });
  await user.click(within(form).getByRole('checkbox', { name: 'Lantern Notes' }));
  await user.click(within(form).getByRole('button', { name: 'Preview pack' }));
  const preview = await within(form).findByRole('region', { name: 'Source pack preview' });
  await within(form).findByRole('region', { name: 'Preflight: source pack' });
  expect(vi.mocked(client.packagePreflight)).toHaveBeenLastCalledWith({ kind: 'sourcePack', selection: ['src-1'] });
  expect(within(preview).getByRole<HTMLButtonElement>('button', { name: 'Save pack…' }).disabled).toBe(true);

  // Untick and tick again with nothing blocking: Save comes on.
  vi.mocked(client.packagePreflight).mockResolvedValue(clear);
  await user.click(within(form).getByRole('checkbox', { name: 'Lantern Notes' }));
  await user.click(within(form).getByRole('checkbox', { name: 'Lantern Notes' }));
  await user.click(within(form).getByRole('button', { name: 'Preview pack' }));
  const again = await within(form).findByRole('region', { name: 'Source pack preview' });
  await waitFor(() => expect(within(again).getByRole<HTMLButtonElement>('button', { name: 'Save pack…' }).disabled).toBe(false));
});

it('the campaign-pack export stays off while the preflight blocks and comes on when it is clear', async () => {
  const user = userEvent.setup();
  vi.mocked(client.listCampaigns).mockResolvedValue([{ id: 'camp-1', name: 'Lantern Table', rulesFamily: 'srd-5.1', allowedSources: ['src-1'] }]);
  vi.mocked(client.campaignPackPreview).mockResolvedValue({
    fileName: 'Lantern-Table-campaign-pack.tomestack.zip', campaignId: 'camp-1', name: 'Lantern Table', rulesFamily: 'srd-5.1', included: [], referenced: [], leftOut: [], revisions: 0, warnings: [],
  });
  vi.mocked(client.packagePreflight).mockResolvedValue(blocked);
  render(<CampaignsPanel rulesFamilies={[]} onError={() => {}} onStatus={() => {}} />);

  await user.click(await screen.findByRole('button', { name: 'Share Lantern Table…' }));
  const share = await screen.findByRole('region', { name: 'Share Lantern Table' });
  await within(share).findByRole('region', { name: 'Preflight: campaign pack' });
  expect(vi.mocked(client.packagePreflight)).toHaveBeenCalledWith({ kind: 'campaignPack', selection: ['camp-1'] });
  expect(within(share).getByRole<HTMLButtonElement>('button', { name: 'Save campaign pack…' }).disabled).toBe(true);

  cleanup();
  vi.mocked(client.packagePreflight).mockResolvedValue(clear);
  render(<CampaignsPanel rulesFamilies={[]} onError={() => {}} onStatus={() => {}} />);
  await user.click(await screen.findByRole('button', { name: 'Share Lantern Table…' }));
  const clearShare = await screen.findByRole('region', { name: 'Share Lantern Table' });
  await waitFor(() => expect(within(clearShare).getByRole<HTMLButtonElement>('button', { name: 'Save campaign pack…' }).disabled).toBe(false));
});

it('the campaign-pack share asks again when the campaign is edited and saved while it is open', async () => {
  const user = userEvent.setup();
  const before = { id: 'camp-1', name: 'Lantern Table', rulesFamily: 'srd-5.1' as const, allowedSources: ['src-1'] };
  const after = { ...before, allowedSources: [], updatedAt: 't2' };
  vi.mocked(client.listCampaigns).mockResolvedValue([before]);
  vi.mocked(client.saveCampaign).mockResolvedValue(after as never);
  vi.mocked(client.campaignPackPreview).mockResolvedValue({
    fileName: 'Lantern-Table-campaign-pack.tomestack.zip', campaignId: 'camp-1', name: 'Lantern Table', rulesFamily: 'srd-5.1', included: [], referenced: [], leftOut: [], revisions: 0, warnings: [],
  });
  vi.mocked(client.packagePreflight).mockResolvedValue(blocked);
  render(<CampaignsPanel rulesFamilies={[]} onError={() => {}} onStatus={() => {}} />);

  await user.click(await screen.findByRole('button', { name: 'Share Lantern Table…' }));
  const share = await screen.findByRole('region', { name: 'Share Lantern Table' });
  await within(share).findByRole('region', { name: 'Preflight: campaign pack' });
  expect(vi.mocked(client.packagePreflight)).toHaveBeenCalledTimes(1);
  expect(within(share).getByRole<HTMLButtonElement>('button', { name: 'Save campaign pack…' }).disabled).toBe(true);

  // Edit the same campaign while the share form stays open, untick the source and save.
  vi.mocked(client.listCampaigns).mockResolvedValue([after]);
  vi.mocked(client.packagePreflight).mockResolvedValue(clear);
  await user.click(screen.getByRole('button', { name: 'Edit Lantern Table' }));
  await user.click(await screen.findByRole('checkbox', { name: /Lantern Notes/ }));
  await user.click(screen.getByRole('button', { name: 'Save campaign' }));

  await waitFor(() => expect(vi.mocked(client.packagePreflight)).toHaveBeenCalledTimes(2));
  expect(vi.mocked(client.packagePreflight)).toHaveBeenLastCalledWith({ kind: 'campaignPack', selection: ['camp-1'] });
  const again = await screen.findByRole('region', { name: 'Share Lantern Table' });
  await waitFor(() => expect(within(again).getByRole<HTMLButtonElement>('button', { name: 'Save campaign pack…' }).disabled).toBe(false));
});

it('a campaign-pack preview that answers late does not overwrite the one asked after the save', async () => {
  const user = userEvent.setup();
  const before = { id: 'camp-1', name: 'Lantern Table', rulesFamily: 'srd-5.1' as const, allowedSources: ['src-1'] };
  const after = { ...before, allowedSources: [], updatedAt: 't2' };
  const preview = (fileName: string) => ({
    fileName, campaignId: 'camp-1', name: 'Lantern Table', rulesFamily: 'srd-5.1' as const, included: [], referenced: [], leftOut: [], revisions: 0, warnings: [],
  });
  let answerFirst: (value: ReturnType<typeof preview>) => void = () => {};
  vi.mocked(client.listCampaigns).mockResolvedValue([before]);
  vi.mocked(client.saveCampaign).mockResolvedValue(after as never);
  vi.mocked(client.campaignPackPreview)
    .mockReturnValueOnce(new Promise((resolve) => { answerFirst = resolve; }))
    .mockResolvedValue(preview('New-pack.zip'));
  vi.mocked(client.packagePreflight).mockResolvedValue(clear);
  render(<CampaignsPanel rulesFamilies={[]} onError={() => {}} onStatus={() => {}} />);

  await user.click(await screen.findByRole('button', { name: 'Share Lantern Table…' }));
  const share = await screen.findByRole('region', { name: 'Share Lantern Table' });
  vi.mocked(client.listCampaigns).mockResolvedValue([after]);
  await user.click(screen.getByRole('button', { name: 'Edit Lantern Table' }));
  await user.click(await screen.findByRole('checkbox', { name: /Lantern Notes/ }));
  await user.click(screen.getByRole('button', { name: 'Save campaign' }));
  await within(share).findByText(/New-pack\.zip/);

  await act(async () => {
    answerFirst(preview('Old-pack.zip')); // the pre-save answer arrives last
  });
  expect(within(screen.getByRole('region', { name: 'Share Lantern Table' })).queryByText(/Old-pack\.zip/)).toBeNull();
  expect(within(screen.getByRole('region', { name: 'Share Lantern Table' })).getByText(/New-pack\.zip/)).toBeTruthy();
});

it('the character-package export checks a share, and a backup keeps its own rule', async () => {
  const user = userEvent.setup();
  vi.mocked(client.previewExport).mockResolvedValue({ purpose: 'share', fileName: 'Pell.tomestack.zip', characters: ['ch-1'], included: [], omitted: [], gapNotes: 0 });
  vi.mocked(client.packagePreflight).mockResolvedValue(blocked);
  render(<ExportPanel characterId="ch-1" active updatedAt="t1" onError={() => {}} onStatus={() => {}} />);

  // A backup is personal: no preflight, and Export is on.
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Export package' }).disabled).toBe(false);
  expect(vi.mocked(client.packagePreflight)).not.toHaveBeenCalled();

  await user.click(screen.getByRole('radio', { name: /Share with someone/ }));
  await screen.findByRole('region', { name: 'Preflight: shared package' });
  expect(vi.mocked(client.packagePreflight)).toHaveBeenCalledWith({ kind: 'package', selection: ['ch-1'] });
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Export package' }).disabled).toBe(true);
});

it('the sheet export checks a share, and a personal copy keeps its own rule', async () => {
  const user = userEvent.setup();
  vi.mocked(client.previewVttExport).mockImplementation((_id, target, purpose) =>
    Promise.resolve({ token: 'tok', target, fileName: 'Pell.sheet.json', bytes: 10, adapterVersion: '1', purpose, dropped: [], notices: [], differences: [], warnings: [] }),
  );
  vi.mocked(client.packagePreflight).mockResolvedValue(blocked);
  render(<VttExportPanel characterId="ch-1" onError={() => {}} onStatus={() => {}} />);

  // Nothing is asked until the user previews, and a share then lists what is left out and blocks.
  expect(vi.mocked(client.packagePreflight)).not.toHaveBeenCalled();
  await user.click(screen.getByRole('radio', { name: /TomeStack sheet \(JSON\)/ }));
  await user.click(screen.getByRole('button', { name: 'Preview export' }));
  const preview = await screen.findByRole('region', { name: 'Export preview' });
  await within(preview).findByRole('region', { name: 'Preflight: shared sheet' });
  expect(vi.mocked(client.packagePreflight)).toHaveBeenCalledWith({ kind: 'sheet', selection: ['ch-1'] });
  expect(within(preview).getByRole<HTMLButtonElement>('button', { name: 'Save export file…' }).disabled).toBe(true);

  // Personal: the share rule is not asked, and Save is on.
  vi.mocked(client.packagePreflight).mockClear();
  await user.click(screen.getByRole('radio', { name: /Personal copy/ }));
  await user.click(screen.getByRole('button', { name: 'Preview export' }));
  const personal = await screen.findByRole('region', { name: 'Export preview' });
  expect(within(personal).queryByRole('region', { name: 'Preflight: shared sheet' })).toBeNull();
  expect(vi.mocked(client.packagePreflight)).not.toHaveBeenCalled();
  expect(within(personal).getByRole<HTMLButtonElement>('button', { name: 'Save export file…' }).disabled).toBe(false);
});
