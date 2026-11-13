# ADR-024: Content schema v13, creation guides and packs (the engine; no bundled content yet)

Status: **accepted** and built on 2026-11-12 (owner, 2026-11-11: the wave 3 design, spec 3.6 and Q3, Q3a, Q3b, Q3c; LIVING_SPECS D97 and D98). This change is the engine only: the schema, validation, down-conversion, kit expansion and a catalogue lookup. No bundled guide or gear ships in it.
Date: 2026-11-12

## Context

A new character should start with the class's and background's starting equipment and a hint about which abilities matter most. Until now the builder had no data for either, so a player typed gear in by hand. The owner approved storing both as content: a "creation guide" per class or background, and a gear catalogue the guides point at (wave 3 design, section 3.6).

Content schema v12 cannot express this. A guide is advice used only while creating a character, so it must never change a calculation. A pack (for example an adventuring bundle) must list what it holds, so taking it can add its contents. Both need new members and one new content kind, so content schema goes from 12 to 13. Character schema stays 11.

## Decision

1. **Shape.** A new content kind `creationGuide` whose effects are three new typed effects, valid only in a v13 revision:

   ```json
   { "kind": "creationGuide", "schemaVersion": 13, "name": "Example starting guide", "effects": [
     { "type": "guideFor", "id": "for", "content": { "contentId": "<a class or background>" } },
     { "type": "primaryAbilities", "id": "primary", "abilities": ["wis"] },
     { "type": "startingEquipment", "id": "kit", "picks": [
       { "id": "weapon", "label": "Weapon", "options": [
         { "id": "a", "lines": [ { "item": { "contentId": "<mace>" }, "quantity": 1 } ] },
         { "id": "b", "lines": [ { "item": { "contentId": "<hammer>" } } ], "requiresProficiency": "weapon.martial" } ] } ] } ] }
   ```

   `guideFor` names one class or background by content id. `primaryAbilities` lists one or two ability keys (`str` to `cha`). `startingEquipment` holds picks; each pick holds options; each option holds lines and may carry `requiresProficiency` (a `weapon.` or `armor.` key). A line is exactly one of four kinds:
   - `{ "item": {contentId}, "quantity": n }`: a fixed item.
   - `{ "anyOf": "weapon.simple" }`: the player chooses one catalogue item in that category. The categories are `weapon.simple`, `weapon.martial`, `armor.light`, `armor.medium`, `armor.heavy` and `armor.shield`, the same vocabulary as grants, never a name.
   - `{ "coins": { "gp": 50 } }`: coins; this is how an "A or B, with B as gold" option is written.
   - `{ "pack": {contentId} }`: adds the pack's contents, not the pack.

   A pack is an ordinary item revision whose `item` effect gains `contains`: `{ "type": "item", "id": "item", "contains": [ { "item": {contentId}, "quantity": 10 } ] }`. `contains` lives on the effect, not on the revision. The JSON schema is `docs/schemas/content-revision.v13.schema.json`.

2. **Meaning.** A guide is advice for the character builder: it says what a class or background starts with and which abilities matter. It changes no calculation. **A character never pins a guide.** The builder reads it and writes ordinary equipment lines and coins on the character, so class and background revisions do not change, and a saved character does not depend on a guide being present. A pack revision is never pinned either, because taking a pack adds its contents. Guides are found through a lookup (`IContentCatalog.GuidesFor`, given a class or background content id), not listed as content: `ListContent`, the Coverage rows and the library side of Duplicates leave them out.

3. **Serialized form (a deviation from the plan, stated plainly).** The plan wrote coins as a currency value and a line quantity as an integer that defaults to 1. The built form keeps what was written. Coins on a kit line are a `KitCoins` record whose `cp`, `sp`, `ep`, `gp` and `pp` are each written only when present, so an explicit zero round-trips. The `quantity` of a kit line and of a contained (pack) line is optional, and absent means 1. Consumers convert `KitCoins` to a total when they need one. Reason: revision hashes are computed from the re-serialized typed model, so the written form is frozen once a v13 revision is published; a default written out would change the hash of a revision that left it off.

