import { useEffect, useState } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { LibraryBackupPreview, PackagePreview, SourceChoice } from '../api/types';
import { SourceDecision } from './ImportPreview';

const megabytes = (bytes: number) => `${(bytes / (1024 * 1024)).toFixed(1)} MB`;

const needsDesktop = (error: unknown, what: string) =>
  error instanceof TomeStackError && error.code === 'unsupported' ? new Error(`${what} needs the TomeStack desktop app (its Save and Open dialogs).`) : error;

interface Props {
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** After a restore: the character list and open screens may have changed. */
  onRestored: () => void;
}

/**
 * M2.1 (SPEC P-02, Q-01): "Back up everything" writes the whole library, drafts, unused homebrew and PDF copies included,
 * to a file you choose; "Restore full backup" checks such a file completely, shows what it would change, and restores
 * it only when you confirm. Different from a character's export on its sheet: that one is for one character, or for
 * sharing, and never includes PDFs.
 */
export function BackupsPanel({ onError, onStatus, onRestored }: Props) {
  const [contents, setContents] = useState<LibraryBackupPreview>();
  const [busy, setBusy] = useState<'backup' | 'choose' | 'restore'>();
  const [restore, setRestore] = useState<{ token: string; fileName: string; preview: PackagePreview }>();
  const [choices, setChoices] = useState<Record<string, SourceChoice>>({});

  useEffect(() => {
    client.libraryBackupPreview().then(setContents).catch(onError);
  }, [onError]);

  async function backUp() {
    setBusy('backup');
    try {
      const outcome = await client.saveLibraryBackup();
      if (outcome.saved) {
        const left = outcome.warnings.map((w) => ` ${w.message}`).join('');
        onStatus(`Saved the full backup ${outcome.fileName} (${megabytes(outcome.bytes)}).${left}`);
        setContents(outcome.contents);
      }
    } catch (error) {
      onError(needsDesktop(error, 'Backing up everything'));
    } finally {
      setBusy(undefined);
    }
  }

  async function choose() {
    setBusy('choose');
    try {
      const chosen = await client.chooseLibraryRestore();
      if (chosen.chosen) {
        setChoices({});
        setRestore(chosen);
      }
    } catch (error) {
      onError(needsDesktop(error, 'Restoring a full backup'));
    } finally {
      setBusy(undefined);
    }
  }

  async function apply() {
    if (!restore) return;
    setBusy('restore');
    try {
      const result = await client.applyLibraryRestore(restore.token, choices);
      setRestore(undefined);
      onRestored();
      setContents(await client.libraryBackupPreview());
      onStatus(
        `Restored ${restore.fileName}: ${result.added} added, ${result.replaced} replaced, ${result.unchanged} unchanged, ${result.pdfsCopied} PDF(s) copied.` +
          (result.safetyCopy ? ` Your data as it was before is saved as ${result.safetyCopy} in your data folder.` : '') +
          result.warnings.map((w) => ` ${w.message}`).join(''),
      );
    } catch (error) {
      onError(error);
    } finally {
      setBusy(undefined);
    }
  }

  const preview = restore?.preview;
  const counts = new Map<string, number>();
  for (const item of preview?.items ?? []) counts.set(`${item.kind}:${item.action}`, (counts.get(`${item.kind}:${item.action}`) ?? 0) + 1);
  const kinds = [...new Set((preview?.items ?? []).map((i) => i.kind))];
  const decisions = (preview?.items ?? []).filter((item) => item.kind === 'source' && item.changes && item.changes.length > 0);
  const undecided = decisions.filter((item) => !choices[item.id]).length;
  const replaced = (preview?.items ?? []).filter((i) => i.action === 'replace' && i.kind !== 'source');

  return (
    <section className="panel" aria-labelledby="backups-heading">
      <h2 id="backups-heading">Backups</h2>

      <section aria-labelledby="backup-everything-heading">
        <h3 id="backup-everything-heading">Back up everything</h3>
        <p>
          One file with your whole library: every character, campaign and gap note, all your homebrew (drafts and unused
          entries too), and the PDFs TomeStack keeps a copy of. Keep it somewhere other than this computer. It is personal:
          do not share it.
        </p>
        {contents && (
          <ul aria-label="What the backup contains">
            <li>
              {contents.characters} character(s), {contents.campaigns} campaign(s), {contents.gapNotes} gap note(s)
            </li>
            <li>
              {contents.sources} source(s), {contents.publishedRevisions} published and {contents.draftRevisions} draft entries (the bundled SRD
              content is not repeated: every install has it)
            </li>
            <li>
              {contents.managedPdfs} PDF copy/copies ({megabytes(contents.managedPdfBytes)})
              {contents.linkedPdfs > 0 && `; ${contents.linkedPdfs} linked PDF(s) stay where they are and are not included`}
            </li>
            <li>Not included: text read from PDFs and import jobs (import them again from the PDF).</li>
          </ul>
        )}
        {contents && contents.unreadable.length > 0 && (
          <p className="warn" role="note">
            These PDF copies are missing or damaged and would be left out: {contents.unreadable.join(', ')}. Attach them again.
          </p>
        )}
        <button type="button" onClick={backUp} disabled={busy !== undefined}>
          {busy === 'backup' ? 'Backing up…' : 'Back up everything…'}
        </button>
      </section>

      <section aria-labelledby="restore-heading">
        <h3 id="restore-heading">Restore full backup</h3>
        <p>
          Checks the whole file first, PDFs included, and shows what it would add or replace. Nothing in this library is
          deleted. Before anything is replaced, TomeStack saves a copy of your current data in the backups folder.
        </p>
        <button type="button" onClick={choose} disabled={busy !== undefined}>
          {busy === 'choose' ? 'Checking…' : 'Choose a full backup…'}
        </button>

        {restore && preview && (
          <section aria-labelledby="restore-preview-heading">
            <h4 id="restore-preview-heading">Restore {restore.fileName}</h4>
            {preview.errors.length > 0 && (
              <div role="alert">
                <p>This backup cannot be restored:</p>
                <ul className="errors">
                  {preview.errors.map((e, i) => (
                    <li key={i}>{e.message}</li>
                  ))}
                </ul>
              </div>
            )}
            {kinds.length > 0 && (
              <table>
                <caption>What the restore would do</caption>
                <thead>
                  <tr>
                    <th scope="col">Type</th>
                    <th scope="col">Add</th>
                    <th scope="col">Replace</th>
                    <th scope="col">Unchanged</th>
                  </tr>
                </thead>
                <tbody>
                  {kinds.map((kind) => (
                    <tr key={kind}>
                      <th scope="row">{kind}</th>
                      <td>{counts.get(`${kind}:add`) ?? 0}</td>
                      <td>{counts.get(`${kind}:replace`) ?? 0}</td>
                      <td>{counts.get(`${kind}:unchanged`) ?? 0}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            {replaced.length > 0 && (
              <p className="warn">
                Replaced with the backup&apos;s copy: {replaced.map((i) => i.name).join(', ')}.
              </p>
            )}
            {preview.warnings.length > 0 && (
              <ul className="warnings">
                {preview.warnings.map((w, i) => (
                  <li key={i}>{w.message}</li>
                ))}
              </ul>
            )}
            {decisions.map((item) => (
              <SourceDecision key={item.id} item={item} choice={choices[item.id]} onChoose={(choice) => setChoices((c) => ({ ...c, [item.id]: choice }))} />
            ))}
            {undecided > 0 && (
              <p className="hint" id="restore-undecided">
                Choose a version for each source above before restoring.
              </p>
            )}
            <div className="actions">
              <button
                type="button"
                onClick={apply}
                disabled={!preview.canApply || busy !== undefined || undecided > 0}
                aria-describedby={undecided > 0 ? 'restore-undecided' : undefined}
              >
                {busy === 'restore' ? 'Restoring…' : 'Restore'}
              </button>
              <button type="button" onClick={() => setRestore(undefined)} disabled={busy === 'restore'}>
                Cancel
              </button>
            </div>
          </section>
        )}
      </section>

      <p className="hint">
        To give one character to someone, use Export on its sheet and choose &quot;Share with someone&quot;: a share leaves out content you may not
        share, and never includes PDFs or gap notes.
      </p>
    </section>
  );
}
