// @vitest-environment jsdom
// "Save for an older TomeStack" (D85) in "Back up everything": the target control, the change region and the confirmation.
// The client is mocked; values are invented.
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import type { LibraryBackupPreview } from '../api/types';
import { blockedHint, confirmLabel, describedBy, exportChange, failedLine, firstRadio, packageTargetsFixture } from '../test/downConversion';
import { BackupsPanel } from './BackupsPanel';

vi.mock('../api/client', () => ({
  client: {
    packageTargets: vi.fn(),
    libraryBackupPreview: vi.fn(),
    libraryBackupSaveAs: vi.fn(),
    chooseLibraryRestore: vi.fn(),
    applyLibraryRestore: vi.fn(),
    dailyCopies: vi.fn(),
    setDailyCopies: vi.fn(),
  },
}));

afterEach(cleanup);

const base: LibraryBackupPreview = {
  fileName: 'compat-backup.tomestack-library.zip',
  characters: 1,
  campaigns: 0,
  gapNotes: 0,
  sources: 1,
  publishedRevisions: 1,
  draftRevisions: 0,
  managedPdfs: 0,
  managedPdfBytes: 0,
  linkedPdfs: 0,
  unreadable: [],
};
const changesFor: Record<string, ReturnType<typeof exportChange>[]> = {};
const previewOf = (target?: string | null): LibraryBackupPreview =>
  target ? { ...base, target, changes: changesFor[target] ?? [], changesHash: `hash-${target}` } : base;

beforeEach(() => {
  for (const key of Object.keys(changesFor)) delete changesFor[key];
  vi.mocked(client.packageTargets).mockReset();
  vi.mocked(client.packageTargets).mockResolvedValue(packageTargetsFixture);
  vi.mocked(client.libraryBackupPreview).mockReset();
  vi.mocked(client.libraryBackupPreview).mockImplementation(async (target) => previewOf(target));
  vi.mocked(client.libraryBackupSaveAs).mockReset();
  vi.mocked(client.libraryBackupSaveAs).mockResolvedValue({ saved: false });
  vi.mocked(client.dailyCopies).mockResolvedValue({ on: false, copies: [], bytes: 0 });
});

async function openBackups(onError: (e: unknown) => void = () => {}) {
  const user = userEvent.setup();
  render(<BackupsPanel onError={onError} onStatus={() => {}} onRestored={() => {}} />);
  await screen.findByRole('list', { name: 'What the backup contains' });
  return user;
}
const backUp = () => screen.getByRole('button', { name: 'Back up everything…' }) as HTMLButtonElement;

it('re-runs the preview for the chosen release and again without a target for the first choice', async () => {
  const user = await openBackups();
  expect(vi.mocked(client.libraryBackupPreview).mock.lastCall).toEqual([]);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await waitFor(() => expect(vi.mocked(client.libraryBackupPreview).mock.lastCall).toEqual(['0.6.0']));
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  await user.click(screen.getByRole('radio', { name: firstRadio }));
  await waitFor(() => expect(vi.mocked(client.libraryBackupPreview).mock.lastCall).toEqual([]));
});

it('keeps focus on the radio that was chosen while the preview reloads', async () => {
  const user = await openBackups();
  const radio = await screen.findByRole('radio', { name: '0.6.0' });
  await user.click(radio);
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  expect(document.activeElement).toBe(radio);
});

it('disables the backup while something blocks, and describes why', async () => {
  changesFor['0.6.0'] = [exportChange('blocks', 'Compat Hero needs 0.7.0.')];
  const user = await openBackups();
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await screen.findByText('Compat Hero needs 0.7.0.');
  expect(backUp().disabled).toBe(true);
  expect(describedBy(backUp())).toBe(blockedHint);
});

it('needs the tick for a dropped item, and a new preview clears it', async () => {
  changesFor['0.6.0'] = [exportChange('dropped', 'Compat Tide Stance is left out.')];
  changesFor['0.5.0'] = [exportChange('dropped', 'Compat Steady Hands is left out.')];
  const user = await openBackups();
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await user.click(await screen.findByRole('checkbox', { name: confirmLabel }));
  expect(backUp().disabled).toBe(false);
  await user.click(screen.getByRole('radio', { name: '0.5.0' }));
  await screen.findByText('Compat Steady Hands is left out.');
  expect((screen.getByRole('checkbox', { name: confirmLabel }) as HTMLInputElement).checked).toBe(false);
  expect(backUp().disabled).toBe(true);
});

it('waits for the preview of a chosen release before the backup is possible', async () => {
  let settle: (p: LibraryBackupPreview) => void = () => {};
  const user = await openBackups();
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  vi.mocked(client.libraryBackupPreview).mockReturnValueOnce(new Promise<LibraryBackupPreview>((r) => (settle = r)));
  await user.click(screen.getByRole('radio', { name: '0.5.0' }));
  expect(backUp().disabled).toBe(true);
  settle(previewOf('0.5.0'));
  await waitFor(() => expect(backUp().disabled).toBe(false));
});

it('sends the release and the preview hash when a release is chosen, and nothing for the first choice', async () => {
  const user = await openBackups();
  await user.click(backUp());
  expect(vi.mocked(client.libraryBackupSaveAs).mock.lastCall).toEqual([]);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  await user.click(backUp());
  expect(vi.mocked(client.libraryBackupSaveAs).mock.lastCall).toEqual(['0.6.0', 'hash-0.6.0']);
});

