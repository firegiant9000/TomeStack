# ADR-003: Declarative effect AST and bounded formulas

Status: accepted. The effect model and migration are implemented in `RulesCore/Effects.cs`, and the formula grammar in `RulesCore/Formulas.cs` (`tests/RulesCore.Tests/FormulaTests.cs`: explicit hostile cases, 20,000 fuzzed strings, 2,000 generated formulas, and feature isolation). Property tests (roadmap T3, [testing/properties.md](../testing/properties.md)) add the parser round trip, evaluation totality, versioned-type gating and a golden hash per content schema version.
Date: 2026-09-25

## Context

ARCHITECTURE "Rules execution" steps 3–4 and SPEC I-04, I-05, C-03 and Q-02 ask for supported mechanics to be declarative data: grant, choice, bonus/set/replace, resource, restriction, recovery and roll. Stacking and timing must be explicit, formulas typed and bounded (`PB + CON.MOD`, `floor(CLASS_LEVEL / 2)`), and there must be no `eval`, scripting or recursion from user content. M0 used one string-typed `Effect { type, ability?, amount? }`.

## Decision

### Effect union (`src/RulesCore/Effects.cs`, schema `docs/schemas/content-revision.v2.schema.json`)

Every effect has `type` (the discriminator), `id`, `automation` (`automatic` / `assisted` / `reference`), `timing`, optional `text`, and round-tripped unknown fields.

| `type` | Fields | Used by |
| --- | --- | --- |
| `modifier` | `operation` (`bonus` / `set` / `replace`), `target` (field id), `value` (formula), `stacking` (`stack` / `highestInGroup`), `stackGroup` | calculator |
| `grant` | `grant` (`proficiency` / `expertise` / `content`), `target` (field id) or `content` (a pin), optional `level` (v3) | calculator (item 11–12; levels M1 item 5) |
| `hitDie` (v3) | `die` (6 / 8 / 10 / 12) | hit points (M1 item 5) |
| `armor` (M2 item 4; content v4 only, see "Content schema v4") | `category` (`light` / `medium` / `heavy` / `shield`), `armorClass`, `dexterityCap?` | Armor Class of equipped items: body armor is a `replace` of the base, and while it is worn other Armor Class replacements do not apply; a shield is a `bonus` (`features/equipment.md`) |
| `resource` | `resourceId`, `label`, `maximum` (formula) | sheet maximum with trace, and `character.play` spending (M2 item 2, `features/sheet-play.md`) |
| `choice` | `choiceId`, `count`, `options[]` (pins), optional `level` (v3) | calculator and `character.choose` (M1 item 4, `features/choices.md`); builder UI (M2) |
| `restriction` | `field`, `minimum` | prerequisite check in the calculator (M1 item 3, `features/validation-and-restrictions.md`) |
| `recovery` | `resourceId`, `on` (`shortRest` / `longRest`), `amount` (formula or `all`) | long rest preview and confirmed rest (M2 item 3, `features/rests.md`); short rest after M2 |
| `roll` | `rollId`, `label`, `dice`, optional `resourceId` | dice engine (item 13) |
| `spellcasting` (content v5 only) | `ability`, `preparation`, `spellList`, `slotKind`, `slots` (20 rows), optional `cantrips`, `spellsTable` or `spellsFormula`, and `multiclassCaster` (v7) | spell fields and `sheet.spellcasting` (`features/spellcasting.md`); combined multiclass slots (v7) |
| `toggle` (content v6 only) | `toggleId`, `label`, optional `resourceId` | play-state switch; bound `whileActive` modifiers apply while it is on (`features/m3-effects.md`) |
| `scale` (content v9 only) | `scaleId`, `label`, `values` (20 integers) | a per-level column of a class or subclass, read as `SCALE.<scaleId>` ([ADR-010](ADR-010-custom-classes-and-progression.md)) |
| `weapon` (content v5 only) | `category`, `attack`, `damage`, `damageType`, `properties`, `versatile`, `range`, `weaponKey`, `mastery` | attacks of equipped items (`features/multiclass-and-attacks.md`) |
| `spell` (content v5 only) | `level`, `lists`, `school`, `castingTime`, `range`, `components`, `duration`, `concentration`, `ritual`, `attack`, `save`, `dice` | spells of a caster; never active content |

