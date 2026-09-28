# Rules-family policy differences

SPEC S-02, S-03, C-01 · ADR-002 · ROADMAP M1 risk "rules breadth across two editions".

Differences between SRD 5.1 (2014 rules) and SRD 5.2.1 (2024 rules) are either **content** (a different revision for each family, never matched by name) or a **named field on `RulesFamilyPolicy`**, tested side by side. Nothing is inferred from names.

## Policy fields

| Field | 2014 (`srd-5.1`) | 2024 (`srd-5.2.1`) | Tests |
| --- | --- | --- | --- |
| `AbilityIncreaseSource`: which origin content may change ability scores (every operation; a feature chosen from or granted by that origin, through any chain of features, counts as it) | species | background | `RulesFamilySideBySideTests`, `ChoiceTests`, `SrdRulesFamilyTests` |
| `BackgroundGrantsFeat`: whether a background, or a feature that comes from one, may bring in a feat, by a grant or as a choice option (other content is allowed under both) | no | yes | `RulesFamilySideBySideTests`, `SrdRulesFamilyTests`, `ChoiceTests` |
| `LongRestExhaustionNeedsFoodAndDrink` (M2 item 3): a long rest removes one exhaustion level only with food and drink. The rest preview proposes the reduction in both families and, where this is true, names the condition so the player can untick it | yes | no | `RestPlannerTests.Exhaustion_needs_food_and_drink_only_under_2014_rules_side_by_side` |
| `LongRestHitDice` (short rest, D01 follow-up): how many spent hit dice a long rest gives back (SRD 5.1 p. 87, SRD 5.2.1 p. 185) | `HalfTotal`: up to half the total, at least one | `All` | `ShortRestAndHitDiceTests.Long_rest_hit_dice_half_the_total_under_2014_and_all_under_2024_side_by_side` |
| `HitDieHealingMinimum`: the fewest hit points one spent hit die restores (SRD 5.2.1 p. 187 "minimum of 1"; SRD 5.1 p. 87 states none, and TomeStack uses 0 so a die never takes hit points away) | 0 | 1 | `ShortRestAndHitDiceTests.A_short_rest_heals_each_rolled_die_plus_con_and_the_minimum_differs_side_by_side` |
| `ShortRestNeedsOneHitPoint`: a short rest needs at least 1 hit point to start (SRD 5.2.1 p. 187; not stated in SRD 5.1). A long rest needs it under both, which is a rules constant | no | yes | `ShortRestAndHitDiceTests.Resting_at_0_hit_points_follows_each_family_side_by_side` |
| `HalfCasterLevels` (M3 C3): how a half caster's (`multiclassCaster: half`) class levels count toward the Multiclass Spellcaster table (SRD 5.1 p. 58, SRD 5.2.1 p. 25) | half, rounded down | half, rounded up | `SrdCasterTests.A_Sorcerer_Paladin_combines_slots_on_the_multiclass_table_differently_per_family_side_by_side`, `MulticlassSpellSlotTests` |
| `ThirdCasterLevels` (M3 C3): the same for a third caster. Neither SRD has one; TomeStack's choice for homebrew | a third, rounded down | a third, rounded down | `MulticlassSpellSlotTests.A_third_caster_counts_a_third_rounded_down_under_both_families` |
| `MulticlassSpellSlots` (M3 C3): the Multiclass Spellcaster table. Identical numbers in both SRDs (parsed twice from each PDF, and equal to the Wizard's table); a field so that a family could differ | SRD 5.1 p. 58 | SRD 5.2.1 p. 26 | `MulticlassSpellSlotTests` |

Origin is carried on the active content: species and background content is its own origin, a feature inherits the origin of whatever granted or offered it, and a feat or class starts none. A policy therefore holds however the content is reached.

## M1 item 6 review: does the SRD slice need more?

**No new field (2026-09-26).** Each difference the slice exercises, and how it is handled:

| Difference in the slice | Handled by |
| --- | --- |
| Half-Orc raises Str/Con (2014); 2024 species raise none | `AbilityIncreaseSource`, plus content (the 2024 Dwarf has no increase) |
| The 2024 Soldier's +2/+1 or +1/+1/+1 ability options | Content (a choice of seven option features) plus `AbilityIncreaseSource` (the options count as background content) |
| The 2024 Soldier grants Savage Attacker; the 2014 Acolyte grants a feature | `BackgroundGrantsFeat` (only feats are restricted) |
| The Barbarian's Rage recovers one use on a Short Rest (2024) but not (2014); Danger Sense wording; Weapon Mastery and Primal Knowledge exist only in 2024 | Content: two different Barbarian revisions |
| Hit points (die maximum, then the fixed value), proficiency bonus, skill and save calculation | Identical in both SRDs, so no difference to encode |

**Considered and not added:**

- "Ability increases cannot raise a score above 20". Both SRDs have this rule, so it is not a difference. Since M2 item 4 it is a rules constant (`CharacterCalculator.AbilityScoreIncreaseCap`, `AbilityCapTests` for both families).
- Armor (M2 item 4): light, medium and heavy armor and shields work the same in both SRDs, so they are content (`armor` effects), not a field.
- A per-family hit point rule. Both SRDs use "maximum at level 1, then the roll or the fixed value", so a field would encode nothing.

**Spellcasting (M2, content v5): no policy field.** The 2014/2024 differences in spellcasting are per class: slot progressions (for example half casters from level 1 in 2024), prepared counts as a formula (2014) or a table (2024), and which classes prepare. Each SRD class revision states them in its `spellcasting` tables, so they are content. Spell attack and save DC use the same formula in both SRDs (`SpellcastingTests.The_same_caster_calculates_the_same_under_both_families_side_by_side`). **Multiclass slots (M3 C3) do need fields:** whether a class is a full or half caster is content (`multiclassCaster`), but how half levels round differs by family, so that is `HalfCasterLevels`.

Revisit this table whenever the SRD slice grows. A new difference gets a named field and a side-by-side test before any content depends on it.
