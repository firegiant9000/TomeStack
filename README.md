# TomeStack

A local-first Windows desktop app for building fifth-edition characters and homebrew. It works offline, with no account.

> **Status: M0 foundation.** You can create a character under SRD 5.1 (2014) or SRD 5.2.1 (2024) rules, see its initiative with a source-aware calculation trace, override it, save it locally, and export/import a portable package. The only content is original test fixtures. No SRD text ships yet (pending attribution review, SPEC Q-03).

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
```

CI runs the same steps on `windows-latest` ([.github/workflows/ci.yml](.github/workflows/ci.yml)).

## Run

**Desktop app:**

```powershell
dotnet run --project src/DesktopShell
```

Data goes to `%LOCALAPPDATA%\TomeStack` (override with `TOMESTACK_DATA_DIR` or `--data-dir <path>`). Pass `--devtools` to enable WebView2 DevTools.

**Self-test** (launches the shell, loads the UI, round-trips commands over the bridge, then exits 0/2 and writes a JSON report):

```powershell
src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe --smoke --smoke-report smoke.json
```

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

No installer yet (see ADR-006, "Not yet proven").

## Safety defaults

- Offline: the shell refuses any network request outside the app's virtual host, and the production bundle has a strict CSP.
- Only published content revisions affect calculations. Imported candidates become drafts, and imported effects are reference-only until reviewed.
- Package import is preview-then-apply, with size limits, a fixed path layout and SHA-256 verification. PDFs are never packaged.
- 2014 and 2024 rules are separate rules-family IDs, and their differences are explicit policy fields.

## License

Code is licensed under the [Apache License 2.0](LICENSE). Fixture content is original to this project. SRD content, once added, will be used under CC-BY-4.0 with its required attribution (LIVING_SPECS D09). TomeStack is not affiliated with or endorsed by Wizards of the Coast.
