# ADR-018: A reusable play-mechanics engine

Status: accepted (owner decision D43, 2026-10-17; slice 1 built on `play-mechanics`, 2026-10-21)
Date: 2026-10-21

## Context

LIVING_SPECS D43 adds a generic play-mechanics engine as an owner exception to ROADMAP R4. Some content works as a set of forms: one is on at a time, switching costs something from a shared pool, and a few extra effects sit on top of a form. Content schema v6 toggles cannot say any of that. A toggle is on or off alone, spends one use of a resource of its own revision, and never turns another toggle off.

The engine has no rules logic for any one book or homebrew. It is a few small, general ideas that content authors combine. It ships in the release after 0.6.0 (D43, D60 amended), and the schema fields were approved by the owner (D14) before the work started.

## Decision

The engine is built in slices. Each slice is complete and tested before the next starts. Only slice 1 exists today; the rest is scope, named here so the pieces fit.

**Slice 1: form groups, overlays and costs from a shared pool.**

- **A form group** is a named set of toggles, declared by a `toggleGroup` effect (`groupId`, `label`). A toggle joins it with `group`, and `groupContent` when another content declares the group. At most one primary of a group is on. "No primary on" is the base form.
- **Primaries and overlays.** A toggle's `layer` is `primary` (the default in a group) or `overlay`. An overlay sits on top of a form. It lists the primaries it may be on with (`allowedWith`) and may also be allowed with no form on (`allowedInBase`). Two overlays exclude each other only through disjoint `allowedWith` and `allowedInBase`. A combination is its own overlay entry that names only its primaries and sets `allowedInBase: false`. There is no general overlay-to-overlay rule.
- **Costs from a shared pool.** A toggle may carry a `cost` (a formula) and the pool it spends (`resourceId`, with `resourceContent` when the pool belongs to other content). A toggle with a resource and no cost still spends one use, as in v6. A cost of 0 spends nothing. `level` offers a toggle from a character level on.
- **One confirmed change.** `toggleOn` takes `replace`. Switching forms turns the old primary off, turns off the overlays the new one does not allow, and pays the cost in one change. If the pool cannot pay, nothing is written. Turning a primary off also turns off the overlays that needed it. There are no refunds.
- **Content from other sources.** A group may be declared by one content and joined by another (a species toggle can join a class's group). If the declaring content is not on the character, the toggle is a group of one.
- **Calculation repairs, never throws.** A hand-edited or imported character with two primaries in one group applies the last one stored. An overlay that is not allowed now is ignored and listed as unavailable with a reason.

**Later slices (scope only; each gets its own review and owner approval of its fields).** A player-owned round counter with durations, per-turn upkeep, and allowances that refresh once per round (owner, 2026-10-20). Assisted damage-time responses that are never applied without confirmation. Attacks and action details from features, with riders. The basis guard and exact undo for compound changes. They add character schema v10 and more content v11 fields. Out of scope: other creatures' turns, positions, monsters, and any DM tool (D63).

**Structured conditions, not a grammar.** Where content needs a condition (which forms allow an overlay, from which level), it is a field with a closed shape. There is no expression language for conditions and no conditional grants. A formula is still the only expression, and it computes a number.

## Consequences

- **Content schema v11** (`docs/schemas/content-revision.v11.schema.json`). Content is written at the lowest version it needs: a revision that uses no v11 field keeps its version, bytes and hash. Below v11 the new keys stay extension data, byte for byte, and have no effect. Declaring v11 fields at an older version is `validate.requires-v11`. Character schema v10 is reserved for later slices; slice 1 adds no character field.
- **Packages need a v11-capable build** once they carry v11 content. An older build refuses the package. Nothing changes for existing content, characters or packages.
- **Published revisions stay insert-only.** Adding a form group to a feature is a new revision.
- **Validation grows.** The new codes are in [play-mechanics.md](../features/play-mechanics.md). A reference to content that is not installed is a warning, so a source can be published before the source it refers to arrives.
- **Undo is partial in slice 1.** A costed switch on can be undone from the client by regaining its cost. A form switch (and a switch off that dropped overlays) has no client undo until the basis guard of slice 2.
- **Packages and the debugger follow references.** A toggle that names other content (its group, its pool, the primaries an overlay allows) is a reference for the package check (`pack.reference-outside`), the relationship tree and the resource debugger.
- **The cost is that a toggle can now refuse.** The sheet lists a form that cannot be chosen with the reason, instead of hiding it.

## Alternatives considered

- **Mutual exclusion by naming the other toggles** (`turnsOff: [...]`): rejected; it grows with every form, and a content from another source cannot add itself.
- **A condition grammar** (`when: "form == x and ..."`): rejected; it needs a parser, a debugger and a way to explain a failure to a player. Closed fields explain themselves.
- **Overlays as groups of their own**: rejected; they depend on the form that is on, and a second group would need its own cross-rules.
- **A general overlay-to-overlay exclusion**: rejected for slice 1; a combination entry covers the known cases and keeps validation simple.

## Evidence

- `tests/RulesCore.Tests/ToggleGroupTests.cs`, `ToggleGroupValidationTests.cs` and `Properties/ToggleGroupProperties.cs` (resolution, calculation, validation), `tests/AppService.Tests/ToggleGroupCommandTests.cs` (atomic switch, cost, refusals) and `SourcePackTests.cs` (references), `src/Ui/src/components/FormGroups.test.tsx`, `src/Ui/src/undo.test.ts`, and the e2e flow in `src/Ui/e2e/forms.e2e.tsx`. Fixture: `tests/RulesFixtures/fixture-pack-forms.json`.
- [play-mechanics.md](../features/play-mechanics.md) lists the fields, codes and actions.
