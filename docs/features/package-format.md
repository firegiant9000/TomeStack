# Portable package format (v7; v1 to v6 still importable)

SPEC P-02 · status: implemented for characters (M0) and their campaigns (M2 item 7), for the whole library (M2.1, "Full library backup" below) and for homebrew sources (M6 slice 1, "Source packs" below; approved by the owner 2026-09-29; **the number v7 stays provisional until it merges**). Character packages never include PDFs (ADR-005, ADR-007). A full library backup includes managed PDF copies and is never for sharing.

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
| `format` / `formatVersion` | `tomestack.package` / `5` for character packages, `7` for full library backups and source packs (v7, M6 slice 1: `scope: "source"`, `attestations`, and library-backup sources that carry `importDerived`, `origin` and `shareConfirmedAt`; v6: `scope`, `revisionOrder`, `attachments/` and `files/` entries, library backups only; v5: `gaps/` entries, backups only; v4: `campaigns/` entries, and entries may be content schema v4 and character schema v4; v3: `purpose` and `omitted`, ADR-007; v2: content entries use content schema v2 with typed effects, ADR-003). v1 and v2 packages still import as backups, and v1 revisions are upcast. Newer versions are refused with a clear message. |
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
10. **Backup before replace (SPEC C-07, Q-01):** if the package replaces characters that already exist locally, apply first exports their current local copies to `<data dir>/backups/pre-import-<UTC timestamp>.tomestack.zip`. That file is an ordinary package, so you restore it by importing it. If the local copy cannot be exported (for example, a pinned revision is missing), the import is refused with `package.backup-failed`, and nothing changes. A source pack that adds or replaces anything first copies the whole database to `<data dir>/backups/pre-import-<UTC timestamp>.db` (an SQLite online backup, as for a library restore); to go back, close TomeStack and put that file in place of `tomestack.db`.
11. **Sources keep their provenance (M6 slice 1).** On every import (and, where noted, every restore):
    - the import-derived flag only goes up (`package.source-import-derived` warns when a package raises it), and in a restore a source that had a PDF in the backup is marked even if the PDF does not come back;
    - a source that is new here is recorded as `received`, and a received source can never be marked as the receiver's own work; a source already here keeps the origin this machine recorded (a source stored before database v8 keeps its unknown origin);
    - no package adds content to a source you made here (`package.own-source`); a source of unknown origin that gets content from a package loses its share confirmation (`package.source-unconfirmed`), so it must be marked as shareable again;
    - no package adds content under a bundled SRD source (`package.bundled-source-content`), and `content.saveDraft` refuses it too (`content.source-not-editable`), so nothing else travels with the SRD's CC-BY notice. A character made with a newer build whose SRD packs hold a revision this build lacks is refused the same way, and the message says to update;
    - no package but a full restore adds a revision to a content that belongs to another source here, or splits one content across two of its own sources (`pack.content-conflict`; a character package too since the M6 stack review, 2026-09-30). Otherwise the added revision became that content's newest, SRD content included, and the content sat in two sources for good. Only added revisions count: your own backup of a content an earlier build split, or its pre-import copy (rule 10), imports again on the machine that holds both revisions and writes nothing. `content.saveDraft` refuses a draft under a source other than the one its content already has (`content.source-mismatch`), so no new split is made here;
    - a character or source pack import never raises an existing source's `redistributable`;
    - a bundled SRD source record is never replaced (`package.bundled-source-kept`); a full restore still gives it the backup's PDF when it has none here;
    - `importDerived`, `origin` and `shareConfirmedAt` are never a `keepLocal` / `useImported` choice.

    **What this does not stop (review, 2026-09-29).** These rules guard against mistakes and against packages from other people, not against a user who sets out to get round them on their own machine:
    - a **full library restore** is the user's own file and is taken at its word: a hand-edited backup (its hashes are plain SHA-256) can bring a source back without the flag, with `origin: "local"`, or with `redistributable` raised;
    - that includes **someone else's full backup** restored here: a source new here comes back with the backup's `origin` and `shareConfirmedAt`, so their homebrew counts as made and confirmed here (and your own packages from your other machine are then refused for it with `package.own-source`). Found by the M6 stack review (2026-09-30); changing what a restore records is an owner decision, so the behaviour is kept until then;
    - a **v6 backup** of a source whose PDF was already removed, or that only had import jobs, restores unflagged (jobs are not in backups);
    - a source **stored before database v8** whose PDF was removed after pages were imported from it (a page import records no import job) is not marked by the v8 migration;
    - a source **stored before database v8** has an unknown origin, so a friend's source received back then can still be marked as your own work (you confirm it is yours);
    - text **copied by hand** from a PDF into a new source is not tracked: the flag is per source, not per text.
12. **Newer packages are refused as newer.** `format` and `formatVersion` are read from the raw manifest first, before any other entry's name is checked (M6 stack review, 2026-09-30), so a package from a later TomeStack, even with a scope or an entry folder this build does not know, is refused with `package.unsupported-format` ("update TomeStack"), not as a damaged file.

