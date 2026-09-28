# Data schemas

JSON Schema (draft 2020-12) for the JSON that TomeStack stores and exchanges. The app's serializer (`RulesJson`) is the source of truth. These schemas document its output and are checked against it by `tests/AppService.Tests/SchemaTests.cs`. The test validates every fixture and every entry of a real exported package.

| File | Describes | Version field |
| --- | --- | --- |
| `source.v1.schema.json` | `SourceRecord` (SPEC S-01) | none (v1) |
| `content-revision.v1.schema.json` | `ContentRevision` with M0 string-typed effects (ADR-002). Read and upcast; no longer written | `schemaVersion` |
| `content-revision.v2.schema.json` | `ContentRevision` with typed effects (ADR-003). Still read and kept as v2 (not upcast) | `schemaVersion` |
| `content-revision.v3.schema.json` | Adds `grant.level`, `hitDie`, and the `armorClass` / `hitPoints` targets (ADR-003 "Content schema v3"). An `armor` effect in a v3 revision is unknown and reference-only. Still read and kept as v3 | `schemaVersion` |
| `content-revision.v4.schema.json` | Adds the `armor` effect (M2 item 4) and `extendsChoice` (M2 item 5): an extra option of another content's choice, such as a homebrew subclass. Still read and kept as v4 | `schemaVersion` |
| `content-revision.v5.schema.json` | Adds the `spellcasting`, `spell` and `weapon` effects, the spell fields (`features/spellcasting.md`), weapon proficiency grants, `onlyAs`, restriction `multiclass` and `group`, and roll `activation` (`features/multiclass-and-attacks.md`). In an older revision these types are unknown and reference-only. Still read and kept as v5 | `schemaVersion` |
| `content-revision.v6.schema.json` | Adds the `toggle` effect, `modifier.toggle`, and roll `resourceContent`, `cost` and `variableCost` (`features/m3-effects.md`). Still read and kept as v6 | `schemaVersion` |
| `content-revision.v7.schema.json` | Adds `spellcasting.multiclassCaster` (`full` / `half` / `third`; `features/spellcasting.md`, M3 C3). Current: new revisions are written as v7 | `schemaVersion` |
| `character.v1.schema.json` | `Character`: choices, pins and overrides. Read and upcast | `schemaVersion` |
| `character.v2.schema.json` | Adds `level` and `crossFamilyExceptions`. Read and upcast | `schemaVersion` |
| `character.v3.schema.json` | Adds `classes` (levels per class) and `choices`. Read and upcast | `schemaVersion` |
| `character.v4.schema.json` | Adds `play`: current and temporary hit points, spent resources, conditions, exhaustion (M2 item 2, SPEC C-05), `equipment` (M2 item 4) and `campaignExceptions` (M2 item 7). Read and upcast | `schemaVersion` |
| `character.v5.schema.json` | Adds `play.hitDiceSpent`, `play.deathSaves` and `play.inspiration` (short rest and hit dice, SPEC C-05; [rests.md](../features/rests.md)). Read and upcast | `schemaVersion` |
| `character.v6.schema.json` | Adds `spells` (per caster, prepared or not) and `play.spellSlotsSpent` / `play.pactSlotsSpent` (D04; [spellcasting.md](../features/spellcasting.md)). Read and upcast | `schemaVersion` |
| `character.v7.schema.json` | Adds `play.toggles` (M3 B2; [m3-effects.md](../features/m3-effects.md)). Current | `schemaVersion` |
| `package-manifest.v1.schema.json` | `manifest.json` of a `*.tomestack.zip` ([package-format.md](../features/package-format.md)). Still importable | `formatVersion` |
| `package-manifest.v2.schema.json` | Same layout; content entries are content schema v2. Still importable | `formatVersion` |
| `package-manifest.v3.schema.json` | Adds `purpose` (`backup` / `share`) and `omitted[]` (ADR-007). Still importable | `formatVersion` |
| `package-manifest.v4.schema.json` | Adds `campaigns/` entries (M2 item 7); entries may be content and character schema v4. Still imported | `formatVersion` |
| `package-manifest.v5.schema.json` | Adds `gaps/` entries, backups only (M3 B3). Current for character packages | `formatVersion` |
| `package-manifest.v6.schema.json` | Adds `scope` and `revisionOrder`; `attachments/` and `files/` entries in full library backups only (M2.1). Current for library backups | `formatVersion` |
| `attachment.v1.schema.json` | `attachments/<attachmentId>.json` in a full library backup: a managed or linked PDF record (ADR-005, M2.1) | none (v1) |
| `gap-note.v1.schema.json` | A session gap note (M3 B3; [gap-notes.md](../features/gap-notes.md)). Current | `schemaVersion` |
| `campaign.v1.schema.json` | `Campaign` (SPEC P-01): rules family, allowed sources, house rules. Current | `schemaVersion` |

The files are named `<kind>.v<version>.schema.json`. The test picks the schema from the document's own version field.

## Versioning rules

- Bump a version when a change alters how existing data is calculated or read. That includes a new effect *type*: an older revision may already carry that type as an unknown effect, stored byte for byte, and typing it would change its hash and its meaning (the `armor` effect is content v4 only). Unknown extra properties on revisions, effects and characters round-trip and are not a version change.
- Data with a version newer than the build supports is **refused with a diagnostic, never silently read**: `package.schema-unsupported` on import, `character.schema-unsupported` on save or validation, and `content.schema-unsupported`, which isolates the revision in calculations.
- Older versions are migrated on read. The migration is recorded in the ADR that introduced the new version. Content revisions are an exception where a newer version is a pure superset: they keep the version they were written in, so their hashes stay stable (content v2 under v3).