it('says which TomeStack opens the default file, and shows nothing when the service does not say', async () => {
  vi.mocked(client.libraryBackupPreview).mockResolvedValue({ ...base, opensIn: '0.5.0' });
  await openBackups();
  expect(await screen.findByText('This file opens in TomeStack 0.5.0 or newer.')).toBeTruthy();
  cleanup();
  vi.mocked(client.libraryBackupPreview).mockResolvedValue(previewOf());
  await openBackups();
  expect(screen.queryByRole('region', { name: 'Compatibility' })).toBeNull();
});

it('shows no target control, and reports nothing, when the targets cannot be read', async () => {
  vi.mocked(client.packageTargets).mockRejectedValue(new Error('unsupported'));
  const onError = vi.fn();
  await openBackups(onError);
  await waitFor(() => expect(client.packageTargets).toHaveBeenCalled());
  expect(screen.queryByText('Which TomeStack will open it?')).toBeNull();
  expect(backUp().disabled).toBe(false);
  expect(onError).not.toHaveBeenCalled();
});

it('lets the last chosen release win when an earlier reply arrives late', async () => {
  changesFor['0.5.0'] = [exportChange('dropped', 'Compat Steady Hands is left out.')];
  const user = await openBackups();
  let late: (p: LibraryBackupPreview) => void = () => {};
  vi.mocked(client.libraryBackupPreview).mockReturnValueOnce(new Promise<LibraryBackupPreview>((r) => (late = r)));
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  await user.click(screen.getByRole('radio', { name: '0.5.0' }));
  await screen.findByText('Compat Steady Hands is left out.');
  late(previewOf('0.6.0'));
  await new Promise((r) => setTimeout(r, 20));
  expect(screen.getByRole('region', { name: 'Changed for TomeStack 0.5.0' })).toBeTruthy();
  expect(screen.queryByRole('region', { name: 'Changed for TomeStack 0.6.0' })).toBeNull();
  await user.click(screen.getByRole('checkbox', { name: confirmLabel }));
  await user.click(backUp());
  expect(vi.mocked(client.libraryBackupSaveAs).mock.lastCall).toEqual(['0.5.0', 'hash-0.5.0']);
});

it("a restore that finishes after another release was chosen does not overwrite that release's preview", async () => {
  changesFor['0.6.0'] = [exportChange('dropped', 'Compat Tide Stance is left out.')];
  vi.mocked(client.chooseLibraryRestore).mockResolvedValue({
    chosen: true,
    token: 't',
    fileName: 'compat-restore.zip',
    preview: { items: [], errors: [], warnings: [], canApply: true },
  } as unknown as Awaited<ReturnType<typeof client.chooseLibraryRestore>>);
  let finish: (r: Awaited<ReturnType<typeof client.applyLibraryRestore>>) => void = () => {};
  vi.mocked(client.applyLibraryRestore).mockReturnValue(new Promise((r) => (finish = r)));
  const user = await openBackups();
  await user.click(screen.getByRole('button', { name: 'Choose a full backup…' }));
  await user.click(await screen.findByRole('button', { name: 'Restore' }));
  await user.click(await screen.findByRole('radio', { name: '0.6.0' })); // the radios stay usable during the restore
  await user.click(await screen.findByRole('checkbox', { name: confirmLabel }));
  finish({ added: 0, replaced: 0, unchanged: 0, pdfsCopied: 0, warnings: [] });
  await waitFor(() => expect(vi.mocked(client.libraryBackupPreview).mock.calls.length).toBeGreaterThan(2));
  await screen.findByRole('region', { name: 'Changed for TomeStack 0.6.0' });
  expect((screen.getByRole('checkbox', { name: confirmLabel }) as HTMLInputElement).checked).toBe(false); // a new preview: the tick is cleared
  expect(vi.mocked(client.libraryBackupPreview).mock.lastCall).toEqual(['0.6.0']);
  await user.click(screen.getByRole('checkbox', { name: confirmLabel }));
  await user.click(backUp());
  expect(vi.mocked(client.libraryBackupSaveAs).mock.lastCall).toEqual(['0.6.0', 'hash-0.6.0']);
});

it('says so plainly, and keeps the backup off, when the preview for a chosen release fails', async () => {
  vi.mocked(client.libraryBackupPreview).mockImplementation(async (target) => {
    if (target) throw new Error('boom');
    return previewOf();
  });
  const onError = vi.fn();
  const user = await openBackups(onError);
  await user.click(await screen.findByRole('radio', { name: '0.6.0' }));
  expect(await screen.findByText(failedLine('0.6.0'))).toBeTruthy();
  expect(backUp().disabled).toBe(true);
  expect(onError).not.toHaveBeenCalled();
});

it('tells how to restore a copy made for an older TomeStack, and how to move to a new PC', async () => {
  await openBackups();
  expect(screen.getByText('Restoring a copy made for an older TomeStack: restore it into a new, empty data folder on that version.')).toBeTruthy();
  expect(
    screen.getByText(
      'New version or new PC? A newer version on the same PC opens your library as it is; you do not need to export anything. To move to a new PC, choose Back up everything… here, copy the file across, then on the new PC choose Choose a full backup… and Restore.',
    ),
  ).toBeTruthy();
});
