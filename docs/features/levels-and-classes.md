# Levels, classes, hit points and armor class

SPEC C-01, C-02, C-03 · ROADMAP M1 · status: calculation implemented (M1 item 5). The level-up UI is M2 ([builder.md](builder.md)).

`src/RulesCore/Calculation.cs`, tested in `tests/RulesCore.Tests/ClassLevelTests.cs`.

## Class levels (character schema v3)

- `classes: [{ class, level }]` records levels per class, in the order taken. The first entry is the starting class. Each class reference is an active pin, so it does not need repeating in `pins`.
- The **total level** is the sum of the class levels. The service keeps the stored `level` equal to it on save. A character without classes (M0 and M1 fixtures) keeps its stored `level`.
- Validation: each class level is 1–20, the total is at most 20, a class appears once, and `level` must match the sum (`character.level-mismatch`).
- Content recorded as a class that is not class content gives no levels (`character.class-not-a-class`). A class pinned without levels gets `character.class-without-levels`: grants without a `level` still apply, but its level features and hit points do not.

## Levels in content (content schema v3)

- `grant.level`: the grant applies from that level. That is the **class level** for class content and for content that belongs to a class (granted by it, or later chosen from it), and the **character level** for anything else. Below that level, the grant simply does not apply; it is not an error.
- `CLASS_LEVEL` in a formula is the level in the class the content belongs to. Content outside a class cannot use it: the effect is disabled with `formula.value-unavailable`, and the rest of the sheet still calculates.
- `LEVEL` is the total character level.
- A content revision that is pinned directly belongs to no class, even if a class also grants it (the pin is admitted first).

## Class columns (content schema v9, M5)

A class or subclass can name a per-level column with a `scale` effect: `{ scaleId, label, values }`, where `values` holds 20 integers, one for each class level ([ADR-010](../decisions/ADR-010-custom-classes-and-progression.md)).

- Any formula of content that belongs to the class reads it as `SCALE.<scaleId>` at the class's level. The rule is the same as for `CLASS_LEVEL`: the class itself, its chosen subclass, and what they grant. Outside a class it is unavailable (`formula.value-unavailable`), and only that effect is disabled.
- A subclass's column is indexed by the class level. It applies only once the subclass is chosen.
- A class and its subclasses share one set of ids. Validation refuses a duplicate it can see (`validate.scale-duplicate`). At calculation the class's column wins (`scale.duplicate`).
- The sheet lists each column with its current value (`sheet.scales`).
- `SCALE` is read only in a v9 revision. In an older one it is an unknown identifier, as in older builds.

The first user is the original **Test Chronicler** fixture (`tests/RulesFixtures/fixture-pack-m5-chronicler.json`). Its Ink column sets a resource, a short-rest recovery and a feature's initiative bonus, and its Lore column sets a roll bonus, a skill and the prepared-spell count. Since M5 slice 1b the studio authors classes, columns included ([homebrew-studio.md](homebrew-studio.md#a-class-of-your-own-m5-slice-1b-adr-010)). The sheet shows the columns under "Class columns".

## Hit points (`hitPoints`)

The SRD rule: at level 1, the starting class's hit die maximum. At every other class level, the fixed value (half the die plus 1; for example 7 for a d12). Add the Constitution modifier once per character level. The class declares its die with a `hitDie` effect (d6, d8, d10 or d12).

The trace has one step for the starting class at level 1, one per class for the remaining levels (citing the class revision, source and page), and one rules step for Constitution × level. Content can add to it, for example `modifier bonus hitPoints value "LEVEL"`.

Rolled hit points are recorded as an **override** (SPEC C-06) until the level-up UI stores rolls (M2). Without a class, hit points are 0, `assisted`, with `hit-points.no-class`. A class without a hit die gets `class.hit-die-missing`.

## Armor class (`armorClass`)

The base is 10 + Dexterity modifier. Alternatives such as Unarmored Defense are `replace` effects (`10 + DEX.MOD + CON.MOD`). The highest replacement wins, and the others are traced as not used (ADR-003). Since M2 item 4, worn armor replaces the base, alternatives do not apply while armor is worn, and a shield adds its bonus ([equipment.md](equipment.md)).