**Unknown types** deserialize to `UnknownEffect`, which keeps the original JSON and writes it back with the same properties, order and values (whitespace and string escaping are normalized), and is always reference-only (`effect.unsupported`). A *known* type with a malformed body, including wrong value kinds such as a numeric `id`, degrades the same way instead of failing the whole revision. Only an effect that is not a JSON object fails its revision.

Field ids: `initiative`, `proficiencyBonus`, `armorClass`, `hitPoints`, `ability.<abl>.score`, `ability.<abl>.mod`, `save.<abl>`, `skill.<name>` (all 18 skills; see `features/levels-and-classes.md`), and since content v5 `spellAttack`, `spellSaveDc`, `spellSlots.1`–`spellSlots.9` and `pactSlots`; since 2026-10-06 (D24) also `passive.perception`, `passive.insight`, `passive.investigation` and `speed`.

### Stacking and order (per field)

1. **base**: the character's choice or the rules' derivation. The highest `replace` substitutes for it, and the other replacements are traced as not applied.
2. **bonus** in content order. A bonus to an ability score stops at 20 (the SRD rule, since M2 item 4). `stack` bonuses all add. Among `highestInGroup` bonuses with the same `stackGroup`, only the highest applies, and the rest are traced as "does not stack". A bonus that would take the running value outside ±1,000,000 is not applied (`effect.out-of-range`), so many bounded bonuses cannot overflow.
3. The highest **set** (if any) replaces the running value.
4. **Rounding:** any fraction rounds down at the end of a formula (the 5e default).
5. **User override** last (SPEC C-06). The computed value and its trace are kept.

**Cycles:** an effect is disabled (`effect.dependency-cycle`) when its dependency edge lies inside a strongly connected component of the field graph, or reads its own target. An edge that base inputs already imply (for example a Dex modifier effect reading `DEX.SCORE`) is exempt: it adds no reachability, so it cannot close a cycle.

**Automation per field:** a field is `assisted` when it or any field it reads has an effect the calculator did not apply (not `automatic`, not `always`, an invalid formula, a cycle or out of range), because the user may have to account for it by hand. Effects ignored by rules-family policy do not count: the rules say they do not apply.

### Timing

Derived fields use only `always`, plus `whileActive` modifiers bound to a toggle while that toggle is on (content v6; see "Content schema v6"). An unbound `whileActive` effect stays assisted. `onRoll` belongs to the dice engine. `onShortRest` and `onLongRest` belong to rest previews, which never apply themselves (ARCHITECTURE "commands vs calculation").

### Formula grammar (item 10)

```text
formula := sum
sum     := product (("+" | "-") product)*
product := unary (("*" | "/") unary)*
unary   := "-" unary | primary
primary := NUMBER | IDENT | FUNC "(" sum ("," sum)* ")" | "(" sum ")"
FUNC    := floor | ceil | min | max | abs
IDENT   := PB | LEVEL | CLASS_LEVEL | (STR|DEX|CON|INT|WIS|CHA) "." (MOD|SCORE)
         | "SCALE" "." [a-z][A-Za-z0-9]{0,31}      (content v9 revisions only; ADR-010)
NUMBER  := [0-9]+
```

**Bounds (SPEC Q-02):** at most 200 characters, at most 64 tokens, nesting depth ≤ 8, literals ≤ 10,000, and every intermediate result within ±1,000,000. `min`/`max` take 2–4 arguments; `floor`/`ceil`/`abs` take 1. Division by zero, unknown identifiers or functions, and any bound violation are **parse or evaluation errors** with a diagnostic that names the feature and effect. The effect is skipped and the rest of the sheet still calculates (SPEC C-03). There is no `eval`, no user-defined identifiers, and no way for a formula to reference another formula, so user content cannot create recursion. Identifiers map to fields (for example `DEX.MOD` → `ability.dex.mod`), which gives the dependency graph its edges (item 11).

