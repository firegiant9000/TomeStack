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
| SRD 5.1 | Half-Orc (species, p. 7); Acolyte (background, pp. 60–61) and its feature Shelter of the Faithful (p. 61); Barbarian levels 1–3 (class, pp. 8–9): Rage, Unarmored Defense, Reckless Attack, Danger Sense, and the six skill options; Path of the Berserker (subclass, p. 9) with Frenzy; Grappler (feat, p. 75) |
| SRD 5.2.1 | Dwarf (species, p. 84); Soldier (background, p. 83) with its seven ability score options (from the background rules on p. 83); Savage Attacker (origin feat, p. 87); Barbarian levels 1–3 (class, pp. 28–30): Rage, Unarmored Defense, Weapon Mastery, Danger Sense, Reckless Attack, Primal Knowledge, and the six skill options; Path of the Berserker (subclass, p. 30) with Frenzy |

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
- No SRD armor or equipment is bundled. Adding the armor table needs this review extended (SPEC Q-03).
- Frenzy (5.2.1) offers a 2d6 roll for Rage Damage +2 (Barbarian levels 1–8).
- Advantage-granting features (Rage, Danger Sense, Reckless Attack) stay text only. Since M2 item 2, the sheet shows them as reference-only features, and the player picks advantage for the roll.
