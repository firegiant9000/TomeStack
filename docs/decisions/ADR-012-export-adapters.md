# ADR-012: Export adapters for virtual tabletops (B20)

Status: **accepted (owner, 2026-09-29): the recommended targets** (LIVING_SPECS D14). Foundry VTT `dnd5e` (the core and system pair is pinned at slice start and verified against that release's data models), the neutral sheet-export JSON, and Roll20 deferred. `purpose: "personal"` goes only as far as your own homebrew that is not import-derived. Nothing is built yet (M6 slice 4). ADR-007 is amended for the "totals only" share rule before that slice. Proposed 2026-09-28.
Date: 2026-09-28

## Context

- **BACKLOG B20** "Foundry/Roll20 export adapters", with the acceptance seed "Document supported scope and validate generated file". ROADMAP M6 "export adapters".
- **Constraints:**
  - ADR-007: non-redistributable content never leaves the machine in a share, and attribution travels with SRD content.
  - SPEC Q-03 and D09: no Wizards of the Coast trademarks in branding.
  - ADR-001: no network, so an adapter writes a file and never uploads anything.
  - The UI calls `client.ts` only.
- **What exists (checked against the code 2026-09-28):** no export to any other format. The computed `CharacterSheet` (`RulesCore/Calculation.cs`) holds fields, hit points, hit dice, resources, features, spellcasting, slots, attacks and toggles. `package.saveAs` shows the pattern for a native Save dialog that keeps paths out of the page.

## Targets (owner decision)

| Target | What an import file is | Assessment (2026-09-28) |
| --- | --- | --- |
| **Foundry VTT, `dnd5e` system** | One Actor document as JSON, imported with "Import Data" on an actor. The system defines its data models in its public repository (`foundryvtt/dnd5e`), which is on the 5.x to 6.0 line as of 2026-09 | **Recommended.** It is a documented, versioned data model, and a file the user imports by hand. **The pin is a pair:** a Foundry core generation and the `dnd5e` release made for it, because each system release targets one core generation. Both go into the document's `_stats` values. The pair is chosen at slice start and verified against that release's data models. **To verify then:** whether Foundry migrates a document from an older system version on import. If it does, one pin stays useful for longer |
| **Roll20** | No documented, supported import file for its official 5e sheets was found in a web search on 2026-09-28 (the Roll20 wiki pages "Character Sheets/Import" and "Import", and forum threads on JSON import for the 2024 sheets). Character import goes through Pro-tier API scripts, importers built into some sheets, or browser userscripts. Check again at slice start | **Recommended: defer.** The alternatives are (i) a documented JSON plus a sample Roll20 API script that the user runs in their own Pro game (TomeStack would maintain third-party-runtime JavaScript it never runs), or (ii) a text summary for manual entry, which option C covers |
| **Neutral JSON** (the ADR-011 sheet export model v1) | TomeStack's own documented JSON | **Recommended, and needed anyway.** It is the input of every adapter and of extensions. Any tool can read it |

## Decision (accepted 2026-09-29, for the recommended targets)

### Where it lives

`AppService/Exports/`, pure `net10.0`. An adapter is a function from the sheet export model v1 (ADR-011) to bytes. **So the sheet export model, with its purpose filter and `notices[]`, must be accepted before this slice starts, even if the rest of ADR-011 (the execution model) is still open (review fix).** It is first-party code; no third-party code runs. `RulesCore` does not change.

The commands are `export.preview { characterId, target, purpose }` and `export.saveAs`, a native Save dialog as with `package.saveAs`. The file names end in `.foundry-dnd5e.json` and `.tomestack-sheet.json`.

### What maps to Foundry `dnd5e`, and what is lost

| TomeStack | Foundry | Notes |
| --- | --- | --- |
| Ability scores | ability values | The final scores. The adapter does not reproduce the effects that produced them |
| Class levels, hit die, subclass | class and subclass items with levels and the hit die | A homebrew class becomes a class item with its name, levels and hit die only |
| Hit points (max, current, temporary), hit dice spent | the HP and hit dice fields | The maximum is the value TomeStack calculated, including an override |
| Armor Class | a flat Armor Class value | TomeStack's value, so Foundry's own armor formula is not used |
| Skill proficiency, expertise, half proficiency | skill proficiency levels 0, 0.5, 1 and 2 (0.5 is half proficiency, as the SRD Bard's Jack of All Trades gives) | Foundry recomputes the bonuses. The preview lists every field where Foundry's result may differ from TomeStack's. Verify the levels against the pinned release |
| Saving-throw proficiency | 0 or 1 (no expertise level) | As above |
| Spell slots and Pact Magic, spent slots | the spell slot fields, with a maximum override | |
| Attacks (weapons) | weapon items: attack bonus, damage, properties | |
| Features and resources | feature items with name and text. A resource defined by a feature becomes that item's uses and recovery | |
| Spells known and prepared | spell items: name, level, school, text | |
| Provenance | `flags.tomestack`: content and revision ids and source titles only | |

**Lost, and listed in `docs/features/export-adapters.md`:**

- traces, override reasons, TomeStack's automation (the effects become numbers and text);
- toggles and their state, variable costs, shared resources beyond one item's uses;
- choice history and revision pins, apart from the flag;
- conditions and exhaustion, apart from a documented subset;
- anything reference-only (it arrives as text);
- gap notes and PDF links, which are **never** exported.

### Licensing: an export is a share by default

A VTT file is meant to leave the machine, to a Foundry server that other people use. So:

- **Default `purpose: "share"`:** the sheet export model's share filter (ADR-011) applies.
  - A `redistributable: false` source contributes only **aggregate totals**: ability scores, Armor Class, hit point maximum, save and skill totals, slot counts.
  - Its features, resources and their uses and recovery, attacks, spells and `scales` (ADR-010) are **dropped whole**, not kept without their names, because per-item values are the book's mechanics (ADR-007 "Alternatives"). The preview lists what was dropped, by source title and count.
  - **This amends ADR-007 and is made there before the slice ships (review fix).** ADR-007 shares omit whole revisions, and the receiver recalculates without them. Here the totals keep the omitted content's effect, because a VTT file has no recalculation that could restore it.
- **Option `purpose: "personal"` (owner decision), narrowed (review fix):** it includes the full text of your **own** homebrew only: sources created locally that are not import-derived (M6 slice 1's durable flag). As built in M6 slice 1: `origin: "local"` and no `importDerived`; a source of unknown origin (stored before database v8) counts only after its author marks it as shareable. Other publishers' `redistributable: false` content is always filtered, even here, because the file's only use is a server that other people read. It is labelled as backups are: "Personal copy: includes your own homebrew. Do not share it."
- **Attribution for every included source, not only the SRD (review fix):** the file carries every `notices[]` entry of the sheet export model (title, publisher, license, attribution, modification notice) in the actor's biography, and the source titles in the flag. That covers the CC-BY-4.0 SRD statements, CC-BY third-party sources and shareable homebrew with an attribution.
- **Trademarks (review fix):**
  - The adapter names its target software nominatively: "Foundry VTT (dnd5e system)".
  - The README and the export UI say that TomeStack is not affiliated with Foundry Gaming LLC, Roll20 or Wizards of the Coast.
  - Whether "dnd5e", the system's own id, may appear in the UI is a nominative-use question. It is recorded under D08 and D09 for the trademark check before public release, not decided here.
- **Never:** local paths, attachment ids, Windows user names, gap notes or extracted PDF text. A test scans every golden file for them.

### How a generated file is validated

1. **A JSON Schema that TomeStack writes for exactly the subset it emits,** pinned to the target version (`docs/schemas/export-foundry-dnd5e.<system version>.schema.json`). The exporter validates before it writes and refuses on failure (`export.invalid-output`). The sheet export model has `sheet-export.v1.schema.json`.
2. **Golden files** for original fixture characters: one per family, a multiclass caster, a Fighter in armor, and the Test Chronicler once M5 slice 1 lands. The output is deterministic (sorted keys, no timestamps other than one `exportedAt`).
3. **Privacy:** the sheet export model is an allowlist with no path fields, which is the primary control. On top of that runs the ADR-011 output scan (normalized, and covering the data folder, the profile, the user name and linked-PDF paths). Tests run the adapter with a data folder and a linked PDF under a sentinel user name and assert that the sentinel never appears. Also: no attachment ids, no gap-note text, and nothing from filtered sources in a share.
4. **Owner check (not automatable):** import each golden file into a real Foundry world of the pinned version and compare it with the sheet. Foundry is licensed software that CI does not have. Until this check is recorded, the adapter's evidence level is **fixture-verified**, and the UI labels it "Experimental".

### Versioning

- Each adapter has its own `adapterVersion` and the target version it was verified against. The preview shows them.
- Supporting a new target version is a new schema file, new golden files and a new owner check. The old one is kept while the target still imports it.
- The sheet export model is versioned with the extension API (ADR-011).

## Consequences

- One first-party adapter to maintain against a third-party data model that changes with its major versions. The pin and the owner check contain that.
- Roll20 users get the neutral JSON and nothing more until a supported import path appears or the owner picks option (i).
- A share export can make a character look incomplete in Foundry (omitted features). That is intended, as for ADR-007 shares, and the preview says so.

## Alternatives considered

- **Export through an extension (ADR-011, option A):** a declarative mapping cannot express Foundry's item structure well, and a first-party adapter is simpler to test. The neutral JSON lets extensions build their own.
- **Writing into a Foundry world's database directly, or through its API:** it needs file paths into another app's data, or a network call. Rejected by ADR-001.
- **Roll20 via a userscript:** a browser-extension runtime that TomeStack does not control. Rejected.

## Evidence (planned; none yet)

`FoundryExportTests` (the schema, golden files, the privacy scan, share and personal), `SheetExportModelTests` (the schema, no paths), and an e2e step that exports a character and checks the preview. An owner check for each target version, recorded in `docs/features/export-adapters.md`.

Supersedes: none. Applies ADR-007 to a new kind of output.
