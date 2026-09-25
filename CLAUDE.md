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
src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe --smoke --smoke-report <path>   # Windows GUI smoke
```

## Invariants

- `RulesCore` stays free of persistence, UI and Windows references.
- Keep `srd-5.1` and `srd-5.2.1` separate. Encode differences as fields on `RulesFamilyPolicy`, never by name.
- Only `published` revisions affect calculations. Published revisions are insert-only; change content by adding a new revision.
- Imported content (PDF candidates, packages) never executes code and never becomes active without review.
- UI components call `src/Ui/src/api/client.ts` only. Only `transport.ts` may use `fetch` (lint-enforced).
- The shipped app opens no listening socket (ADR-006). `src/DevHost` is dev-only.
- Fixtures must be original. Add no SRD or third-party rules text without an attribution/license review (SPEC Q-03).
- The repo is **public** on GitHub.
