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

## Play state (character schema v4)

`character.play` stores what changes at the table, separately from choices:

| Field | Meaning |
| --- | --- |
| `currentHitPoints` | Absent/null = at the maximum, so a character at full keeps full hit points after a level-up. Shown clamped to 0…maximum |
| `temporaryHitPoints` | 0 or more |
| `resources[]` | `{ contentId, resourceId, spent }`. Keyed by content id, not revision, so an update to a new revision keeps the count |
| `conditions[]` | Keys of the 14 conditions both SRDs define (`blinded` … `unconscious`). Reminders only: they do not change calculated values |
| `exhaustion` | 0–6 |

**Migration on read:** character schema v1–v3 are upcast to v4 with a fresh play state (full hit points, nothing spent, no conditions), which is exactly their meaning. Characters are stored as JSON and are not hashed, so no database migration is needed. A build before this one refuses v4 characters (`character.schema-unsupported`, `package.schema-unsupported`) instead of dropping the play state.

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

Amounts are 0–10,000 (`play.amount-out-of-range`).

## Rolls on the sheet (SPEC C-04)

- Every ability modifier, saving throw, skill and initiative field has a "Roll" button (a d20 test through the `roll` command). The "d20 rolls" radio group chooses normal, advantage or disadvantage. Features with a `roll` effect have a button, and "Critical hit" doubles their dice.
- The **Last roll** region (`aria-live="polite"`) shows the total, formula, mode, every die (dropped and critical dice are marked), the modifiers and the provenance (content, source, page).
- **Rolling never spends anything.** If the roll names a resource (`linkedResourceId`), the record offers a separate "Spend 1 …" button, which is a confirmed `character.play`.

## Not in this slice

Attacks and weapon damage (no weapons yet; item 4 adds armor only), death saves, inspiration, hit dice and spell slots (spellcasting slice), and conditions that change calculations.
