# Homebrew studio and update review

SPEC I-04, I-05, I-06, S-01 · ADR-002, ADR-003, ADR-004 · MVP "Homebrew", definition of done 3 · status: implemented (M2 item 5).

UI: `src/Ui/src/components/HomebrewStudio.tsx`, `UpdateReviewPanel.tsx`. Service: `src/AppService/Studio.cs` over the M1 publishing API (`features/publishing-and-updates.md`). Acceptance: `tests/AppService.Tests/HomebrewStudioTests.cs` and the e2e test "authors a homebrew subclass in the studio, plays it, and reviews an update".

## Sources

`source.createHomebrew { title, rulesFamilies, publisher?, redistributable? }` creates a local source (SPEC S-01): publisher "Personal homebrew" by default, license "Personal homebrew", edition version `homebrew`, and **not redistributable** unless the author says so. A share export therefore leaves it out and lists it (ADR-007), while a backup keeps it. `source.list` returns every source, without the machine-local PDF path. `content.bySource { sourceId }` lists each content entity of a source with all its revisions (drafts too), in the order they were added.

## Guided authoring

The studio authors classes (since M5, below), subclasses, features, feats and items. Every control writes a declarative effect (ADR-003); nothing runs code (SPEC Q-02):

| Control | Effect |
| --- | --- |
| Add modifier | `modifier`: a field from `app.info.fields`, bonus / replace / set, a value or formula |
| Add resource | `resource`: a name and uses (formula, for example `PB`). Its `resourceId` is set once, when it is added, and never follows the name, so renaming keeps recoveries, rolls and spent uses linked (e2e "keeps a resource linked to its recovery…"; M2 review fix) |
| Add recovery | `recovery`: a resource of this revision, long or short rest, `all` or a formula |
| Add roll or action | `roll`: dice, optionally the resource it uses (a **limited-use action**) |
| Grant a feature (subclass, feat) | `grant content` of a published feature from the same source, from a class level |
| Add armor (item) | `armor` ([equipment.md](equipment.md)) |
| Description only | A revision with no effects is a **reference-only** feature: its text is kept and shown, and the sheet says "reference only" |

Every effect has an automation setting (automatic, assisted, reference only) and its own text. **Check** validates the unsaved revision (`content.validate`), **Save draft** stores it (`content.saveDraft`; drafts are insert-only, so each save is a new revision id), and **Publish** saves and publishes it (`content.publish` validates again and refuses on any error). Editing published content starts a new draft of the same content id.

## A class of your own (M5 slice 1b, ADR-010)

"New class" opens the class editor (`ClassBasicsEditor.tsx`, inside `HomebrewStudio.tsx`). Each control writes a declarative effect of an existing type, so authoring a nonstandard class needs no code edit:

| Control | Effect |
| --- | --- |
| Hit die | `hitDie` (d6, d8, d10 or d12) |
| Saving throw proficiencies | `grant proficiency save.<ability>`, `onlyAs: startingClass` |
| Multiclass prerequisites | `restriction` on `ability.<ability>.score` with `multiclass`. "Any one of these is enough" puts them in one `group` |
| Skill choice | "Create skill choice" publishes one feature per ticked skill in this source ("<class>: <skill>", each granting that proficiency), as the SRD packs do, and adds a `choice` of them, `onlyAs: startingClass` |
| This class has subclasses | A `choice` (`subclass`) at the chosen class level with **no options of its own**. Homebrew subclasses join it through "Offered in the choice" (`extendsChoice`) |
| Add class column | `scale` (content v9): a name, a key that formulas read as `SCALE.<key>`, and 20 values |
| Add spellcasting | `spellcasting`: ability, spell list key, prepared or known, an optional prepared-spell formula, the slot table (one line per class level) and the multiclass share. The share is none, full, half, a third, or its own table of 20 caster levels (content v9) |
| Grant a feature | `grant content` from a class level, as for subclasses; the level table of the class |

Resources, recoveries, rolls and modifiers work as for other content, and their formulas can read the class's columns. A published class appears in the builder like any class.

