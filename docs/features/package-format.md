# Portable package format (v3; v1 and v2 still importable)

SPEC P-02 · status: implemented for characters (M0). Campaigns and assets are not yet included.

A package is a ZIP file (`*.tomestack.zip`) with this fixed layout:

```text
manifest.json
sources/<sourceId>.json        SourceRecord, including license and redistribution flag
content/<revisionId>.json      ContentRevision (immutable, pinned by characters)
characters/<characterId>.json  Character choices, pins, overrides and play state (no derived values)
```

`manifest.json`:

| Field | Meaning |
| --- | --- |
| `format` / `formatVersion` | `tomestack.package` / `3` (v3: `purpose` and `omitted`, ADR-007; v2: content entries use content schema v2 with typed effects, ADR-003). v1 and v2 packages still import as backups, and v1 revisions are upcast. Newer versions are refused with a clear message. |
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

- **`backup`** (default): characters, every pinned revision and every source, including `redistributable: false` ones. File name `<name>-personal-backup.tomestack.zip`.
- **`share`**: revisions from non-redistributable sources, and those sources, are left out and listed in `omitted[]`. Characters keep their pins. File name `<name>.tomestack.zip`.

`package.exportPreview { characterIds, purpose }` returns `{ purpose, fileName, characters, included[], omitted[] }` without writing anything. The UI shows the omitted list before a share export. `package.export` returns the package as base64 (used by browser development). In the desktop app, `package.saveAs` writes it where the user chooses in a native Save dialog, and returns only `{ saved, fileName }`. The UI waits for it without a timeout, because the response only comes once the user closes the dialog. Both take `purpose` (default `backup`).

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
9. **Source metadata is never overwritten silently.** If a package's source differs from the local record with the same ID, the preview lists each differing field (local vs. imported). Apply then needs an explicit `sourceChoices[sourceId]` of `keepLocal` or `useImported`, and refuses with `package.source-choice-required` otherwise. `pdfRef` and `attachmentId` (ADR-005) are machine-local. They are never exported, and an import never changes them; nor does it bring a PDF.
10. **Backup before replace (SPEC C-07, Q-01):** if the package replaces characters that already exist locally, apply first exports their current local copies to `<data dir>/backups/pre-import-<UTC timestamp>.tomestack.zip`. That file is an ordinary package, so you restore it by importing it. If the local copy cannot be exported (for example, a pinned revision is missing), the import is refused with `package.backup-failed`, and nothing changes.

## Decided

D03 (owner, 2026-09-26): separate `backup` and `share` exports; a share leaves out non-redistributable sources ([ADR-007](../decisions/ADR-007-export-package-and-license-policy.md)). Tests: `tests/AppService.Tests/ExportPurposeTests.cs`.
