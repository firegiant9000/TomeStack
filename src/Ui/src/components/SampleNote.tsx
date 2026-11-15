import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';

interface Props {
  characterId: string;
  name: string;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** After the sample was removed: the app goes home and refreshes the list. */
  onRemoved: () => void;
  /** After the sample was archived instead: the list and the sheet reload. */
  onArchiveChanged: () => Promise<void> | void;
}

const noteKey = (id: string) => `tomestack.sampleNote.${id}`;

function noteDismissed(id: string): boolean {
  try {
    return globalThis.localStorage?.getItem(noteKey(id)) === 'dismissed';
  } catch {
    return false;
  }
}

/**
 * Wave 3 3.2: a note under the sheet header on the sample character. "Remove sample" asks first (inline, focus moves to
 * "Keep"; Keep or Escape returns it to "Remove sample"). A sample that has snapshots cannot be deleted: the service's
 * words show, with "Archive instead". The dismissal is a preference of this machine, in the page's own storage.
 */
export function SampleNote({ characterId, name, onError, onStatus, onRemoved, onArchiveChanged }: Props) {
  const [dismissed, setDismissed] = useState(() => noteDismissed(characterId));
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [blocked, setBlocked] = useState<string>();
  const removeButton = useRef<HTMLButtonElement>(null);
  const keepButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (confirming) keepButton.current?.focus();
  }, [confirming]);

  if (dismissed) return null;

  function dismiss() {
    try {
      globalThis.localStorage?.setItem(noteKey(characterId), 'dismissed');
    } catch {
      // storage unavailable: the note hides for this sheet only
    }
    setDismissed(true);
  }

  function keep() {
    setConfirming(false);
    removeButton.current?.focus();
  }

  async function remove() {
    if (busy) return;
    setBusy(true);
    try {
      await client.deleteSample(characterId);
      try {
        globalThis.localStorage?.removeItem(`tomestack.sheetTab.${characterId}`);
        globalThis.localStorage?.removeItem(noteKey(characterId));
      } catch {
        // storage unavailable: nothing to clear
      }
      onRemoved();
    } catch (error) {
      if (error instanceof TomeStackError && error.code === 'character.sample-has-snapshots') {
        setBlocked(error.message);
        setConfirming(false);
      } else {
        onError(error);
      }
    } finally {
      setBusy(false);
    }
  }

  async function archiveInstead() {
    if (busy) return;
    setBusy(true);
    try {
      await client.archiveCharacter(characterId);
      setBlocked(undefined);
      onStatus(`Archived ${name}. It is under "Archived" in the character list, and nothing was deleted.`);
      await onArchiveChanged();
    } catch (error) {
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section
      className="sample-note"
      aria-label="Sample character"
      onKeyDown={(event) => {
        if (event.key === 'Escape' && confirming) {
          event.preventDefault();
          keep();
        }
      }}
    >
      <p>This is a sample character.</p>
      <div className="actions">
        <button type="button" ref={removeButton} disabled={busy} aria-expanded={confirming} onClick={() => setConfirming(true)}>
          Remove sample
        </button>
        <button type="button" onClick={dismiss}>
          Dismiss sample note
        </button>
      </div>
      {confirming && (
        <div role="alertdialog" aria-label={`Remove ${name}?`} className="play-panel">
          <p>Remove {name}? This cannot be undone.</p>
          <div className="actions">
            <button type="button" ref={keepButton} disabled={busy} onClick={keep}>
              Keep
            </button>
            <button type="button" disabled={busy} onClick={remove}>
              Remove
            </button>
          </div>
        </div>
      )}
      {blocked && (
        <div role="alert">
          <p>{blocked}</p>
          <button type="button" disabled={busy} onClick={archiveInstead}>
            Archive instead
          </button>
        </div>
      )}
    </section>
  );
}
