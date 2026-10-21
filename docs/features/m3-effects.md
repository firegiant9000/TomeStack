# M3 B2: toggled effects, shared resources and variable costs

ROADMAP M3 ("complex resource/action mechanics") · SPEC I-05, C-05 · ADR-003 content schema v6 · status: **implemented**.

These are the mechanics the M3 task named as likely Stardust Guardian gaps: a resource shared between features, an action that spends a variable amount, and conditional or toggled effects (`whileActive`). B1 found no concrete gaps yet, because the owner's material is not on this machine. When it runs, `report.md` lists any other effect types as "unsupported", and they get the same treatment.

Rules core: `ToggleEffect`, `ModifierEffect.Toggle`, and `RollEffect.ResourceContent`/`Cost`/`VariableCost` in `src/RulesCore/Effects.cs`. Service: `toggleOn`/`toggleOff` in `src/AppService/Play.cs`. UI: "Active effects" in "Attacks and actions", and the "Spend" control in the roll record (`src/Ui/src/components/PlayPanels.tsx`). Acceptance: `tests/RulesCore.Tests/ToggleAndCostTests.cs`, `tests/AppService.Tests/ToggleCommandTests.cs`, and the e2e test "switches a toggled effect on and off…". Fixtures: `tests/RulesFixtures/fixture-pack-m3-effects.json` ("Fixture Radiant Stance", "Fixture Borrowed Spark").

## Content (schema v6)

| Effect | Fields | Does |
| --- | --- | --- |
| `toggle` (new type, v6 only) | `toggleId`, `label`, optional `resourceId` | Something switched on and off at the table (a stance, an aura, a transformation). Turning it on spends one use of `resourceId`, which must be a resource of the same revision |
| `modifier` | `toggle` (with `timing: whileActive`) | Applies while that toggle of its revision is on. While it is off, it does not apply, and the field stays **automatic**, because the rules decide. A `whileActive` modifier without a toggle stays assisted, as before |
| `roll` | `resourceContent` | The content id that defines `resourceId`: one pool several features spend |
| `roll` | `cost` (formula, default 1), `variableCost` | Uses the action spends. With `variableCost`, the player picks 1 to `cost` (or to what is left) |

Validation:

- `validate.toggle-unknown` and `validate.toggle-timing`: a modifier's toggle must be defined in the revision, and its timing must be `whileActive`.
- `validate.toggle-resource`: a toggle's resource must be defined in the revision.
- `validate.toggle-auto-resource` (content v10, D38): a toggle with `autoWhen` cannot name a `resourceId`, so an auto toggle never spends a use.
- `validate.toggle-auto-target` (v10, D38): a `whileActive` modifier of a toggle with `autoWhen` cannot target `hitPoints` or any `ability.*` field (score or modifier), so the toggle cannot move the line Bloodied is judged by. Both are errors. The check looks at direct targets only: a modifier on some other field that is itself worked out from hit points or an ability is not caught.
- `validate.roll-cost`: a cost needs a resource.
- `validate.roll-resource-content`: the shared resource must exist in its content.
- `validate.requires-v6`: any of these below v6.

Like `armor` (v4) and the v5 types, `toggle` is typed only in a v6 revision. In an older one it stays unknown and byte for byte (ADR-003). Every new field on an existing type is nullable and absent by default, so no existing revision changes.

## Play (character schema v7)

`play.toggles: [{ contentId, toggleId }]`. v1–v6 characters are upcast with every toggle off, and there is no database migration.

| Action | Does | Refused with |
| --- | --- | --- |
| `toggleOn { contentId, toggleId }` | Switches it on. If it names a resource, one use is spent **in the same confirmed change**, so the whole change is refused when no use is left | `toggle.not-found`, `toggle.already-on`, `resource.insufficient` |
| `toggleOff` | Switches it off (spent uses stay spent) | `toggle.already-off` |

- **Auto toggles (content v10, D38):** a toggle with `autoWhen: "bloodied"` is on exactly while the character is bloodied (hit point maximum at least 1, current hit points at most half the maximum rounded down; temporary hit points do not count). The calculator decides it from a first pass without auto toggles, so the maximum is the one before any toggle and an auto toggle cannot feed back into it; only when the character is bloodied and has one is the sheet calculated a second time with those toggles on. `sheet.bloodied` is the flag, and `sheet.toggles[].auto` marks the toggle. It is never stored: a `play.toggles` entry naming it is ignored, `toggleOn` and `toggleOff` of it are refused with `toggle.automatic` (nothing is written, no use spent), and Undo of damage needs no toggle step. Below v10 `autoWhen` is extension data and the toggle is an ordinary manual one. Same in `srd-5.1` and `srd-5.2.1` (a display rule, no policy field).
- **Damage grants (content v10, Wave 3 item 21):** `damageResistance`, `damageImmunity` and `damageVulnerability` grants (with `damageType`) are now computed: they feed `sheet.damageDefences` and typed damage (see [sheet-play.md](sheet-play.md), "Damage types"). They honour `level` and `onlyAs` like other grants, must be automatic and `always` to be applied (a conditional one is listed as assisted), and `damageType` is compared in any case. The mechanics inventory reports them as automatic exactly when they are applied.
- **Long rest:** every active toggle is proposed off (`toggle:<content>:<id>`). The player can untick one to keep it. Auto toggles are left out.
- **Rolls:** the record names the resource's defining content (`linkedResourceContent`), so "Spend" finds a shared pool. It spends `cost`, or offers "<resource> to spend (1 to N)" for a variable cost. Rolling still spends nothing by itself.
- `sheet.toggles` lists each toggle with its state, and the feature effects carry `cost`, `variableCost` and `resourceContent`.

## The `item` effect (content v10, Wave 3 item 25)

`{ type: "item", id, requiresAttunement?, charges?: { max: 1 to 99, recovery?: { period: shortRest | longRest, regain?: dice } } }`, on an item revision only, at most one per revision (`validate.item-*`, `validate.requires-v10`). In a revision below v10 it is an unknown effect: the item's other effects apply when equipped, as before. It is computed now:

- `requiresAttunement`: the item's effects apply only while an equipped entry is attuned (`item.not-attuned` otherwise). The same in `srd-5.1` and `srd-5.2.1`; the three-item limit is a text warning on Inventory, not a policy field.
- `charges`: `max` is the per-entry maximum; `chargesSpent` on the entry is the play state. `recovery.regain` omitted means all charges back on that rest; dice mean the player rolls them (see [rests.md](rests.md)).
- `sheet.itemUses` lists each entry of an item with attunement or charges (carried or equipped): `entryKey`, `entryId`, `entryIndex`, `equipped`, `requiresAttunement`, `attuned`, `chargesMax`, `chargesSpent`, `chargesLeft`, `recovery`. See [equipment.md](equipment.md) for ids, second copies and the play and save paths. The mechanics inventory reports the `item` effect as automatic, since it is applied.

## Not yet

- A toggle that lasts a set time (rounds, minutes).
- Toggles that turn each other off.
- Toggled effects other than modifiers, such as grants or proficiencies.
- A cost paid in something other than a resource, such as hit points or spell slots.

These wait for a concrete need from the Stardust Guardian run.
