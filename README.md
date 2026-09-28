# TomeStack

A local-first Windows desktop app for building fifth-edition characters and homebrew. It works offline, with no account.

> **Status: v0.3.0, a limited-content build.** The M2 "Usable MVP" checks passed, but the MVP goal (any SRD character, levels 1–20) is not met yet. M2.1 (data safety) is implemented and not released yet. The M3 and M4 engineering is done, and neither gate is met ([ROADMAP](docs/ROADMAP.md)).
>
> - **Classes you can build:** the eight SRD spellcasters (Bard, Cleric, Druid, Paladin, Ranger, Sorcerer, Warlock, Wizard), levels 1–20 in both rules families, and the Barbarian at levels 1–3. **Not yet:** Fighter (next, ROADMAP M2.2), Monk, Rogue, Barbarian 4–20, and the SRD armor table.
> - **Species and backgrounds:** one each per family (Half-Orc and Acolyte for SRD 5.1; Dwarf and Soldier for SRD 5.2.1). Anything else you add in the homebrew studio.
> - **Works today:** build under SRD 5.1 (2014) or SRD 5.2.1 (2024) rules as a cancelable draft (classes, level-ups, multiclassing, spells, every choice), and see every calculated field with a source-aware trace. Play: hit points, resources, spell slots, conditions, equipment, rolls, short and long rests, death saves. Author homebrew subclasses and features, review updates, attach your own PDFs to open cited pages, keep campaign profiles, and print a sheet. Back up and restore your whole library, PDFs included, or export one character to back it up or share it.
> - **Experimental:** reading a PDF's text and proposing candidates (M4). Nothing becomes a rule until you review and publish it, and it has not yet been proven on a real third-party book.
> - **Evidence:** checks are either fixture-verified (automated tests), Windows-install verified (the CI desktop smoke, or an owner check on an installed build), or accepted in real play. Nothing is accepted in real play yet (that is the M3 gate). See [features/m2-acceptance.md](docs/features/m2-acceptance.md#evidence-levels-m21-2026-09-28).
>
> The bundled content is a small, attributed slice of both SRDs (CC-BY-4.0; see [ATTRIBUTION.md](ATTRIBUTION.md)).

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
scripts/single-instance-check.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe   # two processes on one data folder
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
- Package import is preview-then-apply, with size limits, a fixed path layout and SHA-256 verification. Character packages never include PDFs. Only a full library backup contains your managed PDF copies, and it is personal: do not share it.
- One TomeStack per data folder: a second launch brings the open window forward.
- 2014 and 2024 rules are separate rules-family IDs, and their differences are explicit policy fields.

## License

TomeStack's code is licensed under the [Apache License 2.0](LICENSE); see also [NOTICE](NOTICE) (LIVING_SPECS D07). Fixture content is original to this project. Third-party components are listed in [ATTRIBUTION.md](ATTRIBUTION.md), and export and license policy is in [ADR-007](docs/decisions/ADR-007-export-package-and-license-policy.md). TomeStack is not affiliated with or endorsed by Wizards of the Coast.
