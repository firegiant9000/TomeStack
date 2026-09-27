# TomeStack

A local-first Windows desktop app for building fifth-edition characters and homebrew. It works offline, with no account.

> **Status: M1 rules core in progress.** You can create a character under SRD 5.1 (2014) or SRD 5.2.1 (2024) rules and see every calculated field with a source-aware trace. You can override it, save it locally, and export or import a portable package (a personal backup, or a share that leaves out content you may not share). The bundled content is a small, attributed slice of both SRDs (CC-BY-4.0; see [ATTRIBUTION.md](ATTRIBUTION.md)). There is no builder UI yet (M2).

Specs live in [`docs/`](docs/). [SPEC](docs/SPEC.md) is the behavioral source of truth. [MVP](docs/MVP.md) sets the release boundary, [ROADMAP](docs/ROADMAP.md) the milestones, and [LIVING_SPECS](docs/LIVING_SPECS.md) covers the change process and open decisions. Decisions are in [`docs/decisions/`](docs/decisions/).

## Layout

```text
src/RulesCore/      Pure rules: rules-family policies, content/character/provenance types, calculator + trace
src/AppService/     Application service: SQLite store, package export/import, CommandDispatcher (JSON protocol)
src/ImportWorker/   Import contracts; candidates can only become inactive drafts
src/DesktopShell/   WPF + WebView2 shell (TomeStack.exe); in-process service over the message bridge
src/DevHost/        Development-only loopback host for running the UI in a browser
src/Ui/             React + TypeScript (Vite)
tests/RulesFixtures/  Original fixture content and characters for both rules families
tests/*.Tests/      xUnit tests
docs/               Specs, ADRs, feature docs, changelog, diagram
```

Dependencies point inward: `DesktopShell`/`DevHost` → `AppService` → `RulesCore`. `RulesCore` has no persistence, UI or Windows references.

## Prerequisites (Windows 10/11)

- .NET SDK 10.0.1xx or later (`global.json` rolls forward)
- Node.js 22 with npm
- Microsoft Edge WebView2 Runtime (preinstalled on Windows 11)

## Build and test

Run from the repository root (PowerShell or Git Bash). The UI bundle must be built before the desktop shell, which copies it:

```powershell
npm ci --prefix src/Ui
npm run lint --prefix src/Ui
npm test --prefix src/Ui
npm run build --prefix src/Ui          # typecheck + production bundle -> src/Ui/dist
dotnet build TomeStack.slnx -c Release
dotnet test TomeStack.slnx -c Release
npm run test:e2e --prefix src/Ui       # UI flow against the real DevHost (needs the Release build above)
```

CI runs the same steps on `windows-latest` ([.github/workflows/ci.yml](.github/workflows/ci.yml)).

## Run

**Desktop app:**

```powershell
dotnet run --project src/DesktopShell
```

Data goes to `%LOCALAPPDATA%\TomeStack` (override with `TOMESTACK_DATA_DIR` or `--data-dir <path>`; a folder inside OneDrive or another sync root gets a warning). Pass `--devtools` to enable WebView2 DevTools. The app seeds the bundled SRD packs. Set `TOMESTACK_DEV_FIXTURES=1` to also seed the original test fixtures (DevHost always does).

**Self-test.** This launches the shell, loads the UI, round-trips commands over the bridge, creates a fixture character, exports it and previews the package. It then exits 0/2 and writes a JSON report:

```powershell
src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe --smoke --smoke-report smoke.json
scripts/smoke.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe       # same run, with the report checked
scripts/offline-check.ps1 -Mode MissingRuntime     # simulated missing WebView2 runtime (smoke-only flag)
scripts/offline-check.ps1 -Mode AssumeOffline      # turn on airplane mode first
```

Every script in `scripts/` runs under both Windows PowerShell 5.1 and pwsh 7 (checked 2026-09-26: `smoke`, `offline-check` in all modes, `installer-smoke` with both adapters, and `pack-installer`).

**UI with hot reload in a browser** (two terminals):

```powershell
dotnet run --project src/DevHost        # 127.0.0.1:5178, data in %LOCALAPPDATA%\TomeStack-dev
npm run dev --prefix src/Ui             # http://127.0.0.1:5173
```

The dev host writes a fresh token to `<data dir>/devhost.token` at each launch, and the Vite proxy attaches it. If you set `TOMESTACK_DATA_DIR`, set it the same way for both processes.

**Self-contained publish:**

```powershell
dotnet publish src/DesktopShell -c Release -r win-x64 --self-contained true -o artifacts/publish
```

**Installer** (Velopack, per-user, self-contained, unsigned; [ADR-008](docs/decisions/ADR-008-installer-and-distribution.md)). Build the UI first. `vpk` is a repo-local tool, so run `dotnet tool restore` once:

```powershell
dotnet tool restore
scripts/pack-installer.ps1        # -> artifacts/installer/<version>/TomeStack.App-win-Setup.exe
scripts/installer-smoke.ps1 -Adapter Velopack -OldBuild artifacts/installer/<old> -NewBuild artifacts/installer/<new>
```

It installs to `%LOCALAPPDATA%\TomeStack.App`. Uninstalling removes only that folder, never the data folder. The version comes from `Directory.Build.props` and must go up with every build you hand out.

## Safety defaults

- Offline: the shell refuses any network request outside the app's virtual host, and the production bundle has a strict CSP.
- Only published content revisions affect calculations. Imported candidates become drafts, and imported effects are reference-only until reviewed.
- Package import is preview-then-apply, with size limits, a fixed path layout and SHA-256 verification. PDFs are never packaged.
- 2014 and 2024 rules are separate rules-family IDs, and their differences are explicit policy fields.

## License

TomeStack's code is licensed under the [Apache License 2.0](LICENSE); see also [NOTICE](NOTICE) (LIVING_SPECS D07). Fixture content is original to this project. Third-party components are listed in [ATTRIBUTION.md](ATTRIBUTION.md), and export and license policy is in [ADR-007](docs/decisions/ADR-007-export-package-and-license-policy.md).
