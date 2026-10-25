# Homebrew Import Lab

LIVING_SPECS D60, D62 · ROADMAP "Homebrew Import Lab" · SPEC I-01, I-02, I-03 · ADR-004, ADR-009 · status: **Planned (D62); the requirements behind ROADMAP's Homebrew Import Lab entry.**

Provenance: this is the owner's 2026-10-18 Lab spec, name-scrubbed for the public repository and narrowed by D62 (owner, 2026-10-20). It keeps the requirements and drops everything that only made sense with one paid sourcebook. Nothing here is built yet. Each requirement is marked **Now** (D62 builds it) or **Later** (D62 defers it).

## Goal

Take material the user legitimately owns, extract structured entities and mechanics, show what TomeStack understands and what it does not, let a human review and correct it, test the result on sample characters, and publish it as a local, versioned source.

Not a goal: shipping third-party rules text, building a complete table simulator, applying arbitrary natural-language rules without review, or needing AI or a network.

Success looks like this:

- A local PDF or Markdown file is read and indexed without leaving the device.
- A reviewer picks a class, sees the proposal next to the source page, corrects it, tests it and publishes it.
- Every mechanic is **automatic**, **assisted** or **reference-only**, with a reason. Nothing unsupported is silently marked automatic.
- Sharing never carries import-derived content. A personal backup keeps the user's own copy.
- Existing SRD content, packages, backups and offline use are unchanged.

## Rules that never change

- Imported content never executes code and never becomes active without review.
- Import-derived sources are never shareable. Extracted text stays local.
- Fixtures are original. Text from a third-party book never enters the repository, the logs, the issues or a diagnostic export.
- No AI and no network use. Any AI import is a later, separate, optional and consented feature. RulesCore stays free of any language model.
- Published revisions are insert-only. A re-import makes a new revision for review.
- The two rules families stay separate. A candidate is written for one or both families, chosen by the reviewer.

## Three statuses

"Parsed", "human reviewed" and "playable" are three different statuses. Parsing does not imply review, and review does not imply that TomeStack can automate the mechanics. The coverage report shows the three separately.

## What D62 builds, and what it defers

| Area | D62 |
| --- | --- |
| Capability registry and coverage report | **Now** |
| Class progression projection with a read-only studio grid | **Now** |
| Library-scale pass (indexed per-source queries, candidate paging, entry-cap review, benchmark) | **Now** |
| Per-effect source span in candidate review | **Now** |
| Package preflight (provenance, notices, redistributable flags; import-derived content blocked) | **Now** |
| Duplicate detection that lists, never merges | **Now** |
| Deterministic Markdown importer that feeds the existing candidate detector | **Now** |
| Mechanics recipes (after D43) | Later |
| Source dependency graph | Later |
| Batch review and version diff | Later |
| Edition lineage between a book and its later revision | Later |
| Importers for other third-party formats | Later |
| Scenario simulator | Later |
| AI-assisted import | Later, separate, consented |

The gate (D60, unchanged): a synthetic 20-level custom class is imported from Markdown or PDF into a reviewed class, its unsupported mechanics are shown, and published content stays immutable. Evidence that counts beyond the feature: a precision/recall table on the SRD and on one privately licensed third-party PDF, and an FsCheck property that import, publish and export round-trips.

## Functional scope

### IL-01: Source onboarding and access

- **Now:** local PDF, Markdown and user-entered text. Never claim a format is supported before it is.
- **Later:** JSON and other structured third-party formats, added one adapter at a time.
- Each source records provenance: title, publisher or author, edition, revision, owned or permitted status, file hash, page range, original file name, an import-derived flag, licensing metadata, and whether it may leave the device.
- The attached file is never changed. Derived data refers to the file hash and page identifiers. Files with the same content hash are deduplicated.
- Offline only, by default and always in this milestone.
- Failures are announced in words: encrypted PDF, scan-only page, mixed columns, missing pages, unreadable table, oversize file, broken reference.
- **Later:** several files for one logical book, such as a book and its later revision, as separate versions that are never merged automatically (edition lineage).

### IL-02: Document structure and entity detection

- Read tables of contents, headers and footers, sections, tables and column order, then find named features, level tables, class and subclass features, spells, items, feats, equipment traits, companion stat blocks and appendices.
- Confidence is per field, not per page. Each field records its extraction method and source span.
- Discovery (there may be an entity), interpretation (it means X) and execution (TomeStack handles X) stay separate steps.
- Names alone are never identity keys. Two entries with the same name keep distinct source ids.
- **Now:** the existing PdfPig and OCR worker, and the existing candidate detector, with a per-effect source span shown in review.
- **Later:** a dependency graph across class, subclass, feature, spell, item and companion.

