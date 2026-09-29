# Equipment groundwork: armor, shields and the ability score cap

SPEC C-02, C-03, C-05 · MVP "Builder" (equipment) · status: implemented (M2 item 4).

Rules core: `ArmorEffect` in `src/RulesCore/Effects.cs`, and `AddArmor` plus the cap in `src/RulesCore/Calculation.cs`. UI: `src/Ui/src/components/EquipmentPanel.tsx`. Acceptance: `tests/AppService.Tests/EquipmentTests.cs`, `tests/RulesCore.Tests/AbilityCapTests.cs`, `tests/RulesCore.Tests/ArmorValidationTests.cs`, and the equipment part of the e2e test "builds an SRD 5.2.1 Barbarian as drafts".

## Equipment on the character (character schema v4)

`equipment: [{ item, equipped, quantity }]`: an exact pin per item, one entry per item (`character.equipment-duplicate`), quantity 1–9,999. **Only equipped items apply.** They are active content like a pin: their effects count, and they appear in the features list. Equipping content that is not an item is refused on the sheet (`equipment.not-an-item`). Unequipped items are still references, so packages carry them and `content.affected` reports them (`equipment`).

The sheet's **Equipment** panel adds an item from the character's rules family, equips and unequips it, and removes it. Each change is saved with the character.

## The `armor` effect

`{ type: "armor", id, category: light | medium | heavy | shield, armorClass, dexterityCap? }` on an item, in a **content schema v4** revision. In a v2 or v3 revision an `armor` effect stays unknown, reference-only and byte for byte, because 0.2.0 stored it that way; typing it would change its hash and its meaning (ADR-003 "Content schema v4"). Validation refuses armor below v4 (`validate.requires-v4`). Older builds keep it as an unknown, reference-only effect. The rules are the same in both SRDs:

| Category | Armor Class |
| --- | --- |
| light | `armorClass` + Dex modifier |
| medium | `armorClass` + Dex modifier, at most `dexterityCap` (default 2) |
| heavy | `armorClass` |
| shield | + `armorClass` (a bonus) |

Validation: `armorClass` 0–30 (`validate.armor-class`), a Dex cap only on medium armor (`validate.armor-dexterity-cap`), at most one armor and one shield per item (`validate.armor-duplicate`), and a warning on non-item content (`validate.armor-kind`).

## Armor Class

1. The base is 10 + Dex modifier.
2. **Worn body armor** replaces the base, and the trace cites the item, its source and page.
3. **While armor is worn, every other Armor Class `replace` is an unarmored alternative and does not apply.** It is traced as "not used: it applies only while no armor is worn, and item '…' is worn". Every SRD alternative in the slice (Unarmored Defense in both SRDs) applies only without armor, and a shield does not stop the Barbarian's. So the M1 Unarmored Defense revisions become conditional without a new revision.
4. A shield adds its bonus, with or without armor.
5. Only one body armor and one shield count. Extra ones get `equipment.multiple-armor` / `equipment.multiple-shields`.

Brenna (Dex +1, Con +2): unarmored 13 (Unarmored Defense), light 11 → 12, medium 14 → 15, heavy 17, shield +2.

