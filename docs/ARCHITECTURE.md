# Architecture · v0.1

**Status:** the design as built through 0.3.0 and M2.1; sections marked as proposals are not built yet. The desktop host and transport are decided by the M0 spike ([ADR-006](decisions/ADR-006-desktop-host-and-ipc.md)); the installer is Velopack ([ADR-008](decisions/ADR-008-installer-and-distribution.md)).

## Boundaries

| Component | Responsibility | Does not own |
| --- | --- | --- |
| Windows shell | Install/update, window, native file dialogs, open PDF page, controlled local host lifetime | Rule calculations |
| React + TypeScript UI | Builder, sheet, editor, import review, trace display | Trusted rule truth |
| .NET application service (in-process in the shell) | Commands, validation, transactions, import/export orchestration | User-facing layout |
| Rules core (.NET library) | Edition policy, formulas, modifiers, choices, dependency graph, derived state and traces | Persistence or Windows APIs |
| Import worker | PDF extraction/OCR adapters (in a child process), quarantined drafts. Candidate detection runs in the app process, bounded by regex timeouts and a text budget (M4) | Automatic publication |
| SQLite store | Sources, immutable content revisions (drafts too), character state (stored, not events), attachments, extracted page text, migrations | Business rules |

**Packaging (ADR-006, 2026-09-24):** a WPF + WebView2 Windows shell hosts the React UI and runs the .NET application service **in-process**, with SQLite for storage. The UI is served from the app folder through a WebView2 virtual host (`https://app.tomestack.localhost/`). It talks to the service over the **WebView2 message bridge** using a transport-neutral JSON command protocol (`CommandDispatcher`). The shipped app opens no listening socket. A development-only loopback host (`src/DevHost`: 127.0.0.1 only, per-launch token, origin allowlist) exposes the same dispatcher for browser development under Vite. The shell refuses any http(s) request outside the app origin. The spike superseded the originally proposed local ASP.NET Core service. The installer is a per-user, self-contained Velopack package (pack id `TomeStack.App`, installed to `%LOCALAPPDATA%\TomeStack.App`, separate from the data folder). Upgrade and uninstall are proven on the development machine, and a clean-VM install of 0.2.2 passed (reported by the owner, 2026-09-28; ADR-008). The domain core remains UI independent.

## Core entities and identity

```text
Source(id, name, publisher, edition, license, attachmentId?)   # pdfRef became an attachment in database v3 (ADR-005)
Attachment(id, sha256?, fileName, mode: managed | linked, linkedPath?)
ContentRevision(contentId, revisionId, kind, rulesFamily, sourceId, pageRef?, data, status)
ContentReference(contentId, revisionId)
Campaign(id, ruleFamily, sourcePolicy, houseRules)
Character(id, campaignId?, ruleFamily, selections[], pins[], state, overrides[])
ImportJob(id, sourceId, pageScope, candidates[], warnings[])
```

IDs are stable UUIDs; display names are never identity. A revision is immutable once published. Content links use an exact revision pin. Character state records *choices* and mutable play state separately from the derived sheet. Each published entity has source provenance and a schema version. A revision graph records replacements and migrations. A character may opt into a newer revision only after a diff and validation. Reference integrity is checked before deletion or export.

## Rules execution

1. Resolve character selections and pinned content revisions within the selected rules family.
2. Validate prerequisites, choices, dependency cycles and compatibility; allow a recorded cross-edition exception where meaningful.
3. Compile supported declarative effects into a dependency graph: grant, choice, bonus/set/replace, resource, action, spellcasting, restriction, recovery, roll. Distinguish stacking rules and effect timing explicitly. The typed effect union, stacking order, timing and formula grammar are defined in [ADR-003](decisions/ADR-003-declarative-effect-ast.md); spellcasting is modeled since content schema v5 (`features/spellcasting.md`); action effects are not modeled yet.
4. Evaluate formulas with a typed, bounded expression AST, e.g. `PB + CON.MOD` and `floor(CLASS_LEVEL / 2)`. No `eval`, scripting, file access, network access or recursion from user content. Dice expressions evaluate only on a requested roll.
5. Return `{ value, units, trace[], warnings[], automationStatus }` for each field. Trace entries include effect ID, revision ID, source/page, operation, inputs and resulting value.
6. Apply a labeled user override as the final display layer; preserve the computed value and its trace. A malformed feature is disabled with a diagnostic scoped to that feature.