### IL-03: Staging representation

The staging form is versioned, inert and non-executable. It records the entity kind, the source version, where each part came from (file hash, page, span), the progression, and the proposed mechanics with their evidence, confidence and an automation proposal. Unknown types stay inert. Nothing in it can be a script, a remote reference or an unbounded expression; only the vetted, bounded formulas the content schema already allows.

**Now:** the existing candidate record plus a per-effect span. Any stored-format or database change is listed for owner approval in that task's plan (D62).

### IL-04: Capability registry and coverage

- **Now:** a versioned registry that maps an effect type and field to its default automation, the reason, and the fixture evidence for it.
- **Now:** a coverage report per source, class and feature: entities found, mechanics interpreted, mechanics automatic, assisted, reference-only and unsupported. It also drives the missing-mechanic warnings.
- Similar wording is not the same rule. Before reuse, check the trigger, scope, duration, stacking, cost, target, timing and rules family.
- Never report an invented precision or recall. Only measured numbers appear.
- Missing capabilities are ranked by how many authorized fixtures they block, not by how often a word appears.
- **Later:** mechanics recipes: reusable patterns such as spend a pool, add an attack option, change a spell list, advance a form, summon a companion.

### IL-05: Human review and publication

- Show the source page beside the proposed structure, with the evidence highlighted. Every edit keeps provenance.
- Review actions: approve, edit, map to a supported pattern, mark assisted, mark reference-only, defer, dismiss, resolve a linked entity.
- Publish only after schema, graph and rights checks and an explicit confirmation. A failed publish leaves the source and every character unchanged.
- Import is idempotent: the same source hash and the same entity mapping never create a duplicate class.
- Active characters are never changed silently. A new revision is a reviewed, opt-in migration with a restorable backup.
- **Now:** duplicate detection that lists duplicates for review and never merges them.
- **Later:** batch operations for identical patterns, and a diff between two versions of a source.

### IL-06: Other importers

- **Now:** Markdown only, deterministic, feeding the existing detector.
- **Later:** other third-party formats, each per-version, deterministic, safe and tested alone.
- Never scrape paid entitlements, and never decrypt or redistribute protected modules. Another tool's public documentation is a design reference, not permission to import.

## Engine needs the Lab will surface

The Lab reports these as unsupported or assisted; building them is play-mechanics work (D43, ADR-018) and later waves, not the Lab. Each needs a typed, declarative effect or an honest "assisted" fallback.

| Need | Examples of what it covers |
| --- | --- |
| Timed, exclusive and overlay states | Forms that replace each other, upkeep, expiry, rollback |
| Triggers and events | On hit, on miss, start or end of turn, failed save, hit points at a threshold |
| Action definitions | Action, bonus action, reaction, an attack replaced by another action, limited free uses |
| Costs and consequences | Hit points, hit dice, points, charges, partial payment, refunds |
| Allied creatures | A companion, cohort or summon with its own hit points, attacks, resources and rules for who controls it |
| Replacement and conditional choices | Swap a daily power, replace a pick at level-up, a temporary feature package |
| Targeting and typed outcomes | Cones, lines, radius, several targets, forced movement, conditions |
| Prepare, charge, release | Multi-turn actions that can be interrupted or queued |
| Equipment mods and crafting | Attachments, switch weapons, ammunition and reload, item upgrades, a crafting recipe |
| Other power systems | Casting from points or from hit points, powers that are not spells |
| Context and marks | Target type, a marked creature, distance, nearby allies, while equipped or charged |
| Deterministic composition | Provenance, conflict and order rules, atomic state changes, explainable results |

Combat rules the engine must keep: no effect chain without a bounded transition budget and cycle detection; a confirmed action that changes several things commits all of them or none; reactions cannot trigger themselves unless a rule says so; events from the physical table are assisted and confirmed, never inferred from dice logs.

## Fixtures and licensing

- Public fixtures are original and synthetic: small classes written for the tests, with a progression, a pool, a timed toggle and an unsupported trigger.
- A third-party book may be checked privately. The owner supplies a legitimately owned PDF; the run records counts only, under `tests/RulesFixtures/local/`, which is gitignored. No book text goes into tests, logs, issues or screenshots.
- A third-party class is a candidate until its literal rules are checked against a lawful copy. Publisher summaries are not a verification.
- Reverify any publisher's content policy before shipping a feature that touches that publisher's material.
- A source that holds import-derived content is marked non-shareable by default. A share preview says "licensed source not included in shared export" instead of silently dropping a dependency.
- Do not send passages of a paid book to an external model without informed consent and a rights review. This milestone has no AI.

## UX flow

