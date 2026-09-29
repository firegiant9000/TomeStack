# ADR-010: Custom base classes and arbitrary progression (content schema v9)

Status: **accepted** (owner, 2026-09-28, as proposed: dice that scale by level stay out of v9). **Slice 1a (the rules core) is implemented and fixture-verified (2026-09-29)**, see "Evidence". **The v9 bump is approved by the owner (2026-09-29)**, including slice 1b's addition (a choice with no declared options). Merging still waits for the owner.
Date: 2026-09-28

## Context

ROADMAP M5's exit gate: "a nonstandard class levels and multiclasses without code edits". SPEC I-04 (guided authoring, no scripts) and Q-02 (bounded formulas, no code execution) still hold. The invariants: only published revisions calculate; published revisions are insert-only, and a bundled revision already on any pushed branch never changes bytes; `srd-5.1` and `srd-5.2.1` differ only through `RulesFamilyPolicy` fields; `RulesCore` stays free of persistence, UI and Windows.

**What a class already is (checked against the code 2026-09-28).** A class is content (`ContentKind.Class`), and nothing in `RulesCore` names an SRD class. Most of a nonstandard class can therefore be written today, in content schemas v3 to v8:

| A class needs | Already expressed by |
| --- | --- |
| Hit die | `hitDie` (v3): d6, d8, d10 or d12 |
| Per-level features | `grant` with `level` (v3); `CLASS_LEVEL` in formulas |
| Per-level choices, the subclass level | `choice` with `level` (v3); homebrew subclasses join through `extendsChoice` (v4) |
| Saving throws, skills, weapons, armor | proficiency grants: `save.*`, `skill.*`, `weapon.*` (v5), `armor.*` (v8). A skill choice is a `choice` of option features that each grant one skill, as in the SRD packs |
| Starting-class and multiclass proficiency subsets | `onlyAs` on `grant` and `choice` (v5) |
| Multiclass prerequisites | `restriction` with `multiclass` and `group` (v5) |
| Resources and their recovery | `resource` (formula maximum) and `recovery` (v2) |
| Spellcasting of its own | `spellcasting` (v5): ability, list key, preparation, a 20-row slot table, cantrips and spells tables or a formula; Pact Magic as its own pool |
| Attacks, critical range, armor rules | v8 fields (M2.2) |

**What it cannot express:**

1. **Multiclass caster shares other than full, half or a third.** `spellcasting.multiclassCaster` is a closed enum (v7, `Effects.cs`). The family policy rounds half and third (`HalfCasterLevels`, `ThirdCasterLevels`). A 2/3 caster, a 1/4 caster, or a caster whose share changes by level would each need a code edit.
2. **Named per-level values.** A column such as "Ink: 2, 2, 3, 3, 4, …" has to be written as floor arithmetic in every formula that uses it (the SRD Channel Divinity is `1 + floor((CLASS_LEVEL+12)/18) + floor(CLASS_LEVEL/18)`). An arbitrary column often needs more than the 200-character, 64-token bound (ADR-003). The sheet cannot show it as a column of the class table either.
3. **Authoring.** The studio authors only subclasses, features, feats and items, and has no editor for `hitDie`, `choice`, `restriction`, `onlyAs` or `spellcasting`. This is UI work (slice 1b) and needs no schema change.

**Rules constants that stay:** the proficiency bonus is by total character level (both SRDs), a character has at most 20 levels, and hit points are the die maximum at level 1 and then the fixed value. A class can still add to the proficiency bonus or hit points with a `modifier` (existing).

## Decision

### Content schema v9 adds exactly three things

**1. The `scale` effect (a new type, typed only in a v9 revision).**

```json
{ "type": "scale", "id": "…", "scaleId": "ink", "label": "Ink", "values": [2,2,3,3,4,4,4,5,5,5,6,6,6,7,7,7,8,8,8,9] }
```

- `values` has 20 integers, from 0 to 10,000 (the ADR-003 literal bound); row 0 is class level 1. `scaleId` matches `[a-z][A-Za-z0-9]{0,31}` and never follows the label, like `resourceId`.
- It is allowed on class and subclass content only (`validate.scale-kind`). A `scaleId` is unique within a class and its subclasses (`validate.scale-duplicate`; at calculation, a subclass that repeats a class's id gets `scale.duplicate`, and the class's column wins).
- The sheet lists the scales of each class with their current value (`sheet.scales`), which is the class-table column.

