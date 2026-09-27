# Equipment groundwork: armor, shields and the ability score cap

SPEC C-02, C-03, C-05 · MVP "Builder" (equipment) · status: implemented (M2 item 4).

Rules core: `ArmorEffect` in `src/RulesCore/Effects.cs`, and `AddArmor` plus the cap in `src/RulesCore/Calculation.cs`. UI: `src/Ui/src/components/EquipmentPanel.tsx`. Acceptance: `tests/AppService.Tests/EquipmentTests.cs`, `tests/RulesCore.Tests/AbilityCapTests.cs`, `tests/RulesCore.Tests/ArmorValidationTests.cs`, and the equipment part of the e2e test "builds an SRD 5.2.1 Barbarian as drafts".

## Equipment on the character (character schema v4)

`equipment: [{ item, equipped, quantity }]`: an exact pin per item, one entry per item (`character.equipment-duplicate`), quantity 1–9,999. **Only equipped items apply.** They are active content like a pin: their effects count, and they appear in the features list. Equipping content that is not an item is refused on the sheet (`equipment.not-an-item`). Unequipped items are still references, so packages carry them and `content.affected` reports them (`equipment`).

The sheet's **Equipment** panel adds an item from the character's rules family, equips and unequips it, and removes it. Each change is saved with the character.

## The `armor` effect

`{ type: "armor", id, category: light | medium | heavy | shield, armorClass, dexterityCap? }` on an item. This is a new effect type, so older builds keep it as an unknown, reference-only effect (ADR-003), and no content schema version change is needed. The rules are the same in both SRDs:

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

Not modeled yet: armor proficiency, heavy armor's Strength requirement and speed penalty, Stealth disadvantage (text only), a feature that needs "no shield" (for example the Monk's Unarmored Defense, not in the SRD slice), and weapons or attacks.

## "No ability score above 20" (owner decision 2026-09-27)

Both SRDs say ability score increases cannot raise a score above 20. So in both families (a rules constant, not a policy field):

- A **bonus** to an ability score stops at 20. The trace step says "capped: increases cannot raise an ability score above 20 (+4 would give 21)". If the score is already above 20 (a base score or a `set`), a bonus does not raise it and does not lower it.
- Bonuses apply in content order, so the cap lands on whichever bonus crosses 20.
- `set` effects (for example magic items that set a score) and user overrides may exceed 20. Penalties are not capped.
- A higher content-declared maximum (for example "up to 24") is not modeled yet.

## Why no SRD armor table yet

The bundled SRD packs contain only the reviewed M1 slice ([srd-pack-review.md](../licensing/srd-pack-review.md)). Adding the SRD armor table means adding SRD text and page citations, which requires the pack review against the SRD PDFs (SPEC Q-03). Until then, armor comes from homebrew or manual entry (M2 item 5). Tests and development use original fixture armor (`tests/RulesFixtures/fixture-pack-m2.json`, seeded only by development hosts).
