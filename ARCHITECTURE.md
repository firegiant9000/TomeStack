# Architecture · v0.1

**Status:** proposed technical design, subject to an early desktop packaging spike.

## Boundaries

| Component | Responsibility | Does not own |
| --- | --- | --- |
| Windows shell | Install/update, window, native file dialogs, open PDF page, controlled local host lifetime | Rule calculations |
| React + TypeScript UI | Builder, sheet, editor, import review, trace display | Trusted rule truth |
| .NET application service | Commands, validation, transactions, import/export orchestration | User-facing layout |
| Rules core (.NET library) | Edition policy, formulas, modifiers, choices, dependency graph, derived state and traces | Persistence or Windows APIs |
| Import worker | PDF extraction/OCR adapters, candidate detection, quarantined drafts | Automatic publication |
| SQLite store | Sources, immutable content revisions, character events/state, drafts, PDF index, migrations | Business rules |

**Packaging proposal:** a WPF + WebView2 Windows shell hosting the React UI and a local ASP.NET Core application service, using .NET and SQLite. An early spike must prove offline packaging, process lifetime, installer behavior and local transport. Prefer a private in-process bridge or loopback endpoint with per-launch token and strict origin binding; never expose a service on all interfaces. If the local host adds needless complexity, switch to a WebView2 message bridge behind the same application interface. The domain core must remain UI independent.

## Core entities and identity

```text
Source(id, name, publisher, edition, license, pdfRef?, sha256)
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
3. Compile supported declarative effects into a dependency graph: grant, choice, bonus/set/replace, resource, action, spellcasting, restriction, recovery, roll. Distinguish stacking rules and effect timing explicitly.
4. Evaluate formulas with a typed, bounded expression AST, e.g. `PB + CON.MOD` and `floor(CLASS_LEVEL / 2)`. No `eval`, scripting, file access, network access or recursion from user content. Dice expressions evaluate only on a requested roll.
5. Return `{ value, units, trace[], warnings[], automationStatus }` for each field. Trace entries include effect ID, revision ID, source/page, operation, inputs and resulting value.
6. Apply a labeled user override as the final display layer; preserve the computed value and its trace. A malformed feature is disabled with a diagnostic scoped to that feature.

Separate *calculation* from *commands*: `LongRest` examines recovery rules and generates a preview of proposed state changes. The user confirms the transaction. A roll records inputs/result but does not consume a resource unless the associated action explicitly requests it. This prevents accidental gameplay changes.

## Import lifecycle

`PDF attached → extract page text/layout (+ OCR fallback) → detect entities → propose fields and effects → review edits → validate → publish revision → opt in on characters`

Store page coordinates when extractable; do not make page navigation depend on successful parsing. If OCR/extraction fails, permit manual entry linked to a page. Suggestions are immutable snapshots until user edits them. Confidence is a UI hint, never permission to publish. Book-wide imports run as cancellable, resumable jobs with size/page limits, progress and an audit log. A later optional local model adapter feeds only the proposal stage.

## Persistence, backups and exchange

- SQLite transactions cover creation, leveling, rest, revision publication and import commits. Migrations are numbered and backed up before upgrading a user database.
- Keep PDFs/files outside the database, referenced through managed IDs and content hashes; prohibit archive path traversal. Allow choosing a data directory before large imports.
- Portable package: ZIP with `manifest.json`, JSON schema version, `content/`, `characters/`, `campaigns/`, optional permitted `assets/`; verify hashes and references before commit. Publisher/license metadata travels with content. Third-party PDFs are excluded from sharing by default.
- Export is deterministic enough for human inspection and useful diffs. Document compatibility and round-trip unknown extension fields.
- Backups are local, discoverable and restorable on a clean installation; do not confuse an export with a complete backup when PDF attachments were omitted.

## Editions and licensing

Represent SRD 5.1 and SRD 5.2.1 as different rule-pack IDs, with tested policy differences. Content declares one or both compatible families; universal support must be asserted, not assumed. The published SRD page identifies both Creative Commons routes and notes attribution requirements; review the exact SRD text and CC notice before packaging source packs: https://www.dndbeyond.com/srd . The distributed app should not include books beyond its recorded licenses. Retain provenance and redistribute rights independently of whether local import is possible.

## Early engineering decisions to record

ADR-001 local-only Windows release; ADR-002 edition-aware content IDs and revision pins; ADR-003 declarative effect AST; ADR-004 review-before-publish import; ADR-005 managed PDF attachment versus external links; ADR-006 desktop host/IPC choice after spike; ADR-007 export package and license policy. Record reversals in the decision log, not as silent edits.
