# Sheet for play: features, resources, rolls and play state

SPEC C-03, C-04, C-05, I-05 · MVP "Sheet" · status: implemented (M2 item 2). Rests: [rests.md](rests.md) (M2 item 3).

Rules core: `src/RulesCore/Calculation.cs` (resources, features, hit points) and `src/RulesCore/PlayState.cs`. Service: `src/AppService/Play.cs`. UI: `src/Ui/src/components/PlayPanels.tsx`. Acceptance: `tests/RulesCore.Tests/ResourceAndFeatureTests.cs`, `tests/AppService.Tests/PlayCommandTests.cs`, and the play part of the e2e test "builds an SRD 5.2.1 Barbarian as drafts".

## Features (SPEC I-05)

`sheet.features` lists every active revision (pinned, class, granted or chosen) in resolution order. Each entry has the name, kind, summary, source, page and "granted by …" / "chosen from …", the text and automation of each effect, and the problems scoped to it. Its **automation** is the least automated of its effects:

- `reference` when any effect is reference-only or unknown, or when the revision has no effects at all (for example the SRD 5.2.1 Danger Sense, which is text only). Pure text is never shown as automated.
- `assisted` when an effect needs the player (for example Stonecunning), or when a diagnostic names one of its effects (a formula that fails).
- `automatic` otherwise.

## Resources

`sheet.resources` has one entry per `resource` effect of an active revision (the first definition of a resource id in a revision):

- **Maximum:** the `maximum` formula evaluated like a modifier, in the content's own context: `CLASS_LEVEL` is the level in its class, and `PB` and fields are the displayed values. The trace step cites the content, source and page, and lists the inputs (Rage at Barbarian 3: `min(2 + floor(CLASS_LEVEL / 3), 4)` = 3, with `CLASS_LEVEL 3`).
- **A formula that fails** (does not parse, or reads `CLASS_LEVEL` outside a class) disables only that resource: no maximum, `assisted`, and an `effect.invalid-formula` warning. The rest of the sheet calculates (SPEC C-03).
- **Reference-only** resources are listed as "tracked by hand" and cannot be spent through TomeStack.
- **Current** = maximum − spent, never below 0 (a lower maximum after an update does not go negative).
- **Recoveries:** the `recovery` effects of the same revision for that resource, with the amount evaluated (`value`) unless it is `all`. They are listed only; the long rest previews them ([rests.md](rests.md)).

## Play state (character schema v4, v5)

`character.play` stores what changes at the table, separately from choices:

| Field | Meaning |
| --- | --- |
| `currentHitPoints` | Absent/null = at the maximum, so a character at full keeps full hit points after a level-up. Shown clamped to 0…maximum |
| `temporaryHitPoints` | 0 or more |
| `resources[]` | `{ contentId, resourceId, spent }`. Keyed by content id, not revision, so an update to a new revision keeps the count |
| `conditions[]` | Keys of the 14 conditions both SRDs define (`blinded` … `unconscious`). Reminders only: they do not change calculated values |
| `exhaustion` | 0–6 |
| `hitDiceSpent[]` (v5) | `{ die, spent }` per hit die size (d6–d12, 0–20). Spent on a short rest, given back by a long rest ([rests.md](rests.md)) |
| `deathSaves` (v5) | `{ successes, failures }`, 0–3 each. Three successes: Stable (the 3 stays as the marker). Three failures: dead |
| `inspiration` (v5) | Inspiration (2014) or Heroic Inspiration (2024): you have it or not |
| `toggles[]` (v7) | Active toggles (`{ contentId, toggleId }`; actions `toggleOn`, `toggleOff`; [m3-effects.md](m3-effects.md)) |
| `spellSlotsSpent[]`, `pactSlotsSpent` (v6) | Spent spell slots per spell level and spent Pact Magic slots ([spellcasting.md](spellcasting.md); actions `spendSlot`, `regainSlot`, `spendPactSlot`, `regainPactSlot`) |

**Migration on read:** character schema v1–v4 are upcast to v5 with the new state at its default (full hit points, nothing spent, no conditions, no hit dice spent, no death saves, no inspiration), which is exactly their meaning. Characters are stored as JSON and are not hashed, so no database migration is needed. A build before each version refuses its characters (`character.schema-unsupported`, `package.schema-unsupported`) instead of dropping the play state; 0.2.1 refuses v5.

