# ADR-021: Mechanics beyond the play-mechanics engine (MECH-X)

Status: accepted in part (owner decision D75, 2026-10-24). Items 0, 1 and 5 are accepted; items 2–4 wait for the sizing counts below. Schema numbers are taken only after the owner approves each one.
Date: 2026-10-27

## Context

ADR-018 (D43) built a generic play-mechanics engine: form groups, costs from a shared pool, rounds and upkeep, dice-less actions, attacks and riders from features, and the basis guard with server undo. A few mechanics that third-party and homebrew classes use are still out of reach:

- A weapon cannot carry its own bonus. The `AttackEntry` doc promised "the weapon's own bonus", but no field provided one, so a +1 weapon can only be a reference.
- A feature cannot apply a rider only while the player says a target is marked.
- Costs are paid only from a named pool: not from hit points or spell slots, and no feature casts a spell without a slot.
- A choice cannot say when it may be replaced (at a level-up or a rest).
- Items have no per-entry mods and no ammunition.
- A class feature cannot bring a companion with its own card.

Each of these could become schema. This ADR decides which ones do, in what order, and on what evidence.

## Decision

The work is one item at a time. Each item is generic and declarative, with a trace, and is player-triggered only. Nothing names a class, a book or a product.

**Size before you build.** Items 1–4 are adopted, trimmed or rejected on counts taken from the owner's private sources: how many features use each mechanic, never their text. The counts live only in the gitignored `tests/RulesFixtures/local/` and are summarised here as numbers. Items 0 and 5 need no sizing: item 0 is a defect, and item 5 has no schema.

| # | Item | Decision |
|---|---|---|
| 0 | A weapon's own bonus | **Adopt.** `weapon.bonus` (an integer 0..10) adds to the attack's to-hit and to its damage, traced as "+N (weapon)". It is a new field, so it needs a new content version (approved below). |
| 5 | Target marks | **Adopt, no schema.** A mark is a player-set toggle ("Marked target present"), and its riders apply only while it is on, through the D43 rider `toggle` gate. There is no target model. If a mark needs state per target, the item stops: that is a target model (see "D63 clarification"). |
| 1 | Payment kinds, spell tags, non-slot casting | **Adopt.** The counts: hit-point payment appears in 4 classes or subclasses (8 features), above the threshold of 2, so it ships with slot payment and spell tags (about 22 spells carry a tag beyond school). Three shapes fit: a flat cost per use (`cost`), a cost per die (`perUnit`, the player picks the units) and one spell slot per use. **By-slot-level hit-point payment stays by hand** (owner, 2026-10-27): the cost is worked out before a slot is chosen and has no slot-level input, so it would need a further field. Casting a spell without a slot (`castsSpell`) ships because the counts found it in 2 of the 3 sampled sources. |
| 2 | Replaceable choices | **Pending the counts.** Advisory only: the level-up list (D74) or a rest offers "You may replace one pick". If every window is a level-up, the rest windows are dropped. No character history field. |
| 3 | Item mods and ammunition | **Pending the counts.** If no mod needs state per item entry, mods apply per item and the character schema is unchanged. |
| 4 | Content companions | **Pending the counts**, under the D63 clarification below. If companions spend no pools of their own, their pools are dropped. |

**D63 clarification (owner, 2026-10-24).** D63 keeps DM and combat tools out (ROADMAP R4). A content companion is not one of them. It is a player-side feature of a class or feat, always owned by one character and shown as a card on that character's sheet (the existing companion card, extended rather than duplicated). It has no initiative, no turn order, no tracking of enemies or other creatures, and no free-form statblock authoring. It is not the D05 statblock creator.

**Schema.** All MECH-X content fields share one new content version, extended item by item until the branch ships (as D43 did with v11). If items 3 or 4 need character fields, they share one new character version. Each version is proposed to the owner before it is used, and recorded here.

| Version | Fields | Status |
|---|---|---|
| Content v12 | `weapon.bonus` (item 0) | approved by the owner (2026-10-27) |
| Content v12 | `payment` (on a roll, a feature attack and a toggle), `castsSpell` (on a roll) and spell `tags` (item 1) | approved by the owner (2026-10-27) |
| Character v11 | none yet (only if items 3–4 need one) | not proposed |

## Consequences

