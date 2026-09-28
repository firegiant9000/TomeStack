# M2 acceptance: every MVP.md check, with its evidence

ROADMAP M2 exit gate ("all MVP.md checks pass on an installed Windows build") · build **0.2.2** (the M2 exit candidate) · status: **automated checks pass; owner checks pending**. Following ADR-008, the version becomes 0.3.0 only when every row below passes on an installed build.

Automated means it runs in the gate (CLAUDE.md): `dotnet test`, `npm run test:e2e` against the real DevHost, and `scripts/smoke.ps1` on the built shell. Owner means a person at the installed app. Anything that cannot be verified on the development machine is listed at the end.

## Committed candidate scope (MVP.md table)

| MVP area | Exit evidence required | Executable acceptance | Status |
| --- | --- | --- | --- |
| Windows app | Clean-machine install and reopen | `scripts/smoke.ps1` (offline shell, no listening socket, PDF viewer offline); `UpgradeTests` (backup before migration, data kept, newer data refused); owner: `scripts/installer-smoke.ps1` 0.2.0 → 0.2.2 | Automated ✅ · owner ☐ · clean VM ✗ (below) |
| Rules packs | Fixture characters for each family; explicit mix warning | `M1AcceptanceTests` (Korga 5.1, Brenna 5.2.1); `SrdPackTests` (separate sources and ids, attribution, every revision validates, side-by-side content differences); `RulesFamilySideBySideTests`; `SrdRulesFamilyTests`; cross-family exception (`content.cross-family-exception`) | ✅ |
| Builder | Finish two fixture characters and level them; unresolved choices flagged | e2e "builds an SRD 5.2.1 Barbarian as drafts…" (create, cancelled level-up, level to 3, subclass, unresolved choice flagged, then answered); e2e "builds a spellcaster…" (spell picker); e2e "equips a weapon…"; `BuilderDraftTests`; `SrdCasterTests` (SRD casters 1–20, multiclass prerequisites and subsets) | ✅ with limits (below) |
| Sheet | Play through a short scripted encounter and rest | **`M2AcceptanceTests`** (an SRD Wizard per family: damage, cantrip attack, weapon attack, a slot spent, short rest with a hit die, long rest, backup round trip); e2e Brenna flow (resources, temporary hit points, damage, conditions, rolls with advantage, long and short rest, death saves, Heroic Inspiration); `RestCommandTests`, `PlayCommandTests`, `SpellcastingCommandTests`, `AttackCommandTests` | ✅ |
| Homebrew | Stardust Guardian fixture with representative automatic and assisted features | `HomebrewStudioTests` and e2e "authors a homebrew subclass…" (synthetic stand-in: one modifier, one class resource, one limited-use action, one reference-only feature, update review). The real character: `StardustGuardianAcceptanceTests`, added by M3 B1, which skips without the owner's local material | Synthetic ✅ · real ☐ (owner) |
| Sources | Open the cited page from a feature offline | `AttachmentTests`; `PageImportTests` (page ranges and whole documents become draft reference entries; drafts inactive); e2e "attaches a PDF…" (attach, import pages, "Open …, p. 7", remove with a warning); smoke opens a generated PDF offline | Automated ✅ · owner ☐: the viewer shows the cited page |
| Safety | Malformed feature leaves sheet usable; fresh-install round trip | `MalformedContentTests`, `FormulaTests` (hostile and fuzzed), `ContentValidatorTests`, `HostileInputTests`, `PackageLimitTests`, `PackageRoundTripTests`, `ExportPurposeTests`, `UpgradeTests.Backup_includes_committed_data_still_in_the_wal_after_a_crash` | ✅ |
| Campaign | Two profiles show different allowed content | `CampaignTests`; e2e "shows different allowed content for two campaign profiles…" | ✅ |

## Definition of done (MVP.md)