4. **Validation.** A guide effect, the `creationGuide` kind or a present `contains` needs content schema 13 (`validate.requires-v13`, which names what was found). The codes:
   - `validate.guide-kind`: a guide effect on a revision that is not a creation guide, or any other effect on a creation guide.
   - `validate.guide-target`: a guide without exactly one `guideFor`, or one that targets itself or something that is not a published class or background of the revision's rules family.
   - `validate.guide-duplicate`: another guide already guides the same target in a shared rules family.
   - `validate.guide-abilities`: not one or two different ability keys.
   - `validate.guide-picks`: too many picks (at most 12), too many options in a pick (at most 20), too many lines in an option (`MaxGuideLines`, 30), a repeated pick or option id, a pick with no options, or a line that is not exactly one kind.
   - `validate.guide-item`: a fixed item or pack line naming something that is not a published item (or pack) of the family, or the guide itself.
   - `validate.guide-quantity`: a quantity outside 1 to 999.
   - `validate.guide-category`: an `anyOf` category outside the six, or a `requiresProficiency` that is not a `weapon.` or `armor.` key.
   - `validate.guide-coins`: a coins line with no coins, or a denomination outside 0 to the currency maximum (`Currency.MaxCoins`).
   - `validate.item-contains`: more than 30 contained lines (`MaxPackLines`), a quantity outside 1 to 999, a line with no item, a pack that contains itself, an item that is not published in the family, or another pack inside it (a pack holds items, not packs).

   Rules that apply to all of them:
   - Below v13, an untyped guide-named effect keeps the generic unsupported-effect warning. Older hand-written content is never newly refused.
   - Each reference resolves to one revision per content id: the batch's newest published one, else the catalogue's newest published one. It resolves once per validation call. Any revision of the target counts, like the other id references.
   - A list over its cap is reported once and not walked.
   - One guide per target per rules family. The check runs when validating a draft too, with the rival required to be published, so a duplicate is found before publish. A new revision of the same guide is not a duplicate.
   - A pack cannot contain itself or another pack, and a kit's `pack` line must name a pack.
   - Coins summed across lines are not validated; kit expansion clamps the total.

5. **Kit expansion.** `KitExpansion.Expand(kit, choices, catalog)` turns a `startingEquipment` effect and the player's `KitChoice`s into new unequipped equipment entries, summed coins and diagnostics. `KitExpansion.Merge` combines results (a class kit and a background kit): equal items merge, coins sum, problems follow in order. Both are pure and tolerate unvalidated data. The diagnostics, each skipping only its own line:
   - `kit.item-unresolved`: an item or pack that is not published, a pack that cannot be given, a pack inside a pack, or a quantity below 1.
   - `kit.choice-missing`: a pick with several options and no choice, a choice naming no option, or an `anyOf` line with no item chosen.
   - `kit.any-of-mismatch`: the chosen item is not in the category.
   - `kit.coins-capped`: a coin total over the currency maximum, capped there.
   - `kit.quantity-capped`: an item total over the equipment maximum, capped there (once per item).

   A pack line's quantity multiplies each contained quantity. A quantity below 1 skips the line, not rounded up. `requiresProficiency` is not enforced by the engine; the builder decides what to offer. `anyOf` categories are tested by one shared list (`KitExpansion.CategoryTests`) that a test keeps equal to the validator's list.

6. **Down-conversion (13 to 12; ADR-023 item 9 refers here).** A creation guide is left out whole below 13, by kind alone, listed as `below.creation-guide`: it only advises, so the copy is never stronger. An item's `contains` is dropped and listed as `below.item-contains`; the item stays as one item. A guide effect on any other revision (not valid, but lowering must be safe) is dropped on its own. Three consequences:
   - A source or campaign file whose source is left with nothing after lowering is blocked (`package.target-blocked`, "has no published content to share yet"), so a notebook that holds only guides cannot be saved for 0.8.x. A reader refuses an empty source, so the writer does too.
   - Plain gear the target does not ship becomes a custom item (Q3c, amends D85): an equipment entry whose item is a bundled revision outside the target's set becomes a `CustomItem` with the same name and quantity, not equipped and no longer linked, listed as `below.gear-as-custom-item`. "Plain gear" means an item revision with no effects, or only `item` effects with no charges and no attunement; a pack is plain gear and becomes one custom item named after it, its contents not expanded. An entry with mods, attunement or spent charges keeps today's rule (the bundled-outside-target block). Nothing is converted for a target below character schema 9, which has no custom items. An item that anything else still holds keeps blocking: a kept entry or its mods, another character in the same file, or a revision in the file that grants or offers it. The save blocks (one line, nothing converted) only when the character would hold more than `Character.MaxCustomItems` (50) custom items, a quantity is outside 1 to the equipment maximum, or a name is longer than `Character.MaxName`. The custom item's id is a UUIDv5 of the entry id, so converting twice gives the same item.
   - Weapon and armour revisions the target has are written as they are.

