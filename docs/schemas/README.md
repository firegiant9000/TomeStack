# Data schemas

JSON Schema (draft 2020-12) for the JSON that TomeStack stores and exchanges. The app's serializer (`RulesJson`) is the source of truth. These schemas document its output and are checked against it by `tests/AppService.Tests/SchemaTests.cs`. The test validates every fixture and every entry of a real exported package.

| File | Describes | Version field |
| --- | --- | --- |
| `source.v1.schema.json` | `SourceRecord` (SPEC S-01), as character packages and source packs write it | none (v1) |
| `source.v2.schema.json` | Adds `importDerived`, `origin` and `shareConfirmedAt` (M6 slice 1; set only by the machine that holds the source). Stored from database v8 and written only in library backups (format v7, or v9 with extensions); a record with any of them is v2 | none (by field) |
| `content-revision.v1.schema.json` | `ContentRevision` with M0 string-typed effects (ADR-002). Read and upcast; no longer written | `schemaVersion` |
| `content-revision.v2.schema.json` | `ContentRevision` with typed effects (ADR-003). Still read and kept as v2 (not upcast) | `schemaVersion` |
| `content-revision.v3.schema.json` | Adds `grant.level`, `hitDie`, and the `armorClass` / `hitPoints` targets (ADR-003 "Content schema v3"). An `armor` effect in a v3 revision is unknown and reference-only. Still read and kept as v3 | `schemaVersion` |
| `content-revision.v4.schema.json` | Adds the `armor` effect (M2 item 4) and `extendsChoice` (M2 item 5): an extra option of another content's choice, such as a homebrew subclass. Still read and kept as v4 | `schemaVersion` |
| `content-revision.v5.schema.json` | Adds the `spellcasting`, `spell` and `weapon` effects, the spell fields (`features/spellcasting.md`), weapon proficiency grants, `onlyAs`, restriction `multiclass` and `group`, and roll `activation` (`features/multiclass-and-attacks.md`). In an older revision these types are unknown and reference-only. Still read and kept as v5 | `schemaVersion` |
| `content-revision.v6.schema.json` | Adds the `toggle` effect, `modifier.toggle`, and roll `resourceContent`, `cost` and `variableCost` (`features/m3-effects.md`). Still read and kept as v6 | `schemaVersion` |
| `content-revision.v7.schema.json` | Adds `spellcasting.multiclassCaster` (`full` / `half` / `third`; `features/spellcasting.md`, M3 C3). Still read | `schemaVersion` |
| `content-revision.v8.schema.json` | Adds the `attacks` and `criticalRange` fields, armor training grants (`armor.light` … `armor.shield`, and `armor.none` for a class with none), armor `strength` and `stealthDisadvantage`, modifier `whileArmored` and roll `bonus` (M2.2, the Fighter). Still read | `schemaVersion` |
| `content-revision.v9.schema.json` | Adds the `scale` effect, the formula identifier `SCALE.<id>` and `spellcasting.multiclassCasterTable`, and allows a `choice` with no declared options (M5, [ADR-010](../decisions/ADR-010-custom-classes-and-progression.md)). Current. `content.publish` writes the lowest version a revision needs (see below), so a revision is v9 only when it uses one of these | `schemaVersion` |
| `character.v1.schema.json` | `Character`: choices, pins and overrides. Read and upcast | `schemaVersion` |
| `character.v2.schema.json` | Adds `level` and `crossFamilyExceptions`. Read and upcast | `schemaVersion` |
| `character.v3.schema.json` | Adds `classes` (levels per class) and `choices`. Read and upcast | `schemaVersion` |
| `character.v4.schema.json` | Adds `play`: current and temporary hit points, spent resources, conditions, exhaustion (M2 item 2, SPEC C-05), `equipment` (M2 item 4) and `campaignExceptions` (M2 item 7). Read and upcast | `schemaVersion` |
| `character.v5.schema.json` | Adds `play.hitDiceSpent`, `play.deathSaves` and `play.inspiration` (short rest and hit dice, SPEC C-05; [rests.md](../features/rests.md)). Read and upcast | `schemaVersion` |
| `character.v6.schema.json` | Adds `spells` (per caster, prepared or not) and `play.spellSlotsSpent` / `play.pactSlotsSpent` (D04; [spellcasting.md](../features/spellcasting.md)). Read and upcast | `schemaVersion` |
| `character.v7.schema.json` | Adds `play.toggles` (M3 B2; [m3-effects.md](../features/m3-effects.md)). Read and upcast | `schemaVersion` |
| `character.v8.schema.json` | Adds `currency`, `notes` (dated session notes) and `play.concentration` (owner, 2026-10-06; LIVING_SPECS D19, D21, D22; [sheet-play.md](../features/sheet-play.md)). Current | `schemaVersion` |
| `package-manifest.v1.schema.json` | `manifest.json` of a `*.tomestack.zip` ([package-format.md](../features/package-format.md)). Still importable | `formatVersion` |
| `package-manifest.v2.schema.json` | Same layout; content entries are content schema v2. Still importable | `formatVersion` |
| `package-manifest.v3.schema.json` | Adds `purpose` (`backup` / `share`) and `omitted[]` (ADR-007). Still importable | `formatVersion` |
| `package-manifest.v4.schema.json` | Adds `campaigns/` entries (M2 item 7); entries may be content and character schema v4. Still imported | `formatVersion` |
| `package-manifest.v5.schema.json` | Adds `gaps/` entries, backups only (M3 B3). Current for character packages | `formatVersion` |
| `package-manifest.v6.schema.json` | Adds `scope` and `revisionOrder`; `attachments/` and `files/` entries in full library backups only (M2.1). Still restored | `formatVersion` |
| `package-manifest.v7.schema.json` | Adds `scope: "source"` (a source pack: `sources/` and published `content/` only, with `revisionOrder` and `attestations`) and library backups whose sources are v2 (M6 slice 1; number final, merged to main 2026-09-30). Current for library backups and source packs | `formatVersion` |
| `package-manifest.v8.schema.json` | Adds `scope: "campaign"` (a campaign pack: one `campaigns/` entry, `sources/` and published `content/`, with `revisionOrder`, `attestations` and `omitted[]` without revisions; M6 slice 2; number final, merged to main 2026-09-30). Current for campaign packs | `formatVersion` |
| `package-manifest.v9.schema.json` | A full library backup that keeps installed extensions (`extensions/<sha256>.zip`, kind `extension`, without grants; M6 slice 3; number final, merged to main 2026-09-30). A backup with no extension stays v7 | `formatVersion` |
| `extension-manifest.v1.schema.json` | `extension.json` of a `*.tomestack-ext.zip` (ADR-011 option A; [extensions.md](../features/extensions.md)) | `formatVersion` |
| `extension-transform.v1.schema.json` | `transforms/<hook>.json`: the declarative transform language (extension API 1) | none (API 1) |
| `sheet-export.v1.schema.json` | The sheet export model v1 that extension exports and adapters read (ADR-011, ADR-007 item 11): an allowlist, filtered by purpose | `formatVersion` |
| `export-foundry-dnd5e.6.0.5.schema.json` | Exactly the Foundry VTT Actor subset the dnd5e adapter writes, pinned to dnd5e 6.0.5 with Foundry 14.367 (ADR-012; [export-adapters.md](../features/export-adapters.md)). A new target version gets a new file | none (named by the target version) |
| `attachment.v1.schema.json` | `attachments/<attachmentId>.json` in a full library backup: a managed or linked PDF record (ADR-005, M2.1) | none (v1) |
| `gap-note.v1.schema.json` | A session gap note (M3 B3; [gap-notes.md](../features/gap-notes.md)). Still written for every note about a feature or a field, so a backup without an import note stays readable by older builds | `schemaVersion` |
| `gap-note.v2.schema.json` | v2 adds target kind `import` (D16b): an unmatched item of a character-sheet import, with only a label (`features/ddb-pdf-import.md`). Written only for import notes; an older build refuses it with `package.invalid-json`, not `package.schema-unsupported`: it reads the entry before its version, and the unknown kind `import` fails that read. Current | `schemaVersion` |
| `campaign.v1.schema.json` | `Campaign` (SPEC P-01): rules family, allowed sources, house rules, and (M6 slice 2) the optional `pendingSources`, which changes how no existing field is read, so it stays v1. Current | `schemaVersion` |

