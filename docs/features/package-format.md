# Portable package format v1

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
| `format` / `formatVersion` | `tomestack.package` / `1`. Newer versions are refused with a clear message. |
| `createdAt`, `appVersion` | Provenance of the export. |
| `characters` | Character IDs included. |
| `entries[]` | `path`, `kind`, `sha256`, `size` for every non-manifest entry. |
| `notices[]` | Title, publisher, license, `redistributable` and attribution for every included source. |
| `attachmentPolicy` | States that PDFs are never included. |

JSON is indented UTF-8 with camelCase names and string enums, and entries are sorted. The same data at the same time produces byte-identical packages, so diffs are readable.

## Import rules (untrusted input, SPEC Q-02)

1. Packages over 50 MB, with more than 2,000 entries, or with an entry over 5 MB (checked while reading, not only from the header) are refused.
2. Only the paths above are allowed; anything else, including `..` or absolute paths, is refused. There is no extraction to disk.
3. Every entry must be listed in the manifest and match its SHA-256. Unlisted, missing or duplicate entries are refused.
4. File names must match the IDs inside them.
5. Every character pin and every revision's source must resolve within the package or the local store.
6. A revision with the same ID but different content is a blocking conflict. Published revisions are immutable.
7. **Preview first:** the user sees what will be added, left unchanged or replaced, plus warnings and license notices. **Apply** re-validates from the bytes and commits in one SQLite transaction.
8. Draft revisions stay drafts and remain inactive after import.

## Not yet decided

Whether non-redistributable sources should be excluded from *shared* packages as opposed to personal backups (LIVING_SPECS D03). Today they are included and flagged `redistributable: false`.