7. **Notes from the design.** The `anyOf` vocabulary is weapon and armor categories only, so two kinds of choice are not a line kind: "any instrument" has no category, and "simple melee weapon" is narrower than `weapon.simple` (which includes ranged weapons). Write such a choice as a pick with one option per allowed item, or let the pick offer `weapon.simple` and accept that the player may take a ranged weapon.
8. **Known limits.**
   - Validate-then-insert is not one transaction. The app is single-user and runs one command at a time, so a rival guide cannot appear between the two steps; if that ever changes, the check needs a transaction.
   - Callers of `GuidesFor` must tolerate more than one guide for a target (for example after a restore brings in a guide that a rival already covers). They should choose deterministically, not assume one.
   - Current-version export leaves a source with no published content out, while a lowered-empty source blocks the file. The first is a choice about what to share; the second would write a file a reader refuses.
   - No bundled guides or gear ship in the engine change. The 2014 (SRD 5.1) catalogue and guides arrive with Group 6 (item 9); the 2024 ones come later. The licensing review has a section for the 2014 files (SPEC Q-03).
   - The 0.9.0 release ladder row and `BundledAt/0.9.0.txt` arrive with the release PR. Until then `ReleaseLadder.OpensIn` returns null for content at schema 13 and the preview says "a newer TomeStack", which is correct for an unreleased build.

9. **The 2014 bundled content (Group 6).** `srd-5.1-gear.json` (73 items, 7 packs) and `srd-5.1-guides.json` (13 guides: the 12 classes and the Acolyte). Ids, all `51c00000-0000-4000-8000-000000NNNNNN` (revisions `51e00000-...`): items 500001 to 500073, packs 500101 to 500107, guides 600001 to 600013. Modelling rulings:
   - A holy symbol, an arcane focus or a druidic focus is one pick with one option per catalogue item (the `anyOf` vocabulary has no focus category).
   - "Any martial (or simple) melee weapon" is widened to the weapon category (`weapon.martial`, `weapon.simple`); v13 has no melee-only filter, so the player may take a ranged weapon.
   - A line the SRD gives without a choice is a pick with a single option, which expansion takes without a choice; the builder shows it pre-selected.
   - The Druid's wooden shield is the bundled Shield.
   - The Acolyte's 15 gp is a `coins` line; its pouch is an item.

## Consequences

- Content can carry starting equipment and primary abilities for a class or background. A revision that uses none of the new members keeps its version, bytes and hash and calculates exactly as before. A v12 revision that carries the new members keeps them as unknown data and ignores them.
- A package or backup that holds v13 content warns `package.needs-newer-build` and opens in TomeStack 0.9.0 or newer once that release exists. Saving for an older TomeStack leaves guides out and turns plain gear into custom items; it never makes a copy stronger.
- New codes: the `validate.guide-*` family, `validate.item-contains`, `validate.requires-v13`, `kit.*` (diagnostics, not validation errors), `below.creation-guide`, `below.item-contains` and `below.gear-as-custom-item`.
- `ContentRevision.CurrentSchemaVersion` becomes 13; `Character.CurrentSchemaVersion` stays 11.

## Not doing

- **Bundled 2024 (SRD 5.2.1) guides or gear** in this change.
- **Characters pinning a guide**, or a guide changing a calculation.
- **Packs inside packs**, and expanding a pack's contents when saving for an older TomeStack.
- **Enforcing `requiresProficiency` in the engine.**
- **New `anyOf` categories** (instruments, melee).
- **Database changes.**

## Evidence

- `tests/RulesCore.Tests/CreationGuideV13SchemaTests.cs` (the shape, the version rung, round-trip bytes), `CreationGuideV13ValidationTests.cs` (every code, the caps, the duplicate check) and `KitExpansionTests.cs` (expansion, merge, diagnostics).
- `tests/RulesCore.Tests/DownConvertV13Tests.cs` (guides left out, `contains` dropped).
- `tests/AppService.Tests/CreationGuideCatalogTests.cs` (the `GuidesFor` lookup and leaving guides out of listings), `CreationGuideV13SchemaFileTests.cs` (the published JSON schema `docs/schemas/content-revision.v13.schema.json`).
- `tests/AppService.Tests/DownConversionExportTests.cs` (plain gear as custom items and its limits), `SourcePackTests.cs` and `CampaignPackTests.cs` (a guide-only source blocks), and `ReleaseLadderTests.cs` (content one schema version above the current one opens in no released version; schema 13 gets its ladder row with the 0.9.0 release PR, so no ladder row or test is added here).

Related: [ADR-021](ADR-021-mechanics-beyond-d43.md), [ADR-022](ADR-022-content-v12.md), [ADR-023](ADR-023-down-conversion-export.md), LIVING_SPECS D85, D97 and D98. Supersedes: none.
