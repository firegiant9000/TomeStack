# ADR-005: Managed PDF attachment versus external link

Status: **proposed**. It is blocked on the owner's D02 decision (data directory and PDF default). This is a design ADR. PDF attachment itself is M2.
Date: 2026-09-25

## Context

SPEC S-04: linked PDFs are stored in an application-managed library by default, with a checksum, the original file name and source-page navigation. The user can remove an attachment without deleting accepted content. ARCHITECTURE keeps files outside the database, referenced by managed IDs and content hashes. LIVING_SPECS D02 asks whether to copy or externally link by default, with "managed copy" as the working default. MVP asks for "Open the cited page from a feature offline".

## Options

| | A. Managed copy (working default) | B. External link |
| --- | --- | --- |
| Page links survive the user moving or renaming the file | Yes | No; needs a relink flow |
| Disk use | Duplicates each PDF | None |
| Integrity check (SHA-256) | At attach time; stable afterwards | Must re-hash on every open |
| Backup completeness | A data-directory backup includes the PDFs | Backup misses the PDFs |
| Privacy of stored paths | Only managed IDs are stored | Stores a user path (may name the user) |

## Proposed design (either option)

- Replace `SourceRecord.pdfRef` (a free-form string) with an **attachment record**: `{ attachmentId, sha256, originalFileName, byteLength, mode: managed | linked, linkedPath? }`. Its schema lives in `docs/schemas`.
- **Managed:** copy to `<data dir>/attachments/<sha256>.pdf`, which de-duplicates by content. The file is read-only to the app.
- **Linked:** store the path and hash. On open, compare the hash, and warn if it differs (the page numbers may have shifted).
- Removing an attachment shows what breaks (page links) and keeps all structured content (SPEC S-04).
- Attachments are never exported (ADR-007). `pdfRef` / `linkedPath` is machine-local and already stripped from exports.
- Page navigation opens the page through the shell. It does not depend on successful text extraction (ARCHITECTURE).

## Data directory (part of D02)

Today it is `%LOCALAPPDATA%\TomeStack`, overridable with `TOMESTACK_DATA_DIR` or `--data-dir`. Risk to weigh: a data directory under `Documents` is often **OneDrive-synced**. SQLite in WAL mode plus a sync client can corrupt the database or create conflicting copies. If the owner wants a user-visible location, prefer "choose a folder, and warn when it is inside a known sync root" over defaulting to `Documents`.

## Decision

Pending owner D02. Once decided, record the chosen default here, move this ADR to accepted, and add a migration from `pdfRef` to the attachment record (the database migration also triggers the pre-upgrade backup).

## Evidence

- `PackageRoundTripTests.Export_never_includes_the_machine_local_pdf_reference`.
- No spike yet. The D02 "small usability spike" from ROADMAP is still to do.
