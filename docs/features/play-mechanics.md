# Play mechanics: form groups, turns, upkeep, feature attacks, action details and roll modes (D43, slices 1 to 6)

LIVING_SPECS D43 · [ADR-018](../decisions/ADR-018-play-mechanics.md) · ADR-003 content schema v11 · character schema v10 · status: **slices 1 to 6 implemented** (built on `play-mechanics`; ships in the release after 0.6.0).

Slice 1 lets content declare forms: toggles of one group, of which at most one is on, with overlays that sit on top of a form and a cost paid from a shared pool. It builds on the toggles of [m3-effects.md](m3-effects.md). Slice 2 adds a stale guard and an exact undo, slice 3 adds a round counter, durations, upkeep, counting-up pools and allowances (see "Slice 3" below), slice 4 adds attacks that features give, riders on them, actions that exist only in a form, modifiers that follow another entry's toggle and a pool whose maximum a form raises (see "Slice 4" below), and slice 5 adds action details (a saving-throw DC, an area, a range, targets, an action that rolls nothing and extra dice for extra points spent) and named values (see "Slice 5" below), and slice 6 adds advantage and disadvantage from content, a fly speed and damage defences that apply only while a form is on (see "Slice 6" below). The remaining slice (damage responses) is listed in ADR-018 and is not built.

Rules core: `ToggleEffect`, `ToggleGroupEffect` and `ToggleLayer` in `src/RulesCore/Effects.cs`, `ToggleGroups` in `src/RulesCore/ToggleGroups.cs`, the toggle list in `src/RulesCore/Calculation.cs`, and the checks in `src/RulesCore/ContentValidation.cs`. Service: `toggleOn`/`toggleOff` in `src/AppService/Play.cs`. UI: `FormGroups.tsx` on Play, the toggle and form group controls in `HomebrewStudio.tsx`, and `undo.ts`. Acceptance: `tests/RulesCore.Tests/ToggleGroupTests.cs`, `ToggleGroupValidationTests.cs`, `tests/AppService.Tests/ToggleGroupCommandTests.cs`, and the e2e flow in `src/Ui/e2e/forms.e2e.tsx`. Fixture: `tests/RulesFixtures/fixture-pack-forms.json`.

## Content (schema v11)

| Effect | Field | Type | Does |
| --- | --- | --- | --- |
| `toggleGroup` (new type, v11 only) | `groupId`, `label` | key like a scale id (`[a-z][A-Za-z0-9]{0,31}`), text | Declares a form group. It has no behaviour of its own; the label names the group on Play |
| `toggle` | `group` | the key of a `toggleGroup` | Joins the group |
| `toggle` | `groupContent` | content id | The content that declares the group, when it is not this revision. It may be another source's |
| `toggle` | `layer` | `primary` (default in a group) or `overlay` | A form, or an effect on top of a form |
| `toggle` | `allowedWith` | list of `{ contentId?, toggleId }` | Overlays only: the primaries it may be on with. No `contentId` means the overlay's own content |
| `toggle` | `allowedInBase` | boolean (default false) | Overlays only: it may also be on while no primary of the group is |
| `toggle` | `cost` | formula | The amount of the pool that turning the toggle on spends. Needs `resourceId` |
| `toggle` | `resourceContent` | content id | The content that defines `resourceId`, when it is not this revision |
| `toggle` | `level` | 1 to 20 | The toggle is offered from this level on: the class level for a class's features, otherwise the character level |

Rules:

- At most one primary of a group is on. "No primary on" is the base form.
- Overlays exclude each other only through disjoint `allowedWith` and `allowedInBase`. A combined overlay is its own entry whose `allowedWith` names only its primaries and whose `allowedInBase` is false. There is no general overlay-to-overlay exclusion.
- `groupContent` may name content from another source, so a species toggle can join a class's group. If that content is not on the character, the toggle is a group of one.
- A toggle with `resourceId` and no `cost` spends one use, as in v6. A cost of 0 spends nothing. With `resourceContent`, the use comes from that content's pool.
- `autoWhen` toggles (v10) cannot join a group.
- The cost, the pool and the level are read in play; the cost formula is checked when the content is saved.

Below v11 the keys stay extension data, byte for byte, and do nothing; a `toggleGroup` stays an unknown effect. Content is published at the lowest version it needs (`RequiredSchemaVersion`), so a revision that uses no v11 field keeps its version, bytes and hash.

## Validation

Errors stop a publish; warnings do not.

| Code | Severity | Meaning |
| --- | --- | --- |
| `validate.requires-v11` | error | A v11 field or a `toggleGroup` is used in a revision that declares an older version |
| `validate.toggle-group-incomplete` | error | A `toggleGroup` needs a label and a `groupId` of a lowercase letter followed by up to 31 letters or digits |
| `validate.toggle-group-duplicate` | error | The revision declares the same `groupId` more than once |
| `validate.toggle-group-unknown` | error | The toggle names a group this revision (or the content in `groupContent`) does not declare, or names `groupContent` with no `group` |
| `validate.toggle-group-content-missing` | warning | `groupContent` names content that is not installed; the toggle acts alone until it is |
| `validate.toggle-layer` | error | A `layer` with no `group` |
| `validate.toggle-auto-group` | error | An `autoWhen` toggle has a `group` |
| `validate.toggle-allowed-with` | error | A toggle that is not an overlay has a non-empty `allowedWith` or `allowedInBase: true`, or `allowedWith` names a toggle the content does not define |
| `validate.toggle-allowed-with-content-missing` | warning | `allowedWith` names content that is not installed; the pairing is unchecked until it is |
| `validate.toggle-overlay-unreachable` | warning | An overlay is allowed in no base and with no form, so it can never be on |
| `validate.toggle-cost` | error | A `cost` with no `resourceId` |
| `validate.toggle-resource-content` | error | `resourceContent` names content that defines no such resource |
| `validate.toggle-resource-content-missing` | warning | `resourceContent` names content that is not installed |

A cost that does not parse is the usual `validate.formula-invalid`, and a `level` outside 1 to 20 is `validate.level`. The existing `validate.toggle-resource` (the pool must be in this revision) still applies when no `resourceContent` is named. The packages check adds `pack.reference-outside` when a toggle names other content the pack does not carry, and the relationship tree and the resource debugger follow only a toggle's pool (`resourceContent`). The packages check and import candidates follow the group, the pool and the allowed forms.

## Calculation

The sheet resolves each group from the stored toggles (`play.toggles`, in the order they were switched on):

Calculation diagnostics carry a code and a message, with no severity field. `toggle.group-absent` is informational by intent; the other two say that something stored does not apply.

| Code | When |
| --- | --- |
| `toggle.group-conflict` | Two primaries of one group are stored (a hand-edited or imported character). The last one stored applies |
| `toggle.overlay-not-allowed` | A stored overlay is not allowed with the form that is on now (or with none). It is ignored. It stays stored until the next confirmed change drops it |
| `toggle.group-absent` | The toggle's group content is not on this character, so it is a group of one. The sheet does not list it under "Content not applied", and it belongs to no feature, so it does not make the feature assisted |

`sheet.toggles` carries, for each toggle, `group`, `groupContent`, `groupLabel` (the `toggleGroup` label, for the Play picker), `layer`, `cost`, `costResourceContent`, `available` and `unavailableReason`. A toggle whose group content is not on the character reports no `group`, `groupContent`, `groupLabel` or `layer`: it is a plain toggle. An overlay that is not allowed now is listed with `available: false` and the reason, which names the form it needs by its toggle label ("Needs Tide form."), with the content's name added in brackets when the form is another content's ("Needs Tide form (Fixture Tide Form)."). A toggle below its `level` is not listed. A cost that does not evaluate is `effect.invalid-formula` and the toggle cannot be turned on.

## Play (no new character field)

