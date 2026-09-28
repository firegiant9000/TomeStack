import { useEffect, useState } from 'react';
import { client } from '../api/client';
import type { GapNoteListing } from '../api/types';

interface Props {
  onError: (error: unknown) => void;
  onOpenCharacter: (characterId: string) => void;
}

/**
 * M3 C5: every character's gap notes in one list, open first, to work through after a session. Notes stay on this
 * computer; this screen only reads them and changes a note's status. Deleting stays on the character's sheet.
 */
export function AllGapNotesPanel({ onError, onOpenCharacter }: Props) {
  const [listed, setListed] = useState<GapNoteListing[]>();
  const [showResolved, setShowResolved] = useState(false);

  useEffect(() => {
    let current = true;
    client
      .listAllGapNotes()
      .then((all) => {
        if (current) setListed(all);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [onError]);

  async function toggle(entry: GapNoteListing) {
    try {
      await client.setGapNoteStatus(entry.note.id, entry.note.status === 'open' ? 'resolved' : 'open');
      setListed(await client.listAllGapNotes());
    } catch (error) {
      onError(error);
    }
  }

  const open = (listed ?? []).filter((l) => l.note.status === 'open').length;
  const shown = (listed ?? []).filter((l) => showResolved || l.note.status === 'open');

  return (
    <section aria-labelledby="all-gaps-heading" className="panel">
      <h2 id="all-gaps-heading">Gap notes of all characters{listed ? `: ${open} open` : ''}</h2>
      <p className="hint">Where TomeStack fell short at the table, for every character. Notes stay on this computer.</p>
      <label>
        <input type="checkbox" checked={showResolved} onChange={(e) => setShowResolved(e.target.checked)} /> Show resolved notes
      </label>
      {listed && shown.length === 0 && <p className="hint">{listed.length === 0 ? 'No gap notes yet.' : 'No open gap notes.'}</p>}
      {shown.length > 0 && (
        <ul className="resources" aria-label="Gap notes of all characters">
          {shown.map((entry) => (
            <li key={entry.note.id} className="resource">
              <strong>{entry.characterName}</strong>: {entry.note.target.label}{' '}
              <span className="tag">{entry.note.status === 'open' ? 'Open' : 'Resolved'}</span>
              <p>{entry.note.text}</p>
              <button type="button" onClick={() => toggle(entry)}>
                {entry.note.status === 'open' ? `Mark resolved: ${entry.note.target.label}` : `Reopen: ${entry.note.target.label}`}
              </button>
              <button type="button" onClick={() => onOpenCharacter(entry.note.characterId)}>
                Open {entry.characterName}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
