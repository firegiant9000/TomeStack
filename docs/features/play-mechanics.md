# Play mechanics: form groups, turns and upkeep (D43, slices 1 to 3)

LIVING_SPECS D43 · [ADR-018](../decisions/ADR-018-play-mechanics.md) · ADR-003 content schema v11 · character schema v10 · status: **slices 1 to 3 implemented** (built on `play-mechanics`; ships in the release after 0.6.0).

Slice 1 lets content declare forms: toggles of one group, of which at most one is on, with overlays that sit on top of a form and a cost paid from a shared pool. It builds on the toggles of [m3-effects.md](m3-effects.md). Slice 2 adds a stale guard and an exact undo, and slice 3 adds a round counter, durations, upkeep, counting-up pools and allowances (see "Slice 3" below). Later slices (damage responses, feature attacks) are listed in ADR-018 and are not built.

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
- **The stale guard:** `character.play` takes `expectedUpdatedAt` (ISO string, the view's `character.updatedAt`). A compound change without it is refused with `play.basis-required`. Any command whose stamp is not the stored one is refused with `character.stale`, the same code and words as `character.save`'s compare-and-set (one concurrency token per character, not a second basis; delta §5.4). A switch sends the stamp the save queue holds when the change starts; the Undo sends the stamp of the view that showed its label, so it undoes exactly the change the label names or is refused. A switch sent twice applies once: a double click runs the second press with the fresh stamp and is refused as `toggle.already-on`; only a repeated message with the same stamp is `character.stale`.
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

## On Play and in the studio

- **Play** lists each group under "Forms" (in the Combat strip since slice 3) as a labelled group of radio choices: "Base" first, then each form with its cost ("5 Focus"), and the overlays as checkboxes. A choice that cannot be made is disabled and says why. See [accessibility-checklist.md](accessibility-checklist.md) row 66 for the keyboard rule: the arrow keys move focus and do not choose, because each choice spends from a pool. The strip also shows the round, the timed effects, the combat pools and the turn buttons ([sheet-play.md](sheet-play.md)).
- **Homebrew studio** ([homebrew-studio.md](homebrew-studio.md)): a toggle has Cost (with the formula preview), Form group, Kind (form or overlay), Allowed in base, "Can be on with these forms", Level and, since slice 3, "Lasts (rounds)" (a formula with a preview; empty is until switched off). "Add form group" declares a group, and "Add toggle" adds a toggle. A resource has "Counts up", "When full, say" (only while it counts up) and "Allowance". "Add upkeep" (it needs a pool) adds a rule with Toggle (a select of this revision's toggles, or "Always"), When (start or end of my turn), Pool, Change (spend or regain), Amount (a formula; "all" for a regain only, and a switch back to spend turns "all" into 1) and "If the pool is short" (ask or end; only for a spend that belongs to a toggle). Changing a toggle's group clears its allowed forms; removing the pool clears the cost of the toggles that spent it and removes the upkeeps that changed it; removing a toggle removes the upkeeps of this revision that belonged to it (an upkeep that names a toggle that is missing is refused at validation, `validate.upkeep-toggle-unknown`). The check and publish use the server codes above; the version is chosen from what the rules use.

## Not yet

- Damage responses, feature attacks and action details (later slices; ADR-018).
- Initiative, other creatures and a DM view (out of scope, ADR-018).
- A general overlay-to-overlay exclusion.
- A toggle that gives back what it spent when turned off.