1. **Add source:** pick a file; set edition, title, attribution, and the privacy and sharing status.
2. **Read:** find the table of contents and the content kinds; progress, cancel, resume and diagnostics.
3. **Inventory:** entries grouped by kind, with what was found against what the contents list promised, and possible duplicates.
4. **Parse a class:** level table, feature references, spellcasting, resources, subclass choice, unresolved links, page citations.
5. **Map mechanics:** the recognised pattern, the target effect, the automation grade, the evidence and any conflict.
6. **Test:** make characters at levels 1, 5, 11, 16 and 20 on an unsaved copy; see unlocks, stats, resources and warnings.
7. **Review and publish:** licence warnings, unsupported mechanics, the source's revisions; commit as a local published source.
8. **Update review:** compare a new file hash and the characters it affects. **Later.**

**Now** covers steps 1 to 7 as far as the existing source, import, candidate review and studio screens reach, plus the read-only progression grid and the coverage view. The rest builds on them; it does not add a second parallel app.

Must-have editor behavior:

- A class table grid, 1 to 20, with missing levels highlighted. **Now:** read-only. **Later:** editing and pasting.
- Low-confidence fields first, with the source page always one press away.
- A coverage view: found, unresolved, automatic, assisted, reference-only, and when it was last checked.
- A deterministic test report from a sandbox that saves nothing.
- Cancel and draft recovery for every import and edit, with no change to active characters.
- Keyboard use, labelled fields, managed focus, statuses that do not rely on color, and reduced motion.
- **Later:** a gallery of effect recipes, and learning corrections as local templates (never training on third-party text).

## Acceptance scenarios

- **AT-01 Integrity:** importing the same synthetic source twice makes no duplicate; a change makes a reviewed revision; a cancelled job publishes nothing.
- **AT-02 Rules family:** a candidate for one family cannot attach silently to a class of the other.
- **AT-03 Coverage:** every mechanic of a synthetic 20-level class is listed as automatic, assisted or reference-only; none is unclassified.
- **AT-04 Reading order:** a synthetic two-column PDF yields its sections in the right order; low-confidence fields are flagged; unsupported timing stays assisted or reference-only.
- **AT-05 Names:** two entries with the same name keep distinct source ids and character references.
- **AT-06 Licence isolation:** a share export excludes import-derived content and attached PDFs; a personal backup keeps them; no source text reaches a log.
- **AT-07 Fail safe:** a corrupt or oversize file, a circular reference, an invalid formula, a missing dependency or an unsupported schema version fails with a clear message and changes no character.
- **AT-08 Round trip:** import, publish and export round-trips (FsCheck property).
- **AT-09 Regressions:** all SRD fixtures, migrations, packages, calculation traces and the desktop smoke still pass.
- **AT-10 Duplicates:** two near-identical entries are listed for review and are never merged.
- **AT-11 Benchmark:** the library-scale pass is measured at 1000 features and 1000 candidates, and the baseline is recorded before any budget is set.

Scenarios about forms, counters, hit-point payment, companions, crafted items and choice replacement belong to the play-mechanics work (D43), not the Lab.

## Performance, security, accessibility

- Offline by default, with the shell's existing network restrictions. No crawling, no automatic outside requests.
- Bound the parser's time, memory, page count, decompression and graph size. Treat all source content as untrusted; sanitize HTML and Markdown.
- The import worker gets no blanket file or network access. Only the bounded, deterministic formulas run in the core.
- Source files, raw extracts and personal notes never appear in telemetry, error text, fixtures or issue attachments.
- Diagnostics carry counts, error codes, a file fingerprint, a schema version and synthetic reproduction steps. A raw excerpt export is opt-in and labelled sensitive.
- Progress is resumable and cancellable. Staging is written in transactions. Unchanged pages are not read again.
- Benchmarks: 100, 500 and 1000 candidate inventories, and a full personal restore. Set numeric budgets after recording a baseline.
- Keyboard-only use, screen-reader announcements, table headings, signals that do not rely on color.

## Open questions

Log the answers; do not guess.

1. Should an optional import assistant ever run locally, or stay out of scope? (D62: out of scope for now.)
2. Is third-party compatibility a private-import goal only, or also a publisher-permission goal? The answer changes storage and sharing rules.
3. Which third-party class matters most in the owner's campaign? Real blockers rank above breadth.
4. What does a campaign allow for optional rules in otherwise strict 2014 play?
5. What must be true to call a converted class "playable", "fully automated" or "accepted in real play"? Keep owner-verified separate from automated fixture status.
6. Which power systems need a new typed effect, and which fit an existing pool with an assisted action?
7. How should an optional offline tracker coexist with paper or virtual-table play without double-applying damage? (Tied to the later DM-tools note, D63.)