**Since M2.2 (content schema v8):**
- **Armor training:** a class grants `armor.light`, `armor.medium`, `armor.heavy` or `armor.shield`, like weapon proficiencies (`onlyAs` works too). Worn armor without training is a warning on Armor Class: disadvantage on d20 tests that use Strength or Dexterity, and no spellcasting. An untrained shield still adds to Armor Class under 2014 rules and adds nothing under 2024 rules (`RulesFamilyPolicy.UntrainedShieldGivesArmorClass`; SRD 5.1 p. 62, SRD 5.2.1 p. 92).
  - **When training is checked (PR #12 review fix):** only when *every* class the character has levels in records its armor training: a v8 class with at least one `armor.*` grant, gated or not, all of them automatic and always on. A class with no armor training records `armor.none` (the SRD Wizard and Sorcerer). If any class records nothing (every class revision written before v8, such as the bundled Barbarian, Bard, Cleric, Druid and Warlock), has an assisted or conditional armor grant, or does not resolve at all (missing, a newer schema, another rules family), the training warnings and the 2024 untrained-shield rule are both skipped: that class's training is unknown. Training from other content (a feat granting `armor.light`) never turns the check on by itself. An assisted or conditional armor grant on any content (a feat, species or item) turns it off, because the calculator cannot tell what it gives. **Known limit:** training that pre-v8 non-class content grants only in its text is invisible; after taking a v8 class update, the update review shows the Armor Class change, and an override fixes it. So a Paladin 5 / Fighter 1 in Plate is not flagged, and a 2024 Cleric with a light-armor feat keeps the shield's Armor Class.
  - New Paladin revisions record light, medium and shield armor, and heavy armor as the starting class only; new Ranger revisions record light, medium and shield armor, in both families, each worded from its SRD's proficiency and multiclassing text. New Wizard and Sorcerer revisions record `armor.none`. Characters are offered them as updates. (The first v8 Paladin and Ranger revisions, with Extra Attack, stay as they were: revisions are insert-only.)
- **Strength and Stealth:** armor may name `strength` (the score it needs) and `stealthDisadvantage`. Below the Strength score, Armor Class warns that speed is 10 feet lower (TomeStack has no speed field), and Stealth warns about the disadvantage. The player picks disadvantage when they roll.
- **While armored:** a modifier with `whileArmored` applies only while body armor is worn (a shield alone does not count), as the Defense fighting style needs. The validator refuses `whileArmored` on an Armor Class `replace` (`validate.while-armored-replace`): worn armor turns every Armor Class replacement off, so it could never apply.
- **Below content v8:** a revision that declares an older schema but carries `whileArmored`, armor `strength` / `stealthDisadvantage`, a roll `bonus`, an armor training grant, or a modifier on `attacks` or `criticalRange` (an import or a hand edit; the validator refuses to publish that) has the field ignored, as an older build would, with `effect.schema-field-ignored`. A restriction on `attacks` or `criticalRange` keeps its content out, as an unknown field does in an older build. Nothing is rewritten, so no stored hash changes.

Not modeled yet: speed itself, and a feature that needs "no shield" (for example the Monk's Unarmored Defense, not in the SRD slice). Weapons and attacks: [multiclass-and-attacks.md](multiclass-and-attacks.md) (content v5).

## "No ability score above 20" (owner decision 2026-09-27)

Both SRDs say ability score increases cannot raise a score above 20. So in both families (a rules constant, not a policy field):

- A **bonus** to an ability score stops at 20. The trace step says "capped: increases cannot raise an ability score above 20 (+4 would give 21)". If the score is already above 20 (a base score or a `set`), a bonus does not raise it and does not lower it.
- Increases apply first, in content order, so the cap lands on whichever increase crosses 20. Penalties (negative bonuses) apply after them. So the result does not depend on content order: 19 + 2 stops at 20, then − 2 gives 18, whichever effect is listed first (`AbilityCapTests.The_cap_does_not_depend_on_effect_order_increases_first_then_penalties`; M2 review fix).
- `set` effects (for example magic items that set a score) and user overrides may exceed 20. Penalties are not capped.
- A higher content-declared maximum (for example "up to 24") is not modeled yet.

## Why no SRD armor table yet

The bundled SRD packs contain only the reviewed M1 slice ([srd-pack-review.md](../licensing/srd-pack-review.md)). Adding the SRD armor table means adding SRD text and page citations, which requires the pack review against the SRD PDFs (SPEC Q-03). Until then, armor comes from homebrew or manual entry (M2 item 5). Tests and development use original fixture armor (`tests/RulesFixtures/fixture-pack-m2.json`, seeded only by development hosts).
