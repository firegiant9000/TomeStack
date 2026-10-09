# Builder: create, level up and answer choices as drafts

SPEC C-01, C-07, S-02 · ROADMAP M2 · MVP "Builder" · status: implemented (M2 item 1).

`src/Ui/src/components/CharacterBuilder.tsx` over `src/AppService/Builder.cs`. Acceptance: `tests/AppService.Tests/BuilderDraftTests.cs` and the e2e test "builds an SRD 5.2.1 Barbarian as drafts" in `src/Ui/e2e/flow.e2e.tsx`.

## Drafts (SPEC C-07)

Every builder flow edits a **draft**: a character held by the UI, never stored. The service calculates it with the same checks as saving:

| Command | Payload | Does |
| --- | --- | --- |
| `character.preview` | a `Character` | Keeps the level in step with the class levels, validates like `character.save` (including `character.rules-family-changed` for a saved character), and returns `{ character, sheet }`. Writes nothing |
| `character.previewChoice` | `{ draft, source, choiceId, selected }` | Answers one choice on the draft with exactly the checks of `character.choose` (`features/choices.md`), and returns the next draft and its sheet. Writes nothing. Without a draft: `character.draft-required` |

The last button commits the draft in **one** command: `character.create` (which now also takes `classes` and `choices`) for a new character, `character.save` for a level-up or answered choices. **Cancel** discards the draft, so nothing is ever partly applied, and the status line says so.

A draft lives in the UI only. It does not survive closing the app: an interrupted draft is lost, and the stored character is unchanged.

## Flows