**2. The formula identifier `SCALE.<scaleId>`.**

```text
IDENT := PB | LEVEL | CLASS_LEVEL | (STR|DEX|CON|INT|WIS|CHA) "." (MOD|SCORE) | "SCALE" "." NAME     (v9)
NAME  := [a-z][A-Za-z0-9]{0,31}
```

- The value is the column's entry at the level of the class that the content belongs to. It is read from that class or its chosen subclass, following the same rule as `CLASS_LEVEL` (`features/levels-and-classes.md`). Outside a class, or for an id that neither the class nor its subclass defines, the effect is disabled with `formula.value-unavailable` (as built: the evaluator's one code for an identifier without a value), and the rest of the sheet still calculates.
- A scale is a table of literals, not a formula, so it reads no field. It adds no edge to the field graph and cannot create a cycle or recursion (Q-02). The formula bounds of ADR-003 are unchanged.
- The trace records the read as an input of the step that uses it: `SCALE.ink = 4` for a level-5 Test Chronicler (`values[4]` in the example above), next to the effect's own origin (revision, source, page). The sheet lists each column with its class, the revision that defines it and the value (`sheet.scales`).
- Any formula that uses `SCALE` is v9 content. Six fields hold formulas: a modifier `value`, a resource `maximum`, a recovery `amount`, a roll `cost`, a roll `bonus` (v8), and `spellsFormula`. Dice expressions do not accept `SCALE` (see "Not in v9").
- **`SCALE` works only in v9 revisions (review fix).** `Formula.TryParse` does not look at the schema version, so the gate belongs in the resolver, like the calculator's `IgnoresV8`. In a revision below v9, `SCALE.<id>` resolves as `formula.unknown-identifier`, which is exactly what builds before v9 do. Package import does not block on validation errors for v1 and v2 revisions. Without this gate, a stored v2 feature whose formula names `SCALE.ink` would calculate on a v9 build and fail on every older build: one stored revision with two meanings.
- **The id rules are best-effort at publish; calculation is the backstop (review fix).**
  - `validate.scale-duplicate` checks the revision against the catalog: its class, and the subclasses that extend the class's choices. A class revision published later can still collide with an existing homebrew subclass, so calculation reports `scale.duplicate` and the class's column wins.
  - A `SCALE.<id>` that neither the revision nor its class defines is a publish warning, `validate.scale-unknown`. As built, it is checked for a class and for a subclass that names its class (`extendsChoice`). A feature's class, or that of a subclass a class lists as a declared option, is known only when a character has it, so those are left to calculation and to the debugger (M5 slice 2), which also lists collisions across revisions.
  - A subclass's column is indexed by the **class** level. Rows below the level where the subclass is chosen are never read, because the subclass does not apply there.

**3. `spellcasting.multiclassCasterTable` (a new field on an existing type: nullable, absent by default).**

- It holds 20 integers: the caster levels that this class contributes to the Multiclass Spellcaster table at each of its levels.
- Validation (`validate.spellcasting-multiclass-table`):
  - each entry is 0–20, no greater than the class level, and never lower than the entry before it;
  - it cannot be combined with `multiclassCaster` (`validate.spellcasting-multiclass-both`), nor with Pact Magic (the existing `validate.spellcasting-multiclass-pact` is extended).
- **How casters combine** (the change to `Calculation.cs` slot combining):
  1. **The combining check changes (review fix).** Today slots combine only when every slot caster has `MulticlassCaster` set. The new check accepts a caster that has either the enum or a table. A table caster has no enum value, because both cannot be set. Under today's check it would fall back to the manual path, and under the enum switch's default branch it would count as a full caster. So the table is its own branch, evaluated before the enum switch.
  2. The character's caster level is the sum of every slot caster's contribution, capped at 20.
  3. An enum caster counts through the family policy, as today. A table caster counts its table's entry, exactly, with no family rounding: the content states the numbers.
  4. The slots then come from `RulesFamilyPolicy.MulticlassSpellSlots`, as today.
  5. A single caster still uses its own slot table. Pact Magic is still never combined.
