# Spellcasting (D04)

SPEC C-01, C-02, C-04, C-05 · LIVING_SPECS D04 · MVP "Builder" (spells), "Sheet" (slots) · status: **engine implemented (M2); SRD casters and spells not bundled yet** (owner decision 2026-09-27: the full SRD casters, levels 1–20, in both families, under an extended pack review, SPEC Q-03).

Rules core: `SpellcastingEffect` and `SpellEffect` in `src/RulesCore/Effects.cs`, and the "spellcasting" section of `src/RulesCore/Calculation.cs`. Service: slot actions in `src/AppService/Play.cs`, spell rolls in `src/AppService/Rolling.cs`. UI: the spell picker in `src/Ui/src/components/CharacterBuilder.tsx` and `src/Ui/src/components/SpellsPanel.tsx`. Acceptance: `tests/RulesCore.Tests/SpellcastingTests.cs`, `tests/AppService.Tests/SpellcastingCommandTests.cs`, and the e2e test "builds a spellcaster…". Fixtures: `tests/RulesFixtures/fixture-pack-m2-spells.json` (invented casters and spells; the tables are not the SRD's).

## D04 scope

- **In M2:** one spellcasting class per character, with spell attack bonus, save DC, slots by class level, and prepared or known spells.
- **A second caster:** calculated separately (its own attack bonus, save DC and slots). Combining spell slots across classes (the SRD multiclass spellcaster table) is **not** done. The slot fields are assisted, with `spellcasting.multiclass-slots`. The manual step is to record the total as an override (SPEC C-06), which play then uses.
- **Pact Magic:** its own pool (`pactSlots`), recovered by a short or a long rest, next to ordinary slots.

## Content (schema v5)

A class, subclass or class feature carries one `spellcasting` effect:

| Field | Meaning |
| --- | --- |
| `ability` | The spellcasting ability |
| `preparation` | `prepared` (the list can change, as with a spellbook) or `known` |
| `spellList` | A key such as `wizard`. Spells name the lists they are on. A key, never a display name |
| `slotKind` | `spellSlots` (default) or `pactMagic` (each row has one non-zero count: the slots of that level) |
| `slots` | 20 rows, one per class level, each the counts for spell levels 1–9 |
| `cantrips`, `spellsTable` | Optional, 20 counts each: cantrips, and spells known or prepared |
| `spellsFormula` | Optional instead of `spellsTable`, for example `max(1, INT.MOD + CLASS_LEVEL)` (2014 prepared casters) |

Tables live in the content, so each SRD class revision states its own progression. The 2014/2024 differences are **content, not policy fields**, for example half casters with slots at level 1 in 2024 and a prepared count that is a formula (2014) or a table (2024). `rules-family-policy.md` records this conclusion. The SRD content commits add a side-by-side test for each difference.

A spell is `kind: spell` content with one `spell` effect: `level` (0 is a cantrip), `school`, `castingTime`, `range`, `components`, `duration`, `concentration`, `ritual`, `lists`, `attack` (`none` / `melee` / `ranged`), `save` and optional `dice`. **A spell is never active content**, so nothing on it changes calculated fields. Validation checks the tables, the level, the dice, and that both types are in a v5 revision (`validate.spellcasting`, `validate.spell-level`, `validate.requires-v5`).

Like `armor`, both types are typed only in a revision of schema v5 or newer. In an older revision an effect with the same type name stays unknown, reference-only and byte for byte (ADR-003).

## Calculation

- **Casters:** the active revisions with a non-reference `spellcasting` effect, in the order the classes were taken. The first is the **primary** caster. Spellcasting outside a class the character has levels in is disabled (`spellcasting.no-class`), and so are bad tables (`spellcasting.table-invalid`). The rest of the sheet still calculates.
- **Fields:**
  - `spellAttack` = PB + the ability modifier, and `spellSaveDc` = 8 + PB + the ability modifier (both SRDs).
  - `spellSlots.1`–`spellSlots.9` come from the primary slot caster's table at its class level.
  - `pactSlots` comes from the pact caster.
  - Each field traces its content, source and page, and can be overridden.
  - For a non-caster they are 0, automatic, and hidden on the sheet.
- **Spells:** `sheet.spellcasting[]` lists each caster with its own attack bonus and save DC, the counts for its level, and its recorded spells with their data. Problems are flagged, and the spell stays listed:
  - a spell not on the caster's list (`spells.not-on-list`);
  - a level above the caster's highest slot (`spells.level-too-high`);
  - more cantrips or spells than the count (`spells.too-many-cantrips`, `spells.too-many`).
  
  A spell that cannot be used at all (missing, a draft, the wrong family, not a spell) is dropped from the list with `spells.unusable`.

## The character (schema v6)

`spells: [{ caster, spell, prepared }]`:

- `caster` is the content id of the spellcasting class, so an update of that class keeps the list.
- `spell` is an exact pin.
- `prepared` matters for prepared casters (spells in a spellbook that are not prepared today).

Play state adds `spellSlotsSpent: [{ level, spent }]` and `pactSlotsSpent`. v1–v5 characters are upcast with no spells and nothing spent. There is no database migration. Spells are character references: packages carry them, and `content.affected` reports them (role `spell`).

## Play

| Action | Does | Refused with |
| --- | --- | --- |
| `spendSlot` / `regainSlot` (`amount` = spell level) | One slot of that level | `slots.none-left`, `slots.nothing-spent`, `play.amount-out-of-range` |
| `spendPactSlot` / `regainPactSlot` | One Pact Magic slot | `slots.none-left`, `slots.nothing-spent` |

- **Rests:** a long rest proposes every spent slot and Pact Magic slot back. A short rest proposes the Pact Magic slots back ([rests.md](rests.md)).
- **Rolls:** `roll { spell, spellAttack }` rolls the spell's attack with the caster's bonus, or its `dice` (critical doubles). Rolling never spends a slot.
- **The sheet's "Spells and slots" panel:** slots with Spend / Regain, and each caster with its spells. A spell has "Roll … attack", "Roll … (dice)" and "Cast … (spend a slot)". Cast spends the lowest slot that is left at the spell's level or higher. To cast at a higher level, the player spends that slot with its own button. A prepared caster has a "Prepared" checkbox per spell, which is saved with the character (preparing is a choice, not play state).

## Builder

The choices step (create, level-up and "Make choices") has a picker per caster. It lists the spells on the caster's list up to its highest slot level, grouped by level, and shows the counts ("0 of 3 cantrips, 0 of 4 prepared spells"). Picks are previewed on the draft like choices, and going over a count is flagged on the sheet, not blocked.

## Not yet

- The SRD casters and spells (the next content commits, each with its pack review update).
- Combined multiclass slots and Pact Magic combined with slots (M3, D04).
- Upcast damage, cantrip scaling, spell components and material costs, and concentration tracking (text only).
- Spell authoring in the homebrew studio. Homebrew spells can be imported as content v5 revisions.
