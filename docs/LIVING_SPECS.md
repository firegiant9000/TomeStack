# Living specifications and decision log · v0.1

## Source of truth

`SPEC.md` describes promised behavior; `ARCHITECTURE.md` describes the current proposed implementation; `MVP.md` and `ROADMAP.md` scope delivery; `BACKLOG.md` retains approved ideas. A shipped feature requires an acceptance example and a changelog entry. Git tracks full history once this set is placed in a repository. Do not replace documents wholesale at each milestone.

## Change workflow

1. File a change with motivation, affected spec IDs, edition(s), source/license impact and example character.
2. Update the behavioral spec and acceptance example first. If architecture changes, add or supersede an ADR in `docs/decisions/` and update the diagram.
3. Add/update rules fixtures or an interaction rehearsal; make data/export migration explicit where relevant.
4. Land code and doc change together; log user-visible behavior in `CHANGELOG.md`. Mark features delivered only when end-to-end acceptance passes.
5. For homebrew content revisions, publish immutable revisions and offer characters a reviewed migration; do not mutate pinned rule history silently.

## Suggested repository structure

```text
README.md
docs/SPEC.md                  docs/ARCHITECTURE.md
docs/MVP.md                   docs/ROADMAP.md
docs/BACKLOG.md               docs/CHANGELOG.md
docs/decisions/ADR-001-*.md  docs/features/*.md
docs/diagrams/*.drawio        docs/examples/*.json
src/DesktopShell/             src/Ui/
src/AppService/               src/RulesCore/
src/ImportWorker/             tests/RulesFixtures/
```

## ADR template

```markdown
# ADR-NNN: Title
Status: proposed | accepted | superseded
Date: YYYY-MM-DD
Context: The concrete choice and constraints.
Decision: What we will do.
Consequences: Benefits, costs, migration and security implications.
Alternatives considered: Options actually evaluated.
Evidence: Spike/test/doc links.
Supersedes: ADR-NNN if applicable.
```

## Changelog seed

Create `CHANGELOG.md` with `## Unreleased` and subsections `Added`, `Changed`, `Fixed`, `Migration`. A release entry links affected spec IDs and notes export/schema/database changes. Do not record planned backlog items as shipped changes.

## Pending product decisions

| ID | Question | Working default | Decide by |
| --- | --- | --- | --- |
| D01 | Which rests/recoveries auto-apply? | Preview deterministic deltas, ask for contextual choices | M2 UX rehearsal |
| D02 | Copy or externally link a PDF by default? | **Decided 2026-09-25:** managed copy for reliable page links, removable attachment. Data stays in `%LOCALAPPDATA%\TomeStack`, overridable with `--data-dir` or `TOMESTACK_DATA_DIR`. To be recorded in ADR-005 | Decided |
| D03 | What goes into shared packages? | JSON and licensed assets; omit third-party PDFs | M2 export test |
| D04 | How much multiclass/spellcasting in MVP? | SRD-supported paths under both editions. M1 field order: ability modifiers, proficiency bonus, saves, skills, then AC and HP | M1 fixture review |
| D05 | What accessibility target? | **Decided 2026-09-25:** WCAG 2.2 AA for the builder and sheet, including full keyboard use, text zoom and contrast | Decided |
| D06 | Which Windows shell/IPC? | **Decided (ADR-006):** WPF/WebView2 shell, in-process service, WebView2 message bridge; loopback host dev-only. **Installer decided 2026-09-25:** Velopack, per-user, self-contained, unsigned until public release. To be proven and recorded in ADR-008 | Installer spike (M0) |
| D07 | License for the project's own code? | **Decided 2026-09-25:** Apache-2.0 (`LICENSE`) | Decided |
| D08 | Name availability and public branding? | TomeStack working name; trademark check before launch | Before public release |
| D09 | Which SRD license route? | **Decided 2026-09-25:** SRD 5.1 and SRD 5.2.1 under CC-BY-4.0. No SRD text ships until the owner approves the exact attribution statements (SPEC Q-03). No Wizards of the Coast trademarks in branding | Attribution review (M0) |
| D10 | Which Windows versions? | **Decided 2026-09-25:** Windows 10 and Windows 11. The installer must provide the WebView2 runtime when it is missing (common on Windows 10) | Installer spike (M0) |
| D11 | Seed test fixtures into user data? | **Decided 2026-09-25:** keep for M0. Stop seeding, and remove seeded fixtures with a migration, once real SRD content ships (M1) | M1 |
| D12 | Third-party homebrew in the repo? | **Decided 2026-09-25:** none. The Stardust Guardian material is a friend's third-party work: out of scope for now, and never committed to this public repo without the author's permission | Before any M3 work |

## Change history

- **2026-09-24 · v0.1:** Initial product specification, Windows-first architecture proposal, two-rule-family MVP, accepted 20-item backlog, milestone plan and editable diagram. No implementation is claimed.
- **2026-09-24 · M0 foundation:** Documents moved to `docs/`. The desktop spike resolved D06: in-process service over the WebView2 message bridge instead of a local ASP.NET Core service ([ADR-006](decisions/ADR-006-desktop-host-and-ipc.md)). Content IDs and pins are recorded in [ADR-002](decisions/ADR-002-edition-aware-ids-and-revision-pins.md), and package format v1 in [features/package-format.md](features/package-format.md). The first vertical slice (initiative with trace, local save, export/import) is implemented against original fixtures; see [CHANGELOG.md](CHANGELOG.md). The functional overview diagram (`diagrams/TomeStack-Architecture.svg`) does not depict process topology, so ADR-006 needs no diagram change. The repo has no editable `.drawio` source yet.
- **2026-09-25 · owner decisions:** D02, D05, D07 and the installer part of D06 decided. D09–D12 added and decided (SRD route, Windows 10/11, fixture seeding, third-party homebrew). Code is licensed Apache-2.0. The M3 Stardust Guardian acceptance fixture is deferred until the author grants permission (D12).