## `character.play` (confirmed changes only)

`character.play { characterId, action, confirm, amount?, contentId?, resourceId?, condition? }` makes one change and returns the recalculated view. Without `confirm: true` it is refused (`play.confirmation-required`) and nothing changes. The UI sends it only from a deliberate button press.

`character.save` never changes the play state of a stored character. It keeps the stored `play`, whatever the payload carries, so a save for another reason (a name, an override, equipment), or one from a stale copy, cannot undo damage or spent uses. Only `character.play` and the confirmed `character.rest` write it. A new character keeps the play state it is first saved with (`PlayCommandTests.Saving_the_character_never_changes_its_play_state`; M2 review fix).

| `action` | Does | Refused with |
| --- | --- | --- |
| `spend` / `regain` | Spends `amount` uses (at most what is left) or regains them (at most what is spent) | `resource.not-found`, `resource.untracked`, `resource.insufficient`, `resource.nothing-spent` |
| `damage` | Temporary hit points absorb it first; hit points stop at 0 | |
| `heal` | Up to the maximum | |
| `setTemporaryHitPoints` | Replaces them (they do not stack) | |
| `setHitPoints` | 0 to the maximum | `play.hit-points-above-maximum` |
| `addCondition` / `removeCondition` | A known condition key | `play.condition-unknown` |
| `setExhaustion` | 0–6 | `play.exhaustion-out-of-range` |
| `recordDeathSave` | Records a death saving throw whose d20 showed `amount` (1–20), with the SRD outcome (SRD 5.1 p. 98, SRD 5.2.1 p. 17; the same in both): 10 or higher is a success, below 10 a failure, a 1 two failures, and a 20 regains 1 hit point and clears the saves | `play.not-dying` (above 0 hit points), `play.amount-out-of-range` |
| `addDeathSaveFailure` | Adds `amount` (1–3) failures, for example damage at 0 hit points (a critical hit is 2) | `play.amount-out-of-range` |
| `clearDeathSaves` | Resets both to 0 | |
| `setInspiration` | `amount` 1 gives Inspiration, 0 spends or removes it | `play.amount-out-of-range` |

Amounts are 0–10,000 (`play.amount-out-of-range`). **Regaining hit points clears death saving throws** (SRD): `heal` or `setHitPoints` from 0 to above 0 resets them in the same confirmed change.

## Rolls on the sheet (SPEC C-04)

- Every ability modifier, saving throw, skill and initiative field has a "Roll" button on the Stats tab (a d20 test through the `roll` command), and the summary has a "Roll <Ability> check (+N)" button per ability. The "d20 rolls" radio group, in the summary at the top of the sheet since ADR-014 ([sheet-layout.md](sheet-layout.md)), chooses normal, advantage or disadvantage. The result of a summary roll reads "Strength check: 14 (1d20)", the same words as its button (2026-10-06).
- **Attacks and actions** (M2 item 2, [multiclass-and-attacks.md](multiclass-and-attacks.md)): equipped weapons' attacks (to hit, damage, traced), and feature rolls grouped by action, bonus action, reaction and other. "Critical hit" doubles damage dice. Resources and spell slots show pips beside the text (decoration only; the text is the value).
- The **Last roll** region (in the summary, visible on every tab; `aria-live="polite"`) shows the total, formula, mode, every die (dropped and critical dice are marked), the modifiers and the provenance (content, source, page). Since ADR-015 the region also draws the dice (hidden from assistive tech; the text is the result): they show the service's values at once and tumble for 0.6 s, unless reduced motion is on or the setting is off. At most ten are drawn; the text lists every die.
- **Rolling never spends anything.** If the roll names a resource (`linkedResourceId`), the record offers a separate "Spend 1 …" button, which is a confirmed `character.play`.
- **Death saving throws** (the panel appears at 0 hit points, or while saves are recorded): "Roll death saving throw" rolls a d20 (`roll` with `deathSave: true`), and "Record death saving throw (N)" records it. "d20 rolled at the table" + "Record this roll" records a physical roll. The heading reads "Death saving throws: 1 of 3 successes, 2 of 3 failures".
- The hit points panel shows the hit dice left and an "Inspiration" / "Heroic Inspiration" checkbox, and the summary has the same checkbox and one-point hit-point buttons.

## Not in this slice

Conditions that change calculations, and damage at 0 hit points adding a death save failure by itself (the player presses "Add a failure").