- **The table is read only in v9 revisions (review fix).** `spellcasting` has been typed since v5, and an unknown property on it goes to extension data. A v5–v8 revision that carries `multiclassCasterTable` (edited by hand, or written by a newer build and relabelled) keeps it as **extension data**, in document order (`SpellcastingEffect.FromUnknown`). It is never typed, so it serializes byte for byte. The calculator therefore never sees it, and does what a v8 build does: no diagnostic, no combining. As a second guard, the calculator also ignores a table on any revision below v9 that was built in code.
- The trace step reads "Test Chronicler at class level 5 counts 3 caster level(s) (its multiclass table)".
- A homebrew author who wants 2014 and 2024 rounding to differ publishes one revision per family. That is content, which keeps the rule "differences are content or policy fields, never names". The **Test Chronicler** fixture is a 2/3 caster by table: `[0,1,2,2,3,4,4,5,6,6,7,8,8,9,10,10,11,12,12,13]`.

### What needs no schema change (slice 1b, studio only)

The studio gains a **class** kind:

- the hit die;
- a level table, with features (`grant.level`) and choices (`choice.level`) per level, including the subclass choice and its level;
- a skill-choice helper that writes the option features and the `choice`;
- saving-throw and other proficiency grants, with a starting-class or multiclass `onlyAs`;
- multiclass prerequisites (`restriction`, `multiclass`, `group` for "A or B");
- scales, and resources whose maximum uses one;
- a spellcasting editor: ability, list key, preparation, the 20-row slot table, and a multiclass share of none, full, half, third or a table.

Everything it writes is declarative and validated by `content.validate` and `content.publish` (ADR-004). A published homebrew class appears in the builder like any class.

### SRD classes stay byte-identical

No SRD revision is edited or added for M5. The SRD casters keep `multiclassCaster` (v7). Their Multiclass Spellcaster behavior, and the half and third rounding by family, are unchanged. Checks:

- `SrdPackTests` keeps its hashes;
- `git diff origin/main --stat -- src/AppService/Content` is empty for every M5 PR;
- `SrdCasterTests` and `SrdFighterTests` pass unchanged.

The Test Chronicler is an original fixture under `tests/RulesFixtures/` (never `local/`), not bundled content.

### Migration (forward-only) and minimum-version publishing

- **Stored revisions:**
  - No stored revision re-serializes, so there is no database migration and no hash changes.
  - `scale` is typed only in a v9 revision (the `VersionedEffects` table in `Effects.cs`). In a v2–v8 revision, an effect that says `"type": "scale"` stays an `UnknownEffect`: reference-only and written back byte for byte, exactly as for `armor` in v4 (ADR-003).
  - `multiclassCasterTable` is nullable and absent by default (`WhenWritingNull`), so no existing spellcasting revision changes.
  - In a v2–v8 revision, `SCALE.x` in a formula is an unknown identifier: a formula error that disables only that effect, as it would have before.
- **Characters:** scales are derived, so there is no play state and the character schema stays v7.
- **Packages:** the formats are unchanged (character packages v5, library backups v6). An entry may now be content v9.
- **Minimum-version publishing:** `ValidationReport.RequiredSchemaVersion` (M2.2) returns 9 for a revision with any of the following, and `content.publish` keeps writing `min(draft version, required)`, so a homebrew class that uses none of them is still published as v3–v8 and stays readable by older builds. Bundled packs declare their version by hand, by the same rule (`docs/schemas/README.md`).
  - a `scale` effect;
  - a `multiclassCasterTable`;
  - `SCALE.` in any of the six formula fields it holds.

  Today `RequiredSchemaVersion` looks only at effect types and fields, and `CheckFormula` throws away the parsed formula. So every `CheckFormula` call site must collect the formula's identifiers and require v9 when one starts with `SCALE.` (review fix). Otherwise a roll `bonus` of `SCALE.ink` would publish as v8, and a v8 build would silently disable it instead of refusing the revision.
- **Older builds refuse; they never misread.**
  - A build whose `CurrentSchemaVersion` is below 9 isolates a v9 revision in calculation (`content.schema-unsupported`). It refuses it on import (`package.schema-unsupported`) and in validation (`validate.schema-unsupported`).
  - So a v8 build cannot calculate a table caster as a non-combining caster, or a scale-driven resource as a formula error. It shows the class as needing a newer TomeStack.
  - This is why `multiclassCasterTable` needs a version. An older build would read it as an unknown extension property and silently calculate the slots without combining them.