**Review fixes (dual-review, 2026-09-29):**
- **Every effect stays visible.** The class editor owns only what it shows: the hit die, starting-class saves, multiclass prerequisites on ability scores, and the `skills` and `subclass` choices. Every other effect of the class (other proficiencies, other restrictions and choices) stays in the rule list, where it can be seen and removed.
- **Edits never reorder or lose effects.** Each edit applies to the latest effect list (`classBasics.ts`) and keeps other effects in place. "Any one of these is enough" survives clearing a value and typing it again, and a prerequisite group of another name is kept.
- **The skill helper publishes no duplicates.** It reuses a feature this source already has for that class and skill, when that feature covers the class's families. It writes the choice only once every option exists, and a retry reuses what was published. The rest of the editor is disabled meanwhile. It warns when options do not cover a family the class was later given.
- **List fields are strict.** Slot rows, columns and the caster table accept only whole numbers, and nothing is dropped. "4, 3, , 2" and "4, 3, x, 2" are errors, never a table with shifted spell levels. Save and Publish stay disabled until they are fixed.
- **Column keys never repeat.** A new column's default key is never one already in use.

**A choice with no options of its own** is content v9 (`validate.choice-options-none` is a warning, and `validate.requires-v9` applies below v9). A v8 build would refuse it with a validation error, so v9 makes it refuse by version instead. The calculator already offered published extensions after declared options.

Acceptance:
- `AppService.Tests/CustomClassTests.A_class_authored_like_the_studio_publishes_takes_a_homebrew_subclass_multiclasses_and_round_trips`;
- the e2e flow "authors a class in the studio and builds it at levels 1, 20 and 5/3 with an SRD class". It authors "E2E Chronicler" with most of the controls above (not "Grant a feature", "Any one of these is enough", the prepared/known picker or the spell-count formula), builds it at level 1 in the builder, and creates and checks it at level 20 and as Chronicler 5 / SRD Wizard 3 (caster level 6) through the service client.

## The homebrew debugger (M5 slice 2, B02)

"Find problems in <source>" studies every content of the source at its latest revision, drafts included. "Find problems" in the editor studies the unsaved revision on screen. Both call `content.diagnose { sourceId | reference | revision }` (exactly one). It is read-only: it writes nothing and changes no calculation.

Each finding names its entry and, when it concerns one, the rule. **Show** opens that entry in the editor (an entry already open keeps its unsaved edits) and moves keyboard focus to the rule's fieldset. A class's hit die, saves, prerequisites, skill choice and subclass choice focus their group in "Class basics". A finding about the whole entry focuses the editor's heading.

The findings are validation (`content.validate`: missing references, invalid formulas, levels outside 1–20, and so on) plus what needs the whole content graph (`RulesCore.ContentGraph`, `ContentDebugger`):

