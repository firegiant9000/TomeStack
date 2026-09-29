# TomeStack roadmap review, September 2026

_Prepared 2026-09-29 against the local `m6-1-source-packs` tip (`e698aaf`, which is `origin/m6-1-source-packs` at `030f68c` plus twelve unpushed commits) and `origin/main` at `3eaf731`. Read-only audit; nothing here is a measured result._

This review is the evidence base for the 2026-09 revision at the top of [`ROADMAP.md`](ROADMAP.md). It also contains the repository cleanup plan the revision requires before any other milestone.

> **Status 2026-09-30 (reconciled, [LIVING_SPECS](LIVING_SPECS.md) D15).** The audit findings stand as the record of 2026-09-29. Made obsolete by the M6 stack and the owner's direction:
> - Sections 1, 6 and 7: "M6 slices 2 to 5 … DEFERRED until the T2 gap report". The owner had M6 slices 2 to 6 built before T2 on purpose (PRs #39 to #43), and they are kept. T2 still gates M7, content breadth and further studio or extension work.
> - Section 5: "Unpushed local work" and cleanup step 2. Every stack branch now matches `origin`.
> - Section 5, step 4 (rebase `m5-plan` and force-push the stack). The `m5-m6-integration` branch merges the stack into `origin/main` with merge commits instead, so no pushed branch is rewritten. The owner chooses whether to merge that one PR or the stack.
> - Section 5, step 5's list of approvals is out of date. Database v7, database v8 with the source fields, and the `source` scope were approved on 2026-09-29. The approvals still owed are listed in LIVING_SPECS and the integration PR.
> - Section 8, the "package-format version numbers lag the code" row. The M6 plan text now states `main` 6 and the branches 7, 8 and 9 (provisional).
>
> The rest of section 5 still applies: merge #24 first, delete the stale branches and worktrees, move the clone out of OneDrive, and tag the merged tip. On 2026-09-30 a git auto-repack in the OneDrive clone raced a commit. The commit landed, and `git fsck` found no damage.

## 1. Decision

TomeStack's primary objective is unchanged from the ROADMAP's M3 gate, and this revision makes it the only thing that matters next: **Arlo uses it for a real character in a real session.** No studio, DM, combat, plugin or cloud feature is added before that gate produces a gap report. After the gate, the repository specialises in what it already does best: .NET architecture, local-first desktop engineering, correctness (property tests), performance (benchmarks), release engineering and recovery evidence.

The reason: M5 (eight studio slices) and M6 slice 1 were built in three days for a studio nobody has needed yet, while the M3 played session, the M4 third-party PDF run, the second-machine restore and the first installer are all still "owner check". The engineering is ahead of the evidence. The revision stops adding and starts proving.

## 2. Current roadmap goals (as written)

| Milestone | Stated status (ROADMAP.md) | Verified |
|---|---|---|
| M0 Foundation | no status text | CI, ADRs, fixtures exist |
| M1 Rules core | Delivered 2026-09-26 (v0.2.0) | `M1AcceptanceTests` exists |
| M2 Usable MVP | Checks passed 2026-09-28 (v0.3.0), limited content | Fighter, eight casters, Barbarian 1 to 3 |
| M2.1 Data safety | Done, fixture-verified; second-machine restore is an owner check | `LibraryBackupTests` 580 lines; no drill record |
| M2.2 Fighter | Done, fixture-verified, not install-verified | `SrdFighterTests` |
| M3 Personal replacement | Not met: owner material and played session needed | correct |
| M4 Import intelligence | Engineering done, gate not met (third-party PDF owed) | correct |
| M5 Creation power | "In progress", then lists slices 2 to 8 as done 2026-09-29 | all nine PRs open and unmerged (#27, #29 to #36, plus #37 merged into #36's branch) |
| M6 Sharing and extension | Slice 1 built on PR #38, awaiting owner approval | correct; local branch carries 12 more unpushed commits |
| M7 Expanded tabletop | no status | nothing built |

## 3. Already implemented (verified in code)

- .NET 10 solution with `RulesCore` free of outward references; `Calculation.cs` 2,249 lines; `Formulas.cs` 386 lines; `Dice.cs` 297 lines with `IRandomSource`, `SystemRandomSource` and a SplitMix64 `SeededRandomSource` injected through `Rolling.cs:32`.
- Insert-only revisions hashed as `Sha256(Serialize(revision))` over compact JSON (`SqliteStore.cs:674-676`); the only sanctioned hash rewrite was migration v2.
- SQLite in WAL mode, online-backup API for backups, pre-upgrade `{db}.v{N}.bak`, refusal to open a newer database, forward-only migrations, one per transaction. Database schema 8, content schema 9, character schema 7, package format 7.
- Library backup v7 with per-entry SHA-256, PDFs stored by hash, preview-then-apply with a one-use token, pre-restore safety copy (`LibraryBackup.cs`, `PackageService.cs`).
- Single-instance lock (`DataFolderLock`, `SingleInstance.cs`), data-folder sync-root warning (`DataFolder.cs:39-54`).
- Velopack 1.2.158 wired (`VelopackApp.Build().Run()`), `scripts/pack-installer.ps1`, `scripts/installer-smoke.ps1`; no `UpdateManager`, so no update check.
- CI on `windows-latest`: lint, Vitest unit, UI build, .NET Release build and tests, e2e against DevHost, WebView2 probe, blocking desktop smoke, blocking two-instance check.
- 644 `[Fact]`/`[Theory]` attributes across 80 files; 20,000 fuzzed strings and 2,000 generated formulas in `FormulaTests.cs:133-172` (hand-rolled, not a property framework).
- Twelve ADRs, all accepted.

## 4. Claimed but unproven

| Claim | Where | What is missing |
|---|---|---|
| M2.1 "Done" | ROADMAP row | "Restore a full backup on a second machine" has never been done; `package-format.md:98` and `m2-acceptance.md:26` say so. Risk-table row "fresh-machine restore drill" has no artifact. |
| M2.2, M5, M6 slice 1 "Done" | ROADMAP rows | All fixture-verified only; none Windows-install verified; none merged to `main`. |
| M5 row wording | ROADMAP.md M5 | Says "M5 is not delivered until slices 2 to 8 are done" and then lists them done. Self-contradictory. |
| "one `CurrentFormatVersion` (6)" and "library backups v6" | ROADMAP.md package-format section, M6 plan | Code is at 7 (`PackageModels.cs:29`). |
| Hash stability across serializer changes | gotchas and ADR text | No golden-hash test exists that pins a known revision's hash across serializer upgrades. |
| Installer usable by someone else | README "build from source" | No release, no tag, no signing, unresolved Velopack crate notices (`ATTRIBUTION.md:39`), SmartScreen behaviour untested (ADR-008 R7). |
| Accessibility passes | `accessibility-checklist.md` | Items 15 to 24 (Narrator, 200 % zoom, High Contrast) are owner checks not done. |

## 5. Repository health (the merge and cleanup problem)

Facts from `git branch -vv`, `git worktree list` and `gh pr list` on 2026-09-29:

- **Open PR stack** on `main`: #27 `m5-plan` ← #29 `m5-1a-content-v9` ← #30 `m5-1b-studio-classes` ← #31 `m5-2-debugger` ← #32 `m5-3-sandbox` ← #33 `m5-4-diff` ← #34 `m5-5-graph` ← #35 `m5-6-templates` ← #36 `m5-7-feedback` ← #38 `m6-1-source-packs`. PR #37 (snapshots, DB v7) was merged into `m5-7-feedback`, not `main`. Independent of the stack: #24 `docs-outstanding` (M0 to M4 status corrections) and #26 (Dependabot).
- **Unpushed local work.** Local `m6-1-source-packs` is 12 commits ahead of origin; local `m5-1a` through `m5-7` are 1 to 10 commits ahead (review fixes, the e2e 10-second timeout fix, owner-approval docs). The pushed stack lacks those fixes.
- **Another session was committing to the main clone during this audit** (tip moved f494f11 → e698aaf). Any cleanup must be coordinated with it.
- **Conflict risk.** #24 and #27 both change ROADMAP, LIVING_SPECS and CHANGELOG. The stack branches delete the README screenshots that `main` gained in PR #28 and differ on `.github/dependabot.yml` from PR #25. The stack is merge-commit chains; squash-merging bottom-up will conflict unless each PR is rebased or retargeted.
- **Mis-tracked and stale local branches.** `m2.1-merge-main` tracks `origin/m2.1-data-safety`; `m2-exit`, `m2-usable-mvp`, `m3-finish`, `m3-personal-replacement`, `m4-import-intelligence` show ahead/behind against remotes that were already merged (stale copies); local `main` is 116 behind with nothing unique; five `worktree-agent-*` branches all sit at the `origin/main` tip with nothing unique.
- **Six worktrees** registered (`TomeStack-docs`, `TomeStack-hardening`, `TomeStack-m2.1`, `TomeStack-main` detached and behind, plus the main clone and this review's worktree).
- **The repository, its `.git` and worktrees live under OneDrive** (`OneDrive\Documents\Projects\TomeStack`). The app warns users against a data folder under a sync root; the code does not enjoy the same protection. No document mentions this.
- **No releases, no tags, no stashes.** Zero TODO/FIXME in source.

### Safe merge and cleanup plan (nothing here is executed by this review)

1. Coordinate with the other active session first. Do not run any git command in the main clone until it is idle.
2. Push the unpushed local commits on `m5-1a` through `m6-1-source-packs` so each PR shows the reviewed state (fast-forwards; no history rewrite).
3. Merge #24 `docs-outstanding` into `main` first (docs only, independent), then #26 if CI is green.
4. Rebase `m5-plan` onto the new `main` and resolve the ROADMAP/LIVING_SPECS/CHANGELOG conflicts once, at the bottom of the stack. Restore the README screenshots and `dependabot.yml` from `main` in that rebase. Force-push only that branch, and only after the owner confirms; every branch above it then rebases onto its parent in order (#29 → #30 → … → #38). This is the one place a force-push is unavoidable; it is on feature branches, never on `main`.
5. Merge the stack bottom-up with **merge commits, not squash**, so GitHub retargets each child PR automatically when its base is deleted. Owner approvals still outstanding before the merge: DB v7 (snapshots), the "resource faster than PB" hint wording, DB v8 and the three source fields, package format v7, and items 4 to 6 on PR #38.
6. After the stack is on `main`: delete the five `worktree-agent-*` branches, the stale `m2-*`, `m3-*`, `m4-*` local copies and the `m2.1-merge-main` branch; remove the `TomeStack-*` worktrees; fast-forward local `main`.
7. Move the clone (or at minimum `.git` and all worktrees) out of OneDrive. A clone at `C:\src\TomeStack` with OneDrive left for documents is the simplest. Record the decision in `docs/gotchas` or the README.
8. Acceptance for the cleanup: `origin/main` contains M5 and M6 slice 1; `git status` clean in one clone; zero open stacked PRs; no git repository under a sync root; a tag `v0.4.0` (or the next number) at the merged tip.

## 6. Technically useful future work (kept or added)

- M3 gate exactly as written, with a formal gap-report procedure (revision milestone T2).
- M2.1 second-machine restore drill, promoted from owner check to a milestone with recorded counts and time (T6).
- M4 third-party PDF run: kept as an owner check; not on the critical path.
- FsCheck property tests on the invariants the ADRs already assert (T3).
- BenchmarkDotNet on `Calculation` and package import (T4).
- One downloadable release through Velopack with the licensing questions closed (T5).
- M6 slices 2 to 5 (campaign packs, extension API, export adapters, docs): kept, DEFERRED until after the T2 gap report re-ranks them.
- M7: unchanged, DEFERRED.

## 7. Feature work that should stop

| Item | Decision | Reason |
|---|---|---|
| Further studio tooling beyond M5 slices 1 to 8 | DEFERRED | The studio has a debugger, sandbox, diff, tree, templates, feedback and snapshots before one session has been played. |
| M6 slices 2 to 5 | DEFERRED until the T2 gap report | The gap report may reorder them; building first inverts the roadmap's own rule. |
| M7 monsters, DM tools, PDF search, printable layouts, local AI | DEFERRED | Nothing in the evidence asks for them. |
| Content breadth (Monk, Rogue, Barbarian 4 to 20, species, backgrounds) | DEFERRED until T2 | Already deferred by owner decision; the played session decides. |
| Cloud sync, accounts, plugin sandbox with executable code | CANCELLED | Contradicts SPEC (no accounts) and ADR-011 option A (declarative only). |
| Optional B09 command palette, B11 tags and folders | DEFERRED | Polish before use. |

## 8. Roadmap contradictions

- M5 row says "not delivered until slices 2 to 8 are done" and lists them all done; the intended meaning is "delivered when merged and install-verified". Reworded in the revision.
- M5 was started before the M3 gate by owner direction (D13) with the explicit caveat that M3 gap notes would re-rank M5 priorities. Those notes do not exist yet, so all of M5's template and hint choices are provisional. The revision keeps that caveat and stops further building until the notes exist.
- README says M2.1 and M2.2 are "not released yet" while ROADMAP marks them "Done". Both are true under the evidence levels (fixture-verified vs released); the revision restates the evidence level next to every "Done".
- Package-format version numbers in the M6 plan text (6) lag the code (7).

## 9. Current risks

- The unpushed, un-merged stack is the single biggest risk to the repository: twelve local commits exist only on one OneDrive-synced disk.
- OneDrive syncing `.git` can corrupt the repository during a rebase or when two sessions write at once.
- Unsigned installer with untested SmartScreen behaviour and unreviewed Velopack crate notices blocks any release to another person.
- Windows.Media.Ocr is used through the OS, not redistributed; ADR-009 is the only note. The Windows SDK `REDIST.TXT` question is still listed in the pre-release checklist and should be closed by stating which binaries, if any, ship from the SDK.
- Hash stability has no golden test; a serializer upgrade could silently change every revision hash.
- All UI behaviour is in one 1,666-line e2e file; a flaky step there blocks the whole gate.

## 10. Evidence gaps (in the order the revision closes them)

1. A clean repository with the work on `main` and tagged.
2. A played session and a dated gap report.
3. Property tests on engine invariants.
4. A benchmark table.
5. A downloadable release and one tested upgrade.
6. A recorded restore drill on a second machine.