## Full library backup (M2.1)

A character backup protects characters and what they use. It does not protect homebrew no character uses yet, studio drafts, campaigns without characters, or PDFs (audit H1, 2026-09-28). The **Backups** screen adds a separate kind of file for that:

- **Back up everything** (`library.backupPreview`, `library.backupSaveAs`) writes one v7 package (v6 before M6 slice 1; both are restored) with `scope: "library"` and `purpose: "backup"` to a file you pick in the native Save dialog. It is written to `<file>.partial` first and then renamed.
  - It contains every source (keeping its `attachmentId`), and every revision, published, superseded or draft, in the order it was stored (`revisionOrder`, so the newest revision stays the newest after a restore). It also has every character, campaign and gap note, and every attachment record (`attachments/<attachmentId>.json`, [attachment.v1](../schemas/attachment.v1.schema.json)).
  - Each managed PDF copy is included once, as `files/<sha256>.pdf`.
  - It leaves out the bundled SRD revisions (every install seeds them), the files of linked PDFs (their records are kept), and the local-only extracted text, import jobs and candidates (ADR-009). Those can be read again from the PDF.
  - It also leaves out **character snapshots** (M5 slice 8; owner decision LIVING_SPECS D14), as every character package does. Snapshots stay on this machine ([snapshots.md](snapshots.md)), so the format is unchanged.
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
- The two kinds do not mix. `package.preview` refuses a library backup (`package.library-backup`), and a restore refuses a character package (`restore.not-a-library-backup`). Only a v6 or later library backup may contain `attachments/` or `files/` entries, and only a v7 or later source pack has `scope: "source"`.
- Tests: `tests/AppService.Tests/LibraryBackupTests.cs` covers the clean-folder restore compared as a whole, including a restart, plus the exclusions, the refusals, an altered PDF, the pre-restore copy and a damaged PDF copy. `LibraryBackupTests.The_commands_use_the_native_dialogs…` covers the commands. The desktop smoke (`scripts/smoke.ps1`) backs up its data folder, PDF included, and restores it into a second, clean folder with the shipped exe.
- **Not verified:** a restore on a second machine, and a backup of a real library with large PDFs. Both are owner checks.

## Source packs and "Mark as shareable" (M6 slice 1)

**Status: implemented and fixture-verified (2026-09-29) on the unmerged PR for M6 slice 1. The owner approved the new source fields, the `source` scope and database v8 on 2026-09-29 (LIVING_SPECS D14 item 6); merging still waits for the owner.** Format v7 is the number it would take if it merged next; the number is fixed only when it merges (ROADMAP "Package format numbers").

A **source pack** shares your own homebrew: one or more sources and their published content, and nothing else.

- **Contents:** `sources/<id>.json` and `content/<revisionId>.json` only. Every *published* revision of each source is included (superseded ones too, so characters that pin them keep working), in the order they were stored (`revisionOrder`, so the newest stays the newest). No characters, drafts, gap notes, campaigns, attachment records, PDFs or text read from PDFs. The source records carry no machine-local field (`pdfRef`, `attachmentId`, `importDerived`, `origin`, `shareConfirmedAt`, `sha256`).
- **Manifest:** `scope: "source"`, `purpose: "share"`, `formatVersion: 7`, and `attestations[]`: for each source, the statement its author confirmed and when (no user or machine name). Schema: [package-manifest.v7](../schemas/package-manifest.v7.schema.json).
- **Commands:** `package.sourcePackPreview { sourceIds }` (what it would hold, and warnings for content it refers to but does not carry), `package.sourcePackSaveAs { sourceIds }` (the native Save dialog) and `package.sourcePackExport { sourceIds }` (base64; browser development and tests). Importing uses `package.preview` and `package.apply`, as for character packages.

**The guard runs at every export** (D14 item 6), not only when a source is marked. A pack takes a source only if it is homebrew made on this machine, is not import-derived, and is marked as shareable (`pack.source-not-shareable`, `pack.source-import-derived`, `pack.source-received`, `pack.source-bundled`, `pack.source-missing`, `pack.source-empty`). A content whose revisions sit in two sources cannot leave with one of them (`pack.content-spans-sources`). The reader's limits are checked before anything is written (`pack.too-large`). At most 50 sources per pack.

