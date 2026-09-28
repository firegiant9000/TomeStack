import { useRef, useState } from 'react';
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
 */
export function ArchivePanel({ character, onError, onStatus, onChanged }: Props) {
  const [preview, setPreview] = useState<ArchivePreview>();
  const archiveButton = useRef<HTMLButtonElement>(null);

  async function confirm() {
    try {
      await client.archiveCharacter(character.id);
      setPreview(undefined);
      onStatus(`Archived ${character.name}. It is under "Archived" in the character list, and nothing was deleted.`);
      await onChanged();
    } catch (error) {
      onError(error);
    }
  }

  async function unarchive() {
    try {
      await client.unarchiveCharacter(character.id);
      onStatus(`${character.name} is back in the character list.`);
      await onChanged();
    } catch (error) {
      onError(error);
    }
  }

  if (character.archivedAt) {
    return (
      <section aria-labelledby="archive-heading" className="play-panel">
        <h3 id="archive-heading">Archived</h3>
        <p>This character is archived: it is kept, with everything it uses, but listed apart.</p>
        <button type="button" onClick={unarchive}>
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
        aria-expanded={preview !== undefined}
        onClick={() => client.archivePreview(character.id).then(setPreview).catch(onError)}
      >
        Archive…
      </button>
      {preview && (
        <div role="alertdialog" aria-label={`Archive ${preview.name}?`} className="play-panel">
          <p>
            {preview.name} moves to &quot;Archived&quot; in the character list. Nothing is deleted: its play state
            {preview.gapNotes > 0 ? `, its ${preview.gapNotes} gap note${preview.gapNotes === 1 ? '' : 's'}` : ''}
            {preview.campaign ? `, its place in the campaign ${preview.campaign}` : ''} and the content it uses all stay,
            and &quot;Back up everything&quot; still includes it. You can unarchive it at any time.
          </p>
          <div className="actions">
            <button type="button" onClick={confirm}>
              Archive
            </button>
            <button
              type="button"
              onClick={() => {
                setPreview(undefined);
                archiveButton.current?.focus();
              }}
            >
              Keep it
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
