# ADR-022: Content schema v12, one slice at a time (G6 first: response triggers and amounts; G7: roll modes on attacks and initiative; G1: scaled dice, critical-only and once-per-turn riders; G9: roll floor on checks; G10: check ability, climb and swim speed; G11: condition immunity; G5: ability-score cap)

Status: **accepted** for the slice list and for slices G6, G7, G1, G9, G10, G11 and G5 (owner, 2026-10-28: LIVING_SPECS D76 and B4; D86, D87, D88, D89, D90, D91, D92; G9 and G10 design rulings 2026-11-05; G11 design rulings 2026-11-06; G5 design rulings 2026-11-08). Slice G6 is built on branch `v12-g6`, slice G7 on `v12-g7` (stacked on G6), slice G1 on `v12-g1` (stacked on G7), slice G9 on `v12-g9` (stacked on G1), slice G10 on `v12-g10` (stacked on G9), slice G11 on `v12-g11` (stacked on G10), slice G5 on `v12-g5` (stacked on G11). The warn-or-block judgement for G6's down-conversion (decision 6), the one for G7's (G7 decision 6) and the one for G1's (G1 decision 6), and "once per turn is offered, not tracked" (G1 decision 4), are proposals that await the owner.
Date: 2026-11-08

## Context

The "Any SRD character" milestone (D76) needs engine gaps that content schema v11 cannot express. ADR-021 already opened v12 with optional members that earlier slices added (a weapon's `bonus`, item mods, companions, spell `tags`, `replaceable`, `payment`). The owner approved finishing v12 as a series of slices, each a small and complete change from model to docs, rather than one large schema change.

The first gap (G6) is in the damage panel. A `response` effect (v11, ADR-018 and D45) reduces or changes a hit, but it has no idea how the hit came about and can only subtract a fixed amount. Features that matter here:

- A feature that halves damage from one kind of hit (an attack, a fall, or a saving throw that already halves on a success) must not be offered for the others.
- A feature that halves, or takes off everything, cannot be written as a fixed `amount`.
- A feature that takes off a rolled amount (a die plus a number) has nowhere to put the player's roll.
- A feature that applies by default should be pre-ticked, and one that cannot be used while a condition holds should say so.
- A feature that replaces another one should not leave both on the sheet.

## Decision

1. **Six optional members on `response`, all v12** (`ResponseEffect.V12Keys`: `trigger`, `share`, `dice`, `automatic`, `unlessCondition`, `supersedes`). A revision without them keeps its schema version and its bytes.
   - `trigger`: `{ from: attack | fall | save, ability?, result?, damageTypes? }`. `ability` and `result` (`success` or `failure`) are required with, and only allowed with, `from: save`. A save trigger matches only a save that halves on a success.
   - `share`: `half` or `all`, damage reduction only, instead of `amount`. Half leaves `floor(d / 2)`; all leaves 0.
   - `dice`: a dice expression such as `1d10`, damage reduction with `amount` only. The reduction is the player's rolled total plus `amount`.
   - `automatic`: `true` pre-ticks the response in the damage panel. Damage reduction only, no cost. The engine still applies only what the player chose.
   - `unlessCondition`: a condition key. The response is on the sheet but not available while the character has that condition. A condition that includes incapacitated (paralyzed, petrified, stunned, unconscious) counts as incapacitated.
   - `supersedes`: `{ content?, responseId }`. While this response is available (its cost, pool, toggle, formulas and `unlessCondition` all allow it) and the link is not part of a loop, the named one is left off the sheet; an upgrade the character cannot use leaves what it replaces in place. Validation refuses a loop (`validate.response-supersedes`, including one through installed content). A loop that still reaches a sheet (older catalog, content installed later) is ignored: every response in it stays, with the sheet diagnostic `response.supersedes-cycle`.
   - `dice` must not be able to total below 0 (`1d4-5`, `-1d6`): `validate.response-dice`. The engine also never lets one response add damage.
2. **The hit is described on the request only, and never stored.** `DamageEvent` gains `From` (attack, fall or save, or unknown) and `Save` (`DamageSave`: the ability, whether it succeeded, whether it halves). `Save` is allowed only with `From == Save`. The service refuses an inconsistent pair with `damage.invalid-source`. Nothing about the hit is written to the character.
3. **Matching.** A response with no trigger matches every hit, as in v11. An unknown source (the player did not say) still offers attack and fall triggers, so the player judges, as in v11, but never a save trigger. A known source offers only its own kind. `damageTypes` needs the hit's type in the list; an untyped hit never matches. A trigger gates every phase of the response.
4. **The order** (amends D45): the save's half (`floor(d / 2)` on a success when the save halves), then the chosen `share` reductions in sheet order on the running amount, then flat `amount` reductions (plus any rolled `dice`), then defences, temporary hit points and hit points (`Resolve`, unchanged), then survivals and thresholds as before. The damage plan's reduction counts what responses took off, not what the save took off. As a result the panel's "Reduced by N" never exceeds the damage that reached the responses.
5. **Rolls.** A chosen dice response must carry the player's `Rolled` total within the expression's minimum to maximum, and only a dice response may carry one. Otherwise the preview and the confirm report `damage.response-roll`, one note per response, and nothing applies.
6. **Down-conversion** (ADR-023, "never silently stronger"). At the 12 to 11 rung a response that uses `trigger`, `share`, `dice`, `unlessCondition` or `supersedes` is dropped whole and listed (`below.effect-unsupported`): each of these makes the sheet offer less than the content intended, so keeping the response without the member would make the copy stronger or wrong. `automatic` alone drops just the key (`below.v12-key`), because it only pre-ticks. When the dropped response had a `supersedes` naming a response of the same revision, that response is dropped too (`below.effect-unsupported`, its own entry): otherwise the copy would offer the weaker response the original hid, never stronger. No new code is added. **This judgement is proposed; the owner decides** whether any of the new members should instead warn or block (ADR-023, consequences).
7. **Numbers.** This ADR is the one reserved by D76. The decision row is D86.

### The slice list

G6 was built first, G7 second, G1 (with G2 and G3) third, G9 fourth, G10 fifth, G11 sixth and G5 seventh (below). The remaining slice is G4 (the gap ids are in [effect-vocabulary-coverage.md](../features/effect-vocabulary-coverage.md)). Each extends this ADR with its own decisions and evidence, keeps every new member optional, and adds its own lowering rule.

**0.8.0 waits for every slice** (owner ruling, 2026-11-04): the bundled classes get new v12 revisions only when v12 is complete, and no release is cut while v12 is open. Until then G6 and G7 sit on their branches and, once merged, in `main`.

## Consequences

- Content can say which hits a response answers, take off a share or a rolled amount, and be pre-ticked, unavailable under a condition, or replaced by another response. Existing responses behave as before.
- The hit points panel asks "How did it hit?" (attack, fall, or saving throw with its result and whether it halves). Automatic responses are pre-ticked, and a dice response takes the player's roll. The Homebrew studio can author all six members.
- The damage panel's "Reduced by N" is now capped at the damage after the save. In v11 the figure could show a reduction larger than the damage.
- A revision that uses any of the members needs content schema 12; below it the validator refuses them (`validate.requires-v12`), and a package that holds one warns `package.needs-newer-build`.
- New validator codes: `validate.response-trigger`, `validate.response-share`, `validate.response-dice`, `validate.response-automatic`, `validate.response-condition`, `validate.response-supersedes`. New note: `damage.response-roll`. New refusal: `damage.invalid-source`.
- Every member is automatic in the capability registry, so no new registry row was needed.
- The hit's source is asked each time. A player who never says how a hit came sees the v11 behaviour.

## Not doing

- **Hit-time formula variables** (a `DAMAGE` name readable by formulas): amounts that depend on the hit would need the engine to evaluate formulas per hit. Shares, rolled dice and flat amounts cover the cases in view.
- **Storing the hit's source on the character:** the hit is a request detail; storing it would add character schema and a stale value.
- **A redirect or reflect damage roll** (a response that turns the reduction into an attack of its own): the app still does not roll for other creatures (ADR-018). It stays by hand.
- **Applying a saving throw for the player:** the player says what the save did.

## Alternatives rejected

- **A new effect kind for halving:** the `response` already has the choice, cost and outcome machinery; a second kind would duplicate it.
- **Trigger by name** (such as a feature name): the tool matches data, not names.
- **Making `share` a formula:** half and all are the only shares in view, and a closed list validates and down-converts exactly.
- **Applying `automatic` responses without a tick:** the player must confirm every change to their hit points (the engine applies only what is chosen).
- **Keeping the response and dropping the trigger on down-conversion:** the older build would offer the response against every hit, which is stronger than the content says.

## Evidence

- `tests/RulesCore.Tests/ResponseV12SchemaTests.cs` and `tests/RulesCore.Tests/ResponseV12ValidationTests.cs` (the six members, the version rung, the validator codes), and the JSON schema in `docs/schemas/content-revision.v12.schema.json`.
- `tests/RulesCore.Tests/ResponseV12SheetTests.cs` (availability under `unlessCondition`, `supersedes`, the sheet's `ResponseValue`).
- `tests/RulesCore.Tests/DamagePipelineV12Tests.cs` (matching, the order, shares, rolled dice, `damage.response-roll`) and `tests/RulesCore.Tests/Properties/DamagePipelineProperties.cs` (a reduction never exceeds the damage; the result never gains hit points).
- `tests/RulesCore.Tests/DownConvertV12Tests.cs` (drop whole, drop the key) over `tests/RulesFixtures/downconvert/requires-vN.json`, with `DownConvertCoverageTests` unchanged at its count.
- `tests/AppService.Tests/DamageResponseV12ServiceTests.cs` (the preview and the play command with the hit's source and save, `damage.invalid-source`).
- UI: `src/Ui/src/components/DamagePanel.test.tsx` ("How did it hit?", pre-ticked automatic responses, the roll box) and `src/Ui/src/components/HomebrewStudioResponses.test.tsx` (authoring the members).
- The original fixture pack `tests/RulesFixtures/fixture-pack-responses-v12.json`.

## Slice G7: roll modes on attacks and initiative

The second gap is in the roll modes of D43 (`rollMode`, v11). A v11 `rollMode` reaches a saving throw, a skill or an ability check, but not the character's own attack rolls, not initiative on its own, and not the attack rolls other creatures make against the character. Features that matter here:

- A feature that gives advantage on some of your attacks (by tag, or by the ability the attack uses) while a stance is on.
- A feature that gives advantage on initiative, and nothing else.
- A feature that gives other creatures disadvantage on their attacks against you, or that stops them having advantage while it holds.
- A mode that is switched off while the character has a condition.

### Decision (G7)

1. **Three new targets (v12):** `attack` (the character's own attack rolls), `initiative` (initiative only; `ability.dex.check` still reaches it too, as in v11) and `attacksAgainst` (attack rolls other creatures make against the character).
2. **Four optional members on `rollMode`, all v12** (`RollModeEffect.V12Keys`: `tags`, `ability`, `unlessCondition`, `prevent`).
   - `tags`: required with, and only allowed with, target `attack`; non-empty, each tag listed once. It matches as attack riders match: `all`, or any tag in the attack's tag list.
   - `ability`: target `attack` only. The attack must use that ability.
   - `unlessCondition`: any target, a condition key. While the character has the condition, the effect is left out (not listed, like a toggle that is off). A condition that includes incapacitated (paralyzed, petrified, stunned, unconscious) counts as incapacitated.
   - `prevent`: target `attacksAgainst` only. `true`: attacks against the character can't have `mode`, and the sources of that mode are ignored. `false` is allowed and still needs v12.
   - `against` (v11) stays allowed with `attack` and is refused with `initiative` and `attacksAgainst` (`validate.roll-mode-against`).
3. **Attack tags.** A weapon attack carries `weapon` and `melee` or `ranged`; a feature attack carries its own tags plus `melee` or `ranged`; a spell attack roll carries `spell` and `melee` or `ranged`. Companion attacks and custom rolls get no automatic mode: they are not the character's own rolls.
4. **Attacks against you.** `CharacterSheet.attacksAgainst` is a summary (null when nothing reaches it). Its automatic mode combines the automatic reasons after removing every mode an automatic `prevent` reason names; a prevent reason contributes no mode itself. The app still never rolls for other creatures (ADR-018): the line is shown on the sheet, and the capability registry's `rollMode` row is Automatic and names attacks against you.
5. **Rolling.** Weapon, feature and spell attack rolls combine the player's mode with the automatic reasons and report the automatic mode, its sources and the note exactly as a field roll does. Custom and companion rolls are unchanged. Initiative already rolls through the field branch. `CharacterSheet.attackModes` and `AttackEntry.mode` (null on a companion attack) carry the reasons to the UI.
6. **Down-conversion** (ADR-023, "never silently stronger"). At the 12 to 11 rung a `rollMode` that uses a v12 target or a v12 member is lowered by whether it **helps** the character. It helps when its target is the character's own (`attack`, `initiative` or a v11 target) with advantage, or when it targets `attacksAgainst` with disadvantage and no `prevent`, or with advantage and `prevent: true`. A helping one is dropped whole and listed (`below.effect-unsupported`), because the older build cannot calculate it and the copy is only weaker. Any other one blocks (`below.roll-mode-disadvantage`, the code v11 already uses for a mode that hinders): dropping it would make the copy stronger, and the blocked copy omits it as its one change. `prevent: false` alone on an otherwise v11 effect drops just the key (`below.v12-key`). No new code is added and the down-conversion coverage count stays 40. **This judgement is proposed; the owner decides** whether any of it should instead warn or block (ADR-023, consequences).
7. **Numbers.** No new ADR. The decision row is D87.

### Consequences (G7)

- Content can give advantage or disadvantage on the character's attacks by tag or ability, on initiative alone, and on attacks against the character, and can switch a mode off while a condition holds. Existing roll modes behave as before.
- Attack buttons show the automatic mode; Play shows an "Attacks against you" line near Armor Class. The Homebrew studio can author all four members and the three targets, and reports members that do not fit the chosen target with a Remove button.
- A revision that uses any of the targets or members needs content schema 12; below it the validator refuses them (`validate.requires-v12`), and a v11 revision whose rollMode names a v12 target stays an unknown effect, its bytes and hash unchanged. A package that holds one warns `package.needs-newer-build`.
- New validator codes: `validate.roll-mode-tags`, `validate.roll-mode-ability`, `validate.roll-mode-condition`, `validate.roll-mode-prevent`. `validate.roll-mode-target` and `validate.roll-mode-against` are reused.
- The capability registry gains four rows (`tags`, `ability`, `unlessCondition`, `prevent`), all Automatic; the `rollMode` row stays Automatic and its text now names attack rolls, initiative and attacks against you.

### Not doing (G7)

- **"Next attack roll only"** (a mode that switches its toggle off after one roll): the player switches the toggle off.
- **"If you haven't moved"** and similar conditions: the player judges, with the toggle.
- **A prevent on the character's own rolls:** only attacks against the character have it.
- **A mode on damage or non-d20 rolls.**

### Evidence (G7)

- `tests/RulesCore.Tests/RollModeV12SchemaTests.cs` and `RollModeV12ValidationTests.cs` (the targets, the four members, the version rung, the validator codes), and the JSON schema in `docs/schemas/content-revision.v12.schema.json`.
- `tests/RulesCore.Tests/RollModeV12SheetTests.cs` (attack tags and ability, initiative, `attacksAgainst` with `prevent`, `unlessCondition`, companion attacks).
- `tests/RulesCore.Tests/DownConvertV12Tests.cs` (the rollMode cases: drop whole, block, drop the key) over `tests/RulesFixtures/downconvert/requires-vN.json`, with `DownConvertCoverageTests` unchanged at 40.
- `tests/AppService.Tests/RollModeV12ServiceTests.cs` (weapon, feature and spell attack rolls with the player's mode and the automatic reasons).
- UI: `src/Ui/src/components/HomebrewStudioRollModes.test.tsx` (authoring), `SheetSummary.test.tsx` (the "Attacks against you" line) and `ActionsPanel.test.tsx` (the attack buttons' automatic mode).
- The original fixture pack `tests/RulesFixtures/fixture-pack-roll-modes-v12.json`.

## Slice G1: scaled dice, critical-only and once-per-turn riders

The third gap is in the dice of D43 (`attackRider.dice`, `roll.dice`, v11), which are literal `NdM`. Features that matter here:

- Extra damage dice whose count follows a class level (a count that rises every few levels).
- Extra dice added only when the hit is a critical hit.
- Extra dice on one hit a turn, which the player chooses to add.

### Decision (G1)

1. **`scaledDice` (v12, `attackRider` and `roll`):** `{ "count": <formula>, "die": <formula>? }`, evaluated in the declaring content's context (a `SCALE.` reference needs v9, a `VALUE.` reference v11). It is exclusive with `dice` on the same effect (`validate.scaled-dice`). `die` is required on a roll; on a rider, no `die` means the attack's own damage die (the weapon's, the versatile die for a two-handed roll, a feature attack's evaluated die), and such a rider may not have a `stackGroup`.
2. **Evaluated range.** A count of 0 adds no dice: a rider adds the trace note `rider.no-dice` and is not listed when it has no other part; a roll has no dice, so the roll command gives `roll.no-dice` (or rolls the spell's dice when the roll casts a spell). A count below 0 or above 100, a die that is not a d4 to d12, or a failing formula is reported as `rider.dice-invalid` on a rider (the rider is left out of that attack, the attack stays listed and rollable) and as `roll.dice-invalid` on a roll (the roll command refuses with that code). Nothing throws.
3. **`critical: true` (v12, rider):** its dice are added only to a damage roll the player marks as critical, and are rolled once, never doubled. It needs `dice` or `scaledDice` and refuses `toHit`, `damageBonus`, `stackGroup` and `oncePerTurn` (`validate.rider-critical`).
4. **`oncePerTurn: true` (v12, rider):** its dice are not in the attack's damage. Each weapon or feature-attack damage roll offers it and the player adds it; added dice are part of the formula, so a critical hit doubles them. **TomeStack does not track turns; the player decides when it applies.** It needs `dice` or `scaledDice` and refuses `toHit`, `damageBonus` and `stackGroup` (`validate.rider-once-per-turn`); a rider with both flags is reported as `validate.rider-critical`. **This judgement is proposed; the owner decides.** `false` is allowed for both flags, still needs v12, and counts as absent.
5. **Rolling.** `RollCommand` gains `WithRiders` (a list of content and effect id picks). It is allowed only on a weapon or feature-attack damage roll (`roll.riders-not-allowed`); each pick must be a once-per-turn rider on that entry (`roll.rider-unknown`); duplicates collapse. Picked dice are appended to the damage formula. On a critical roll the entry's critical riders' dice are added undoubled. The field roll branch takes automatic reasons as the other branches do (a G7 leftover).
6. **Down-conversion** (ADR-023, "never silently stronger"). At the 12 to 11 rung a rider with any of `scaledDice`, `critical` or `oncePerTurn` is **dropped whole** and listed (`below.effect-unsupported`): keeping it without the flag would make it stronger, and a v11 rider cannot scale; a rider that also lowers the attack (a `toHit` or `damageBonus` that can be negative) blocks the export instead (`below.effect-unsupported`), since dropping it would remove the penalty. A roll with `scaledDice` loses only that key (`below.v12-key`) when it still has a cost; when nothing a v11 roll accepts would remain (a v11 roll needs dice, a cost or a spell) it is dropped whole (`below.effect-unsupported`). A roll that casts a spell still blocks the export, as in G6. Both are weaker, not stronger. No new code: the down-conversion coverage count stays 40. **This judgement is proposed; the owner decides** (ADR-023, consequences).
7. **Numbers.** No new ADR. The decision row is D88.

### Consequences (G1)

- Content can scale extra dice with a level, add dice on a critical hit only, and offer dice once per turn. Existing riders and rolls behave as before, and a revision without the new members keeps its version, bytes and hash. A v11 revision whose rider carries one of them keeps it as unknown data and applies with its v11 `dice`.
- Attack buttons offer once-per-turn riders on damage rolls. The Homebrew Studio authors all three: a rider's dice can be "By formula" (count and optional die), with "Only on a critical hit" and "Once per turn, added when you choose" boxes (each disables the other). Switching a rider to "By formula" clears its stack group.
- A revision that uses any of the members needs content schema 12 (`validate.requires-v12`). A package that holds one warns `package.needs-newer-build`.
- New codes: `validate.scaled-dice`, `validate.rider-critical`, `validate.rider-once-per-turn`, `rider.dice-invalid`, `rider.no-dice` (a trace note), `roll.dice-invalid`, `roll.riders-not-allowed`, `roll.rider-unknown`.
- The capability registry gains rows for `scaledDice` and `critical` on riders and `scaledDice` on rolls (Automatic) and `oncePerTurn` (Assisted).

### Not doing (G1)

- **A tracked "used this turn" state:** it needs character data past schema 11. The player decides.
- **Dice that depend on a roll's result,** and a name for the attack's die inside formulas.
- **Folding `scaledDice` to a literal when lowering,** and crit-only `roll` effects.
- **Detecting a critical hit from the d20:** the player still marks a critical damage roll.

### Evidence (G1)

- `tests/RulesCore.Tests/ScaledDiceV12SchemaTests.cs` and `ScaledDiceV12ValidationTests.cs` (the members, the version rung, the validator codes, a v11 revision keeping them as unknown data), and the JSON schema in `docs/schemas/content-revision.v12.schema.json`.
- `tests/RulesCore.Tests/ScaledDiceV12SheetTests.cs` (count 0, out-of-range, die-less riders on a versatile roll, stack groups).
- `tests/AppService.Tests/ScaledDiceV12ServiceTests.cs` (critical-only and once-per-turn rolling, picks refused, `roll.dice-invalid`, `roll.no-dice`).
- `tests/RulesCore.Tests/DownConvertV12Tests.cs` (rider dropped whole; roll key dropped or roll dropped) with `DownConvertCoverageTests` unchanged at 40, and `CapabilityRegistryTests.cs` (the registry rows).
- UI: `src/Ui/src/components/HomebrewStudioAttacks.test.tsx` and `HomebrewStudioRolls.test.tsx` (authoring), `ActionsPanel.test.tsx` (the once-per-turn offer on damage rolls).

## Slice G9: roll floor

The fourth gap is a minimum on a check: content that says "when you make this kind of check, a d20 that lands low counts as a set number", or "a check total below a set value counts as that value". v11 has no way to say it (`rollMode` only changes how many d20s are rolled).

### Decision (G9)

1. **A new kind `rollFloor` (v12)** (owner ruling, 2026-11-05: a new kind, not `rollMode` members): `{ "target", "face" | "total", "proficient"? }`. `target` is `check` (every ability-check field: each ability modifier, every skill, initiative), `ability.<x>.check` (that ability's modifier, its skills, and initiative for Dex) or `skill.<s>` (a known skill). **Checks only:** no saves, attacks or other targets (`validate.roll-floor-target`). Exactly one of `face` (an integer 2 to 20: a d20 below it counts as it) or `total` (a formula: a total below it counts as it, evaluated in the declaring content's context, so `SCALE.` needs v9 and `VALUE.` v11) (`validate.roll-floor-value`). There is no toggle, condition or tag.
2. **`proficient: true`** reaches only fields marked proficient or expertise (owner ruling): a bare ability check and initiative are never reached. `false` counts as absent but still needs v12.
3. **Sheet.** `DerivedValue.Floor` (`RollFloorSummary`, with `RollFloorReason` entries in content order) holds the highest face and the highest total, separately. Each reason adds one `"floor"` trace step after the field's own steps. A failing total formula leaves that reason out with `effect.invalid-formula`; the sheet still calculates. A total that evaluates below 1 still counts (a no-op floor). Each floor trace step has `Amount` = the floor and `Result` = the field value unchanged. The displayed field value never changes.
4. **Rolling.** Only a field roll gets a floor; attacks, damage, saves, death saves, roll effects, spells and custom dice never do. The face raises each d20 of the main expression below it, before keeping, and both raw faces stay visible (`DieResult.Rolled`). The total is floored after modifiers. `RollRecord.Unfloored` and `Floor` keep the raw total and the floor. The roll text reads "7 → 10 (floor)" for a die and "total 12 raised to 16 (source)" for a total.
5. **Down-conversion** (ADR-023, "never silently stronger"). At the 12 to 11 rung a `rollFloor` is **dropped whole** and listed (`below.effect-unsupported`) (owner ruling): it only raises a roll, so the copy is weaker, and a v11 effect cannot carry it.
6. **Numbers.** No new ADR and no schema bump (`ContentRevision.CurrentSchemaVersion` stays 12; nothing new is stored on a character). The decision row is D89.

### Consequences (G9)

- Content can set a minimum d20 face or a minimum total on checks, optionally only on skills the character is proficient in. A revision without it keeps its version, bytes and hash; a v11 revision holding a `rollFloor` keeps it as unknown data and ignores it.
- The sheet lists the floor on each field it reaches. The Homebrew Studio authors it (a "Floor on" choice of "The d20" or "The total", an "At least" value that is a number from 2 to 20 or a formula, and an "Only when proficient" box).
- A revision that uses it needs content schema 12 (`validate.requires-v12`). A package that holds one warns `package.needs-newer-build`.
- New codes: `validate.roll-floor-target`, `validate.roll-floor-value`, `validate.roll-floor-automation` (a roll floor is always automatic and always on; validation refuses other automation or timing, and the sheet skips such stored content).
- The capability registry gains a `rollFloor` row (Automatic, "Applied when a check field is rolled"). The down-conversion coverage count is unchanged by the new drop.

### Not doing (G9)

- **Saves, attacks and tool checks** (tool checks are not sheet fields).
- **Flooring the displayed field value,** toggles and conditions.
- **A floor on rolls made outside the app.**

### Evidence (G9)

- `tests/RulesCore.Tests/RollFloorV12SchemaTests.cs` and `RollFloorV12ValidationTests.cs` (the members, the version rung, the validator codes).
- `tests/RulesCore.Tests/RollFloorV12SheetTests.cs` (highest face and total, proficient-only marks, content order, failing formula, trace steps).
- `tests/RulesCore.Tests/RollFloorRollerTests.cs` (each d20 raised before keeping, total floored after modifiers).
- `tests/AppService.Tests/RollFloorV12ServiceTests.cs` (field rolls floored, other roll kinds never) and `tests/RulesCore.Tests/DownConvertV12Tests.cs` (dropped whole).
- UI: `src/Ui/src/components/HomebrewStudioRollFloors.test.tsx` (authoring) and `rollText.test.ts` (the roll text).
- The original fixture pack `tests/RulesFixtures/fixture-pack-roll-floor.json`.

## Slice G10: check ability, climb and swim speed

The fifth gap is two small things content could not say in v11: "this skill may use another ability" (the skill takes whichever of the two modifiers is higher), and a climbing or swimming speed, including one that is a share of the walking speed ("a climb speed equal to your walking speed").

### Decision (G10)

1. **A new kind `checkAbility` (v12)** (owner ruling, 2026-11-05): `{ "target", "ability" }`. `target` is `skill.<s>` (a known skill) or `ability.<x>.check`, meaning every skill whose **printed** ability is x. It never reaches a bare ability check, initiative or a save. `ability` is the ability the skill may use, written as a roll mode's `ability` is. It must differ from the printed ability of every skill it reaches (`validate.check-ability-target`, `validate.check-ability-ability`). There is no toggle, condition or tag: a situational substitution stays text.
2. **"May use": the mode first, then the modifier** (owner ruling, 2026-11-05). For each candidate (the printed ability and every `checkAbility` candidate for that skill) the sheet works out the automatic roll mode the skill's checks would get from the active roll modes. The best mode wins: advantage, then normal, then disadvantage (advantage and disadvantage together are normal). Among candidates with the same mode the strictly higher modifier wins, and a tie goes to the printed ability, then content order. So a raging hero with Strength 14 and Dexterity 16 keeps Strength for Athletics when Rage gives Strength checks advantage, and uses Dexterity when not raging. Roll modes and G9 floors follow the ability the skill ended up using: with a Dexterity-based Athletics, `ability.dex.check` advantage reaches it and `ability.str.check` disadvantage no longer does. A skill with no substitution keeps its printed ability. Known limit: roll floors are not compared when choosing.
3. **Always on.** `automation` must be `automatic` and `timing` `always` (`validate.check-ability-automation`); the sheet skips stored content that breaks this, and the studio hides the two selects. This is the G9 `rollFloor` pattern.
4. **Sheet.** When a substitute wins, the skill's base step reads "{Label} starts at the {Ability} modifier ({source} lets you use {Ability} instead of {Printed})", carries the content origin and takes the chosen modifier as an input; the skill's reads become that modifier and the proficiency bonus. A substitute that cannot win changes nothing: same value, same steps, no new input. Passive scores follow the skill.
5. **Climb and swim speed.** Two new fields, `speed.climb` ("Climb speed") and `speed.swim` ("Swim speed"), in feet, base 0 ("No climbing speed unless content gives one"). Content may target them only from v12 (`validate.requires-v12`). The sheet shows them in the Combat group and in the summary after Fly ("Climb 30 ft."), hidden at 0 unless overridden.
6. **`SPEED`.** A speed modifier on `speed.climb`, `speed.swim` or `speed.fly` may read `SPEED` (the walking speed, as in `SPEED / 2`); it needs v12 (`validate.requires-v12`) and stays an unknown identifier everywhere else (armor class, skills, any other target, a v11 revision). The formula preview has no value for it. The cycle the first design guarded against cannot arise: formulas read only ability scores and modifiers, the proficiency bonus, levels, class levels and `SPEED`, never skills, so no `checkAbility` can feed back into its own inputs.
7. **Down-conversion** (ADR-023, owner rulings). At the 12 to 11 rung: a `checkAbility` is **dropped whole** and listed (`below.effect-unsupported`). Every **modifier** on `speed.climb` or `speed.swim` is dropped (`below.unknown-target`): the older version has no such field, so the copy has no such speed. A **restriction** on either **blocks** (`below.restriction-lifted`; follow-up ruling, 2026-11-05): dropping it would lift a limit on the character. A `speed.fly` modifier whose value reads `SPEED` is dropped when it is a `bonus` or `replace` with a formula that cannot be negative (the copy is weaker), and blocks when it is a `set` or the formula might be negative (`below.value-formula`).
8. **Registry.** `checkAbility` is Automatic ("Applied to the skill's value on the sheet").
9. **Numbers.** No new ADR and no schema bump (`ContentRevision.CurrentSchemaVersion` stays 12; nothing new is stored on a character). The decision row is D90.

### Consequences (G10)

- Content can let a skill, or every skill of one ability, use another ability, and can give a climb or swim speed, including one tied to the walking speed. A revision without any of it keeps its version, bytes and hash; a v11 revision holding a `checkAbility` keeps it as unknown data and ignores it. Every sheet gains two fields, `speed.climb` and `speed.swim`, at 0; nothing else on a v11-style sheet moves.
- The Homebrew Studio authors the substitution (a rule titled "Use another ability for a skill", button "Add check ability"; a "Skill" choice of "Every Strength skill" and so on, then each skill; a "Uses instead" choice) and shows a hint in the modifier editor: "You can use SPEED (your walking speed) in a climb, swim or fly speed modifier, such as SPEED / 2."
- A revision that uses any of it needs content schema 12 (`validate.requires-v12`). A package that holds one warns `package.needs-newer-build`.
- New codes: `validate.check-ability-target`, `validate.check-ability-ability`, `validate.check-ability-automation`.
- The capability registry gains a `checkAbility` row. The down-conversion coverage count is unchanged by the new drops.

### Not doing (G10)

- **Situational substitutions** (toggles, conditions: "while climbing").
- **Saves, initiative and bare ability checks** as targets, and a forced (lower) ability.
- **Constitution as a target.** No skill is printed with it, so `ability.con.check` is refused and the studio does not offer "Every Constitution skill". The targets are every Strength, Dexterity, Intelligence, Wisdom or Charisma skill, or one skill.
- **Exports carrying the substitution.** SheetExport and the Foundry export keep the printed ability; neither format has a field for a substituted ability.
- **Jump distance** (no field) and **`SPEED` outside speed modifiers.**
- **Re-authoring bundled SRD features.** The correction to the coverage notes: Acrobatic Movement (5.2.1) is not a G10 feature (it grants no speed and no check, only vertical surfaces and liquids while unarmored) and stays text. Of the two Second-Story Work versions, 5.1 has no speed; only the 5.2.1 climb speed is expressible, and its jump line is not.

### Evidence (G10)

- `tests/RulesCore.Tests/CheckAbilityV12SchemaTests.cs` and `CheckAbilityV12ValidationTests.cs` (the members, the version rung, the validator codes).
- `tests/RulesCore.Tests/CheckAbilityV12SheetTests.cs` (highest wins, tie, step text, roll modes and floors follow the ability used) and `ClimbSwimV12Tests.cs` (the fields, `SPEED` scope).
- `tests/RulesCore.Tests/DownConvertV12Tests.cs` (the drops and the blocks).
- `tests/AppService.Tests/CheckAbilityV12ServiceTests.cs` and `CheckAbilityV12SchemaFileTests.cs` (the sheet through the service; the published JSON schema).
- UI: `src/Ui/src/components/HomebrewStudioCheckAbility.test.tsx` (authoring), `CharacterSheet.test.tsx` and `SheetSummary.test.tsx` (climb and swim speed shown).
- The original fixture pack `tests/RulesFixtures/fixture-pack-check-ability.json`.

## Slice G11: condition immunity

The sixth gap is "you are immune to the charmed condition", always or only while a mode (a rage, a form) is on. Content could only say it in text. This is the grant the bundled Barbarian's and Monk's immunity lines need once C2-6 re-authors them.

### Decision (G11)

1. **A new grant kind `conditionImmunity` (v12)** (owner rulings, 2026-11-06), with a new member `condition` (JSON `condition`): one key of the 14 conditions, compared exactly (lower case). It is required on this kind and refused on every other kind. The kind takes no `target`, `content` or `damageType`; `level` and `onlyAs` work as on a damage defence.
2. **Older revisions never bind it.** In a v10 or v11 revision (and below) a grant of this kind stays `unknown`, byte for byte with its hash; a `condition` key on any other grant stays extension data. Only a v12 revision types it.
3. **Always on, or while a toggle is on.** It is calculated only when `automation` is `automatic` and `timing` is `always`, gated by `level`, `onlyAs` and `whileToggle` (the same or another content's toggle) exactly as a damage defence is. A manual or timed one is not applied and its feature shows Assisted; it is not an error. There is no `unlessCondition` or other condition gate on the grant.
4. **Play: suppress, do not block.** An immune condition is not held. `ConditionKeys.Holds` takes the immunity set: an immune key is not held, and "incapacitated" is implied only by a held condition that is itself not immune. The G6 response gates and the G7 roll-mode `unlessCondition` gates read this. The player can still record the condition (`addCondition` and `removeCondition` are accepted) and the stored play state never changes because of an immunity, so switching the toggle off shows the condition held again with no write. The G7 `against` lists are untouched.
5. **Sheet.** `conditionImmunities` lists each condition with its sources, in the order of the 14 keys, sources grouped and distinct; it is absent when there are none, so a sheet without the grant does not change. The grant is not a proficiency. The immunity set is worked out once per calculation, before the roll modes.
6. **Which conditions.** Any of the 14. Exhaustion (a level) and disease (not a condition) stay text.
7. **Down-conversion** (ADR-023, owner ruling, 2026-11-06). At the 12 to 11 rung the grant is **dropped whole** and listed (`below.effect-unsupported`: "'{name}': its immunity to the {condition} condition; the feature stays as text."). A copy without it is only weaker, so nothing blocks.
8. **Registry.** The existing grant row's text reads "Proficiencies, expertise, content, damage defences and condition immunities calculate"; it keeps its evidence class and gains no second row.
9. **UI copy and numbers.** The Play legend reads "Conditions (reminders; a condition you are immune to has no effect)" and an immune condition's chip reads "{Label} (immune)", still toggleable. The sheet's Defences panel gains a read-only "Condition immunities" list ("{Label} ({sources})"). The studio has "Add condition immunity" (row "Condition immunity") with a "Condition" choice of the 14 labels and the "Only while" choice (shown when a toggle is available); its Automation select is hidden, as for every grant, so a manual or timed immunity comes only from JSON or import and shows as Assisted. No new ADR and no schema bump (`ContentRevision.CurrentSchemaVersion` stays 12; `Character.CurrentSchemaVersion` stays 11). The decision row is D91.

### Consequences (G11)

- Content can grant immunity to one condition, optionally while a toggle is on, and the sheet and Play honour it. A revision without it keeps its version, bytes and hash; a v11 revision holding one keeps it as unknown data and ignores it.
- A revision that uses it needs content schema 12 (`validate.requires-v12`). A package that holds one warns `package.needs-newer-build`.
- New codes: `validate.grant-condition` (missing or not one of the 14 keys), `validate.grant-condition-kind` (`condition` on another kind) and `validate.grant-condition-fields` (`target`, `content` or `damageType` on the new kind).
- Print and the summary's conditions line list the stored conditions, as before.

### Not doing (G11)

- **Exhaustion or disease immunity.**
- **Ending a held condition automatically.** An effect such as "the condition ends when you enter the mode" stays a player action (text).
- **Blocking `addCondition`.**
- **The G7 `against` lists**, which stay player-applied.
- **Re-authoring bundled SRD features** (the Barbarian's Mindless Rage, the 5.1 Monk's Purity of Body): C2-6. Purity of Body's disease line stays text.

### Evidence (G11)

- `tests/RulesCore.Tests/ConditionImmunityV12SchemaTests.cs` and `ConditionImmunityV12ValidationTests.cs` (the member, the version rung, the validator codes, the published JSON schema).
- `tests/RulesCore.Tests/ConditionImmunityV12SheetTests.cs` (the listing, toggle gating, no proficiency) and `ConditionImmunityV12GateTests.cs` (`Holds`, response and roll-mode gates, implied incapacitated).
- `tests/RulesCore.Tests/DownConvertV12Tests.cs` (the drop).
- `tests/AppService.Tests/ConditionImmunityV12ServiceTests.cs` and `SchemaTests` (the sheet through the service; play state is not rewritten).
- UI: `src/Ui/src/components/HomebrewStudioConditionImmunity.test.tsx` (authoring), `DefencesPanel` and `PlayPanels` tests.
- The original fixture pack `tests/RulesFixtures/fixture-pack-condition-immunity.json`.

## Slice G5: ability-score cap

The seventh gap is "this increase can raise a score above 20, up to a named maximum". The usual rule stops every ability-score increase at 20 (a rules constant, `CharacterCalculator.AbilityScoreIncreaseCap`); content could only say it in text. This is the grant the bundled Body and Mind feature needs once C2-6 re-authors it.

### Decision (G5)

1. **Shape** (owner rulings, 2026-11-08). A new optional member `ModifierEffect.Cap` (`int?`, JSON `cap`), trailing after `toggleContent`, constant `ContentRevision.AbilityCapSchemaVersion = 12`. It is allowed only when `operation` is `bonus` and `target` is an ability score (`ability.<x>.score`). The range is 21 to 30 inclusive (20 is the default and 30 is the score maximum). A literal negative `value` with a `cap` is refused; a formula that evaluates negative is a penalty and its cap does nothing. The cap is a content field, never a `RulesFamilyPolicy` field.
2. **Older revisions never bind it.** In a v12 revision the member is typed in full. In a v11 revision a `cap` key stays extension data (round-trips byte for byte, hash unchanged) and calculates as if no cap were there; below v11 the v11 and v12 keys are both left as data.
3. **Combining: lowest cap first.** Ability-score bonuses sort (stable) with penalties last and, among increases, by their ceiling (`cap`, or 20 with none) ascending. An increase stops at the larger of its ceiling and the running score, so content order never matters. Penalties are never capped; `set` overwrites everything and is uncapped; a `highestInGroup` loser is skipped as today and its cap does nothing; among equal amounts in a group the higher cap wins. Examples: 18 with a plain +2 and a +4 with cap 25 gives 18, then 20, then 24 in either content order; 20 with a +2 capped at 22 and a +2 capped at 24 gives 22, then 24.
4. **Trace copy.** A stopped increase reads "Bonus from {name}, capped: increases cannot raise an ability score above {ceiling} (+{amount} would give {next})" (exactly today's text when there is no cap, because the ceiling is 20). An increase with a cap that is not stopped reads "Bonus from {name} (it can raise the score up to {cap})". Without a cap the text and the step order are unchanged, so golden sheets do not move.
5. **Inherited members all work** (`automation`, `timing`, `toggle` and `toggleContent`, `whileArmored`, `stacking`), except **item mods**: a `cap` inside an item mod is `validate.item-mod-modifier`, and a `cap` on a top-level modifier of an item-mod revision (which applies as a mod modifier) is `validate.modifier-cap`. The calculator ignores a cap on any item mod's modifier, nested or top-level, and a cap outside 21 to 30, so unvalidated content still stops at 20 there.
6. **Down-conversion** (ADR-023, owner ruling, 2026-11-08). At the 12 to 11 rung the `cap` key is dropped and the bonus kept (`below.v12-key`: "'{name}': that the increase can raise the score above 20 (up to {cap}); the older version stops it at 20."). A copy that stops at 20 is only weaker, so nothing blocks.
7. **Import.** The ability solver, for a sheet score above 20 with increases from content, previews the bases from the one the increases would give (score minus increases) up to the score and keeps the first that reproduces the sheet score (so a partly used cap solves); otherwise it keeps today's answer (the base equals the score). When the next base reproduces the score too, it adds the note `ability.cap-ambiguous` ("reaches a content maximum of N, so its base may be higher than proposed; the smallest base that fits is used."). It costs at most the increases plus two extra previews for such an ability and changes nothing for scores of 20 or below.
8. **Registry, preview, graph.** The capability registry's modifier row gains "; an ability-score increase may declare a maximum above 20". The formula preview and the content graph read only `value`, so they do not change.
9. **UI copy (studio only; no sheet change).** A number field "Highest the score can reach" (21 to 30) with the hint "Leave empty for the usual 20; up to 30.", shown when the target is an ability score, the operation is "Add (bonus)" and the field is not nested in an item mod. Empty means no cap. Changing the target or operation so the field hides removes `cap`. No schema bump (`ContentRevision.CurrentSchemaVersion` stays 12; `Character.CurrentSchemaVersion` stays 11). The decision row is D92.

### Consequences (G5)

- Content can let one increase go above 20 up to a maximum it names, and several such increases combine the same way in any order. A revision without `cap` keeps its version, bytes and hash and calculates exactly as before; a v11 revision holding one keeps it as unknown data and ignores it.
- A revision that uses it needs content schema 12 (`validate.requires-v12`: "an ability-score cap"). A package that holds one warns `package.needs-newer-build`.
- New codes: `validate.modifier-cap` (on an operation or target that does not allow it, or a literal negative value) and `validate.modifier-cap-range` (not 21 to 30).
- The sheet gains no new panel: the score shows its total and "Why?" shows the trace lines above.

### Not doing (G5)

- **Raising the maximum for every source** (a separate "maximum" effect).
- **Caps on `set` or `replace`, or on other fields.**
- **`cap` in item mods.**
- **Re-authoring bundled Body and Mind** (C2-6).

Known limits (G5):

- In a highest-in-group set a larger plain bonus still beats a smaller capped one (the group keeps its highest amount; equal amounts prefer the higher cap).
- At the 12 to 11 rung the cap is dropped as weaker. A formula elsewhere that reads that ability with a negative sign could come out stronger in the lowered copy; this is not checked (owner question on the PR).
- The import solver's check keeps the other abilities at the probe base, so a formula that reads another ability can miss; then the sheet score stays as the base.

### Evidence (G5)

- `tests/RulesCore.Tests/AbilityCapV12SchemaTests.cs` and `AbilityCapV12ValidationTests.cs` (the member, the version rung, the validator codes, item mods), and `tests/AppService.Tests/AbilityCapV12SchemaFileTests.cs` (the published JSON schema).
- `tests/RulesCore.Tests/AbilityCapV12SheetTests.cs` (lowest cap first in either order, penalties and set uncapped, traces, item-mod and out-of-range caps ignored, the group tie rule) and `AbilityCapTests.cs` (the unchanged 20 cap for both families).
- `tests/RulesCore.Tests/DownConvertV12Tests.cs` (the key drop).
- `tests/AppService.Tests/AbilityCapV12ServiceTests.cs` (the sheet through the service) and `AbilitySolverTests.cs` (scores above 20 solve the base; unreproducible scores keep today's answer; the second preview equals the sheet).
- UI: the studio field in `src/Ui/src/components/ModifierFields.tsx` and its tests, `src/Ui/src/components/HomebrewStudioAbilityCap.test.tsx`.
- The original fixture pack `tests/RulesFixtures/fixture-pack-ability-cap.json`.

Related: [ADR-018](ADR-018-play-mechanics.md), [ADR-021](ADR-021-mechanics-beyond-d43.md), [ADR-023](ADR-023-down-conversion-export.md), [play-mechanics.md](../features/play-mechanics.md#content-v12-g6-response-triggers-and-amounts), [its G7 section](../features/play-mechanics.md#content-v12-g7-roll-modes-on-attacks-and-initiative), [its G1 section](../features/play-mechanics.md#content-v12-g1-scaled-dice-critical-only-and-once-per-turn-riders), [its G9 section](../features/play-mechanics.md#content-v12-g9-roll-floor), [its G10 section](../features/play-mechanics.md#content-v12-g10-check-ability-climb-and-swim-speed), [its G11 section](../features/play-mechanics.md#content-v12-g11-condition-immunity) and [its G5 section](../features/play-mechanics.md#content-v12-g5-ability-score-cap), LIVING_SPECS D76, D86, D87, D88, D89, D90, D91 and D92. Supersedes: none.
