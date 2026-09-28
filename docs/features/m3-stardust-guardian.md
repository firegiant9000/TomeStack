# M3: the Stardust Guardian acceptance

ROADMAP M3 exit gate ("Arlo plays that character end to end without D&D Beyond") · MVP definition of done 3 and "Separate replacement milestone (M3)" · status: **B1 implemented; waiting for the owner's material.**

Rules core: `src/RulesCore/Mechanics.cs` (`MechanicsInventory`). Service: `character.mechanics` (`src/AppService/MechanicsCommands.cs`). Acceptance: `tests/AppService.Tests/StardustGuardianAcceptanceTests.cs` and `tests/RulesCore.Tests/MechanicsInventoryTests.cs`.

## The owner's material stays private

The Stardust Guardian is the owner's homebrew and must not appear in this public repository. It lives in the gitignored `tests/RulesFixtures/local/stardust-guardian/`:

| File | What | How to make it |
| --- | --- | --- |
| `character.tomestack.zip` | **Required.** A "Personal backup" package of the character with its homebrew source and content | Author the subclass and features in the homebrew studio, build the character, then export it with "Personal backup" (a share export would leave the homebrew out, ADR-007) |
| `expectations.json` | Optional. The owner's classification of each mechanic, which the test compares with TomeStack's | `{ "character": "optional name", "mechanics": [ { "feature": "…", "effect": "effect id, or null for a text-only feature", "automation": "automatic" \| "assisted" \| "reference" } ] }` |
| `report.md` | Written by the test: every mechanic, TomeStack's classification, the expected one and the manual step | Read it after a run; it stays in the local folder |

Run it with `dotnet test --filter StardustGuardianAcceptanceTests`. Without the package the test is skipped, so the gate passes on any machine. **The test output never names or quotes the material**: failures give counts and positions in `report.md` only, because test output is a log.

## What the test checks (MVP definition of done 3)

The package is imported into a clean data folder, like a restore on a new machine. Then:

1. **Nothing is missing:** no `content.missing`, `content.schema-unsupported`, `content.source-missing` or `content.unpublished` on the sheet.
2. The homebrew content gives **at least one** automatic modifier, one tracked class resource, one limited-use action (a roll that names its resource) and one reference-only feature.
3. **Every mechanic is classified** as automatic, assisted or reference (`MechanicsInventory`), and every one that is not automatic has **a manual step**: its effect text, or else its feature's summary. A mechanic without either is a gap.
4. With `expectations.json`: each listed mechanic must be on the character with the owner's classification.

The mechanics of the homebrew sources come from the calculated sheet, so the inventory says exactly what TomeStack does. An effect of a type this build does not know is listed under "unsupported effect types": those are the gaps to design (M3 B2).

## `character.mechanics`

`{ characterId, sourceIds? }` returns `{ mechanics[], unsupported[], hasModifier, hasResource, hasLimitedUseAction, hasReferenceOnlyFeature }`. By default it covers every source made in TomeStack (the user's own homebrew). It writes nothing. The session gap notes (M3 B3) attach to these mechanics.

## M3 exit gate: what is still needed

- The material in the local folder, and a passing run with the owner's `expectations.json`.
- B2 is done for the expected gaps ([m3-effects.md](m3-effects.md): toggles, shared resources, variable costs). Any other gap the run finds gets the same treatment.
- A whole session played with the character, recording gaps as notes (B3), then fixing or accepting each.
- Printable backup and multiclass slot combination (D04's M3 part), if the character needs them.
