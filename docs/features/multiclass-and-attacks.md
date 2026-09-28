# Multiclass prerequisites, proficiency subsets, weapons and attacks

SPEC C-01, C-02, C-04 · LIVING_SPECS D04 · MVP "Builder" (multiclass path), "Sheet" (attacks, damage) · status: **implemented (M2)**. The SRD weapon tables, the SRD Barbarian's multiclass data and the eight SRD casters' multiclass prerequisites and proficiencies are bundled (SPEC Q-03, `licensing/srd-pack-review.md`).

Rules core: `RestrictionEffect.Multiclass`/`Group`, `GrantEffect.OnlyAs`, `ChoiceEffect.OnlyAs`, `RollEffect.Activation` and `WeaponEffect` in `src/RulesCore/Effects.cs`. The calculator is in `src/RulesCore/Calculation.cs` (`Unmet`, `EntryApplies`, `CollectAttacks`). Service: weapon rolls in `src/AppService/Rolling.cs`. UI: the "Attacks and actions" panel in `src/Ui/src/components/PlayPanels.tsx`. Acceptance: `tests/RulesCore.Tests/MulticlassAndAttackTests.cs`, `tests/AppService.Tests/AttackCommandTests.cs`, and the e2e test "equips a weapon…". Fixtures: `tests/RulesFixtures/fixture-pack-m2-combat.json` (the invented "Fixture Duelist" and three weapons).

All of this is content schema v5. The new fields on existing effect types are optional and absent by default, so existing revisions serialize byte for byte as before (ADR-003).

## Multiclass prerequisites (D04)

A class declares `restriction { field, minimum, multiclass: true }` (for example Strength 13). Alternatives share a `group`: "Strength 13 **or** Dexterity 13" is two restrictions with one group. Restrictions without a group must each be met.

- Checked **only when the character has levels in two or more classes**, for every class. Both SRDs require the prerequisites of the current classes and of the new one.
- Checked against the calculated sheet as it is. A class's own features count, because its levels are already taken.
- **Unmet: a warning, not an exclusion** (`restriction.multiclass-unmet` on the class). The class stays applied, so its features and hit points do not vanish from a character. The builder's level-up preview and the sheet show the warning.
- Ordinary prerequisites (feats, items) keep their M1 behavior: checked without the content itself, and unmet content is left out. They can now use `group` too.

## Proficiency subsets (D04)

`onlyAs: "startingClass" | "multiclass"` on a class's `grant` or `choice`:

- `startingClass`: only when the class is the first class taken. Examples are saving throw proficiencies and the full skill choice.
- `multiclass`: only when it was taken later. This is the SRD "as a multiclass character" subset, for example one skill.

A choice that does not apply to this entry is not offered, so it is never "unresolved". Content outside a class ignores `onlyAs`.

## Weapons and attacks (SPEC C-02, C-04)

An item carries `weapon { category: simple | martial, attack: melee | ranged, damage, damageType, properties[], versatile?, range?, weaponKey, mastery? }`. Proficiency is a `grant proficiency` in `weapon.simple`, `weapon.martial` or `weapon.<weaponKey>`, and it may be `onlyAs` like any grant.

Each **equipped** weapon gives an entry in `sheet.attacks`:

| Value | Rule (both SRDs) |
| --- | --- |
| Ability | Strength for melee, Dexterity for ranged. With `finesse`, the better of the two |
| To hit | The ability modifier + the proficiency bonus when proficient. The trace cites the proficiency's content |
| Damage | The dice + the same ability modifier (`1d8+2`). `versatile` gives a two-handed alternative |

**Proficiency not recorded:** when no active content grants any weapon proficiency (for example a class published before weapons existed, such as the M1 SRD Barbarian revisions), the attack is **assisted**: no proficiency bonus, and `attack.proficiency-unknown` says to add it by hand. When some proficiencies are recorded but not this one, the attack is automatic with `attack.not-proficient`.

**Rolls:** `roll { weapon, damage?, versatile?, mode, critical }` rolls the attack (d20 + to hit, with advantage or disadvantage) or the damage (critical doubles the dice). Rolling changes nothing.

## Actions grouped (SPEC C-04)

`roll` effects may declare `activation: action | bonusAction | reaction | other`. The sheet's **Attacks and actions** panel lists weapon attacks under "Actions", and every feature roll under its activation group ("Other" when none is declared), with the "Critical hit" toggle. A roll that names a resource still spends nothing; "Spend" is offered in the roll's record (`sheet-play.md`). The features list keeps each feature's text, source and "Open page".

## Not yet

- The multiclass data of the non-caster classes that are not bundled (Fighter, Monk, Rogue). Bundled: the eight casters, the weapon tables (`srd-<family>-equipment.json`) and a content v5 revision of the SRD Barbarian (`srd-<family>-classes.json`). Characters that pin the M1 Barbarian revision keep it. Pickers offer the newest revision (`ContentOption.Superseded`), and moving a saved character to it is the reviewed update (SPEC I-06).
- The net and the blowgun: text only, because their damage is not a dice roll.
- Armor proficiency and its penalties, ammunition tracking, Weapon Mastery effects (text only), fighting styles, two-weapon fighting and bonus damage from features.