| Code | Severity | What it means |
| --- | --- | --- |
| `debug.resource-dead` | warning | No roll or toggle spends the resource and no recovery restores it, so its uses change only by hand |
| `debug.recovery-orphan` | warning | The recovery names a resource its own revision does not define. The calculator looks only there, so it never applies. It replaces validation's softer `validate.recovery-resource` |
| `debug.roll-resource-unknown` | warning | The roll spends a resource its revision does not define (and names no other content with `resourceContent`) |
| `debug.grant-nested` | warning | The content is only ever granted, and granted content's own grants are not followed (grants are one level deep; `grant.nested-ignored` at calculation), so its grants never apply where it is granted (only when a character picks it directly) |
| `debug.subclass-unreachable` | warning | Nothing lists, extends to or grants the subclass |
| `debug.feature-unreachable` | warning | The content reads `CLASS_LEVEL` or a class column, but no class reaches it. (A level gate alone is fine: outside a class it counts character levels) |
| `debug.choice-empty` | warning | A choice with no options that nothing extends yet |
| `debug.extension-choice-missing` | warning | The subclass extends a choice that the class's current revision no longer has (an older one did). Validation accepts any revision; the calculator offers it only with the revision that has the choice |
| `debug.scale-undefined` | warning | A formula reads `SCALE.<id>` that neither the class nor the subclass it comes through defines, for at least one class that reaches it. It replaces `validate.scale-unknown` |
| `debug.scale-unused` | note | A class or subclass column that no formula of the class, its subclasses or their features reads (it still shows on the sheet) |
| `debug.scale-collision` | warning | A subclass defines a scale id that a class it is offered in also defines (the class's column wins). When the subclass extends the class's choice, validation's `validate.scale-duplicate` already says so as an error that blocks publishing, and it is listed once. Otherwise (a declared option, a grant) nothing blocks it, so it is a warning |
| `debug.scale-collision-older` | warning | The same, with an older published revision of the class. It replaces `validate.scale-duplicate-older` |
| `debug.reference-stale` | note | A grant or choice option names an older revision than the newest published one, so characters get the older one |

Only validation errors are errors (they block publishing); every graph finding is a warning or a note.

**Reach follows the calculator.** From each class, only automatic grants that always apply are followed, only from a root (the class, or content chosen in a choice), and one level deep. Choices are followed from anything reached, and an extension only while the extended revision has that choice. Level gates are not applied: the graph asks what a class can ever reach. A subclass sets the context in which its own columns are defined, and only automatic columns count, as in the calculation. **Not modeled:** rules families. A class for both families that grants a 5.1-only feature counts as reaching it for both, while the calculator refuses it for 5.2.1 characters (validation flags only fully disjoint families).

**Bounded (SPEC Q-02).** The walk stops at 200,000 states or 2,000,000 edges examined (`ContentGraph.MaxReachStates`, `MaxEdgeSteps`), and walks the classes under study first. If it stops early, the report says `truncated` and leaves out the findings that need the whole walk (unreachable content and the scale checks) rather than report them falsely. The graph's lookups, the scope keyed by reference and by content, and the set of choices something extends are built once per report. What the budgets do not bound is the merge of scale reads per reach (reaches times the scale ids one content reads), which is limited by the reach bound and the size of one revision. Nothing is claimed beyond that.

**Review fixes (2026-09-29).**
- A grant or option is followed to the exact revision it names, as the calculator admits it, not to the content's current revision; an extension names none and is offered from every published revision that extends the choice (and from the revision under study).
- When the walk is truncated, validation's `validate.scale-unknown` and `validate.scale-duplicate-older` are kept, since their replacements (`debug.scale-undefined`, `debug.scale-collision-older`) are skipped then.
- `debug.grant-nested` now says the grant does not apply where the content is granted; a character who picks the content directly still gets it.
- The formula identifiers of a pre-v9 revision are read without `SCALE.<id>`; revisiting a known state at the bound no longer marks the walk truncated.

**Scope.** For a source, the scope is each content's latest revision. "Find problems" on one revision studies it among the latest revisions of its own source (drafts too), so both buttons agree about it. Everything else uses its newest published revision. The scope is validated together, so its drafts may name each other.

Acceptance:
- `RulesCore.Tests/ContentDebuggerTests`: each finding on original content, the reach rules, both bounds and the classes-first order, and the Test Chronicler with no findings;
- `AppService.Tests/ContentDiagnoseTests`: the scope rules, the command's JSON, "writes nothing", and both bundled SRD sources (each over 500 revisions) with no findings;
- the e2e flow "finds a problem in a source with the debugger, shows its rule, and clears it in the editor", including Show on an entry already open, which keeps its unsaved edits.

## Try it: the draft sandbox (M5 slice 3, B03)

The class and subclass editors have a **Try it** section. It calculates the revision on screen, saved or not, as if it were published. It can run on a blank character (ability scores 10, the draft's first rules family) or on **a copy of** a saved character of a family the draft supports. The level is optional. It is the level in the class, or for a subclass, in the class it joins. `content.sandbox { revision | reference, characterId? | rulesFamily?, level? }` returns the unsaved sheet, the draft's validation report, and, for a copy, every calculated sheet field that changes (fields only: resource maximums and class columns are not compared, but show in the sheet).

**How the draft is placed on the copy:**
- Every reference to another revision of the same content becomes the draft.
- A class the copy already has keeps its level, unless one is chosen; otherwise the class is added. A character with no class levels recorded (its class only pinned) keeps its level, and the class is no longer pinned separately.
- A subclass is selected in the choice it extends, on the class revision the copy has. A selection of an older revision in another choice (the draft now extends a different one) is dropped. If the copy lacks that class, the class's newest published revision is added, at the chosen level or else at the choice's level (never below the character's level when it has no class levels recorded).
- A subclass that extends a feature's choice can be tried only on a copy that already makes that choice, and then with no level (it has no class level to set).
- A subclass offered only as a **declared option** of a choice cannot be tried as a draft: that choice names one exact revision, and the calculator accepts nothing else there.

**The invariant (owner decision, LIVING_SPECS D14): only published revisions affect *saved* characters.**
- The sandbox is the one place a draft calculates. `RulesCore.DraftOverlayCatalog` shows that one draft as published, in memory, for one calculation. There, it replaces the other revisions of its content in the choice it extends.
- The copy gets a new id, and the studio never stores it.
- `content.sandbox` writes no revision, character, play state, campaign or gap note. `SandboxTests` compare revisions, characters, sources, campaigns and gap notes before and after. A saved character still calculates its published revision.
- **What stays true even if a client saves the copy:** `character.save` does not refuse draft references (characters that pin drafts are a supported case, shown inactive with `content.unpublished`). A copy saved through the API would therefore be stored. It would still never calculate the draft.
- The campaign check reads through the overlay too, so an unsaved draft from a source the campaign does not allow is flagged on the copy.
- Changing the revision, the character, the level or the blank character's rules family hides the result until **Try it** runs again. A draft for both families can be tried on a blank character of either.

Refusals:
- `sandbox.draft-required`: published content, or no draft sent;
- `sandbox.kind`: only a class or subclass can be tried;
- `sandbox.level`;
- `sandbox.level-class`: a level for a subclass of a feature's choice;
- `sandbox.rules-family`;
- `sandbox.scope`: both a character and a blank character's family;
- `sandbox.subclass-unplaced`: the subclass is offered in no choice;
- `sandbox.subclass-declared-option`;
- `sandbox.subclass-class`: it joins a choice of a feature, and there is no copy that makes it;
- `rules-family.unknown`, `character.not-found`, `content.not-found`, `validate.empty-entry`;
- the character's own checks, such as more than 20 levels in total.

Acceptance:
- `RulesCore.Tests/DraftOverlayCatalogTests`: both families, and the catalog underneath is untouched;
- `AppService.Tests/SandboxTests`;
- the e2e flow "tries a draft class at a chosen level on a blank character without saving anything".

## A homebrew subclass in an SRD class (content schema v4)

A class's choice options are exact pins in a published revision, so the SRD Barbarian cannot list a homebrew subclass. Content schema v4 adds **`extendsChoice: { contentId, choiceId }`** to a revision. It says "I am also an option of that choice", naming the content by id, never by name. The calculator offers every *published* revision that extends a choice after the declared options (`IContentCatalog.ChoiceExtensions`). A draft is never offered to a saved character (the studio sandbox alone offers one draft, in memory; see "Try it"). `character.choose` accepts it like any option, and chosen from the class, its features follow the class level and `CLASS_LEVEL`.

Validation: the target content must offer that choice (`validate.extends-choice-unknown`) and share a rules family (`validate.reference-family`). A target that is not installed is a warning (`validate.extends-choice-missing`). A revision cannot extend its own choice. `extendsChoice` needs `schemaVersion` 4 (`validate.requires-v4`).

Performance: every calculation looks up the extensions of every offered choice, and a request may calculate several times. The store reads the published extensions once and keeps them in memory. Revisions are insert-only, so it drops that copy only when a revision is added or a transaction rolls back (`HomebrewStudioTests.Choice_extensions_are_read_from_the_database_once_and_follow_new_and_rolled_back_revisions`; M2 review fix). With 5,000 revisions in the library, a play command went from about 430 ms to about 5 ms.

The studio's subclass form offers "Offered in the choice" with every choice of every class for the source's family.

## Publishing and updating characters (SPEC I-06)

After a publish, the studio lists the characters that use an older revision, with their role (`pin`, `class`, `choice`, `equipment`, `grant`). **Review update** opens the update review: the rule changes (effects and properties, including `extendsChoice`), calculated values that change, overrides whose calculated value moves, choices left open and new problems. **Apply update** calls `character.applyUpdate` with `confirm`. "Keep the current revision" changes nothing.

A revision that a character only gets through a grant cannot be updated directly, because the granting revision pins it exactly (ADR-002). The studio says so: publish a new revision of the granting content that grants the new one, then update that. Automating this re-pinning is not in this slice.

## Definition of done 3 (synthetic stand-in)

`HomebrewStudioTests` and the e2e test build an original subclass, "Path of the Test Storm" / "Path of the E2E Storm", for an SRD 5.2.1 Barbarian 3. It has **one modifier** (initiative), **one class resource** (Storm charges = PB, recovered on a long rest), **one limited-use action** (Storm bolt 1d8 + 2, which names the resource; spending it is a separate button) and **one reference-only feature** (Sky Lore). The actual Stardust Guardian test character stays with the owner in the gitignored `tests/RulesFixtures/local/`, so the real DoD 3 check is an owner step.
