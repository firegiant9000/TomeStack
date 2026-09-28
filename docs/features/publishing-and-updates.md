# Publishing revisions and updating characters

SPEC I-06 · ADR-002, ADR-004 · status: API implemented and tested (M1 item 2). The authoring and update-review UI is implemented (M2 item 5, [homebrew-studio.md](homebrew-studio.md)). `content.affected` also reports `equipment` (M2 item 4), and the mechanics diff includes `extendsChoice`.

`src/AppService/Publishing.cs`, `src/RulesCore/ContentDiff.cs`. Tested in `tests/AppService.Tests/PublishingTests.cs`.

## Lifecycle

`draft → content.validate → content.publish → content.affected → character.reviewUpdate → character.applyUpdate (confirm)`

| Command | Payload | Does |
| --- | --- | --- |
| `content.saveDraft` | `{ revision }` (status `draft`) | Stores an inactive draft. Drafts are insert-only too: a changed draft is saved under a new revision id. Anything other than a draft is refused (`content.draft-required`) |
| `content.validate` | `{ reference }` or `{ revision }` | Reports schema, reference, formula and cycle problems (`features/validation-and-restrictions.md`) |
| `content.publish` | `{ reference }` of a draft | Validates **again** and refuses on any error (`content.validation-failed` plus the errors). Otherwise it inserts a new revision with the same `contentId`, a new `revisionId` and status `published`. The draft is kept unchanged, and no character changes. Returns the new reference, the report and the affected characters |
| `content.revisions` | `{ contentId }` | Every stored revision of that content, in the order it was added |
| `content.affected` | `{ contentId }` | Characters that use any revision of that content, and how: `pin`, `class`, `choice`, or `grant` (granted by something they reference, one level, with `via`) |
| `character.reviewUpdate` | `{ characterId, from, to }` | Computes, **without changing anything**: the mechanics diff (effects added, removed or changed by id; name, kind, families, page, source and summary); every displayed field whose value changes; new and resolved diagnostics; choices left unresolved; and overrides whose calculated value moves underneath them |
| `character.updates` (M3 C7) | `{ characterId }` | **Writes nothing.** For each revision the character references directly (pins, classes, choice selections, equipment and spells), the newest published revision of that content that supports the character's family (or its recorded exception), when it differs: `{ from, to, name, role, sourceTitle, bundled }`. `bundled` is true for a source that ships with TomeStack (an SRD pack) and false for the user's own. Granted content moves with its granter, so it is not offered on its own |
| `character.applyUpdate` | `{ characterId, from, to, confirm }` | Refused unless `confirm` is `true` (`update.confirmation-required`). Replaces `from` with `to` in pins, classes, choice sources and selections, and recorded exceptions. Levels, overrides and other choices are kept |

## Rules

- **Nothing changes silently.** Publishing never touches characters, and every character opts in separately (LIVING_SPECS change workflow step 5).
- An update stays within one content id (`update.different-content`). It adopts only a published revision (`update.not-published`) that supports the character's family, unless an exception is recorded for it (`update.rules-family`). It moves a revision the character actually uses (`update.not-referenced`).
- When a class revision is replaced, choice selections carry over to the new revision by `choiceId`. A choice the new revision no longer offers shows up in the review as unresolved, and on the sheet as `choice.orphaned`.
- Overrides stay the displayed value (SPEC C-06), and the review lists every override whose calculated value would change, so the user can decide whether to keep it.
- Published revisions are never rewritten. `ListRevisions` shows the whole history of a content id, including drafts.
- **New picks get the newest revision (M2).** `content.list` marks every older published revision of a content `superseded`. The builder's pickers and the equipment "Add" list leave those out, while saved characters still show the names of the revisions they pin. Bundled SRD content follows the same rule, for example the content v5 Barbarian revision (`licensing/srd-pack-review.md`).

## Source updates (M3 C7, ROADMAP M3)

A new revision can arrive in two ways: a newer TomeStack build seeds a new revision of a bundled pack (for example the SRD casters' content v7 revisions, M3 C3), or the user publishes one in the studio. Either way:

- **Offered, never applied.** The sheet's **Updates available** panel lists each offer (`character.updates`) as bundled or "your source". "Review update: <name>" opens the same review as the studio (`character.reviewUpdate`), and only its "Apply update" changes the character (`character.applyUpdate` with `confirm`). "Keep the current revision" closes it.
- Seeding, restarting or importing never moves a pin. The panel appears only when there is an offer.
- Acceptance: `SourceUpdateTests` uses an original fixture, `fixture-pack-m3-source-update.json`, a second revision of the dev pack's "Fixture Arcanist" as a newer build would ship it. The offer appears, the character is unchanged after a restart, the review shows the change, applying needs `confirm`, and afterwards nothing more is offered. A homebrew source works the same way, and a newer revision for another rules family is not offered. The e2e test "authors a homebrew subclass… reviews an update" applies an update from the sheet, while a bundled Barbarian offer stays unapplied.

## Packages

An updated character exports its new revision (and whatever that revision grants), not the old one. On a clean machine it calculates the same sheet (`PublishingTests.An_updated_character_round_trips_through_a_package_to_a_clean_machine`).
