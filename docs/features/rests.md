# Rests: preview, then confirm

SPEC C-05 · LIVING_SPECS D01 · MVP "Sheet" ("play through a short scripted encounter and rest") · status: long rest implemented (M2 item 3). Short rest: not in M2.

Rules core: `src/RulesCore/Rests.cs` (`RestPlanner`). Service: `src/AppService/Resting.cs`. UI: `src/Ui/src/components/RestPanel.tsx`. Acceptance: `tests/RulesCore.Tests/RestPlannerTests.cs`, `tests/AppService.Tests/RestCommandTests.cs`, and the rest part of the e2e test "builds an SRD 5.2.1 Barbarian as drafts".

## D01, decided (owner, 2026-09-27)

**M2 has the long rest only, as a preview that the player confirms.** A short rest (hit dice spending, short-rest recoveries) comes after M2. Until then, `character.restPreview` with `shortRest` is refused (`rest.short-rest-not-supported`), and resources are adjusted by hand with Spend/Regain.

## What a long rest proposes

The plan is calculated from the sheet (both SRDs):

| Change | Proposed when | Notes |
| --- | --- | --- |
| Hit points → maximum | Current is below the maximum | |
| Temporary hit points → 0 | Any are left | They last until a long rest ends |
| Each resource by its `longRest` recovery | Uses are spent | `all` clears what is spent. A formula (for example `PB - 1`) is evaluated in the content's context, and it never gives back more than was spent. Only `longRest` recoveries count: a long rest is not a short rest, so content that recovers on both declares both |
| Exhaustion − 1 | The level is above 0 | 2014: only with food and drink. That is the `RulesFamilyPolicy.LongRestExhaustionNeedsFoodAndDrink` difference, and the line says so, so the player can untick it |

**Nothing is skipped silently.** A recovery TomeStack cannot calculate (a formula that fails, or an assisted recovery) is listed under "Do these by hand" (`rest.recover-by-hand`). So is a spent resource whose content has no recovery rule at all (`rest.no-recovery-encoded`). Resources that recover only on a short rest are left as they are.

Not handled yet: hit dice (not tracked), spell slots (the spellcasting slice), and death saves.

## Commands

| Command | Payload | Does |
| --- | --- | --- |
| `character.restPreview` | `{ characterId, kind: "longRest" }` | Returns `{ kind, changes[], manual[], basis }`. Writes nothing. Each change has a stable `id`, `from`, `to`, the reason and its origin (content, effect, source) |
| `character.rest` | `{ characterId, kind, confirm, basis, skip[] }` | Refused without `confirm: true` (`rest.confirmation-required`). Refused when `basis` is not the current proposal (`rest.preview-stale`), for example after another hit, so a confirmation never applies changes the player did not see. Otherwise it applies every change except the `skip` ids, in one transaction, and returns the view |

`basis` is a SHA-256 of the stored character and the plan.

## UI

"Long rest…" on the sheet opens the proposal with every change ticked, and focus moves to its heading. The player unticks what does not apply, then presses "Finish long rest" or "Cancel rest". The status line then says how many changes were applied.
