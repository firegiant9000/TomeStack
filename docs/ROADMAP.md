# Roadmap · v0.1

**Planning rule:** milestone exit evidence, rather than speculative dates, determines progress. All 20 ideas are approved for the backlog, not promised in MVP.

| Milestone | Deliverable | Exit gate | Dependencies |
| --- | --- | --- | --- |
| M0 Foundation | Repo, CI, license/attribution review, desktop packaging spike, source/data schemas, test fixtures, first diagram and ADRs | Windows offline shell opens; fixture content persists and exports | None |
| M1 Rules core | Edition packs, revisioned content, effect AST, validation, trace, choices, dice engine | Two rules-family fixture characters calculate and explain outputs. **Delivered 2026-09-26 (v0.2.0):** `M1AcceptanceTests` passes ([features/m1-acceptance.md](features/m1-acceptance.md)) | M0 |
| M2 Usable MVP | Builder, sheet, homebrew subclass studio, PDF attachment/page links, manual content entry, rests, backup/import/export, campaign source policy | All [MVP.md](MVP.md) checks pass on an installed Windows build. **Exit candidate (v0.2.2):** every automated check passes ([features/m2-acceptance.md](features/m2-acceptance.md)); the owner checks on the installed build are pending, and 0.3.0 follows them | M1 |
| M3 Personal replacement | Stardust Guardian migration; complex resource/action mechanics, multiclass/spellcasting polish, source updates and session feedback | Arlo plays that character end to end without D&D Beyond. **Started:** B1, the acceptance ([features/m3-stardust-guardian.md](features/m3-stardust-guardian.md)), waiting for the owner's material | M2 |
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
- ~~Sheet UI: features list with text, resources, the `roll` command, and the "Choices to make" answers.~~ Done (M2 items 1–2, [features/sheet-play.md](features/sheet-play.md)).
- ~~Authoring and update-review UI over `content.saveDraft` / `publish` / `affected` / `reviewUpdate` (SPEC I-04, I-06).~~ Done (M2 item 5, [features/homebrew-studio.md](features/homebrew-studio.md)).
- ~~PDF attachments and the `pdfRef` → attachment migration (ADR-005), with page navigation (SPEC S-04, B05).~~ Done (M2 item 6, [features/pdf-attachments.md](features/pdf-attachments.md)); owner check: the viewer lands on the cited page.
- Make the Stardust Guardian acceptance fixture (in the gitignored `tests/RulesFixtures/local/`) with at least one mechanic that cannot be fully automated. *(The test is ready (M3 B1); the owner puts a backup export there, see [features/m3-stardust-guardian.md](features/m3-stardust-guardian.md).)*

## M2 status (2026-09-27, v0.2.2: exit candidate)

All M2 slices are done and every automated MVP check passes ([features/m2-acceptance.md](features/m2-acceptance.md)). What remains are the owner checks on the installed 0.2.2: the installer upgrade from 0.2.0, the keyboard and Narrator passes, the viewer landing on the cited page, and the real Stardust Guardian. The clean-VM checks cannot be done on the development machine. The notes below record how the slices went.

### Earlier status (v0.2.1)

Items 1–7 are done: builder, sheet for play, long rest (D01), equipment and armor, homebrew studio with update review, PDF attachments with page navigation (database schema 3), and campaign profiles (database schema 4, package format v4). Still needed for the M2 exit gate:

- **Spellcasting (D04): done**: engine (content schema v5, character schema v6, [features/spellcasting.md](features/spellcasting.md)), the SRD spells, and the eight SRD casters levels 1–20 in both families. **Still not bundled:** Fighter, Monk, Rogue and Barbarian levels 4–20, which MVP's "level-1-to-20 SRD-based character" goal needs for the non-caster classes. The original plan was: a `spellcasting` effect on a class (ability, save DC and attack formula, prepared or known, and slots per class level as a table), spell content kind and picker, slots as play state recovered by the long rest, and the "one spellcasting class" rule with a manual step for a second one. A new `spellcasting` effect *type* needs no content schema bump (ADR-003), but known or prepared spells and spent slots on the character are character schema v5. It also needs SRD spell text with a pack review update (SPEC Q-03), and a side-by-side test for any 2014/2024 difference.
- ~~Multiclass prerequisites and proficiency subsets (D04); weapons, attacks and damage (C-02, C-04).~~ Engine done (content v5, [features/multiclass-and-attacks.md](features/multiclass-and-attacks.md)); the SRD weapon table and the classes' data come with the SRD content. ~~Short rest and hit dice (D01 follow-up); death saves and inspiration (C-05).~~ Done (character schema v5, [features/rests.md](features/rests.md)).
- **SRD scope (owner, 2026-09-27):** the full SRD casters, levels 1–20, in both families, with their spells and the weapon table, under an extended pack review (SPEC Q-03). That is several content commits after the spellcasting engine.
- ~~Manual content entry tied to pages is possible in the studio (page field); PDF page-range import as reference (I-01, I-03) is not started.~~ Done: page ranges and whole documents become draft reference entries, reviewed and published in the studio; no text extraction in M2 (owner decision 2026-09-27; [features/pdf-attachments.md](features/pdf-attachments.md)).
- Owner and clean-machine checks: the installed build on a clean VM (ADR-008), the viewer landing on the cited page, keyboard and Narrator passes, and the real Stardust Guardian character (DoD 3).
