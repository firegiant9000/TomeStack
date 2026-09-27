# ADR-003: Declarative effect AST and bounded formulas

Status: accepted. The effect model and migration are implemented in `RulesCore/Effects.cs`, and the formula grammar in `RulesCore/Formulas.cs` (`tests/RulesCore.Tests/FormulaTests.cs`: explicit hostile cases, 20,000 fuzzed strings, 2,000 generated formulas, and feature isolation).
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
| `resource` | `resourceId`, `label`, `maximum` (formula) | sheet maximum with trace, and `character.play` spending (M2 item 2, `features/sheet-play.md`) |
| `choice` | `choiceId`, `count`, `options[]` (pins), optional `level` (v3) | calculator and `character.choose` (M1 item 4, `features/choices.md`); builder UI (M2) |
| `restriction` | `field`, `minimum` | prerequisite check in the calculator (M1 item 3, `features/validation-and-restrictions.md`) |
| `recovery` | `resourceId`, `on` (`shortRest` / `longRest`), `amount` (formula or `all`) | long rest preview and confirmed rest (M2 item 3, `features/rests.md`); short rest after M2 |
| `roll` | `rollId`, `label`, `dice`, optional `resourceId` | dice engine (item 13) |

**Unknown types** deserialize to `UnknownEffect`, which keeps the original JSON and writes it back with the same properties, order and values (whitespace and string escaping are normalized), and is always reference-only (`effect.unsupported`). A *known* type with a malformed body, including wrong value kinds such as a numeric `id`, degrades the same way instead of failing the whole revision. Only an effect that is not a JSON object fails its revision.

Field ids: `initiative`, `proficiencyBonus`, `armorClass`, `hitPoints`, `ability.<abl>.score`, `ability.<abl>.mod`, `save.<abl>`, `skill.<name>` (all 18 skills; see `features/levels-and-classes.md`).

### Stacking and order (per field)

1. **base**: the character's choice or the rules' derivation. The highest `replace` substitutes for it, and the other replacements are traced as not applied.
2. **bonus** in content order. `stack` bonuses all add. Among `highestInGroup` bonuses with the same `stackGroup`, only the highest applies, and the rest are traced as "does not stack". A bonus that would take the running value outside ±1,000,000 is not applied (`effect.out-of-range`), so many bounded bonuses cannot overflow.
3. The highest **set** (if any) replaces the running value.
4. **Rounding:** any fraction rounds down at the end of a formula (the 5e default).
5. **User override** last (SPEC C-06). The computed value and its trace are kept.

**Cycles:** an effect is disabled (`effect.dependency-cycle`) when its dependency edge lies inside a strongly connected component of the field graph, or reads its own target. An edge that base inputs already imply (for example a Dex modifier effect reading `DEX.SCORE`) is exempt: it adds no reachability, so it cannot close a cycle.

**Automation per field:** a field is `assisted` when it or any field it reads has an effect the calculator did not apply (not `automatic`, not `always`, an invalid formula, a cycle or out of range), because the user may have to account for it by hand. Effects ignored by rules-family policy do not count: the rules say they do not apply.

### Timing

Derived fields use only `always`. `whileActive` effects need an assisted toggle (M2). `onRoll` belongs to the dice engine. `onShortRest` and `onLongRest` belong to rest previews, which never apply themselves (ARCHITECTURE "commands vs calculation").

### Formula grammar (item 10)

```text
formula := sum
sum     := product (("+" | "-") product)*
product := unary (("*" | "/") unary)*
unary   := "-" unary | primary
primary := NUMBER | IDENT | FUNC "(" sum ("," sum)* ")" | "(" sum ")"
FUNC    := floor | ceil | min | max | abs
IDENT   := PB | LEVEL | CLASS_LEVEL | (STR|DEX|CON|INT|WIS|CHA) "." (MOD|SCORE)
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
- **Why a version and not an extension field:** a v2-only build would read `level` as an unknown extension and apply a level-3 feature at level 1. Refusing v3 (`content.schema-unsupported`, `package.schema-unsupported`) is safer than silently calculating differently. New effect *types*, by contrast, are forward-compatible (they become `UnknownEffect`), but new *fields* on existing types are not.

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
