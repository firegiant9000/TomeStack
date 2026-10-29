# License review: bundled SRD packs (M1 slice)

**Reviewed:** 2026-09-26 · **Route:** CC-BY-4.0 for both SRDs (owner, ADR-007) · **Attribution:** approved in [srd-attribution-draft.md](srd-attribution-draft.md) · **Packs:** `src/AppService/Content/srd-5.1.json`, `src/AppService/Content/srd-5.2.1.json` (schema `docs/schemas/content-pack.v1.schema.json`)

## Documents checked

| SRD | File | SHA-256 (matches the recorded hash) |
| --- | --- | --- |
| 5.1 | `SRD_CC_v5.1.pdf` | `2504d2a0abb0a4d491a939be4f17910a2dde0312570ab8d208080225ccf0a1f0` |
| 5.2.1 | `SRD_CC_v5.2.1.pdf` | `8974902d109d6e63672d7c490bde9ccf052410503d9cfa768237154fbc5e3d87` |

All text in the packs comes from these two files, nowhere else. Each source record stores the PDF hash in `sha256` (SPEC S-01). The printed page numbers equal the PDF page numbers in both files, and every revision cites its page or range in `provenance.page`.

## What was taken

| Pack | Content (kind, page) |
| --- | --- |
| SRD 5.1 | Half-Orc (species, p. 7); Acolyte (background, pp. 60–61) and its feature Shelter of the Faithful (p. 61); Barbarian levels 1–3 (class, pp. 8–9; levels 4–20 in the C2-1 section below): Rage, Unarmored Defense, Reckless Attack, Danger Sense, and the six skill options; Path of the Berserker (subclass, p. 9) with Frenzy; Grappler (feat, p. 75) |
| SRD 5.2.1 | Dwarf (species, p. 84); Soldier (background, p. 83) with its seven ability score options (from the background rules on p. 83); Savage Attacker (origin feat, p. 87); Barbarian levels 1–3 (class, pp. 28–30): Rage, Unarmored Defense, Weapon Mastery, Danger Sense, Reckless Attack, Primal Knowledge, and the six skill options; Path of the Berserker (subclass, p. 30) with Frenzy |

Since 2026-10-27 (C2-1) the scope also includes `srd-5.2.1-barbarian.json` and `srd-5.1-barbarian.json`, the Barbarian levels 1–20 with the Path of the Berserker (section "C2-1 extension" below); the rows above describe the first slice. Since 2026-10-27 (C2-2) it also includes `srd-5.2.1-rogue.json` and `srd-5.1-rogue.json`, the Rogue levels 1–20 with the Thief (section "C2-2 extension" below).

## M2 extension: spells (2026-09-27, owner decision: the full SRD casters, levels 1–20, both families)

**Documents:** the same two PDFs. They were re-downloaded on 2026-09-27 from the official SRD links, and their SHA-256 hashes match the table above.

| Pack file | Content | Pages |
| --- | --- | --- |
| `src/AppService/Content/srd-5.1-spells.json` | All 319 spells of SRD 5.1 "Spells" (the descriptions, pp. 114–194), each on the class spell lists of pp. 105–113 | per spell |
| `src/AppService/Content/srd-5.2.1-spells.json` | All 339 spells of SRD 5.2.1 "Spells" (the descriptions, pp. 107–175), each with the classes printed on its level line | per spell |

Each spell is one revision (kind `spell`, content schema v5) with its level, school, casting time, range, components, duration, concentration, ritual, class lists, the attack or saving throw it asks for, the base damage or healing dice where the text states one, and the full description in the `spell` effect's text. Every revision cites its page or page range. Each file carries the family's source record unchanged (`SrdPackTests.A_familys_packs_carry_the_identical_source_record`), with the same attribution and modification notice.

**Method (the M1 packs were excerpted by hand; 658 spells are not practical that way):**

- Scripts extracted the text from the hash-checked PDFs: pypdf, with two-column reading order rebuilt. They parsed the fields, and they flag rather than guess. The scripts and their reports stay outside the repository. What they changed:
  - joined line breaks and repaired hyphenation, keeping real compound hyphens such as "10-foot-by-20-foot";
  - collapsed repeated whitespace;
  - separated paragraphs with a blank line;
  - removed a space the italics left before punctuation ("Detect Magic .").
- **Repairs of extraction faults**, each checked against the PDF:
  - 5.1: "Guardsand Wards" → "Guards and Wards".
  - 5.1 p. 176: Sacred Flame "Flame -like" → "Flame-like". Found by a scan for spaced hyphens, the only one in either pack.
  - 5.2.1: the small-caps "Acid SplASh" → "Acid Splash".
  - 5.2.1: two places where the dump interleaved columns, pp. 109–110 (Animate Objects to Antipathy/Sympathy) and pp. 130–131 (Find Steed, Find the Path). The misplaced stat blocks and sentences were moved back to their spells.
- **Kept as printed:**
  - the stat blocks and roll tables inside spell text (Animate Objects, Find Steed, Confusion, Reincarnate, Teleport), as plain text lines, with the stat-block ability headings in the PDF's small-caps casing;
  - "Component:" where the PDF prints the singular.
- **The summary line** of each spell is rebuilt from its data in each SRD's own style ("3rd-level evocation (ritual)" for 5.1; "Level 3 Evocation (Sorcerer, Wizard)" for 5.2.1).
- **Structured fields beyond the text:**
  - `attack`, `save` and `dice` are derived from the text. `dice` is the base-level dice only; scaling stays in the text.
  - Class lists come from the SRD 5.1 lists and from the 5.2.1 level lines.
