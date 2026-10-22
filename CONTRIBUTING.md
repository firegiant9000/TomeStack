# Contributing to TomeStack

TomeStack is a local-first Windows desktop app: a WPF + WebView2 shell, a React/TypeScript UI, a .NET 10 service and SQLite. Setup and the everyday build are in the [README](README.md#build-and-test).

The specs in [`docs/`](docs/) are authoritative: [SPEC](docs/SPEC.md) is behavior, [MVP](docs/MVP.md) is the release boundary and [ROADMAP](docs/ROADMAP.md) is the milestones. Changes follow [LIVING_SPECS](docs/LIVING_SPECS.md#change-workflow): update the specs or ADRs and [CHANGELOG](docs/CHANGELOG.md) in the same change as the code.

## The gate

Every change passes all of these, in this order. The warning budget is 0 (`TreatWarningsAsErrors` is on).

```
npm ci --prefix src/Ui
npm run lint --prefix src/Ui
npm test --prefix src/Ui
npm run build --prefix src/Ui      # must precede the .NET build; the shell copies src/Ui/dist
dotnet build TomeStack.slnx -c Release
dotnet test TomeStack.slnx -c Release
npm run test:e2e --prefix src/Ui   # after the .NET build: UI flow (Vitest + Testing Library) against the real DevHost
scripts/smoke.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe   # Windows GUI smoke (checks the report)
scripts/single-instance-check.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe   # two processes on one data folder (M2.1)
```

## Invariants

- `RulesCore` stays free of persistence, UI and Windows references (enforced by `RulesCoreBoundaryTests`).
- Keep `srd-5.1` and `srd-5.2.1` separate. Encode differences as fields on `RulesFamilyPolicy`, never by name.
- Only `published` revisions affect saved characters. The one exception is the studio sandbox (`DraftOverlayCatalog`, `content.sandbox`), which calculates one draft on an unsaved copy and writes nothing (LIVING_SPECS D14). Published revisions are insert-only; change content by adding a new revision.
- Imported content (PDF candidates, packages) never executes code and never becomes active without review.
- UI components call `src/Ui/src/api/client.ts` only. Only `transport.ts` (and the e2e DevHost harness) may use `fetch` (lint-enforced).
- The shipped app opens no listening socket (ADR-006). `src/DevHost` is dev-only.
- Fixtures must be original. SRD text lives only in `src/AppService/Content/`, and in screenshots of it listed in the review (`docs/media/`) (CC-BY-4.0, reviewed in `docs/licensing/srd-pack-review.md`); add no more SRD or any third-party rules text without updating that review (SPEC Q-03).
- The repository is public: no secrets, personal data or third-party text in commits.
