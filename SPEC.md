# Product specification · v0.1

**State:** proposed baseline · **Updated:** 2026-09-24 · **Change policy:** [LIVING_SPECS.md](LIVING_SPECS.md)

## Users and jobs

The primary user is a fifth-edition player or DM who wants a reliable local character sheet and a practical way to use third-party books and personal homebrew. They can build a character, see how rules affect it, run a session, update homebrew, and export the result. The initial target is one Windows user on one computer, with no account or network required for normal use.

## Scope and behavior

### Sources and provenance

- **S-01** A source record stores title, creator/publisher, rules compatibility, edition/version, license/redistribution status, import date, file hash, and optional local PDF reference. Every entity keeps source ID and page or page range when known.
- **S-02** SRD 5.1 and SRD 5.2.1 are separate source packs. A character chooses a primary rule family. The picker shows the source and compatibility of every option, including user content.
- **S-03** Each character/campaign may enable sources. Duplicate names are displayed with source and rules family. An explicit selection or override resolves conflicts; the app does not merge mechanics by name.
- **S-04** Linked PDFs are stored in an application-managed local library by default, with checksum, original filename and source-page navigation. The user may remove the attachment without deleting accepted structured content, subject to a confirmation describing what breaks.

### Import and authoring

- **I-01** A user can import a whole PDF or a page range/selection. Text extraction, OCR if needed, and content detection produce **draft candidates**, never active rules.
- **I-02** A candidate shows original excerpt/page, proposed entity, extracted fields, mechanical effects, confidence/uncertainties and unresolved references. Each candidate can be edited, accepted or ignored; accepting runs schema and reference validation.
- **I-03** The first release may import PDFs as searchable/reference sources and manually author structured entries. Automated interpretation expands later. Source navigation works even for reference-only material.
- **I-04** The homebrew studio can create/edit subclasses, features, species, backgrounds, feats, spells, items and other supported entities through guided controls. Advanced formulas are optional; no arbitrary imported script executes.
- **I-05** Feature automation is marked `automatic`, `assisted` or `reference`. Assisted actions track eligible resources/rolls but require a player choice. Unhandled mechanics retain their full text.
- **I-06** Drafts can be previewed; publishing creates an immutable content revision. Existing characters remain pinned until an explicit update with a diff and migration review. Authors can see affected characters and dependency errors.
- **I-07** Optional local AI may propose interpretations or design feedback in a later milestone; every suggestion must cite its local excerpt and require acceptance. Turning AI off preserves core workflows.

### Building and playing a character

- **C-01** The builder chooses primary rules, enabled sources, species, background, class and subclass, abilities, proficiencies/choices, spells and equipment. It validates level-specific choices. Both rule families work from the first release; differences are encoded explicitly, not inferred from names.
- **C-02** Levels, multiclass prerequisites/proficiencies, HP, proficiency bonus, saves, skills, AC, initiative, movement, attacks, damage, spell attacks/DCs/slots and class resources evaluate from structured rules where modeled.
- **C-03** Every derived number exposes a trace of base value, applied rules, source, ordering and override. Invalid/conflicting modifiers yield a visible explanation and isolate the affected calculation.
- **C-04** The sheet groups actions by action/bonus action/reaction/other; supports attack, damage, skill and save rolls, advantage/disadvantage and critical damage. Rolls show formula, component dice, modifiers and provenance.
- **C-05** HP, temporary HP, death saves, spell slots, resources, conditions, inspiration and inventory are editable. Short/long rests propose deterministic recovery, show the pending changes and let the user confirm or adjust context-dependent outcomes.
- **C-06** A user can override a calculated field with value and optional reason. The sheet labels the override and shows the underlying result. Overrides survive recalculation until removed or invalidated by a reviewed migration.
- **C-07** Character creation/level-up are recoverable drafts; the user can cancel without partial application. Backups and export/import are available offline.

### Campaigns, portability and presentation

- **P-01** A local campaign groups characters, allowed sources, rule family, house rules and homebrew; an incompatible selection warns and allows a deliberate exception.
- **P-02** Exports use a documented, versioned JSON manifest inside a portable archive; include selected content revisions, character state and any permitted assets. The importer previews dependencies and conflicts before applying. Do not bundle third-party PDFs by default.
- **P-03** The digital sheet is keyboard accessible and usable at common desktop sizes. Printable and condensed PDF sheets are planned after the first release; no mimicry of D&D Beyond trade dress.
- **P-04** Search covers structured content from enabled sources; later phases add full PDF text, facets, tags and commands.
- **P-05** A future extension API exposes versioned, permissioned data/import/export hooks; it does not grant raw code execution to content packs. Plugins are a later design and implementation milestone.

## Quality, privacy and legal gates

- **Q-01** A clean Windows installation launches offline, keeps data in a user-selected or discoverable local location, backs it up, and can restore from export.
- **Q-02** Imported documents, archives, formulas and images are untrusted: restrict paths, sizes and decompression; parse formulas with a bounded grammar; isolate parse failures. Never evaluate JavaScript from homebrew.
- **Q-03** All distributed rules text, icons and fonts have recorded rights and attributions. Ship only licensed material (e.g. selected Creative Commons SRD content after attribution review). Do not ship user-imported third-party books.
- **Q-04** Calculations and edition differences have rule fixture tests; import has malformed-file tests; backups have restore tests; interactive flows receive manual accessibility checks.

## Explicit later scope

Full custom base classes, large-book mechanics recognition, homebrew monsters/DM tools, full-text PDF search, printable cards, design feedback and plugins follow the first usable release. Cloud accounts, multiplayer and a VTT have no planned milestone.

## Open behavioral decisions

These are tracked in [LIVING_SPECS.md](LIVING_SPECS.md): exact rest automation by rule/context; whether linked PDF is copied or linked by default; inclusion of character-scoped source copies in exported packs; release-level scope of multiclassing and spellcasting; exact accessibility target. Current defaults above are proposals for testing, not silently settled user preferences.
