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
- Session notes (character schema v8, D22) follow the same rule: in a personal backup, never in a share.
- **No error message or log quotes a note's text.** Errors name the note's id and a count. The error log records only unexpected exceptions, and the note commands raise none that carry the text.

## A note

`{ id, schemaVersion, characterId, target, text, status, createdAt, updatedAt }` ([v1 schema](../schemas/gap-note.v1.schema.json), [v2 schema](../schemas/gap-note.v2.schema.json)):

- **`target`:**
  - `{ kind: "feature", contentId, effectId? }` or `{ kind: "field", fieldId }`.
  - It must be on the character's sheet when the note is written (`gap.target-not-found`).
  - `label` is the feature name (with `: <effect label>` for an effect) or the field label. It is taken from the sheet, so a note stays readable after the feature is removed or updated.
  - **`{ kind: "import", label }`** (schema v2, D16b): an item of a D&D Beyond sheet import that TomeStack could not match or place, named by its label alone (1 to 200 characters, cut to 200). Only `ddb.apply` writes these, in its own transaction, with a fixed text; `gap.add` refuses them (`gap.target-invalid`). They list, resolve, delete, print and travel in a personal backup like the others ([ddb-pdf-import.md](ddb-pdf-import.md)).
- **`schemaVersion`:** the lowest version that holds the note: 1 for a feature or field note, 2 for an import note. So a backup without an import note is still read by builds that know only v1; one with an import note is refused by them (they report `package.invalid-json`, "Entry … is not valid", since they read the entry before its version and fail on the unknown kind `import`). The data folder itself moves to database 10 with no table change, so those builds refuse it outright (`NewerDatabaseException`) instead of opening it and failing on every read of an import note, full backups included.
- **`text`:** 1 to 2,000 characters, trimmed (`gap.text-required`).
- **`status`:** `open` or `resolved`.
- **Limit:** at most 500 notes per character (`gap.too-many`).

A note never changes the character, and has no effect on the sheet.

## Commands

| Command | Payload | Does |
| --- | --- | --- |
| `gap.list` | `{ characterId }` | The notes, open first, then newest first. Writes nothing |
| `gap.listAll` (M3 C5) | none | Every character's notes as `{ note, characterName }`, open first, then newest first. Writes nothing |
| `gap.add` | `{ characterId, target, text }` | Stores a note. Sent only from "Save note" |
| `gap.setStatus` | `{ id, status }` | Marks a note resolved (fixed or accepted), or open again |
| `gap.delete` | `{ id, confirm }` | Refused without `confirm: true` (`gap.confirmation-required`). The UI asks first: "Delete this note? It cannot be undone." |

`package.exportPreview` returns `gapNotes`: the number of notes a backup would include, and always 0 for a share.

## UI

"Gap notes: N open" on the sheet has:

- An "About" picker: every feature, then every field.
- "What was missing or wrong?" and "Save note".
- The notes, each with "Mark resolved" / "Reopen" and "Delete…", which asks for confirmation.

**"Report a gap: <name>" (M3 C5)** is a button on each feature (in "Features") and in each field's details. It picks that feature or field in "About" and moves focus to "What was missing or wrong?", so a note takes one click and typing. Since ADR-014 the form is on the sheet's Notes tab, which "Report a gap" opens before focusing the text. It saves nothing by itself: "Save note" still does.

**"Gap notes" in the sidebar (M3 C5)** lists every character's notes (`gap.listAll`), with the character's name, open first. "Show resolved notes" adds the resolved ones. Each note has "Mark resolved" / "Reopen" and "Open <character>", which opens the sheet on its Notes tab (ADR-014). Deleting stays on the character's sheet, with its confirmation.

Acceptance for C5: `GapNoteTests.Notes_of_every_character_list_together_with_their_names_open_first_and_the_list_writes_nothing` and the "Report a gap" and "Gap notes of all characters" steps of the e2e test "records a gap note on a field and a feature…".

## Not yet

- Exporting the notes as text. The print view can include them (`printable-backup.md`).
