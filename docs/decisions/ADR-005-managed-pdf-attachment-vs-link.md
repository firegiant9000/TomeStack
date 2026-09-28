# ADR-005: Managed PDF attachment versus external link

Status: **accepted** (owner decision D02, 2026-09-26). The data-folder warning is implemented. PDF attachment, page navigation and the `pdfRef` migration are implemented (M2 item 6, database schema 3; `features/pdf-attachments.md`). One deviation from the plan, recorded under "Implementation (M2 item 6)".
Date: 2026-09-25 (proposed), 2026-09-26 (accepted)

## Context

SPEC S-04: linked PDFs are stored in an application-managed library by default, with a checksum, the original file name and source-page navigation. The user can remove an attachment without deleting accepted content. ARCHITECTURE keeps files outside the database, referenced by managed IDs and content hashes. LIVING_SPECS D02 asks whether to copy or externally link by default, with "managed copy" as the working default. MVP asks for "Open the cited page from a feature offline".

## Options

| | A. Managed copy | B. External link |
| --- | --- | --- |
| Page links survive the user moving or renaming the file | Yes | No; needs a relink flow |
| Disk use | Duplicates each PDF | None |
| Integrity check (SHA-256) | At attach time; stable afterwards | Must re-hash on every open |
| Backup completeness | A data-directory backup includes the PDFs | Backup misses the PDFs |
| Privacy of stored paths | Only managed IDs are stored | Stores a user path (may name the user) |

## Decision

1. **Data folder:** `%LOCALAPPDATA%\TomeStack` by default, overridable with `--data-dir` or `TOMESTACK_DATA_DIR`. It is outside `Documents`, so it is not OneDrive-synced by default. It is also not the installer's folder (ADR-008: pack id `TomeStack.App`).
2. **Sync-root warning (implemented):** if the data folder is inside a cloud sync root, `app.info` returns a `data-dir.sync-root` warning, and the UI shows it at the top of the window. The folder is still used, because the user chose it. Sync roots come from the OneDrive environment variables, OneDrive account folders, and every root registered with the Windows cloud files API (`SyncRootManager`, which also covers Dropbox and Google Drive) (`AppService/DataFolder.cs`, `DataFolderTests`). The warning names the sync folder but not the full path. Reason: SQLite in WAL mode plus a sync client can corrupt the database or create conflicting copies.
3. **PDFs: option A, managed copy, by default.** Linking (option B) stays available as an explicit per-attachment choice.

### Attachment design (M2)

- Replace `SourceRecord.pdfRef` (a free-form string) with an **attachment record**: `{ attachmentId, sha256, originalFileName, byteLength, mode: managed | linked, linkedPath? }`. Its schema lives in `docs/schemas`.
- **Managed:** copy to `<data dir>/attachments/<sha256>.pdf`, which de-duplicates by content. The file is read-only to the app.
- **Linked:** store the path and hash. On open, compare the hash, and warn if it differs (the page numbers may have shifted).
- Removing an attachment shows what breaks (page links) and keeps all structured content (SPEC S-04).
- Attachments are never exported (ADR-007). `pdfRef` / `linkedPath` is machine-local and already stripped from exports.
- Page navigation opens the page through the shell. It does not depend on successful text extraction (ARCHITECTURE).

### Migration plan: `pdfRef` → attachment record (M2)

A numbered database migration, so the pre-upgrade backup `tomestack.db.v<old>.bak` is taken first (`UpgradeTests`):

1. Add an `attachments` table (`attachment_id`, `sha256`, `original_file_name`, `byte_length`, `mode`, `linked_path`, `created_at`) and `sources.attachment_id`.
2. For each source with a `pdfRef`: if the file exists and is a PDF within the size limit, hash it, copy it to `attachments/<sha256>.pdf` and record it as `managed`. Otherwise, record it as `linked` with the old path and no hash, and flag it "missing; re-attach" in the UI. The migration never fails because of a missing file.
3. Bump the source schema to v2 (`attachmentId` replaces `pdfRef`). v1 sources on import still have no `pdfRef` (exports never carried one), so packages need no change.
4. Keep the original `pdfRef` in a `legacy_pdf_ref` column for one release, then drop it in a later migration.
5. Tests: a folder with an existing PDF, a missing PDF, and two sources pointing at the same file (one attachment, de-duplicated).

### Implementation (M2 item 6, 2026-09-27)

- As planned: the attachment record and table, managed copies at `attachments/<sha256>.pdf` (read-only, de-duplicated), linked files with a hash check on open, removal with a preview of the page links it breaks, and migration v3 with `tomestack.db.v2.bak` and `legacy_pdf_ref`. The tests from step 5 are in `AttachmentTests`.
- M2 review fix (2026-09-27): deleting an unused managed copy is best effort, after the commit. A file held open elsewhere stays until the next start, which deletes unused `<sha256>.pdf` copies and `.partial` leftovers (including any from a rolled-back migration v3). Before this, detaching such a file committed the change but replied with an internal error.
- **Deviation from step 3:** the source record gained `attachmentId` next to the obsolete `pdfRef`, without a source `schemaVersion`. Both fields are machine-local and stripped from every export, so the *exported* source document is unchanged. A version bump would have forced a package format change for nothing.
- **Page navigation:** a separate shell window with WebView2's built-in PDF viewer and `#page=N`. The same request blocking as the main window, so no listening socket and no network (ADR-006).
- **Corrected by the M2 review (2026-09-27):** the viewer first mapped the PDF's folder to its own virtual host and allowed anything on that host. A linked PDF in Downloads or Documents therefore exposed that whole folder, with scripts on, to any link inside the PDF. Now no folder is mapped. The window loads exactly one URL, `https://pdf.tomestack.localhost/document.pdf#page=N`, and answers it with the PDF's bytes, opened with delete sharing, so removing an open attachment still works. Every other request and navigation is refused and reported (`PdfViewerRequests`, `PdfViewerRequestsTests`; the smoke still opens page 2 with no blocked requests).
- Size limit 1 GiB. Only the `%PDF-` signature is checked; TomeStack never parses the PDF.

## Consequences

- Managed copies use disk space. The attachment UI must show the size and let the user switch a large book to linked.
- A user who deliberately keeps data in OneDrive is warned on every start, not blocked.

## Evidence

- `DataFolderTests`: containment, prefix siblings, case and trailing separators, and the `app.info` warning without the full path.
- `PackageRoundTripTests.Export_never_includes_the_machine_local_pdf_reference`.
- The ROADMAP "small usability spike" for D02 was replaced by the owner's decision.
