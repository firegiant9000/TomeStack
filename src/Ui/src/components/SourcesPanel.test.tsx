// @vitest-environment jsdom
// "Save for an older TomeStack" (D85) in the source pack form: the target control, the change region and the confirmation.
// The client is mocked; values are invented.
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { SourcePackPreview, SourceRecord } from '../api/types';
import { blockedHint, confirmLabel, describedBy, exportChange, failedLine, firstRadio, packageTargetsFixture } from '../test/downConversion';
import { SourcesPanel } from './SourcesPanel';

vi.mock('../api/client', () => ({
  client: {
    listSources: vi.fn(),
    attachment: vi.fn(),
    packageTargets: vi.fn(),
    sourcePackPreview: vi.fn(),
    saveSourcePackAs: vi.fn(),
    exportSourcePack: vi.fn(),
  },
}));

afterEach(cleanup);

const source: SourceRecord = {
  id: 'compat-homebrew-a',
  title: 'Compat Homebrew A',
  publisher: 'Compat Press',
  rulesFamilies: ['srd-5.1'],
  editionVersion: 'homebrew',
  license: 'own work',
  redistributable: true,
  origin: 'local',
  shareConfirmedAt: '2026-10-01T00:00:00Z',
};

const base = { fileName: 'compat.tomestack-sources.zip', sources: [], revisions: 2, drafts: 0, warnings: [] };
const changesFor: Record<string, ReturnType<typeof exportChange>[]> = {};
const previewOf = (target?: string | null): SourcePackPreview =>
  target ? { ...base, target, changes: changesFor[target] ?? [], changesHash: `hash-${target}` } : base;

beforeEach(() => {
  for (const key of Object.keys(changesFor)) delete changesFor[key];
  vi.mocked(client.listSources).mockResolvedValue([source]);
  vi.mocked(client.attachment).mockResolvedValue(undefined as unknown as Awaited<ReturnType<typeof client.attachment>>);
  vi.mocked(client.packageTargets).mockReset();
  vi.mocked(client.packageTargets).mockResolvedValue(packageTargetsFixture);
  vi.mocked(client.sourcePackPreview).mockReset();
  vi.mocked(client.sourcePackPreview).mockImplementation(async (_ids, target) => previewOf(target));
  vi.mocked(client.saveSourcePackAs).mockReset();
  vi.mocked(client.saveSourcePackAs).mockResolvedValue({ saved: true, fileName: 'compat.zip' } as Awaited<ReturnType<typeof client.saveSourcePackAs>>);
});

async function openPreview(onError: (e: unknown) => void = () => {}) {
  const user = userEvent.setup();
  render(<SourcesPanel onError={onError} onStatus={() => {}} onOpenStudio={() => {}} />);
  await user.click(await screen.findByRole('checkbox', { name: 'Compat Homebrew A' }));
  await user.click(screen.getByRole('button', { name: 'Preview pack' }));
  await screen.findByRole('region', { name: 'Source pack preview' });
  return user;
}
const save = () => screen.getByRole('button', { name: 'Save pack…' }) as HTMLButtonElement;

it('re-runs the preview for the chosen release and again without a target for the first choice', async () => {
  const user = await openPreview();
  expect(vi.mocked(client.sourcePackPreview).mock.lastCall).toEqual([['compat-homebrew-a']]);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await waitFor(() => expect(vi.mocked(client.sourcePackPreview).mock.lastCall).toEqual([['compat-homebrew-a'], '0.6.0']));
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  await user.click(screen.getByRole('radio', { name: firstRadio }));
  await waitFor(() => expect(vi.mocked(client.sourcePackPreview).mock.lastCall).toEqual([['compat-homebrew-a']]));
});

it('keeps focus on the radio that was chosen while the preview reloads', async () => {
  const user = await openPreview();
  const radio = await screen.findByRole('radio', { name: '0.6.0' });
  await user.click(radio);
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  expect(document.activeElement).toBe(radio);
});

it('disables Save while something blocks, and describes why', async () => {
  changesFor['0.6.0'] = [exportChange('blocks', 'Compat Homebrew A holds a rule 0.6.0 cannot read.')];
  const user = await openPreview();
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await screen.findByText('Compat Homebrew A holds a rule 0.6.0 cannot read.');
  expect(save().disabled).toBe(true);
  expect(describedBy(save())).toBe(blockedHint);
});

it('needs the tick for a dropped item, and a new preview clears it', async () => {
  changesFor['0.6.0'] = [exportChange('dropped', 'Compat Tide Stance is left out.')];
  changesFor['0.5.0'] = [exportChange('dropped', 'Compat Steady Hands is left out.')];
  const user = await openPreview();
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await user.click(await screen.findByRole('checkbox', { name: confirmLabel }));
  expect(save().disabled).toBe(false);
  await user.click(screen.getByRole('radio', { name: '0.5.0' }));
  await screen.findByText('Compat Steady Hands is left out.');
  expect((screen.getByRole('checkbox', { name: confirmLabel }) as HTMLInputElement).checked).toBe(false);
  expect(save().disabled).toBe(true);
});

it('sends the release and the preview hash when a release is chosen, and neither for the first choice', async () => {
  const user = await openPreview();
  await user.click(save());
  expect(vi.mocked(client.saveSourcePackAs).mock.lastCall).toEqual([['compat-homebrew-a']]);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  await user.click(save());
  expect(vi.mocked(client.saveSourcePackAs).mock.lastCall).toEqual([['compat-homebrew-a'], '0.6.0', 'hash-0.6.0']);
});

it('says which TomeStack opens the default file, and shows nothing when the service does not say', async () => {
  vi.mocked(client.sourcePackPreview).mockResolvedValue({ ...base, opensIn: '0.5.0' });
  await openPreview();
  expect(await screen.findByText('This file opens in TomeStack 0.5.0 or newer.')).toBeTruthy();
  cleanup();
  vi.mocked(client.sourcePackPreview).mockResolvedValue(previewOf());
  await openPreview();
  expect(screen.queryByRole('region', { name: 'Compatibility' })).toBeNull();
});

it('shows no target control, and reports nothing, when the targets cannot be read', async () => {
  vi.mocked(client.packageTargets).mockRejectedValue(new Error('unsupported'));
  const onError = vi.fn();
  await openPreview(onError);
  await waitFor(() => expect(client.packageTargets).toHaveBeenCalled());
  expect(screen.queryByText('Which TomeStack will open it?')).toBeNull();
  expect(save().disabled).toBe(false);
  expect(onError).not.toHaveBeenCalled();
});

it('says so plainly, and has no Save, when the preview for a chosen release fails', async () => {
  vi.mocked(client.sourcePackPreview).mockImplementation(async (_ids, target) => {
    if (target) throw new Error('boom');
    return previewOf();
  });
  const onError = vi.fn();
  const user = await openPreview(onError);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  expect(await screen.findByText(failedLine('0.6.0'))).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Save pack…' })).toBeNull();
  expect(onError).not.toHaveBeenCalled();
});