- **New artifacts:** `docs/schemas/content-revision.v9.schema.json`; `validate.requires-v9`; `ContentRevision.CurrentSchemaVersion = 9`; an ADR-003 "Content schema v9" section that points here.

### Not in v9 (kept small and revisable, D13)

- **Dice that scale by level** (a martial-arts-style d4 → d10). A roll's `dice` does not accept `SCALE`. Such features stay assisted with the die in their text until a concrete need appears. Adding it later is another version, because dice parsing changes.
- **Hit dice outside d6–d12**, levels above 20, and a per-class proficiency progression.
- **Spell points instead of slots.** A resource can hold the points, and the spells stay a list. Converting slots to points is reference text.
- **Tools and languages as fields.** They stay reference text, as in the SRD packs.

## Consequences

- A nonstandard class needs no code edit. The engine change is one effect type, one identifier and one field, each with a validator rule and a side-by-side test.
- The Multiclass Spellcaster table stays a policy field. Only the *contribution* becomes data.
- The studio's class editor is the largest piece of slice 1 by effort, and it is plain UI over existing commands.
- A class published as v9 cannot be shared with 0.3.x or M2.2 builds, and the share preview must say so. `package.exportPreview` names the minimum TomeStack version when any entry is above v7 (slice 1b).

## Alternatives considered

| Option | Why not |
| --- | --- |
| A `table(CLASS_LEVEL, v1, …, v20)` formula function | 21 arguments exceed the 2–4 argument bound and often the 200-character bound (ADR-003). Every use repeats the table |
| A fraction (`multiclassCasterShare: { numerator, denominator }`) with family rounding | Each new fraction would need a rounding policy field, and a share that changes by level is not a fraction. A table is exact and uses the existing 20-row pattern |
| More enum values (`twoThirds`, `quarter`) | A code edit per shape, which the exit gate forbids |
| `resource.maximumTable` only | It covers resources but not modifiers, costs or prepared-spell formulas; a named scale covers all of them and shows on the sheet |
| Scripting (Roslyn, Jint, Lua) | Rejected by SPEC Q-02 |
| Editing the SRD classes to use the new table | It would change bundled revisions or add new ones for no behavior change. The SRD classes keep the enum |

## Evidence

**Slice 1a (fixture-verified, 2026-09-29).** The fixture is `tests/RulesFixtures/fixture-pack-m5-chronicler.json`: the original Test Chronicler class, a granted feature that reads the class's column, and a subclass with a column of its own.

