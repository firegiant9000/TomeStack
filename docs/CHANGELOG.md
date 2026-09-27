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

### Changed

- D06 resolved: the application service runs in-process behind the WebView2 message bridge instead of as a local ASP.NET Core service. The loopback host is development-only (ADR-006).
- Spec documents moved into `docs/`, and the diagram into `docs/diagrams/`.

### Fixed

- The shell showed an unhandled exception when its UI bundle was missing. It now shows an error and exits cleanly (found by the spike's negative control).

### Migration

- Database schema v1 (new). Package format v1 (new).
