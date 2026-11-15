import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';

interface Props {
  characterId: string;
  name: string;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** After the sample was removed: the app goes home and refreshes the list. */
  onRemoved: () => Promise<void> | void;
  /** After the sample was archived instead: the list and the sheet reload. */
  onArchiveChanged: () => Promise<void> | void;
  /** An archived sample cannot be removed here: the note is not offered. */
  archived?: boolean;
  /** The sheet's heading: the stable place focus goes when this note's controls go away. */
  focusHeading: () => void;
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
export function SampleNote({ characterId, name, onError, onStatus, onRemoved, onArchiveChanged, archived, focusHeading }: Props) {
  const [dismissed, setDismissed] = useState(() => noteDismissed(characterId));
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  // A fresh object per refusal, so a second refusal with the same words still moves focus to "Archive instead".
  const [blocked, setBlocked] = useState<{ message: string }>();
  const removeButton = useRef<HTMLButtonElement>(null);
  const keepButton = useRef<HTMLButtonElement>(null);
  const archiveButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (confirming) keepButton.current?.focus();
  }, [confirming]);

  useEffect(() => {
    if (blocked) archiveButton.current?.focus();
  }, [blocked]);

  if (dismissed || archived) return null;

  function dismiss() {
    try {
      globalThis.localStorage?.setItem(noteKey(characterId), 'dismissed');
    } catch {
      // storage unavailable: the note hides for this sheet only
    }
    focusHeading();
    setDismissed(true);
  }

  function keep() {
    if (busy) return;
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
      await onRemoved();
    } catch (error) {
      if (error instanceof TomeStackError && error.code === 'character.sample-has-snapshots') {
        setBlocked({ message: error.message });
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
      focusHeading();
      await onArchiveChanged();
      focusHeading();
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
        <button type="button" ref={removeButton} aria-disabled={busy || undefined} aria-expanded={confirming} onClick={() => !busy && setConfirming(true)}>
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
            <button type="button" ref={keepButton} aria-disabled={busy || undefined} onClick={keep}>
              Keep
            </button>
            <button type="button" aria-disabled={busy || undefined} onClick={remove}>
              Remove
            </button>
          </div>
        </div>
      )}
      {blocked && (
        <div role="alert">
          <p>{blocked.message}</p>
          <button type="button" ref={archiveButton} aria-disabled={busy || undefined} onClick={archiveInstead}>
            Archive instead
          </button>
        </div>
      )}
    </section>
  );
}