The files are named `<kind>.v<version>.schema.json`. The test picks the schema from the document's own version field.

## Versioning rules

- Bump a version when a change alters how existing data is calculated or read. That includes a new effect *type*: an older revision may already carry that type as an unknown effect, stored byte for byte, and typing it would change its hash and its meaning (the `armor` effect is content v4 only). Unknown extra properties on revisions, effects and characters round-trip and are not a version change.
- Data with a version newer than the build supports is **refused with a diagnostic, never silently read**: `package.schema-unsupported` on import, `character.schema-unsupported` on save or validation, and `content.schema-unsupported`, which isolates the revision in calculations.
- Older versions are migrated on read. The migration is recorded in the ADR that introduced the new version. Content revisions are an exception where a newer version is a pure superset: they keep the version they were written in, so their hashes stay stable (content v2 under v3).
- **Content revisions are published in the lowest version that holds them (M2.2).** `content.publish` writes `ValidationReport.RequiredSchemaVersion`: the highest version among the features the revision uses (the same checks as `validate.requires-v3` … `requires-v9`, plus the typed effects, the spell field targets and, since v9, the identifiers its formulas read), never below v3 and never above the draft's version. v3 is the floor because builds from v3 on validate before publishing, and a package import blocks on validation errors only from v3. So homebrew that uses no v8 field is written as v3 to v7 and stays readable by 0.3.x builds. The published revision is a new insert with its own hash; the draft keeps its version and no existing revision changes. Bundled packs declare their version by hand, by the same rule.