| Action | Does | Refused with |
| --- | --- | --- |
| `toggleOn { contentId, toggleId, replace? }` | Switches it on and pays the cost, in one confirmed change. A primary with another primary on needs `replace: true`, which turns the old one off. Turning a primary on drops the overlays it does not allow (including base-only ones) | `toggle.not-found`, `toggle.already-on`, `toggle.automatic`, `toggle.group-occupied`, `toggle.overlay-not-allowed`, `resource.insufficient`, `effect.invalid-formula` |
| `toggleOff` | Switches it off. Turning a primary off drops the overlays that need it. Spent uses stay spent | `toggle.not-found`, `toggle.already-off`, `toggle.automatic` |

- **Atomic:** a refusal writes nothing. The new toggle list and the payment are worked out first, and the whole change is refused if the pool cannot pay.
- **The pool** is `(resourceContent, or the toggle's own content; resourceId)`. A cost of 0 spends nothing. A toggle with a resource and no cost spends 1, from `resourceContent` when it is set.
- **No refunds:** turning off or switching forms gives nothing back.
- **Group of one:** a toggle whose group content is not on the character shares the group with nothing, so `toggle.group-occupied` never applies to it.

### Undo

Undo is the client's (`src/Ui/src/undo.ts`), one level, as in [sheet-play.md](sheet-play.md). A costed `toggleOn` regains exactly its cost, from the pool it spent. A form switch with `replace` is compound and has no client undo: the service undoes it (next section). A plain `toggleOn` that dropped overlays has none either.

## Slice 2: stale guard and exact undo (D44, amends D23)

