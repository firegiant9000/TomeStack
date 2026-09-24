# MVP definition · v0.1

## Goal

A Windows player can install TomeStack, create and play a level-1-to-20 SRD-based character under either 2014 or 2024 rules, add a structured homebrew subclass or feature, and recover/export their work offline. The product is useful before PDF mechanics interpretation or arbitrary custom base classes exist.

## Committed candidate scope

| Area | MVP behavior | Exit evidence |
| --- | --- | --- |
| Windows app | Offline installer; local data location; no login; safe startup after restart | Clean-machine install and reopen |
| Rules packs | Attributed SRD 5.1 and 5.2.1 baseline data, separate edition IDs and key differing rules | Fixture characters for each family; explicit mix warning |
| Builder | Species, background, class/subclass, scores, skills, equipment, spells, level-up; multiclass path | Finish two fixture characters and level them; unresolved choices flagged |
| Sheet | Derived stats with traces; attacks, saves, skills, damage and formula dice; mutable HP/slots/resources/conditions | Play through a short scripted encounter and rest |
| Homebrew | Guided subclass/features, simple modifiers/resources/actions/rolls/recovery; reference-only text; revision pinning | Stardust Guardian fixture with representative automatic and assisted features |
| Sources | Attach PDF; page navigation; manual entry tied to pages; import limited page ranges and whole documents as reference | Open the cited page from a feature offline |
| Safety | Validation, failure isolation, visible overrides; backup/restore; human-readable export/import | Malformed feature leaves sheet usable; fresh-install round trip |
| Campaign | Local profile with allowed sources and rules family | Two profiles show different allowed content |

**Boundary:** whole-book automatic extraction into working rules, high-quality OCR and inferred mechanics, general custom class authoring, plugins, full text search, printable cards, and DM/monster tooling are post-MVP. PDF page import as reference and manual structured authoring are MVP; candidate extraction can ship experimentally only if the review gate is complete.

## Definition of done

1. A real Windows build is installable and fully functional offline after installation.
2. The user can create one SRD 5.1 and one SRD 5.2.1 character, save/reopen, level, rest, roll and see a trace for major numbers.
3. A Stardust Guardian test character can use at least one modifier, one class resource, one limited-use action and one reference-only feature from a homebrew subclass. Unsupported mechanics remain visible with a clear manual step.
4. An imported PDF remains locally accessible from a linked feature/page; imported text never activates a rule without acceptance.
5. Export/import on a clean data directory preserves pinned content, character state, overrides, campaign/source metadata and licensing notices; the user is told if a PDF is absent.
6. Malformed formulas, missing references and unsupported cross-edition content produce actionable errors without data loss.

## Separate replacement milestone (M3)

After MVP, build a complete character using Arlo's current Stardust Guardian material and play an entire session without D&D Beyond for that character. Test complex choices, multiclass, spellcasting if applicable, item interactions, short/long rest recovery, update review and printable backup. Gaps become prioritized issues; passing requires the actual character, not only synthetic fixtures. This is a stronger gate than shipping MVP.

## Release checks

Small domain tests for representative 2014/2024 rules, import failure tests, export round-trip test, installer smoke check, backup recovery, keyboard walkthrough and manual play rehearsal. Record any supported feature only after an example passes end to end.
