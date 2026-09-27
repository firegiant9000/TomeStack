# Choices

SPEC C-01 · ROADMAP M1 · status: data, calculation and API implemented (M1 item 4). The builder UI is M2.

`src/RulesCore/Calculation.cs` (resolution), `TomeStackApp.Choose` (`character.choose`). Tested in `tests/RulesCore.Tests/ChoiceTests.cs` and `tests/AppService.Tests/ChoiceCommandTests.cs`.

## Content

A `choice` effect offers `count` of its `options` (exact pins), for example "two skills" or "a subclass". Content schema v3 adds `level`: the choice is offered from that class level (character level outside a class), just like `grant.level`.

An option is ordinary content: a feature that grants a skill proficiency, a subclass, a feature with an ability increase. So everything an option does is traced like any other content.

## Character

`choices: [{ source, choiceId, selected: [pins] }]` (character schema v3). `source` is the exact revision offering the choice. There is one entry per choice (`character.choice-duplicate`). Chosen revisions count as the character's references, so packages include them.

## Calculation

- Every choice offered by an active revision whose level is reached appears in `sheet.choices`, with its options, what was selected and `resolved`.
- An unresolved choice (fewer selections than `count`) is flagged with `choice.unresolved` and listed on the sheet ("Choices to make").
- A selection not in the options is not applied (`choice.invalid-option`). Selections beyond `count` are not applied (`choice.too-many`). A choice whose `count` is below 1 (which validation refuses on publish and import) is not offered, and nothing selected for it applies (`choice.invalid-count`).
- An option must be usable under the character's rules family. Otherwise it is refused (`content.rules-family-mismatch`, message "Chosen from …"), and the choice stays unresolved. A cross-family exception (B06) recorded for that exact option admits it, and the character's own policy then governs it. Content *granted* by another revision cannot have its own exception: it follows its granter.
- Chosen content joins the active set as a root, like a pin. Its grants are followed (one level), its own choices are offered, and its trace says "chosen from class 'Barbarian'". A subclass chosen from a class belongs to that class, so its features follow the class level and can read `CLASS_LEVEL`.
- A selection for a choice that is not offered (the revision is not active, or the level is not reached yet) is kept but not applied (`choice.orphaned`).
- Every revision is admitted once, so selections that point back at each other cannot loop.
- **Rules-family policy:** a *feature* chosen from or granted by a species or background counts as that origin, however many features away it is (a background grants a feature, which offers a choice, whose option raises Strength). A background's "+2 Str, +1 Con" option therefore applies only where backgrounds may raise scores (2024), and a 2014 background cannot bypass the policy through a chosen feature. Likewise, under 2014 rules a background, or a feature that comes from one, cannot bring in a feat by offering it as a choice any more than by granting it (`policy.background-feat`).

## API

`character.choose { characterId, source, choiceId, selected }` records the selection and returns the recalculated view. An empty `selected` clears it. It refuses, and changes nothing, when:

- the choice is not offered now (`choice.not-offered`);
- the same option appears twice (`choice.duplicate-option`);
- the count is exceeded (`choice.too-many`);
- an option is not listed (`choice.invalid-option`);
- an option is not a published revision for the character's family (`choice.option-unavailable`);
- an option is already selected for another of the character's choices (`choice.option-already-chosen`), as SRD wording such as "another skill" requires;
- the source or an option is empty (`choice.empty-entry`).
