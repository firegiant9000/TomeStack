# ADR-020: Capability registry and coverage

Status: accepted (consolidated decision #10, 2026-10-20; LIVING_SPECS D66; built in Import Lab Tasks 1 to 3)
Date: 2026-10-25

## Context

- Each effect carries an author-declared `automation` (`AutomationStatus`, default Automatic). Author-declared automation can claim more than the engine does.
- Unknown types become Reference (`UnknownEffect`). Separately, import quarantine forces unconfirmed effects to Reference; that rule and the registry ceiling are different mechanisms.
- The mechanics inventory kept a hand-written set of known types, and it drifted (it once missed `scale` and `item`).
- The Import Lab gate needs "unsupported mechanics shown", and its coverage numbers need a stable denominator.

## Decision

1. **Registry in RulesCore.** `CapabilityRegistry.Entries` is a list of `Capability(EffectType, Field, Ceiling, Reason, Evidence)`.
   - `Field == null` is the type-wide entry.
   - A field entry narrows the ceiling when that JSON member is present and not null on the effect (for example `rollMode` with `against`: the player applies it, so `Assisted`).
   - `Evidence` names the test class that proves the ceiling. A test checks that the class exists.
2. **Effective automation** is the lowest of the author's value, the type's ceiling and the ceiling of each present field, ordered `Reference < Assisted < Automatic`. A diagnostic on the effect lowers it to Assisted, as the mechanics inventory did before.
3. **Unregistered type: Unsupported.** It is a fourth state outside `AutomationStatus`, which is a content enum. **`AutomationStatus` stays unchanged; there is no content schema change.**
4. **The engine sets the ceiling; the author may only lower it.**
5. **`MechanicsInventory`'s hand-kept `known` set is replaced by `CapabilityRegistry.IsRegistered`.**
6. **A registered type this build kept untyped reads as Reference.** An effect stored below the version that introduced its type (or whose body does not fit) is an `UnknownEffect`: the calculator ignores it, so the registry caps it at Reference whatever the type's ceiling (built in Import Lab Task 2, after the first registry task).
7. **Coverage reads the registry.** `content.coverage` counts each effect of the installed content by `CapabilityRegistry.Effective` (the author's value, with a failing diagnostic on the effect lowering Automatic to Assisted), never by the ceiling alone. A feature with no effects counts once as the registry's `text` entry (Reference). The four states are Automatic, Assisted, Reference and Unsupported. It is read-only and writes nothing; "may need a mechanic" is advice for a feature with no effects whose text holds a generic phrase, never a gate.

**Ceilings follow the code.** A row records what this build's calculator and commands do, proven by a test, not what a plan said they would do. Where the Lab design's first table disagreed with the code (a roll that names a resource: the action command now spends it), the code won.

**Where effective automation shows:** the mechanics report, the Lab's coverage view and the by-hand panel. It is **not** applied to calculation: the calculator keeps reading the author's value, so no calculated number, golden file or revision hash changes. Having the sheet's per-item tags read the effective value would change what users see on existing sheets; it is a separate owner-visible decision.

## Consequences

- A new effect type without a row fails the completeness test (`CapabilityRegistryTests.Every_typed_effect_type_is_registered`, read from the deserializer's own type lists). Each slice that adds a type adds its row, and the test class that proves it.
- `Every_evidence_names_an_existing_test_class` lives in `tests/AppService.Tests`. That project does not reference `RulesCore.Tests`, so it finds those classes in their source files. The build graph is unchanged.
- No stored change and no hash change: the registry is code.

## Alternatives rejected

- **Trust the author's value.** Coverage would be dishonest.
- **A `ceiling` field in content.** That is a content schema change, and what the engine can do is not content.
- **Compute the ceiling from tests at runtime.** Not deterministic, and not offline-friendly.
