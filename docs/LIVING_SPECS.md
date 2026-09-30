# Living specifications and decision log · v0.1

## Source of truth

`SPEC.md` describes promised behavior; `ARCHITECTURE.md` describes the current proposed implementation; `MVP.md` and `ROADMAP.md` scope delivery; `BACKLOG.md` retains approved ideas. A shipped feature requires an acceptance example and a changelog entry. Git tracks full history once this set is placed in a repository. Do not replace documents wholesale at each milestone.

## Change workflow

1. File a change with motivation, affected spec IDs, edition(s), source/license impact and example character.
2. Update the behavioral spec and acceptance example first. If architecture changes, add or supersede an ADR in `docs/decisions/` and update the diagram.
3. Add/update rules fixtures or an interaction rehearsal; make data/export migration explicit where relevant.
4. Land code and doc change together; log user-visible behavior in `CHANGELOG.md`. Mark features delivered only when end-to-end acceptance passes.
5. For homebrew content revisions, publish immutable revisions and offer characters a reviewed migration; do not mutate pinned rule history silently.

## Suggested repository structure

```text
README.md
docs/SPEC.md                  docs/ARCHITECTURE.md
docs/MVP.md                   docs/ROADMAP.md
docs/BACKLOG.md               docs/CHANGELOG.md
docs/decisions/ADR-001-*.md  docs/features/*.md
docs/diagrams/*.drawio        docs/examples/*.json
src/DesktopShell/             src/Ui/
src/AppService/               src/RulesCore/
src/ImportWorker/             tests/RulesFixtures/
```

## ADR template

```markdown
# ADR-NNN: Title
Status: proposed | accepted | superseded
Date: YYYY-MM-DD
Context: The concrete choice and constraints.
Decision: What we will do.
Consequences: Benefits, costs, migration and security implications.
Alternatives considered: Options actually evaluated.
Evidence: Spike/test/doc links.
Supersedes: ADR-NNN if applicable.
```

## Changelog seed

Create `CHANGELOG.md` with `## Unreleased` and subsections `Added`, `Changed`, `Fixed`, `Migration`. A release entry links affected spec IDs and notes export/schema/database changes. Do not record planned backlog items as shipped changes.

## Pending product decisions

