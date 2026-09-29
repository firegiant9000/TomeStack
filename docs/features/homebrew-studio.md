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

## Compare revisions: diff and before/after (M5 slice 4, B07 and B04)

The editor has a **Compare revisions** section once the entry has a stored revision. It compares any stored revision ("From") with another stored one or with the revision on screen, unsaved ("To"). It calls `content.compare { from, to | toRevision, characterIds?, blank? }`, which writes and applies nothing.
- **Rules:** `ContentDiff` by effect id and property, the same table as the update review (`MechanicsDiffTable.tsx`).
- **Texts:** the name, the description and each rule's text, line by line (`RulesCore.ContentTextDiff`, a longest-common-subsequence alignment). Each line says in words whether it was added, removed or unchanged, and colour is only a second cue. It is bounded (SPEC Q-02): a text over 2,000 lines, one over 1,000,000 alignment cells, or a comparison over 4,000,000 cells in total is shown whole (old text, then new) instead of aligned.
- **Before and after:** both revisions run on unsaved copies of chosen characters (at most 20), and for a class or subclass also on a blank character at a chosen level. This is the update review's computation (`SheetChanges`, shared with `character.reviewUpdate`): the values that change, new and resolved problems, and choices left open.
  - A copy that already uses the content, of any kind (a declared-option subclass too), is moved from one revision to the other as `character.applyUpdate` would move it. The update review's family rule applies: the revision supports the character's family, or the character records a cross-family exception for it. `CompareTests` show that the copy's values equal `character.reviewUpdate`'s.
  - A class or subclass the copy lacks, and the blank character, are placed as in the sandbox.
  - Other content the copy lacks cannot be compared. A character that gets it only through a grant is told so (`compare.granted`: the grant names one exact revision). The others get `compare.unused`.
  - A character that cannot take a revision (its family, its levels, a choice) gets a "Not run" note; the rest still run.
  - A draft calculates through the sandbox overlay, one draft per calculation, so a stored draft can be compared with an unsaved one (tested).
  - **Bounds (SPEC Q-02):**
    - each revision has at most 5,000 rules (`compare.too-large`);
    - at most 20 characters;
    - diagnostics are matched by key, linear in their number;
    - text lines are aligned as numbered ids;
    - a bad blank-character level or family is refused before anything is calculated.
  - The ROADMAP's "bundled original sample fixtures" are not in the shipped app. The blank character stands in for them.

Review fixes (2026-09-29):
- A text shown whole is capped (SPEC Q-02): 1,000 lines and 100,000 characters per text, 4,000 lines and 400,000 characters per comparison. `TextChange.notShown` counts the lines left out, and the panel says "N more lines not shown". A very long text is counted, not split into lines.
- The panel also shows each character's resolved problems and choices left open, as the update review does.
- Characters are offered by the union of the two revisions' families (the on-screen draft's when it is the To) plus those that record a cross-family exception for this content (`character.list` carries `exceptionContentIds`, ids only). A pick that is no longer offered is not sent.
- `CompareTests` cover new and resolved problems, and that a problem which persists is in neither list, against the update review.

Refusals:
- `compare.to-required`;
- `compare.different-content`;
- `compare.same-revision`;
- `compare.too-many`;
- `compare.too-large`;
- `compare.blank-kind`: a blank character needs a class or subclass;
- per character, as a "Not run" note: `compare.unused`, `compare.granted`, `sandbox.rules-family` and the sandbox's placement codes;
- `content.not-found`, `character.not-found`, `rules-family.unknown`, `sandbox.level`.

Acceptance:
- `RulesCore.Tests/ContentTextDiffTests`;
- `AppService.Tests/CompareTests`, which also shows that the copy's values equal `character.reviewUpdate`'s and that nothing is written;
- the e2e flow "compares the published revision with the unsaved one by rules, text and on a character copy".

## Relationships: the content tree (M5 slice 5, B19)

**Show relationships** in the editor draws the revision on screen as a tree. It runs from the class, to the level each grant or choice applies from, to the feature or choice option it brings in, to that content's resources, and on to the rolls and toggles that spend them and the recoveries that restore them. Other rolls and the class columns are shown too. The revision on screen is placed among its source's latest revisions (drafts included), as the debugger studies it. `content.tree { reference | revision }` builds it (`RulesCore.ContentTree`, over `ContentGraph`) and writes nothing.
- **As the calculator reads it:**
  - a grant or declared option shows the exact revision it pins, which is what characters get when it is published, with a note when a newer revision exists. A pin on a draft is still shown, with the note "Draft: characters get nothing from it until it is published; publishing creates a new revision, so re-point this grant", because the calculator refuses a draft (`content.unpublished`). Content that extends a choice shows its current revision, with "Draft: not offered to characters until published" while that is a draft;
  - a granted content's own grants are shown as "Not followed" (grants are one level deep);
  - a grant that is not automatic, or not always applied, is shown as never applied, and one that names no content says so;
  - a resource id defined twice keeps the first definition, and the repeat is marked ignored;
  - toggles that spend nothing are listed too;
  - a choice with nothing to pick says so;
  - a recovery for a resource its content does not define says it never applies.
  - **Not modeled:** rules families (as for the debugger).
