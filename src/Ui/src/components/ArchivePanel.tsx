import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { client } from '../api/client';
import type { ArchivePreview, Character } from '../api/types';

interface Props {
  character: Character;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** After archiving or unarchiving: the list and the sheet reload. */
  onChanged: () => Promise<void> | void;
}

/**
 * SPEC C-08: archive a character instead of deleting it. "Archive…" first shows what happens (the preview), and only
 * the "Archive" confirmation archives it. Nothing is removed; an archived character can be brought back.
 * WCAG 2.4.3: focus moves into the confirmation, back to "Archive…" when it is dismissed (Escape or "Keep it"), and to
 * the control that replaces the pressed one after archiving or unarchiving. A request in flight disables the buttons.
 */
export function ArchivePanel({ character, onError, onStatus, onChanged }: Props) {
  const [preview, setPreview] = useState<ArchivePreview>();
  const [busy, setBusy] = useState(false);
  const archiveButton = useRef<HTMLButtonElement>(null);
  const unarchiveButton = useRef<HTMLButtonElement>(null);
  const confirmButton = useRef<HTMLButtonElement>(null);
  // Set when the archive state changed here, so the control that replaced the pressed one takes focus.
  const moveFocus = useRef(false);
  const archived = Boolean(character.archivedAt);

  useEffect(() => {
    if (preview) confirmButton.current?.focus();
  }, [preview]);

  useEffect(() => {
    if (!moveFocus.current) return;
    moveFocus.current = false;
    (archived ? unarchiveButton : archiveButton).current?.focus();
  }, [archived]);

  async function run(action: () => Promise<unknown>, status: string) {
    if (busy) return;
    setBusy(true);
    try {
      await action();
      setPreview(undefined);
      moveFocus.current = true;
      onStatus(status);
      await onChanged();
    } catch (error) {
      moveFocus.current = false;
      onError(error);
    } finally {
      setBusy(false);
    }
  }

  function dismiss() {
    setPreview(undefined);
    archiveButton.current?.focus();
  }

  function onDialogKey(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      event.preventDefault();
      dismiss();
    }
  }

  if (archived) {
    return (
      <section aria-labelledby="archive-heading" className="play-panel">
        <h3 id="archive-heading">Archived</h3>
        <p>This character is archived: it is kept, with everything it uses, but listed apart.</p>
        <button
          type="button"
          ref={unarchiveButton}
          disabled={busy}
          onClick={() => run(() => client.unarchiveCharacter(character.id), `${character.name} is back in the character list.`)}
        >
          Unarchive
        </button>
      </section>
    );
  }

  return (
    <section aria-labelledby="archive-heading">
      <h3 id="archive-heading" className="visually-hidden">
        Archive
      </h3>
      <button
        type="button"
        ref={archiveButton}
        disabled={busy}
        aria-expanded={preview !== undefined}
        onClick={() => client.archivePreview(character.id).then(setPreview).catch(onError)}
      >
        Archive…
      </button>
      {preview && (
        <div role="alertdialog" aria-label={`Archive ${preview.name}?`} className="play-panel" onKeyDown={onDialogKey}>
          <p>
            {preview.name} moves to &quot;Archived&quot; in the character list. Nothing is deleted: its play state
            {preview.gapNotes > 0 ? `, its ${preview.gapNotes} gap note${preview.gapNotes === 1 ? '' : 's'}` : ''}
            {preview.campaign ? `, its place in the campaign ${preview.campaign}` : ''} and the content it uses all stay,
            and &quot;Back up everything&quot; still includes it. You can unarchive it at any time.
          </p>
          <div className="actions">
            <button
              type="button"
              ref={confirmButton}
              disabled={busy}
              onClick={() =>
                run(
                  () => client.archiveCharacter(character.id),
                  `Archived ${character.name}. It is under "Archived" in the character list, and nothing was deleted.`,
                )
              }
            >
              Archive
            </button>
            <button type="button" disabled={busy} onClick={dismiss}>
              Keep it
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
