# Play mechanics: form groups, overlays and pool costs (D43, slice 1)

LIVING_SPECS D43 · [ADR-018](../decisions/ADR-018-play-mechanics.md) · ADR-003 content schema v11 · status: **slice 1 implemented** (built on `play-mechanics`; ships in the release after 0.6.0).

Slice 1 lets content declare forms: toggles of one group, of which at most one is on, with overlays that sit on top of a form and a cost paid from a shared pool. It builds on the toggles of [m3-effects.md](m3-effects.md). Later slices (a round counter, damage responses, feature attacks) are listed in ADR-018 and are not built.

Rules core: `ToggleEffect`, `ToggleGroupEffect` and `ToggleLayer` in `src/RulesCore/Effects.cs`, `ToggleGroups` in `src/RulesCore/ToggleGroups.cs`, the toggle list in `src/RulesCore/Calculation.cs`, and the checks in `src/RulesCore/ContentValidation.cs`. Service: `toggleOn`/`toggleOff` in `src/AppService/Play.cs`. UI: `FormGroups.tsx` on Play, the toggle and form group controls in `HomebrewStudio.tsx`, and `undo.ts`. Acceptance: `tests/RulesCore.Tests/ToggleGroupTests.cs`, `ToggleGroupValidationTests.cs`, `tests/AppService.Tests/ToggleGroupCommandTests.cs`, and the e2e flow in `src/Ui/e2e/forms.e2e.tsx`. Fixture: `tests/RulesFixtures/fixture-pack-forms.json`.

## Content (schema v11)

| Effect | Field | Type | Does |
| --- | --- | --- | --- |
| `toggleGroup` (new type, v11 only) | `groupId`, `label` | key like a scale id (`[a-z][A-Za-z0-9]{0,31}`), text | Declares a form group. It has no behaviour of its own; the label names the group on Play |
| `toggle` | `group` | the key of a `toggleGroup` | Joins the group |
| `toggle` | `groupContent` | content id | The content that declares the group, when it is not this revision. It may be another source's |
| `toggle` | `layer` | `primary` (default in a group) or `overlay` | A form, or an effect on top of a form |
| `toggle` | `allowedWith` | list of `{ contentId?, toggleId }` | Overlays only: the primaries it may be on with. No `contentId` means the overlay's own content |
| `toggle` | `allowedInBase` | boolean (default false) | Overlays only: it may also be on while no primary of the group is |
| `toggle` | `cost` | formula | The amount of the pool that turning the toggle on spends. Needs `resourceId` |
| `toggle` | `resourceContent` | content id | The content that defines `resourceId`, when it is not this revision |
| `toggle` | `level` | 1 to 20 | The toggle is offered from this level on: the class level for a class's features, otherwise the character level |

Rules:

- At most one primary of a group is on. "No primary on" is the base form.
- Overlays exclude each other only through disjoint `allowedWith` and `allowedInBase`. A combined overlay is its own entry whose `allowedWith` names only its primaries and whose `allowedInBase` is false. There is no general overlay-to-overlay exclusion.
- `groupContent` may name content from another source, so a species toggle can join a class's group. If that content is not on the character, the toggle is a group of one.
- A toggle with `resourceId` and no `cost` spends one use, as in v6. A cost of 0 spends nothing. With `resourceContent`, the use comes from that content's pool.
- `autoWhen` toggles (v10) cannot join a group.
- The cost, the pool and the level are read in play; the cost formula is checked when the content is saved.

Below v11 the keys stay extension data, byte for byte, and do nothing; a `toggleGroup` stays an unknown effect. Content is published at the lowest version it needs (`RequiredSchemaVersion`), so a revision that uses no v11 field keeps its version, bytes and hash.

## Validation

Errors stop a publish; warnings do not.