- **Checks:**
  - Counts and unique names (`SrdPackTests.The_spell_packs_have_every_SRD_spell_once_with_its_data`).
  - Every 5.1 list entry has a description and every description is on a list.
  - Every revision validates.
  - Spot checks against the PDFs: Acid Arrow, Fireball, Cure Wounds, Eldritch Blast, Shield, Magic Missile, Sacred Flame, Ice Storm and Guiding Bolt, plus 10 random spells per family in the extraction reports.
- **Residual risk:** a whitespace or line-join error in some of the 658 texts is possible. It would be a typographic error, not a changed rule. Report one with the spell and page, and a corrected revision replaces it (revisions are insert-only).

## M2 extension: the eight spellcasting classes, levels 1–20 (2026-09-27)

| Pack file | Content | Pages |
| --- | --- | --- |
| `srd-5.1-classes.json` | Bard, Cleric, Druid, Paladin, Ranger, Sorcerer, Warlock and Wizard: proficiencies, every class feature of levels 1–20, the SRD subclass of each (College of Lore, Life Domain, Circle of the Land, Oath of Devotion, Hunter, Draconic Bloodline, The Fiend, School of Evocation), and the multiclassing prerequisites and proficiencies | pp. 11–55; multiclassing pp. 56–57 |
| `srd-5.2.1-classes.json` | The same eight classes with their Core Traits, every class feature of levels 1–20, the SRD subclass of each (College of Lore, Life Domain, Circle of the Land, Oath of Devotion, Hunter, Draconic Sorcery, Fiend Patron, Evoker), and "As a Multiclass Character" | pp. 31–82; multiclassing pp. 24–25 |

- **Method:**
  - Scripts extracted the feature and subclass text from the hash-checked PDFs, verbatim apart from whitespace, hyphenation and column repair, and a space the italics left before punctuation.
  - **Every number in the class tables was parsed a second time**, independently, by a script that reads each row's cells from the end of the row. It was diffed against the first extraction. The diff found two errors in the 5.2.1 transcription, both corrected:
    - the Druid's cantrips (it had read the Wild Shape column);
    - the Warlock's cantrips at level 10 (4, not 3).
  - For 5.1 the two parses agree on every cell. Checks: `SrdCasterTests` (full casters reach 9th-level slots, half casters 5th, Pact Magic 4 slots of 5th level; 5.2.1 Paladins and Rangers have 2 slots at level 1).
- **Structure:**
  - Each class is one revision: hit die, saving throws and the skill choice for the starting class only, weapon proficiencies split by the multiclass subset, the multiclass prerequisites, a level-gated grant of each feature, and the subclass choice.
  - The Spellcasting (or Pact Magic) feature carries the `spellcasting` effect with the tables, so it starts at its level (2nd for 2014 Paladins and Rangers).
  - Each feature is a revision with its verbatim text. Most are reference only.
  - A checked set has resources:
    - 5.1: Bardic Inspiration, Channel Divinity, Wild Shape, Divine Sense, Lay on Hands, Cleansing Touch, Sorcery Points and Arcane Recovery;
    - 5.2.1: Bardic Inspiration, Channel Divinity, Wild Shape, Lay On Hands, Sorcery Points and Arcane Recovery.
    
    Each maximum follows the class table or the feature text, and each resource effect's text says which (for example "2 at levels 2-5, 3 at 6-17, 4 at 18-20").
  - Weapon keys follow the weapon table ("rapiers" → `rapier`).