**Content graph (M5 slice 2).** Apart from any character, `RulesCore.ContentGraph` holds how content revisions reach each other (grants, choice options, `extendsChoice`) and every way each class reaches each content, by the same rules as step 1: grants only from a root and one level deep, choices from anything reached. It is read-only. The homebrew debugger (`ContentDebugger`, `content.diagnose`) and the relationship view read it; it never takes part in a calculation.

Separate *calculation* from *commands*: `LongRest` examines recovery rules and generates a preview of proposed state changes. The user confirms the transaction. A roll records inputs/result but does not consume a resource unless the associated action explicitly requests it. This prevents accidental gameplay changes.

## Import lifecycle

`PDF attached → extract page text/layout (+ OCR fallback) → detect entities → propose fields and effects → review edits → validate → publish revision → opt in on characters`

Extraction and OCR are decided in [ADR-009](decisions/ADR-009-pdf-extraction-and-ocr.md): PdfPig for text and word boxes, and Windows.Data.Pdf plus Windows.Media.Ocr for pages without a text layer. Both run in a child process (`TomeStack.ImportWorker.Host.exe`) over stdin and stdout, with size, page, time and memory limits. Extracted text stays in the local database and is never exported. Store page coordinates when extractable; do not make page navigation depend on successful parsing. If OCR/extraction fails, permit manual entry linked to a page. Suggestions are immutable snapshots until user edits them. Confidence is a UI hint, never permission to publish. Book-wide imports run as cancellable, resumable jobs with size/page limits, progress and an audit log. A later optional local model adapter feeds only the proposal stage.

## Persistence, backups and exchange

- SQLite transactions cover creation, leveling, rest, revision publication and import commits. Migrations are numbered and backed up before upgrading a user database.
- Character snapshots (M5 slice 8, database v7) are insert-only rows, enforced by triggers. A restore writes its undo snapshot and the restored character in one transaction. Snapshots are local: no package or library backup carries them ([features/snapshots.md](features/snapshots.md)).
- Keep PDFs/files outside the database, referenced through managed IDs and content hashes; prohibit archive path traversal. Allow choosing a data directory before large imports.
- **Data folder (M2.1):** one process per data folder. `TomeStackApp.Open` takes `tomestack.lock` (opened without sharing, released by Windows when the process ends, even after a crash) before the database opens, because start-up interrupts leftover imports and deletes unreferenced attachment files. A second shell launch signals the first to come forward (a session-local named event keyed on a hash of the folder path) and exits with code 3; `scripts/single-instance-check.ps1` proves it with two real processes.
- Portable package: ZIP with `manifest.json`, JSON schema version, `content/`, `characters/`, `campaigns/`, `gaps/` (backups only), optional permitted `assets/`; verify hashes and references before commit. Publisher/license metadata travels with content. Third-party PDFs are excluded from sharing by default.
- Export is deterministic enough for human inspection and useful diffs. Document compatibility and round-trip unknown extension fields.
- Backups are local, discoverable and restorable on a clean installation; do not confuse an export with a complete backup when PDF attachments were omitted. **Built (M2.1):** a character export is labeled as one character without PDFs, and "Back up everything" is the complete backup: package format v6 `scope: "library"`, managed PDFs included, streamed to and from a file, restored with preview and a pre-restore database copy ([features/package-format.md](features/package-format.md#full-library-backup-m21)).

## Editions and licensing

Represent SRD 5.1 and SRD 5.2.1 as different rule-pack IDs, with tested policy differences. Content declares one or both compatible families; universal support must be asserted, not assumed. The published SRD page identifies both Creative Commons routes and notes attribution requirements; review the exact SRD text and CC notice before packaging source packs: https://www.dndbeyond.com/srd . The distributed app should not include books beyond its recorded licenses. Retain provenance and redistribute rights independently of whether local import is possible.

## Early engineering decisions to record

ADR-001 local-only Windows release; ADR-002 edition-aware content IDs and revision pins; ADR-003 declarative effect AST; ADR-004 review-before-publish import; ADR-005 managed PDF attachment versus external links; ADR-006 desktop host/IPC choice after spike; ADR-007 export package and license policy. Later: ADR-008 installer, ADR-009 PDF extraction and OCR; for M5 and M6 (2026-09-28): ADR-010 custom classes and progression (accepted), ADR-011 extension API and ADR-012 export adapters (both accepted 2026-09-29: declarative extensions only, and Foundry `dnd5e` plus a neutral JSON). Record reversals in the decision log, not as silent edits.
