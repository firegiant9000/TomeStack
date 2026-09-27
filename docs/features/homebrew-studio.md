# Homebrew studio and update review

SPEC I-04, I-05, I-06, S-01 · ADR-002, ADR-003, ADR-004 · MVP "Homebrew", definition of done 3 · status: implemented (M2 item 5).

UI: `src/Ui/src/components/HomebrewStudio.tsx`, `UpdateReviewPanel.tsx`. Service: `src/AppService/Studio.cs` over the M1 publishing API (`features/publishing-and-updates.md`). Acceptance: `tests/AppService.Tests/HomebrewStudioTests.cs` and the e2e test "authors a homebrew subclass in the studio, plays it, and reviews an update".

## Sources

`source.createHomebrew { title, rulesFamilies, publisher?, redistributable? }` creates a local source (SPEC S-01): publisher "Personal homebrew" by default, license "Personal homebrew", edition version `homebrew`, and **not redistributable** unless the author says so. A share export therefore leaves it out and lists it (ADR-007), while a backup keeps it. `source.list` returns every source, without the machine-local PDF path. `content.bySource { sourceId }` lists each content entity of a source with all its revisions (drafts too), in the order they were added.

## Guided authoring

The studio authors subclasses, features, feats and items. Every control writes a declarative effect (ADR-003); nothing runs code (SPEC Q-02):

| Control | Effect |
| --- | --- |
| Add modifier | `modifier`: a field from `app.info.fields`, bonus / replace / set, a value or formula |
| Add resource | `resource`: a name and uses (formula, for example `PB`) |
| Add recovery | `recovery`: a resource of this revision, long or short rest, `all` or a formula |
| Add roll or action | `roll`: dice, optionally the resource it uses (a **limited-use action**) |
| Grant a feature (subclass, feat) | `grant content` of a published feature from the same source, from a class level |
| Add armor (item) | `armor` ([equipment.md](equipment.md)) |
| Description only | A revision with no effects is a **reference-only** feature: its text is kept and shown, and the sheet says "reference only" |

Every effect has an automation setting (automatic, assisted, reference only) and its own text. **Check** validates the unsaved revision (`content.validate`), **Save draft** stores it (`content.saveDraft`; drafts are insert-only, so each save is a new revision id), and **Publish** saves and publishes it (`content.publish` validates again and refuses on any error). Editing published content starts a new draft of the same content id.

## A homebrew subclass in an SRD class (content schema v4)

A class's choice options are exact pins in a published revision, so the SRD Barbarian cannot list a homebrew subclass. Content schema v4 adds **`extendsChoice: { contentId, choiceId }`** to a revision. It says "I am also an option of that choice", naming the content by id, never by name. The calculator offers every *published* revision that extends a choice after the declared options (`IContentCatalog.ChoiceExtensions`). A draft is never offered. `character.choose` accepts it like any option, and chosen from the class, its features follow the class level and `CLASS_LEVEL`.

Validation: the target content must offer that choice (`validate.extends-choice-unknown`) and share a rules family (`validate.reference-family`). A target that is not installed is a warning (`validate.extends-choice-missing`). A revision cannot extend its own choice. `extendsChoice` needs `schemaVersion` 4 (`validate.requires-v4`).

Performance: every calculation looks up the extensions of every offered choice, and a request may calculate several times. The store reads the published extensions once and keeps them in memory. Revisions are insert-only, so it drops that copy only when a revision is added or a transaction rolls back (`HomebrewStudioTests.Choice_extensions_are_read_from_the_database_once_and_follow_new_and_rolled_back_revisions`; M2 review fix). With 5,000 revisions in the library, a play command went from about 430 ms to about 5 ms.

The studio's subclass form offers "Offered in the choice" with every choice of every class for the source's family.

## Publishing and updating characters (SPEC I-06)

After a publish, the studio lists the characters that use an older revision, with their role (`pin`, `class`, `choice`, `equipment`, `grant`). **Review update** opens the update review: the rule changes (effects and properties, including `extendsChoice`), calculated values that change, overrides whose calculated value moves, choices left open and new problems. **Apply update** calls `character.applyUpdate` with `confirm`. "Keep the current revision" changes nothing.

A revision that a character only gets through a grant cannot be updated directly, because the granting revision pins it exactly (ADR-002). The studio says so: publish a new revision of the granting content that grants the new one, then update that. Automating this re-pinning is not in this slice.

## Definition of done 3 (synthetic stand-in)

`HomebrewStudioTests` and the e2e test build an original subclass, "Path of the Test Storm" / "Path of the E2E Storm", for an SRD 5.2.1 Barbarian 3. It has **one modifier** (initiative), **one class resource** (Storm charges = PB, recovered on a long rest), **one limited-use action** (Storm bolt 1d8 + 2, which names the resource; spending it is a separate button) and **one reference-only feature** (Sky Lore). The actual Stardust Guardian test character stays with the owner in the gitignored `tests/RulesFixtures/local/`, so the real DoD 3 check is an owner step.