## Migration (schemaVersion 1 → 2)

- **Read:** `abilityScoreIncrease {ability, amount}` becomes `modifier bonus ability.<abl>.score value "<amount>"`. `initiativeBonus {amount}` becomes `modifier bonus initiative`. Unmapped v1 fields become extensions, and other v1 types become `UnknownEffect`. A v1 effect the v1 build could not have written (no string `id`, a missing or non-integer `amount`, a missing or unknown `ability`) also becomes `UnknownEffect`, so the mapping never drops data. This keeps imports of v1 packages lossless, where there is no `legacy_json`. The revision is upcast to `schemaVersion: 2`, and `UpgradedFrom` records 1.
- **Database migration v2** rewrites each upgraded row's `json` and `sha256` in the new representation and keeps the original bytes in `legacy_json`. This is the one sanctioned rewrite of published revisions: a lossless change of representation, not of content. Without it, every M0 data folder would fail to open, because re-seeding the fixtures would hit the insert-only hash check (`UpgradeTests.Schema_v1_data_folder_with_v1_revision_json_migrates_to_typed_effects` first failed with `ImmutableRevisionException`). The pre-upgrade backup (`tomestack.db.v1.bak`) is taken first.
- **Packages:** `formatVersion` 2. v1 packages still import and are upcast. Builds that only know v1 refuse v2 with `package.unsupported-format` instead of misreading typed effects.
- Calculation is proven identical before and after (`EffectModelTests.Calculation_is_identical_before_and_after_the_v1_to_v2_migration`).

## Content schema v3 (M1, 2026-09-26)

- **Adds** `grant.level`, `choice.level`, the `hitDie` effect type, and the `armorClass` and `hitPoints` targets. `CLASS_LEVEL` now resolves: it is the level in the class the content belongs to (`features/levels-and-classes.md`).
- **No upcast from v2.** v2 is a subset of v3, so v2 revisions keep `schemaVersion: 2` and their serialized form, which means their hashes do not change and no database migration is needed. v1 still upcasts to exactly v2. New revisions are written as v3.
- **Why a version and not an extension field:** a v2-only build would read `level` as an unknown extension and apply a level-3 feature at level 1. Refusing v3 (`content.schema-unsupported`, `package.schema-unsupported`) is safer than silently calculating differently. New effect *types*, by contrast, are forward-compatible for older builds (they become `UnknownEffect`), but new *fields* on existing types are not. A new type still needs a version for *newer* builds, because existing revisions may already use that type name as an unknown effect (see `armor` under v4).

## Content schema v4 (M2 item 5, 2026-09-27)

- **Adds** `extendsChoice: { contentId, choiceId }` on a revision: it is also an option of that content's choice (a homebrew subclass for an SRD class; `features/homebrew-studio.md`). The calculator offers published extensions after the declared options, looked up through `IContentCatalog.ChoiceExtensions`.
- **No upcast**, as for v3: v2 and v3 revisions keep their version and serialized form, so their hashes and the bundled SRD packs are unchanged and no database migration is needed. New revisions are written as v4.
- **Why a version:** a v3 build would read `extendsChoice` as an unknown extension. It would then refuse the character's selection of the homebrew subclass (`choice.invalid-option`) instead of saying it needs a newer build. Refusing v4 (`content.schema-unsupported`) is clearer.
- **Also adds the `armor` effect type (M2 item 4; corrected 2026-09-27 by the M2 review).** It first shipped as a new type with no version change, on the assumption that new types are always forward-compatible. They are not in the other direction: a revision stored by 0.2.0 may already carry `"type": "armor"` as an `UnknownEffect`, written byte for byte. Typing it re-serializes it in a different layout, so its hash changes. Re-adding the identical revision then fails (`ImmutableRevisionException`, `package.revision-conflict`), and published content silently starts changing Armor Class. So `armor` is typed only in a v4 revision (`ContentRevision.OnDeserialized`). In a v2 or v3 revision it stays unknown, reference-only and unchanged. Validation refuses armor on a revision below v4 (`validate.requires-v4`). Evidence: `EffectModelTests.Armor_in_a_revision_older_than_v4_stays_unknown_and_byte_for_byte`, `EquipmentTests.A_v3_armor_revision_stored_by_0_2_0_re_adds_unchanged_and_stays_reference_only`.

