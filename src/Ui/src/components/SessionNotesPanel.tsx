import { useState, type SubmitEvent } from 'react';
import { client } from '../api/client';
import type { CharacterView } from '../api/types';

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

/** The local calendar date (a UTC date would be tomorrow's, or yesterday's, around midnight). */
const today = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

/**
 * Character schema v8 (D22): the player's dated session notes, saved with the character. A journal, not rules: never
 * calculated, never in a share package (like gap notes), printed only when ticked. Newest session first.
 */
export function SessionNotesPanel({ view, onChanged, onError, onStatus }: Props) {
  const { character } = view;
  const notes = [...(character.notes ?? [])].sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : a.createdAt < b.createdAt ? 1 : -1));
  const [date, setDate] = useState(today());
  const [text, setText] = useState('');
  const [deleting, setDeleting] = useState<string>();
  const [saving, setSaving] = useState(false);

  async function saveNotes(next: typeof notes, status: string): Promise<boolean> {
    try {
      onChanged(await client.saveCharacter({ ...character, notes: next }));
      onStatus(status);
      return true;
    } catch (error) {
      onError(error);
      return false;
    }
  }

  async function add(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!text.trim() || !date || saving) return;
    setSaving(true);
    const ok = await saveNotes([...notes, { id: crypto.randomUUID(), date, text: text.trim(), createdAt: new Date().toISOString() }], 'Session note saved.');
    setSaving(false);
    if (ok) setText(''); // a failed save keeps what was typed
  }

  return (
    <section aria-labelledby="session-notes-heading" className="play-panel">
      <h3 id="session-notes-heading">Session notes</h3>
      <p className="hint">Your own notes per session. They stay on this computer and in your personal backups; a share package never carries them.</p>
      <form onSubmit={add} aria-label="New session note" className="inline-form">
        <label className="field">
          Session date
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
        </label>
        <label className="field">
          Note
          <textarea value={text} onChange={(e) => setText(e.target.value)} maxLength={4000} rows={3} required />
        </label>
        <button type="submit" disabled={!text.trim() || !date || saving}>
          Save session note
        </button>
      </form>
      {notes.length === 0 && <p className="hint">No session notes yet.</p>}
      {notes.length > 0 && (
        <ul className="resources" aria-label="Session notes">
          {notes.map((note) => (
            <li key={note.id} className="resource">
              <strong>{note.date}</strong>
              <p>{note.text}</p>
              {deleting === note.id ? (
                <div className="actions">
                  <button type="button" onClick={() => void saveNotes(notes.filter((n) => n.id !== note.id), 'Session note deleted.')}>
                    Confirm delete
                  </button>
                  <button type="button" onClick={() => setDeleting(undefined)}>
                    Keep it
                  </button>
                </div>
              ) : (
                <button type="button" onClick={() => setDeleting(note.id)}>
                  Delete session note from {note.date}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
