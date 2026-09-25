# ADR-002: Edition-aware content IDs and revision pins

Status: accepted for M0 (effect model deferred to ADR-003)
Date: 2026-09-24

## Context

SPEC S-02, S-03, C-01 and I-06 require that SRD 5.1 (2014) and SRD 5.2.1 (2024) never be merged by name. They also require that published content is immutable and that characters stay on the revision they were built with.

## Decision

- Rules families are the string IDs `srd-5.1` and `srd-5.2.1` (`RulesFamilies`). A character has exactly one. Content declares a list of compatible families, and "both" must be stated explicitly.
- Each difference between families is a named field on `RulesFamilyPolicy`. The first one is `AbilityIncreaseSource`: species under 2014 rules, background under 2024 rules. Differences are never inferred from names.
- Content identity is `(contentId, revisionId)` UUIDs. Characters store exact `ContentReference` pins. Display names are never identity.
- Only `published` revisions affect calculations. Content that is draft, from the wrong family, missing, or without a source is isolated with a diagnostic instead of failing the sheet.
- Stored revisions are insert-only. Re-adding identical JSON is a no-op. Different JSON under the same `revisionId` is rejected (`ImmutableRevisionException`, `package.revision-conflict`).
- Unknown JSON fields on characters, revisions and effects round-trip through `[JsonExtensionData]`.

## Consequences

- Deliberate cross-family use (backlog B06) will need a recorded, per-character exception. There is no silent mixing today.
- A revision's JSON hash is computed from the app's own serializer. Changing the serializer settings (for example property order) would make identical content look different, so treat them as a schema change.
- Migration and update review between revisions (SPEC I-06) is not built yet.

## Alternatives considered

Name-based matching with a per-edition flag was rejected by SPEC S-03. Storing mutable content with a version counter was rejected because pinned characters would change silently.

## Evidence

`tests/RulesCore.Tests/InitiativeTraceTests.cs`, `tests/AppService.Tests/PersistenceAndDispatchTests.cs`, `tests/AppService.Tests/PackageRoundTripTests.cs`.
