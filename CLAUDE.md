# TomeStack

Local-first Windows desktop app (WPF + WebView2 shell, React/TS UI, .NET 10 service, SQLite). The specs in `docs/` are authoritative: SPEC is behavior, MVP is the release boundary, and ROADMAP is the milestones. Follow `docs/LIVING_SPECS.md` for changes: update specs/ADRs and `docs/CHANGELOG.md` in the same change.

## Gate (all must pass; warning budget 0: `TreatWarningsAsErrors` is on)

```
npm ci --prefix src/Ui
npm run lint --prefix src/Ui
npm test --prefix src/Ui
npm run build --prefix src/Ui      # must precede the .NET build; the shell copies src/Ui/dist
dotnet build TomeStack.slnx -c Release
dotnet test TomeStack.slnx -c Release
npm run test:e2e --prefix src/Ui   # after the .NET build: UI flow (Vitest + Testing Library) against the real DevHost
scripts/smoke.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe   # Windows GUI smoke (checks the report)
```

## Invariants

- `RulesCore` stays free of persistence, UI and Windows references.
- Keep `srd-5.1` and `srd-5.2.1` separate. Encode differences as fields on `RulesFamilyPolicy`, never by name.
- Only `published` revisions affect calculations. Published revisions are insert-only; change content by adding a new revision.
- Imported content (PDF candidates, packages) never executes code and never becomes active without review.
- UI components call `src/Ui/src/api/client.ts` only. Only `transport.ts` (and the e2e DevHost harness) may use `fetch` (lint-enforced).
- The shipped app opens no listening socket (ADR-006). `src/DevHost` is dev-only.
- Fixtures must be original. SRD text lives only in `src/AppService/Content/` (CC-BY-4.0, reviewed in `docs/licensing/srd-pack-review.md`); add no more SRD or any third-party rules text without updating that review (SPEC Q-03).
- The repo is **public** on GitHub.
