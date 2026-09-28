# Roadmap · v0.1

**Planning rule:** milestone exit evidence, rather than speculative dates, determines progress. All 20 ideas are approved for the backlog, not promised in MVP.

| Milestone | Deliverable | Exit gate | Dependencies |
| --- | --- | --- | --- |
| M0 Foundation | Repo, CI, license/attribution review, desktop packaging spike, source/data schemas, test fixtures, first diagram and ADRs | Windows offline shell opens; fixture content persists and exports | None |
| M1 Rules core | Edition packs, revisioned content, effect AST, validation, trace, choices, dice engine | Two rules-family fixture characters calculate and explain outputs. **Delivered 2026-09-26 (v0.2.0):** `M1AcceptanceTests` passes ([features/m1-acceptance.md](features/m1-acceptance.md)) | M0 |
| M2 Usable MVP | Builder, sheet, homebrew subclass studio, PDF attachment/page links, manual content entry, rests, backup/import/export, campaign source policy | All [MVP.md](MVP.md) checks pass on an installed Windows build. **Checks passed 2026-09-28 (v0.3.0), limited content:** every automated check passed on 0.2.2, and the owner reported the owner checks passing on the installed 0.2.2, including a clean-VM install ([features/m2-acceptance.md](features/m2-acceptance.md)). The MVP *goal* is not met yet: only the eight SRD casters build 1–20, and there is one species and one background per family | M1 |
| M2.1 Data safety and reliability | Full library backup and restore (drafts, unused homebrew, campaigns, managed PDFs; package format v6); one TomeStack per data folder; every spellcasting class's attack and save DC with modifiers and a trace; stable e2e gate and a blocking desktop smoke | A clean data folder restored from a full backup matches the original as a whole; a second launch on the same folder hands over to the first and touches nothing. **Done 2026-09-28 (fixture-verified; not yet Windows-install verified on a released build):** `LibraryBackupTests`, `DataFolderTests`, `scripts/single-instance-check.ps1`, the smoke's backup round trip. Owner check left: restore a full backup on a second machine | M2 |
| M2.2 Fighter baseline | SRD Fighter 1–20 in both families with one SRD subclass each, the SRD armor and shield table, and the minimum mechanics a Fighter needs (Extra Attack count, a wider critical range, level-scaled uses; 2024 Weapon Mastery as tracked choices). Pack review extended first (SPEC Q-03). Plan: "M2.2 Fighter baseline" below | A fixture Fighter per family levels 1–20 and passes side-by-side tests; a homebrew Fighter subclass (a synthetic stand-in for the Stardust Guardian) is offered, chosen and played through the studio flow. **Done 2026-09-28 (fixture-verified):** `SrdFighterTests`, `CombatDetailsTests`, the e2e "adds a homebrew Fighter subclass…" flow. Not yet Windows-install verified on a released build | M2.1. **Moved before M3 (owner direction, 2026-09-28):** the Stardust Guardian run needs a Fighter to build on, so Fighter cannot come after it |
| M3 Personal replacement | Stardust Guardian migration; complex resource/action mechanics, multiclass/spellcasting polish, source updates and session feedback | Arlo plays that character end to end without D&D Beyond. **Not met (2026-09-28):** the engineering items are done, but the owner's material and the played session are still needed ([features/m3-acceptance.md](features/m3-acceptance.md)). Synthetic fixtures never meet this gate. **Done:** B1, the acceptance ([features/m3-stardust-guardian.md](features/m3-stardust-guardian.md)), waiting for the owner's material; B2, toggles, shared resources and variable costs ([features/m3-effects.md](features/m3-effects.md)); B3, session gap notes ([features/gap-notes.md](features/gap-notes.md)); C3, combined multiclass spell slots ([features/spellcasting.md](features/spellcasting.md)); C4, printable backup ([features/printable-backup.md](features/printable-backup.md)); C5, "Report a gap" and the notes of all characters; C7, source updates offered on the sheet ([features/publishing-and-updates.md](features/publishing-and-updates.md)) | M2.2 (owner direction 2026-09-28: the character is built on a Fighter) |
| M4 Import intelligence | Page and whole-book extraction, OCR fallback, candidate/entity recognition, review UI, confidence and dependency validation | A third-party test PDF produces reviewable candidates; no unapproved active rules. **Experimental until this gate passes** (MVP.md boundary): the UI and docs call the PDF import experimental until a real third-party PDF run and the SRD detection measurements (precision and recall floors) both pass. **The SRD floors passed on 2026-09-28** (both SRDs, now including the Fighter and armor; `m4-acceptance.md`). The third-party run is still owed. Synthetic fixtures never meet this gate. **Engineering done, gate not met (2026-09-28):** extraction with an OCR fallback in an isolated worker (ADR-009), resumable jobs, rule-based candidates, validated review and the review UI. The original fixture book runs with "active without approval: 0"; the third-party test PDF run is still owed ([features/m4-acceptance.md](features/m4-acceptance.md)) | M2; may run alongside M3 |
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

