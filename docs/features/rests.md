# Rests: preview, then confirm

SPEC C-05 · LIVING_SPECS D01 · MVP "Sheet" ("play through a short scripted encounter and rest") · status: long rest implemented (M2 item 3); short rest and hit dice implemented (D01 follow-up, M2).

Rules core: `src/RulesCore/Rests.cs` (`RestPlanner`). Service: `src/AppService/Resting.cs`. UI: `src/Ui/src/components/RestPanel.tsx`. Acceptance: `tests/RulesCore.Tests/RestPlannerTests.cs`, `tests/RulesCore.Tests/ShortRestAndHitDiceTests.cs`, `tests/AppService.Tests/RestCommandTests.cs`, and the rest part of the e2e test "builds an SRD 5.2.1 Barbarian as drafts".

## D01, decided (owner, 2026-09-27)

**Long rest (M2 item 3):** a preview that the player confirms. Nothing auto-applies: every proposed change is ticked, and the player can untick any of them.

**Short rest and hit dice (follow-up, owner 2026-09-27: "SRD rules, previewed"):** the player picks the hit dice to spend. Each die is rolled by TomeStack or entered from the table, and gets the Constitution modifier added. Short-rest recoveries are listed as ticked changes, and the long rest gives hit dice back under each family's rule.

## Hit dice (character schema v5)

The sheet's `hitDice` has one pool per die size, largest first: one die per level in every class with that hit die. The SRD pools dice of the same size, so `play.hitDiceSpent` is keyed by the die (`{ die, spent }`), not by the class. Remaining = total − spent, never below 0.

## What a short rest proposes

| Change | Proposed when | Notes |
| --- | --- | --- |
| One per spent hit die (`hitDie:<n>`) | The player adds it | Heals roll + Con modifier, at least `HitDieHealingMinimum` (2024: 1; 2014 states none, so TomeStack uses 0), up to the maximum. Rolls are 1 to the die size, from dice the character has left (`rest.hit-die-roll-invalid`, `rest.hit-die-unknown`, `rest.hit-dice-insufficient`) |
| Death saving throws → 0 | A hit die restores hit points and saves are recorded | The SRD resets them when hit points are regained |
| Each resource by its `shortRest` recovery | Uses are spent | As for the long rest, but only `shortRest` recoveries count (the SRD 5.2.1 Rage regains one use) |

Hit dice are chosen before the proposal, so they are not ticked: the player removes a die from the list, and the proposal is worked out again. When the rest is applied, each die heals the *actual* current hit points, so leaving one out never lets the others heal more than they do.

**0 hit points:** under 2024 rules a short rest needs at least 1 hit point (`ShortRestNeedsOneHitPoint`, SRD 5.2.1 p. 187). SRD 5.1 (p. 87) does not say so, so a 2014 character at 0 can spend hit dice. A long rest needs 1 hit point under both (SRD 5.1 p. 87, SRD 5.2.1 p. 185). A refused rest says `rest.needs-hit-points`.

## What a long rest proposes

The plan is calculated from the sheet (both SRDs):

| Change | Proposed when | Notes |
| --- | --- | --- |
| Hit points → maximum | Current is below the maximum | Also resets recorded death saving throws |
| Temporary hit points → 0 | Any are left | They last until a long rest ends |
| Spent hit dice back (`hitDice:d<size>`) | Hit dice are spent | `LongRestHitDice`: 2014 gives back up to half the total number of hit dice, at least one (SRD 5.1 p. 87); 2024 gives back all of them (SRD 5.2.1 p. 185). With several die sizes under 2014 rules, the player chooses which come back. TomeStack proposes the largest first and says so |
| Each resource by its `longRest` recovery | Uses are spent | `all` clears what is spent. A formula (for example `PB - 1`) is evaluated in the content's context, and it never gives back more than was spent. Only `longRest` recoveries count: a long rest is not a short rest, so content that recovers on both declares both |
| Exhaustion − 1 | The level is above 0 | 2014: only with food and drink. That is the `RulesFamilyPolicy.LongRestExhaustionNeedsFoodAndDrink` difference, and the line says so, so the player can untick it |

**Nothing is skipped silently.** A recovery TomeStack cannot calculate (a formula that fails, or an assisted recovery) is listed under "Do these by hand" (`rest.recover-by-hand`). So is a spent resource whose content has no recovery rule at all (`rest.no-recovery-encoded`). Resources that recover only on the other kind of rest are left as they are.

