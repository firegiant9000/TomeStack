# Export adapters (ADR-012)

BACKLOG B20 · ROADMAP M6 slice 4 · [ADR-012](../decisions/ADR-012-export-adapters.md) (accepted) · status: **implemented and fixture-verified (M6 slice 4, 2026-09-29; merged to `main` 2026-09-30 via #46); the Foundry adapter is labelled Experimental** until the owner check below is recorded.

"Export for a virtual tabletop" on the character sheet writes one file that you import into another tool by hand. Nothing is uploaded, no network call is made, and the shipped app still opens no socket (ADR-001, ADR-006). Both adapters are first-party code in `src/AppService/Exports/` that read only the **sheet export model v1** ([extensions.md](extensions.md#the-sheet-export-model-v1)), filtered by purpose ([ADR-007 item 11](../decisions/ADR-007-export-package-and-license-policy.md)).

## Targets

| Target | File | Adapter | Verified against |
| --- | --- | --- | --- |
| Foundry VTT, dnd5e system (`foundry-dnd5e`) | `<name>.foundry-dnd5e.json`: one Actor document for "Import Data" on an actor | 1.0.0 | **Foundry core 14.367 with dnd5e 6.0.5** (the pinned pair; dnd5e 6.0.5, released 2026-09-22, declares compatibility minimum 14.367, verified 14) |
| TomeStack sheet (`sheet-json`) | `<name>.tomestack-sheet.json`: the sheet export model v1 itself | 1.0.0 | `docs/schemas/sheet-export.v1.schema.json` |
| Roll20 | none | none | **Deferred** (owner decision D14): no documented, supported import file for its official 5e sheets. The neutral JSON is what any tool can read |

**How the pin was verified (2026-09-29).** The dnd5e data models at tag `release-6.0.5` of `github.com/foundryvtt/dnd5e`: `module/data/actor/character.mjs`, `templates/common.mjs`, `creature.mjs` and `attributes.mjs`; the item models `class.mjs`, `subclass.mjs`, `feat.mjs`, `spell.mjs` and `weapon.mjs`; `templates/item-description.mjs` and `activities.mjs`; `shared/damage-field.mjs` and `uses-field.mjs`; and `system.json` for the compatibility pair. Each data model has a `_migrateData` step that Foundry runs when it builds a document from its source, and `system.json` names 6.0.0 as `needsMigrationVersion`: a document stamped with an older system version is migrated on load, so one pin stays importable while the system keeps those migrations. The file is stamped `_stats.systemVersion` 6.0.5 and `coreVersion` 14.367.

## What maps to Foundry, and how

| TomeStack (sheet export model) | Foundry dnd5e 6.0.5 |
| --- | --- |
| Ability scores; saving-throw proficiency | `system.abilities.<str…cha>.value`; `.proficient` 0 or 1 (no expertise level for saves) |
| Skills: proficiency none, proficient, expertise | `system.skills.<acr…sur>.value` 0, 1 or 2 (Foundry's 0.5, half proficiency, is never written: TomeStack does not model it), with `ability` |
| Armor Class | `system.attributes.ac.flat` and `.override` (TomeStack's value; Foundry's armor formulas are not used, `calcs` is empty) |
| Hit points: maximum, current, temporary | `system.attributes.hp.max` (the character override field), `.value`, `.temp` |
| Exhaustion | `system.attributes.exhaustion` |
| The first caster's ability (each spell also carries its own) | `system.attributes.spellcasting` |
| Spell slots and Pact Magic, spent | `system.spells.spell1…spell9` and `.pact`: `override` = maximum, `value` = remaining |
| Classes: name, levels, hit die, hit dice spent | `class` items: `identifier` (unique in the actor: a repeated one gets `-2`, `-3`), `levels`, `hd.denomination` (`d6`…`d12`), `hd.spent` |
| Subclass | `subclass` item with `identifier` and `classIdentifier` |
| Features, feats, species and background traits | `feat` items with `description.value` (the summary and effect texts, HTML-escaped); a resource the feature defines becomes `uses.max`, `uses.spent` and `uses.recovery`: period `lr` or `sr`, type `recoverAll` for "all" or `formula` for a whole number |
| Known and prepared spells | `spell` items: `level`, `school` (`abj`…`trs`), `prepared` (cantrips and prepared spells 1), `ability` (the caster's), `method` (`pact` for Pact Magic spells above cantrips, otherwise `spell`), description |
| Attacks (equipped weapons) | `weapon` items: `equipped`, `proficient`, `damage.base` (`number`, `denomination`, `types`) when the damage starts with dice; the to-hit and damage TomeStack calculated are in the description |
| Notices (every contributing source) | `system.details.biography.value`: title, publisher, license, attribution and modification notice for each, "only totals" where content was left out, the personal-copy label, and the non-affiliation statement |
| Provenance | `flags.tomestack` on the actor (adapter version, `exportedAt`, purpose, source titles) and on each item (content and revision ids only) |

Keys are sorted and there is no timestamp but `flags.tomestack.exportedAt`, so the same character gives the same file.

**The preview lists where Foundry may differ**, because Foundry recalculates with its own numbers: its proficiency bonus from the class levels in the file (`floor((level + 7) / 4)`, so +1 with none) and each modifier from the score. It lists the proficiency bonus when it differs, and any skill, save (Foundry adds proficiency once, even for Expertise), initiative, or spell attack and save DC whose TomeStack value is not what those give. It also lists what the file cannot carry (below). A share export also notes that its totals include content that was left out.

## What is lost

- Traces, override reasons, and TomeStack's automation: effects become numbers and text.
- Toggles and their state, variable costs, and shared resources beyond one item's uses.
- Choice history and revision pins, apart from the provenance flag.
- Conditions other than exhaustion.
- Equipment that is not an equipped weapon (armor is in Armor Class; other items are not written), scales (in the neutral JSON only), and resources that belong to no listed feature.
- Anything reference-only arrives as text.
- The Pact Magic slot level: Foundry works it out from class spellcasting data the file does not carry, so it shows the slots as level 1 (the preview says so when TomeStack's level is higher).
- A recovery that is neither "all" nor a whole number (a formula over TomeStack values): Foundry does not restore it (named in the preview).
- A class with no accepted hit die: Foundry gives it d8 hit dice, none spent (named in the preview).
- Foundry enrichers: an `@` or `[[` in exported text gets a word joiner (U+2060) after it, so `@UUID[…]`, `@Embed[…]` and `[[/r …]]` stay text and never become links, embeds or roll buttons in a world.
- Gap notes and PDF links: **never** exported.

## Licensing: an export is a share by default

`purpose: "share"` (the default) keeps only totals from content that may not be shared: its features, resources, attacks, spells, toggles and scales are dropped whole and counted by source in the preview. `purpose: "personal"` also lets out your own homebrew (made here and not import-derived) and is labelled "Personal copy: includes your own homebrew. Do not share it." Other publishers' non-shareable content is filtered in both. The rule is ADR-007 item 11.

## Validation and privacy

1. **A validator per adapter**, run before anything is written (`export.invalid-output`): `FoundryDnd5e.Validate` checks the emitted subset (the pinned `_stats`, the six abilities, skill levels, hit points, Armor Class, spell-slot keys, item types, class levels and unique class identifiers, spell levels and methods, and recoveries); `SheetJson.Validate` checks the model's format, version and lists. The tests also check every golden file against `docs/schemas/export-foundry-dnd5e.6.0.5.schema.json` and `sheet-export.v1.schema.json`.
2. **Golden files** (`tests/AppService.Tests/Golden/`): original fixture characters only (one per family, a multiclass caster, a weapon user in armor, the Test Chronicler), so no SRD or third-party text is in the repository (SPEC Q-03). Rewrite them after a deliberate change with `TOMESTACK_WRITE_GOLDEN=<that folder> dotnet test --filter ExportAdapterTests`.
3. **Privacy:** the model is an allowlist, and the output scan runs on every file (the data folder, the profile, every linked PDF, and the Windows user name as a path segment; [extensions.md](extensions.md#running-a-hook)). The Foundry file's HTML-encoded text writes letters like é and ' as entities, so for that target the unencoded sheet it came from is scanned too. A test runs the adapters in a data folder named after a sentinel user with a linked PDF and a gap note and asserts that no path, attachment id or gap note reaches either file.
4. **Owner check (not automatable):** import each golden Foundry file into a real Foundry 14 world with dnd5e 6.0.5 and compare it with the sheet. Until that is recorded here, the Foundry adapter's evidence level is fixture-verified and the UI labels it Experimental.

| Date | Foundry / dnd5e | Files | Result |
| --- | --- | --- | --- |
| (not yet) | | | |

## Commands

`export.preview { characterId, target, purpose }` returns the file name, size, adapter and target versions, what was left out, the notices, the Foundry differences and warnings, and a one-use token; nothing is written. `export.saveAs { token }` writes it through the native Save dialog (a cancelled dialog keeps the token); `export.download { token }` returns it as base64 in browser development.

## Tests

`tests/AppService.Tests/ExportAdapterTests.cs`: the golden files for both targets, deterministic and schema-valid, with no SRD text; the Foundry mapping against the sheet (abilities, Armor Class, hit points, skills, classes, spells, provenance, the biography notice); the share and personal filters and the Foundry differences; the sentinel privacy test; the validators refusing what the target would not read; the dispatcher commands. e2e: "exports a character for Foundry VTT and as sheet JSON after a preview, with no local path in either file".

**Not verified:** an import into a real Foundry world (owner check above), the native Save dialog for exports, and a Narrator pass (accessibility item 27).