## Order from here (2026-09-28)

M2.1 (done) → M2.2 Fighter baseline (done) → **M3 Stardust Guardian run and played session** (waiting for the owner's material) → M4's third-party PDF run (can happen any time; it needs only a PDF). M5 starts after the M3 gate, and its template and effect priorities come from the M3 gap notes. Evidence levels (implemented, fixture-verified, Windows-install verified, accepted in real play) are defined in [features/m2-acceptance.md](features/m2-acceptance.md#evidence-levels-m21-2026-09-28).

## M2.2 Fighter baseline (plan)

*Scope: the smallest Fighter that lets the owner enter the real character in M3. The Stardust Guardian material stays out of this public repo (D12); the smoke flow uses an original stand-in.*

**What exists (checked 2026-09-28):** no Fighter content anywhere, and no SRD armor. The engine already has what most Fighter features need. Choices cover Fighting Style and the subclass at level 3. Resources with rest recovery and level formulas cover Second Wind, Action Surge and Indomitable. `extendsChoice` lets a homebrew subclass join the SRD subclass choice, and armor effects (light, medium with the Dex cap, heavy, shield) exist. Missing: an attack count (Extra Attack), a critical range, the heavy-armor Strength requirement, Stealth disadvantage and armor proficiency, and 2024 Weapon Mastery beyond a text property. Ability Score Improvements are reference-only text for every class.

1. **Content and provenance review first (SPEC Q-03).** Extend [licensing/srd-pack-review.md](licensing/srd-pack-review.md) to cover the Fighter class, the Champion subclass (in both SRDs), the armor and shield table (5.1 and 5.2.1), and the Fighting Style options each SRD lists. Record the pages and the CC-BY attribution, as for the casters. No other third-party text.
2. **Minimum mechanics (RulesCore; ADR-003; a content schema bump if older builds would misread the new fields):**
   - an `attacks` field (1 + Extra Attack grants, highest wins), shown next to Attacks and traced;
   - a `criticalRange` field (20 by default; Improved and Superior Critical lower it), used by attack rolls;
   - armor `strength` and `stealthDisadvantage` on `ArmorEffect`, with a sheet warning when unmet (speed −10 in 5.1 and 5.2.1 alike), and an armor-proficiency warning like the weapon one;
   - Weapon Mastery (5.2.1) as a choice of weapon kinds with a level-scaled count, the property shown on attacks. Its effects stay reference text (assisted).
   - Each needs a side-by-side test. Any 2014/2024 difference goes into a `RulesFamilyPolicy` field, never a branch on the family name.
3. **Content packs:** `srd-5.1-fighter.json` and `srd-5.2.1-fighter.json` (class 1–20, Champion, Fighting Styles, feature resources with SRD uses), and the armor rows in the equipment packs. Second Wind is `1d10 + LEVEL` in 5.1; in 5.2.1 it has 2 uses at level 1, rising with level, and Tactical Mind/Shift are reference text. Action Surge gives a second use at 17. Indomitable has 1/2/3 uses at 9/13/17.
4. **Levels and choices:** subclass at 3; ASIs at 4, 6, 8, 12, 14, 16 and 19, still reference text with the manual step (as for the casters; an ASI effect is a separate item). The 2024 level-1 Weapon Mastery count and the Fighting Style feat.
5. **Equipment interactions:** starting armor and shield, AC traces for each armor category, the Strength warning, and a Stealth roll with disadvantage noted. Two-weapon and versatile damage already work.
6. **Side-by-side fixtures:** `SrdFighterTests` follows `SrdCasterTests`, with one Fighter per family at levels 1, 3, 5, 11, 17 and 20. It checks resources, attacks, critical range, AC in chain mail with a shield, and the choices offered. `RulesFamilySideBySideTests` gains any new policy field.
7. **Stardust Guardian smoke flow (synthetic):** an e2e flow authors an *original* Fighter subclass, "Test Starward Warden", in the studio. It has a modifier, a limited-use resource, an assisted roll and a reference feature. It extends `fighter-subclass`, is chosen at level 3, played through a short and a long rest, and backed up with "Back up everything". The real material runs only through `StardustGuardianAcceptanceTests` from the gitignored `tests/RulesFixtures/local/`.

**Before Arlo can enter the real character**, these must be done: the Fighter class and its level-3 subclass choice (1, 3, 4), because a homebrew subclass needs a base class to extend; the armor table with its warnings (1, 2, 5), because Fighters are the main armor users; and the attack count (2). The critical range and Weapon Mastery can be tracked by hand at first; that is the manual step, and the gap notes record it.

## M2 status (2026-09-28: checks passed as 0.3.0, limited content)

The owner checks on the installed 0.2.2 passed on 2026-09-28: the upgrade from 0.2.0, the keyboard and Narrator passes, the viewer landing on the cited page, a clean-VM install and an SRD play rehearsal. By owner decision, the synthetic stand-in meets DoD 3 for M2, and the real Stardust Guardian is the M3 gate.

### Earlier status (2026-09-27, v0.2.2: exit candidate)

All M2 slices are done and every automated MVP check passes ([features/m2-acceptance.md](features/m2-acceptance.md)). What remains are the owner checks on the installed 0.2.2: the installer upgrade from 0.2.0, the keyboard and Narrator passes, the viewer landing on the cited page, and the real Stardust Guardian. The clean-VM checks cannot be done on the development machine. The notes below record how the slices went.

### Earlier status (v0.2.1)

Items 1–7 are done: builder, sheet for play, long rest (D01), equipment and armor, homebrew studio with update review, PDF attachments with page navigation (database schema 3), and campaign profiles (database schema 4, package format v4). Still needed for the M2 exit gate:

- **Spellcasting (D04): done**: engine (content schema v5, character schema v6, [features/spellcasting.md](features/spellcasting.md)), the SRD spells, and the eight SRD casters levels 1–20 in both families. **Still not bundled:** Fighter, Monk, Rogue and Barbarian levels 4–20, which MVP's "level-1-to-20 SRD-based character" goal needs for the non-caster classes. The original plan was: a `spellcasting` effect on a class (ability, save DC and attack formula, prepared or known, and slots per class level as a table), spell content kind and picker, slots as play state recovered by the long rest, and the "one spellcasting class" rule with a manual step for a second one. A new `spellcasting` effect *type* needs no content schema bump (ADR-003), but known or prepared spells and spent slots on the character are character schema v5. It also needs SRD spell text with a pack review update (SPEC Q-03), and a side-by-side test for any 2014/2024 difference.
- ~~Multiclass prerequisites and proficiency subsets (D04); weapons, attacks and damage (C-02, C-04).~~ Engine done (content v5, [features/multiclass-and-attacks.md](features/multiclass-and-attacks.md)); the SRD weapon table and the classes' data come with the SRD content. ~~Short rest and hit dice (D01 follow-up); death saves and inspiration (C-05).~~ Done (character schema v5, [features/rests.md](features/rests.md)).
- **SRD scope (owner, 2026-09-27):** the full SRD casters, levels 1–20, in both families, with their spells and the weapon table, under an extended pack review (SPEC Q-03). That is several content commits after the spellcasting engine.
- ~~Manual content entry tied to pages is possible in the studio (page field); PDF page-range import as reference (I-01, I-03) is not started.~~ Done: page ranges and whole documents become draft reference entries, reviewed and published in the studio; no text extraction in M2 (owner decision 2026-09-27; [features/pdf-attachments.md](features/pdf-attachments.md)).
- Owner and clean-machine checks: the installed build on a clean VM (ADR-008), the viewer landing on the cited page, keyboard and Narrator passes, and the real Stardust Guardian character (DoD 3).
