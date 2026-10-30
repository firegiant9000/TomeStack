# Effect vocabulary coverage: Avrae automation and Foundry dnd5e activities against content v11

Written 2026-10-26 · LIVING_SPECS D43 · addendum F6 · measured against content v11 as built on PR #92 cc0201c ([play-mechanics.md](play-mechanics.md), [m3-effects.md](m3-effects.md), [dice-engine.md](dice-engine.md), [homebrew-studio.md](homebrew-studio.md); schema `docs/schemas/content-revision.v11.schema.json`).

## Purpose and non-goals

Two public automation vocabularies give TomeStack an outside yardstick: the node types of the [Avrae automation reference](https://avrae.readthedocs.io/en/latest/automation_ref.html) and the activity types of the [Foundry VTT dnd5e system](https://github.com/foundryvtt/dnd5e/wiki/Activities) (the wiki page is marked up to date as of dnd5e 6.0.0). Each row below names one of their terms and the content v11 effect or field that carries the same job, or says why nothing does. It is a checklist for the Lab's capability registry (D62) and for authoring the remaining martial classes under v11, so a missing verb shows up before the content does.

Non-goals:

- **No adoption.** TomeStack neither reads nor writes either format; the Foundry export ([export-adapters.md](export-adapters.md)) is unchanged.
- **No scripting.** Branches, variables bound at run time and called automation stay out (ADR-011, ADR-003). A row that needs them is "not covered" by design, not a gap.
- **No rules text.** Only the vocabularies' own term names appear here, and SRD feature names where the gap table needs them. TomeStack is not affiliated with Avrae or Foundry VTT.
- **Other creatures stay out** (ADR-018): targets, their saves, their defences, summons and maps are table play.

Coverage words are the Lab's registry words: **Automatic** (the sheet works it out and applies it), **Assisted** (the sheet works out the numbers or offers the step; the player confirms or applies it), **Reference** (text only). G-ids are the gaps G1–G12 of the 10x plan evaluation, §3 (also listed at the end).

## Avrae automation nodes

| Avrae node | v11 effect(s) | Coverage | Gap | Note |
| --- | --- | --- | --- | --- |
| `target` | `roll.targets` (text) | Reference | — | Not covered: other creatures are out of scope (ADR-018). Everything TomeStack applies is to the character itself |
| `attack` | `attack` (with `abilities`, `damage`, `damageBonus`, `tags`, `whileToggle`), `attackRider` (`toHit`, `dice`, `damageBonus`, `stackGroup`), `weapon` | Automatic | G1, G2, G7, G8 | To-hit and damage are calculated and rolled. Hit and miss branches are table play. A spell's attack is spell data, not an applied effect. Missing: a rider dice count as a formula (G1), dice on a critical hit only (G2), a mode on the character's own attack rolls (G7), a rider that matches the ability used (G8) |
| `save` | `roll.save` (`ability`, `dc` formula); `value` for a shared DC | Assisted | — | The DC is evaluated and traced; nothing rolls a target's save or applies its result (ADR-018). `roll.area`, `roll.range` and `roll.targets` are text only (Reference). A save the character makes is the `save.<ability>` field, Automatic |
| `damage` | `attack.damage`, `attackRider.dice`, `roll.dice`, `roll.perUnit`; taking damage: the D45 order and `response` | Assisted | G1, G2 | Attack and rider damage carry a type; a `roll` has no damage-type field. Dealing damage to a target is table play. Damage the character takes is applied with defences, reductions and survivals. Healing is the `heal` play action ([sheet-play.md](sheet-play.md)) |
| `temphp` | `response` outcome `temporaryHitPoints`; the `setTemporaryHitPoints` play action | Assisted | — | Only as a damage-response outcome or by hand; no effect grants temporary hit points on use |
| `ieffect2` | `toggle` with its `whileActive` modifiers, `toggleGroup` and `layer` for exclusive forms | Automatic | — | A timed effect on the character is a toggle that lasts some rounds; its modifiers apply while it is on. Its timing and other parts are the rows below |
| `ieffect2` `duration`, `end` | `toggle.duration`, `upkeep.at` (`turnStart` or `turnEnd`) | Assisted | — | Rounds and upkeeps run in a turn the player previews and confirms. `tick_on_caster` has no counterpart: there is only the character's own turn |
| `ieffect2` `conc` | — | Reference | — | Not covered: a toggle does not end when concentration is lost; the concentration save DC is worked out on damage, the toggle is switched off by hand |
| `ieffect2` `stacking`, `parent`, `save_as`, `target_self` | `stackGroup` (strongest applies) | Reference | — | Not covered: effects do not own other effects (no scripting). Everything is on the character, so `target_self` is the only case |
| passive `to_hit_bonus` | `attackRider.toHit` (tag `all`) with `toggle` | Automatic | — | |
| passive `damage_bonus` | `attackRider.damageBonus` or `dice` with `toggle` | Automatic | G1, G8 | Literal `NdM` dice only (G1); no match on the ability used (G8) |
| passive `attack_advantage` | — | Reference | G7 | `rollMode` has no attack-roll target |
| passive `magical_damage`, `silvered_damage` | — | Reference | — | Not covered: damage has a type but no magical or silvered property; text |
| passive `resistances`, `immunities`, `vulnerabilities` | `grant` `damageResistance` / `damageImmunity` / `damageVulnerability` with `whileToggle` | Automatic | — | |
| passive `ignored_resistances` | — | Reference | — | Not covered: a target's defences are out of scope (ADR-018) |
| passive `ac_value`, `ac_bonus` | `modifier` `set` / `replace` / `bonus` on `armorClass` with `toggle`; `armor` | Automatic | G4 | No "no shield" or "no heavy armour" gate; the only equipment gate is `whileArmored` (G4) |
| passive `max_hp_value`, `max_hp_bonus` | `modifier` on `hitPoints` with `toggle` | Automatic | — | An `autoWhen` toggle may not target `hitPoints` (`validate.toggle-auto-target`) |
| passive `save_bonus` | `modifier` `bonus` on `save.<ability>` | Automatic | — | |
| passive `save_adv`, `save_dis` | `rollMode` on `save.<ability>`, always or with `toggle` | Automatic | — | |
| passive `save_adv`, `save_dis` against a condition | `rollMode` on `save.<ability>` with `against` | Assisted | — | The mode is a note the player confirms |
| passive `check_bonus` | `modifier` on `skill.<skill>` | Automatic | — | Skills only: there is no modifier target for a plain ability check |
| passive `check_adv`, `check_dis` | `rollMode` on `skill.<skill>` or `ability.<ability>.check` | Automatic | G7 | No target for initiative alone (G7) |
| passive `dc_bonus` | `modifier` `bonus` on `spellSaveDc`; `value` for a feature DC | Assisted | — | A spell DC bonus is Automatic; a feature's `roll.save.dc` is a formula and cannot follow a toggle |
| `ieffect2` attack interaction | `attack` with `whileToggle` | Automatic | — | An attack that exists only in a form |
| `ieffect2` button interaction | `roll` with `whileToggle`, `alsoChange`, `drawsDown` | Assisted | — | An action that exists only in a form; "Use" spends from its pools |
| `remove_ieffect` | `toggleOff` play action; `upkeep.ifShort: end`; `toggle.duration` running out | Assisted | — | The player ends a form, or an upkeep that cannot be paid ends it |
| `roll` | `roll.dice`, `roll.perUnit`, `roll.bonus` | Assisted | G1, G9, G12 | Rolled and recorded. A roll's result cannot feed a later formula (no run-time variables, ADR-011). Missing: a dice count as a formula (G1), a minimum-roll floor (G9), a reroll (G12) |
| `text` | `automation: reference`; the feature's description; `resource.resourceFull.text` | Reference | — | Not covered by design: prose only, nothing to automate |
| `variable` | `value` (`valueId`, `formula`, read as `VALUE.<valueId>`) | Automatic | — | A named formula over the sheet, not a run-time value |
| `condition` | `toggle` gates (`toggle`, `whileToggle`), `autoWhen: bloodied`, `response.when.atOrBelow` | Assisted | G6 | Not covered as a general branch (no scripting). A response cannot test a save result or a trigger such as damage type (G6) |
| `counter` | `resource`, `roll.resourceId` / `cost` / `variableCost`, `attack.cost`, `toggle.cost`, `upkeep`, `recovery`, `alsoChange`, `drawsDown`; `item.charges`; spell slots from `spellcasting` | Assisted | G3 | Costs are evaluated and spent in one confirmed change ("Spend", "Use"). "Once per turn" is not consumed automatically (G3); a per-turn `allowance` regained at `turnStart` is the nearest |
| `spell` | `resource` plus a spend-only `roll` for the uses | Reference | — | Not covered: a feature cannot add a spell to the character or cast one without a slot. The pool tracks the uses; the spell is named in text and cast by hand |
| `check` | `skill.<skill>` fields; `rollMode` targets `skill.<skill>` and `ability.<ability>.check` (a roll target, not a field) | Assisted | G10 | The character's own checks are Automatic. Not covered: an action asking a target for a check (`roll` has `save` only), and using another ability for a skill (G10) |

## Foundry dnd5e activity types

| Foundry activity | v11 effect(s) | Coverage | Gap | Note |
| --- | --- | --- | --- | --- |
| Attack | `attack`, `attackRider`, `weapon` | Automatic | G1, G2, G7, G8 | As the Avrae `attack` row |
| Cast | `resource` plus a spend-only `roll` | Reference | — | As the Avrae `spell` row |
| Check | `skill.<skill>` fields; `rollMode` on `skill.<skill>` and `ability.<ability>.check` | Assisted | G10 | As the Avrae `check` row |
| Damage | `roll.dice`, `roll.perUnit`, `attackRider.dice` | Assisted | G1, G2 | A roll effect's damage has no typed field; the type is in its label or text |
| Enchant | `attackRider` (tag `weapon`) with `toggle` | Reference | — | Not covered: no effect changes another item's own fields; a toggled rider on every weapon is the nearest |
| Forward | a second `roll` with its own `cost` on the same pool; `response` outcome `offerRoll` | Assisted | — | One roll cannot call another; author each use as its own roll |
| Heal | `response` outcome `regainHitPoints`; `roll.dice` then the `heal` play action | Assisted | — | Healing the character is a roll the player applies; healing others is out of scope (ADR-018) |
| Save | `roll.save` | Assisted | — | As the Avrae `save` row: the DC is worked out; `roll.area`, `roll.range` and `roll.targets` are text only |
| Summon | Companions on Play ([sheet-play.md](sheet-play.md)) | Reference | — | Partly covered: the content v12 `companion` effect (MECH-X item 4, ADR-021) gives a calculated card with its own hit points, attacks and pools; summoned creatures, level-gated upgrades and initiative are not covered. A hand companion is still free text the player fills in (ADR-018) |
| Teleport | `roll.range`, `roll.area` (text) | Reference | — | Not covered: no map or positions (ADR-018) |
| Transform | `toggleGroup` and `toggle` (`layer`, `cost`, `duration`); `modifier` `set` on `ability.*`, `armorClass`, `hitPoints`; `attack` and `roll` with `whileToggle`; `grant` defences with `whileToggle` | Assisted | G5, G11 | A form changes each number by its own modifier; no whole stat block swap. No per-effect ability cap (G5), no condition immunity (G11) |
| Utility | `roll` (with or without `dice`), `toggle`, `value` | Assisted | G9, G12 | A spend-only roll ("Use") or a free roll; no roll floor (G9) or reroll (G12) |

## What this says about the martial classes

Both vocabularies' everyday verbs (attack, damage, a saving-throw DC, a pool spent, a timed form, a bonus or a mode on own rolls) map onto v11. The rows that stop at Reference are the evaluation's gaps, and they fall on Barbarian, Rogue and Monk. Ranked by how many Reference rows each would remove (evaluation §3):

- **Remove the most:** G1 (dice count as a formula on riders and rolls), G4 (no armour, no shield, no heavy armour gates), G5 (per-effect ability-score cap), G6 (responses with dice or halving, and save-result or trigger conditions).
- **Cheap:** G7 (modes on own attack rolls, attacks against the character, initiative alone), G9 (roll floor).
- **Reminders that may stay Reference:** G8 (rider match on the ability used; Dex for tagged non-finesse weapons), G10 (skill ability substitution; climb and swim speeds), G11 (condition immunity), G12 (reroll).
- G2 (crit-only dice) and G3 (once per turn, consumed automatically) are on the plan's original shortlist; the evaluation does not rank them, and each is needed by few features (G2 by one 5.1 feature).

The v12 scope is decided once, from the Lab's coverage report after authoring Barbarian, Rogue and Monk under v11, not class by class.

## The gap ids

| ID | Need |
| --- | --- |
| G1 | Dice count as a formula (or scale) on `attackRider` and `roll` |
| G2 | Rider dice on a critical hit only |
| G3 | Once per turn, consumed automatically |
| G4 | Equipment gates: no armour, no shield, no heavy armour |
| G5 | Per-effect ability-score cap |
| G6 | Responses with dice or halving amounts, and save-result or trigger conditions |
| G7 | `rollMode` on own attack rolls, attacks against the character, initiative alone |
| G8 | Rider match on the ability used; Dex substitution for tagged non-finesse weapons |
| G9 | Roll floor |
| G10 | Skill-check ability substitution; climb and swim speeds |
| G11 | Condition immunity grants |
| G12 | Reroll |
