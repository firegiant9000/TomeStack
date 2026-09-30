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
| M5 Creation power | Full custom base classes, arbitrary progression, sandbox/diff/debugger, templates, design feedback toggle. Plan: "M5 plan" below | A nonstandard class levels and multiclasses without code edits. **In progress. Plan approved and ADR-010 accepted by the owner (2026-09-28). Exit gate fixture-verified (2026-09-29): slices 1a and 1b (the rules core with content v9, and the studio's class editor), shown by RulesCore and AppService `CustomClassTests` and the e2e flow that authors a class in the studio and builds it at levels 1, 20 and 5/3 with an SRD class (levels 20 and 5/3 are created through the service, not levelled up in the UI). Not Windows-install verified. v9 was approved by the owner (2026-09-29); merging the PRs still waits for the owner. M5 is not delivered until slices 2–8 are done. Slices 2 (the homebrew debugger), 3 (the draft sandbox), 4 (compare revisions), 5 (the relationship tree), 6 (templates), 7 (design feedback) and 8 (snapshots) done 2026-09-29, fixture-verified (`SnapshotTests`, the e2e snapshot flow; `DesignFeedbackTests`, `DesignFeedbackCommandTests`, the e2e feedback flow; `templates.test.ts`, the e2e templates flow; `ContentTreeTests`, `ContentTreeCommandTests`, the e2e keyboard-tree flow; `ContentTextDiffTests`, `CompareTests`, the e2e compare flow; `DraftOverlayCatalogTests`, `SandboxTests`, the e2e "Try it" flow; `ContentDebuggerTests`, `ContentDiagnoseTests`, the e2e debugger flow); the owner decisions for slices 3–8 are recorded (LIVING_SPECS D14)** | M1–M4 engineering. **Started before the M3 gate (owner direction, 2026-09-28, LIVING_SPECS D13);** priorities that were to come from the M3 gap notes are provisional |
| M6 Sharing and extension | Versioned pack format, campaign packs, plugin SDK sandbox, export adapters, documentation. Plan: "M6 plan" below | External sample extension and safe cross-machine round trip. **In progress. Plan approved by the owner (2026-09-28); ADR-011 (option A, declarative only) and ADR-012 (Foundry `dnd5e` and neutral JSON, Roll20 deferred) accepted 2026-09-29 (LIVING_SPECS D14). Slice 1 (source packs, the import-derived flag, "Mark as shareable") is built and fixture-verified on its PR (`SourcePackTests`, the e2e source-pack flow), and waits for the owner to approve the new source fields, database v8 and package format v7 before it merges. Not Windows-install verified.** | M5 (slice 1 for the pack format; the export adapters need only the sheet, see the plan) |
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

M2.1 (done) → M2.2 Fighter baseline (done) → **M3 Stardust Guardian run and played session** (waiting for the owner's material) → M4's third-party PDF run (can happen any time; it needs only a PDF).

**Changed by owner direction, 2026-09-28 (LIVING_SPECS D13):** M5 engineering starts now, in parallel with the wait for the M3 material, instead of after the M3 gate. The M3 gate is unchanged: it stays "not met" until the Stardust Guardian run and a played session, and M4 stays experimental until a third-party PDF run. What was to come from the M3 gap notes is **provisional**: the B15 template set and new effect types from [features/m3-effects.md](features/m3-effects.md) "Not yet". M5 builds the mechanisms with a small, revisable concrete set, and the gap notes re-rank them when they arrive. Then M6, whose export adapters and pack format can start before the M5 exit gate (see "M6 plan").

Earlier text (kept for the record): "M5 starts after the M3 gate, and its template and effect priorities come from the M3 gap notes." Evidence levels (implemented, fixture-verified, Windows-install verified, accepted in real play) are defined in [features/m2-acceptance.md](features/m2-acceptance.md#evidence-levels-m21-2026-09-28).

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

## M5 Creation power (plan, approved by the owner 2026-09-28)

*Scope: a user builds a class that is not in any SRD, in the studio, with no code edits, and gets tools to check it. Started before the M3 gate (D13). The template set (slice 6) and any new effect types are provisional until the M3 gap notes arrive. Every fixture is original; no SRD text is added (SPEC Q-03). No bundled revision changes bytes.*

**What exists (checked against the code 2026-09-28):**

- **A class is already data.** `ContentKind.Class` carries `hitDie` (d6–d12), level-gated `grant` and `choice` (v3), `restriction` with `multiclass` and `group` plus `onlyAs` subsets (v5), `resource` and `recovery` with `CLASS_LEVEL` formulas, and `spellcasting` with 20-row slot tables (v5). Nothing in `RulesCore` names an SRD class. The builder offers every published class of the family (`CharacterBuilder.tsx`), so a published homebrew class would already be pickable.
- **Gaps for a nonstandard class:**
  - The multiclass caster share is a closed enum, `full`, `half` or `third` (`Effects.cs` `MulticlassCaster`, `Calculation.cs` slot combining). A 2/3 or 1/4 caster cannot be expressed.
  - A per-level column (for example "2, 2, 3, 3, 4 …") has no name. It has to be written as floor arithmetic in each formula that uses it, and the sheet cannot show it as a class-table column.
  - The studio authors only subclasses, features, feats and items (`HomebrewStudio.tsx` `authorable`), with editors for modifier, resource, recovery, roll, grant and armor. There is no class kind and no editor for `hitDie`, `choice`, `restriction`, `onlyAs` or `spellcasting`.
  - Skill choices in the SRD packs are option features, each granting one proficiency. The studio cannot generate them.
- **Rules constants that stay:** the proficiency bonus is by total level (`Calculation.cs`); total level ≤ 20; hit points are the die maximum and then the fixed value.
- **Reusable:**
  - `ContentValidator` and `ValidationReport` diagnostics, for the debugger;
  - `character.preview` and `previewChoice`, for the sandbox;
  - `ContentDiff`, `character.reviewUpdate` and `UpdateReviewPanel`, for the diff and before/after;
  - `ValidationReport.RequiredSchemaVersion` (M2.2), for minimum-version publishing.
- **Absent:** templates, character snapshots (characters are mutable rows, and the archive is only a mark, `Archive.cs`), and any graph view.

**Slices, in dependency order:**

1. **Custom base classes and arbitrary progression ([ADR-010](decisions/ADR-010-custom-classes-and-progression.md)).** Two PRs, because the schema bump should be reviewed on its own:
   - **1a. Rules core, content schema v9.**
     - Adds the `scale` effect (a named 20-row integer column on a class or subclass), the formula identifier `SCALE.<id>`, and `spellcasting.multiclassCasterTable` (20 caster levels, one per class level).
     - Adds `validate.requires-v9` and `content-revision.v9.schema.json`, and extends `RequiredSchemaVersion`.
     - Proven by RulesCore side-by-side tests on an original fixture class, "Test Chronicler" (d8, a scaling Ink resource, and a 2/3 caster by table), in both families at levels 1, 3, 5, 11, 17 and 20.
     - Multiclass tests pair it with a fixture full caster, a half caster and a non-caster, in both families.
     - **Stop point: the owner approves before v9 merges.** Approved (owner, 2026-09-29), with 1b's choice with no declared options; merging still waits for the owner.
   - **1b. The studio authors a class.**
     - "New class", with the hit die and a level table: features and choices per level, and the subclass level.
     - A skill-choice helper that generates the option features, saving throws, and starting-class and multiclass proficiency subsets (`onlyAs`).
     - Multiclass prerequisites (`restriction`, `multiclass`, `group`), and resources driven by a scale.
     - A spellcasting editor: ability, list key, preparation, the 20-row slot table, and the multiclass share (none, full, half, third or a table).
     - Proven by `HomebrewStudioTests` and an e2e flow: author the Chronicler in the studio and build it at levels 1, 20 and 5/3 with an SRD Wizard (levels 20 and 5/3 are created through the service client; the SRD Fighter multiclass and the package round trip are service-level tests).
2. **B02 homebrew debugger.** **Done 2026-09-29 (fixture-verified; [features/homebrew-studio.md](features/homebrew-studio.md#the-homebrew-debugger-m5-slice-2-b02)).**
   - A read-only `content.diagnose { sourceId | reference | revision }` over a new RulesCore `ContentGraph` (grants, choices, resources, recoveries, rolls, scales, levels).
   - It reports missing references and invalid formulas, and flags dead resources (never recovered and never spent) and unreachable features (a level above 20; a non-standalone feature, one that reads CLASS_LEVEL or a class column, that no class reaches). **As built:** "a choice that nothing reaches" became two checks: a choice with nothing to pick (`debug.choice-empty`), and a subclass that nothing offers (`debug.subclass-unreachable`). The content a choice sits on is covered by the feature check.
   - It covers undefined or unused scales, per draft and per source. Each item links to its effect in the studio. It writes nothing.
3. **B03 character sandbox.** **Done 2026-09-29 (fixture-verified; [features/homebrew-studio.md](features/homebrew-studio.md#try-it-the-draft-sandbox-m5-slice-3-b03)).** The invariant now reads "only published revisions affect saved characters" (SPEC I-06, CLAUDE.md).
   - "Try it" in the studio: a draft class or subclass at chosen levels, on an in-memory copy of a character or on a blank one.
   - It reuses `character.preview` and saves nothing.
   - **Owner decision:** previewing a *draft* needs an in-memory overlay that treats that one draft as published for one calculation. That touches the invariant "only published revisions calculate" (see "What I must decide").
4. **B04 before/after tests and B07 diff viewer.** **Done 2026-09-29 (fixture-verified; [features/homebrew-studio.md](features/homebrew-studio.md#compare-revisions-diff-and-beforeafter-m5-slice-4-b07-and-b04)).** The shipped app has no sample fixtures, so a blank character stands in for them.
   - Compare any two revisions of a content by mechanics (`ContentDiff`, by effect id) and by text.
   - Run both revisions on chosen characters: your own, unchanged copies, or the bundled original sample fixtures. The field changes come from the `reviewUpdate` computation, without applying anything.
   - Drafts use slice 3's overlay.
5. **B19 relationship graph.** **Done 2026-09-29 (fixture-verified; [features/homebrew-studio.md](features/homebrew-studio.md#relationships-the-content-tree-m5-slice-5-b19); accessibility checklist item 22; the Narrator pass is an owner check).** Class → level → feature → resource → roll or recovery, from slice 2's `ContentGraph`.
   - It is a keyboard-navigable tree (the WAI-ARIA tree pattern, or nested lists of links), not a canvas.
   - WCAG 2.2 AA items from [features/accessibility-checklist.md](features/accessibility-checklist.md). A Narrator pass is an owner check.
6. **B15 templates (provisional).** **Done 2026-09-29 (fixture-verified; [features/homebrew-studio.md](features/homebrew-studio.md#templates-m5-slice-6-b15-provisional)).** The skeletons' "features at the subclass levels" and "reference-only Ability Score Improvement features" are empty grant slots, because a template creates no content of its own. Their levels are fixed, provisional defaults (3/6/10/14 and 4/8/12/16/19), not taken from the class or family. Four templates that create **drafts only**:
   - a resource with a recovery;
   - a toggled stance (toggle, `whileActive` modifier, resource);
   - a subclass skeleton (`extendsChoice` and features at the class's subclass levels);
   - a class skeleton (hit die, level-1 proficiencies, a subclass choice, and reference-only Ability Score Improvement features).
   
   The templates are original data with no SRD text, and the M3 gap notes revise the set.
7. **Design feedback toggle.** **Done 2026-09-29 (fixture-verified; [features/homebrew-studio.md](features/homebrew-studio.md#design-feedback-m5-slice-7-off-by-default)).** The setting is a UI preference on this machine (the page's own storage), so it needs no database change and is never exported.
   - An app setting, **off by default**. Hints come from a read-only analyzer and compare a class with the bundled SRD classes of its family, for example:
     - more slots than a full caster at a level;
     - a multiclass share above what its own slot table implies;
     - a resource that grows faster than PB (**changed by the owner, 2026-09-29: faster than every SRD pool of the family**, because SRD pools also outgrow PB);
     - a level with no feature.
   - Hints never block, never change a calculation, and are never exported. The hint set is the owner's call.
8. **B08 character snapshots.** **Done 2026-09-29 (fixture-verified; [features/snapshots.md](features/snapshots.md)): database migration v7 (approved by the owner, 2026-09-29), by hand only, not in backups (D14).**
   - Snapshots are insert-only, in a new table (a forward-only database migration, with the usual pre-upgrade backup). The commands are `character.snapshot`, `character.snapshots` and `character.restorePreview`: choices, pins, play state and the sheet diff, shown like an update review.
   - `character.restoreSnapshot { token, confirm }` first snapshots the current state, so a restore can be undone and nothing is lost. The undo snapshot and the restore run in **one SQLite transaction**, as candidate acceptance does. The command takes the preview's one-use token, so a repeated confirmation does nothing (review fix).
   - Interactions to settle:
     - ~~A full library backup should include snapshots, which is a new library-backup format version.~~ **Decided (owner, 2026-09-29; D14): not in the library backup.** Character packages and shares never include them either.
     - A snapshot never carries the archive mark, and restoring one never archives or unarchives (SPEC C-08).
     - A snapshot pinning revisions missing on this machine restores as `content.missing`.
   - Independent of slices 2–7, so it can move earlier.

**Exit gate (restated):** a nonstandard class levels 1–20 and multiclasses with an SRD class, authored in the studio with no code edits.

- **Evidence level:** fixture-verified. It rests on the RulesCore side-by-side tests (both families), the AppService tests and the e2e flow of slice 1.
- **Windows-install verified:** needs the owner to author the class on an installed build (owner check).
- **Only slice 1 is needed for the gate.** M5 is delivered when the gate passes and slices 2–8 are done.
- M3 stays "not met" and M4 stays experimental whatever M5 reaches.

**What I must decide (owner):**

1. ~~Accept ADR-010: content v9 with `scale`, `SCALE.<id>` and `multiclassCasterTable`. Dice that scale by level (a d4 → d10 die) stay reference text for now.~~ **Accepted (owner, 2026-09-28)**, together with this plan. Items 2–5 are asked again when their slices start.
2. ~~Approve v9 before slice 1a merges (the stop point).~~ **Approved (owner, 2026-09-29).**
3. The sandbox overlay for drafts (slice 3), and the reworded invariant: "only published revisions affect **saved** characters; a sandbox previews a draft on an unsaved copy and writes nothing". **Approved (owner, 2026-09-29; LIVING_SPECS D14).**
4. Snapshots (slice 8):
   - whether they go into the full library backup (a new library-backup format version);
   - whether they are taken automatically before a level-up, an applied update and a restore, or only by hand.
   
   **Decided (owner, 2026-09-29; D14): by hand only, and not in the full library backup** (so this slice takes no package format number).
5. The design-feedback hint set (slice 7), and the four-template set (slice 6). **Approved as proposed (owner, 2026-09-29; D14).**

## M6 Sharing and extension (plan, approved by the owner 2026-09-28; ADR-011 and ADR-012 accepted 2026-09-29, LIVING_SPECS D14)

*Scope: share homebrew and campaigns safely between machines, add a versioned extension API without running third-party code by default (SPEC P-05, Q-02), and export to a VTT. The shipped app still opens no listening socket and makes no network call (ADR-001, ADR-006).*

**What exists (checked against the code 2026-09-28):**

- **Packages:** character packages are v5 and full library backups v6. They already have size and entry limits, a path allowlist, hash checks, refusal of newer versions, preview-then-apply, validation of published revisions on import (ADR-004), and the ADR-007 share rules with `notices[]` and `omitted[]`.
- **Campaigns** travel only inside character packages. There is no standalone campaign export (`CommandDispatcher` has `campaign.list`, `save` and `delete`). A campaign holds no paths.
- **Homebrew sources** can be created `redistributable` (`source.createHomebrew`), but that cannot be changed later, and there is no source-only export (ADR-007 item 10).
- **No extension, plugin, hook or adapter code exists.** The command protocol has no version field; only the payload schemas do.
- **The PDF worker** (ADR-009) is the one isolation pattern: a child process over stdin and stdout, with heap, time and size caps and a watchdog. It has no job object, no AppContainer and no network block.
- **The computed `CharacterSheet`** in `RulesCore` is what an export adapter maps from. No VTT export exists.

**Slices (which can start early is noted):**

1. **Content-pack format.** It can start after M5 slice 1a and is independent of M5 slices 2–8.
   - A new package format version, `scope: "source"`: one or more homebrew sources with their published revisions. No characters, drafts, gap notes, PDFs or extracted text.
   - Only sources marked shareable are included. "Mark as shareable" asks the author to confirm the source is their own work. It is refused for an import-derived source, which is the guard against a user source that wraps a bought book (ADR-009 (d)); owner decision. **The guard needs durable state and runs at every export (review fix):**
     - A permanent **import-derived** flag on the source. It is set when a PDF is attached and when a candidate is accepted into the source, and by ADR-011 extension imports. `source.detach` never clears it, and it travels in library backups. Today the only record of an import is the candidate rows, which backups leave out.
     - The check runs at every content-pack and campaign-pack export, not only when a source is marked.
     - `source.createHomebrew` no longer accepts `redistributable: true` unchecked. It goes through the same guard, or loses the parameter.
   - A documented schema and a compatibility matrix: pack format × content schema × app version.
   - Unknown future versions are refused (existing path). The package limits and quarantine are reused (SPEC Q-02, ADR-004).
   - **As built (2026-09-29; fixture-verified; not merged; the schema changes were approved by the owner on 2026-09-29, with format v7 still provisional, LIVING_SPECS D14):** see [features/package-format.md](features/package-format.md#source-packs-and-mark-as-shareable-m6-slice-1). The plan review (dual review, cross-checked) found the flag alone could be bypassed, so the slice also has:
     - one guard for every outbound path, the character share included;
     - a flag that only goes up, on every write and every import;
     - a machine-set `origin` (`local` or `received`), so a source received from someone else can never be marked as your own work;
     - database v8, which backfills the flag, and format v7 for source packs and library backups;
     - revisions bound to the pack's own sources, stored order kept, and bundled SRD source records never replaced;
     - the manifest's version read before the rest, so a later format is refused as newer.
     
     Correction to the text above: the candidate rows were not the only record of an import. Attachments, pre-v3 PDF references and import jobs are too, and the v8 backfill uses them. The name is **source pack**, because `content-pack.v1.schema.json` is the bundled SRD pack format.
2. **B13 campaign packs.**
   - `scope: "campaign"`: the campaign profile, its house rules, and the shareable content of its allowed sources.
   - SRD sources are referenced by id, never copied. Non-shareable allowed sources are listed in `omitted[]`.
   - No characters, gap notes, paths or machine-local ids. No asset kinds exist yet, so no assets.
   - Preview then apply, with a `keepLocal` or `useImported` choice when a campaign id already exists.
   - **Settled with the existing campaign rules (review fix):**
     - Today every allowed source must be installed (`campaign.source-missing`, [features/campaigns.md](features/campaigns.md)). An allowed source that the pack omits is therefore imported as a **pending reference**, with a warning, until the source is installed.
     - `useImported` replaces `allowedSources`. The preview lists the local characters whose content would become "not allowed".
     - The pre-import backup (package-format rule 10) also covers a campaign that is replaced.
   - **As built (2026-09-29; fixture-verified; not merged; format v8 provisional; the scope and the campaign's `pendingSources` approved by the owner 2026-09-30):** see [features/package-format.md](features/package-format.md#campaign-packs-m6-slice-2-b13) and [features/campaigns.md](features/campaigns.md).
     - The slice 1 guard decides what a campaign pack carries: only sources that would pass a source-pack export. Received, import-derived, unmarked or empty sources are named in `omitted[]` with the reason, not refused, because the profile is what is being shared.
     - A pending reference is the source id kept in `allowedSources` (so older builds allow it once installed) plus an informational `pendingSources` entry on the campaign (title, publisher, license). The field changes how no existing field is read, so campaign stays v1 (schemas/README "Versioning rules").
     - "Use the imported one" is a per-campaign `campaignChoices` choice, like `sourceChoices`; the preview's `campaignImpact` lists the characters that would newly get "not allowed" content.
     - Rule 10 now also covers a character package that replaces a campaign (a database copy), which it did not before.
3. **The extension API ([ADR-011](decisions/ADR-011-extension-api.md)), after the owner picks the sandbox model.** It has versioned, permissioned data, import and export hooks. One external sample extension is kept in `examples/extensions/` and passes the M6 gate. Nothing that runs third-party code is built without that decision.
   - **As built (2026-09-29; fixture-verified; not merged; database v9 and library-backup format v9 (number provisional) approved by the owner 2026-09-30):** option A, [features/extensions.md](features/extensions.md). A bounded declarative transform language (fuzzed; every bound refused with its own code); a versioned manifest whose permissions you grant at install, bound to the file's SHA-256; import hooks that write drafts only, into a new import-derived source; export hooks over the sheet export model v1 ([ADR-007 item 11](decisions/ADR-007-export-package-and-license-policy.md), amended first), with an output scan for paths and the user name; installed extensions in library backups without grants, restored turned off; the sample `examples/extensions/spell-list-and-sheet-summary`.
4. **B20 export adapters ([ADR-012](decisions/ADR-012-export-adapters.md)), after the owner picks the targets.** They need only the computed sheet, through the sheet export model v1 defined in ADR-011. So they are independent of M5, and can start once the targets are chosen **and** that model is accepted (its purpose filter and `notices[]`), even if ADR-011's execution model is still open. Each adapter has a validator.
   - **As built (2026-09-29; fixture-verified; not merged; no package or database change; one addition to slice 3's sheet export model, `slotKind` on casters, from the review, approved by the owner 2026-09-30):** [features/export-adapters.md](features/export-adapters.md). Foundry VTT dnd5e pinned to Foundry 14.367 with dnd5e 6.0.5 and verified against that release's data models (Experimental until the owner imports the golden files into a real world); the neutral sheet JSON; a validator for each; golden files from original fixture characters; the output scan; Roll20 deferred.
5. **Author documentation** (`docs/authoring/`): how to write a class, a content pack and an extension. It is tested by following it in the e2e fixtures.
6. **M6 exit gate:** the sample extension installs, is permissioned, and runs its hook in an e2e flow. A content pack, a campaign pack and a library backup round-trip into a clean data folder and compare equal (fixture-verified). The **cross-machine** part uses the owner's second-machine restore evidence if it is recorded by then. Otherwise it stays an owner check.

**Package format numbers (review fix, 2026-09-28).** Four M5 and M6 slices change the package format:

- ~~snapshots in library backups (M5 slice 8)~~ (not taken: the owner kept snapshots out of backups, D14);
- the source scope, and the source fields in library backups (M6 slice 1; one number for both, since they ship together; it would be v7 if it merged next, LIVING_SPECS 2026-09-29);
- the campaign scope (M6 slice 2; it would be v8 if it merged after slice 1, LIVING_SPECS 2026-09-29);
- extensions in library backups (ADR-011; M6 slice 3: it would be v9 if it merged after slices 1 and 2).

They land in no fixed order, and on `main` there is one `CurrentFormatVersion` (6; the unmerged M6 branches use 7, 8 and 9 provisionally). So no plan text fixes a number. Each slice takes the **next** number when it merges, records it in the version table of [features/package-format.md](features/package-format.md), and adds its manifest schema. Every new shape gets its own number, and a number is never reused.

**Optional after the M6 gate:** B09 command palette, B11 tags, folders and collections.

**Exit gate (restated):** an external sample extension and a safe cross-machine round trip. The evidence level is fixture-verified for the clean-folder round trip and the sample extension. It is Windows-install verified only once the owner's second-machine restore is recorded.

**What I must decide (owner):**

1. The extension sandbox model (ADR-011 options A to D). **Decided (owner, 2026-09-29; D14): A, declarative only.**
2. The adapter targets and versions (ADR-012). **Decided (D14): Foundry `dnd5e` (pair pinned at slice start) and the neutral JSON; Roll20 deferred.**
3. The "Mark as shareable" rule for your own homebrew: the durable import-derived flag, and what happens to `source.createHomebrew`'s `redistributable` parameter. **Design approved (D14): the flag as proposed, and `redistributable` goes through the same guard. The flag itself is approved again before it ships.** **Re-approved (owner, 2026-09-29)** with `origin`, `shareConfirmedAt` and database v8; the character-share rule, page citations and unknown-origin sources stay open.
4. How far ADR-012's `personal` purpose may go. Proposed: only your own homebrew that is not import-derived; bought books are always filtered. **Approved as proposed (D14).**
5. Whether the extension API or the adapters come first. Both wait on 1 or 2 above, and the adapters also wait on the sheet export model. The pack format waits on neither.

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