**The import-derived flag.** A source is import-derived once material from outside its author entered it: a PDF attached (managed or linked), an import job started, a PDF candidate accepted, pages imported, and later an ADR-011 extension import. The flag is never cleared, not even by removing the PDF, and an import-derived source is never redistributable and never shared: not in a source pack, and not in a character share either (a share leaves it out and lists it in `omitted[]`, as for any non-redistributable source). The bundled SRD sources are exempt: you may attach an SRD PDF for its page links, and the SRD content stays shareable under CC-BY-4.0. The flag is stored twice (the source's JSON and an `import_derived` column that can only go up), so no later write can lower it. Database migration v8 marks every existing source with a PDF, a pre-v3 PDF reference (`legacy_pdf_ref`) or an import job.

**"Mark as shareable"** (`source.setShareable { sourceId, shareable, confirmOwnWork }`) sets `redistributable` and records `shareConfirmedAt`. Marking needs the author's confirmation that "the source is my own work, and it holds no text, tables or rules copied from a book, PDF or other material I did not write". It is refused for a bundled source (`source.bundled`), an import-derived one (`source.import-derived`) and one received from someone else (`source.received`). Stopping sharing is always allowed and does not recall files already sent. `source.createHomebrew`'s `redistributable: true` goes through the same guard: it needs `confirmOwnWork` (`source.confirm-own-work`).

**Importing a source pack**, beyond rules 1 to 12:

- The pack must be a v7 share with only `sources/` and `content/` entries (`package.entry-not-allowed`), a `revisionOrder` that lists every revision once, and an attestation for each source (`pack.attestation-missing`).
- Each source must be redistributable and not import-derived (`pack.source-not-shareable`), not a bundled SRD source (`pack.source-bundled`), and have content (`pack.source-empty`). Every revision must be published (`pack.draft-not-allowed`) and belong to a source in the pack (`pack.revision-source`).
- A pack may not add content to a source you made on this machine (`package.own-source`, rule 11), or a revision to a content that belongs to another source here or to two of its own sources (`pack.content-conflict`, checked for every package but a full restore, rule 11). Your own pack imported back where it was made adds nothing and is allowed.
- When the pack's newest revision of a content is already here and it adds older ones, one of those becomes the newest here (newest is last stored); the preview warns (`pack.newest-changes`).
- The imported sources are recorded as `received`: they can travel on in character shares under the sender's `redistributable`, but never in your source packs, and you cannot mark them as your own.
- The preview says the attestation is the sender's claim, which TomeStack cannot verify. Apply copies the database first (rule 10).

**Where each shape is read (compatibility matrix).** "Refused" means refused with a clear message, never misread.

| File | Written by | Format | Content schema inside | Read by 0.3.x (format ≤ 5) | Read by M2.1 to M5 builds (format ≤ 6) | Read by this build (format ≤ 7) |
| --- | --- | --- | --- | --- | --- | --- |
| Character package (backup or share) | all builds | 5 | lowest version each revision needs (3 to 9) | yes, if no entry is newer than it supports | yes, same condition | yes |
| Full library backup | M2.1 to M5 builds | 6 | 3 to 9 | refused (v6) | yes | yes; every source that comes back with a PDF is marked import-derived |
| Full library backup | this build | 7 | 3 to 9 | refused | refused (v7) | yes, with the flag, origin and share confirmation |
| Source pack | this build | 7 | 3 to 9 | refused | refused (v7) | yes |
| A later format (8 and on) | later builds | 8+ | any | refused | refused | refused (`package.unsupported-format`) |

The database is versioned the same way: this build migrates a v7 database to v8 (forward-only; one copy first, at the version the database is opened with: `tomestack.db.v7.bak`, or `tomestack.db.v6.bak` from 0.3.x, which runs v7 and v8 in turn), and an older build refuses a v8 data folder (`NewerDatabaseException`), so no older build can rewrite a source without its flag.

**Tests:** `tests/AppService.Tests/SourcePackTests.cs`: the round trip into a clean data folder (stored order, equal revisions, received origin, the pre-import copy); every export refusal; "Mark as shareable" and its refusals; the flag on attach, detach, page import and candidate accept, and a character share leaving it out; the flag, origin and confirmation through a v7 library backup, and a v6 backup restored with a PDF; imports that cannot lower the flag, raise `redistributable` or replace a bundled source; ten hostile packs; a pack adding to your own source or to another source's content; a character package adding to your own source or under an SRD source, and a draft saved under one; a pre-v8 source keeping its unknown origin through your own backup, and losing its share confirmation when a package adds to it; the newest-changes warning; a full restore giving an SRD source its PDF back; a newer format with an unknown scope; the v8 migration backfill; the dispatcher commands. The source-pack manifest is validated against package-manifest v7 and its sources against source v1, and library-backup sources against source v2 (`Library_backup_sources_match_the_source_v2_schema`).

**Dual review (2026-09-29, both reviewers and the cross-check; none refuted).** Fixed: a pre-v8 source was relabelled "received" by any import; additions to your own source were refused only in source packs; content could be stored under an SRD source id (package or draft); a restore dropped an SRD source's PDF; an older revision could silently become the newest; a pack could split one content across two of its sources; a backup source with a PDF that did not come back was not marked; tests and UI wording. The limits listed under rule 11 are documented, not fixed. The e2e flow "marks a source as shareable after confirming it is your own work, saves a source pack and imports it".

**Not verified:** a pack moved to a second machine and imported there (the clean-folder test stands in), and screen-reader use of the confirmation (owner checks).

## Decided

D03 (owner, 2026-09-26): separate `backup` and `share` exports; a share leaves out non-redistributable sources ([ADR-007](../decisions/ADR-007-export-package-and-license-policy.md)). Tests: `tests/AppService.Tests/ExportPurposeTests.cs`.
