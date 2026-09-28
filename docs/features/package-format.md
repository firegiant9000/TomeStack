# Portable package format (v2; v1 still importable)

SPEC P-02 · status: implemented for characters (M0). Campaigns and assets are not yet included.

A package is a ZIP file (`*.tomestack.zip`) with this fixed layout:

```text
manifest.json
sources/<sourceId>.json        SourceRecord, including license and redistribution flag
content/<revisionId>.json      ContentRevision (immutable, pinned by characters)
characters/<characterId>.json  Character choices, pins and overrides (no derived values)
```

`manifest.json`:

| Field | Meaning |
| --- | --- |
| `format` / `formatVersion` | `tomestack.package` / `2` (v2: content entries use content schema v2 with typed effects, ADR-003). v1 packages still import, and their revisions are upcast. Newer versions are refused with a clear message. |
| `createdAt`, `appVersion` | Provenance of the export. |
| `characters` | Character IDs included. |
| `entries[]` | `path`, `kind`, `sha256`, `size` for every non-manifest entry. |
| `notices[]` | Title, publisher, license, `redistributable` and attribution for every included source. |
| `attachmentPolicy` | States that PDFs are never included. |

JSON is indented UTF-8 with camelCase names and string enums, and entries are sorted. The same data at the same time produces byte-identical packages, so diffs are readable.

## Export

`package.export` returns the package as base64 (used by browser development). In the desktop app, `package.saveAs` writes it where the user chooses in a native Save dialog, and returns only `{ saved, fileName }`. The UI waits for it without a timeout, because the response only comes once the user closes the dialog.

Every limit and check below has its own test in `tests/AppService.Tests/PackageLimitTests.cs`.

## Import rules (untrusted input, SPEC Q-02)

1. Packages over 50 MB, with more than 2,000 entries, with an entry over 5 MB, or whose entries together unpack to more than 64 MB (`package.content-too-large`, against archives that expand far beyond their size) are refused. Sizes are checked while reading, not only from the header.
2. Only the paths above are allowed; anything else, including `..` or absolute paths, is refused before any entry is unpacked. There is no extraction to disk.
3. Every entry must be listed in the manifest and match its SHA-256. Unlisted, missing or duplicate entries are refused.
4. Every entry must be a valid document of its kind (`package.invalid-json`); a malformed *effect* inside a valid revision is kept as reference-only instead (ADR-003). File names must match the IDs inside them. Entries with a `schemaVersion` newer than this build supports are refused (`package.schema-unsupported`). JSON Schemas for every entry kind and for the manifest are in [docs/schemas](../schemas/README.md).
5. Every character pin and every revision's source must resolve within the package or the local store.
6. A revision with the same ID but different content is a blocking conflict. Published revisions are immutable.
7. **Preview first:** the user sees what will be added, left unchanged or replaced, plus warnings and license notices. **Apply** re-validates from the bytes and commits in one SQLite transaction.
8. Draft revisions stay drafts and remain inactive after import.
9. **Source metadata is never overwritten silently.** If a package's source differs from the local record with the same ID, the preview lists each differing field (local vs. imported). Apply then needs an explicit `sourceChoices[sourceId]` of `keepLocal` or `useImported`, and refuses with `package.source-choice-required` otherwise. `pdfRef` is machine-local. It is never exported, and an import never changes it.
10. **Backup before replace (SPEC C-07, Q-01):** if the package replaces characters that already exist locally, apply first exports their current local copies to `<data dir>/backups/pre-import-<UTC timestamp>.tomestack.zip`. That file is an ordinary package, so you restore it by importing it. If the local copy cannot be exported (for example, a pinned revision is missing), the import is refused with `package.backup-failed`, and nothing changes.

## Not yet decided

Whether non-redistributable sources should be excluded from *shared* packages as opposed to personal backups (LIVING_SPECS D03). Today they are included and flagged `redistributable: false`. [ADR-007](../decisions/ADR-007-export-package-and-license-policy.md) proposes separate `backup` and `share` purposes (manifest v2) for the owner to decide.