## Content schema v5 (M2 spellcasting, 2026-09-27)

- **Adds** the `spellcasting` effect (a class's ability, preparation, spell list key, slot kind and 20-row tables, `features/spellcasting.md`) and the `spell` effect (a spell's level, lists, attack or save, and dice). It also adds the calculated fields `spellAttack`, `spellSaveDc`, `spellSlots.1`–`spellSlots.9` and `pactSlots`, which modifiers, restrictions and overrides can target.
- **Typed only in v5 revisions**, like `armor` in v4 (the table below is `VersionedEffects` in `Effects.cs`). In a v2 to v4 revision these type names stay unknown, reference-only and byte for byte. No revision is upcast, so no hash changes and no database migration is needed. New revisions are written as v5. Validation refuses the types below v5 (`validate.requires-v5`). Evidence: `SpellcastingTests.A_spellcasting_effect_in_a_revision_older_than_v5_stays_unknown_and_byte_for_byte`.
- **Why tables in content, not a caster "type":** the SRD progressions differ by family and class (for example, 2024 half casters have slots at level 1). A table per class revision states them exactly. Formula tables would exceed the bounded grammar, and hard-coded progressions would encode differences by name.
- **Also in v5 (M2 item 2, `features/multiclass-and-attacks.md`):**
  - The `weapon` effect (typed only in v5).
  - Weapon proficiency grants (`weapon.simple`, `weapon.martial`, `weapon.<key>`).
  - `onlyAs` on `grant` and `choice` (starting class or later class).
  - `multiclass` and `group` on `restriction`.
  - `activation` on `roll`.

  v5 is still unreleased (0.3.0), so it grows during the M2 exit instead of taking a v6. Each new field on an existing type is **nullable and absent by default**. The serializer writes every non-null value (`WhenWritingNull`), so a non-nullable default such as `false` would have re-serialized every stored revision, changed its hash and broken re-seeding. No existing content uses these property names (checked against the SRD packs and fixtures). Validation refuses them below v5 (`validate.requires-v5`).
- **The spell fields read the primary caster's ability, which is data.** Statically they read every ability modifier, so the evaluation order is right. The automation closure and the trace use only the proficiency bonus and the actual ability (`Closure`'s `actualReads`). A non-caster's spell fields therefore never turn assisted because some unrelated modifier is.

## Content schema v6 (M3 B2, 2026-09-27)

- **Adds:**
  - the `toggle` effect (typed only in v6);
  - `modifier.toggle`: a `whileActive` modifier that applies while its revision's toggle is on;
  - `roll.resourceContent` (a shared resource), `roll.cost` (formula) and `roll.variableCost` (`features/m3-effects.md`).
  
  Toggle state is play state (character schema v7, `play.toggles`).
- **Why v6 and not more v5:** v5 shipped in the 0.2.2 build handed over for the M2 owner checks, and content authored there may already be v5. Extending v5 now would change what those revisions mean. As with v5, every new field on an existing type is nullable and absent by default, so no stored revision re-serializes. Validation refuses them below v6 (`validate.requires-v6`). Evidence: `ToggleAndCostTests.A_toggle_effect_in_a_revision_older_than_v6_stays_unknown_and_byte_for_byte`.
- **Timing:** `whileActive` effects are no longer always assisted. A modifier bound to a toggle is automatic, on or off. Only an unbound `whileActive` modifier stays assisted.

