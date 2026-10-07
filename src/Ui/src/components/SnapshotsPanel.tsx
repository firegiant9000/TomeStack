import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { Character, CharacterView, Currency, RestorePreview, SnapshotSummary } from '../api/types';

interface Props {
  character: Character;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

const when = (iso: string) => new Date(iso).toLocaleString();

/** "3 gp, 12 sp": the coins held, largest first; none at all reads "no coins". */
const coinsText = (c: Currency) => {
  const held = (['pp', 'gp', 'ep', 'sp', 'cp'] as const).filter((k) => c[k] > 0).map((k) => `${c[k].toLocaleString('en-US')} ${k}`);
  return held.length > 0 ? held.join(', ') : 'no coins';
};

/**
 * M5 slice 8 (B08; owner decision LIVING_SPECS D14): snapshots of this character, taken by hand. A restore shows what
 * would change first, like an update review; "Restore" then takes an undo snapshot and restores in one step, so a
 * restore can itself be restored. Snapshots stay on this machine: no export, share or full backup includes them.
 * WCAG 2.4.3: focus moves to the preview's heading, and back to the snapshot's button when it is cancelled.
 */
export function SnapshotsPanel({ character, onChanged, onError, onStatus }: Props) {
  const [snapshots, setSnapshots] = useState<SnapshotSummary[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [label, setLabel] = useState('');
  const [preview, setPreview] = useState<RestorePreview>();
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);
  const previewHeading = useRef<HTMLHeadingElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    let current = true;
    client
      .snapshots(character.id)
      .then((page) => {
        if (!current) return;
        setSnapshots(page.items);
        setHasMore(page.hasMore);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [character.id, reload, onError]);

  useEffect(() => {
    if (preview) previewHeading.current?.focus();
  }, [preview]);

  async function run(action: () => Promise<void>) {
    if (busy) return;
    setBusy(true);
    try {
      await action();
    } catch (error) {
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  const take = () =>
    run(async () => {
      const taken = await client.takeSnapshot(character.id, label.trim() || undefined);
      setLabel('');
      setReload((n) => n + 1);
      onStatus(`Took a snapshot of ${taken.name}${taken.label ? `: ${taken.label}` : ''}.`);
    });

  const restore = (token: string) =>
    run(async () => {
      try {
        const result = await client.restoreSnapshot(token);
        setReload((n) => n + 1);
        onChanged(result.view);
        onStatus(`Restored the snapshot. The state before it is kept as a snapshot too, so you can go back.`);
      } finally {
        // The token is used either way (review fix): a failed restore closes the preview, so "Restore" is previewed again.
        setPreview(undefined);
        heading.current?.focus(); // WCAG 2.4.3: focus stays in this section, not on the page body
      }
    });

  // Snapshots are never removed, so older ones come a page at a time, after the oldest one shown.
  const showOlder = () =>
    run(async () => {
      const page = await client.snapshots(character.id, snapshots[snapshots.length - 1]?.id);
      setSnapshots((shown) => [...shown, ...page.items]);
      setHasMore(page.hasMore);
      onStatus(`Showing ${page.items.length} older snapshot${page.items.length === 1 ? '' : 's'}.`);
    });

  const name = (s: SnapshotSummary) => s.label ?? (s.reason === 'beforeRestore' ? 'Before a restore' : 'Snapshot');

  return (
    <section aria-labelledby="snapshots-heading">
      <h3 id="snapshots-heading" tabIndex={-1} ref={heading}>
        Snapshots
      </h3>
      <p className="hint">A copy of this character to come back to. Snapshots stay on this computer; exports and full backups leave them out. Session notes are not copied into a snapshot, and a restore keeps the notes you have.</p>
      <div className="inline-form">
        <label className="field">
          Snapshot name (optional)
          <input value={label} maxLength={200} onChange={(e) => setLabel(e.target.value)} />
        </label>
        <button type="button" onClick={() => void take()} disabled={busy}>
          Take snapshot
        </button>
      </div>
      {snapshots.length === 0 ? (
        <p className="hint">No snapshots yet.</p>
      ) : (
        <ul>
          {snapshots.map((s) => (
            <li key={s.id}>
              {name(s)} <span className="hint">(level {s.level}, {when(s.createdAt)})</span>{' '}
              <button
                type="button"
                id={`snapshot-restore-${s.id}`}
                disabled={busy}
                aria-expanded={preview?.snapshot.id === s.id}
                onClick={() => void run(async () => setPreview(await client.restorePreview(character.id, s.id)))}
              >
                Restore {name(s)}…
              </button>
            </li>
          ))}
        </ul>
      )}
      {hasMore && (
        <button type="button" onClick={() => void showOlder()} disabled={busy}>
          Show older snapshots
        </button>
      )}
      {preview && (
        <div role="region" aria-labelledby="restore-heading" className="play-panel">
          <h4 id="restore-heading" tabIndex={-1} ref={previewHeading}>
            Restore {name(preview.snapshot)}?
          </h4>
          {preview.fields.length === 0 ? (
            <p>No calculated value changes.</p>
          ) : (
            <table>
              <caption>Calculated values that change</caption>
              <thead>
                <tr>
                  <th scope="col">Field</th>
                  <th scope="col">Now</th>
                  <th scope="col">After the restore</th>
                </tr>
              </thead>
              <tbody>
                {preview.fields.map((f) => (
                  <tr key={f.field}>
                    <td>{f.label}</td>
                    <td>{f.before}</td>
                    <td>{f.after}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p>
            {preview.added.length + preview.removed.length === 0
              ? 'The same content, choices and levels.'
              : `${preview.added.length} content reference(s) come back and ${preview.removed.length} go.`}{' '}
            {preview.playChanges ? 'Hit points, spent uses and conditions go back to the snapshot too.' : 'Play state is the same.'}
            {preview.nameAfter ? ` The name goes back to ${preview.nameAfter}.` : ''}
            {preview.exceptionsChange ? ' Recorded cross-family exceptions go back to the snapshot too.' : ''}
            {preview.currencyAfter && preview.currencyNow ? ` Coins go back to ${coinsText(preview.currencyAfter)} (now ${coinsText(preview.currencyNow)}).` : ''} Its campaign stays as it is now.
          </p>
          {(preview.campaignWarnings ?? []).length > 0 && (
            <ul className="warnings" aria-label="Campaign warnings after the restore">
              {preview.campaignWarnings!.map((d, i) => (
                <li key={`${d.code}-${i}`}>{d.message}</li>
              ))}
            </ul>
          )}
          {preview.newDiagnostics.length > 0 && (
            <ul className="warnings" aria-label="Problems after the restore">
              {preview.newDiagnostics.map((d, i) => (
                <li key={`${d.code}-${i}`}>{d.message}</li>
              ))}
            </ul>
          )}
          <div className="actions">
            <button type="button" disabled={busy} onClick={() => void restore(preview.token)}>
              Restore
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                const id = preview.snapshot.id;
                setPreview(undefined);
                document.getElementById(`snapshot-restore-${id}`)?.focus(); // back to the button that opened it
              }}
            >
              Keep the current state
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
