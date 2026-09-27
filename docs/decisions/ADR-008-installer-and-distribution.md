# ADR-008: Installer and distribution

Status: **accepted** (owner decisions 2026-09-26). Clean-VM checks remain open; see the end.
Date: 2026-09-25 (proposed), 2026-09-26 (accepted)

## Context

MVP: "a real Windows build is installable and fully functional offline after installation". SPEC Q-01: a clean install launches offline and keeps data in a discoverable local location. ADR-001: per-user and local-only. ADR-006: WPF + WebView2 shell, which needs the Evergreen WebView2 Runtime.

## Requirements any choice must meet

| # | Requirement | How it is checked |
| --- | --- | --- |
| R1 | Per-user install with no admin prompt | Clean VM, standard user |
| R2 | Installs and runs offline | Clean VM with the network disabled; `scripts/offline-check.ps1 -Mode AssumeOffline` |
| R3 | Upgrading over an older build keeps the data, and a schema change creates `tomestack.db.v<old>.bak` first | `scripts/installer-smoke.ps1`; `UpgradeTests` |
| R4 | Uninstall **keeps** the data directory by default | `installer-smoke.ps1` step 4 |
| R5 | WebView2 Runtime missing: install it (offline Evergreen Standalone) or show the shell's message | Clean VM without the runtime; `offline-check.ps1 -Mode MissingRuntime` (simulated) |
| R6 | Ships the third-party notices from `ATTRIBUTION.md` | Inspect the installed folder |
| R7 | SmartScreen and antivirus do not block the install (signing decision) | Clean VM |

## Options and data-loss risks found

| | Velopack | MSIX | WiX (MSI) |
| --- | --- | --- | --- |
| Per-user, no admin | Yes (default) | Yes | Possible (`MSIINSTALLPERUSER`), but awkward |
| Updates | Built in (feed or local folder) | Store / App Installer | None built in |
| **R4 risk** | **Installs to `%LocalAppData%\{packId}` and deletes that whole folder on uninstall.** Our data folder is `%LOCALAPPDATA%\TomeStack`, so a packId of `TomeStack` **would delete user data**. Needs a different packId (for example `TomeStack.App`) or a different data folder | **Packaged desktop apps redirect newly created AppData files to a per-package location and remove them on uninstall.** Needs flexible virtualization / `unvirtualizedResources`, or a data folder outside AppData | Only removes what it installed. Data is safe by default |
| Signing | Optional (SmartScreen warns without it) | **Required** (a trusted certificate) | Optional |
| Offline install | Yes (Setup.exe) | Yes (.msix sideload) | Yes |

