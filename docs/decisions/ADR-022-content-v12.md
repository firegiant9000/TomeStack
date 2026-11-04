# ADR-022: Content schema v12, one slice at a time (G6 first: response triggers and amounts)

Status: **accepted** for the slice list and for slice G6 (owner, 2026-10-28: LIVING_SPECS D76 and B4; D86). Slice G6 is built on branch `v12-g6`. The warn-or-block judgement for G6's down-conversion (decision 6) is a proposal that awaits the owner.
Date: 2026-11-04

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
   - `unlessCondition`: a condition key. The response is on the sheet but not available while the character has that condition.
   - `supersedes`: `{ content?, responseId }`. While this response is on the sheet, the named one is left off it.
2. **The hit is described on the request only, and never stored.** `DamageEvent` gains `From` (attack, fall or save, or unknown) and `Save` (`DamageSave`: the ability, whether it succeeded, whether it halves). `Save` is allowed only with `From == Save`. The service refuses an inconsistent pair with `damage.invalid-source`. Nothing about the hit is written to the character.
3. **Matching.** A response with no trigger matches every hit, as in v11. An unknown source (the player did not say) still offers attack and fall triggers, so the player judges, as in v11, but never a save trigger. A known source offers only its own kind. `damageTypes` needs the hit's type in the list; an untyped hit never matches. A trigger gates every phase of the response.
4. **The order** (amends D45): the save's half (`floor(d / 2)` on a success when the save halves), then the chosen `share` reductions in sheet order on the running amount, then flat `amount` reductions (plus any rolled `dice`), then defences, temporary hit points and hit points (`Resolve`, unchanged), then survivals and thresholds as before. The damage plan's reduction counts what responses took off, not what the save took off. As a result the panel's "Reduced by N" never exceeds the damage that reached the responses.
5. **Rolls.** A chosen dice response must carry the player's `Rolled` total within the expression's minimum to maximum, and only a dice response may carry one. Otherwise the preview and the confirm report `damage.response-roll`, one note per response, and nothing applies.
6. **Down-conversion** (ADR-023, "never silently stronger"). At the 12 to 11 rung a response that uses `trigger`, `share`, `dice`, `unlessCondition` or `supersedes` is dropped whole and listed (`below.effect-unsupported`): each of these makes the sheet offer less than the content intended, so keeping the response without the member would make the copy stronger or wrong. `automatic` alone drops just the key (`below.v12-key`), because it only pre-ticks. No new code is added. **This judgement is proposed; the owner decides** whether any of the new members should instead warn or block (ADR-023, consequences).
7. **Numbers.** This ADR is the one reserved by D76. The decision row is D86.

### The slice list

G6 (this ADR) is built first. The remaining slices follow in the order G7, then G1 with G2 and G3, then G9, G10, G11, G5 and G4 (the gap ids are in [effect-vocabulary-coverage.md](../features/effect-vocabulary-coverage.md)). Each extends this ADR with its own decisions and evidence, keeps every new member optional, and adds its own lowering rule.

**0.8.0 waits for every slice** (owner ruling, 2026-11-04): the bundled classes get new v12 revisions only when v12 is complete, and no release is cut while v12 is open. Until then G6 sits on the branch and, once merged, in `main`.

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

Related: [ADR-018](ADR-018-play-mechanics.md), [ADR-021](ADR-021-mechanics-beyond-d43.md), [ADR-023](ADR-023-down-conversion-export.md), [play-mechanics.md](../features/play-mechanics.md#content-v12-g6-response-triggers-and-amounts), LIVING_SPECS D76 and D86. Supersedes: none.
