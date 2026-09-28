# M3 B3: session gap notes

ROADMAP M3 ("session feedback") · status: **implemented**.

- Service: `src/AppService/GapNotes.cs`, with database migration v5 (the `gap_notes` table) and `gaps/` package entries.
- UI: the "Gap notes" panel on the sheet (`src/Ui/src/components/GapNotesPanel.tsx`).
- Acceptance: `tests/AppService.Tests/GapNoteTests.cs`, the gap entries in `SchemaTests.Every_entry_and_the_manifest_of_an_exported_package_match_their_schemas`, and the e2e test "records a gap note on a field and a feature…".

While playing, the player writes down where TomeStack fell short: a mechanic it does not handle, a number that is wrong at their table, or a step they had to do by hand. Each note is fixed or accepted later. The notes are how the M3 exit gate's play session records its gaps ([m3-stardust-guardian.md](m3-stardust-guardian.md)).

## Privacy

Notes can describe private homebrew, so:

- They are stored **only in the local database**: the `gap_notes` table, keyed by character. The shipped app opens no socket (ADR-006), and no command sends a note anywhere.
- They leave the machine **only in a personal backup** (`gaps/<id>.json`, package format v5):
  - A share export never includes them (ADR-007).
  - An import refuses a share package, or a pre-v5 package, that carries them (`package.gap-notes-not-allowed`).
  - An import refuses a note whose character is not in the package (`package.gap-note-orphan`).
  - The export radios say so.
- **No error message or log quotes a note's text.** Errors name the note's id and a count. The error log records only unexpected exceptions, and the note commands raise none that carry the text.

## A note

`{ id, schemaVersion: 1, characterId, target, text, status, createdAt, updatedAt }` ([schema](../schemas/gap-note.v1.schema.json)):

- **`target`:**
  - `{ kind: "feature", contentId, effectId? }` or `{ kind: "field", fieldId }`.
  - It must be on the character's sheet when the note is written (`gap.target-not-found`).
  - `label` is the feature name (with `: <effect label>` for an effect) or the field label. It is taken from the sheet, so a note stays readable after the feature is removed or updated.
- **`text`:** 1 to 2,000 characters, trimmed (`gap.text-required`).
- **`status`:** `open` or `resolved`.
- **Limit:** at most 500 notes per character (`gap.too-many`).

A note never changes the character, and has no effect on the sheet.

## Commands

| Command | Payload | Does |
| --- | --- | --- |
| `gap.list` | `{ characterId }` | The notes, open first, then newest first. Writes nothing |
| `gap.add` | `{ characterId, target, text }` | Stores a note. Sent only from "Save note" |
| `gap.setStatus` | `{ id, status }` | Marks a note resolved (fixed or accepted), or open again |
| `gap.delete` | `{ id, confirm }` | Refused without `confirm: true` (`gap.confirmation-required`). The UI asks first: "Delete this note? It cannot be undone." |

`package.exportPreview` returns `gapNotes`: the number of notes a backup would include, and always 0 for a share.

## UI

"Gap notes: N open" on the sheet has:

- An "About" picker: every feature, then every field.
- "What was missing or wrong?" and "Save note".
- The notes, each with "Mark resolved" / "Reopen" and "Delete…", which asks for confirmation.

## Not yet

- A "Report a gap" button on each feature and field. The picker covers both for now.
- Listing notes across all characters.
- Exporting the notes as text.
