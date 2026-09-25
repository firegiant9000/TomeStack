# ADR-008: Installer and distribution

Status: **proposed**. It is blocked on owner decisions: installer technology, framework-dependent vs. self-contained, code signing, and the Windows 10 support level. Installer-neutral evidence is recorded below.
Date: 2026-09-25

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

**Framework-dependent vs. self-contained:** self-contained is about 145 MB untrimmed (ADR-006). Framework-dependent needs the .NET 10 Desktop Runtime on the machine. That conflicts with R2 on a clean offline machine unless the installer bundles the runtime. The owner decides.

## Decision

Pending the owner. When decided, add an adapter for the chosen technology to `scripts/installer-smoke.ps1` and run it on a clean VM. Record the results here and in ADR-006 "Not yet proven", then move this ADR to accepted.

## Installer-neutral evidence (2026-09-25, Windows 11 26200)

| Check | Result |
| --- | --- |
| `UpgradeTests.Upgrading_the_schema_backs_up_the_database_first_and_keeps_the_data` | Pass. A simulated v2 migration creates `tomestack.db.v1.bak` with the data, and the upgraded database keeps it |
| `UpgradeTests.Backup_includes_committed_data_still_in_the_wal_after_a_crash` | Pass. **This found a bug and led to its fix:** the backup used to be a file copy of `tomestack.db` alone, and after a crash the WAL still held the committed data (here even the schema), so the backup would have been empty. It now uses SQLite's online backup API |
| `UpgradeTests.Data_folder_from_a_newer_build_is_refused_and_left_untouched` | Pass. A clear "update TomeStack" message; nothing is changed and no backup is taken |
| `scripts/installer-smoke.ps1 -Adapter Xcopy` (same build as old and new; harness plumbing) | Pass for steps 1, 2 and 4. Step 3 was skipped because the schema is unchanged |

Xcopy is not an installer. It shows that the harness works and that binaries upgrade in place over the same data folder. It does **not** prove R1, R4, R5, R6 or R7 for any real installer.

## Clean VM only (cannot be proven on the development machine)

A first install on a machine that never had TomeStack, a standard user without admin rights, a truly absent WebView2 Runtime and the runtime bootstrap, an offline install, SmartScreen behavior for signed and unsigned builds, and Windows 10.

Supersedes: none. Completes the open part of LIVING_SPECS D06 once accepted.
