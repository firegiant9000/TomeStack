# Implementation plan: character import from a D&D Beyond PDF sheet

Spec: [ddb-pdf-import.md](ddb-pdf-import.md) (reviewed 2026-10-02). Status: **built as planned in slices S0 to S4 and S6 (PRs #57 to #63); the contract additions and deviations are in the LIVING_SPECS change history, and this plan is kept as written for the record** · D16f and D16g added 2026-10-05 (PR `ddb-subclass-equip`). Every slice lands on its own branch off `main`, as a PR, with tests written first, the doc updates LIVING_SPECS asks for, and the full CLAUDE.md gate green (warning budget 0, `TreatWarningsAsErrors`).

The gate, for every slice:

```
npm ci --prefix src/Ui
npm run lint --prefix src/Ui
npm test --prefix src/Ui
npm run build --prefix src/Ui
dotnet build TomeStack.slnx -c Release
dotnet test TomeStack.slnx -c Release
npm run test:e2e --prefix src/Ui
scripts/smoke.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe
scripts/single-instance-check.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe
```

## Order, parallelism and what blocks on the owner

| Slice | Depends on | Can run beside | Blocked on the owner |
| --- | --- | --- | --- |
| S0 Spike | S1's worker request and `WorkerFormReader` (the first half of S1; the fixture writer and the committed fixture can come after S0) | the rest of S1 | The owner runs it on their exports and pastes the finding into the spec. Everything in S2 that touches field names waits for it |
| S1 Reader | nothing | S0, the S2 parser scaffold (semantic types and matching rules are layout-independent) | no |
| S2 Parser | S1 (the field list shape); the **map contents** wait for S0 | S3's matching and back-solve (they take a hand-built `DdbSheet`) | S0's findings |
| S3 Proposal | S2's `DdbSheet`; S1's reader for `ddb.read` | the S4 UI can be built against `ddb.preview`'s contract once its types are fixed | no |
| S4 Apply and UI | S3 | nothing | D16b is given; the gap-note v2 schema needs no further approval. The manual accessibility pass is an owner check |
| S5 Drafts | S4; owner decision after S6 | n/a | D16c (revisited after S6) |
| S6 Acceptance | S4 and the owner's `sheet.pdf` | nothing | The owner's export in the local folder, and their `expectations.json` |

Schema or migration changes: **S4 only** (gap-note schema v2, D16b). No database migration, no package format number, no character or content schema change in any slice.

Branches: `ddb-s0-spike`, `ddb-s1-reader`, `ddb-s2-parser`, `ddb-s3-proposal`, `ddb-s4-apply-ui`, `ddb-s6-acceptance`. S2 stacks on S1 and S3 on S2 if they are in flight together; otherwise each off `main`.

## Global constraints (from the spec and CLAUDE.md)

- `RulesCore` gains nothing. `src/ImportWorker` stays `net10.0` with no Windows references.
- The UI calls `src/Ui/src/api/client.ts` only; `fetch` stays in `transport.ts`.
- No socket. The worker is a child process over stdin and stdout.
- No D&D Beyond text, values or screenshots in the repository. Fixtures are invented; every fixture name starts with "Fixture" or "Testy". The owner's PDF and S0's `fields.json` live only in `tests/RulesFixtures/local/ddb-import/` (gitignored, `.gitignore:28`).
- Error messages, logs, test output and `report.md` carry codes and counts, never a field value.
- Limits: 20 MB, 50 pages, 2,000 fields, 20,000 characters per value, 1 MB of values, 30 s per read, 8 live tokens, 30 minutes per token.
- Only published revisions are matched. Every import makes a new character id.

---

## S0 · Spike: the field inventory (local only)

**Goal.** Learn the real layouts without committing a value.

**Files**

- Modify `src/DevHost/Program.cs:8-13` and `64-90`: a third mode `--ddb-fields <pdf> [--out <fields.json>]` beside `--drill-report`, exiting before any host starts.
- Create `src/AppService/Diagnostics/FormInventory.cs`: `public static class FormInventory { public static Report Read(string workerPath, string pdfPath); public sealed record Report(int FieldCount, IReadOnlyList<Entry> Fields); public sealed record Entry(string Name, string Type, int? Page, int ValueLength, bool? Checked, string? OnState); }`. It reads through S1's `WorkerFormReader`: the app never parses a PDF itself (ADR-009), so there is no in-process shortcut. **Build S1's worker request and reader first** (they are small and layout-independent), then S0 on top; the fixture writer and the map-dependent tests of S1 can follow S0.
- Create `docs/features/ddb-pdf-import.md` section "S0 findings (YYYY-MM-DD)" (the owner or the engineer writes it from `fields.json`; counts, names of semantic things, never values).

**Tests first** (`tests/AppService.Tests/FormInventoryTests.cs`)

- `The_inventory_of_the_fixture_sheet_lists_every_field_with_its_type_page_and_value_length_and_no_value` (reads `tests/RulesFixtures/pdf/fixture-ddb-sheet.pdf`, asserts the JSON has no `value` property and no fixture value string anywhere).
- `The_inventory_refuses_a_file_that_is_not_a_pdf_with_a_code` (`pdf.not-a-pdf`).

**Doc updates.** The findings section in the spec; a line in `docs/features/restore-drill-procedure.md` is not needed (different tool). LIVING_SPECS change-history entry "S0 spike tool (dev-only)".

**Done.** Gate green; the owner has run `dotnet run --project src/DevHost -- --ddb-fields <their export> --out tests/RulesFixtures/local/ddb-import/fields.json` on each layout they have and the spec has the findings; `git status` shows nothing under `local/`.

---

## S1 · Reader: worker `formFields`, limits, fixture writer, hostile inputs

**Files**

- Modify `src/ImportWorker/Extraction/WorkerProtocol.cs:11`: `WorkerRequest` gains `string? Kind = null` (`"extract"` when null, or `"formFields"`) and `FormLimits? Form = null`. `WorkerMessage` (line 17) gains `IReadOnlyList<FormField>? Fields = null`. `WorkerMain.RunAsync` (50-97) branches on `Kind` after the `worker` line: `formFields` runs `AcroFormReader.Read(path, limits, form)` and writes one `fields` message then `done`.
- Create `src/ImportWorker/Forms/FormContracts.cs`:
  - `public sealed record FormField(string Name, string Type, int? Page, string? Value = null, bool? Checked = null, IReadOnlyList<string>? Selected = null);` (`Type` is the `AcroFieldType` name in lower case: `text`, `checkbox`, `radio`, `combo`, `list`, `other`).
  - `public sealed record FormLimits { int MaxFields = 2_000; int MaxValueChars = 20_000; int MaxTotalValueChars = 1_048_576; static FormLimits Default; }`.
  - `public static int MaxFieldsMessageChars(FormLimits f) => 6 * f.MaxTotalValueChars + 256 * f.MaxFields + 64 * 1024;`.
- Create `src/ImportWorker/Forms/AcroFormReader.cs` (runs in the child): `public static IReadOnlyList<FormField> Read(string path, ExtractionLimits limits, FormLimits form)`. Reuses `PdfPigExtractor.CheckFile` and `Open` (make them `internal static`), checks `NumberOfPages <= limits.MaxPages`, calls `TryGetForm`; throws `ExtractionException("ddb.no-form-fields", …)` when there is none or it has no terminal fields; walks `AcroForm.Fields` recursively through `AcroNonTerminalField.Children`, building the full name by joining `PartialName`s with `.`; stops with `ddb.too-many-fields` / `ddb.value-too-long` at the limits; maps every other PdfPig exception to `pdf.unreadable`.
- Create `src/ImportWorker/Forms/WorkerSession.cs`: the parts of `WorkerProcessExtractor.cs:108-209` both callers need: `internal sealed class WorkerSession : IAsyncDisposable { static Task<WorkerSession> StartAsync(string workerPath, ExtractionLimits limits, int maxLineChars, CancellationToken ct); Task SendAsync(WorkerRequest request); Task<WorkerMessage> NextAsync(TimeSpan timeout, CancellationToken ct); }` with the heap-cap hello check, the memory watchdog, the stderr sink and `Kill`. `WorkerProcessExtractor` is refactored onto it with its tests unchanged.
- Create `src/ImportWorker/Forms/WorkerFormReader.cs`: `public sealed class WorkerFormReader(string workerPath, ExtractionLimits? limits = null, FormLimits? form = null) : IFormReader { Task<IReadOnlyList<FormField>> ReadAsync(string path, CancellationToken ct); }` with `public interface IFormReader` beside it (the app injects a fake in tests, as `IDocumentExtractor` is injected, `TomeStackApp.cs:83`). Accepts exactly `worker`, `fields`, `done`, `error`; anything else is `worker.protocol`.
- Create `tests/ImportWorker.Tests/FormPdfWriter.cs`: `internal static class FormPdfWriter { static byte[] Write(IReadOnlyList<FormSpec> fields, int pages = 2, bool needAppearances = true, string onState = "Yes", bool withJavaScript = false, bool nested = false); record FormSpec(string Name, string? Text = null, bool? Checked = null, int Page = 1); }`. Writes objects by hand: catalog (`/AcroForm << /Fields [...] /NeedAppearances true >>`, with `/OpenAction` or `/AA` JavaScript when asked), pages, one widget per field (`/Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (value) /Rect [...]`, or `/FT /Btn /V /Yes /AS /Yes`), nested fields as a parent with `/Kids` when `nested`, then the xref table and trailer. Header `%PDF-1.7`.
- Create `tests/ImportWorker.Tests/FixtureSheets.cs`: `internal static class FixtureSheets { static byte[] Sheet2014(); static byte[] Sheet2024(); static string CommittedPath; }` with the invented character ("Testy McFixture", "Fixture Fighter 3", invented species, three invented spells, four invented items). Field names come from the maps once S0 has settled them; until then placeholder names prefixed `fixture.` and the test that compares the committed file is added when the maps exist.
- Create `tests/RulesFixtures/pdf/fixture-ddb-sheet.pdf` (generated; `TOMESTACK_WRITE_FIXTURES=1` writes it). Copied to the test output like `fixture-import.pdf` (check `tests/ImportWorker.Tests/TomeStack.ImportWorker.Tests.csproj` and `tests/AppService.Tests` for the `RulesFixtures` item group and add the file).

**Tests first** (`tests/ImportWorker.Tests/FormReaderTests.cs`)

- `A_hand_written_form_reads_every_text_field_and_checkbox_with_its_full_name_type_page_and_value`
- `Nested_fields_get_the_period_joined_full_name`
- `A_checkbox_with_another_on_state_is_read_as_checked`
- `A_pdf_without_a_form_is_refused_with_ddb_no_form_fields`
- `A_form_with_only_an_xfa_stream_and_no_terminal_fields_is_refused_with_ddb_no_form_fields`
- `More_than_the_field_limit_is_refused_with_ddb_too_many_fields_before_values_are_held`
- `A_value_over_the_length_limit_is_refused_with_ddb_value_too_long`
- `Values_over_the_total_limit_are_refused_with_ddb_value_too_long`
- `A_pdf_with_javascript_reads_its_fields_and_runs_nothing` (the file has an `/OpenAction` JavaScript writing a marker file; assert the marker does not exist)
- `An_encrypted_form_is_refused_with_pdf_encrypted` (reuse the encrypted fixture from `ExtractionTests`)
- `A_truncated_form_is_pdf_unreadable_never_invented_fields`
- `More_than_fifty_pages_is_pdf_too_many_pages_before_any_field_is_read`
- `The_committed_fixture_sheet_has_exactly_the_generators_fields` (added once the maps exist; same pattern as `ExtractionTests.The_committed_fixture_book_has_exactly_the_generators_original_text`)

`tests/ImportWorker.Tests/WorkerProcessTests.cs` (extend)

- `A_formFields_request_gets_worker_fields_and_done_through_the_child_process`
- `A_formFields_request_on_a_file_without_a_form_gets_an_error_line_and_exit_code_2`
- `A_fields_line_longer_than_the_form_limit_is_refused_before_it_is_held`
- `A_formFields_read_that_sends_no_line_for_the_timeout_is_stopped_with_worker_page_timeout`
- `Extraction_still_passes_every_existing_WorkerProcessTests_after_the_session_refactor` (no new test; the existing ones stay green)

**Doc updates.** `docs/features/pdf-import.md` "D1" gains one paragraph: the worker's second request kind and its limits. ADR-009 "Consequences": one line. LIVING_SPECS change history. `docs/CHANGELOG.md`: nothing yet (nothing user-visible).

**Done.** Gate green. `WorkerProcessTests` count unchanged or higher. The smoke still imports its two-page PDF (the shipped worker's default kind is unchanged).

---

## S2 · Parser: layout maps and `DdbSheet`

**Files**

- Create `src/AppService/CharacterImport/DdbSheet.cs`:
  ```csharp
  public sealed record DdbSheet(string Layout, string? SuggestedFamily, Read<string> Name, Read<IReadOnlyList<ClassText>> Classes, Read<string> Species, Read<string> Background,
      IReadOnlyDictionary<Ability, Read<int>> Abilities, IReadOnlyDictionary<Ability, Read<bool>> SaveProficient, IReadOnlyDictionary<string, Read<bool>> SkillProficient,
      IReadOnlyList<Read<string>> Feats, IReadOnlyList<Read<SpellText>> Spells, IReadOnlyList<Read<ItemText>> Items, IReadOnlyList<Read<string>> Features,
      IReadOnlyDictionary<string, Read<int>> Numbers, DdbPlay Play);
  public sealed record ClassText(string Name, int Level, string? Subclass);
  public sealed record SpellText(string Name, bool? Prepared);
  public sealed record ItemText(string Name, int Quantity, bool? Equipped);
  public sealed record DdbPlay(Read<int>? CurrentHitPoints, Read<int>? TemporaryHitPoints, IReadOnlyList<(int Die, int Spent)> HitDiceSpent, Read<int>? DeathSuccesses, Read<int>? DeathFailures, Read<bool>? Inspiration, IReadOnlyList<(int Level, int Spent)> SpellSlotsSpent);
  public readonly record struct Read<T>(T? Value, ReadStatus Status) { public static Read<T> Ok(T v); public static Read<T> Missing; public static Read<T> Unreadable; }
  public enum ReadStatus { Ok, Missing, Unreadable }
  ```
  `Numbers` is keyed by calculated field id (`proficiencyBonus`, `armorClass`, `initiative`, `hitPoints`, `save.str`, `skill.athletics`, `spellAttack`, `spellSaveDc`, `spellSlots.1`…).
- Create `src/AppService/CharacterImport/LayoutMap.cs`: `public sealed record LayoutMap(string Id, int Version, string? SuggestedFamily, IReadOnlyList<string> Required, IReadOnlyDictionary<string, FieldRule> Fields, string CheckboxOnState, IReadOnlyList<SplitRule> Splits)`, `FieldRule(string Semantic, string? Pattern)`, `SplitRule(string Semantic, string Separator)`. Loaded from embedded `Layouts/ddb-*.v1.json` by `LayoutMaps.All` (validated once at load: every `Semantic` is in `DdbSemantics.Known`, every pattern compiles with a 100 ms timeout).
- Create `src/AppService/CharacterImport/Layouts/ddb-2014.v1.json` and `ddb-2024.v1.json` (names from S0; `Unverified` until then).
- Create `src/AppService/CharacterImport/DdbParser.cs`: `public static class DdbParser { public static LayoutMap? Recognise(IReadOnlyList<FormField> fields); public static DdbSheet Parse(LayoutMap map, IReadOnlyList<FormField> fields); }`. Total: every branch yields `Missing` or `Unreadable`, never throws. Regexes: `[GeneratedRegex(..., RegexOptions.None, 100)]` as `CandidateDetector.cs:693-711`. Class text: `^(?<name>[^/\d]{1,60}?)\s+(?<level>[1-9]|1[0-9]|20)(?:\s*\((?<sub>[^()]{1,60})\))?$` per `/`-separated part (**unverified** shape; the map's `Splits` and `Pattern` override it).
- Modify `src/AppService/TomeStack.AppService.csproj`: `<EmbeddedResource Include="CharacterImport\Layouts\*.json" />`.

**Tests first** (`tests/AppService.Tests/DdbParserTests.cs`)

- `Every_layout_map_is_valid_names_only_known_semantics_and_its_patterns_compile`
- `The_fixture_2014_sheet_is_recognised_as_the_2014_layout_and_parses_to_the_expected_DdbSheet`
- `The_fixture_2024_sheet_is_recognised_as_the_2024_layout_and_suggests_srd_5_2_1`
- `A_field_set_missing_a_required_name_is_not_recognised`
- `A_missing_optional_field_is_Missing_and_an_unparsable_value_is_Unreadable_not_an_error` (level "25", score "abc", quantity "0")
- `Class_text_with_two_classes_keeps_the_sheets_order_and_reads_each_subclass`
- `Class_levels_that_add_to_more_than_twenty_are_Unreadable_as_a_whole`
- `Unmapped_fields_are_not_in_the_DdbSheet` (a field named like a player name is given a value; assert the value appears nowhere in the serialized sheet)
- `No_message_or_diagnostic_quotes_a_field_value` (parse a sheet whose every value is a unique sentinel; collect every string the parser produces; assert none contains a sentinel)

`tests/AppService.Tests/Properties/DdbParserPropertyTests.cs` (FsCheck.Xunit 3.4.0, as `docs/testing/properties.md`)

- `The_parser_never_throws_over_any_field_set` (generated names from the map plus random names, random strings up to 20,000 characters including control characters, random checkbox states).
- `Parsing_is_deterministic` (same input twice gives equal sheets).

**Doc updates.** Spec "Architecture: layout maps" if S0 changed the semantic keys. LIVING_SPECS change history.

**Done.** Gate green. Both fixture sheets parse. The property tests run under the same seed policy as the T3 suites.

---

## S3 · Proposal: matching, choices, the back-solve, tokens, `ddb.preview`

**Files**

- Create `src/AppService/CharacterImport/ImportSessions.cs`: `internal sealed class ImportSessions(TimeProvider time) { Guid Add(DdbSheet sheet); DdbSheet? Take(Guid token); DdbSheet? Peek(Guid token); bool Discard(Guid token); void Clear(); const int MaxLive = 8; static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30); }`. `Peek` is for `ddb.preview` (the token stays), `Take` for `ddb.apply`.
- Create `src/AppService/CharacterImport/Normalise.cs`: `public static string Name(string s)` (case fold, trim, collapse whitespace, fold `‘’“”–—` to ASCII).
- Create `src/AppService/CharacterImport/Matcher.cs`: `internal static class Matcher { static IReadOnlyList<MatchRow> Propose(DdbSheet sheet, IReadOnlyList<ContentOption> options, CharacterSheet draftSheet, IReadOnlyDictionary<string, Resolution> resolutions); }` with
  ```csharp
  public enum MatchKind { Species, Background, Class, Subclass, Feat, Skill, Spell, Item, Feature }
  public enum MatchStatus { Matched, Choose, NotFound, NoPlace, Unreadable, LeftOut }
  public sealed record MatchRow(string RowId, MatchKind Kind, string Label, MatchStatus Status, IReadOnlyList<MatchCandidate> Candidates, MatchCandidate? Chosen, string? Note);
  public sealed record MatchCandidate(ContentReference Reference, string Name, string SourceTitle, IReadOnlyList<string> Families, bool? AllowedInCampaign, Placement Placement);
  public sealed record Placement(PlacementKind Kind, ContentReference? ChoiceSource = null, string? ChoiceId = null, Guid? Caster = null);
  public enum PlacementKind { Pin, Choice, Class, Spell, Equipment, None }
  public sealed record Resolution(string RowId, ContentReference? Chosen, bool LeaveOut, Guid? Caster = null);
  ```
  `RowId` is stable per sheet item (`spell:3`, `skill:athletics`, `class:0:subclass`). `Label` is the sheet's text, shown to the user only.
- Create `src/AppService/CharacterImport/AbilitySolver.cs`: `internal static class AbilitySolver { static AbilityPlan Solve(Func<AbilityScores, CharacterSheet> preview, IReadOnlyDictionary<Ability, int> sheetScores); }` with `AbilityPlan(AbilityScores ProposedBase, IReadOnlyList<AbilityNote> Notes)`, `AbilityNote(Ability Ability, string Code, string Message)` (`ability.set-by-content`, `ability.cap-ambiguous`, `ability.out-of-range`). Implements spec steps 1–3 exactly, reading `DerivedValue.Trace` operations `add`, `replace`, `set`.
- Create `src/AppService/CharacterImport/Proposal.cs`: `internal static class Proposal { static Character Build(DdbSheet sheet, string family, Guid? campaignId, IReadOnlyList<MatchRow> rows, AbilityScores baseScores, IReadOnlyList<NumberChoice> numbers, bool includePlayState, Guid id, Func<Character, ContentReference, string, IReadOnlyList<ContentReference>, Character> withChoice); }`: classes first, species and background pins, then subclass choices, then the rest through `withChoice` (make `TomeStackApp.WithChoice` `internal`); equipment merged by item with summed quantity capped at 9,999; spells deduplicated per caster, cantrips `Prepared: true`, at most 500; `Overrides` from `numbers` with reason `"Imported from D&D Beyond"`; `Play` from `DdbPlay` when `includePlayState`.
- Create `src/AppService/CharacterImport/DdbImport.cs` (`partial class TomeStackApp`): 
  - `public DdbReadResult ReadDdbSheet(string path)` (calls `IFormReader`, `DdbParser.Recognise`/`Parse`, `ImportSessions.Add`) and `public DdbReadResult ReadDdbSheetData(string fileName, byte[] bytes)` (length check, `tmp/ddb-<guid>.pdf`, `finally` delete).
  - `public DdbPreview PreviewDdbImport(DdbPreviewRequest request)` → `DdbPreview(IReadOnlyList<MatchRow> Matches, IReadOnlyList<ChoiceStatus> OpenChoices, IReadOnlyList<NumberRow> Comparison, AbilityPlan AbilityPlan, DdbReport Report, IReadOnlyList<Diagnostic> Diagnostics, bool CanApply)`; `NumberRow(string Field, string Label, int? Sheet, int Calculated, bool Differs)`; `DdbReport(int Matched, int Chosen, int NotFound, int NoPlace, int Unreadable, int LeftOut, IReadOnlyList<string> NotBroughtOver, bool SameNameExists, bool FamilyMismatch)`.
  - `public sealed record DdbReadResult(Guid Token, string Layout, string? SuggestedFamily, DdbSummary Summary)`; `DdbSummary(string Name, string ClassText, int Features, int Spells, int Items)`.
  - `public sealed record DdbPreviewRequest(Guid Token, string RulesFamily, Guid? CampaignId, IReadOnlyList<Resolution>? Resolutions, IReadOnlyList<NumberChoice>? NumberChoices, bool IncludePlayState)`; `NumberChoice(string Field, NumberAction Action)`; `enum NumberAction { UseTomeStack, KeepSheet, Note }`.
  - A startup sweep of `tmp/ddb-*.pdf` in `TomeStackApp.Open` and `ImportSessions.Clear()` in `Dispose` (`TomeStackApp.cs:356-361`).
- Modify `src/AppService/CommandDispatcher.cs`: commands `ddb.read`, `ddb.readData`, `ddb.preview`, `ddb.discard` (`ddb.apply` is S4); `ddb.read` next to `ChooseExtensionInput` (321-334) uses `host.ChooseOpenFile("D&D Beyond character sheet", ".pdf")`.
- Modify `src/AppService/TomeStackApp.cs:83-92`: an optional `ImportWorker.Forms.IFormReader? formReader` parameter on `Open`, defaulting to `WorkerFormReader` over `WorkerFileName`.

**Tests first** (`tests/AppService.Tests/DdbImportTests.cs`, with a `FakeFormReader` that returns a field list, and sheets built through `FixtureSheets`' field lists rather than PDFs)

Matching:
- `A_name_installed_once_in_the_family_is_Matched_and_shows_its_source_and_family`
- `A_name_installed_in_two_sources_is_Choose_and_never_picked_silently`
- `A_name_installed_only_in_the_other_family_is_NotFound`
- `A_matched_subclass_is_recorded_as_the_class_subclass_choice_not_a_pin`
- `A_homebrew_subclass_that_extends_the_SRD_class_choice_is_a_candidate`
- `A_proficient_skill_is_matched_by_the_options_grant_target_and_recorded_on_the_first_choice_that_offers_it`
- `A_skill_both_the_class_and_the_background_offer_is_recorded_once`
- `A_2024_general_feat_with_no_open_choice_is_NoPlace_and_listed`
- `A_subclass_row_is_not_offered_for_a_class_below_its_choice_level`
- `Classes_keep_the_sheets_order_and_the_first_is_the_starting_class`
- `A_spell_on_one_casters_list_goes_to_that_caster_and_one_on_two_lists_asks`
- `A_spell_on_no_casters_list_asks_with_every_caster`
- `A_cantrip_is_always_prepared`
- `The_same_spell_twice_for_one_caster_is_recorded_once_and_under_two_casters_twice`
- `More_than_MaxSpells_spells_record_the_first_five_hundred_and_list_the_rest`
- `Two_rows_of_the_same_item_merge_into_one_entry_with_the_summed_quantity_capped`
- `A_feature_the_sheet_has_but_the_calculated_sheet_lacks_is_NotFound_and_a_granted_one_is_not_pinned`
- `Normalisation_folds_case_whitespace_quotes_and_dashes_and_nothing_else` (a near-miss stays NotFound)
- `A_campaign_that_does_not_allow_the_option_source_still_lists_it_with_a_warning`

Back-solve (`tests/AppService.Tests/AbilitySolverTests.cs`):
- `A_plain_bonus_gives_sheet_minus_bonus`
- `A_sheet_score_of_twenty_under_a_plus_two_gives_base_eighteen_and_notes_the_ambiguity`
- `A_sheet_score_above_twenty_gives_base_equal_to_the_score_minus_penalties`
- `A_penalty_and_a_bonus_together_solve_in_the_calculators_order`
- `A_score_set_by_content_is_not_solved_and_is_noted`
- `A_score_replaced_by_content_is_not_solved_and_is_noted`
- `A_bonus_the_family_policy_refuses_does_not_count` (a 2014 background bonus under `srd-5.1`)
- `The_second_preview_equals_the_sheet_for_every_solved_score`
- Property (`Properties/AbilitySolverPropertyTests.cs`): `For_any_base_and_bonus_set_the_solver_recovers_a_base_whose_preview_equals_the_sheet_or_notes_why_not` (generate base 1–30, bonuses −5..+5 across origin content; build a fixture character; take its calculated scores as "the sheet"; solve; preview; assert equality or a note).

Round trip (`tests/AppService.Tests/DdbRoundTripTests.cs`):
- `An_SRD_5_1_character_written_as_a_sheet_imports_back_with_equal_classes_pins_choices_spells_equipment_and_base_scores_and_zero_differences`
- `An_SRD_5_2_1_character_written_as_a_sheet_imports_back_the_same_way`
- `The_round_trip_of_a_multiclass_caster_keeps_each_spell_on_its_caster`

Tokens and privacy:
- `A_preview_writes_nothing` (database bytes and row counts before and after; reuse `RestoreDrill.Count` on a copy or the drill's digest helper)
- `A_ninth_read_drops_the_oldest_token`
- `A_token_expires_after_thirty_minutes_by_the_apps_clock` (a `FakeTimeProvider`)
- `Discard_and_Dispose_drop_every_token`
- `ReadData_writes_the_temporary_file_under_the_data_folder_and_deletes_it_even_when_the_reader_throws`
- `Opening_the_app_deletes_leftover_ddb_temporary_files`
- `ReadData_refuses_more_than_twenty_megabytes_before_writing_anything`
- `No_error_message_and_no_diagnostic_from_any_ddb_command_quotes_a_field_value` (sentinel values again, through the dispatcher)
- `An_unmapped_field_never_reaches_the_token` (the fake reader includes a `playerName`-like field; serialize the session's sheet; assert absence)

Dispatcher (`tests/AppService.Tests/PersistenceAndDispatchTests.cs`, extend): `ddb_read_answers_unsupported_without_a_host_and_reads_through_the_hosts_Open_dialog_with_one` (the existing fake host in `HostServicesTests`).

**Doc updates.** Spec: the commands table is the contract; adjust if names changed. LIVING_SPECS change history ("S3 built; writes nothing"). No CHANGELOG yet.

**Done.** Gate green. Both round trips are zero-difference. `ddb.preview` provably writes nothing.

---

## S4 · Apply and UI, gap-note schema v2

**Files (service)**

- Modify `src/AppService/GapNotes.cs`: `GapTargetKind { Feature, Field, Import }`; `GapNote.CurrentSchemaVersion = 2`; `IJsonOnDeserialized` upcast (v1 → 2); `Validate` third branch (`Import`: `Label` 1–200, every id null); `AddGapNote` refuses `Import` with `gap.target-invalid`; `internal void AddImportGapNotes(Guid characterId, IReadOnlyList<string> labels)` used only by apply, inside the apply transaction, respecting `MaxNotesPerCharacter` (the 501st and later are counted in the report, not stored).
- Create `docs/schemas/gap-note.v2.schema.json`; modify `docs/schemas/README.md` (row: "v2 adds target kind `import` (D16b): an unmatched item of a sheet import, with only a label. Current").
- Modify `src/AppService/CharacterImport/DdbImport.cs`: `public sealed record DdbApplyRequest(Guid Token, string RulesFamily, Guid? CampaignId, IReadOnlyList<Resolution> Resolutions, IReadOnlyList<NumberChoice> NumberChoices, bool IncludePlayState, bool Confirm = false)`; `public DdbApplyResult ApplyDdbImport(DdbApplyRequest request)` (`confirm` check → `ddb.confirmation-required`; `ImportSessions.Take` → `ddb.token-invalid`; rebuild through `Proposal.Build`; `_store.InTransaction(() => { SaveCharacter path; AddImportGapNotes; field notes for NumberAction.Note })`; `DdbApplyResult(Guid CharacterId, int Overrides, int GapNotes, DdbReport Report)`). The character is saved by the same `SaveWithPlay` code as `character.save` (make the private method reachable, or call `SaveCharacter` then the notes inside one outer transaction; `SqliteStore.InTransaction` must support the nesting it already uses for `PackageService`, verify at `SqliteStore.cs`).
- Modify `src/AppService/CommandDispatcher.cs`: `ddb.apply`.

**Files (UI)**

- Modify `src/Ui/src/api/types.ts`: `GapTargetKind = 'feature' | 'field' | 'import'`; the `Ddb*` types mirroring S3's records (`DdbReadResult`, `DdbPreview`, `MatchRow`, `MatchCandidate`, `Resolution`, `NumberRow`, `NumberChoice`, `AbilityPlan`, `DdbReport`, `DdbApplyResult`).
- Modify `src/Ui/src/api/client.ts`: `ddbRead()`, `ddbReadData(fileName, base64)` (both `{ timeoutMs: null }`), `ddbPreview(request)`, `ddbApply(request)`, `ddbDiscard(token)`.
- Create `src/Ui/src/components/ddb/DdbImportPanel.tsx` (state machine over steps 1–5, `useEffect` cleanup sends `ddbDiscard` on unmount; focus to the step heading on every step change), `DdbChooseStep.tsx` (dialog or file input fallback on `unsupported`, as `SourcesPanel.tsx:213-225`), `DdbFamilyStep.tsx`, `DdbMatchesStep.tsx` (grouped `<table>`s with row headers, filters, "Leave out", Open choices list), `DdbNumbersStep.tsx` (`<table>`, differences first, three radios per difference), `DdbSummaryStep.tsx` (play-state checkbox, "not brought over" list, same-name notice, "Create character").
- Modify `src/Ui/src/App.tsx:96-112`: the "Import from D&D Beyond PDF…" button, a `screen.kind === 'ddb-import'` branch, and `title` help text on "Import package…".
- Modify `src/Ui/src/components/GapNotesPanel.tsx` only if its list assumes two kinds (it renders `label`, so expected: no change; verify).

**Tests first**

`tests/AppService.Tests/GapNoteTests.cs` (extend):
- `A_v1_note_reads_as_v2_unchanged`
- `An_import_target_needs_only_a_label_and_is_refused_from_gap_add`
- `An_import_note_lists_prints_resolves_and_deletes_like_the_others`
- `A_backup_with_an_import_note_round_trips_and_a_share_still_has_none`

`tests/AppService.Tests/SchemaTests.cs`: no new test; the existing `Every_entry_and_the_manifest_of_an_exported_package_match_their_schemas` must pass with a v2 note in the exported backup (add an import note to its setup at line 92-93).

`tests/AppService.Tests/DdbImportTests.cs` (extend):
- `Apply_without_confirm_is_refused_and_writes_nothing`
- `Apply_with_an_unknown_used_or_expired_token_is_refused_with_ddb_token_invalid`
- `Apply_saves_the_character_its_overrides_and_its_gap_notes_in_one_transaction` (inject a store failure on the note write; assert no character)
- `Apply_creates_a_new_id_every_time_and_never_touches_an_existing_character`
- `Apply_with_play_state_ticked_keeps_the_sheets_hit_points_and_spent_slots_and_without_it_the_character_is_rested`
- `A_kept_sheet_number_is_a_FieldOverride_with_the_import_reason_and_the_computed_value_beneath_it`
- `A_noted_difference_becomes_a_field_gap_note_and_each_unmatched_item_an_import_gap_note`
- `Unmatched_items_past_the_note_limit_are_counted_not_stored`
- `Apply_refuses_unreadable_classes_with_the_characters_own_level_diagnostics`

`src/Ui/src/components/ddb/DdbImportPanel.test.tsx` (Vitest + Testing Library, mocking `client`):
- `moves focus to the step heading on every step and Cancel discards the token`
- `shows Needs a choice rows first under the filter and requires a pick before Create`
- `falls back to a file input when the host has no dialog`
- `every table has a caption and row headers` (axe-style assertions as the existing component tests do; check `src/Ui/src/components/*.test.tsx` for the pattern)

`src/Ui/e2e/flow.e2e.tsx` (extend): `it('imports a D&D Beyond sheet, resolves a choice, keeps one sheet number as an override and creates the character', …)` reading `tests/RulesFixtures/pdf/fixture-ddb-sheet.pdf` through `ddb.readData` (the DevHost has no dialog), plus `it('cancelling the D&D Beyond import at each step leaves the character list unchanged', …)`.

**Doc updates.** Spec: status to "built, awaiting owner checks"; `docs/features/gap-notes.md` "A note" gains the `import` kind and the refusal in `gap.add`; `docs/schemas/README.md`; `docs/features/accessibility-checklist.md` gains an item for the step view; `docs/CHANGELOG.md` "Unreleased / Added"; LIVING_SPECS change history (built; gap-note v2; no DB or package change). The companion edits below are listed for the owner, not made here.

**Done.** Gate green including the e2e flow and the smoke. The owner records the manual accessibility pass (keyboard, Narrator) as an open check in the spec.

---

## S5 · Unmatched content as drafts (optional, D16c, after S6)

Not planned in detail until the owner revisits D16c. Shape, if approved: a new user-made source per import ("Imported from a D&D Beyond sheet, <date>"), `importDerived: true`, `origin: local`, with one reference-only draft per unmatched feature, spell or item holding the sheet's text; the existing outbound guards (M6 slice 1) keep it out of every share. Tests: `ImportQuarantineTests`-style "the draft is inactive and never redistributable", and `SourcePackTests` "an import-derived source is refused". Doc: `features/package-format.md` rule 11 reference.

---

## S6 · Real-sheet acceptance (local only)

**Files**

- Create `tests/AppService.Tests/DdbImportAcceptanceTests.cs` (the `StardustGuardianAcceptanceTests` pattern): skips without `tests/RulesFixtures/local/ddb-import/sheet.pdf`; optional `expectations.json` `{ "layout": "...", "family": "srd-5.1", "classes": [{ "level": 5 }, ...], "counts": { "matched": n, "notFound": n, "noPlace": n, "differences": n } }`; writes `report.md` with counts and row positions only.
- The test runs through the real `WorkerFormReader` (the built worker exe is next to the test output, as `WorkerProcessTests` finds it).

**Tests first**

- `The_owners_sheet_is_recognised_and_parses_with_no_unreadable_required_field`
- `The_owners_sheet_imports_with_zero_differences_after_the_proposed_overrides_and_the_counts_match_expectations`
- `The_report_names_no_value` (every line of `report.md` is a count, a row id or a code)

**Doc updates.** The counts go into the spec ("Evidence (S6, YYYY-MM-DD)"); `docs/features/m3-acceptance.md` gets a line that the character entered through the importer; LIVING_SPECS change history.

**Done.** The owner's character is imported and playable; the counts are in the spec; nothing under `local/` is tracked.

---

## Companion document edits (listed, not made; the owner approves separately)

| Document | Edit |
| --- | --- |
| `docs/SPEC.md` "Import and authoring" | **I-08** "A user can import a character from a D&D Beyond PDF character sheet (form fields only) into a **new** character after reviewing every match and number. Nothing of the PDF is kept; unmatched items are recorded as gap notes. `features/ddb-pdf-import.md`." |
| `docs/decisions/ADR-013-third-party-character-sheet-import.md` | Status proposed; Date 2026-10-02; Context (the owner's character lives on D&D Beyond; T2 needs it in TomeStack; Q-02, Q-03, ADR-006, ADR-009); Decision (form fields read once in the worker from a dialog path or a temporary copy, never attached; data-driven layout maps; name matches are proposals the user confirms; choices applied through the builder's check; the base score absorbs what is not modelled; always a new character; gap notes for what is left out); Consequences (the worker reads its first file outside the attachments folder; an unofficial format that can change; no package or database change; gap-note v2); Alternatives considered (the D&D Beyond JSON endpoint, page-text parsing and OCR, reusing attachments and import jobs); Evidence (S1–S4 tests, the S3 round trips, S6's counts); Supersedes none, extends ADR-009 |
| `docs/LIVING_SPECS.md` "Pending product decisions" | Row **D16**: the five questions and the owner's answers of 2026-10-02 as in the spec's table; "Decide by": done |
| `docs/LIVING_SPECS.md` "Change history" | "2026-10-02 · owner decisions (D16): the D&D Beyond sheet importer goes before T2 as an exception (like D15); gap target kind `import` (gap-note schema v2) approved; unmatched content as drafts deferred; play state behind a checkbox; currency and notes left out. Spec: `features/ddb-pdf-import.md`; plan: `ddb-pdf-import-plan.md`. Nothing built." |
| `docs/ROADMAP.md` "T2" | One line under T2: "Exception (D16a, 2026-10-02): the D&D Beyond sheet importer (`features/ddb-pdf-import.md`) is built before the gap report because it is how the T2 character enters TomeStack; its unmatched list feeds the report. It does not reopen the hold for anything else." |
| `docs/CHANGELOG.md` | Nothing until S4 ships |

## Risk register

| # | Risk | Likelihood | Impact | Mitigation | Retired in |
| --- | --- | --- | --- | --- | --- |
| R1 | The export is not an AcroForm (flattened, XFA only) or is encrypted | medium | high: v1 cannot read it at all | S0 finds out before any parser work; the refusal messages tell the user to export again; page-text parsing stays a later option | S0 |
| R2 | Field names are nested, numbered or change between exports | high | medium: the maps are wrong | Full names from the parent chain; maps are versioned data with `required` sets; unknown layouts are refused, never guessed; S6 re-runs on a fresh export | S0, S2 |
| R3 | Class, subclass, prepared and equipped marks are encoded in a way the parser cannot read (one free-text field) | medium | medium: those become Unreadable rows | The map's split rules; Unreadable is a row, not an error; the user fixes it in the builder | S0, S2 |
| R4 | A real sheet's values appear in a log, an error or test output | low | high (privacy, public repo) | Total parser; every code path tested with sentinel values; `report.md` counts only; stderr discarded; the `internal` log never receives parser exceptions | S2, S3, S6 |
| R5 | The temporary PDF of `ddb.readData` outlives the command | low | medium | `finally` delete, startup sweep, a test for each | S3 |
| R6 | The back-solve gives a wrong base that matches the sheet today and drifts later (ASI answered in the builder) | medium | low: one score off by two, visible in the trace | The ambiguity and the "answer later raises it" notice are shown; the trace explains every step | S3 |
| R7 | `WithChoice` refuses a selection the matcher proposed (order, `option-already-chosen`, level gate) | medium | medium: an unexpected `Choose` or NoPlace row | Choices applied in a fixed order with recalculation; every refusal becomes a row note, never an error | S3 |
| R8 | A character above level 3 differs on hit points and the user keeps the sheet's number, hiding a real rule gap | high | low | Per-field overrides only, "Note it" beside each; the report counts overrides so S6 shows how many were needed | S4, S6 |
| R9 | The `WorkerSession` refactor breaks extraction | low | high (M4 imports) | `WorkerProcessTests` and `ImportJobTests` unchanged and green; the smoke's worker import | S1 |
| R10 | Gap-note v2 breaks older builds reading a backup | certain for a backup with an import note | low: refused (it reports `package.invalid-json`, not `package.schema-unsupported`: the entry is read before its version) | Documented; no silent read; `SchemaTests` covers the v2 file. **Review, 2026-10-03:** the same notes in the live database would break an older build's note reads and full backups, so S4 adds a forward-only no-op database migration (10) that makes an older build refuse the folder outright | S4 |
| R11 | The step UI fails WCAG 2.2 AA (D05) | medium | medium | Real tables, labelled regions, focus management tested in Vitest; a manual Narrator pass is an owner check | S4 (owner) |
| R12 | Most of the owner's content is unmatched and the import feels broken | high | low if expected | The counts are shown up front; the unmatched list is the T2 evidence; D16c is revisited after S6 | S6 |
| R13 | Serialized commands: a slow worker read freezes other UI calls for up to 30 s | low | low | Same as `attachPdf` today; the read has its own timeout; the UI shows a busy state on step 1 | S4 |
