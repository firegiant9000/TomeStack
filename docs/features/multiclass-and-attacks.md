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

An attack row shows the weapon's `mastery` only when the character's active content includes a feature named exactly "Weapon Mastery" (an interim name rule); otherwise the row's mastery is empty.

Each **equipped** weapon gives an entry in `sheet.attacks`:

| Value | Rule (both SRDs) |
| --- | --- |
| Ability | Strength for melee, Dexterity for ranged. With `finesse`, the better of the two |
| To hit | The ability modifier + the proficiency bonus when proficient. The trace cites the proficiency's content |
| Damage | The dice + the same ability modifier (`1d8+2`). `versatile` gives a two-handed alternative |

**Proficiency not recorded:** when no active content grants any weapon proficiency (for example a class published before weapons existed, such as the M1 SRD Barbarian revisions), the attack is **assisted**: no proficiency bonus, and `attack.proficiency-unknown` says to add it by hand. When some proficiencies are recorded but not this one, the attack is automatic with `attack.not-proficient`.

**Rolls:** `roll { weapon, damage?, versatile?, mode, critical }` rolls the attack (d20 + to hit, with advantage or disadvantage; Play sends the page's Advantage / Disadvantage choice with weapon, spell and own-attack attack rolls, never with damage) or the damage (critical doubles the dice). Rolling changes nothing.

## Your own attacks (character schema v9, item 18, D42)

`customAttacks: [{ id, name, toHit, damage?, damageType?, note? }]` are attacks the player types in (a weapon the content does not list). Nothing is calculated: `toHit` is the typed bonus (−20 to +30), `damage` is null or a dice expression within the dice limits (a flat "5" is refused), and `damageType` is null or one of the thirteen SRD damage types. At most 30, a name of 1 to 80, a note of at most 200, unique non-empty ids. They are edited on Inventory ("Your own attacks"); the damage box shows the dice engine's own message (the `dice.roll` command, as the dice box does) and saves nothing until the dice are valid.

**Rolls:** `roll { customAttack, damage?, mode, critical }` rolls `1d20` under `mode` with one modifier "{name} to hit" (the typed bonus, no origin), or the `damage` dice under normal mode with `critical` doubling them. The record's provenance is `custom.attack` / `custom.damage`, labelled "{name} attack" / "{name} damage ({type})" (the type only when set), with no content or source. An unknown id is `roll.custom-attack-unknown` and a damage roll with no damage is `roll.custom-attack-no-damage`; neither message quotes the name, which goes only into the label. `customAttack` counts toward `roll.ambiguous` like the other targets. On Play, "Attacks and actions" lists each one under "Actions" with "Roll {name} attack" (using the summary's advantage / disadvantage choice) and, when it has damage, "Roll {name} damage" (with the "Critical hit" box). The panel shows even when the character has no weapon attack, roll or toggle.

## Feature attacks and riders (content schema v11, D43 slice 4)

A feature can give an attack with an `attack` effect (an unarmed strike, a bolt) and can add to attacks with an `attackRider` effect. The fields, the validation codes and the calculation are in [play-mechanics.md](play-mechanics.md) "Slice 4"; in short:

- A feature attack is a second kind of entry in `sheet.attacks` (`source: feature`; weapons are `source: weapon`). To hit = the best listed ability modifier + the proficiency bonus (unless the attack says otherwise); damage = `{count}d{die}` (both formulas, so a die can follow a class column such as `SCALE.handDie`), the dice of the riders that apply, and one flat total (the ability modifier or the attack's `damageBonus`, plus the riders' flat parts). It has no weapon category, properties or mastery.
- **Riders** match by tag. A weapon carries `weapon` and `melee` or `ranged`; a feature attack carries its own tags and `melee` or `ranged`; `all` matches every attack. A rider can be always on or follow a toggle of its own revision or of another entry. A rider adds to-hit, extra dice (`3d8`: a count of 1 to 100, a d4 to d12) and/or flat damage; its dice appear in the damage string (`1d8+3d8+5`) and each part is a step in the trace. A critical hit doubles every die, riders' included, and not the flat total.
- **Stack groups:** of the matching riders in one `stackGroup`, only the strongest applies (highest average; the first in sheet order on a tie); the others leave a trace note (`rider.not-applied`). Riders with no group all add.
- **Unavailable attacks:** an attack that is outside the form it needs (`whileToggle` off) is listed disabled with "Available in <form>" and can still be rolled by the service; an attack whose die or count cannot be worked out is unavailable with `attack.die-invalid`, has no damage, and the service refuses to roll it.
- **Rolls:** `roll { content, effectId, mode, damage?, critical }` with the attack's effect id, like a roll effect; the weapon form (`weapon`) still finds weapons only. A pool the attack spends is offered as "Spend" in the attack roll's record; nothing spends it automatically.
- **Exports:** feature attacks are not exported in v1 ([export-adapters.md](export-adapters.md)).

## Actions grouped (SPEC C-04)

`roll` effects may declare `activation: action | bonusAction | reaction | other`. The sheet's **Attacks and actions** panel lists weapon attacks under "Actions", a feature attack under its own `activation` ("Actions" when none), and every feature roll under its activation group ("Other" when none is declared), with the "Critical hit" toggle. A roll that names a resource still spends nothing; "Spend" is offered in the roll's record (`sheet-play.md`). The features list keeps each feature's text, source and "Open page".

## Attack count, critical range and roll bonuses (M2.2, content schema v8)

- **`attacks`** (Combat): attacks per Attack action. Base 1. An Extra Attack feature is a `set` modifier (for example `2 + floor((CLASS_LEVEL + 1) / 12) + floor(CLASS_LEVEL / 20)` for the 2014 Fighter's 2/3/4). The highest set applies, so Extra Attack from two classes does not add up (SRD 5.1 p. 57, SRD 5.2.1 p. 25). The trace shows the unused one.
- **`criticalRange`** (Combat): the lowest d20 roll that is a critical hit with weapon attacks. Base 20; Improved and Superior Critical are `bonus -1` each (20 → 19 → 18). A weapon attack roll's record says "critical hit on 19–20" when the range is lower. The player still ticks "Critical hit" for the damage roll. Only `bonus` may change it: `set` and `replace` keep the *highest* value, but a lower range is better, so the validator refuses them (`validate.critical-range-operation`).
- **Bounds:** the attack count is at least 1 and the critical range is 1 to 20. Content that takes either outside is bounded, with a "bound" trace step and an `effect.out-of-range` warning naming the last effect that changed the field. An override is not bounded.
- **`roll.bonus`:** a formula added to a feature roll's dice for the character, for example Second Wind's `1d10` plus `CLASS_LEVEL`. It is a flat modifier in the record, never doubled on a critical. It keeps its sign: `STR.MOD` at Strength 8 is −1. The button shows it with the sign spelled out ("1d10 + 5", "1d6 − 1"), and the record labels it "<roll label> bonus", without the formula. A formula that cannot be evaluated for the character (for example `CLASS_LEVEL` on a feat, which belongs to no class) is a content diagnostic (`effect.invalid-formula`, the feature becomes assisted), and the roll is refused (`roll.bonus-invalid`) rather than rolled without its bonus.

## Not yet

- The multiclass data of the non-caster classes that are not bundled (Monk; the Rogue is bundled since C2-2, with its multiclass data). The Fighter is bundled since M2.2. Bundled: the eight casters, the weapon tables (`srd-<family>-equipment.json`) and a content v5 revision of the SRD Barbarian (`srd-<family>-classes.json`). Characters that pin the M1 Barbarian revision keep it. Pickers offer the newest revision (`ContentOption.Superseded`), and moving a saved character to it is the reviewed update (SPEC I-06).
- The net and the blowgun: text only, because their damage is not a dice roll.
- Ammunition tracking, Weapon Mastery effects (text only), fighting styles other than Defense (text only), two-weapon fighting and bonus damage from features. Armor training and its warnings are in `equipment.md` (M2.2).
