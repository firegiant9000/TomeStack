# ADR-004: Review before publish for imported content

Status: accepted (contract and enforcement). The review UI and the publish command are M2/M4.
Date: 2026-09-25

## Context

SPEC I-01, I-02 and I-06 and ARCHITECTURE "Import lifecycle": PDF extraction and candidate detection produce **draft candidates, never active rules**. Confidence is a UI hint, never permission to publish. SPEC Q-02 treats every imported document as untrusted.

## Decision

- The import worker can only produce `DraftCandidate`s. `CandidateQuarantine.ToDraftRevision` is the only conversion, and it always yields `status: draft` with every effect forced to `automation: reference`. No overload or flag produces a published revision.
- Publishing is a separate, user-confirmed command (not built yet). It creates a new immutable revision (ADR-002) after schema and reference validation.
- The calculator applies only `published` revisions. Drafts are isolated with a `content.unpublished` diagnostic and never contribute to a trace.
- The same rule holds for packages: drafts inside a package stay drafts and inactive after import.
- Imported content never executes code. Effects are declarative data, and formulas use a bounded grammar with no `eval` (ADR-003).

## Consequences

- Even a 0.99-confidence candidate needs a person to accept it. This is intended.
- The M2/M4 review UI must show the excerpt, the page and the proposed effects, and it must call a publish command that re-validates.

## Alternatives considered

- Auto-publishing above a confidence threshold: rejected by SPEC I-02 and ARCHITECTURE ("confidence is a UI hint").

## Evidence

- `tests/AppService.Tests/ImportQuarantineTests.cs`: `Imported_candidate_becomes_an_inactive_draft_even_with_high_confidence`.
- `tests/RulesCore.Tests/InitiativeTraceTests.cs`: `Draft_revision_never_affects_calculation`.
- `tests/AppService.Tests/PackageRoundTripTests.cs`: `Draft_revisions_stay_inactive_after_import`.
