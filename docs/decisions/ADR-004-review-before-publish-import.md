# ADR-004: Review before publish for imported content

Status: accepted (contract and enforcement). The publish command, which validates first, is built (M1 item 2); the review UI is M2/M4.
Date: 2026-09-25

## Context

SPEC I-01, I-02 and I-06 and ARCHITECTURE "Import lifecycle": PDF extraction and candidate detection produce **draft candidates, never active rules**. Confidence is a UI hint, never permission to publish. SPEC Q-02 treats every imported document as untrusted.

## Decision

- The import worker can only produce `DraftCandidate`s. `CandidateQuarantine.ToDraftRevision` is the only conversion, and it always yields `status: draft` with every effect forced to `automation: reference`. No overload or flag produces a published revision.
- Publishing is a separate command (`content.publish`, M1 item 2). It re-runs schema, reference, formula and cycle validation (`ContentValidator`), refuses on any error, and creates a new immutable revision (ADR-002) with a new revision id. The draft is kept unchanged.
- The calculator applies only `published` revisions. Drafts are isolated with a `content.unpublished` diagnostic and never contribute to a trace.
- The same rule holds for packages: drafts inside a package stay drafts and inactive after import. A *published* revision in a package becomes active, so each new one gets the same validation as `content.publish`, shown in the import preview. For content schema v3 (always validated before publishing), errors refuse the import. For older revisions (published by v0.1, before validation existed) they are warnings, and the calculator isolates what it cannot apply (`docs/features/validation-and-restrictions.md`, 2026-09-26 M1 review).
- Imported content never executes code. Effects are declarative data, and formulas use a bounded grammar with no `eval` (ADR-003).
- **M4 D3 (2026-09-28):** `DraftCandidate` also carries `unresolvedReferences`, `fields`, `lowConfidenceFields` and a `summary`. Detection is rule-based (`CandidateDetector`) and stores candidates per import job, locally, for review. It never writes a revision.

## Consequences

- Even a 0.99-confidence candidate needs a person to accept it. This is intended.
- The M2/M4 review UI must show the excerpt, the page and the proposed effects, and it must call a publish command that re-validates.

## Alternatives considered

- Auto-publishing above a confidence threshold: rejected by SPEC I-02 and ARCHITECTURE ("confidence is a UI hint").

## Evidence

- `tests/AppService.Tests/ImportQuarantineTests.cs`: `Imported_candidate_becomes_an_inactive_draft_even_with_high_confidence`.
- `tests/RulesCore.Tests/InitiativeTraceTests.cs`: `Draft_revision_never_affects_calculation`.
- `tests/AppService.Tests/PackageRoundTripTests.cs`: `Draft_revisions_stay_inactive_after_import`.
- `tests/AppService.Tests/PackageLimitTests.cs`: `Published_v3_revision_that_fails_validation_is_rejected`, `Published_v2_revision_that_fails_validation_imports_with_a_warning`.
