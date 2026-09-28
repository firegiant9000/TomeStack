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

1. **New character:** name, rules family, base scores, then one species, one background and one starting class (radio groups with "None"), plus other content. Every option shows its source, page and families, and the other family's options are listed but disabled (SPEC S-02). Changing the rules family (or picking a campaign that changes it) drops any pick that no longer fits, so nothing stays checked but disabled and is then silently not applied (as in the M1 form; e2e "drops picks that do not fit when the rules family changes…"; M2 review fix). "Next: choices" previews the draft.
2. **Level up** (sheet button): pick the class that gains the level: an existing class, or a new one at level 1 (the multiclass path). Hit points use the fixed value. A rolled total is recorded as an override on the sheet.
3. **Make choices** (on the sheet's "Choices to make"): the draft is the saved character, and only its choices are shown.

The choices step lists every choice the draft offers now, from `sheet.choices`: level-1 choices, a subclass at its level, and choices from chosen content (Primal Knowledge). Each is a group of checkboxes named by its legend ("Barbarian: choose 2 (to do: 0 of 2 chosen)"). Checkboxes beyond the count are disabled. A refused answer (for example `choice.option-already-chosen`) is shown as an error, and the draft is kept.

**Spells (D04, [spellcasting.md](spellcasting.md)):** for each caster the draft has, the choices step adds a picker. It lists the spells on the caster's list up to its highest slot level, grouped by level, with the counts for its level ("0 of 3 cantrips, 0 of 4 prepared spells"). Picks are recorded on the draft and previewed. Spells never appear under "Other content". Nor does content that another revision grants or offers in a choice (`ContentOption.Standalone` false: class features, skill options); it arrives through its class. Superseded revisions are not offered either ([publishing-and-updates.md](publishing-and-updates.md)).

**Unresolved choices are flagged, not blocking.** The step says how many are left, and saving is allowed. The sheet then lists them under "Choices to make" (`choice.unresolved`).

## Accessibility (WCAG 2.2 AA, D05)

Radio groups and choice groups are `fieldset`/`legend`. Each step change moves focus to the builder heading (2.4.3). The e2e test drives the whole flow by role and accessible name only.

## Not in this slice

- Blocking a level-up that misses a multiclass prerequisite. The engine checks them (content v5, [multiclass-and-attacks.md](multiclass-and-attacks.md)), and the level-up preview and the sheet warn (`restriction.multiclass-unmet`), but saving is allowed. The SRD classes' prerequisite data comes with the SRD content commits.
- Rolled hit points stored per level (overrides for now).
- Equipment in the builder (it is on the sheet, M2 item 4).
- Drafts that survive a restart.
