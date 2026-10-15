# Session prep: a character built mostly from homebrew

For players whose class, feats or items come from a book that TomeStack does not ship. It tells you what to set up in the Homebrew studio before the session, and what you will still do by hand at the table. Every name below ("Fixture Ward", "Fixture Parry") is invented, so you can follow along without any book.

Start with the walkthrough in [From a book to a character](pdf-import.md#from-a-book-to-a-character) if the content is in a PDF. Nothing here becomes a rule until you publish it in the studio (ADR-004), and only published content changes a saved character.

## A homebrew class

1. In the studio, pick your source. Under "Start from a template" choose "A class skeleton", then press "Use template". It opens as an unsaved draft, with a d8 hit die, two starting saving throws, a subclass choice at level 3 and Ability Score Improvement slots.
2. Set the name, the hit die and the saves to the book's. Add one feature per level, even if it is only reference text. A level with no feature is easy to miss at the table.
3. "Save draft", then "Publish". The studio checks the class again and tells you what is wrong.
4. In the builder, choose the class, or use "Level up" on the sheet for an existing character.

If the class has a "Spell list key", see Spells from a book below.

## A feat you switch on: Fixture Ward

Fixture Ward gives +1 AC while it is switched on.

1. "New feat". Name it Fixture Ward.
2. "Add modifier": changes Armor Class, "Add (bonus)", value `1`.
3. Add a toggle: use the "A stance you switch on and off" template for a ready-made one, or build the rules by hand. Name it "Ward". Under "Turning it on spends", choose "Nothing".
4. On the modifier, set "Applies" to "While Ward is on".
5. "Publish".

On the sheet, you switch Ward on and off yourself. The AC changes only while it is on.

## A limited reaction: Fixture Parry

Fixture Parry has `max(1, WIS.MOD)` uses. Spending one switches on a bonus to AC of `floor(WIS.MOD / 2)`.

1. "New feat". Name it Fixture Parry.
2. "Add resource": "Resource name" Parry uses, "Uses" `max(1, WIS.MOD)`.
3. Add a toggle named "Parry". Under "Turning it on spends", choose "One use of Parry uses".
4. "Add modifier": changes Armor Class, "Add (bonus)", value `floor(WIS.MOD / 2)`. Set "Applies" to "While Parry is on".
5. "Add recovery" if the uses come back on a rest: "Recovers" Parry uses, "When" Long rest.
6. "Publish".

At the table, switch Parry on when you react, and **switch it off by hand** afterwards. TomeStack does not know when a reaction ends.

The formula words are `WIS.MOD`, `floor` and `max`, spelled like that. The studio tells you if one is wrong.

## Spells from a book

1. Import the book and accept the spells as drafts (see the walkthrough above).
2. Give the class its list. In the class's spellcasting rule, the "Spell list key" is a short name you choose (for example `fixture-list`). Each spell must name the same key on its lists.
3. Do this **before** you publish. A spell that is on no list publishes with the warning `validate.spell-lists`, and casters can then record it only by hand.

Known limit: the studio edits the class's "Spell list key", but it has no field for a spell's own lists yet. A spell that arrives without one stays list-less, and you record it by hand on the sheet's Spells tab.

## Familiars, custom weapons and backstory

TomeStack has no place for these yet. Until later waves land, write them in the sheet's **Notes** tab, in "Session notes": enter a "Session date" and a "Note", then choose "Save session note". Session notes stay on this computer and in your personal backups.

## What is still manual at the table

- Familiars and companions.
- Hand-typed weapons (name, bonus, damage).
- Backstory and personality text.
- Bloodied or other condition-based toggles.
- Reactions (switch the toggle on, then off again).
- Damage resistance.
- Attunement and item charges.
- XP.

Later waves strike lines from this list as they land.

Use the sheet's Report a gap for anything that blocks you; the notes shape what comes next.