**Item charges (Wave 3 item 25, R160, R171):** a charged item's `recovery` names its rest (`item.charges.recovery.period`); that rest proposes one `Charges` change per entry with charges spent (`charges:<entry key>`, labelled "<item> charges", the entry key in `resourceId`). With no `regain` all charges come back (`SpentAfter` 0). With `regain` dice the change waits for a total: it is listed with its dice (`dice`, `spentAfter` null) and applies nothing until the roll is in, like a hit die. The panel rolls the dice through the dice roller (`roll` with `chargeEntry`; the roll appears in the roll log like a hit-die roll) or takes a total rolled at the table, and the preview and the finish carry it as `chargeRolls: [{ entry, roll }]`. Charges regained are the total, never more than were spent. No `recovery`: nothing comes back on a rest. An unreadable dice expression goes under "Do these by hand" (`rest.recover-by-hand`). A roll for an entry that regains no dice on this rest, or twice, is `rest.charge-roll-unknown`; a total the dice cannot show is `rest.charge-roll-invalid`; more rolls than the sheet has items with uses are refused at once as one `rest.charge-rolls-too-many` (counts only, nothing written). Applying the rest writes `chargesSpent` on the equipment entries through the play path (the entry gets its id if it had none), in the same transaction as the play state; unticking the change keeps the charges. Undo does not cover a rest.

**Toggles (content v6, [m3-effects.md](m3-effects.md)):** a long rest proposes switching every active toggle off (`toggle:<content>:<id>`); untick it to keep one on.

**Concentration (character schema v8, D19):** a long rest of a character who is concentrating proposes ending it (`concentration`, "Concentration", 1 → 0, naming the spell), so the end is seen before it happens; untick it to keep concentrating, as with a toggle. A short rest does not list it.

**Encounter (character schema v10, D43 slice 3, [play-mechanics.md](play-mechanics.md)):** a long rest of a character who is in an encounter proposes "End the encounter" (`encounter`, the round → 0, "The encounter (round N) ends after a long rest (untick to keep it)"), once. Applying it clears the encounter and every toggle's rounds left, as "End encounter" does (the toggles themselves are the separate toggle lines above). Unticking it keeps the encounter. A short rest does not list it. A character that is not in an encounter sees no such line.

**Spell slots ([spellcasting.md](spellcasting.md)):** a long rest proposes every spent slot back (`spellSlots:<level>`), and both rests propose spent Pact Magic slots back (`pactSlots`). Each is ticked and can be unticked like any change.

## Commands

| Command | Payload | Does |
| --- | --- | --- |
| `character.restPreview` | `{ characterId, kind: "shortRest" \| "longRest", hitDice?: [{ die, roll }], chargeRolls?: [{ entry, roll }] }` | Returns `{ kind, changes[], manual[], basis }`. Writes nothing. Each change has a stable `id`, `from`, `to`, the reason and its origin (content, effect, source), and for hit dice the `die` and `amount`. Hit dice on a long rest are refused (`rest.hit-dice-long-rest`) |
| `character.rest` | `{ characterId, kind, confirm, basis, skip[], hitDice?, chargeRolls? }` | Refused without `confirm: true` (`rest.confirmation-required`). Refused when `basis` is not the current proposal (`rest.preview-stale`), for example after another hit or with different hit dice, so a confirmation never applies changes the player did not see. Otherwise it applies every change except the `skip` ids, in one transaction, and returns the view |
| `roll` with `hitDie: <size>` | | Rolls one of the character's hit dice (the die alone) for the short rest. Changes nothing |
| `roll` with `chargeEntry: <entry key>` | | Rolls the `regain` dice of that entry's charges (`charges.regain`). Changes nothing. `roll.charges-unknown`, `roll.charges-no-dice` |

`basis` is a SHA-256 of the stored character and the plan.

## UI

"Short rest…" and "Long rest…" sit in the sheet header beside Level up (D29); the proposal opens in its own row under the summary with every change ticked, focus moves to its heading, and closing it (Cancel or Finish) returns focus to the button that opened it. Pressing the other button switches the kind and writes nothing. For a short rest, each die size has "Roll a d<size>" and "d<size> rolled at the table" + "Add"; the dice to spend are listed with the hit points each restores and a "Remove" button. The player unticks what does not apply, then presses "Finish short rest" / "Finish long rest" or "Cancel rest". A charges row waiting for its roll shows "Roll <dice>: <item>" and "<item>: <dice> rolled at the table (total)" + "Add the <dice> total: <item>"; once rolled it is an ordinary ticked change with "Clear the roll: <item> charges". The status line then says how many changes were applied (a charges row still waiting for its roll counts as none). The hit points panel shows the hit dice left ("Hit dice: d12 2 of 3").