Sources: Velopack [preserved files](https://docs.velopack.io/integrating/preserved-files) and [Windows overview](https://docs.velopack.io/packaging/operating-systems/windows). Microsoft Learn [packaged desktop apps: file system](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-behind-the-scenes#file-system) and [flexible virtualization](https://learn.microsoft.com/windows/msix/desktop/flexible-virtualization). All were read on 2026-09-25 and still need confirming in the spike itself.

**Framework-dependent vs. self-contained:** self-contained is about 145 MB untrimmed (ADR-006). Framework-dependent needs the .NET 10 Desktop Runtime on the machine. That conflicts with R2 on a clean offline machine unless the installer bundles the runtime. **Decided: self-contained.**

## Decision

- **Velopack, per-user, self-contained** (win-x64, about 145 MB unpacked, so no .NET runtime is needed on the machine; R2). Built by `scripts/pack-installer.ps1`: `dotnet publish --self-contained`, then `vpk pack`. `vpk` is a repo-local dotnet tool pinned in `.config/dotnet-tools.json` (1.2.158), and the `Velopack` library has the same version.
- **Pack id `TomeStack.App`, never `TomeStack`.** Velopack installs to `%LOCALAPPDATA%\<packId>` and deletes that folder on uninstall. `%LOCALAPPDATA%\TomeStack` is the data folder (ADR-005), so the pack id must differ. `installer-smoke.ps1` refuses an install folder equal to the data folder and checks that the data folder survives uninstall.
- `App.Main` calls `VelopackApp.Build().Run()` first, so Velopack's install, update and uninstall hooks run and exit before WPF starts. TomeStack never creates an `UpdateManager`, so there is no update check or network call (ADR-001). Updates are installed by running a newer `Setup.exe`.
- **No code signing yet.** SmartScreen will warn (R7). Signing is required before a public release.
- **Windows 10: best-effort** (ADR-001). It is not tested and not blocking.
- `LICENSE`, `NOTICE` and `ATTRIBUTION.md` are copied next to `TomeStack.exe` by the shell project, so they are in every build and install (R6).

### Versioning

- `<Version>` in `Directory.Build.props` is the single source for the assembly version, `app.info`, the smoke report and `vpk --packVersion`.
- Semantic versions `MAJOR.MINOR.PATCH`, with no pre-release suffixes. Before 1.0, MINOR goes up when a milestone is delivered (M1 → 0.2.0) and PATCH for any other build given to a user. Velopack refuses to update to a version that is not higher, so a version is never reused or lowered.
- The bump goes in the same commit as the change being released. 0.1.1 is the first installable build.

## Installer-neutral evidence (2026-09-25, Windows 11 26200)

| Check | Result |
| --- | --- |
| `UpgradeTests.Upgrading_the_schema_backs_up_the_database_first_and_keeps_the_data` | Pass. A simulated v2 migration creates `tomestack.db.v1.bak` with the data, and the upgraded database keeps it |
| `UpgradeTests.Backup_includes_committed_data_still_in_the_wal_after_a_crash` | Pass. **This found a bug and led to its fix:** the backup used to be a file copy of `tomestack.db` alone, and after a crash the WAL still held the committed data (here even the schema), so the backup would have been empty. It now uses SQLite's online backup API |
| `UpgradeTests.Data_folder_from_a_newer_build_is_refused_and_left_untouched` | Pass. A clear "update TomeStack" message; nothing is changed and no backup is taken |
| `scripts/installer-smoke.ps1 -Adapter Xcopy` (same build as old and new; harness plumbing) | Pass for steps 1, 2 and 4. Step 3 was skipped because the schema is unchanged |
| `installer-smoke.ps1 -Adapter Xcopy`, **old = the item 1 commit (database schema 1, M0 effect model), new = the item 9 commit (database schema 2, typed effects)**, framework-dependent publishes | **Pass, all 4 steps.** The old build seeded v1 revisions and saved a character. The new build opened the same folder, migrated it (`charactersAtStart: 1`, `schemaVersion: 2`) and wrote `tomestack.db.v1.bak`, and the data folder survived "uninstall" |

Both builds report app version 0.1.0 because `Directory.Build.props` was not bumped (fixed by the versioning policy above).

Xcopy is not an installer. It shows that the harness works and that binaries upgrade in place over the same data folder. It does **not** prove R1, R4, R5, R6 or R7 for any real installer.

## Velopack evidence (2026-09-26, Windows 11 26200, development machine, admin-capable user)

`scripts/installer-smoke.ps1 -Adapter Velopack -OldBuild artifacts/installer/0.1.0 -NewBuild artifacts/installer/0.1.1`, run under Windows PowerShell 5.1 and again under pwsh 7.6, **passed every step both times**:

| Step | Result |
| --- | --- |
| 1. Install the old build with `Setup.exe --silent` and smoke on a fresh data folder | Pass: v0.1.0, database schema 1, installed to `%LOCALAPPDATA%\TomeStack.App` with no elevation prompt |
| 2. Run the new `Setup.exe --silent` over it and smoke again | Pass: v0.1.1, schema 2, the character from step 1 is still there |
| 3. Backup before migration | Pass: `tomestack.db.v1.bak` |
| 5. Notices and version | Pass: `LICENSE`, `NOTICE` and `ATTRIBUTION.md` in `current\`; 0.1.0 → 0.1.1 |
| 4. `Update.exe --uninstall --silent` | Pass: the app folder and its shortcuts are gone; the test data folder and the real `%LOCALAPPDATA%\TomeStack` are untouched |

The old build is the item 8 commit (`b476810`, database schema 1), packed as 0.1.0. It was patched only to add the same `VelopackApp.Build().Run()` entry point, because M0 builds had none and Velopack's install hooks need it. That patch was never committed. Both runs used `-DataDir` with a throwaway folder, so the installed app did not open the real data folder on this machine.

This proves R3, R4 and R6 for Velopack on this machine, and R1 as far as "no elevation prompt as this user" goes.

**M1 upgrade (2026-09-26):** 0.1.1 (M0 closeout, which seeds the test fixtures) → 0.2.0 (M1, which seeds only the SRD packs), Windows PowerShell 5.1: all steps pass. The database schema is unchanged (2), because content schema v3 lives in the stored JSON and needs no migration, so step 3 is skipped as expected. The character created by 0.1.1 with fixture content is still there and still opens under 0.2.0.

## Clean VM only (cannot be proven on the development machine)

A first install on a machine that never had TomeStack, a standard user without admin rights (R1), a truly absent WebView2 Runtime and the runtime bootstrap (R5), an offline install (R2), SmartScreen and antivirus behavior for the unsigned build (R7), the installed app on the real default data folder, and Windows 10 (best-effort).

Supersedes: none. Completes the open part of LIVING_SPECS D06 once accepted.