1. **New character (D32):** six steps, each a named form with its own "Next" and "Back" (listed once, under [The new-character steps](#the-new-character-steps-d32)). A step says how many are installed when a family has one species or background or none. Back keeps every pick, and the heading takes focus on each step change. Every option shows its source, page and families, and only the character's rules family is offered: the other family's species, backgrounds, classes (also in level-up), other content and choice options are not listed (SPEC S-02, D31). Content of the character's own family that a campaign does not allow stays listed and disabled ("not allowed in this campaign", P-01) until an exception reason is given. A choice option that was already chosen when the picker appeared (an imported character) stays shown, and stays in place when unticked (inert, never removed or disabled under focus), so it can be unticked without the focus being lost. Changing the rules family (or picking a campaign that changes it) drops any pick that no longer fits, so nothing stays checked but disabled and is then silently not applied (as in the M1 form; e2e "drops picks that do not fit when the rules family changes…"; M2 review fix). Leaving Background ("Next: choices") previews the draft.
2. **Level up** (sheet button): pick the class that gains the level: an existing class, or a new one at level 1 (the multiclass path). Hit points use the fixed value. A rolled total is recorded as an override on the sheet.
3. **Make choices** (on the sheet's "Choices to make"): the draft is the saved character, and only its choices are shown.

The choices step lists every choice the draft offers now, from `sheet.choices`: level-1 choices, a subclass at its level, and choices from chosen content (Primal Knowledge). Each is a group of checkboxes named by its legend ("Barbarian: choose 2 (to do: 0 of 2 chosen)"). Checkboxes beyond the count are disabled. A refused answer (for example `choice.option-already-chosen`) is shown as an error, and the draft is kept.

**Spells (D04, [spellcasting.md](spellcasting.md)):** for each caster the draft has, the choices step adds a picker. It lists the spells on the caster's list up to its highest slot level, grouped by level, with the counts for its level ("0 of 3 cantrips, 0 of 4 prepared spells"). Picks are recorded on the draft and previewed. Spells are campaign-restricted like other content: one from a source the campaign does not allow is listed disabled ("not allowed in this campaign") until an exception reason is given, and the exception is then recorded with the spell on save ([campaigns.md](campaigns.md)). Spells never appear under "Other content". Nor does content that another revision grants or offers in a choice (`ContentOption.Standalone` false: class features, skill options); it arrives through its class. Superseded revisions are not offered either ([publishing-and-updates.md](publishing-and-updates.md)).

**Unresolved choices are flagged, not blocking.** The step says how many are left, and saving is allowed. The sheet then lists them under "Choices to make" (`choice.unresolved`).

## The new-character steps (D32)

1. **Rules:** the name (Next waits for one), the campaign (if any) and the rules family. The other family's content is never offered afterwards (D31).
2. **Ability scores:** the base scores by one of four methods, standard array first (owner answer 8); "Next" waits until the chosen method gives valid scores (see below).
3. **Species**, 4. **Class** and 5. **Background:** radio groups, each with "None"; Background also holds the other content.
6. **Choices and create:** a read-only **Review** (a `section` named "Review" holding a `dl`: name, rules family, campaign, scores with the method's name, species, class, background; "none" where nothing is picked), then the choice and spell pickers, then "Create and save", Back and Cancel. The review appears only when creating, not for a level-up or "Make choices".

### The four score methods

| Method | How it works |
| --- | --- |
| Standard array | Hand out 15, 14, 13, 12, 10 and 8, one each, to the six abilities. A value already given shows "(used)" and cannot be picked twice (a stale select that sends a used value is reset). "Assigned: N of 6" counts; Next waits for all six |
| Point buy | Every score starts at 8 and can be set from 8 to 15. You have 27 points. A score costs, from 8 up: 8 costs 0, 9 costs 1, 10 costs 2, 11 costs 3, 12 costs 4, 13 costs 5, 14 costs 7 and 15 costs 9. "Points left: N of 27" counts; going over 27 blocks Next with the reason linked to the button |
| Roll | "Roll six scores (4d6, drop the lowest)" (then "Roll again") asks the service for six rolls of four six-sided dice and keeps the three highest of each (the lowest die is dropped). The six totals are then assigned to the abilities, once each. A polite "Six scores rolled." follows. A failed roll is reported as an error and the builder stays open |
| Enter by hand | Six number fields ("Base ability scores"), for scores worked out elsewhere |

Owner check: the SRD 5.2.1 describes these methods; page reference to be confirmed by the owner. The rules above are written in this project's own words and add no SRD text.

### The dice command and what is recorded

Rolling uses `dice.roll` (`4d6`, `keepHighest` 3, label "Ability score roll N"; see [dice-engine.md](dice-engine.md)). It needs no character and writes nothing. The score method and the rolled dice are **not stored** (owner answer 7, no schema change): the character records only its six base scores, like any other, and the Roll method's dice are gone when the builder closes.

### Spell search (D30)

In the choices step each caster's picker has a search by name ([spellcasting.md](spellcasting.md)). A blank or accent-marks-only query shows every spell and announces nothing; while a query filters, the polite status says "N of M spells shown", and adds ", N chosen hidden by the search" when the search hides spells already picked (owner addendum A3).

### Tests

Vitest: `CharacterBuilder.test.tsx` (steps, methods, review). e2e: the `createCharacter(user, { name, family?, scores?, species?, cls?, background?, other? })` helper in `src/Ui/e2e/flow.e2e.tsx` walks the six steps. With `scores` it uses "Enter by hand"; without, it assigns the standard array in order.

## Accessibility (WCAG 2.2 AA, D05)

Radio groups and choice groups are `fieldset`/`legend`. Each step change moves focus to the builder heading (2.4.3). Each step is a named form, the selects and spinbuttons are labelled, Next is `aria-disabled` (never natively disabled, so it cannot lose focus) and `aria-describedby` its reason while it waits, the commit buttons and Cancel are `aria-disabled` while a save is in flight, and the roll is announced politely (checklist row 45). The review list is a labelled region whose values wrap, so it reflows at 400% (1.4.10). The e2e test drives the whole flow by role and accessible name only.

## Not in this slice

- Blocking a level-up that misses a multiclass prerequisite. The engine checks them (content v5, [multiclass-and-attacks.md](multiclass-and-attacks.md)), and the level-up preview and the sheet warn (`restriction.multiclass-unmet`), but saving is allowed. The SRD classes' prerequisite data comes with the SRD content commits.
- Rolled hit points stored per level (overrides for now).
- Equipment in the builder (it is on the sheet, M2 item 4).
- Drafts that survive a restart.
