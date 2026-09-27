# Attribution and third-party notices

TomeStack's own code is licensed under the Apache License 2.0 ([LICENSE](LICENSE), [NOTICE](NOTICE); LIVING_SPECS D07, decided 2026-09-26). Every third-party license below is compatible with it.

## Rules content

TomeStack ships **no SRD or third-party rules text** today. The only bundled content is the original test fixtures in `tests/RulesFixtures/`, which were written for this project (SPEC Q-03). SRD 5.1 and SRD 5.2.1 content will be added only after the owner approves the attribution statements in [docs/licensing/srd-attribution-draft.md](docs/licensing/srd-attribution-draft.md) (ADR-007).

## Third-party components in the shipped app

Checked on 2026-09-25 from `npm ls --omit=dev` and `dotnet list src/DesktopShell package --include-transitive`. Development-only tools (Vite, TypeScript, ESLint, Vitest, xUnit and similar) are not shipped and are not listed.

| Component | Version | License | Copyright |
| --- | --- | --- | --- |
| React (`react`) | 19.3.0 | MIT | Meta Platforms, Inc. and affiliates |
| React DOM (`react-dom`) | 19.3.0 | MIT | Meta Platforms, Inc. and affiliates |
| `scheduler` (dependency of React DOM) | 0.28.0 | MIT | Meta Platforms, Inc. and affiliates |
| Microsoft.Web.WebView2 (SDK) | 1.0.4191.47 | BSD-3-Clause (package `LICENSE.txt`) | Microsoft Corporation |
| Microsoft.Data.Sqlite / .Core | 10.0.12 | MIT | Microsoft Corporation |
| SQLitePCLRaw (`core`, `bundle_e_sqlite3`, `provider.e_sqlite3`, `lib.e_sqlite3`) | 2.1.12 | Apache-2.0 | SourceGear, LLC |
| SQLite (native `e_sqlite3`, via SQLitePCLRaw) | bundled | Public domain | D. Richard Hipp and contributors |
| .NET runtime, WPF (self-contained publish and the installer) | 10.0 | MIT | .NET Foundation and contributors |
| Velopack (library, plus the installer's `Setup.exe` and `Update.exe`) | 1.2.158 | MIT | Velopack Ltd. |

The Microsoft Edge WebView2 **Runtime** is not redistributed. It is a system component that is preinstalled on Windows 11 or installed by the user. The UI uses system fonts and ships no fonts or icon sets.

The Velopack installer (ADR-008) installs this file, `LICENSE` and `NOTICE` next to `TomeStack.exe` (checked by `scripts/installer-smoke.ps1`). BSD-3-Clause, Apache-2.0 and MIT all require the notice to be kept. Velopack's `Setup.exe` and `Update.exe` are compiled from Rust and statically include third-party crates; their individual notices have not been reviewed yet (open item before public release).
