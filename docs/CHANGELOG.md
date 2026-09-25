# Changelog

## Unreleased

### Added

- M0 foundation: repository layout, .NET 10 solution, React/TypeScript UI, CI workflow on Windows.
- Rules core with the `srd-5.1` and `srd-5.2.1` rules-family IDs and an explicit policy difference: ability score increases come from species in 2014 rules and from background in 2024 rules (SPEC S-02, C-01).
- Initiative is calculated with a source-aware trace. Each step lists its operation, amount, result, and the rules family, content revision, effect, source title and page behind it. Invalid, draft, wrong-family and missing content is isolated with diagnostics (SPEC C-03).
- Labeled user overrides are the final layer; the calculated value and its trace are kept (SPEC C-06).
- Local SQLite storage with numbered migrations and a backup before schema upgrades. Published content revisions are insert-only (SPEC I-06, ADR-002).
- Portable package export/import (format v1). It includes pinned revisions, sources, license notices and SHA-256 hashes, and imports go through a preview step. Packages are restricted to a fixed layout with size limits (SPEC P-02, Q-02; docs/features/package-format.md).
- Import-worker contracts. Candidates can only become inactive drafts (SPEC I-01).
- WPF + WebView2 desktop shell. It uses an in-process service over the WebView2 message bridge, blocks non-app network requests, and has a `--smoke` self-test (ADR-006).
- Original M0 test fixtures for both rules families (no SRD or third-party text).
- `ATTRIBUTION.md` lists the licenses of the third-party components that ship. ADR-007 (proposed) covers export and license policy. The SRD 5.1 and 5.2.1 CC-BY-4.0 attribution statements are drafted verbatim from the official documents for owner approval (`docs/licensing/srd-attribution-draft.md`). There is still no SRD content and no project `LICENSE` (D07 open).
- `--smoke` also proves the M0 exit gate in the built app: it creates a character with fixture content, exports it and previews the package. The report includes `charactersAtStart`, so two runs on one data folder prove persistence. `scripts/smoke.ps1` checks the report, and `scripts/offline-check.ps1` covers a simulated missing WebView2 runtime and a manual airplane-mode run (ADR-006).
- UI flow test (`npm run test:e2e`) drives create → sheet → override → export → import, plus keyboard-only access, against the real DevHost. It uses Vitest with Testing Library and jsdom, which are dev-only. It is part of the gate and CI. There is an accessibility checklist for the working-default target (formal target D05 still open): `docs/features/accessibility-checklist.md`.
- ADR-003: a typed declarative effect model with explicit stacking (`stack` / `highestInGroup`), operations (`bonus` / `set` / `replace`) and timing, plus the bounded formula grammar (SPEC I-04, I-05, Q-02).
- A bounded formula parser and evaluator in the rules core (ADR-003 grammar: `+ - * /`, parentheses, `floor`, `ceil`, `min`, `max`, `abs`, and `PB`, `LEVEL`, `CLASS_LEVEL`, `<ABL>.MOD`, `<ABL>.SCORE`). Limits: 200 characters, 64 tokens, depth 8, literals ≤ 10,000, intermediates within ±1,000,000. A failing formula disables only its own effect, with an `effect.invalid-formula` diagnostic that names the feature, the effect and the error code (SPEC C-03, Q-02).
- Dependency-ordered calculation (item 11). The sheet has ability scores and modifiers, a proficiency bonus from level, all six saving throws, Stealth and initiative. Each field is a node in a graph whose edges are its base inputs plus every identifier its effects' formulas read. `grant` effects give proficiency or expertise. Stacking follows ADR-003 (highest `replace`, then bonuses with `highestInGroup`, then highest `set`). A formula that would create a cycle (a self-loop, or score → modifier → score) disables only the effects in the cycle, with `effect.dependency-cycle` naming the path (SPEC C-02, C-03).
- A second explicit rules-family difference, `RulesFamilyPolicy.BackgroundGrantsFeat`: 2024 backgrounds may grant a feat, 2014 backgrounds may not (`policy.background-feat`). `grant` effects of kind `content` bring in another revision, one level deep only (`grant.nested-ignored`), and the trace says "granted by …".
- Per-character cross-family exceptions (BACKLOG B06), stored as data (`crossFamilyExceptions`, with a required reason). Recorded content applies under the character's own family with a `content.cross-family-exception` warning. The UI for recording one is still to come.
- Original M1 fixtures (`tests/RulesFixtures/fixture-pack-m1.json`, test-only) with a deliberate cross-edition conflict: two different revisions named "Fixture Keen Senses". Side-by-side tests show the same inputs giving different, explained outputs per family (`tests/RulesFixtures/README.md`).
- Dice engine skeleton in the rules core (`docs/features/dice-engine.md`): bounded `NdM±K` expressions, advantage and disadvantage on a single d20, critical dice doubling, a seeded RNG for tests, and a CSPRNG for play. Roll records hold the formula, every die, modifiers with origins, and provenance. Rolling never consumes a resource (SPEC C-04). There is no UI yet.
- ADR-008 (installer and distribution, proposed; the technology is an owner decision) sets out the requirements and the options. It records two data-loss risks: Velopack's default install folder is the data folder, and MSIX virtualizes AppData. `scripts/installer-smoke.ps1` is an installer-neutral install → smoke → upgrade → smoke → uninstall harness. Only an Xcopy adapter exists so far.
- ADR-001 (local-only Windows, accepted), ADR-004 (review before publish, accepted; evidence is the quarantine and draft tests) and ADR-005 (managed PDF copy vs. link, proposed and blocked on D02).
- JSON Schemas for source, content revision, character and package manifest (v1) in `docs/schemas/`. A test validates every fixture and a real exported package against them.