| # | Requirement | Executable acceptance | Status |
| --- | --- | --- | --- |
| 1 | A real Windows build is installable and fully functional offline after installation | `scripts/pack-installer.ps1` builds the Velopack installer; `scripts/smoke.ps1` on the build; owner: `scripts/installer-smoke.ps1 -Adapter Velopack -OldBuild artifacts/installer/0.2.0 -NewBuild artifacts/installer/0.2.2` and `scripts/offline-check.ps1` | Automated ✅ · owner ☐ · clean VM ✗ |
| 2 | One SRD 5.1 and one SRD 5.2.1 character: save/reopen, level, rest, roll, trace | `M1AcceptanceTests` (traces for every major field), `M2AcceptanceTests` (both families on SRD casters), `SrdCasterTests`, e2e Brenna flow | ✅ |
| 3 | A Stardust Guardian test character uses a modifier, a class resource, a limited-use action and a reference-only feature; unsupported mechanics stay visible with a manual step | Synthetic: `HomebrewStudioTests`. Real: `StardustGuardianAcceptanceTests` (M3 B1; skips when `tests/RulesFixtures/local/` is absent) | Synthetic ✅ · real ☐ (the local material is not on this machine) |
| 4 | An imported PDF stays accessible from a linked feature/page; imported text never activates a rule without acceptance | `AttachmentTests`, `PageImportTests.A_page_range_becomes_a_draft_reference_entry_that_is_inactive_until_published`, `ImportQuarantineTests` | ✅ · owner ☐ (viewer page) |
| 5 | Export/import on a clean data directory keeps pinned content, character state, overrides, campaign and source metadata, and licensing notices; the user is told when a PDF is absent | `PackageRoundTripTests`, `M2AcceptanceTests` (play state, spells, notices), `CampaignTests` (campaign in packages), `SpellcastingCommandTests.Spells_and_spent_slots_survive…`, `AttachmentTests` ("No PDF attached" on a clean machine) | ✅ |
| 6 | Malformed formulas, missing references and unsupported cross-edition content give actionable errors without data loss | `MalformedContentTests`, `FormulaTests`, `ContentValidatorTests`, `SrdRulesFamilyTests`, `UpgradeTests.Data_folder_from_a_newer_build_is_refused_and_left_untouched` | ✅ |

## Release checks (MVP.md)

| Check | Evidence | Status |
| --- | --- | --- |
| Small domain tests for representative 2014/2024 rules | `RulesFamilySideBySideTests`, `ShortRestAndHitDiceTests` (three new policy fields), `SrdPackTests` and `SrdCasterTests` side-by-side content differences | ✅ |
| Import failure tests | `HostileInputTests`, `PackageLimitTests`, `ImportQuarantineTests` | ✅ |
| Export round-trip test | `PackageRoundTripTests`, `M2AcceptanceTests` | ✅ |
| Installer smoke check | `scripts/installer-smoke.ps1` (owner, below) | ☐ |
| Backup recovery | `UpgradeTests` (backup before every schema change, WAL-safe) | ✅ |
| Keyboard walkthrough | e2e keyboard test; owner walkthrough (`accessibility-checklist.md`), last run on 0.1.0 | ☐ re-run on 0.2.2 |
| Manual play rehearsal | Owner: one session with an SRD character (and the Stardust Guardian, M3) | ☐ |

## Owner checks for 0.2.2

1. **Installer upgrade:** `scripts/pack-installer.ps1` builds `artifacts/installer/0.2.2`. Then run `scripts/installer-smoke.ps1 -Adapter Velopack -OldBuild artifacts/installer/0.2.0 -NewBuild artifacts/installer/0.2.2`. Expect every step to pass. The database schema stays 4 (content v5 and character v6 are JSON inside it), so step 3 (backup before migration) is skipped. The character created by 0.2.0 must open under 0.2.2.
2. **Keyboard walkthrough** (`accessibility-checklist.md`, steps 1–5): add the new panels, the short rest (hit dice), death saves, "Spells and slots", "Attacks and actions", and the builder's spell picker.
3. **Narrator pass** (checklist items 15 and 17): the sheet (including the new panels), the builder, the rest proposal and the import preview.
4. **The PDF viewer lands on the cited page:** attach an SRD PDF to its source, and use "Open …, p. N" on a feature. The page shown must be N. The smoke proves only that the PDF loads.
5. **Stardust Guardian:** put the material in `tests/RulesFixtures/local/` and run `dotnet test --filter StardustGuardianAcceptanceTests` (M3 B1).

Record each result here: date, build, pass or fail, notes.

| Date | Build | Check | Result | Notes |
| --- | --- | --- | --- | --- |
| | 0.2.2 | | | |

## Could not be verified here

- **Clean machine** (ADR-008): a first install on a machine that never had TomeStack, a standard user without admin rights, an absent WebView2 Runtime, an offline install, SmartScreen and antivirus behavior for the unsigned build, and Windows 10.
- **What the PDF viewer shows on screen** (above, owner check 4).
- **The real Stardust Guardian character** (the material is not on this machine).

## Known limits against MVP.md's goal

- **"Create and play a level-1-to-20 SRD-based character under either rules family":** true for the eight casters, levels 1–20 in both families. The bundled non-casters are only the M1 Barbarian slice (levels 1–3), and Fighter, Monk and Rogue are not bundled. There is one species and one background per family (Half-Orc and Acolyte; Dwarf and Soldier). Other species, backgrounds and feats come from the homebrew studio or later SRD packs.
- **Armor:** no SRD armor table (text in the SRD, not bundled). Armor items come from homebrew.
- **Automated mechanics:** most class features are reference-only text with their pages. Main resources are tracked; the rest is by hand, as MVP "Homebrew: reference-only text" allows.
