# Rules-family policy differences

SPEC S-02, S-03, C-01 · ADR-002 · ROADMAP M1 risk "rules breadth across two editions".

Differences between SRD 5.1 (2014 rules) and SRD 5.2.1 (2024 rules) are either **content** (a different revision for each family, never matched by name) or a **named field on `RulesFamilyPolicy`**, tested side by side. Nothing is inferred from names.

## Policy fields

| Field | 2014 (`srd-5.1`) | 2024 (`srd-5.2.1`) | Tests |
| --- | --- | --- | --- |
| `AbilityIncreaseSource`: which origin content may change ability scores (every operation; a feature chosen from or granted by that origin, through any chain of features, counts as it) | species | background | `RulesFamilySideBySideTests`, `ChoiceTests`, `SrdRulesFamilyTests` |
| `BackgroundGrantsFeat`: whether a background, or a feature that comes from one, may bring in a feat, by a grant or as a choice option (other content is allowed under both) | no | yes | `RulesFamilySideBySideTests`, `SrdRulesFamilyTests`, `ChoiceTests` |

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

- "Ability increases cannot raise a score above 20". Both SRDs have this rule, so it is not a difference. It is a calculation gap, recorded in `docs/licensing/srd-pack-review.md`.
- A per-family hit point rule. Both SRDs use "maximum at level 1, then the roll or the fixed value", so a field would encode nothing.

Revisit this table whenever the SRD slice grows (spellcasting in M2 is the likely next difference). A new difference gets a named field and a side-by-side test before any content depends on it.
