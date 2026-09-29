# Portable package format (v6; v1 to v5 still importable)

SPEC P-02 · status: implemented for characters (M0) and their campaigns (M2 item 7), and for the whole library (M2.1, "Full library backup" below). Character packages never include PDFs (ADR-005, ADR-007). A full library backup includes managed PDF copies and is never for sharing.

A package is a ZIP file (`*.tomestack.zip`) with this fixed layout:

```text
manifest.json
sources/<sourceId>.json        SourceRecord, including license and redistribution flag
content/<revisionId>.json      ContentRevision (immutable, pinned by characters)
characters/<characterId>.json  Character choices, pins, overrides and play state (no derived values)
campaigns/<campaignId>.json    Campaign profile of an exported character (v4; SPEC P-01)
gaps/<noteId>.json             Session gap note of an exported character (v5; backups only, gap-notes.md)
```

`manifest.json`:

| Field | Meaning |
| --- | --- |
| `format` / `formatVersion` | `tomestack.package` / `5` for character packages, `6` for full library backups (v6: `scope`, `revisionOrder`, `attachments/` and `files/` entries, library backups only; v5: `gaps/` entries, backups only; v4: `campaigns/` entries, and entries may be content schema v4 and character schema v4; v3: `purpose` and `omitted`, ADR-007; v2: content entries use content schema v2 with typed effects, ADR-003). v1 and v2 packages still import as backups, and v1 revisions are upcast. Newer versions are refused with a clear message. |
| `createdAt`, `appVersion` | Provenance of the export. |
| `purpose` | `backup` (everything; not for sharing) or `share` (non-redistributable sources left out). |
| `characters` | Character IDs included. |
| `entries[]` | `path`, `kind`, `sha256`, `size` for every non-manifest entry. |
| `notices[]` | Title, publisher, license, `redistributable` and attribution for every included source. |
| `omitted[]` | Share only: each left-out source (title, publisher, license) with its revisions and the characters that pin them. |
| `attachmentPolicy` | States that PDFs are never included. |

JSON is indented UTF-8 with camelCase names and string enums, and entries are sorted. The same data at the same time produces byte-identical packages, so diffs are readable.

## Export

Every export has a `purpose` (ADR-007, D03), and the UI asks for it:

- **`backup`** (default): characters, every pinned revision and every source, including `redistributable: false` ones, and the characters' gap notes ([gap-notes.md](gap-notes.md)). File name `<name>-personal-backup.tomestack.zip`.
- **`share`**: revisions from non-redistributable sources, and those sources, are left out and listed in `omitted[]`. Characters keep their pins. Gap notes are never included, and an import refuses a share package that has them (`package.gap-notes-not-allowed`). File name `<name>.tomestack.zip`.

`package.exportPreview { characterIds, purpose }` returns `{ purpose, fileName, characters, included[], omitted[], gapNotes }` without writing anything. The UI shows the omitted list before a share export. `package.export` returns the package as base64 (used by browser development). In the desktop app, `package.saveAs` writes it where the user chooses in a native Save dialog, and returns only `{ saved, fileName }`. The UI waits for it without a timeout, because the response only comes once the user closes the dialog. Both take `purpose` (default `backup`).

Every limit and check below has its own test in `tests/AppService.Tests/PackageLimitTests.cs`.

## Import rules (untrusted input, SPEC Q-02)