## Content schema v7 (M3 C3, 2026-09-28)

- **Adds** `spellcasting.multiclassCaster`: `full`, `half` or `third`, meaning how the caster's class levels count toward the SRD Multiclass Spellcaster table (D04's M3 part, `features/spellcasting.md`). It is ordinary spell slots only, because Pact Magic is never combined (`validate.spellcasting-multiclass-pact`). The rounding of the fractions and the table itself are rules-family policy (`HalfCasterLevels`, `ThirdCasterLevels`, `MulticlassSpellSlots`; `features/rules-family-policy.md`), so the class states *what* it is and the family states *how* it counts.
- **Why v7 and not more v6:** 0.3.0 (the M2 delivery, which already contains v6) was bumped before this change. A v6 build must not read a v7 caster and silently calculate its slots without combining them. As before, the field is nullable and absent by default, so no stored revision re-serializes (`MulticlassSpellSlotTests.An_older_spellcasting_revision_serializes_without_the_new_field`). Validation refuses it below v7 (`validate.requires-v7`).
- **Without the field** (every revision before v7, and homebrew that leaves it out), a second slot caster keeps the M2 behavior: the first caster's slots, assisted, with `spellcasting.multiclass-slots` and an override as the manual step.
- **SRD content:** new v7 revisions of the seven slot casters' Spellcasting features (with the same content ids), and new class revisions that pin them, in both families. They are insert-only. Characters keep their pins until a reviewed update.

## Content schema v9 (M5 slice 1a, 2026-09-29)

The design, the migration and the evidence are in [ADR-010](ADR-010-custom-classes-and-progression.md). In short:

- **The `scale` effect** is typed only in a v9 revision (`VersionedEffects`). In a v2–v8 revision it stays unknown and byte for byte.
- **The formula identifier `SCALE.<id>`** parses only in a v9 revision: `Formula.TryParse(…, allowScales)`, which every calculation site sets from the revision's version. Below v9 it is `formula.unknown-identifier`, exactly as in older builds. A scale is a table of literals, so it reads no field and adds no dependency edge; the formula bounds are unchanged.
- **`spellcasting.multiclassCasterTable`** is nullable and absent by default, and typed only in a v9 revision. Below v9 the key stays extension data, in document order, so it is neither combined nor re-serialized.
- **A `choice` with no declared options** (M5 slice 1b) offers only content that extends it, such as a new homebrew class's subclass choice. It is a warning (`validate.choice-options-none`) and needs v9, because v8 validation refused it.
- **Versions:** `validate.requires-v9`. `RequiredSchemaVersion` now also reads formula identifiers, so `SCALE` in any of the six formula fields (value, maximum, amount, cost, bonus, spellsFormula) makes a revision v9. No stored revision changes, no database migration is needed, and the character schema stays v7.

## Consequences

- Revision hashes now come from the v2 representation. Any future change to effect serialization is a schema change and needs the same kind of migration (ADR-002 consequence).
- `UnknownEffect` means content from a newer build can be *viewed* and exported, but never partially automated.

## Alternatives considered

- **`[JsonPolymorphic]` attributes:** they throw on an unknown discriminator and cannot keep raw JSON, so they fail the round-trip requirement.
- **Expression trees or a scripting engine (Roslyn, Jint):** rejected by SPEC Q-02 (no code execution from content).
- **Keeping v1 bytes and upcasting on every read, with hashes over v1 bytes:** rejected. New revisions would have no v1 bytes, which gives two hash regimes.

## Evidence

`tests/RulesCore.Tests/EffectModelTests.cs`, `tests/AppService.Tests/UpgradeTests.cs`, `tests/AppService.Tests/SchemaTests.cs` (a v2 export validates against the v2 schemas).

Supersedes: the M0 `Effect` record (ADR-002 deferred it here).
