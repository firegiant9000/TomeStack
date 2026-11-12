# Decisions (ADR index)

One row per architecture decision record. The status is the first words of each file's status line; open the file for the full text. Process is in [LIVING_SPECS](../LIVING_SPECS.md#adr-template).

| ADR | Title | Status |
|---|---|---|
| [001](ADR-001-local-only-windows-release.md) | Local-only Windows release | accepted for M0 to M2 |
| [002](ADR-002-edition-aware-ids-and-revision-pins.md) | Edition-aware content IDs and revision pins | accepted |
| [003](ADR-003-declarative-effect-ast.md) | Declarative effect AST and bounded formulas | accepted |
| [004](ADR-004-review-before-publish-import.md) | Review before publish for imported content | accepted |
| [005](ADR-005-managed-pdf-attachment-vs-link.md) | Managed PDF attachment versus external link | accepted |
| [006](ADR-006-desktop-host-and-ipc.md) | Desktop host and UI to service transport | accepted |
| [007](ADR-007-export-package-and-license-policy.md) | Export package and license policy | accepted |
| [008](ADR-008-installer-and-distribution.md) | Installer and distribution | accepted |
| [009](ADR-009-pdf-extraction-and-ocr.md) | PDF extraction and OCR | accepted |
| [010](ADR-010-custom-classes-and-progression.md) | Custom base classes and arbitrary progression (content schema v9) | accepted |
| [011](ADR-011-extension-api.md) | Extension API (M6, SPEC P-05) | accepted (option A, declarative only) |
| [012](ADR-012-export-adapters.md) | Export adapters for virtual tabletops (B20) | accepted |
| [013](ADR-013-third-party-character-sheet-import.md) | Third-party character sheet import | accepted |
| [014](ADR-014-sheet-tabs.md) | Sheet navigation: an in-sheet tablist with every panel kept mounted | accepted |
| [015](ADR-015-visual-language-and-shell-layout.md) | Visual language and shell layout | accepted |
| [016](ADR-016-assisted-class-import.md) | Assisted class import | accepted |
| [017](ADR-017-character-import-from-text.md) | Character import from text | accepted |
| [018](ADR-018-play-mechanics.md) | A reusable play-mechanics engine | accepted |
| 019 | (never used, see below) | none |
| [020](ADR-020-capability-registry.md) | Capability registry and coverage | accepted |
| [021](ADR-021-mechanics-beyond-d43.md) | Mechanics beyond the play-mechanics engine (MECH-X) | accepted |
| [022](ADR-022-content-v12.md) | Content schema v12, one slice at a time | accepted |
| [023](ADR-023-down-conversion-export.md) | Save for an older TomeStack (down-conversion export) | accepted |
| 024 | Content schema v13 (reserved for Wave 3, PR #160) | not yet merged |
| [025](ADR-025-code-signing.md) | Code signing for the installer | **proposed** (waiting for SignPath Foundation) |

## Numbering gaps

- **ADR-019 was never used.** The number was reserved during parallel work and no record was written under it. It is not reissued.
- **ADR-025 was ADR-024.** Code signing was merged as ADR-024 while the content schema v13 record (PR #160) already held that number on its branch. Code signing was renumbered to ADR-025 on 2026-11-12; the 0.8.1 release notes still say ADR-024.
- **D-row numbers skip.** The D-rows in [LIVING_SPECS](../LIVING_SPECS.md#pending-product-decisions) are reserved in blocks by parallel lanes (for example D43 to D49 for the play-mechanics plan, with D46 to D49 unused, and D60 to D69 for the Homebrew Import Lab, with D65 unused and D68 without a row). An unused reservation is not reissued, so a missing number is not a lost decision.