- **`tests/RulesCore.Tests/CustomClassTests.cs`** (45 cases), each side by side in `srd-5.1` and `srd-5.2.1` where it calculates:
  - `The_Test_Chronicler_levels_1_to_20_from_its_columns_side_by_side` at levels 1, 3, 5, 11, 17 and 20. It checks hit points, both columns, the subclass column (absent before level 3), the Ink resource and both recoveries, the Inkblot roll's bonus and cost, a skill modifier, the granted feature's initiative, the prepared-spell formula and the class's own slot table.
  - Multiclass tests with the fixture full caster (caster level 3 + 3, identical in both families), a half caster (the enum caster's family rounding is the only difference), a third caster, and a non-caster (the Chronicler keeps its own table). Also the multiclass Intelligence prerequisite and the starting-class saves.
  - Version gates:
    - `SCALE_in_a_v8_revision_is_an_unknown_identifier_as_in_builds_before_v9`;
    - `A_scale_effect_in_a_revision_older_than_v9_stays_unknown_and_byte_for_byte`;
    - `A_v8_spellcasting_revision_with_the_table_as_extension_data_is_unchanged_and_not_combined` (a later extension key keeps its place too);
    - `A_spellcasting_revision_without_the_table_serializes_unchanged`;
    - `A_v9_revision_is_refused_by_validation_and_calculation_that_support_only_v8`. It shows the refusal one version up (this build and v10), because a test cannot run an older build; the version gate is the same code.
  - Minimum versions:
    - `RequiredSchemaVersion_is_9_when_any_formula_field_reads_a_scale`, with one case for each of the six fields;
    - `RequiredSchemaVersion_stays_below_9_for_a_class_that_uses_nothing_from_v9`.
  - Bounds and validation:
    - `A_scale_reads_no_field_and_adds_no_dependency_edge`;
    - `Scale_identifiers_follow_the_id_pattern`;
    - `Validation_refuses_bad_scales_and_bad_caster_tables`;
    - `A_formula_reading_a_scale_its_class_does_not_define_is_a_warning`;
    - `A_subclass_that_repeats_its_class_scale_id_loses_to_the_class_at_calculation`;
    - `SCALE_outside_a_class_is_unavailable_and_the_rest_calculates`.
- **`tests/AppService.Tests/CustomClassTests.cs`:** the Chronicler with the bundled SRD Wizard (caster level 6 in both families), an SRD Paladin (2014 rounds down, 2024 rounds up) and the SRD Fighter (its own table). `content.publish` writes a Chronicler draft as v9, and a class without v9 features at v5.
- **Schema:** `SchemaTests` validates the fixture against the new `docs/schemas/content-revision.v9.schema.json`.
- **Unchanged:** `SrdPackTests` (the bundled packs' hashes), `SrdCasterTests` and `SrdFighterTests`, and `git diff origin/main --stat -- src/AppService/Content` is empty.

**Slice 1a review fixes (dual-review, 2026-09-29).** None of the findings was refuted; each is fixed and has a test.

- **The table key below v9:** any spelling of the key (the serializer matches names case-insensitively), and any value (`null`, a string, a list of decimals), is taken out before typing. It goes back into extension data in document order. So the spellcasting still types, and the revision writes back byte for byte (`Below_v9_any_spelling_and_value_of_the_table_key_stays_extension_data_byte_for_byte`).
- **`validate.requires-v9` for content read from JSON:** it now also fires for a `scale` that stays unknown below v9 and for a table key held as extension data (`A_scale_in_a_v8_draft_read_from_json_is_named_as_needing_v9`). Before this, such a draft would have been published as v8 with the feature silently ignored.
- **A subclass's scale ids** are checked against its class's newest published revision, plus unsaved revisions validated with it. A clash with an older published revision is a warning (`validate.scale-duplicate-older`). A draft never blocks, and a subclass that extends a feature's choice gets no class checks (`A_subclass_is_checked_against_its_classs_newest_published_revision_only`, `A_subclass_that_extends_a_features_choice_gets_no_class_scale_checks`).
- **Calculation isolates out-of-bound scale values** that bypassed validation, with `scale.invalid` (`A_stored_scale_with_values_out_of_bounds_is_isolated_at_calculation`).

**Second review (dual-review of the M5 stack, 2026-09-29).** Confirmed by both reviewers, fixed:

- **A malformed scale in a v9 revision** (no label, values that are not whole numbers) stayed an unknown effect and published with no error, so its column silently did not exist. It is now `validate.scale-incomplete` (`A_v9_scale_that_does_not_match_the_shape_is_an_error_not_a_silently_missing_column`).
- **Package import and restore no longer refuse what publishing allowed.** When a package's published revision is re-checked, `validate.requires-v9` (an inert `scale` or table key that an earlier build published at v3 to v8) and `validate.scale-duplicate` (a clash the order of publishing allowed; the calculation reports `scale.duplicate` and the class's column wins) are warnings. Both still block `content.publish` (`A_published_v8_revision_with_an_inert_scale_effect_still_imports_with_a_warning`).
- **"Newest" during an import** now means what it means afterwards: the import catalog lists this machine's revisions first, then the package's, the order they have once added.
- **Release constraint:** v9 includes slice 1b's choice with no declared options, so slices 1a and 1b ship in the same release (ROADMAP).
- **Not changed (single-source, low):** a revision with no `schemaVersion` is read at the current version, as before v9. The calculation-side v8 gate is asserted for the resource maximum only; the other five formula sites use the same `AllowsScales` call.

**Slice 1b (planned):** `HomebrewStudioTests` authors and publishes the class in the studio and multiclasses it with SRD classes, with a package round trip. The e2e flow "authors a class in the studio, levels it 1–20 and multiclasses it with an SRD class".

Supersedes: none. Extends ADR-003 (effect union, grammar) and the M2.2 minimum-version rule (`docs/schemas/README.md`).