- **What is compound:** `toggleOn` with `replace: true`; since slice 3 also `startEncounter` and `endEncounter`. A later slice adds damage with responses (`isCompound` in `src/Ui/src/undo.ts`, `CompoundUndo` in `Play.cs`; a shared fixture, `tests/RulesFixtures/compound-play-actions.json`, keeps the two lists equal). A confirmed turn is its own command (`character.turn`), not an action in that switch; it has the same stamp guard and the same one-level service undo. Simple actions are not compound and keep the client inverse, except that switching off a timed toggle while it counts rounds has none (switching it back on would restart the count; the service Undo still applies).
- **The stale guard:** `character.play` takes `expectedUpdatedAt` (ISO string, the view's `character.updatedAt`). A compound change without it is refused with `play.basis-required`. Any play command whose stamp is not the stored one is refused with `character.stale` (a confirmed turn is refused `turn.preview-stale` instead; see slice 3), the same code and words as `character.save`'s compare-and-set (one concurrency token per character, not a second basis; delta §5.4). A switch sends the stamp the save queue holds when the change starts; the Undo sends the stamp of the view that showed its label, so it undoes exactly the change the label names or is refused. A switch sent twice applies once: a double click runs the second press with the fresh stamp and is refused as `toggle.already-on`; only a repeated message with the same stamp is `character.stale`.
- **The undo:** after a compound change the service keeps one slot for the character: the play state it started from, the `updatedAt` it wrote and a label ("switch to Tide form"). The view carries `undoLabel` while the stored `updatedAt` still matches the slot. `character.undoPlay { characterId, updatedAt, confirm }` puts the play state back exactly (the pool, the forms and the overlays) in one write and returns the view. It does not re-check cost or availability: it is a state restore.
- **Refusals** (nothing is written, and the slot stays): `undo.confirmation-required` (no `confirm`), `character.not-found`, `character.stale` (the page's `updatedAt` is behind the stored one), `undo.nothing` (no slot), `undo.stale` (the slot was written for an earlier stamp, because a save, rest, restore or another play change came after). A write that fails leaves the slot, so the undo can be tried again.
- **One level, in memory:** the next compound change replaces the slot, an undo consumes it, and closing the app forgets it. A library restore or a package import clears every slot, because it can store a character with an older `updatedAt` that would match a slot again. The studio sandbox never sets or shows one.
- **On the sheet:** while the view has an `undoLabel`, the Undo button reads "Undo switch to Tide form" and calls `client.undoPlay` through the sheet's save queue; the client entry from before the switch is withdrawn, so there is one Undo. The button is disabled while a play change or an undo is in flight (D23). Errors take the existing path: the service's words are shown, and `character.stale`, `undo.stale` and `undo.nothing` reload the current copy, which drops the label. These codes are the top-level `code` of the error on the wire (with `play.basis-required` and `undo.confirmation-required`).

## Slice 3: encounter, turns, upkeep, allowances (character v10)

Rules core: `UpkeepEffect`, `ResourceCounting` and the resource and toggle fields in `src/RulesCore/Effects.cs`, `TurnPlanner` in `src/RulesCore/Turns.cs`, the upkeep list in `src/RulesCore/Calculation.cs`, the checks in `src/RulesCore/ContentValidation.cs`, and the long rest line in `src/RulesCore/Rests.cs`. Service: `character.turnPreview` and `character.turn` in `src/AppService/Turns.cs`, `startEncounter` and `endEncounter` in `src/AppService/Play.cs`. UI: `CombatStrip.tsx`, `TurnPanel.tsx` and the amount box and allowance groups in `PlayPanels.tsx`, and the upkeep, pool and duration controls in `HomebrewStudio.tsx`. Acceptance: `tests/RulesCore.Tests/TurnPlannerTests.cs`, `UpkeepContentTests.cs` and `CharacterSchemaV10Tests.cs`, `tests/AppService.Tests/TurnCommandTests.cs` and `CompoundParityTests.cs`, and the e2e flow in `src/Ui/e2e/forms.e2e.tsx`.

### Content (schema v11, added)

| Effect | Field | Type | Does |
| --- | --- | --- | --- |
| `upkeep` (new type, v11 only) | `at` | `turnStart` or `turnEnd` | When in the player's turn it happens |
| `upkeep` | `resourceId`, `resourceContent` | pool key, content id | The pool it changes (`resourceContent`: the content that defines it, when it is not this revision) |
| `upkeep` | `change` | `spend` or `regain` | Takes from the pool (or fills a counting-up pool), or gives back |
| `upkeep` | `amount` | formula, or `all` (regain only) | How much. `all` regains what is spent |
| `upkeep` | `toggle`, `toggleContent` | toggle id, content id | The upkeep applies only while this toggle is on. Absent: always |
| `upkeep` | `ifShort` | `ask` (default) or `end` | A spend the pool cannot cover, for an upkeep that belongs to a toggle: ask first, or end the toggle without asking |
| `resource` | `counts` | `up` | The pool is built up from 0 to its maximum instead of used down ("built up 3 of 5") |
| `resource` | `resourceFull` | `{ text }` | What a full counting-up pool says. Only with `counts` |
| `resource` | `allowance` | boolean | A per-turn allowance (such as one reaction) that an upkeep regain refreshes |
| `toggle` | `duration` | formula (rounds) | How long the toggle lasts once on. Absent: until switched off |

At most 20 upkeeps per revision. Below v11 the keys stay extension data and do nothing, and an `upkeep` stays an unknown effect, as for slice 1. The sheet lists the active upkeeps (`sheet.upkeeps`, in content order with the amount evaluated), and its resources and toggles carry `countsUp`, `fullText`, `allowance`, `duration` and `roundsLeft`.

### The encounter and the round counter

- **A round counter only.** An encounter is `{ round }` on the character. There is no initiative and there are no other creatures: TomeStack counts the player's own turns. `startEncounter` begins at round 0 and gives each toggle that is on and has a `duration` its rounds left; `endEncounter` clears the encounter and every count of rounds left. A toggle switched on during the encounter gets its count; one switched on outside an encounter has none and reads "duration: track by hand". A stored count outside an encounter is ignored (the sheet shows no rounds left and a turn does not count it down), and so is a count on a toggle whose content no longer has a duration.
- **No `refresh: eachTurn` mode.** Content that says once per turn is authored as once per round (ruling C2): an allowance is a pool, and an upkeep regain at `turnStart` refreshes it.
- **Round 0 reads "Round 1".** The Combat strip shows "Round 1" from the moment the encounter starts (round 0) until the first turn begins round 1.
- **A turn** is the start or the end of the player's own turn, previewed and then confirmed (`character.turnPreview`, then `character.turn` with `confirm: true`). Nothing is applied by calculation.

### What a turn does, in order

1. At the start: the round goes up by one (required), then each toggle with rounds left that is on counts down by one; a toggle that reaches 0 ends (required) and takes its overlays with it.
2. Upkeeps at that moment: toggle primaries first, then overlays, then ungrouped ones, each in content order. An upkeep whose toggle is not on is skipped.
3. Spends from ordinary pools, then regains, then spends into counting-up pools (never past full).
4. **Ask first.** A spend the pool cannot cover, for an upkeep that belongs to a toggle with `ifShort` absent or `ask`, is a question in the preview: "<toggle> needs 3, has 1", with **End <toggle>** preselected and **Keep it on**. End ends the toggle and its overlays and spends nothing. Keep spends what is left and lists the rest under "Do these by hand" (`turn.settle-by-hand`). `ifShort: end` ends it without asking. A shortfall with no toggle to end spends what is left and lists the rest by hand.
5. **Counting-up pools** are filled and stop at full. When a turn fills one and the content gave a `resourceFull` text, the preview shows that text and the reply carries it (`turn.resource-full`). Separately, the `resource.full` notice (the same text) appears only when the player fills the pool with a direct spend (the Spend buttons or the amount box); it does not appear when a toggle's cost fills the pool. Only the content's own text is ever shown.
6. **Allowances** refresh at the start of the player's own turn, by an upkeep `regain` at `turnStart` (typically `all`).

An unticked upkeep is skipped; unticking the spend of a toggle's upkeep ends the toggle instead (a required ending). Unticking a counting-up spend only skips it; it does not end the toggle. When a toggle ends, a stored overlay that was only stored (not on, because it was not allowed with the form that was on) goes too, with the reason "<overlay>: not allowed with <form>; removed" (an overlay that was on reads "<overlay> needs <form>, which ends"). A confirmed turn whose plan has no changes writes nothing: no new stamp, and the undo slot keeps the step before it; the reply still carries any "settle by hand" notices. The preview lists every change with its before and after and the reason, so the player sees the turn before it happens.

### The turn commands

| Command | Does | Refused with |
| --- | --- | --- |
| `character.turnPreview { characterId, at, skip[], choices{} }` | Returns the changes, the questions (`choices`), the pool messages (`prompts`), the steps to do by hand (`manual`) and `updatedAt`. Writes nothing | `turn.not-in-encounter` |
| `character.turn { characterId, at, expectedUpdatedAt, skip[], choices{}, confirm }` | Applies the previewed turn in one write, as one undo step ("turn start" or "turn end"), and returns the view with `notices` | `turn.confirmation-required`, `play.basis-required` (no stamp), `turn.preview-stale`, `turn.not-in-encounter`, `turn.choice-unknown`, `turn.choice-required` |
| `startEncounter` / `endEncounter` (`character.play`) | Start or end the encounter | `encounter.already-started`, `encounter.not-started`, `play.basis-required`, `character.stale` |

- **The token.** The turn preview carries the character's `updatedAt`, and `character.turn` sends it back. A change in between (a save, a rest, another play change, a double send) is refused `turn.preview-stale`. This differs from the rest preview, whose basis is a SHA-256 of the stored character and the plan: a turn's plan is a pure function of the stored character and the selection, so the one concurrency token every compound command already uses (D44) is enough, and a second hash would only say the same thing.
- **Every question needs a choice.** The turn panel sends an explicit `choices` map with every shown prompt, defaulting to End; the service refuses a missing choice with `turn.choice-required` (a choice for a question the recomputed plan no longer asks is `turn.choice-unknown`). A radio that is already checked fires no change, so the client sends the default itself.
- **Notices** are reply-only and never stored: `turn.settle-by-hand` (a step the player settles by hand), `turn.resource-full` (a pool filled by the turn, with its text), `resource.full` (a spend that filled a counting-up pool) and `encounter.duration-by-hand` (a toggle that lost its count when the encounter ended).

### Validation (added)

| Code | Severity | Meaning |
| --- | --- | --- |
| `validate.upkeep-incomplete` | error | An upkeep needs a `resourceId` and an `amount` |
| `validate.upkeep-toggle-unknown` | error | The toggle (or `toggleContent`) is not defined or not installed |
| `validate.upkeep-resource` | error | The pool is not defined in this revision (or the content in `resourceContent`) |
| `validate.upkeep-amount` | error | The amount is not a formula, or a spend says `all` |
| `validate.upkeep-if-short` | error | `ifShort` on anything but a spend that belongs to a toggle |
| `validate.upkeep-too-many` | error | More than 20 upkeeps in a revision |
| `validate.resource-full` | error | A `resourceFull` on a pool that does not count up, or with no text |
| `validate.toggle-duration` | error | The duration is not a formula |
| `validate.cost-counts-up` | warning | A cost spends from a pool that counts up, so it fills the pool |
| `validate.allowance-no-refresh` | warning | An allowance that no upkeep regains |

### Character schema v10

`play.encounter` (`{ round }`) and `play.toggles[].roundsLeft` are the only new fields. A character is written at v10 only while an encounter is running or a toggle has rounds left; otherwise it is written at v9 or v8 as before, so a character that never starts an encounter stays readable by 0.6.0 and earlier builds. An older build refuses a package or backup that carries a v10 character. A long rest proposes "End the encounter" once ([rests.md](rests.md)); applying it clears the encounter and the counts. The studio sandbox never writes encounter state.

### Undo of turns

A confirmed turn, "Start encounter" and "End encounter" are compound: each sets the service's one undo slot (labels "turn start", "turn end", "start encounter", "end encounter"), and the button reads "Undo turn start" and so on. The undo restores the whole play state: the round, the durations, the pools and the toggles that the turn ended. A turn that has no changes writes nothing and leaves the slot as it was. Switching off a timed toggle while it counts rounds is not offered a client Undo (it would restart the count); the sheet's Undo falls back to the service's, if it holds one.

### Turn panel (UI)

The panel follows the rest panel. Its checklist rule: each row is a checkbox that is ticked at first; a required row (the round, a toggle that must end) is checked and disabled with "(required)"; a row the player unticks is skipped. The checklist comes from the unselected preview, so unticking an upkeep's spend keeps its row to tick again. A tick or a choice fetches a new preview and Confirm waits for it. See [sheet-play.md](sheet-play.md) and [accessibility-checklist.md](accessibility-checklist.md) rows 67 to 71. A turn whose checklist has nothing to apply still confirms, but writes nothing and keeps the earlier Undo.

## Slice 4: feature attacks, riders, form-only actions, cross-entry toggles (content v11 only)

Rules core: `AttackEffect`, `AttackRiderEffect` and the new `ModifierEffect.ToggleContent` and `RollEffect.WhileToggle` in `src/RulesCore/Effects.cs`; `CollectAttacks`, `FeatureAttack`, `ApplyRiders`, the resource-maximum bonus and the cross-entry modifier gate in `src/RulesCore/Calculation.cs`; the checks in `src/RulesCore/ContentValidation.cs`. Service: the feature-attack branch of `src/AppService/Rolling.cs`; the export filter in `src/AppService/Exports/SheetExport.cs`. UI: the actions panel in `PlayPanels.tsx`, `AttackFields.tsx` and `attackAuthoring.ts` with `HomebrewStudio.tsx`. Acceptance: `tests/RulesCore.Tests/FeatureAttackContentTests.cs` and `FeatureAttackTests.cs`, `tests/AppService.Tests/AttackCommandTests.cs`, `ToggleGroupCommandTests.cs` and `ExportAdapterTests.cs`, `src/Ui/src/components/ActionsPanel.test.tsx` and `HomebrewStudioAttacks.test.tsx`, and the e2e flow in `src/Ui/e2e/forms.e2e.tsx`. Fixtures: `tests/RulesFixtures/fixture-pack-feature-attacks.json` and `characters/pm-adept-l5.json`. There is no new character field, play action or version: everything here is content schema v11.

### Content (schema v11, added)

| Effect | Field | Type | Does |
| --- | --- | --- | --- |
| `attack` (new type, v11 only) | `attackId`, `label` | key like a scale id, text | An attack a feature gives (an unarmed strike, a bolt), calculated like a weapon. Ids should be unique in a revision (not enforced; each effect is its own row) |
| `attack` | `kind` | `melee` or `ranged` | Which kind it is; the attack always carries the tag `melee` or `ranged` as well |
| `attack` | `abilities` | list of `str` ... `cha` (at least one) | To hit = the best listed modifier (a tie goes to the first listed) plus the proficiency bonus |
| `attack` | `proficient` | boolean (default true) | Whether the proficiency bonus is added |
| `attack` | `damage` | `{ count, die }`, both formulas | The dice, such as `{ "count": "1", "die": "SCALE.handDie" }` so a die can follow a class column. The die must evaluate to 4, 6, 8, 10 or 12 and the count to 1 to 100 |
| `attack` | `damageBonus` | formula | Added to the damage. Absent: the chosen ability modifier |
| `attack` | `damageType` | one of the 13 damage types, any case | The type of the damage |
| `attack` | `range`, `tags`, `replacesAttack`, `activation` | text, keys, boolean, activation | Range text; the keys a rider matches (`all` is accepted but has no effect on an attack: riders match by tag, and a rider's `all` already matches every attack); whether it can stand in for one attack of the Attack action; the action group it is listed in (default `action`) |
| `attack` | `resourceId`, `resourceContent`, `cost` | pool key, content id, formula | The pool the attack spends from (`resourceContent` when it is not this revision) and how much (default 1 with a pool). Nothing spends it by itself: the attack roll's record offers "Spend", as a roll effect does |
| `attack` | `whileToggle` | `{ contentId?, toggleId }` | The attack is available only while this toggle is on (it may be another entry's) |
| `attackRider` (new type, v11 only) | `tags` | keys (at least one) | The attacks it applies to. `all` matches every attack. A weapon carries `weapon` and `melee` or `ranged`; a feature attack carries its own tags and `melee` or `ranged` |
| `attackRider` | `toHit`, `dice`, `damageBonus` | formula, `NdM`, formula | Added to the attack roll, extra damage dice, added to the damage. At least one is needed. `dice` is a count of 1 to 100 and a d4, d6, d8, d10 or d12 |
| `attackRider` | `damageType` | damage type | The type of the extra dice. Absent: the attack's own |
| `attackRider` | `toggle`, `toggleContent` | toggle id, content id | The rider applies only while this toggle is on (`toggleContent`: the content that declares it, when it is not this revision). Absent: always |
| `attackRider` | `stackGroup` | key | Riders of one group never add: the strongest applies |
| `modifier` | `toggleContent` | content id | With `toggle`: the modifier follows that content's toggle, not one of its own revision |
| `roll` | `whileToggle` | `{ contentId?, toggleId }` | The action is available only while this toggle is on |
| `modifier` | `target: resource.<id>` | a pool of this revision | A `bonus` raises that pool's maximum (see below) |

Below v11 the new keys stay extension data, byte for byte, and do nothing; `attack` and `attackRider` stay unknown effects; a `resource.<id>` target is `effect.unknown-target`. A revision that uses none of them keeps its version, bytes and hash.

### On the sheet

- **A feature attack** is a row in `sheet.attacks` with `source: feature` (a weapon is `source: weapon`), no `category`, its `tags`, `replacesAttack`, `activation`, `resourceId`, `costResourceContent` and evaluated `cost`, `available` and `unavailableReason`, and the `riders` that applied. Its trace shows the ability, the proficiency bonus, the dice (with the class column it came from) and one step for each rider part that applied. Damage reads like a weapon's: `1d8+3d8+5` is the dice, each applied rider's dice and one flat total (the ability modifier or `damageBonus`, plus the riders' flat parts).
- **Riders** apply to weapons and feature attacks alike: a rider tagged `all` adds its dice to an equipped weapon's damage and to its versatile damage. A rider is on while its toggle is on (an automatic toggle follows its condition; a toggle nothing declares, or whose content is not on the character, is off) or, with no toggle, always. A rider whose formula fails is left out with `effect.invalid-formula`.
- **Stack groups:** of the riders of one group that match an attack, only the strongest applies (the highest average of its dice plus its flat parts; on a tie the first in the sheet's content order). A rider that loses adds a trace note, `rider.not-applied`, naming the one that won. Riders with no group all add.
- **An attack that cannot be worked out** (a die that is not a d4 to d12, a count outside 1 to 100, a die or count formula that fails, no ability, or a failing `damageBonus` formula) is listed unavailable with the reason, an empty damage and no riders, the feature is Assisted, and `attack.die-invalid` (or `effect.invalid-formula` for the bonus) is reported on it. A cost formula that fails leaves the attack available, Assisted, with no cost, and the usual `effect.invalid-formula`.
- **Form-only actions:** a roll effect or an attack with `whileToggle` whose toggle is off is listed with `available: false` and `unavailableReason` "Available in <toggle label>" (the toggle id when the toggle is not declared or its content is not on the character). It keeps its numbers; only the buttons are disabled. A feature's effects carry the same `available` and `unavailableReason`.

### Modifiers that follow another entry's toggle

A `whileActive` modifier with `toggle` and `toggleContent` applies while that content's toggle is on. If that content is not active on the character, or does not declare the toggle, the modifier is not applied and the sheet reports `modifier.toggle-content-missing` on the modifier's content. The trace reads "Bonus from <content> while <toggle label> is on". Without `toggleContent`, a modifier's toggle is still one of its own revision's.

### A form raises a pool's maximum

A `bonus` modifier on `resource.<id>` (a pool of the same revision) adds to that pool's maximum, while its toggle is on if it has one. Several bonuses are summed from the formula's value and the total is clamped at 0 once. Spent is never rewritten and `current` is `max(0, maximum - spent)`, so a pool that was raised, spent from and then lowered again (the form ended) stops at 0 and never shows a negative amount, and uses spent stay spent. A bonus whose formula fails, or that is not automatic (or not always or toggled), is not applied and leaves the pool Assisted; a pool with no maximum of its own ignores its bonuses. Stacking and stack groups are not used on a `resource.*` target.

### Rolling

`roll` takes the same `content` and `effectId` as a roll effect: the effect id of an `attack` rolls it. Without `damage` it rolls `1d20` with the attack's to-hit as a traced modifier; with `damage: true` it rolls the damage string, and a critical hit doubles every die (rider dice included) and not the flat total. The weapon branch finds weapons only (`source: weapon`), so a feature attack never answers for a weapon of the same item. An attack that could not be worked out is refused with `attack.die-invalid` (or `effect.invalid-formula` for a failing bonus) and nothing is rolled. An attack that is only outside its form is **not** refused: the form is a hint on the sheet, not a rule the service enforces, and the player may know better. A roll writes nothing.

### Exports

Feature attacks are not exported in v1. The neutral sheet JSON and the Foundry actor list equipped weapons only (`SheetExport.Build` reads `source: weapon`); an unarmed strike or a bolt a feature gives stays on the TomeStack sheet. A weapon that a rider gave extra dice (`1d8+1d6+4`) is exported with its full damage string in the sheet JSON and in the Foundry description; see [export-adapters.md](export-adapters.md) for what Foundry's single base damage part keeps.

### Validation (added)

| Code | Severity | Meaning |
| --- | --- | --- |
| `validate.requires-v11` | error | (existing) now also for an `attack`, an `attackRider`, `toggleContent`, `whileToggle` and a `resource.<id>` target in a revision that declares an older version |
| `validate.attack-incomplete` | error | An attack needs an `attackId` of a lowercase letter followed by up to 31 letters or digits, a label, damage dice (a count and a die) and a `damageType` |
| `validate.attack-abilities` | error | An attack lists no ability |
| `validate.attack-damage` | error | The damage count, die or `damageBonus` is not a formula |
| `validate.grant-damage-type` | error | (existing) the `damageType` of an attack or a rider is not one of the 13 damage types |
| `validate.tag-invalid` | error | A tag is not a key like a scale id (or `all`) |
| `validate.attack-resource` | error | An attack has a `cost` and no `resourceId`, or its pool is not defined by this revision or by the `resourceContent` (a pool nobody declares is a typo, so unlike a roll's this is an error even when the content is not installed) |
| `validate.rider-incomplete` | error | A rider needs at least one tag and at least one of `toHit`, `dice` or `damageBonus` |
| `validate.rider-dice` | error | `dice` is not a count of 1 to 100 and a d4, d6, d8, d10 or d12, such as `3d8` |
| `validate.toggle-content-missing` | **warning** | A toggle reference (a modifier's `toggleContent`, a roll's or attack's `whileToggle`, a rider's `toggleContent`) names content that is not installed. It is unchecked until that content is, and only a modifier reports `modifier.toggle-content-missing` while it is not on the character. A rider, attack or roll whose toggle content is missing is simply off (an attack or roll reads "Available in <toggleId>"), with no diagnostic |
| `validate.toggle-unknown` | error | When the content **is** installed and declares no such toggle: the modifier's error. Also a `toggleContent` with no `toggle`, or a blank toggle id |
| `validate.while-toggle-unknown` | error | The same for a roll's or an attack's `whileToggle` (also a blank toggle id) |
| `validate.rider-toggle-unknown` | error | The same for a rider's `toggle` and `toggleContent` |
| `validate.resource-target` | error | A `resource.<id>` target names a pool this revision does not declare |
| `validate.resource-target-operation` | error | A `resource.<id>` target uses anything but `bonus` |
| `validate.toggle-allowed-with-not-primary` | warning | An overlay's `allowedWith` names a toggle that is not a primary of the overlay's own group, so the pairing can never be on |

A cost, a `toHit` or a `damageBonus` that does not parse is the usual `validate.formula-invalid`. An attack or rider (or a modifier or roll carrying a v11 key) that cannot be read at v11 or later is `validate.effect-malformed`.

### Calculation (added)

| Code | When |
| --- | --- |
| `attack.die-invalid` | A feature attack is unavailable because its die, count, abilities or a formula cannot be worked out (reported on the feature) |
| `rider.not-applied` | A rider lost its stack group; a **trace** note on the attack, not a diagnostic |
| `modifier.toggle-content-missing` | A modifier follows a toggle of content that is not active or does not declare it; the modifier is not applied |

### Studio

"Add attack" and "Add attack rider" add the effects with strict fields: tags (comma separated), the rider's dice, the stack group and the abilities are checked as you type, a bad value is shown with its reason and blocks Save and Publish, and the bad text is never stored. An attack and a roll gain "Available" (a toggle of this revision or of another entry), a modifier's "Applies" is the same select, and a modifier's target can be "Maximum of <pool>" (offered for a bonus). **Other entries' toggles come from those entries' newest published revision only**, so a toggle in an unpublished draft of another entry is not offered. Removing a toggle removes the riders of this revision that named it and clears `whileToggle` on attacks and rolls and the toggle of a modifier that named it (a modifier that follows another entry's toggle of the same id is kept). Removing a pool clears an attack's pool and cost (not one that names a `resourceContent`) and removes a modifier that targeted the pool's maximum. Slice 5 changed one thing here: removing a pool now also clears the pool, cost, variable cost and per-unit dice of a roll that used it (not one that names another entry's pool through `resourceContent`; see "Slice 5").

## Slice 5: action details and named values (content v11 only)

Rules core: `RollEffect` (new fields), `RollSave`, `RollArea`, `RollPerUnit`, `ValueEffect` and `DiceRoller.TryPerUnitFormula` in `src/RulesCore/Effects.cs` and `Dice.cs`; `FormulaFeatures` and the `VALUE.` identifier in `Formulas.cs`; `RollDetailsOf`, `CollectValues`, `ResolveValue` and `FieldReads` in `Calculation.cs`; `FormulaPreview.cs`; the checks in `ContentValidation.cs`. Service: the roll-effect branch of `src/AppService/Rolling.cs`. UI: the actions panel and the roll record in `PlayPanels.tsx`; `RollFields.tsx`, `rollAuthoring.ts` and `HomebrewStudio.tsx`. Acceptance: `tests/RulesCore.Tests/ActionDetailContentTests.cs`, `ActionDetailTests.cs`, `NamedValueTests.cs` and `Properties/PerUnitProperties.cs`, `tests/AppService.Tests/RollCommandTests.cs` (including `The_acceptance_technique_rolls_and_spends_the_same_amount`), `src/Ui/src/components/ActionsPanel.test.tsx`, `HomebrewStudioRolls.test.tsx` and `rollAuthoring.test.ts`, and the e2e flow in `src/Ui/e2e/forms.e2e.tsx`. Fixtures: `tests/RulesFixtures/fixture-pack-action-details.json` and `characters/pm-wave-adept-l5.json`. There is no new character field, play action or version: everything here is content schema v11.

### Content (schema v11, added)

| Effect | Field | Type | Does |
| --- | --- | --- | --- |
| `roll` | `dice` | dice text (now optional) | A roll with no `dice` is an action that rolls nothing and only spends from its pool ("Use"). It needs a `cost`. Typed at v11 only (see below) |
| `roll` | `save` | `{ ability, dc }` | The saving throw the action asks of its targets: `str` ... `cha`, and the DC as a formula (`8 + PB + WIS.MOD`). The sheet shows the evaluated DC and its trace; nothing rolls or applies it |
| `roll` | `area` | `{ shape, size, width? }` | `line`, `cone`, `sphere`, `cube`, `cylinder` or `emanation`; `size` in feet (1 to 1000); `width` (1 to 1000) for a line only. Shown as text, never automated |
| `roll` | `range`, `targets` | text, at most 200 characters each | Shown as text |
| `roll` | `perUnit` | `{ dice, maxUnits? }` | Extra dice for each point spent beyond the base `cost`: one plain `NdM` term (a count of 1 to 100, a d4, d6, d8, d10 or d12) and the most extra points (1 to 100). Needs `dice` to add them to, and `cost` and `resourceId`; not with `variableCost` |
| `value` (new type, v11 only) | `valueId`, `formula` | key like a scale id, formula | A named formula, read as `VALUE.<valueId>` by other formulas (see "Named values") |

Below v11 the new roll keys stay extension data, byte for byte, and do nothing. A roll with no `dice` stays an unknown effect below v11 (a build without slice 5 cannot type it), and a `value` stays an unknown effect. A revision that uses none of them keeps its version, bytes and hash. A `save`, `area` or `perUnit` that does not read (an ability or shape that is not in the list, or a missing `dc`, `size` or per-unit `dice`) leaves the whole roll unreadable, and a v11 revision reports `validate.effect-malformed`.

### Validation (added)

| Code | Severity | Meaning |
| --- | --- | --- |
| `validate.requires-v11` | error | (existing) now also for a `save`, `area`, `range`, `targets` or `perUnit`, a roll with no dice and a `value`, in a revision that declares an older version |
| `validate.roll-save` | error | The save DC is not a formula (an empty DC, or one that does not parse) |
| `validate.roll-area` | error | The area `size` is not 1 to 1000, or a `width` is set on a shape that is not a line or is not 1 to 1000 |
| `validate.roll-text-too-long` | error | `range` or `targets` is over 200 characters |
| `validate.roll-per-unit` | error | The per-unit `dice` is not a count of 1 to 100 and a d4 to d12 such as `1d6`; there is no `cost` or no `resourceId` to count points of; the roll has no `dice` to add them to; `maxUnits` is not 1 to 100; the base dice plus the per-unit dice for every unit (`maxUnits`, else one unit) could throw more than 100 dice (exactly 100 passes, 101 fails); the formula that results cannot be rolled (more than 8 terms or longer than 40 characters); or the roll is also `variableCost` (the amount box replaces the variable spend, so one of them has to go) |
| `validate.roll-dice-or-cost` | error | A roll has neither dice nor a cost, so it does nothing |
| `validate.value-incomplete` | error | A `value` needs a formula and a `valueId` of a lowercase letter followed by up to 31 letters or digits (also when it cannot be read as a value at v11). A value whose automation is not automatic is refused too ("a named value is always calculated"): a value that is not automatic would be ignored by the calculation |
| `validate.value-duplicate` | error | The revision declares the same `valueId` more than once |
| `validate.value-unknown` | **warning** | A formula reads `VALUE.<id>` and the revision, its parent class (for a subclass) and the content it grants or offers declare no such value. Only class and subclass revisions are checked: a feature cannot know its class when it is validated, so it gets no warning |

A cost, a bonus or a DC that does not parse is the usual `validate.formula-invalid` (the DC uses `validate.roll-save`). `VALUE.` in a revision older than v11 is `validate.requires-v11`. The existing `validate.dice-invalid` still applies to a roll that has dice. The relationship tree shows a dice-less roll without parentheses ("Roll: Surge"), and a dice-less roll or a value is a known mechanic, not an unsupported one. A `value` carries no reference to other content, so packages and import candidates follow nothing new.

### Calculation (added)

A feature's roll effect on the sheet carries `saveAbility`, `saveDc`, `saveTrace`, `area`, `range`, `targets` and `perUnit` beside the earlier fields. The trace of a DC reads like "Dexterity save DC, from Proficiency bonus 3 and Wisdom modifier 2" (one step, with the inputs named; it does not repeat the formula). When the DC cannot be worked out, `saveDc` is absent, the row says "Save: Dexterity (DC unavailable)", the feature is Assisted and `effect.invalid-formula` is reported; the numbers that did work are still shown.

| Code | When |
| --- | --- |
| `value.no-class` | A formula reads `VALUE.<id>` from content that belongs to no class (a feature pinned directly, a species). The read fails |
| `value.unknown` | A formula reads a value that no entry of the character's class declares. The read fails |
| `value.cycle` | A value reads itself, directly or through other values (or reads a value that does). Every value of the loop is unavailable and its readers get this code |
| `value.duplicate` | Two entries of one class declare the same `valueId`; the later in sheet order applies. Informational: it does not make the feature Assisted |
| `effect.invalid-formula` | (existing) now also for a roll `cost` or `save.dc` that fails, and for a value whose own formula fails or that is nested more than 32 values deep |

A failing roll `cost` used to be silent (the roll just showed no cost). It now reports `effect.invalid-formula`, makes the feature Assisted and leaves the row available, as an attack's does in slice 4. This applies to a roll in a revision of any version, so a malformed older roll cost now warns. The same code is never reported twice for one roll when a cost and a DC fail the same way; different failures each keep their warning.

### Named values

A `value` effect gives a formula a name. Another formula reads it as `VALUE.<valueId>`: in a cost, a bonus, a save DC, a modifier, a resource's uses and so on (v11 revisions only). `VALUE.<id>` is an identifier, not new grammar, so the formula language is unchanged.

- **Scope:** the class. A value is found among the entries of the character's class: the class itself, its subclass and everything they grant or offer, with the class as the root. Content outside a class has no values (`value.no-class`), and a value on such content is ignored. Of two entries that declare the same `valueId` in one class, the later in sheet order applies (`value.duplicate`).
- **Context:** the value is worked out where it is declared, so `CLASS_LEVEL` and `SCALE.<id>` inside it mean that class and level, whoever reads it. It is worked out once per sheet pass.
- **Cycles:** values may read other values. A loop is `value.cycle` and nothing reads it as 0: the reader's formula fails. A value read through a modifier's formula counts as the fields that value reads, so `effect.dependency-cycle` also catches a modifier that ends up depending on its own target through a value. Reading is bounded at 32 values deep; past it the read fails with `effect.invalid-formula`.
- **Trace:** a value input is named after its declaring entry, such as "Fixture Focus of Mind focusMod 2".
- **Studio preview:** the formula preview (D61) resolves a `VALUE.<id>` that the same revision declares, in a v11 draft. A value the draft does not declare, or a loop, reads "not a value this entry declares" and shows no number.

### Rolling an action with an amount

`roll { content, effectId, amount? }` rolls a roll effect as before (see [dice-engine.md](dice-engine.md)); `amount` is the points to spend on a per-unit action.

- **The rule:** the base is the evaluated `cost` (a per-unit action with no `cost` has no base to count from, which validation refuses). The formula is the roll's dice plus the per-unit dice once for each point beyond the base: `4d10` with `1d10` per point at amount 6 and base 4 is `4d10+2d10`. At the base amount the formula is the base dice. The most is the base plus `maxUnits`, or the pool's maximum when `maxUnits` is absent.
- **Never changes the character:** rolling does not spend. The record carries `spend { contentId, resourceId, amount }`, which is exactly what the Spend button sends (`character.play` with `spend`), so the pool goes down by the amount that was rolled and no more. `spend` is set for any roll effect with a `resourceId` (the amount asked for, else the evaluated cost, else 1 when the roll declares no cost), except when the cost formula fails: then there is no `spend`. For a `variableCost` roll it is the most the roll may spend. A roll without a pool, and every other kind of roll, has no `spend`.
- **Refusals** (all before any die is rolled):

| Code | When |
| --- | --- |
| `roll.no-dice` | The roll effect has no dice, with or without an amount |
| `roll.amount-not-allowed` | An `amount` on a roll effect that has no `perUnit`, or on any roll that is not a content roll effect (a feature attack, a field, a spell or a weapon) |
| `roll.amount-out-of-range` | The amount is below the base or above the most, or the per-unit die cannot be added to the dice (stored content only) |
| `roll.amount-unaffordable` | The pool has fewer than the amount left, or is not tracked on this sheet |
| `effect.invalid-formula` | An amount on a roll whose cost fails: the base is never guessed. The same roll without an amount still rolls, with no `spend` |

A roll whose bonus fails is still refused with `roll.bonus-invalid`; a cost or DC failure alone does not refuse a roll that has no amount. A roll outside its form (`whileToggle`) is not refused, as in slice 4.

### On Play

- **Detail line:** under an action, one line of what it declares and leaves out the rest: `Save: DC 13 Dexterity · 100-ft line, 5 ft wide · Range: Self · Each creature in the line`. A DC that cannot be worked out reads `Save: Dexterity (DC unavailable)`.
- **Per-unit action:** a number box "Points to spend (<pool>)" starts at the base cost and takes whole numbers from the base to the most (the base plus `maxUnits`, or the pool's maximum). Roll always sends the amount. An amount that is not allowed disables Roll, with the reason as text linked to the button and the box. The box starts over when the base cost changes. It does not stop at what is left in the pool: the service refuses an unaffordable amount (`roll.amount-unaffordable`). When the pool is not tracked on the sheet there is no box and Roll sends no amount: the base dice roll as before slice 5 and the record's Spend is the base cost. The box is `aria-invalid` and described by its reason while its value is not allowed.
- **An action with no dice and a pool** is a button "Use <name> (<cost> <pool>)" that spends its cost (`character.play` with `spend`). It is disabled with a visible, linked reason when its form is off, when the pool is short or untracked ("Not enough Focus left (1)."), when the cost cannot be worked out ("Cost unavailable", and the label drops the amount), or when the cost is 0 ("Costs nothing here"). A dice-less action with no pool shows its name and its text.
- **The roll record's Spend button** reads "Spend <amount> <pool> (<current> left)" and sends the record's `spend`. A `variableCost` roll keeps the box for the amount to spend, from 1 to the lesser of the most and what is left. A record's Spend is used once: after it is clicked (the button or the chosen amount) it is disabled for that record, and a new roll's record offers it again. If the service refuses the spend (or the request fails) the button is given back, so the same record's Spend can be pressed again; a refused variable amount comes back into the box only while the box is still for that record and empty, so it never overwrites a newer record's amount. When the roll effect names a pool but its record has no `spend` (the service leaves it out exactly when the cost formula failed), there is no Spend button and no amount box, and the record says "Cost unavailable"; the amount is never guessed as 1. A feature attack's record keeps its own cost.

### Studio

A roll's editor has every field: Dice (clearing it makes an action that rolls nothing, and removes the per-unit dice, which would have nothing to add to), a bonus (with the formula preview), Activation (Action once, by default "Other" for a roll), the pool and its cost (with the preview, floored at 0) and "The player chooses how much to spend", the extra dice for each extra point and the most extra points, the saving throw with its DC (with the preview), the area shape, size and, for a line, width, the range and the targets. The pool select lists this revision's resources first and then pools of other entries of the source (from their newest published revision, shown with the entry's name); a pool that is in neither is kept and listed as "<id> (other content)". Choosing "The player chooses…" removes the per-unit dice, and the per-unit fields are hidden while it is on. The size, width, per-unit dice, most extra points, range and targets are checked as you type: a bad value shows its reason (`aria-invalid`, linked), blocks Save and Publish, is never stored, and a stored bad value blocks them from the moment the rule loads. The server codes above decide the rest. "Add named value" (class and subclass entries only) adds a `value` with a key (the scale-key rule) and a formula with its preview; it has no Automation choice, since a named value is always calculated. Removing a pool clears the pool, cost, variable cost and per-unit dice of the rolls of this revision that used it. See [homebrew-studio.md](homebrew-studio.md).

## Slice 6: roll modes, fly speed and form-only damage defences (content v11 only)

Rules core: `RollModeEffect`, `GrantEffect.WhileToggle` and `FieldIds.FlySpeed` in `src/RulesCore/Effects.cs`; `RollModes.cs` (`RollModeReason`, `RollModeSummary`, `RollModes.Combine`); `CollectRollModes`, `GateOnToggle`, `CollectDamageDefences`, `IsPlayMechanicsOnlyField` and the `speed.fly` spec in `Calculation.cs`; the checks in `ContentValidation.cs`. Service: the field branch of `src/AppService/Rolling.cs`. UI: `rollModes.ts`, `rollText.ts`, the field buttons in `CharacterSheet.tsx` and `SheetSummary.tsx`, `RollResult` and `RollModePicker` in `PlayPanels.tsx`, and `RollModeFields.tsx` with `HomebrewStudio.tsx`. Acceptance: `tests/RulesCore.Tests/RollModeContentTests.cs`, `RollModeTests.cs`, `Properties/RollModeProperties.cs`, `FormOnlyDefenceTests.cs`, `DamagePipelineTests.cs` and `AbilityCapTests.cs`, `tests/AppService.Tests/RollCommandTests.cs`, `src/Ui/src/rollText.test.ts`, `components/CharacterSheet.test.tsx`, `SheetSummary.test.tsx` and `HomebrewStudioRollModes.test.tsx`, and the e2e flow in `src/Ui/e2e/forms.e2e.tsx`. Fixtures: `tests/RulesFixtures/fixture-pack-roll-modes.json` and `characters/pm-instinct-l4.json`. There is no new character field, play action or version: everything here is content schema v11.

### Content (schema v11, added)

| Effect | Field | Type | Does |
| --- | --- | --- | --- |
| `rollMode` (new type, v11 only) | `target` | text | The roll that gets the mode: `save.<ability>` (`str` ... `cha`), `skill.<skill>` (any of the 18 skills, such as `skill.stealth`) or `ability.<ability>.check`. Nothing else is a target (not `initiative`, not `ability.<ability>.mod`) |
| `rollMode` | `mode` | `advantage` or `disadvantage` | `normal` changes nothing and is refused |
| `rollMode` | `against` | list of condition keys | The mode applies only against these conditions (for a save, the condition being imposed); the player applies it (`blinded`, `charmed`, `deafened`, `frightened`, `grappled`, `incapacitated`, `invisible`, `paralyzed`, `petrified`, `poisoned`, `prone`, `restrained`, `stunned`, `unconscious`). Absent: always. Never an empty list |
| `rollMode` | `toggle`, `toggleContent` | toggle id, content id | The mode applies only while that toggle is on. It is the lookup of a modifier's `toggle`: a toggle of this revision, or of another entry with `toggleContent` (which needs a `toggle`) |
| modifier / restriction | `target` | `speed.fly` | A fly speed in feet. The sheet always has the field (0 when nothing gives a fly speed) |
| `grant` (resistance, immunity, vulnerability) | `whileToggle` | `{ contentId?, toggleId }` | The defence applies only while that toggle is on (it may be another entry's). Read on the three damage defence kinds only; the v11 schema refuses it on any other grant |

Below v11 the new keys stay extension data, byte for byte, and do nothing. A `rollMode` stays an unknown effect below v11 (a build without slice 6 cannot type it), a `mode` that names no member of the list leaves the effect unreadable and a v11 revision reports `validate.effect-malformed`, and a revision that uses none of them keeps its version, bytes and hash. A modifier or restriction on `speed.fly` makes a revision need v11; a stored revision below v11 that carries one has it skipped by the calculation (see below). **"+1, maximum 20" is the global ability-score cap** (the existing rule, ruling C1): a bonus to an ability score stops at 20 whatever the content says, a score already above 20 is neither raised nor lowered by a bonus, and a penalty or a set effect is not capped. There is therefore no `upTo` field and no per-effect cap.

### Validation (added)

| Code | Severity | Meaning |
| --- | --- | --- |
| `validate.requires-v11` | error | (existing) now also for a `rollMode`, a damage grant's `whileToggle`, and a modifier or restriction on `speed.fly`, in a revision that declares an older version |
| `validate.roll-mode-target` | error | The target is not a saving throw, a skill or an ability check (see the table) |
| `validate.roll-mode-mode` | error | The mode is `normal` |
| `validate.roll-mode-against` | error | `against` is an empty list, or names a key that is not a condition key |
| `validate.toggle-unknown` | error | (existing) a roll mode's `toggle` is not defined by this revision (or by the content `toggleContent` names), or it names `toggleContent` with no `toggle` |
| `validate.while-toggle-unknown` | error | (existing) a defence grant's `whileToggle` names a toggle its content does not define |
| `validate.toggle-content-missing` | warning | (existing) the content named by `toggleContent` or `whileToggle.contentId` is not installed |
| `validate.effect-malformed` | error | (existing) a `rollMode` whose mode is not a member of the list, in a v11 revision |

The content a roll mode's `toggleContent` or a grant's `whileToggle.contentId` names is a reference: packages follow it (`pack.reference-outside`) and so do import candidates.

### Calculation (added)

A sheet field that is a saving throw, a skill, an ability check or initiative carries `mode` when at least one `rollMode` reaches it:

```
mode: { automatic: "advantage" | "disadvantage" | "normal",
        reasons: [{ mode, source, content, against?, automatic }] }
```

`reasons` lists every mode that reaches the field, in sheet content order: `source` is the content's name and `content` its reference. `automatic` on a reason says whether the roll command applies it by itself. `mode` is absent from a field with no reason.

- **Which reasons are automatic:** an Automatic `rollMode` with no `against`. An `against` reason (the player confirms the condition) and a `rollMode` whose automation is Assisted or Reference are listed and are notes: they never change `automatic` and never change a roll.
- **Cancellation by presence:** `automatic` combines the automatic reasons: any advantage together with any disadvantage is `normal`, however many there are of each; only advantages give `advantage`; only disadvantages give `disadvantage`; none gives `normal`.
- **Which fields a target reaches:** `save.<x>` reaches that saving throw only. `skill.<x>` reaches that skill. `ability.<x>.check` reaches the ability's d20 check (`ability.<x>.mod`, the field the ability's roll button uses) and every skill of that ability, and `ability.dex.check` also reaches `initiative`, which is a Dexterity check. Saving throws are not ability checks, so an `ability.<x>.check` mode never reaches `save.<x>`.
- **Toggles:** `toggle` gates like a modifier's: off, the reason is absent. A `toggleContent` that is not active, or does not declare the toggle, is `modifier.toggle-content-missing`, the reason is not applied and the feature is Assisted. A `toggle` that its own revision does not declare (and no `toggleContent`) is not applied; validation refuses it.
- **Fly speed:** `speed.fly` is a sheet field ("Fly speed", feet, base 0) that modifiers and restrictions can target like `speed`. A modifier or restriction on it from a stored revision below v11 is skipped with `effect.character-only-field`, so only v11 content can give a fly speed.
- **Form-only damage defences:** a defence grant with `whileToggle` is listed in the sheet's damage defences, and so reaches the damage rules, only while its toggle is on (the same gate as a modifier's). With the form off it is not listed. If it follows content that is not active, it is not listed, the feature is Assisted and `modifier.toggle-content-missing` is reported. A defence without `whileToggle` is unchanged.

### Rolling a field with an automatic mode

`roll { field }` for a saving throw, a skill, an ability check or initiative combines the player's choice (the "d20 rolls" picker, `mode`) with **every** automatic reason of the field. Advantage and disadvantage cancel by presence, so the player's choice and an automatic mode combine like this:

| Player's choice | Automatic mode | Rolled |
| --- | --- | --- |
| Normal | Normal | Normal |
| Normal | Advantage | Advantage |
| Normal | Disadvantage | Disadvantage |
| Advantage | Normal | Advantage |
| Advantage | Advantage | Advantage |
| Advantage | Disadvantage | Normal |
| Disadvantage | Normal | Disadvantage |
| Disadvantage | Advantage | Normal |
| Disadvantage | Disadvantage | Disadvantage |

- **Automatic cancellation beats the player's choice:** the service combines the player's choice with each automatic reason, not with the already combined `automatic`. Once an advantage and a disadvantage are both present the roll is Normal however many sources there are: a field whose automatic reasons cancel to Normal does not roll Advantage when the player chooses Advantage, it rolls Normal. (Combining the choice with the single cancelled value would give Advantage.)
- **The record says what applied:** `automaticMode` (the sheet's `automatic`, even when it cancelled to Normal) and `modeSources` (the automatic reasons' sources, in sheet order) are set only when the field has at least one automatic reason. Otherwise both are absent and the record is as before slice 6.
- **Assisted modes never change the roll:** `against` reasons and non-automatic ones are not combined.
- **Critical is unchanged:** the existing refusal `dice.critical-with-advantage` applies to the mode that is rolled, so a request that is critical and ends up with advantage, including one that gets it from an automatic mode, is refused. The UI never sends `critical` on a field roll.

### On Play

- **Field roll buttons:** a field with an automatic mode names it on its roll button: "Roll Wisdom saving throw (advantage: <source>, <source>)" or, when the reasons cancel, "Roll Wisdom saving throw (normal: advantage and disadvantage cancel)". The summary's ability-check button reads "Roll (advantage: ...)" with the rest of its name ("Dexterity check (+2)") kept in its accessible name. A field with no automatic reason is unchanged.
- **Notes:** the Stats card of a field lists the other reasons as text: "Advantage against paralyzed, stunned (Source)" for an `against` reason and "Disadvantage (Source), not applied automatically" for a non-automatic one.
- **The picker:** the "d20 rolls" group says, in text linked to the group, "Automatic modes from the sheet are combined with this choice; advantage and disadvantage cancel."
- **The roll record:** the headline names the sources, such as ", advantage from <source>", ", automatic mode normal: advantage and disadvantage cancel (<sources>)" when they cancelled, or ", automatic advantage from <source>" appended to a rolled mode that differs.
- **Fly speed:** the Combat group of Stats shows "Fly speed: 30 ft." and the summary's Speed reads "30 ft., Fly 30 ft."; both are hidden at 0 unless an override is set.
- **Form-only defences** are in the sheet's damage defences, and so applied to damage, only while the form is on.

### Studio

"Add roll mode" adds a `rollMode` with a target select (grouped as Saving throws, Skills and Ability checks), a mode (Advantage or Disadvantage), the conditions it applies against (a group of 14 checkboxes; none checked means always, and no empty list is ever stored) and "Only while" (a toggle of this revision or another entry's). "Add damage defence" adds a resistance, immunity or vulnerability grant with its kind, damage type and "Only while". A stored value the editor cannot offer (an unknown target, mode or condition) is shown as it is and blocks Save and Publish; the studio target list is not filtered by version, so `speed.fly` is offered on every draft and the validator decides (`validate.requires-v11`). See [homebrew-studio.md](homebrew-studio.md).

## On Play and in the studio

- **Play** lists each group under "Forms" (in the Combat strip since slice 3) as a labelled group of radio choices: "Base" first, then each form with its cost ("5 Focus"), and the overlays as checkboxes. A choice that cannot be made is disabled and says why. See [accessibility-checklist.md](accessibility-checklist.md) row 66 for the keyboard rule: the arrow keys move focus and do not choose, because each choice spends from a pool. The strip also shows the round, the timed effects, the combat pools and the turn buttons ([sheet-play.md](sheet-play.md)).
- **Homebrew studio** ([homebrew-studio.md](homebrew-studio.md)): a toggle has Cost (with the formula preview), Form group, Kind (form or overlay), Allowed in base, "Can be on with these forms", Level and, since slice 3, "Lasts (rounds)" (a formula with a preview; empty is until switched off). "Add form group" declares a group, and "Add toggle" adds a toggle. A resource has "Counts up", "When full, say" (only while it counts up) and "Allowance". "Add upkeep" (it needs a pool) adds a rule with Toggle (a select of this revision's toggles, or "Always"), When (start or end of my turn), Pool, Change (spend or regain), Amount (a formula; "all" for a regain only, and a switch back to spend turns "all" into 1) and "If the pool is short" (ask or end; only for a spend that belongs to a toggle). Changing a toggle's group clears its allowed forms; removing the pool clears the cost of the toggles that spent it and removes the upkeeps that changed it; removing a toggle removes the upkeeps of this revision that belonged to it (an upkeep that names a toggle that is missing is refused at validation, `validate.upkeep-toggle-unknown`). The check and publish use the server codes above; the version is chosen from what the rules use.

## Not yet

- Damage responses (a later slice; ADR-018).
- Feature attacks in the exports (the Foundry actor and the neutral JSON list equipped weapons only), and rolling a feature attack's cost automatically (the attack roll offers "Spend").
- Applying a saving throw, an area or a range: they are shown as text. Nothing rolls a target's save or places the area. A feature attack has no per-unit amount (only a roll effect does; an `amount` on anything else is `roll.amount-not-allowed`). A dice-less `variableCost` action spends its full cost through Use: there is no box to choose a smaller amount.
- Named values outside a class (a species or a feat pinned on its own has none), a value that takes arguments, and a catalog-wide `validate.value-unknown` for features.
- A trace view of a save DC on the sheet (the trace is on the data; Play shows the number).
- Initiative, other creatures and a DM view (out of scope, ADR-018).
- A general overlay-to-overlay exclusion.
- A toggle that gives back what it spent when turned off.
- Roll modes: a target for initiative alone (a Dexterity check mode reaches initiative with the Dexterity skills), a per-effect cap on an ability bonus (`upTo`; the global cap of 20 applies), a mode that applies automatically against a condition (an `against` mode is a note the player confirms), and modes on attack rolls or death saves. The concentration save applies the `save.con` automatic modes and names them on its button; it does not send the d20 picker's choice. An automatic advantage on Perception (or on Wisdom checks) does not add +5 to passive Perception.
- `whileToggle` on a grant that is not one of the three damage defences (the schema refuses it there, and the calculation ignores it).