| Code | Severity | Meaning |
| --- | --- | --- |
| `validate.requires-v11` | error | A v11 field or a `toggleGroup` is used in a revision that declares an older version |
| `validate.toggle-group-incomplete` | error | A `toggleGroup` needs a label and a `groupId` of a lowercase letter followed by up to 31 letters or digits |
| `validate.toggle-group-duplicate` | error | The revision declares the same `groupId` more than once |
| `validate.toggle-group-unknown` | error | The toggle names a group this revision (or the content in `groupContent`) does not declare, or names `groupContent` with no `group` |
| `validate.toggle-group-content-missing` | warning | `groupContent` names content that is not installed; the toggle acts alone until it is |
| `validate.toggle-layer` | error | A `layer` with no `group` |
| `validate.toggle-auto-group` | error | An `autoWhen` toggle has a `group` |
| `validate.toggle-allowed-with` | error | A toggle that is not an overlay has a non-empty `allowedWith` or `allowedInBase: true`, or `allowedWith` names a toggle the content does not define |
| `validate.toggle-allowed-with-content-missing` | warning | `allowedWith` names content that is not installed; the pairing is unchecked until it is |
| `validate.toggle-overlay-unreachable` | warning | An overlay is allowed in no base and with no form, so it can never be on |
| `validate.toggle-cost` | error | A `cost` with no `resourceId` |
| `validate.toggle-resource-content` | error | `resourceContent` names content that defines no such resource |
| `validate.toggle-resource-content-missing` | warning | `resourceContent` names content that is not installed |

A cost that does not parse is the usual `validate.formula-invalid`, and a `level` outside 1 to 20 is `validate.level`. The existing `validate.toggle-resource` (the pool must be in this revision) still applies when no `resourceContent` is named. The packages check adds `pack.reference-outside` when a toggle names other content the pack does not carry, and the relationship tree and the resource debugger follow only a toggle's pool (`resourceContent`). The packages check and import candidates follow the group, the pool and the allowed forms.

## Calculation

The sheet resolves each group from the stored toggles (`play.toggles`, in the order they were switched on):

Calculation diagnostics carry a code and a message, with no severity field. `toggle.group-absent` is informational by intent; the other two say that something stored does not apply.

| Code | When |
| --- | --- |
| `toggle.group-conflict` | Two primaries of one group are stored (a hand-edited or imported character). The last one stored applies |
| `toggle.overlay-not-allowed` | A stored overlay is not allowed with the form that is on now (or with none). It is ignored. It stays stored until the next confirmed change drops it |
| `toggle.group-absent` | The toggle's group content is not on this character, so it is a group of one. The sheet does not list it under "Content not applied", and it belongs to no feature, so it does not make the feature assisted |

`sheet.toggles` carries, for each toggle, `group`, `groupContent`, `groupLabel` (the `toggleGroup` label, for the Play picker), `layer`, `cost`, `costResourceContent`, `available` and `unavailableReason`. A toggle whose group content is not on the character reports no `group`, `groupContent`, `groupLabel` or `layer`: it is a plain toggle. An overlay that is not allowed now is listed with `available: false` and the reason, which names the form it needs by its toggle label ("Needs Tide form."), with the content's name added in brackets when the form is another content's ("Needs Tide form (Fixture Tide Form)."). A toggle below its `level` is not listed. A cost that does not evaluate is `effect.invalid-formula` and the toggle cannot be turned on.

## Play (no new character field)

| Action | Does | Refused with |
| --- | --- | --- |
| `toggleOn { contentId, toggleId, replace? }` | Switches it on and pays the cost, in one confirmed change. A primary with another primary on needs `replace: true`, which turns the old one off. Turning a primary on drops the overlays it does not allow (including base-only ones) | `toggle.not-found`, `toggle.already-on`, `toggle.automatic`, `toggle.group-occupied`, `toggle.overlay-not-allowed`, `resource.insufficient`, `effect.invalid-formula` |
| `toggleOff` | Switches it off. Turning a primary off drops the overlays that need it. Spent uses stay spent | `toggle.not-found`, `toggle.already-off`, `toggle.automatic` |