## Skills

All 18 skills of both SRDs are fields (`skill.<key>`: `acrobatics`, `animalHandling`, `arcana`, `athletics`, `deception`, `history`, `insight`, `intimidation`, `investigation`, `medicine`, `nature`, `perception`, `performance`, `persuasion`, `religion`, `sleightOfHand`, `stealth`, `survival`). Each is its ability modifier plus the proficiency bonus when proficient (doubled for expertise). The sheet now has 40 fields. Since 2026-10-06 (D24) it also has `passive.perception`, `passive.insight`, `passive.investigation` (10 + the skill) and `speed` (feet; 30 by rules policy unless a homebrew species sets one): calculated; overridable on Stats; content can target them only from content schema v10 (validation refuses it below that). A save or skill carries a proficiency mark (none, proficient, expertise).

## Experience points (Wave 3 item 27, R162, R177)

XP is optional and off by default: the character's `xp` (character schema v9, `null` = not tracked, 0 to 1,000,000) is the on/off switch, so "Track XP" on Stats sets it to 0 and unticking it drops the total. The thresholds are one shared rules constant, `Advancement.XpThresholds` in `src/RulesCore/Advancement.cs`, indexed by level - 1: 0, 300, 900, 2,700, 6,500, 14,000, 23,000, 34,000, 48,000, 64,000, 85,000, 100,000, 120,000, 140,000, 165,000, 195,000, 225,000, 265,000, 305,000, 355,000. Like the proficiency bonus by total level, it is not a `RulesFamilyPolicy` field: a policy field encodes a difference, and the two bundled families print the same table. If a family is ever found to differ, the table becomes a policy field then.

The sheet gains two optional values, both absent unless `xp` is set: `xpForNextLevel` (the threshold for the level after the character's total level, absent at level 20 and with no level yet) and `levelUpAvailable` (true when `xp` has reached it). They are judged by the **total** character level, so a multiclass character uses the sum of its class levels. "Level up available" is display only: it never changes the level or the classes, and milestone levelling is untouched. Stats shows it as plain text beside the field, next to "Next level at 2,700".

## Importing a class from a PDF (item 11, D34, ADR-016)

A detected class page becomes a class candidate with proposals. The table (level, proficiency bonus, features, and for a caster cantrips, spells known or prepared and slot columns, or a pact table) is rebuilt from the page's positioned lines. It becomes the class's hit die, saving throws (`save.*` grants), armor and weapon proficiencies, a subclass choice with no options, and, for a caster, a `spellcasting` effect whose spell list key is the class name (marked unsure; saving the candidate settles it). Skills are shown as read, with an uncertainty, and not proposed (R69). The multiclass caster fields are never proposed.

Each proposed effect has its own confirm tick. Accepting as a draft needs the hit die confirmed and, when there is one, the slot table; only confirmed effects stay `automatic`. The features the table names are written as drafts first in the same step and the class pins them with one content grant each, at the lowest level the table gives. These grants are automatic because the calculator applies a content grant only when it is; the features' own effects stay reference. A feature the table names that was not found as its own entry becomes a reference stub citing the class page. A name that matches exactly one installed published feature in the class's families is not stubbed; the class pins that feature (R67). A name with several installed matches (for example "Spellcasting" with the SRD installed) is stubbed. An ignored linked feature is not granted (R66); a feature reviewed in an earlier import of the same PDF is counted in an uncertainty instead of linked (R68). Details: [pdf-import.md](pdf-import.md#classes-from-a-book-item-11-d34-adr-016). Nothing is active until the class and its features are published, which can be done together.

## Not in scope (M2+)

Spellcasting (D04 in LIVING_SPECS), multiclass prerequisites and multiclass proficiency rules (the data model supports several classes; the SRD multiclass tables are M2 builder work), rolled hit points, and speed data in the bundled species (they carry none, so speed is 30 feet by rules policy; a homebrew species can set one from content v10). Equipment-based armor class is M2 item 4.