1. Packages over 50 MB, with more than 2,000 entries, with an entry over 5 MB, or whose entries together unpack to more than 64 MB (`package.content-too-large`, against archives that expand far beyond their size) are refused. Sizes are checked while reading, not only from the header.
2. Only the paths above are allowed; anything else, including `..` or absolute paths, is refused before any entry is unpacked. There is no extraction to disk.
3. Every entry must be listed in the manifest and match its SHA-256. Unlisted, missing or duplicate entries are refused.
4. Every entry must be a valid document of its kind (`package.invalid-json`); a malformed *effect* inside a valid revision is kept as reference-only instead (ADR-003). File names must match the IDs inside them. Entries with a `schemaVersion` newer than this build supports are refused (`package.schema-unsupported`). JSON Schemas for every entry kind and for the manifest are in [docs/schemas](../schemas/README.md). Empty (null) list entries are refused (`character.empty-entry`, `validate.empty-entry`). A new *published* revision is also content-validated like `content.publish`: errors refuse a content schema v3 revision and are warnings for older ones ([validation-and-restrictions.md](validation-and-restrictions.md#validation-on-import)).
5. Every character pin and every revision's source must resolve within the package or the local store. The one exception is a pin that a v3 `share` manifest lists in `omitted[]`: it is imported with a `package.content-omitted` warning naming the source and publisher, and the sheet shows it as `content.missing`. A manifest whose `entries`, `notices` or `omitted` contain nulls is refused (`package.invalid-json`).
6. A revision with the same ID but different content is a blocking conflict. Published revisions are immutable.
7. **Preview first:** the user sees what will be added, left unchanged or replaced, plus warnings and license notices. **Apply** re-validates from the bytes and commits in one SQLite transaction.
8. Draft revisions stay drafts and remain inactive after import.

**Sharing with an older build (M2.2 decision, 2026-09-28).** A package is readable by an older build when every entry's `schemaVersion` is one it supports. Content revisions are published in the lowest content schema that holds them (never below v3; [schemas/README.md](../schemas/README.md#versioning-rules)), so homebrew that uses no content v8 field (attack count, critical range, armor training, armor Strength or Stealth, `whileArmored`, roll bonus) imports into 0.3.x. A revision that uses one is v8, and an older build refuses the package with `package.schema-unsupported` instead of misreading it. Characters are always written in the current character schema, so a package with characters still needs a build that reads it. Revisions published earlier keep the version they were written in (the then-current one, v7 in 0.3.x), because published revisions are immutable.
9. **Source metadata is never overwritten silently.** If a package's source differs from the local record with the same ID, the preview lists each differing field (local vs. imported). Apply then needs an explicit `sourceChoices[sourceId]` of `keepLocal` or `useImported`, and refuses with `package.source-choice-required` otherwise. `pdfRef` and `attachmentId` (ADR-005) are machine-local. They are never exported, and an import never changes them; nor does it bring a PDF.
10. **Backup before replace (SPEC C-07, Q-01):** if the package replaces characters that already exist locally, apply first exports their current local copies to `<data dir>/backups/pre-import-<UTC timestamp>.tomestack.zip`. That file is an ordinary package, so you restore it by importing it. If the local copy cannot be exported (for example, a pinned revision is missing), the import is refused with `package.backup-failed`, and nothing changes.

## Full library backup (M2.1)

A character backup protects characters and what they use. It does not protect homebrew no character uses yet, studio drafts, campaigns without characters, or PDFs (audit H1, 2026-09-28). The **Backups** screen adds a separate kind of file for that:

- **Back up everything** (`library.backupPreview`, `library.backupSaveAs`) writes one v6 package with `scope: "library"` and `purpose: "backup"` to a file you pick in the native Save dialog. It is written to `<file>.partial` first and then renamed.
  - It contains every source (keeping its `attachmentId`), and every revision, published, superseded or draft, in the order it was stored (`revisionOrder`, so the newest revision stays the newest after a restore). It also has every character, campaign and gap note, and every attachment record (`attachments/<attachmentId>.json`, [attachment.v1](../schemas/attachment.v1.schema.json)).
  - Each managed PDF copy is included once, as `files/<sha256>.pdf`.
  - It leaves out the bundled SRD revisions (every install seeds them), the files of linked PDFs (their records are kept), and the local-only extracted text, import jobs and candidates (ADR-009). Those can be read again from the PDF.
  - A managed copy that is missing, or no longer matches its hash, is left out with `backup.pdf-unreadable`, so one damaged file never blocks the backup. Its source then has no PDF after a restore.
- **Restore full backup** (`library.restoreChoose`, then `library.restoreApply { token, sourceChoices, confirm: true }`) reads a file picked in the native Open dialog. The path stays in the service; the page gets a one-use token and the file name.
  - The preview checks the file completely before anything is written. That includes rules 2 to 9 above, the attachment records, and every PDF's size, signature and SHA-256, streamed and never held in memory.
  - Limits: 200,000 entries, 256 MB of unpacked JSON, 10,000 PDFs of at most 1 GB each.
  - Apply checks the file again and refuses with `restore.disk-full` if the PDFs would not fit. It copies the missing PDFs into `attachments/`, verifying each hash, then writes everything else in one transaction. A copy whose transaction fails has no record, and the next start removes it.
  - A restore **deletes nothing**: data that is not in the backup stays. A source that differs needs `keepLocal` or `useImported`, as in rule 9. A source gets the backup's PDF only if it has none here.
  - Before anything is replaced (a character, campaign, gap note, or a source you take from the backup), the whole database is copied to `<data dir>/backups/pre-restore-<UTC timestamp>.db`, an SQLite online backup. To go back, close TomeStack and put that file in place of `tomestack.db`.
- **Hardening (review, 2026-09-28):**
  - A restore adds an attachment record, and copies its PDF, only when a restored source will point to it. Nothing is added for a source you keep local, or one that already has its own PDF here.
  - A linked PDF is restored only if its path is a full path to a `.pdf` on a local drive (`restore.linked-pdf-skipped` otherwise). A network path would make Windows connect to another machine, and send your credentials, just by listing sources.
  - A managed record's size must equal its PDF's, and the writer records the size on disk.
  - PDF entries are checked for size before any is read: at most 64 GB in total, not more than the file itself, and not much more than they occupy (TomeStack stores PDFs uncompressed). The check stops at the first bad PDF.
  - The writer refuses a library over the reader's limits (`backup.too-large`) before writing, so every saved backup can be restored.
  - A backup file that changes between "Choose" and "Restore" is refused (`restore.file-changed`).
  - The preview says a replaced character is kept in the `pre-restore-*.db` copy (`restore.character-replace`). It also warns when a revision from the backup would become the newest over a newer one that only this library has (`restore.newest-changes`).
- The two kinds do not mix. `package.preview` refuses a library backup (`package.library-backup`), and a restore refuses a character package (`restore.not-a-library-backup`). Only a v6 library backup may contain `attachments/` or `files/` entries.
- Tests: `tests/AppService.Tests/LibraryBackupTests.cs` covers the clean-folder restore compared as a whole, including a restart, plus the exclusions, the refusals, an altered PDF, the pre-restore copy and a damaged PDF copy. `LibraryBackupTests.The_commands_use_the_native_dialogs…` covers the commands. The desktop smoke (`scripts/smoke.ps1`) backs up its data folder, PDF included, and restores it into a second, clean folder with the shipped exe.
- **Not verified:** a restore on a second machine, and a backup of a real library with large PDFs. Both are owner checks.

## Decided

D03 (owner, 2026-09-26): separate `backup` and `share` exports; a share leaves out non-redistributable sources ([ADR-007](../decisions/ADR-007-export-package-and-license-policy.md)). Tests: `tests/AppService.Tests/ExportPurposeTests.cs`.
