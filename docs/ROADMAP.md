# Roadmap · v0.1

**Planning rule:** milestone exit evidence, rather than speculative dates, determines progress. All 20 ideas are approved for the backlog, not promised in MVP.

| Milestone | Deliverable | Exit gate | Dependencies |
| --- | --- | --- | --- |
| M0 Foundation | Repo, CI, license/attribution review, desktop packaging spike, source/data schemas, test fixtures, first diagram and ADRs | Windows offline shell opens; fixture content persists and exports | None |
| M1 Rules core | Edition packs, revisioned content, effect AST, validation, trace, choices, dice engine | Two rules-family fixture characters calculate and explain outputs. **Delivered 2026-09-26 (v0.2.0):** `M1AcceptanceTests` passes ([features/m1-acceptance.md](features/m1-acceptance.md)) | M0 |
| M2 Usable MVP | Builder, sheet, homebrew subclass studio, PDF attachment/page links, manual content entry, rests, backup/import/export, campaign source policy | All [MVP.md](MVP.md) checks pass on an installed Windows build | M1 |
| M3 Personal replacement | Stardust Guardian migration; complex resource/action mechanics, multiclass/spellcasting polish, source updates and session feedback | Arlo plays that character end to end without D&D Beyond | M2 |
| M4 Import intelligence | Page and whole-book extraction, OCR fallback, candidate/entity recognition, review UI, confidence and dependency validation | A third-party test PDF produces reviewable candidates; no unapproved active rules | M2; may run alongside M3 |
| M5 Creation power | Full custom base classes, arbitrary progression, sandbox/diff/debugger, templates, design feedback toggle | A nonstandard class levels and multiclasses without code edits | M1–M4 |
| M6 Sharing and extension | Versioned pack format, campaign packs, plugin SDK sandbox, export adapters, documentation | External sample extension and safe cross-machine round trip | M5 |
| M7 Expanded tabletop | Monsters/DM content, full PDF search, printable cards/PDF layouts, deeper accessibility/themes, optional local AI | Separate acceptance plans for each module | M4–M6 |

## Delivery slices within M0–M2

1. Persist a manually entered level-one character from each edition.
2. Calculate a stat and show source-aware trace; add one manual override.
3. Add one subclass feature with a resource and one roll; use it on the sheet.
4. Add level-up, multiclass validation and spellcasting for the supported SRD fixtures.
5. Add rest preview/commit and PDF page jump.
6. Add campaign source filters and export/import on a clean installation.

Each slice includes data migration strategy and an executable acceptance example. The M2 UI should be rehearsed in actual play before adding broad importer intelligence.

## Major risks and responses

| Risk | Response / earliest proof |
| --- | --- |
| Rules breadth across two editions | Separate policy packs from day one; make side-by-side fixture tests in M1 |
| Homebrew semantics exceed effect model | `assisted` and `reference` modes plus diagnostic traces; exercise Stardust Guardian in M2/M3 |
| PDF variability and rights | Propose/review pipeline; OCR fallback; source/license metadata; no third-party redistribution |
| Local host/package complexity | Windows installer and IPC spike in M0 before committing to shell |
| Corruptions or lost attachments | Transactions, upgrade backups, hashes and fresh-machine restore drill |
| Scope explosion | Require exit gate and backlog ID before promoting an M5–M7 feature |

## Immediate issue queue (M2 kickoff, 2026-09-26)

Done since v0.1: SRD fixtures for both families with a cross-edition conflict (M1 item 1; spellcasting is scoped by D04), the data directory and PDF default (D02, ADR-005), the shell and transport (ADR-006), and the typed effect and formula schemas (ADR-003).

- ~~Builder UI over the M1 API: class levels, `character.choose`, and a level-up draft (SPEC C-01, C-07).~~ Done (M2 item 1, [features/builder.md](features/builder.md)); multiclass prerequisites remain.
- Sheet UI: features list with text, resources, the `roll` command, and the "Choices to make" answers.
- Authoring and update-review UI over `content.saveDraft` / `publish` / `affected` / `reviewUpdate` (SPEC I-04, I-06).
- PDF attachments and the `pdfRef` → attachment migration (ADR-005), with page navigation (SPEC S-04, B05).
- Make the Stardust Guardian acceptance fixture (in the gitignored `tests/RulesFixtures/local/`) with at least one mechanic that cannot be fully automated.