- **Cycles end:** content already on the path is shown once more, with no children ("Already shown above").
- **Bounded (SPEC Q-02):**
  - 12 levels deep, and no new node once 5,000 exist (plus the ancestors still open then). A cut tree says so.
  - Each revision's effects are sorted once, so content reached along many paths costs its size once.
  - Labels are cut at 120 characters, and node ids are positions (`0.2.1`), never user text.
  - The revision on screen may have at most 5,000 rules (`tree.too-large`).
  - The store is read once, and the graph is built without the class reach walk, which the tree does not use.
- **Review fixes (2026-09-29):** draft grants and extensions are noted as above, and each owner's extension edges are grouped by choice once, so a choice reads its own instead of scanning every edge.
- **Activation:** Enter on a node focuses its rule when the rule belongs to the entry being edited. Each node carries the owner of its rule, so a "Grants X" node shows the entry's own grant.
- **Keyboard and screen readers (the WAI-ARIA APG tree view pattern; accessibility checklist item 22):**
  - It is a `tree` of `treeitem`s in nested `group`s, each with `aria-level`, `aria-posinset`, `aria-setsize` and `aria-expanded`.
  - One item is in the tab order (roving tabindex).
  - Up and Down move between visible items. Right opens an item, then goes to its first child. Left closes it, then goes to its parent. Home and End go to the first and last item. Space opens or closes an item.
  - Enter on a rule of the entry being edited moves focus to that rule's fieldset.
  - It is not a canvas: every relation is text.

Acceptance:
- `RulesCore.Tests/ContentTreeTests`: the Test Chronicler, grants that are not followed, cycles, the bound;
- `AppService.Tests/ContentTreeCommandTests`;
- the e2e flow "shows an entry's relationships as a keyboard tree and jumps from a node to its rule".

## Templates (M5 slice 6, B15; provisional)

**Start from a template** in the studio opens an unsaved draft in the editor. It never saves or publishes anything by itself (drafts only): the author saves it, and publishing runs the usual checks. The set is the one the owner approved (LIVING_SPECS D14). It is provisional: the M3 gap notes will revise it (D13). All template text is original to TomeStack, with no SRD or third-party text (SPEC Q-03). The templates are `src/Ui/src/templates.ts`, pure functions tested in `templates.test.ts`.

| Template | Kind | What it fills in |
| --- | --- | --- |
| A feature with limited uses | feature | A resource (uses = `PB`), an assisted action that spends one use, and a long-rest recovery of all uses |
| A stance you switch on and off | feature | A resource of 2 uses with a long-rest recovery, a toggle that spends one use when switched on, and a +1 Armor Class bonus only while the toggle is on (content v6 `toggle` and `modifier.toggle`) |
| A subclass skeleton | subclass | Empty feature slots (content grants that name nothing yet) at class levels 3, 6, 10 and 14. The author chooses "Offered in the choice" and a published feature for each slot |
| A class skeleton | class | A d8 hit die, Constitution and Wisdom saves (starting class), a subclass choice at level 3 with no options of its own, and empty reference-only Ability Score Improvement slots at levels 4, 8, 12, 16 and 19 |

**The skeleton levels are fixed, provisional defaults** (3, 6, 10 and 14 for a subclass; 4, 8, 12, 16 and 19 for improvements). They are not taken from the class or the rules family: classes differ, and so do the families' high-level features. The author moves or removes the slots, and the M3 gap notes may change the defaults (D13). A template leaves the description (shown on the sheet) empty; its explanation is only the hint beside the picker.

An empty slot is a validation error (`validate.grant-content-missing`), so a skeleton can be saved as a draft but not published until each slot names a published feature. The debugger and the relationship tree show the slots too. For the stance template, the editor now shows `toggle` rules (name, and the resource it spends) and a modifier's "Applies: while … is on". That choice is offered only for a modifier that is always on or switched by a toggle, so a situational timing from imported content is never turned into "always". Removing a toggle turns the modifiers it switched back to always on, and removing a resource leaves the toggles that spent it spending nothing. "Use template", like "New …", replaces an open editor's unsaved edits.

Acceptance:
- `templates.test.ts`;
- the e2e flow "starts homebrew from a template as an unsaved draft, publishes a stance, and a skeleton waits for its slots". It also runs all four templates through the server's checks (only the skeletons' empty slots are errors) and removes a stance's toggle.

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