| ID | Question | Working default | Decide by |
| --- | --- | --- | --- |
| D01 | Which rests/recoveries auto-apply? | **Decided (owner, 2026-09-27): long rest first (M2 item 3)**, as a preview that the player confirms. Nothing auto-applies: every proposed change is ticked, and the player can untick any of them (`features/rests.md`). **Follow-up decided (owner, 2026-09-27): the short rest uses the SRD rules, previewed.** The player picks the hit dice to spend, each rolled by TomeStack or entered from the table, plus the Con modifier. Short-rest recoveries are ticked changes. The long rest regains half the hit dice (2014) or all of them (2024), as policy fields | Done (implemented in M2) |
| D02 | Copy or externally link a PDF by default? | **Decided (owner, 2026-09-26; ADR-005 accepted):** data folder `%LOCALAPPDATA%\TomeStack`; PDFs are managed copies by default; a data folder inside a sync root (OneDrive and others) gets a startup warning (implemented). The `pdfRef` → attachment migration is planned for M2 | Done |
| D03 | What goes into shared packages? | **Decided (owner, 2026-09-26; ADR-007 accepted, implemented):** separate `backup` and `share` exports (manifest v3 `purpose`); `share` omits non-redistributable sources, lists them in `omitted[]` and previews them first | Done |
| D04 | How much multiclass/spellcasting in MVP? | **Decided (M1 fixture review, 2026-09-26).** *Multiclass in MVP:* levels per class, total level, hit points per class, level-gated class features and `CLASS_LEVEL` (built in M1). The M2 builder adds the SRD multiclass prerequisites (as `restriction` effects) and the "as a multiclass character" proficiency subsets, under both editions. *Spellcasting in MVP:* one spellcasting class per character, with spell attack bonus, save DC, slots by class level, and prepared/known spells for the SRD casters of both editions. *Not in MVP (M3, "multiclass/spellcasting polish"):* combining slots across several spellcasting classes (the SRD multiclass spellcaster table) and Pact Magic combined with slots. Such characters calculate each class separately, and the slot total is assisted with a manual step. Why: the M1 slice (a Barbarian 1–3 per edition) exercises class levels, hit dice and level gates but no spellcasting, and the effect model had no spellcasting effect (ARCHITECTURE step 3). **Implemented (M2):** the `spellcasting` and `spell` effects (content v5); a second caster's slots are an assisted field that the player overrides (`features/spellcasting.md`). **M3 part implemented (2026-09-28, C3):** slots combine on the SRD Multiclass Spellcaster table (content v7 `multiclassCaster`, policy fields `HalfCasterLevels`, `ThirdCasterLevels` and `MulticlassSpellSlots`). Pact Magic stays separate | Done (M2 engine and SRD casters; M3 combined slots) |
| D05 | What accessibility target? | **Decided (owner, 2026-09-26): WCAG 2.2 AA for the sheet and the builder.** Checklist: `features/accessibility-checklist.md` | Done (open AA items tracked there) |
| D06 | Which Windows shell/IPC? | **Decided (ADR-006):** WPF/WebView2 shell, in-process service, WebView2 message bridge; loopback host dev-only. **Installer decided (ADR-008, 2026-09-26):** Velopack, per-user, self-contained, pack id `TomeStack.App` | Done (clean-VM install passed on 0.2.2, reported by the owner 2026-09-28) |
| D07 | License for the project's own code? | **Decided (owner, 2026-09-26): Apache-2.0.** `LICENSE` and `NOTICE` are at the repo root. The dependency review (`ATTRIBUTION.md`: MIT, BSD-3-Clause, Apache-2.0, public domain) found nothing incompatible | Done |
| D08 | Name availability and public branding? | TomeStack working name; trademark check before launch | Before public release |
| D09 | Which SRD license route? | **Decided 2026-09-25:** SRD 5.1 and SRD 5.2.1 under CC-BY-4.0. No Wizards of the Coast trademarks in branding. The attribution statements were approved ([licensing/srd-attribution-draft.md](licensing/srd-attribution-draft.md)) and both packs ship in M1 (SPEC Q-03) | Done |
| D10 | Which Windows versions? | **Decided 2026-09-25:** Windows 10 and Windows 11 (ADR-001 accepted 2026-09-26, Windows 10 best-effort). The installer must provide the WebView2 runtime when it is missing (common on Windows 10) | Installer spike (M0) |
| D11 | Seed test fixtures into user data? | **Decided 2026-09-25:** keep for M0, stop once real SRD content ships. Done in M1: the shipped app no longer seeds fixtures (`TOMESTACK_DEV_FIXTURES=1` re-enables them). Data folders that already have fixture content keep it; no removal migration exists yet. **Hardened (audit 2026-09-28):** the desktop shell honours `TOMESTACK_DEV_FIXTURES=1` only in a Debug build (seeded fixtures cannot be removed, so not even `--smoke` seeds them), and `--devtools` and WebView2's `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` only in a Debug build too (not in a Release `--smoke` run). A Release launch ignores them all; the smoke report records `devTools`, `devFixtures` and whether extra browser arguments or an `AdditionalBrowserArguments` policy were present. The DevHost and tests are unchanged | M1 (removal migration open) |
| D12 | Third-party homebrew in the repo? | **Decided 2026-09-25:** none. The Stardust Guardian material is a friend's third-party work, never committed to this public repo without the author's permission. It lives only in the gitignored `tests/RulesFixtures/local/` | Before any M3 work |
| D13 | Does M5 wait for the M3 gate? | **Decided (owner, 2026-09-28): no. M5 engineering starts now, before the M3 gate.** The M3 gate itself is unchanged: it stays "not met" until the Stardust Guardian run and a played session. What the ROADMAP said should come from the M3 gap notes is **provisional**: build the mechanism, keep the concrete set small and revisable. That covers the B15 template set and any new effect types from `features/m3-effects.md` "Not yet". When the gap notes arrive, they re-rank those items; they do not re-open delivered mechanisms. Plans: ROADMAP "M5 plan" and "M6 plan" (approved by the owner, 2026-09-28). ADR-010 is accepted; ADR-011 (execution model) and ADR-012 (targets) stay proposed until the owner picks | Done (sequencing, plans, ADR-010); ADR-011 and ADR-012 accepted 2026-09-29 (D14) |
| D14 | The M5 and M6 owner decisions (ROADMAP "M5 plan" and "M6 plan", "What I must decide") | **Decided (owner, 2026-09-29), each as proposed:** (1) **Sandbox (M5 slice 3):** an in-memory overlay treats one draft as published for one calculation on an unsaved copy and writes nothing. The invariant becomes "only published revisions affect **saved** characters" (reworded in SPEC and CLAUDE.md in that slice). (2) **Snapshots (M5 slice 8):** taken **by hand only** (plus the undo snapshot a restore always takes), and **not** in the full library backup, so no library-backup format number is used. (3) **Templates and design feedback (M5 slices 6–7):** the four templates (a resource with a recovery, a toggled stance, a subclass skeleton, a class skeleton) and the four hints (slots above a full caster's; a multiclass share above its slot table; a resource that grows faster than PB; a level with no feature). Feedback is off by default, never blocks, never changes a calculation and is never exported. (4) **ADR-011:** option A, declarative only; nothing runs third-party code. (5) **ADR-012:** Foundry VTT `dnd5e` (pinned pair) and the neutral sheet-export JSON, Roll20 deferred; the sheet export model (purpose filter and `notices[]`) is accepted; `personal` exports go only as far as your own non-import-derived homebrew. (6) **"Mark as shareable" (M6 slice 1):** a durable import-derived flag on sources, set on PDF attach, candidate accept and extension import, never cleared by a detach, and carried in library backups. It is checked at every content-pack and campaign-pack export, and `source.createHomebrew`'s `redistributable` goes through the same guard. The flag is a schema field: **the owner approves it again before it ships**, as for every new schema field or version | Done (decided); each is built in its slice |

## Change history

- **2026-09-24 · v0.1:** Initial product specification, Windows-first architecture proposal, two-rule-family MVP, accepted 20-item backlog, milestone plan and editable diagram. No implementation is claimed.
- **2026-09-24 · M0 foundation:** Documents moved to `docs/`. The desktop spike resolved D06: in-process service over the WebView2 message bridge instead of a local ASP.NET Core service ([ADR-006](decisions/ADR-006-desktop-host-and-ipc.md)). Content IDs and pins are recorded in [ADR-002](decisions/ADR-002-edition-aware-ids-and-revision-pins.md), and package format v1 in [features/package-format.md](features/package-format.md). The first vertical slice (initiative with trace, local save, export/import) is implemented against original fixtures; see [CHANGELOG.md](CHANGELOG.md). The functional overview diagram (`diagrams/TomeStack-Architecture.svg`) does not depict process topology, so ADR-006 needs no diagram change. The repo has no editable `.drawio` source yet.
- **2026-09-25 · owner decisions:** D02, D05, D07 and the installer part of D06 decided. D09–D12 added and decided (SRD route, Windows 10/11, fixture seeding, third-party homebrew). Code is licensed Apache-2.0. The M3 Stardust Guardian acceptance fixture is deferred until the author grants permission (D12).
- **2026-09-26 · owner decisions:** D07 decided (Apache-2.0). D06 completed: the installer is Velopack, per-user and self-contained ([ADR-008](decisions/ADR-008-installer-and-distribution.md), accepted), with a version-bump policy. D02 decided (ADR-005 accepted). D03 decided and implemented (ADR-007 accepted, package format v3). D05 decided (WCAG 2.2 AA). SRD route: CC-BY-4.0 for both SRDs. ADR-001 accepted with Windows 10 best-effort. Fixture seeding into user data stays for M0 and becomes dev-only once SRD packs exist. Private homebrew (Stardust Guardian) lives in the gitignored `tests/RulesFixtures/local/`.
- **2026-09-26 · M1 item 1:** SRD 5.1 and SRD 5.2.1 ship as separate CC-BY-4.0 source packs (`src/AppService/Content/`). The attribution checklist is complete ([licensing/srd-attribution-draft.md](licensing/srd-attribution-draft.md), approved), and the pack review is in [licensing/srd-pack-review.md](licensing/srd-pack-review.md). As decided, fixture seeding is now development only.
- **2026-09-27 · owner decisions (M2 kickoff):** D01 decided: long rest only in M2, previewed and confirmed. The SRD rule "no ability score above 20" will be enforced by clipping *bonuses* to ability scores at 20; `set` effects and overrides may exceed it (M2 item 4). The SRD sources keep `publisher` "SRD 5.1 (CC-BY-4.0)" / "SRD 5.2.1 (CC-BY-4.0)" (the judgment call in `licensing/srd-pack-review.md`).
- **2026-09-27 · owner decisions (M2 exit):** the D01 follow-up (short rest with SRD rules, previewed); bundle the **full SRD casters, levels 1–20, in both families** (with spells and weapons) under an extended pack review (SPEC Q-03); PDF page-range import as reference **without text extraction** in M2 (extraction stays M4). The two SRD PDFs were re-downloaded and match the hashes recorded in `licensing/srd-pack-review.md`.
- **2026-09-28 · owner decisions (M3 and M4):**
  - The M2 owner checks passed on the installed 0.2.2, and M2 is delivered as 0.3.0. The synthetic stand-in meets DoD 3 for M2.
  - The printable backup is a print view of the sheet, with gap notes opt-in (SPEC P-03).
  - More SRD content (M3 C6) is deferred until the Stardust Guardian run shows a need.
  - ADR-009 is accepted: PdfPig, Windows OCR, a child-process worker, and extracted text never exported.
- **2026-09-28 · M2.1 data safety and roadmap order (after an independent progress audit):**
  - **M2.1 added and done:** full library backup and restore (package format v6, ADR-007 item 10); one TomeStack per data folder (ARCHITECTURE "Data folder"); every spellcasting class's attack and save DC with modifiers and a trace; a stable e2e gate and a blocking desktop smoke.
  - **Claims now use evidence levels:** implemented, fixture-verified, Windows-install verified, accepted in real play ([features/m2-acceptance.md](features/m2-acceptance.md#evidence-levels-m21-2026-09-28)). M2 is "checks passed, limited content". The MVP goal is unchanged and not met yet.
  - **Owner direction, reversing part of the C6 deferral above:** a Fighter baseline and the SRD armor table (ROADMAP **M2.2**) come *before* the M3 Stardust Guardian run, which depends on them. Monk, Rogue, Barbarian 4–20 and more species and backgrounds stay deferred.
  - **M4 is labeled experimental** until a real third-party PDF and the SRD detection floors pass. M3 and M4 are never marked done on synthetic fixtures.
  - **Repository state:** the local branches had been rebased on GitHub, so the local checkout showed "ahead 41, behind 48". Every local commit's content is on `origin/main`, whose extra text (D09–D12, the 2026-09-25 entry and the trademark line in the README) was missing locally. New work branches from `origin/main`; no local branch was reset or deleted.
- **2026-09-29 · owner decisions for M5 slices 3–8 and M6 (D14):** all six as proposed: the draft sandbox overlay with the reworded invariant, manual snapshots outside backups, the four templates and four hints, ADR-011 option A (accepted), ADR-012's targets (accepted) with `personal` limited to your own non-import-derived homebrew, and the durable import-derived flag (to be approved again before it ships). **M5 slice 2 (B02) delivered:** the homebrew debugger, `content.diagnose` over the new read-only `RulesCore.ContentGraph` ([features/homebrew-studio.md](features/homebrew-studio.md#the-homebrew-debugger-m5-slice-2-b02)). No schema, package or database change.
- **2026-09-29 · M5 slice 3 (B03), the draft sandbox:** "Try it" (`content.sandbox`, `RulesCore.DraftOverlayCatalog`). As decided in D14, the invariant is reworded in SPEC I-06, CLAUDE.md and ADR-010: **only published revisions affect saved characters**. The sandbox calculates one draft, in memory, on an unsaved copy with a new id, and writes nothing. No schema, package or database change.
- **2026-09-29 · owner decision: content schema v9 approved** ([ADR-010](decisions/ADR-010-custom-classes-and-progression.md)): the `scale` effect, `SCALE.<id>`, `spellcasting.multiclassCasterTable`, and a choice with no declared options (M5 slices 1a and 1b). Merging the PRs still waits for the owner.
- **2026-09-28 · owner direction: M5 before the M3 gate (D13):**
  - The ROADMAP said "M5 starts after the M3 gate". The owner reversed that: M5 engineering starts now. The M3 gate is unchanged and still not met; M4 stays experimental until a third-party PDF run.
  - Items whose priorities were to come from the M3 gap notes are provisional: the B15 templates and new effect types from `features/m3-effects.md` "Not yet". The mechanism is built; the concrete set stays small and is revised when the gap notes arrive.
  - ROADMAP gains "M5 plan" and "M6 plan", approved by the owner the same day. [ADR-010](decisions/ADR-010-custom-classes-and-progression.md) (custom base classes, content schema v9) is accepted. The v9 bump itself still needed the owner's approval before it merged (approved 2026-09-29, entry above). [ADR-011](decisions/ADR-011-extension-api.md) (the extension API, SPEC P-05) and [ADR-012](decisions/ADR-012-export-adapters.md) (VTT export adapters, B20) stay proposed until the owner picks the execution model and the targets.
- **2026-09-26 · M1 item 5:** D04 decided (see the table). Character schema v3 (`classes`) and content schema v3 (`grant.level`, `hitDie`) were added, with hit points, armor class and all 18 skills in the field graph ([features/levels-and-classes.md](features/levels-and-classes.md), ADR-003).