### Changed

- Trace shape (ARCHITECTURE step 5): a field's trace now includes the steps of every field it reads, in dependency order. Each entry names its `field` and the `inputs` it read, and each value has `units`. Initiative gains an explicit "starts at the Dexterity modifier" step. A field's warnings include its inputs' warnings, so an ignored Dex increase still explains initiative. Overrides stay the final layer, and dependents read the overridden value.
- The ability-increase policy now restricts only *origin* content (species and background). Feats and class features may raise scores under both rules families; M0 blocked them by mistake.
- The sheet groups its fields and shows each one as an expandable card with its own override form. Override inputs are no longer shared between fields, and derived values no longer use `<output>` (an implicit live region).
- Effect values are formulas. The M0 rule "amount must be between -10 and 10" (`effect.invalid-amount`) is replaced by the formula bounds and `effect.invalid-formula`.
- CI records evidence for the GUI smoke (a WebView2 Runtime probe and the session), runs it through `scripts/smoke.ps1`, adds the missing-runtime check, and uploads the smoke report. The smoke stays non-blocking until it passes on hosted runners (ADR-006). `workflow_dispatch` was added for manual runs.
- Export in the desktop app opens a native Save dialog provided by the shell (`package.saveAs`) instead of WebView2's download flow, which saved silently to Downloads. Browser development against DevHost still falls back to a download (ADR-006).
- D06 resolved: the application service runs in-process behind the WebView2 message bridge instead of as a local ASP.NET Core service. The loopback host is development-only (ADR-006).
- Spec documents moved into `docs/`, and the diagram into `docs/diagrams/`.

### Fixed

- The shell showed an unhandled exception when its UI bundle was missing. It now shows an error and exits cleanly (found by the spike's negative control).
- After an import, the summary (including where the replaced character was backed up) was cleared as soon as the character opened. It now stays visible. Found by the UI flow test.
- In dark mode, error, accent and warning text failed 4.5:1 contrast (2.9, 3.0 and 3.5 to 1). They now use `light-dark()` shades at 7.8 to 10.1 to 1.
- The database backup before a schema upgrade used to copy only `tomestack.db`. In WAL mode, committed data can still be in `tomestack.db-wal` after a crash, so the backup could silently miss it. It now uses SQLite's online backup API (`UpgradeTests`). Opening a data folder from a newer build now shows a clear "update TomeStack" message and changes nothing.
- A character or content revision with a `schemaVersion` newer than the build supports used to be accepted silently. It is now refused on import (`package.schema-unsupported`) and on save (`character.schema-unsupported`), and the calculator isolates it (`content.schema-unsupported`).
- Package import used to replace an existing local character after only a preview warning. It now first saves the local copy to `backups/pre-import-*.tomestack.zip`, and importing that file restores it. If the backup cannot be written, the import is refused (SPEC C-07, Q-01; restore test in `PackageRoundTripTests`).
- Package import used to upsert source records, which could silently overwrite local license and redistribution metadata. The preview now shows a field-by-field diff for each differing source, and apply requires an explicit "keep local" or "use imported" choice per source (SPEC S-01, Q-03).
- Exports no longer include a source's `pdfRef`. It is a machine-local path that can contain the Windows user name. Imports never change the local `pdfRef` either.
- Unexpected command failures no longer send the exception message to the UI, because it could contain file paths or internals. The UI gets a generic message and a correlation id. The details (including the stack) go only to `<data dir>/logs/errors.log`, which rolls over at 1 MB (SPEC Q-02).

### Migration

- Database schema v1 (new). Package format v1 (new).
- **Content schema v2 (ADR-003):** effects are a typed union (`modifier`, `grant`, `resource`, `choice`, `restriction`, `recovery`, `roll`). v1 `abilityScoreIncrease` and `initiativeBonus` map to `modifier` bonuses on read. Unknown effect types are kept byte-for-byte and stay reference-only.
- **Database schema v2:** stored revisions are rewritten in the v2 representation, with new hashes and the original JSON in `legacy_json`. `tomestack.db.v1.bak` is written first. Without this, M0 data folders would have failed to open.
- **Character schema v2:** adds `level` (1–20) and `crossFamilyExceptions`. v1 characters are read as level 1 with no exceptions.
- **Package format v2:** content entries are schema v2. v1 packages still import. Older builds refuse v2 packages with a clear message.
