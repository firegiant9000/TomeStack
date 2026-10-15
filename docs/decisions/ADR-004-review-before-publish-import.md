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
- **M4 D4 (2026-09-28): acceptance validates first.** `import.candidate.accept` runs `ContentValidator` on the draft the candidate would become, and refuses on any error. Low-confidence fields and unresolved references block it, until the candidate is edited or accepted as reference only (without its effects). It still yields only a draft (`features/pdf-import.md`).
- **Enforcement fix (M4 D4, found before release):** `ToDraftRevision` now reads the draft back before forcing effects to reference. A candidate held outside a revision carries versioned effect types (spell, weapon, armor) as unknown effects. Setting `automation` on those never reached their JSON, so the stored draft read back with an **automatic** weapon or armor effect. Evidence: `ImportQuarantineTests.A_stored_candidates_versioned_effects_stay_reference_only_in_the_draft_and_after_it_is_read_back`.
- **D33 (2026-10-14): a confirmed preview of the exact drafts is review.** Review used to mean one entry at a time. For many drafts it means this: `content.publishManyPreview` shows each pending draft (newest revision is a draft) with its validation report, the debugger's findings and the re-pointing it needs, writes nothing, and refuses to say "can publish" while any error exists. `content.publishMany` then needs `confirm: true` **and** the pending draft revision ids the preview showed (`expected`); inside its one transaction it re-reads the pending set and refuses with `content.publish-set-changed`, writing nothing, unless the two are equal. A retry or a double press finds nothing pending and writes nothing. Nothing becomes active without a person reading what will be published and confirming it. Everything is all-or-nothing, and published revisions stay insert-only: grants and choice options that pin a draft or an older revision of something published in the batch are re-pointed by saving a new draft and publishing that, never by editing a revision. A draft of a dependent that the request did not name is an error, never swept in. The preview is the real publish run inside a transaction that is rolled back (ruling R50), so it reports what publishing would refuse. **Residual (ruling R51):** `expected` binds the requested drafts only. The dependents (published content that is re-pointed and published again) are found, re-checked and validated at publish time, but they are not bound to the preview: a dependent that changed between preview and confirm is published as it then is, or the whole batch is refused if it no longer validates. Bulk accept (`import.candidate.acceptMany`) is the same per-candidate acceptance (provenance, blockers, validation) for up to 200 candidates in one transaction; blocked ones are returned with their codes and never forced. Evidence: `PublishManyTests`, `CandidateReviewTests` (accept many).

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
