# Property tests (roadmap T3)

ROADMAP "Revision 2026-09-29", T3 · status: **implemented, fixture-verified (2026-10-01).**

Property tests generate many valid inputs and check that an invariant holds for every one. They use [FsCheck.Xunit](https://fscheck.github.io/FsCheck/) 3.4.0, which runs on the repository's xUnit v2 (2.9.3). They run in the ordinary `dotnet test` gate with a fixed case count each; nothing extra is needed in CI.

- Code: `tests/RulesCore.Tests/Properties/` and `tests/AppService.Tests/Properties/`. The generators (`Generators.cs`) and the golden hashes (`GoldenRevisions.cs`) live in RulesCore.Tests and are linked into AppService.Tests.
- Golden fixture: `tests/RulesFixtures/golden/revisions.json`, one original revision per content schema version, 1 to 9.

## What the generators make

Only valid domain values, so a failure is a real defect and not a generator artefact:

- **Content revisions** are generated as the stored JSON, for a chosen content schema version from 1 to 9.
  - Every effect field appears only from the version that introduced it: `grant.level` from v3, `extendsChoice` from v4, `onlyAs` and `activation` from v5, `modifier.toggle` and roll costs from v6, `multiclassCaster` from v7, `whileArmored`, `roll.bonus` and armor's Strength from v8.
  - The versioned effect types (`armor`, `spellcasting`, `toggle`, `scale`) appear at every version, because below their version they must stay unknown and byte for byte. A `multiclassCasterTable` key also appears below v9, where it must stay extension data.
  - Revisions also carry unknown effect types and unknown top-level fields, which must survive unchanged.
  - v1 revisions use the v1 effect shapes (`abilityScoreIncrease`, `initiativeBonus`).
  - Names mix in characters the serializer escapes (`"`, `\`, `<`, `&`, `é`, `✦`).
- **Rules families:** a revision names `srd-5.1`, `srd-5.2.1` or both, always as separate ids. A character has exactly one. Generated pins in the determinism property come only from revisions of the character's own family.
- **Characters** pass `Character.Validate()`, which is itself a property.
- **Formula ASTs** stay inside every ADR-003 limit (200 characters, 64 tokens, depth 8, literals up to 10,000). The test prints them with only the parentheses that precedence and left associativity need.

All generated text is original (SPEC Q-03). Shrinkers exist for formula ASTs (to subtrees and smaller literals) and revisions (dropping one effect or optional field at a time). The other generators report the failing input unshrunk, with its replay seed.

## The properties

Case counts are per run. "Time" is the local Release run on 2026-10-01; CI time is in the PR.

| # | Property (test) | Invariant | Guards | Cases | Time |
|---|---|---|---|---|---|
| 1 | `RevisionHashProperties.Serialize_deserialize_serialize_is_byte_identical_for_every_schema_version` | Once read, a revision's compact JSON is a fixed point, so its SHA-256 is stable | ADR-002 (insert-only revisions, hashes), ADR-003 "No upcast" | 1,000 | 0.7 s |
| 1 | `…An_optional_field_written_as_null_hashes_like_the_field_left_out` | `"summary": null`, `"toggle": null` and so on hash like the field left out (v2 to v9) | ADR-003 "All are optional, so older revisions serialize unchanged" | 500 | 0.3 s |
| 1 | `…A_versioned_effect_type_is_typed_only_from_its_schema_version_and_kept_unchanged_below_it` | `armor` from v4, `spellcasting` from v5, `toggle` from v6, `scale` from v9; unknown below | ADR-003 (`VersionedEffects`), ADR-010 | 500 | 0.2 s |
| 1 | `…The_golden_revision_of_each_schema_version_keeps_its_hash` (9 facts) and AppService `GoldenRevisionStoreTests` (9 facts) | Each golden revision hashes to its pinned hash, in the serializer and through `SqliteStore.AddRevision`; storing it again is "unchanged" | ADR-002, ADR-003 | 18 | < 1 s |
| 2 | `SerializationProperties.A_character_round_trips_through_its_stored_form_unchanged` | Character JSON → character → JSON is the identity | ADR-007 (packages carry characters as stored), ARCHITECTURE "round-trip unknown extension fields" | 500 | 0.4 s |
| 2 | `…A_content_revision_round_trips_unchanged_once_stored` | Same for content, with ids, version, families and effect types equal | ADR-002, ADR-003 | 500 | 0.4 s |
| 2 | `…The_character_generator_makes_valid_characters_of_one_known_rules_family` | The generator's own check | — | 500 | 0.1 s |
| 3 | `FormulaProperties.Parsing_a_printed_AST_gives_the_same_AST` | `parse(print(ast)) == ast` (structural; `CallNode.Arguments` is a list) | ADR-003 "Bounded formulas" | 1,000 | < 0.1 s |
| 3 | `…SCALE_identifiers_parse_only_where_scales_are_allowed` | A formula with `SCALE.<id>` parses with `allowScales` and fails `formula.unknown-identifier` without | ADR-010 (v9 only) | 500 | 0.1 s |
| 3 | `…Evaluation_is_total_inside_the_bounded_grammar` | Evaluation returns a value within ±1,000,000 or one of `value-unavailable`, `division-by-zero`, `out-of-range`, never throws, and gives the same answer twice | ADR-003, SPEC Q-02 (untrusted content) | 1,000 | < 0.1 s |
| 3 | `…Parsing_any_text_returns_a_result_or_an_error_code_and_never_throws` | Any string (formula fragments, or arbitrary) parses or fails with a `formula.*` code | SPEC Q-02 | 2,000 | 0.2 s |
| 4 | `DeterminismProperties.A_sheet_computed_twice_from_the_same_pins_is_equal` | The same character and pins calculate the same sheet JSON twice, and from a freshly loaded catalog | ARCHITECTURE (the sheet is derived from stored choices), ADR-003 | 300 | 2 s |
| 4 | `…The_same_seed_rolls_the_same_dice` | Two `SeededRandomSource`s with one seed roll the same record; every die is within its sides | SPEC C-04 | 1,000 | 0.3 s |
| 5 | `PackageProperties.A_library_backup_restored_into_a_clean_folder_backs_up_to_the_same_manifest_and_hashes` | Backup → restore into a clean folder (clock one day later) → backup gives the same manifest (but `createdAt`) and entry hashes, and the libraries compare equal as in `M6ExitGateTests` | ADR-007 item 10, package-format "Full library backup" | 12 | 16 s |
| 5 | `…A_source_pack_imports_into_a_clean_folder_equal_and_a_second_import_changes_nothing` | The clean folder holds every published revision with the same JSON and hash, and the same source but for `origin` and `shareConfirmedAt`; importing the pack again previews all "unchanged" and changes nothing | ADR-007, package-format rule 11 | 12 | 9 s |
| 5 | `…A_campaign_pack_imports_into_a_clean_folder_equal_and_a_second_import_changes_nothing` | Same for a campaign pack, with the campaign equal | ADR-007, package-format "Campaign packs" | 12 | 10 s |
| 6 | `MigrationProperties.A_database_of_any_older_version_migrates_keeping_its_rows_and_hashes_and_one_backup` | A database written at v1 to v8 opens at v9 with every row (sources, revisions, characters, campaigns, gap notes) and every revision hash kept. v1 hashes are rewritten once, to the hash of the upcast form. Every stored hash is the hash of what is read back. Exactly one `.bak` exists, at the opened version, holding the old rows and hashes, and opening again writes no second one | ARCHITECTURE "Migrations are numbered and backed up", ADR-002, ADR-003 "Migration (schemaVersion 1 → 2)" | 60 | 16 s |

Totals per run: **6 invariants** in 17 properties and 18 golden facts, **9,396 generated cases**. Local time added: about 2 s to RulesCore.Tests (1 s to 3 s) and about 25 s to AppService.Tests (39 s to 64 s; the package and migration properties open real data folders).

### Where this differs from the roadmap text

- **Source and campaign packs cannot be exported a second time** from the receiving folder: their sources arrive as `origin: received`, and a received source is never packed again (package-format rule 11, `pack.source-received`). For those two scopes, "export → import → export" becomes "export → import → import again", which must change nothing. Library backups do the full export → import → export.
- **Generated old databases** hold content of any version from v2 to v9 at every database version from v2, not only what the build of that version could write. Migrations v3 to v9 never touch revision JSON, so this is a wider input, not a different one. A v1 database holds only v1 revisions.

## Defects found

None in TomeStack so far. The first run found one generator bug: three classes of up to 8 levels could total more than 20. The generator was fixed and is now checked by its own property. A deliberate printer mutation (dropping right-associativity parentheses) was caught within 44 cases and shrank to `0 / 0 / 0`, which shows the parser property is not vacuous.

When a property fails, pin the shrunk input as a `[Fact]` next to the property, and fix the defect in the same change if it is small and clearly correct. Ask the owner first if the fix touches a schema, a hash or a package format. Never update `GoldenRevisions.Hashes` to make a test pass: a changed hash means every stored revision of that version would get a new one.