- **Atomic:** a refusal writes nothing. The new toggle list and the payment are worked out first, and the whole change is refused if the pool cannot pay.
- **The pool** is `(resourceContent, or the toggle's own content; resourceId)`. A cost of 0 spends nothing. A toggle with a resource and no cost spends 1, from `resourceContent` when it is set.
- **No refunds:** turning off or switching forms gives nothing back.
- **Group of one:** a toggle whose group content is not on the character shares the group with nothing, so `toggle.group-occupied` never applies to it.

### Undo

Undo is the client's (`src/Ui/src/undo.ts`), one level, as in [sheet-play.md](sheet-play.md). A costed `toggleOn` regains exactly its cost, from the pool it spent. A form switch with `replace` is compound and has no client undo: the service undoes it (next section). A plain `toggleOn` that dropped overlays has none either.

## Slice 2: stale guard and exact undo (D44, amends D23)

- **What is compound:** `toggleOn` with `replace: true`. Later slices add turns, end encounter and damage with responses (`isCompound` in `src/Ui/src/undo.ts`, `IsCompound` in `Play.cs`). Simple actions are not compound and keep the client inverse.
- **The stale guard:** `character.play` takes `expectedUpdatedAt` (ISO string, the view's `character.updatedAt`). A compound change without it is refused with `play.basis-required`. Any command whose stamp is not the stored one is refused with `character.stale`, the same code and words as `character.save`'s compare-and-set (one concurrency token per character, not a second basis; delta §5.4). The sheet sends the stamp the save queue holds when the change starts. A switch sent twice therefore applies once.
- **The undo:** after a compound change the service keeps one slot for the character: the play state it started from, the `updatedAt` it wrote and a label ("switch to Tide form"). The view carries `undoLabel` while the stored `updatedAt` still matches the slot. `character.undoPlay { characterId, updatedAt, confirm }` puts the play state back exactly (the pool, the forms and the overlays) in one write and returns the view. It does not re-check cost or availability: it is a state restore.
- **Refusals** (nothing is written, and the slot stays): `undo.confirmation-required` (no `confirm`), `character.not-found`, `character.stale` (the page's `updatedAt` is behind the stored one), `undo.nothing` (no slot), `undo.stale` (the slot was written for an earlier stamp, because a save, rest, restore or another play change came after). A write that fails leaves the slot, so the undo can be tried again.
- **One level, in memory:** the next compound change replaces the slot, an undo consumes it, and closing the app forgets it. A library restore or a package import clears every slot, because it can store a character with an older `updatedAt` that would match a slot again. The studio sandbox never sets or shows one.
- **On the sheet:** while the view has an `undoLabel`, the Undo button reads "Undo switch to Tide form" and calls `client.undoPlay` through the sheet's save queue; the client entry from before the switch is withdrawn, so there is one Undo. Errors take the existing path: the service's words are shown, and `character.stale` reloads the current copy.

## On Play and in the studio

- **Play** lists each group under "Forms" as a labelled group of radio choices: "Base" first, then each form with its cost ("5 Focus"), and the overlays as checkboxes. A choice that cannot be made is disabled and says why. See [accessibility-checklist.md](accessibility-checklist.md) row 66 for the keyboard rule: the arrow keys move focus and do not choose, because each choice spends from a pool.
- **Homebrew studio** ([homebrew-studio.md](homebrew-studio.md)): a toggle has Cost (with the formula preview), Form group, Kind (form or overlay), Allowed in base, "Can be on with these forms" and Level. "Add form group" declares a group, and "Add toggle" adds a toggle. Changing a toggle's group clears its allowed forms; removing the pool clears the cost.

## Not yet

- A round counter, durations and upkeep (slice 3), damage responses, feature attacks and action details (later slices; ADR-018).
- A general overlay-to-overlay exclusion.
- A toggle that gives back what it spent when turned off.
