# Session prep: a character built mostly from homebrew

For players whose class, features or items come from a book that TomeStack does not ship. It tells you what to set up in the Homebrew studio before the session, and what you will still do by hand at the table. Every name below ("Fixture Ward", "Fixture Parry") is invented, so you can follow along without any book.

If the content is in a PDF, start with [From a book to a character](pdf-import.md#from-a-book-to-a-character). Nothing here becomes a rule until you publish it in the studio (ADR-004), and only published content changes a saved character.

## A homebrew class

A class gets its features by granting features that are already published, so make the features first.

1. For each level's feature: "New feature", give it a name and its text, then "Save draft" and "Publish". Reference text is enough; it shows on the sheet.
2. Under "Start from a template" choose "A class skeleton", then "Use template". It opens as an unsaved draft, with a d8 hit die, two starting saving throws, a subclass choice at level 3 and empty Ability Score Improvement slots at levels 4, 8, 12, 16 and 19.
3. Set the name, hit die and saves to the book's.
4. For each level's feature, use "Grant a feature": under "Feature" pick the published feature ("Choose a published feature" is the empty choice), and set "From class level".
5. The Ability Score Improvement slots are empty grants, and an empty grant fails validation ("grants content but names none"), which blocks "Publish". Fill each with a published, reference-only feature, or remove it with "Remove rule N".
6. "Save draft", then "Publish". Then pick the class in the builder, or use "Level up" on the sheet.

## A switched feature: Fixture Ward

Fixture Ward gives +1 AC while it is switched on. The studio has no "add toggle" button; a toggle comes from a template, and that template makes a **feature**, not a feat.

1. "Start from a template", "A stance you switch on and off", "Use template".
2. Name the feature Fixture Ward. In its Toggle rule, set "Toggle name" to Ward.
3. The template also adds a resource and a recovery that Ward does not need. In the Toggle rule, set "Turning it on spends" to "Nothing". Then "Remove rule N" on the recovery, then on the resource.
4. Keep the template's modifier (it already adds 1 to Armor Class and applies "While Ward is on"). Do not add a second one.
5. "Save draft", "Publish".

### Getting it onto a character

- **A new character:** in the builder's Background step, open "Other content" and tick Fixture Ward.
- **An existing character:** put it in a class. In the class's draft, use "Grant a feature" and pick Fixture Ward, then "Save draft" and "Publish". On the character's sheet, open the Manage tab. Under "Updates available", choose "Review update: <class name>", check what would change, then "Apply update".

On the sheet you switch Ward on and off yourself. The AC changes only while it is on.

## A limited reaction: Fixture Parry

Fixture Parry has `max(1, WIS.MOD)` uses. Spending one switches on an AC bonus of `floor(WIS.MOD / 2)`. It starts from the same template, so it is also a feature.

1. "Start from a template", "A stance you switch on and off", "Use template".
2. Name the feature Fixture Parry. Set the resource's "Resource name" to Parry uses and its "Uses" to `max(1, WIS.MOD)`.
3. Set the Toggle rule's "Toggle name" to Parry, and "Turning it on spends" to "One use of Parry uses".
4. Edit the template's modifier: set its "Value" to `floor(WIS.MOD / 2)`. Its "Applies" should read "While Parry is on".
5. Keep the template's recovery (Long rest, all uses) or change "When" or "How many".
6. "Save draft", "Publish".

Add Fixture Parry to a character the same way as Fixture Ward (see "Getting it onto a character" above).

At the table, switch Parry on when you react, and **switch it off by hand** afterwards. TomeStack does not know when a reaction ends.

The formula words are `WIS.MOD`, `floor` and `max`, spelled like that.

## Spells from a book

A caster can use only a spell that names the caster's list. A spell that names none publishes with the warning `validate.spell-lists`, and a caster cannot pick it. "Add to a spell list…" fixes that, in the import review before you accept and in the studio after.

1. Give the class its list: in its spellcasting rule ("Add spellcasting"), set the "Spell list key" to a short key you choose, for example `fixture-list`. (For the SRD casters the keys already exist, for example `wizard`.)
2. Import the book. A spell comes with a list only when the PDF has a "<Name> Spells" list page that names it (the candidate marks that unsure; check it). Otherwise its candidate says it names no class lists.
3. In the import review, tick the spell candidates and choose "Add N spells to a spell list…". Type the key or pick one from the list (it shows how many spells each already has), and confirm. The key is saved in lower case with hyphens, and the form shows it before you confirm. A candidate that was already accepted is left alone and listed with the reason.
4. Accept the spells as drafts ("Accept selected as drafts").
5. In the studio, "Publish all drafts in this source…" and confirm the preview.

A spell that was accepted or published without a list: in the studio, tick it and choose "Add N spells to a spell list…". That saves a new draft of each spell with the key and opens the publish panel on exactly those drafts, all ticked; nothing is published until you confirm. Characters keep the spell revisions they recorded until you review an update for each one.

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
