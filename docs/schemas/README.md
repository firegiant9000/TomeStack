# Data schemas

JSON Schema (draft 2020-12) for the JSON that TomeStack stores and exchanges. The app's serializer (`RulesJson`) is the source of truth. These schemas document its output and are checked against it by `tests/AppService.Tests/SchemaTests.cs`. The test validates every fixture and every entry of a real exported package.

| File | Describes | Version field |
| --- | --- | --- |
| `source.v1.schema.json` | `SourceRecord` (SPEC S-01) | none (v1) |
| `content-revision.v1.schema.json` | `ContentRevision` with M0 string-typed effects (ADR-002). Read and upcast; no longer written | `schemaVersion` |
| `content-revision.v2.schema.json` | `ContentRevision` with typed effects (ADR-003). Current | `schemaVersion` |
| `character.v1.schema.json` | `Character`: choices, pins and overrides. Read and upcast | `schemaVersion` |
| `character.v2.schema.json` | Adds `level`. Current | `schemaVersion` |
| `package-manifest.v1.schema.json` | `manifest.json` of a `*.tomestack.zip` ([package-format.md](../features/package-format.md)). Still importable | `formatVersion` |
| `package-manifest.v2.schema.json` | Same layout; content entries are content schema v2. Still importable | `formatVersion` |
| `package-manifest.v3.schema.json` | Adds `purpose` (`backup` / `share`) and `omitted[]` (ADR-007). Current | `formatVersion` |

The files are named `<kind>.v<version>.schema.json`. The test picks the schema from the document's own version field.

## Versioning rules

- Bump a version when a change alters how existing data is calculated or read. Unknown extra properties on revisions, effects and characters round-trip and are not a version change.
- Data with a version newer than the build supports is **refused with a diagnostic, never silently read**: `package.schema-unsupported` on import, `character.schema-unsupported` on save or validation, and `content.schema-unsupported`, which isolates the revision in calculations.
- Older versions are migrated on read. The migration is recorded in the ADR that introduced the new version.
