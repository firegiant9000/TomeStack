# ADR-001: Local-only Windows release

Status: accepted for M0–M2. Windows 10 support level is an open owner decision.
Date: 2026-09-25

## Context

SPEC "Users and jobs": one Windows user on one computer, with no account or network needed for normal use. Q-01 requires a clean install to launch offline and keep data in a discoverable local location. Cloud accounts, multiplayer and a VTT have no planned milestone (SPEC "Explicit later scope").

## Decision

- The first releases are a single-user Windows desktop app: a WPF + WebView2 shell with an in-process service (ADR-006).
- There is no account, telemetry, update check or other network call in normal use. The shell refuses any http(s) request outside its virtual host, and the app opens no listening socket (ADR-006).
- All data is local. SQLite plus files live in one data directory (currently `%LOCALAPPDATA%\TomeStack`, which can be overridden; the final policy is D02 / ADR-005). Portability is by export/import package (ADR-007), not sync.
- Windows only. The rules core stays free of Windows references (`RulesCore` invariant), so another shell stays possible later without changing rules code.

## Consequences

- Backup and moving to another machine are the user's job. The app must make them easy and discoverable: packages, pre-import backups (6b), and the backups made before database migrations.
- Updates cannot be pushed. Installer and updater behavior belongs to ADR-008, and any update check would need an explicit opt-in and a revision of this ADR.
- Windows 10: WebView2 and .NET 10 support it, but it is **not tested**. Its support level (required / best-effort / none) is an open owner decision; see ADR-008.

## Alternatives considered

- Web app with a local server: rejected by ADR-006 (ports, tokens, firewall prompts).
- Cross-platform shell (for example Avalonia or Tauri): not evaluated for M0. The rules-core boundary keeps it possible.

## Evidence

- ADR-006 spike and smoke: `blockedRequests: []` on every gate run; no listening socket.
- `PersistenceAndDispatchTests.Saved_character_survives_reopening_the_data_directory`.