- **Content is written at the lowest version it needs.** A revision that uses no new field keeps its version, bytes and hash. Below the new version the new keys stay extension data, byte for byte, and have no effect. Declaring a new field at an older version is a validation error.
- **Packages carrying new-version content need a build that knows it.** An older build refuses them. Nothing changes for existing content, characters or packages.
- **Published revisions stay insert-only.** A bundled weapon that should carry a bonus gets a new revision and a licensing-review check, never an edit.
- **The trace explains the bonus.** "+N (weapon)" shows on the attack's to-hit and damage, so "Why?" (D72) explains it with one renderer suffix (the damage step ends "damage (weapon)", so a damage roll's "Why?" keeps it and a to-hit roll's does not).
- **Marks are trusted to the player.** The app does not know which creature is marked. The toggle says only that one is present.

## Not doing

- A general event processor (K11). Responses stay player-triggered (D43).
- Enforced attack substitution. `attack.replacesAttack` stays display-only.
- Expiry beyond rounds.
- Conditional grants (rejected in D43).
- A target model, initiative or other creatures' turns (D63).

## Alternatives considered

- **Item 0 with no schema** (fix the doc comment, keep +N weapons as references): rejected. Every item-heavy class and every homebrew magic weapon would stay by hand, and item 3's mods need the same field.
- **Target marks as per-target state**: rejected. It needs a target model, which D63 keeps out.
- **One version per item**: rejected. Each version is a package and build boundary for users; one version per branch keeps them few.

## Evidence

Filled in as each item lands: the tests, the fixture and the feature page.

**Item 0 (a weapon's own bonus, content v12):**

- Tests: `WeaponBonusSchemaTests` (bounds, the v12 rung, older revisions keep their bytes and hash), `WeaponBonusCalculationTests` (to-hit, damage, riders, versatile, no effect below v12), the critical-hit test in `AttackCommandTests` (a flat bonus is not doubled), `weaponAuthoring.test.ts` and `HomebrewStudioWeapons.test.tsx` (the studio editor and its strict input), the package preview tests (`SourcePackTests`, the v12 warning without a release number) and the e2e `authors a +1 weapon in the studio, equips it and rolls it`.
- Fixture: the v12 golden "Fixture Tidal Blade" in `tests/RulesFixtures/golden/revisions.json`.
- Feature pages: [multiclass-and-attacks.md](../features/multiclass-and-attacks.md), [homebrew-studio.md](../features/homebrew-studio.md), [package-format.md](../features/package-format.md) and [schemas/README.md](../schemas/README.md).

**Item 1 (payment kinds, casting without a slot, spell tags, content v12):**

- Tests: `PaymentSchemaTests` (shape, combinations, bounds, the v12 rung, older revisions keep their bytes and hash), `PaymentCalculationTests` and `SlotPaymentsTests` (payable or not, the slot a payment takes, cast-spell numbers), `PaymentPlayTests` (the service path: paying hit points and slots, the refusals, concentration at 0, the server undo, paying toggles), the payment goldens and hash properties, the Vitest cases for the Last roll's Pay button, the quickbar exception, the studio fields and the spell tag filter, and the e2e `authors a hit-point roll in the studio, uses it and undoes it`.
- Fixture: the payment goldens in `tests/RulesFixtures/golden/payment-revisions.json`.
- Feature pages: the "Paying with hit points or a slot" section of [play-mechanics.md](../features/play-mechanics.md), [sheet-play.md](../features/sheet-play.md), [homebrew-studio.md](../features/homebrew-studio.md) and [schemas/README.md](../schemas/README.md).
- Limits: a damage response's `toggleOn` outcome switches a paying toggle on with no payment, as it does for pool costs; by-slot-level hit-point payment is by hand.

**Item 5 (target marks, no schema):**

- Tests: `TargetMarkTests` (RulesCore: riders, roll modes and cross-entry riders follow the toggle) and `TargetMarkServiceTests` (the service path: the pool spend, the rolls, the refusal with no use left, the undo).
- Fixture: `tests/RulesFixtures/fixture-pack-target-marks.json` and `tests/RulesFixtures/characters/pm-marker-l5.json`.
- Feature page: the "Target marks" section of [play-mechanics.md](../features/play-mechanics.md).
- No code or schema changed: D43's toggle-gated riders already cover a mark. One limit: advantage on attack rolls against the target is not covered, because roll modes do not target attack rolls.