- **Known limitations of the text:**
  - Some sub-headings inside long features (for example Pact Magic's "Spells Known of 1st Level and Higher") run into the following sentence.
  - Tables inside features (domain and oath spells, Creating Spell Slots, the Circle of the Land terrains) are kept as plain text lines.
  - Always-prepared subclass spells are text only: the player records them, and the sheet may flag them "not on the list".
- **Not bundled:** the non-spellcasting classes other than the M1 Barbarian slice (Fighter, Monk, Rogue) and Barbarian levels 4–20, backgrounds, species and feats beyond the M1 slice, armor, and magic items. These were not in the owner's scope ("the full SRD casters"). **Next (ROADMAP M2.2, owner direction 2026-09-28):** the Fighter with the Champion, the Fighting Styles, and the armor and shield table, in both SRDs. This review gets a Fighter section, with pages and checks like the casters', before any of that text is added (SPEC Q-03).

## M2 extension: weapons and the Barbarian's multiclass data (2026-09-27)

| Pack file | Content | Pages |
| --- | --- | --- |
| `srd-5.1-equipment.json` | The 37 weapons of the SRD 5.1 Weapons table: category, damage, properties, weight and cost | p. 66 |
| `srd-5.2.1-equipment.json` | The 38 weapons of the SRD 5.2.1 Weapons table, with the mastery property's name | p. 91 |
| `srd-5.1-classes.json`, `srd-5.2.1-classes.json` | A content v5 revision of the Barbarian (the same content id as the M1 revision) with its multiclass prerequisite and proficiencies, and its weapon proficiencies | 5.1 pp. 56–57; 5.2.1 pp. 24–25 and 28 |

- **Method:** the weapon rows were transcribed from the tables and checked row by row. Each weapon's summary restates its printed row. The `weapon` effect encodes the damage, properties, range and key, and the mastery property's name as text; the mastery rules are not bundled.
- **Text only:** the net (5.1: no damage) and the blowgun (a flat 1 piercing) have no weapon effect, because their damage is not a dice roll. The player adds the attack by hand.
- **The Barbarian's new revision** keeps every M1 effect and adds the D04 data:
  - `onlyAs: startingClass` on its saving throws and skill choice;
  - weapon proficiency grants;
  - the multiclass prerequisite (Strength 13, both families).
  
  2014 grants simple and martial weapons either way (the Multiclassing Proficiencies table, p. 57). 2024 grants simple weapons only as the starting class ("As a Multiclass Character: … proficiency with Martial weapons, and training with Shields", p. 28). The quoted sentences sit in the effect texts. The M1 revision stays for the characters that pin it (revisions are insert-only). Pickers offer the newest revision (`ContentOption.Superseded`).

Differences shown as content (side-by-side tests in `SrdPackTests`): the trident deals 1d6 (versatile 1d8) in 5.1 and 1d8 (versatile 1d10) in 5.2.1, and a later-class Barbarian is proficient with simple weapons only under 2014 rules.

A 2014/2024 difference the spells show as content: Cure Wounds heals 1d8 (5.1 p. 132) or 2d8 (5.2.1 p. 121), in two separate revisions (`SrdPackTests.The_same_spell_differs_by_family_as_content_side_by_side`).

## M3 extension: multiclass spellcasting (2026-09-28, M3 C3)

**Documents:** the same two PDFs. Their SHA-256 hashes were checked again on 2026-09-28 and match the table above.

| Where | What was taken | Pages |
| --- | --- | --- |
| `srd-5.1-classes.json`, `srd-5.2.1-classes.json` | A content v7 revision of each slot caster's Spellcasting feature, identical to its v5 revision except `multiclassCaster` (Bard, Cleric, Druid, Sorcerer, Wizard: `full`; Paladin, Ranger: `half`), and a new revision of each of those seven classes that pins it. No new text | 5.1 p. 58; 5.2.1 p. 25 |
| `src/RulesCore/RulesFamilies.cs` (`RulesFamilyPolicy`) | The numbers of the Multiclass Spellcaster table (20 rows × 9 spell levels), and the rounding of half caster levels: down (5.1), up (5.2.1). These are game numbers, not rules text, like the other policy fields (for example `HitDieHealingMinimum`) | 5.1 p. 58; 5.2.1 pp. 25–26 |

- **Method:** the table was parsed twice from each PDF, independently: pypdf plain text read as a token sequence, and a reading of the layout-mode page by eye. Both parses of both SRDs agree cell for cell. They also equal the bundled SRD Wizard tables of both families, which had been parsed twice for M2. The class lists (full: Bard, Cleric, Druid, Sorcerer, Wizard; half: Paladin, Ranger) and the rounding are from the "Spell Slots" paragraph of each SRD's multiclassing rules.
- **Warlock:** not combined. Its Pact Magic stays separate in both SRDs, and casting Pact slots for other classes' spells stays a manual step.
- **Third casters:** neither SRD has one (the classes and subclasses that would be are not in the SRDs). `third` exists for homebrew, and its rounding is TomeStack's choice.
- **Checks:** `SrdCasterTests.A_Sorcerer_Paladin_combines_slots_on_the_multiclass_table_differently_per_family_side_by_side` and `Pact_Magic_stays_separate_from_a_single_casters_own_table`, and `SchemaTests` for the v7 revisions.

## M2.2 extension: the Fighter and the armor table (2026-09-28, owner direction)

**Documents:** the same two PDFs, downloaded again on 2026-09-28 from the official SRD links (SRD 5.1 from media.wizards.com, SRD 5.2.1 from media.dndbeyond.com). Their SHA-256 hashes match the table above. The files stay outside the repository.

**Scope reviewed and approved for bundling** (CC-BY-4.0, the same attribution and modification notice as the other packs; nothing outside these pages):

| Pack file | Content | Pages |
| --- | --- | --- |
| `srd-5.1-fighter.json` | The Fighter: hit points, proficiencies, every class feature of levels 1–20 (Fighting Style with its six options, Second Wind, Action Surge, Martial Archetype, Ability Score Improvement, Extra Attack, Indomitable), the Champion archetype (Improved Critical, Remarkable Athlete, Additional Fighting Style, Superior Critical, Survivor), the multiclass prerequisite and the multiclass proficiencies. The starting equipment is left out | pp. 24–25; multiclassing pp. 56–57 |
| `srd-5.2.1-fighter.json` | The Fighter: Core Fighter Traits, every class feature of levels 1–20 (Fighting Style, Second Wind, Weapon Mastery, Action Surge, Tactical Mind, Fighter Subclass, Ability Score Improvement, Extra Attack, Tactical Shift, Indomitable, Tactical Master, Two Extra Attacks, Studied Attacks, Epic Boon, Three Extra Attacks), the Champion subclass (Improved Critical, Remarkable Athlete, Additional Fighting Style, Heroic Warrior, Superior Critical, Survivor), and the four Fighting Style feats (Archery, Defense, Great Weapon Fighting, Two-Weapon Fighting). The starting equipment and the Epic Boon feats are left out | pp. 47–49; feats pp. 87–88; multiclassing pp. 24–25 |
| `srd-5.1-armor.json`, `srd-5.2.1-armor.json` | The 12 armors and the shield of each Armor table: category, Armor Class, Strength, Stealth, weight and cost. The armor rules (proficiency or training, Strength, Stealth, one shield at a time) are quoted in the effect texts. Armor descriptions (5.1 "Padded. Padded armor consists of…") are left out | 5.1 pp. 62–64; 5.2.1 p. 92 |

- **Method:** as for the casters. The text comes from pypdf, in layout mode for 5.1 and plain mode for 5.2.1 (each mode splits words in the other SRD). Line breaks and hyphenation are repaired, and every sentence is checked against the page. The armor rows and the Fighter Features table (Second Wind and Weapon Mastery columns) are read twice, once from each extraction mode, and compared cell by cell.
- **Encoded as effects:**
  - hit die, saving throws, skill choices and weapon and armor proficiencies;
  - the multiclass prerequisite (Strength 13 **or** Dexterity 13, a restriction group) and the multiclass subset (heavy armor only as the starting class, in both SRDs);
  - resources and recoveries: Second Wind (5.1: once per short rest; 5.2.1: 2/3/4 uses at levels 1/4/10, one back on a short rest); Action Surge (1, 2 at 17); Indomitable (1/2/3 at 9/13/17);
  - the attack count (Extra Attack, highest wins: "the features don't add together", 5.1 p. 57; "don't stack", 5.2.1 p. 25);
  - the critical range (Improved and Superior Critical);
  - the Defense style's +1 while armored, and the armor table.
  
  Everything else is reference text with its page: Fighting Styles other than Defense, Tactical Mind's reroll, Remarkable Athlete, Heroic Warrior, Survivor, ASIs and Weapon Mastery. The player applies those by hand.
- **Not taken:** Epic Boon feats (Boon of Combat Prowess is named in the text only; the boons are bundled since 2026-10-26, see the next section), starting equipment, the armor descriptions, magic armor, and the Weapon Mastery property rules (Push, Sap, Slow and the others). Weapon Mastery's chosen kinds are tracked by the player.
- **Checks:** `SrdFighterTests` (both families, levels 1–20, side by side), `SrdPackTests` (source records, attribution, every revision validates, the armor numbers), and `SchemaTests`.
- **Done 2026-09-28:**
  - `srd-5.1-fighter.json` has 28 revisions: the class, 7 features, the Champion with 5 features, 6 Fighting Style options and 8 skill options.
  - `srd-5.2.1-fighter.json` has 36: the class, 15 features, the Champion with 6 features, 4 Fighting Style feats and 9 skill options.
  - Each `srd-<family>-armor.json` has 13 items.
  - The generating script's armor-row check found every row in both extraction modes of each PDF. For 5.2.1 layout mode, the comparison ignores the spaces it inserts inside words. The script also checked the Second Wind and Weapon Mastery columns of the 5.2.1 Fighter Features table against the plain extraction. The script stays outside the repository, like the others.
- **Also added:** a content v8 revision of the Paladin's and the Ranger's Extra Attack in both families (the same text, now counted as 2 attacks), and a new class revision that grants it. No new text. The earlier revisions stay for the characters that pin them.
- **Armor training (PR #12 review fix):** further v8 Paladin and Ranger class revisions (`5xe9…`, identical to the first v8 ones otherwise) carry `armor.*` grants (Paladin: light, medium, shield, and heavy as the starting class only; Ranger: light, medium, shield), and new v8 Wizard and Sorcerer class revisions carry `armor.none`. The grant texts repeat each class's own proficiency line and multiclassing proficiencies already quoted in these packs (5.1 pp. 30, 35, 42, 52 and p. 57; 5.2.1 the Core Traits tables and multiclassing lines). No new SRD text beyond that restatement.

## SRD 5.2.1 species, backgrounds and feats (2026-10-26, Track 1 under D60)

**Document:** `SRD_CC_v5.2.1.pdf`, downloaded again on 2026-10-26 from media.dndbeyond.com. Its SHA-256 matches the table above. The file stays outside the repository.

**Scope reviewed and approved for bundling** (CC-BY-4.0, the same source record, attribution and modification notice as the other 5.2.1 packs; nothing outside these pages):

| Pack file | Content | Pages |
| --- | --- | --- |
| `srd-5.2.1-origins.json` | Backgrounds Acolyte, Criminal and Sage: ability scores (seven options each, from the background rules already quoted for the Soldier), feat, skill proficiencies, tool proficiency and equipment lines | p. 83 |
| `srd-5.2.1-origins.json` | Species Dragonborn (with the Draconic Ancestors table), Elf (with the Elven Lineages table), Gnome, Goliath, Halfling, Human, Orc and Tiefling (with the Fiendish Legacies table): creature type, size, speed and every special trait | pp. 84–86 |
| `srd-5.2.1-feats.json` | Origin feats Alert, Magic Initiate and Skilled; General feats Ability Score Improvement and Grappler; the seven Epic Boon feats (Combat Prowess, Dimensional Travel, Fate, Irresistible Offense, Spell Recall, the Night Spirit, Truesight) | pp. 87–88 |

With the M1 Dwarf, Soldier and Savage Attacker and the four Fighting Style feats of the Fighter section, SRD 5.2.1 is now bundled with all 9 species, 4 backgrounds and 17 feats it prints. Left out, as for the M1 slice: the introductory "Parts of a Background", "Parts of a Species" and "Parts of a Feat" rules (except the background ability score paragraph, already bundled).

- **Method:** two independent parses of pp. 83–88 with pypdf, plain mode and layout mode, compared by a script that stays outside the repository:
  - **Text:** 345 sentences of the bundled summaries and effect texts, each found verbatim in the plain parse (whitespace, line breaks and hyphenation ignored). Clearly marked "TomeStack:" notes and the table rows are not SRD sentences and are not counted.
  - **Cells:** 90 cells compared in both parses: the 20 cells of the Draconic Ancestors table, the 30 cells of the Elven Lineages and Fiendish Legacies tables, the size and speed lines of the 8 new species, the Ability Scores, Feat, Skill Proficiencies and Tool Proficiency lines of the 3 new backgrounds, and the category and prerequisite line of the 12 new feats. Both parses agree with the packs on every cell. The script also checks the encoded data against those cells: each ancestry's and legacy's damage type, the speed modifiers, and each background's skills, ability options and feat.
  - A control run with one changed cell and one changed sentence was caught by both checks.
- **Encoded as effects (content v3, v5, v8 or v10, each revision at the lowest version it needs):**
  - speed 35 (Goliath, Wood Elf);
  - damage resistance (each Draconic Ancestry and Fiendish Legacy);
  - skill proficiencies (backgrounds, Elf Keen Senses, Human Skillful, Skilled), as choices of one feature per skill like the class skill options;
  - the background ability score options;
  - Human Versatile as a choice of the four Origin feats;
  - the background feats as content grants;
  - uses per rest: Breath Weapon, Draconic Flight, Forest Gnome Speak with Animals, Giant Ancestry, Large Form, Adrenaline Rush, Relentless Endurance, Magic Initiate's level 1 spell and Boon of Fate;
  - Draconic Flight and Large Form granted at character level 5;
  - dice rolls: Breath Weapon 1d10, the Giant Ancestry dice (Stone's Endurance adds the Constitution modifier), Improve Fate 2d4 and Free Casting 1d4;
  - Alert's Proficiency Bonus to Initiative;
  - Ability Score Improvement as 21 options (six +2, fifteen pairs of +1);
  - Grappler's +1 option and its prerequisite (Strength or Dexterity 13, a restriction group).
- **Text only, because no effect type for them exists on content v10 (each named in a "TomeStack:" note where the player acts):**
  - size, darkvision, truesight, tool proficiencies and languages;
  - spells granted by a species or feat (Elven Lineage, Gnomish Lineage, Fiendish Legacy, Otherworldly Presence, Magic Initiate);
  - the Breath Weapon's damage above level 4: the roll stays 1d10, and the text says 2d10, 3d10 and 4d10 at character levels 5, 11 and 17;
  - the "Level 4+", "Level 19+" and "Spellcasting Feature" prerequisites;
  - the Epic Boons' Ability Score Increase "to a maximum of 30": the calculator caps increases at 20, so encoding it would be wrong at 20;
  - conditional benefits (advantage, Shadowy Form's resistance in dim light);
  - a second copy of a repeatable feat (Ability Score Improvement, Magic Initiate, Skilled): a character holds a feat once, so later copies are recorded by hand or as overrides;
  - a tool picked through Skilled (the choice then stays open), and Improve Fate coming back when Initiative is rolled.
- **The M1 Dwarf** stays at content v3, so its Dwarven Resilience poison resistance stays text (a v10 Dwarf revision is a follow-up).
- **Not changed:** `srd-5.2.1.json` (the Dwarf, the Soldier and Savage Attacker stay as published) and every other bundled revision. `BundledContentInsertOnlyTests` checks every revision bundled at 66d5515 against its recorded hash.
- **Checks:** `SrdOrigins521Tests` (a fixture character per species with a mechanic, each background, the feats), `SrdPackTests` (9 species, 4 backgrounds, 17 feats; source record, families, validation), `BundledContentInsertOnlyTests` and `SchemaTests`.

## C2-1 extension: the Barbarian, levels 1–20 (2026-10-27)

**Documents:** the same two PDFs. Their SHA-256 hashes were checked again on 2026-10-27 and match the table above. The files stay outside the repository.

**Scope reviewed and approved for bundling** (CC-BY-4.0, the same attribution and modification notice as the other packs; nothing outside these pages):

| Pack file | Content | Pages |
| --- | --- | --- |
| `srd-5.2.1-barbarian.json` | The Barbarian, levels 1–20: a new class revision and new revisions of Rage, Unarmored Defense and Danger Sense; Ability Score Improvement, Extra Attack, Fast Movement, Feral Instinct, Instinctive Pounce, Brutal Strike, Relentless Rage, Improved Brutal Strike (levels 13 and 17), Persistent Rage, Indomitable Might, Epic Boon and Primal Champion; the Path of the Berserker with Frenzy (a new revision), Mindless Rage, Retaliation and Intimidating Presence. Weapon Mastery, Reckless Attack, Primal Knowledge and the six skill options stay the earlier revisions | 5.2.1 pp. 28–30; multiclassing pp. 24–25 (already reviewed) |
| `srd-5.1-barbarian.json` | The Barbarian, levels 1–20: a new class revision and new revisions of Rage, Unarmored Defense, Danger Sense and the Path of the Berserker; the levels 4–20 features, including Brutal Critical (levels 9, 13 and 17) and the Berserker's levels 6–14 features | 5.1 pp. 8–10; multiclassing pp. 56–57 (already reviewed) |

- **Method:** as for the Fighter. The text comes from pypdf, in plain mode for 5.2.1 and layout mode for 5.1 (each mode splits words in the other SRD). Line breaks and hyphenation are repaired, and every sentence is checked against the page. The Barbarian Features tables (Rages, Rage Damage, and for 5.2.1 Weapon Mastery) were parsed twice, once from each extraction mode, and compared cell by cell. All cells agree. The scripts stay outside the repository, like the others.
- **Encoded as effects:**
  - Rages and Rage Damage as class scales (5.1: "Unlimited" at level 20 is a Rage that spends no use), the Rage toggle with its resource, damage bonus on melee attacks, damage resistance and Strength advantage while it is on, and the hit points of Relentless Rage as a damage response (5.2.1: twice the Barbarian level; 5.1: 1);
  - Unarmored Defense, Danger Sense (5.2.1 automatic; 5.1 assisted, because of its sight and condition clause), Extra Attack and Fast Movement;
  - Frenzy (5.2.1) as three assisted damage rolls (2d6, 3d6, 4d6, one for each Rage Damage bonus) and Intimidating Presence (5.2.1) as a once-per-long-rest action with its save DC;
  - Primal Champion's +4 as a reference-only score change: the player enters it by hand (the sheet then marks the values that read Strength or Constitution as assisted).
- **Reference text only** (with its page): Weapon Mastery, Reckless Attack, Primal Knowledge, Ability Score Improvement, Feral Instinct, Instinctive Pounce, Brutal Strike and Improved Brutal Strike, Brutal Critical, Persistent Rage, Indomitable Might, Epic Boon, Mindless Rage, Retaliation, and 5.1 Frenzy and Intimidating Presence. The Rage rules that no calculation can check (Heavy armor, extending the Rage, the 10-minute limit, no concentration or spells), the Relentless Rage saving throw and its rising DC, and restoring Intimidating Presence by expending a Rage are by hand.
- **Not taken:** the starting equipment, the Epic Boon feats (Boon of Irresistible Offense is named in the text only), the 5.1 p. 10 pointer to Primal Path options outside the SRD, and the Weapon Mastery property rules.
- **Done 2026-10-27:** `srd-5.2.1-barbarian.json` has 22 revisions (the class, the Path of the Berserker and 20 features, among them the new revisions of Rage, Unarmored Defense, Danger Sense and Frenzy). `srd-5.1-barbarian.json` has 17 (the class, the Path of the Berserker and 15 features, among them the new revisions of Rage, Unarmored Defense and Danger Sense). No earlier revision changed.
- **Checks:** `SrdPackTests` (source records, attribution, every revision validates, the newest Barbarian revision), the PDF-gated `SrdClassTableTests` and `SrdDetectionMeasurementTests` with `TOMESTACK_SRD_PDF_DIR` set (the pack grants match the printed tables, level by level), `HomebrewStudioTests` and `ContentDiagnoseTests`.

## C2-2 extension: the Rogue, levels 1–20 (2026-10-27)

**Documents:** the same two PDFs. Their SHA-256 hashes were checked again on 2026-10-27 and match the table above. The files stay outside the repository.

**Scope reviewed and approved for bundling** (CC-BY-4.0, the same attribution and modification notice as the other packs; nothing outside these pages):

| Pack file | Content | Pages |
| --- | --- | --- |
| `srd-5.2.1-rogue.json` | The Rogue, levels 1–20: the class, the Thief (subclass) and the features Expertise, Sneak Attack, Thieves’ Cant, Weapon Mastery, Cunning Action, Steady Aim, Ability Score Improvement, Cunning Strike, Uncanny Dodge, Evasion, Reliable Talent, Improved Cunning Strike, Devious Strikes, Slippery Mind, Elusive, Epic Boon and Stroke of Luck; the Thief’s Fast Hands, Second-Story Work, Supreme Sneak, Use Magic Device and Thief’s Reflexes; ten skill options and eighteen expertise options (one for each SRD skill) | 5.2.1 pp. 61–64; multiclassing pp. 24–25 (already reviewed) |
| `srd-5.1-rogue.json` | The Rogue, levels 1–20: the class, the Roguish Archetype choice with the Thief, and the features Expertise, Sneak Attack, Thieves’ Cant, Cunning Action, Ability Score Improvement (levels 4, 8, 10, 12, 16 and 19), Uncanny Dodge, Evasion, Reliable Talent, Blindsense, Slippery Mind, Elusive and Stroke of Luck; the Thief’s Fast Hands, Second-Story Work, Supreme Sneak, Use Magic Device and Thief’s Reflexes; eleven skill options and nineteen expertise options (the eighteen skills and Thieves’ Tools) | 5.1 pp. 39–41; multiclassing pp. 56–57 (already reviewed) |

- **Method:** as for the Barbarian. The text comes from pypdf, in plain mode for 5.2.1 and layout mode for 5.1 (each mode splits words in the other SRD; the 5.1 pages are read column by column). Line breaks and hyphenation are repaired, and every sentence is checked against the page. The Rogue Features tables (Sneak Attack, and the feature columns) were parsed twice, once from each extraction mode, and compared cell by cell. All cells agree in both families. The scripts stay outside the repository, like the others.
- **Encoded as effects** (against the reference text, which stays in each summary):
  - Sneak Attack dice as a class scale (1d6 at levels 1–2 up to 10d6 at levels 19–20, shown in the class columns on Play; no formula reads it, so the content debugger’s `debug.scale-unused` note for it is expected);
  - the saving throws, the skills (four of the class list; one as a multiclass character), the weapons (Simple weapons and the Finesse or Light Martial weapons: Rapier, Scimitar, Shortsword, Whip, Hand Crossbow in 5.2.1; Hand Crossbow, Longsword, Rapier and Shortsword in 5.1) and Light armor;
  - Expertise as two choices (levels 1 and 6) over the skill options, each granting expertise in one skill (the sheet does not check that the skill is a proficiency; the option says so);
  - Slippery Mind as saving throw proficiencies (5.2.1: Wisdom and Charisma; 5.1: Wisdom);
  - Stroke of Luck as a pool of one use, restored on a Short or Long Rest, with a Use action that spends it (5.2.1: the use of the 20 is by hand); and in 5.1 the Thief’s Supreme Sneak as an assisted Advantage on Dexterity (Stealth) checks, because of its half-speed condition.
- **Declined shapes:** a Sneak Attack bonus ladder of ten bands (the rider limit is four bands, and an attack rider’s dice are a literal); a Sneak Attack as an attack effect (it would be its own attack row with its own attack roll, which is wrong, because the extra damage rides on an attack you already make); an Uncanny Dodge damage response of a fixed amount (half of the damage cannot be expressed).
- **Reference text only** (with its page): Sneak Attack’s conditions, Thieves’ Cant, Weapon Mastery, Cunning Action, Steady Aim, Ability Score Improvement, Cunning Strike, Improved Cunning Strike and Devious Strikes, Uncanny Dodge, Evasion, Reliable Talent, Blindsense, Elusive, Epic Boon, and the Thief’s Fast Hands, Second-Story Work, Use Magic Device and Thief’s Reflexes (5.2.1 also Supreme Sneak). Thieves’ Tools proficiency, the language of Thieves’ Cant and the Expertise in Thieves’ Tools (5.1) are by hand.
- **Not taken:** the starting equipment, the Epic Boon feats themselves (Boon of the Night Spirit is named in the text only), and the choice of the other language for Thieves’ Cant.
- **Checks:** `SrdPackTests` (source records, attribution, every revision validates), the PDF-gated `SrdClassTableTests` and `SrdDetectionMeasurementTests` with `TOMESTACK_SRD_PDF_DIR` set (the pack grants match the printed tables, level by level), and `ContentDiagnoseTests`.

## README screenshots (docs/media, reviewed 2026-09-30)

Two screenshots of the app, shown in the README, reproduce bundled SRD 5.2.1 content as the app renders it. They add no SRD text that the packs above do not already hold, and nothing from outside the two checked PDFs.

| File | SRD text shown | Pages |
| --- | --- | --- |
| `docs/media/builder-choices.png` | Verbatim: the background ability score paragraph ("A background lists three of your character's ability scores. … None of these increases can raise a score above 20."), the Barbarian skill line ("Skill Proficiencies: Choose 2: Animal Handling, Athletics, Intimidation, Nature, Perception, or Survival."), and the names of the Soldier ability score options and two Barbarian skill options, each with its page citation | 5.2.1 pp. 83 and 28 |
| `docs/media/calculation-trace.png` | Names and a page citation only (the Soldier background and one of its ability score options); no rules text | 5.2.1 p. 83 |

- **License:** CC-BY-4.0, as for the packs. The README links [ATTRIBUTION.md](../../ATTRIBUTION.md), which carries the approved SRD 5.2.1 statement and the modification notice, and each image's alt text names System Reference Document 5.2.1.
- **Rule for later screenshots:** a screenshot may show SRD text only from the bundled packs. Add it to this table, with its pages, in the same change.

## Sharing rule (Import Lab L5, wording only)

Preflight is the single rule for what may leave the machine in a share: `package.preflight` and the character-package share, the source pack, the campaign pack and the shared sheet all ask the same code (`Preflight` in `src/AppService/Packages/Preflight.cs`) when they export. It lists what is included and what is left out or blocked, with the reason, and shows each included source's license notice once. It never adds a rule or any text to the packs above: the bundled SRD sources pass it as before (CC-BY-4.0, redistributable, never import-derived), and a PDF or the text read from it is never in a share. Backups keep their own rule (a personal backup holds everything).

## How it was modified (recorded in each source's `modificationNotice`)

- Passages are excerpted. Descriptive text not needed for play (for example age and alignment) is left out, and some passages are shortened. Nothing is reworded to change a rule.
- Line breaks, hyphenation and PDF extraction artifacts are repaired. Some em dashes became commas or parentheses.
- Rules are encoded as effects: ability increases, proficiencies, a hit die, level-gated grants, choices, resources and recoveries, an armor class alternative, and a prerequisite.
- Clearly marked TomeStack notes are added where the data needs one, for example "TomeStack slice: levels 1–3", or "the formula matches the table for levels 1–11".

## What was not taken

No art, logos, trade dress, product names or other publications. No monsters or magic items. Spells are included since M2 (above). Nothing outside the two checked PDFs.

## Attribution placement and judgment calls

- `attribution` holds the approved statement **verbatim**. `SrdPackTests` compares it with the approval page.
- `modificationNotice` is separate so the attribution stays unchanged. Both travel into package `notices[]`.
- **Judgment call, decided (owner, 2026-09-27: keep):** each SRD source's `publisher` is "SRD 5.1 (CC-BY-4.0)" or "SRD 5.2.1 (CC-BY-4.0)", not the rights holder's name. Both legal pages ask for "no other attribution" beyond the statement, so the rights holder is named only inside the statement.
- `ATTRIBUTION.md` and `NOTICE` carry both statements and the modification notice, and the installer ships them.
- The in-app About / Sources screen is M2.

## Known limitations of the encoded rules

- "None of these increases can raise a score above 20" (5.2.1 background ability scores) is enforced since M2 item 4 (owner decision 2026-09-27): bonuses to ability scores stop at 20 in both families (`features/equipment.md`).
- Rages use `min(2 + floor(CLASS_LEVEL / 3), 4)`, which matches both tables for levels 1–11. The slice is levels 1–3.
- Unarmored Defense applies only while no armor is worn since M2 item 4: the calculator treats every Armor Class replacement as an unarmored alternative. Its effect text in the published revisions still says "armor is not modeled yet, so TomeStack assumes none". Published revisions are insert-only, so the note stays until a new SRD pack revision (the trace gives the current behavior).
- The SRD weapon and armor tables are bundled (sections above); other SRD equipment is not. Adding it needs this review extended (SPEC Q-03).
- Frenzy (5.2.1) offers a 2d6 roll for Rage Damage +2 (Barbarian levels 1–8).
- Advantage-granting features (Rage, Danger Sense, Reckless Attack) stay text only. Since M2 item 2, the sheet shows them as reference-only features, and the player picks advantage for the roll.
- **Note (2026-10-27, C2-1):** the four lines about Rages, Unarmored Defense, Frenzy and Advantage above (and the "slice is levels 1–3" wording) describe the M1 and M2 revisions. They are superseded by the new Barbarian revisions of the C2-1 extension (levels 1–20, Rages from the class scale, the Unarmored Defense note corrected, Frenzy at three Rage Damage values, Rage and Danger Sense as automatic or assisted effects). Characters still on the earlier revisions keep those limitations until their class is updated.

## SRD 5.1 races (2026-10-26, D60 amended 2026-10-26)

**Document:** `SRD_CC_v5.1.pdf`, downloaded again on 2026-10-26 from media.wizards.com. Its SHA-256 matches the table above. The file stays outside the repository.

**Scope reviewed and approved for bundling** (CC-BY-4.0, the same source record, attribution and modification notice as the other SRD 5.1 packs; nothing outside these pages):

| Pack file | Content | Pages |
| --- | --- | --- |
| `srd-5.1-origins.json` | Dwarf (p. 3) with Hill Dwarf (p. 4) | pp. 3–4 |
| | Elf with High Elf | p. 4 |
| | Halfling (pp. 4–5) with Lightfoot (p. 5) | pp. 4–5 |
| | Human | p. 5 |
| | Dragonborn (pp. 5–6), with the ten rows of the Draconic Ancestry table (p. 5) | pp. 5–6 |
| | Gnome with Rock Gnome | p. 6 |
| | Half-Elf (pp. 6–7), with its ability score and skill choices | pp. 6–7 |
| | Tiefling, with Infernal Legacy's 3rd- and 5th-level spells | p. 7 |
| | The "Subraces" paragraph (its first two sentences, in each subrace choice's text) | p. 3 |

- **Method:** the scripted method of the casters and the Fighter. The text was transcribed from pypdf layout mode, with line breaks and hyphenation repaired. A second script then checked the pack against **two independent parses** of pp. 3–7: layout mode, with the two columns split by position and read in order, and plain mode (reading order, with its mid-word spaces ignored).
  - **Every SRD sentence** in a summary or effect text was found in both parses: 234 sentences, 0 mismatches.
  - **Numbers and table cells** were read from each parse by pattern and compared with the encoded effects: each race's speed, ability increases (subrace included) and darkvision range, and the three cells of each Draconic Ancestry row plus the resistance it encodes. 72 cells, 0 mismatches.
  - The check was run once on a copy with one changed number and one changed word; it reported both.
  - The scripts stay outside the repository, like the others.
- **Structure:**
  - Each race is a `species` revision. Its summary keeps the race's introduction, Size, Speed, Darkvision, the traits that stay text, and the Languages line shortened to the languages, as the Half-Orc's does. Age, Alignment and the descriptive sentences are left out.
  - A subrace is a `feature` revision offered by a one-option `choice` on its race, the SRD's own structure ("Members of a subrace have the traits of the parent race in addition to the traits specified for their subrace", p. 3), as the M1 Barbarian offers its one SRD path. A chosen subrace is species content, so its ability increase applies under 2014 rules (`RulesFamilyPolicy.AbilityIncreaseSource`).
  - The Dragonborn chooses one of ten `Draconic Ancestry: <dragon>` features, each restating its table row ("Dragon: Red. Damage Type: Fire. Breath Weapon: 15 ft. cone (Dex. save).") and granting the resistance.
  - The Half-Elf chooses two of five ability score features (not Charisma: "two other ability scores") and two of eighteen skill features.
- **Encoded as effects:**
  - ability increases;
  - walking speed as a `replace` on `speed` (25 or 30 feet; content schema v10, so these races are published at v10);
  - damage resistances (Dwarf poison, Dragonborn by ancestry, Tiefling fire; content v10);
  - skill proficiencies (Keen Senses; Skill Versatility);
  - weapon proficiencies (Dwarven Combat Training: battleaxe, handaxe, light hammer, warhammer; Elf Weapon Training: longsword, shortsword, shortbow, longbow);
  - Dwarven Toughness (+1 hit point per level);
  - the Breath Weapon's use (1, back on a short or long rest);
  - Infernal Legacy's hellish rebuke (from 3rd level) and darkness (from 5th level), each once per long rest, as features the Tiefling grants at those character levels.

  Each revision is published at the lowest content schema version it needs (`SrdOrigins51Tests`).
- **Reference text only** (no field or effect type exists for them on `main`): size; darkvision; languages; tool proficiencies (Dwarf, Rock Gnome); the High Elf cantrip and the Tiefling's thaumaturgy; advantage on saves (Dwarven Resilience's half, Fey Ancestry, Brave, Gnome Cunning); Lucky; Trance; Stonecunning; Artificer's Lore; Tinker; Halfling Nimbleness; Naturally Stealthy. **The Breath Weapon's damage** (2d6, 3d6 at 6th level, 4d6 at 11th, 5d6 at 16th) and its save DC (8 + Constitution modifier + proficiency bonus) stay in its text: dice that grow with character level need a scale that species content cannot use on `main` (`scale` belongs to classes and subclasses), so the player rolls it by hand. **The Dwarf's heavy-armor speed** ("Your speed is not reduced by wearing heavy armor") has no field either: the armor Strength warning (`equipment.armor-strength`) still tells a Dwarf below the armor's Strength to lower Speed by an override, which the player ignores. The calculated speed is not changed. `SrdOrigins51Tests` pins this known gap.
- **Not taken:** Age, Alignment, the races' descriptive and Language flavour sentences, and the "Racial Traits" introduction (p. 3) apart from the two "Subraces" sentences.
- **Checks:** `SrdOrigins51Tests` (each race through an invented level-1 fixture, `tests/RulesFixtures/characters/srd51-<race>-l1.json`: scores, speed, size, choices, resistances, proficiencies, the level-gated Infernal Legacy uses; every revision validates at its lowest version; no id collides with other bundled content), and `SrdPackTests` (the source record, the nine races once each).
- **Done 2026-10-26:** `srd-5.1-origins.json` has 47 revisions: 8 races, 4 subraces, 10 draconic ancestries, 5 Half-Elf ability score options, 18 Half-Elf skill options and 2 Infernal Legacy features. `srd-5.1.json` is unchanged.
