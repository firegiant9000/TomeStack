import { useEffect, useState, type RefObject, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { CharacterView, GapNote, GapTarget } from '../api/types';

interface Props {
  view: CharacterView;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
  /** The "About" value (`feature:<id>` or `field:<id>`); the sheet owns it so "Report a gap" buttons can pre-fill it (M3 C5). */
  about: string;
  onAboutChange: (value: string) => void;
  /** The note text box, focused by "Report a gap". */
  textRef?: RefObject<HTMLTextAreaElement | null>;
}

/** The "About" value of a feature or a field, for "Report a gap". */
export const gapAboutFeature = (contentId: string) => `feature:${contentId}`;
export const gapAboutField = (fieldId: string) => `field:${fieldId}`;

/** `feature:<content id>` or `field:<field id>`: the value of one "About" option. */
function targetOf(value: string): GapTarget | undefined {
  const [kind, id] = [value.slice(0, value.indexOf(':')), value.slice(value.indexOf(':') + 1)];
  if (kind === 'feature' && id) return { kind: 'feature', contentId: id };
  if (kind === 'field' && id) return { kind: 'field', fieldId: id };
  return undefined;
}

/**
 * M3 B3: session feedback. At the table the player notes where TomeStack fell short on a feature or field, to fix or
 * accept later. Notes stay on this computer: nothing is sent anywhere, and only a personal backup includes them.
 */
export function GapNotesPanel({ view, onError, onStatus, about, onAboutChange, textRef }: Props) {
  const { character, sheet } = view;
  const [notes, setNotes] = useState<GapNote[]>();
  const [text, setText] = useState('');
  const [deleting, setDeleting] = useState<string>();

  useEffect(() => {
    let current = true;
    client
      .listGapNotes(character.id)
      .then((listed) => {
        if (current) setNotes(listed);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [character.id, onError]);

  const refresh = async () => setNotes(await client.listGapNotes(character.id));

  async function save(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const target = targetOf(about);
    if (!target || !text.trim()) return;
    try {
      const note = await client.addGapNote(character.id, target, text);
      setText('');
      await refresh();
      onStatus(`Gap note saved for ${note.target.label}.`);
    } catch (error) {
      onError(error);
    }
  }

  async function setStatus(note: GapNote) {
    try {
      await client.setGapNoteStatus(note.id, note.status === 'open' ? 'resolved' : 'open');
      await refresh();
    } catch (error) {
      onError(error);
    }
  }

  async function remove(id: string) {
    try {
      await client.deleteGapNote(id);
      setDeleting(undefined);
      await refresh();
      onStatus('Gap note deleted.');
    } catch (error) {
      onError(error);
    }
  }

  const open = (notes ?? []).filter((n) => n.status === 'open').length;

  return (
    <section aria-labelledby="gaps-heading" className="play-panel">
      <h3 id="gaps-heading">Gap notes{notes ? `: ${open} open` : ''}</h3>
      <p className="hint">
        Write down where TomeStack fell short at the table, to fix or accept later. Notes stay on this computer; only a personal backup includes them.
      </p>
      <form onSubmit={save} aria-label="New gap note">
        <label>
          About{' '}
          <select value={about} onChange={(e) => onAboutChange(e.target.value)} required>
            <option value="">Choose a feature or field</option>
            <optgroup label="Features">
              {(sheet.features ?? []).map((f) => (
                <option key={f.content.revisionId} value={gapAboutFeature(f.content.contentId)}>
                  {f.name}
                </option>
              ))}
            </optgroup>
            <optgroup label="Fields">
              {sheet.fields.map((f) => (
                <option key={f.field} value={gapAboutField(f.field)}>
                  {f.label}
                </option>
              ))}
            </optgroup>
          </select>
        </label>
        <label>
          What was missing or wrong?{' '}
          <textarea ref={textRef} value={text} onChange={(e) => setText(e.target.value)} maxLength={2000} rows={3} required />
        </label>
        <button type="submit" disabled={!about || !text.trim()}>
          Save note
        </button>
      </form>
      {notes && notes.length === 0 && <p className="hint">No gap notes yet.</p>}
      {notes && notes.length > 0 && (
        <ul className="resources" aria-label="Gap notes">
          {notes.map((note) => (
            <li key={note.id} className="resource">
              <strong>{note.target.label}</strong> <span className="tag">{note.status === 'open' ? 'Open' : 'Resolved'}</span>
              <p>{note.text}</p>
              <button type="button" onClick={() => setStatus(note)}>
                {note.status === 'open' ? `Mark resolved: ${note.target.label}` : `Reopen: ${note.target.label}`}
              </button>
              {deleting === note.id ? (
                <span role="group" aria-label="Confirm deleting the note">
                  Delete this note? It cannot be undone.{' '}
                  <button type="button" onClick={() => remove(note.id)}>
                    Delete note
                  </button>
                  <button type="button" onClick={() => setDeleting(undefined)}>
                    Keep note
                  </button>
                </span>
              ) : (
                <button type="button" onClick={() => setDeleting(note.id)}>
                  Delete…
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
