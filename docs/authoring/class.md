# Writing a class

For homebrew authors. This guide builds one original class, the **Example Lanternkeeper**, in the Homebrew studio, and shows what the studio saves at each step. Every JSON block tagged `tomestack-example` below is read and published by the tests (`AuthoringGuideTests`), so this guide cannot drift from what TomeStack accepts.

What you need: TomeStack, and a rules family to write for (SRD 5.1, the 2014 rules, or SRD 5.2.1, the 2024 rules; a class can support both). Write your own text: TomeStack's guides and tests never copy a book, and a class you want to share must hold only your own work.

## 1. Make a source for it

Everything you write lives in a **homebrew source**. In the studio, give it a title, pick the rules families and choose **Create source**. Leave "Share" off for now; you can mark it as shareable later (see [source-pack.md](source-pack.md)).

A source is just a record: title, publisher, license and whether it may be shared. TomeStack fills in the rest.

## 2. Write the features the class grants

A class grants features at levels. Write each feature first, then publish it, because a class points to **published** content (only published content counts on a character).

In the studio choose **New feature**, name it, write its text, and add effects (**Add modifier**, **Add resource**, **Add roll or action** and so on). **Save draft** keeps it unfinished; **Check** shows what publishing would refuse. The Lanternkeeper's level-2 feature adds its Lantern column to Perception:

```json tomestack-example:feature
{
  "kind": "feature",
  "name": "Example Steady Flame",
  "rulesFamilies": ["srd-5.1", "srd-5.2.1"],
  "summary": "Your lantern steadies your eye: add your Lantern column to Perception. (Original example text.)",
  "effects": [
    { "type": "modifier", "id": "steady-flame-perception", "operation": "bonus", "target": "skill.perception", "value": "SCALE.lantern" }
  ]
}
```

Then choose **Publish**. TomeStack checks it first and refuses it with a list of problems if something is wrong (an unknown field, a formula it cannot read). Publishing makes a new, fixed revision: to change a published feature later, edit it and publish again, and each character that uses it is offered the update.

## 3. Write the class

Choose **New class** (or **Start from a template** and pick the class skeleton). The class editor has:

- **Hit die** (d6, d8, d10 or d12).
- **Columns** (**Add class column**, a `scale`): a number per level from 1 to 20, such as uses or dice. Formulas read a column as `SCALE.<id>` at the character's level in this class.
- **Proficiencies** your starting class gives, and the subsets a multiclass character gets (`onlyAs`).
- **Resources** (uses with a maximum formula) and how rests recover them.
- **The level table**: which feature each level grants (**Grant a feature**), and the choices it offers (such as a subclass at level 3).
- **Spellcasting**, if it casts: the ability, the list, the slots per level and its share as a multiclass caster.
- **Multiclass prerequisites** (a minimum ability score).

The Lanternkeeper, as the studio saves it. `PUBLISHED-FEATURE` is the feature you published in step 2: in the studio you pick it from a list, and the editor fills in its ids.

```json tomestack-example:class
{
  "kind": "class",
  "name": "Example Lanternkeeper",
  "rulesFamilies": ["srd-5.1", "srd-5.2.1"],
  "summary": "A keeper of small lights who spends oil to push back the dark. (Original example text.)",
  "effects": [
    { "type": "hitDie", "id": "lanternkeeper-hit-die", "die": 8 },
    { "type": "scale", "id": "lanternkeeper-lantern-column", "scaleId": "lantern", "label": "Lantern", "values": [1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5] },
    { "type": "grant", "id": "lanternkeeper-save-wis", "grant": "proficiency", "target": "save.wis", "onlyAs": "startingClass" },
    { "type": "grant", "id": "lanternkeeper-save-cha", "grant": "proficiency", "target": "save.cha", "onlyAs": "startingClass" },
    { "type": "resource", "id": "lanternkeeper-oil", "resourceId": "oil", "label": "Oil", "maximum": "1 + SCALE.lantern" },
    { "type": "recovery", "id": "lanternkeeper-oil-long", "timing": "onLongRest", "resourceId": "oil", "on": "longRest", "amount": "all" },
    { "type": "roll", "id": "lanternkeeper-flare", "timing": "onRoll", "rollId": "flare", "label": "Flare", "dice": "1d4", "resourceId": "oil", "cost": "1", "bonus": "SCALE.lantern", "activation": "bonusAction", "text": "Spend 1 oil: 1d4 plus your Lantern column. (Original example text.)" },
    { "type": "restriction", "id": "lanternkeeper-multiclass-wis", "field": "ability.wis.score", "minimum": 13, "multiclass": true, "text": "To multiclass into or out of this class: Wisdom 13." },
    { "type": "grant", "id": "lanternkeeper-steady-flame", "grant": "content", "level": 2, "content": "PUBLISHED-FEATURE" }
  ]
}
```

Choose **Publish**. The class uses a column, so TomeStack publishes it in content schema v9; a class without one publishes in the lowest version that holds it, so older TomeStack versions can still read it.

## 4. Check it before you use it

- **Find problems** (the debugger): finds a feature nothing grants, a resource nothing recovers, a column no formula reads, a formula that cannot be read.
- **Show relationships**: the class, its levels, and what each grants, as a tree.
- **Try it**: builds the draft at a level you choose on a character that is never saved.
- **Show design feedback** (off by default), then **Get design hints**: compares it with the SRD classes of its family.

## 5. Play it

Create a character, pick the class in the builder, and level it. The sheet traces every number to the effect that produced it, so you can see your columns and formulas at work. A character keeps the revision it was built with until you review and accept an update.

## When something is refused

The refusal names the effect and the problem, for example `validate.formula-invalid` for a formula TomeStack cannot read, `validate.requires-v9` for a column in older content, or `validate.reference-missing` for a grant of content that is not published. Fix it in the editor and publish again.

## Related

- [source-pack.md](source-pack.md): share the source with other people.
- [extension.md](extension.md): import or export your content with an extension.
- [features/homebrew-studio.md](../features/homebrew-studio.md) and [ADR-010](../decisions/ADR-010-custom-classes-and-progression.md): the studio and the class model in full.
