# Changelog

## Unreleased (M2.1, M2.2, M3 and M4)

### Added

- **Snapshots of a character (M5 slice 8):** "Take snapshot" on the sheet keeps a copy of the character to come back to. "Restore" first shows what would change. It then keeps a snapshot of the current state as well, so every restore can be undone. Snapshots stay on this computer: exports, shares and full backups leave them out. The data folder moves to database version 7; the previous version is backed up first (`tomestack.db.v6.bak`).
- **Design feedback, if you want it (M5 slice 7):** a "Show design feedback" setting in the studio, off by default. When it is on, "Get design hints" compares a class or feature with the SRD classes. It points out more spell slots than any SRD full caster has, a multiclass share that gives more slots than the class's own table, a resource that grows faster than the proficiency bonus, and levels that give nothing. Hints never block publishing, never change a character and are never exported.
- **Start homebrew from a template (M5 slice 6):** four starting points in the studio: a feature with limited uses, a stance you switch on and off, a subclass skeleton and a class skeleton. A template opens as an unsaved draft; nothing is saved or published until you do it. The skeletons leave empty feature slots at the usual levels, which you fill with your own published features. The studio can now also edit toggles, and bonuses that apply only while a toggle is on.
- **See how your homebrew fits together (M5 slice 5):** "Show relationships" in the studio editor draws a tree. For a class it runs by level, through the features and choices it brings in, to the resources and to the rolls and rests that use them. It also shows what never applies, such as a grant inside granted content. It works fully by keyboard and with screen readers, and Enter on a rule jumps to it in the editor.
- **Compare two versions of your homebrew (M5 slice 4):** "Compare revisions" in the studio editor shows what changed between any two versions, including the unsaved one on screen. It shows the rules and the texts line by line, and what each version would give copies of your characters (and a blank character, for a class or subclass). Nothing is changed. The update review uses the same calculation.
- **Try a class or subclass before you publish it (M5 slice 3):** "Try it" in the class and subclass editors shows how the revision on screen plays, at a level you pick. It runs on a blank character or on a copy of one of yours, with the values it would change. Nothing is saved, and your characters keep using published content only.
- **Find problems in your homebrew (M5 slice 2):** "Find problems in <source>" in the homebrew studio checks all of a source's content, drafts included, and "Find problems" checks the entry you are editing. It finds what the Check button cannot see on one entry alone. Examples: a resource nothing spends or recovers, a recovery or action for a resource that isn't there, a grant that never applies because the content is itself granted, a subclass no class offers, a class feature nothing grants, a choice with nothing to pick, class columns that are read but missing or defined twice, and grants of an older version. **Show** opens the entry and moves focus to the rule. Nothing is changed.
- **Write your own class in the homebrew studio (M5 slice 1b; ADR-010):** "New class" sets the hit die, the saving throws and multiclass prerequisites. It can also set:
  - a skill choice (TomeStack creates the skill options for you);
  - a subclass choice at any level, which your own subclasses join;
  - class columns such as "Ink", which resources and other formulas can read;
  - features by level;
  - spellcasting with its own slot table and a caster share for multiclassing.
  
  The class appears in the builder like any other, and the sheet shows its columns under "Class columns". Content that uses these features can only be opened by this version of TomeStack or later.
- **Classes with their own columns and caster shares (M5 slice 1a; ADR-010; rules engine only):** class content can now carry named per-level columns, such as "Ink: 2, 2, 3 …". Its formulas can read them, so a resource, a roll bonus, a skill or a prepared-spell count grows with the class. A caster can also say exactly how many caster levels it adds at each class level, for example two thirds, and it combines with the SRD casters on the Multiclass Spellcaster table. The homebrew studio cannot author these yet; that is the next slice. Tested on an original fixture class, not bundled content.

- **Archive a character (SPEC C-08, audit 2026-09-28):** "Archive…" on the sheet first says what happens, then moves the character to a collapsed "Archived" list after you confirm. Nothing is deleted: its play state, gap notes, campaign and the content it uses all stay, and "Back up everything" includes it and restores it archived. "Unarchive" brings it back as it was. There is no hard delete. Exporting a character never passes on that it is archived, and importing a package never archives or unarchives one. Older versions of TomeStack show an archived character as active.

- **The Fighter and the armor table (M2.2; `docs/licensing/srd-pack-review.md` "M2.2 extension"):** the SRD Fighter, levels 1–20, in both rules families, with the Champion, and the SRD armor and shield table.
  - **Automated:** Fighting Style as a choice (Defense adds +1 AC while you wear armor), and Second Wind, Action Surge and Indomitable as resources that come back on rests. Second Wind rolls 1d10 plus your Fighter level. Also automated: the number of attacks (Extra Attack) and the Champion's critical range. The Champion's second Fighting Style is a choice too, and a style can't be picked twice.
  - **Armor:** light, medium and heavy armor and shields set Armor Class as before. They now also warn when you lack the Strength (speed 10 feet lower) or the training, and Stealth warns about disadvantage. Under 2024 rules a shield without training adds nothing to Armor Class; under 2014 rules it still does.
  - **Reference text, applied by hand:** the other Fighting Styles, Weapon Mastery (the kinds you choose), Tactical Mind and Tactical Shift, Remarkable Athlete, Heroic Warrior, Survivor and Ability Score Improvements. The feature texts say what to do.
  - **Homebrew:** a homebrew Fighter subclass joins the Fighter's subclass choice next to the Champion, which is the path the Stardust Guardian will take.
  - **Paladin and Ranger:** their Extra Attack is now counted too, and their armor training is recorded (the Paladin's heavy armor only as a starting class), in new revisions. New Wizard and Sorcerer revisions record that they have no armor training. Existing characters get them through "Updates available".
  - **Content schema v8** adds the fields this needs. Older content is unchanged, and older versions of TomeStack refuse v8 content with a clear message. Homebrew you publish is saved in the oldest content version that can hold it, so homebrew that uses none of the new fields can still be shared with TomeStack 0.3.x.

- **Back up everything and restore it (M2.1; `docs/features/package-format.md` "Full library backup", ADR-007 item 10):** a new **Backups** screen.
  - **Back up everything** saves one file with your whole library: characters, campaigns, gap notes, all your homebrew (drafts, older versions and entries no character uses yet) and the PDFs TomeStack keeps a copy of. Before this, only characters and what they used could be backed up, so unfinished homebrew had no backup at all.
  - **Restore full backup** checks the whole file first, PDFs included, shows what it would add or replace, and restores only when you confirm. It deletes nothing. Before it replaces anything, it saves a copy of your current data in the backups folder.
  - A character's own export is unchanged, and so is its "Share with someone", which never includes PDFs or gap notes.
  - Character packages are still format v5, so TomeStack 0.3.0 can read them. Full backups are format v6, and older versions refuse them with a clear message.

- **M4 exit evidence (`docs/features/m4-acceptance.md`):** the original fixture book is imported through the real worker in the gate, reviewed, and nothing is active without approval. The run on a third-party test PDF is prepared (it reads a private local folder and reports counts only) and still owed, so the version stays 0.3.0.

- **Import review (M4 D5; `docs/features/pdf-import.md`):** on the Sources screen, a source of your own with a PDF has "Read the text and find candidates". Read some pages or the whole book, watch the progress, cancel or resume, and search the text. Then review the candidates, filtered by page, kind, confidence and status. Each shows its excerpt next to "Open page", what was read, what is unsure, and what it depends on. Edit it, accept it as a draft or as a reference entry, or ignore it. Accepted entries are drafts in the studio, where publishing checks them again.

- **Reviewing candidates (M4 D4; `docs/features/pdf-import.md`):** before a candidate is accepted, TomeStack checks the entry it would become (rules, references, formulas) and shows what it depends on. It stays blocked while a field is unsure or a name it refers to is missing, until you edit it or accept it as a reference entry only. Accepting creates a draft; nothing applies until you publish it in the studio.

- **Candidate detection (M4 D3; `docs/features/pdf-import.md`):** an import proposes candidates found in the book's text: spells, feats, class features, weapon and armor table rows, and class feature tables. Each has its excerpt, page, proposed rules, a confidence and what it is unsure of, and the names it mentions that are not installed. Nothing becomes content until you review it. Measured on the two SRDs: every spell is found with its level, and every bundled weapon and class is found.

- **Import jobs (M4 D2; `docs/features/pdf-import.md`):** extracting a PDF runs as a job you can cancel and resume. It survives closing the app and keeps a local audit log with no text from the book. Extracted text is searchable within its source. Imported text stays on this computer: no backup or share includes it.

- **PDF text extraction (M4 D1, ADR-009; `docs/features/pdf-import.md`):** TomeStack can read the text of a PDF page by page, with its layout, and reads pages without a text layer with Windows' built-in OCR. The reading happens in a separate worker process with size, page, time and memory limits, so a damaged or hostile PDF cannot take the app down. Nothing is imported yet: import jobs and candidates follow.
- **Third-party components:** PdfPig 0.1.16 (Apache-2.0), and the Windows SDK C#/WinRT projection (Microsoft Windows SDK license) for OCR. The self-contained install grows by about 31 MB (`ATTRIBUTION.md`).

- **M3 exit evidence (`docs/features/m3-acceptance.md`):** every M3 item is mapped to its executable acceptance. The exit gate is not met yet, because the owner's Stardust Guardian material and a played session are needed. The version stays 0.3.0.

- **Source updates (M3 C7, SPEC I-06; `docs/features/publishing-and-updates.md`):** when a newer revision of something a character uses arrives, from an updated SRD pack or your own homebrew, the sheet's "Updates available" panel offers it. Review it to see what changes, then apply it or keep the current revision. Nothing updates by itself.

- **Gap notes follow-ups (M3 C5; `docs/features/gap-notes.md`):**
  - **Report a gap:** a button on every feature and field fills in the gap note's "About" and puts the cursor in the text box.
  - **All characters:** a new "Gap notes" screen in the sidebar lists every character's notes, open first, with "Mark resolved" and a way to open the character.
  - Notes still stay on this computer and travel only in a personal backup.

- **Printable backup (M3 C4; `docs/features/printable-backup.md`):** "Print…" on the sheet opens a print preview of the character: abilities, saves, skills, combat numbers, attacks, resources, spells, every feature with its source, page and manual step, and overrides. It ends with the license notices. Print it on paper or with "Microsoft Print to PDF" from the app's own print dialog. Gap notes are printed only if you tick them in. Nothing is sent anywhere, and no file path appears.

- **Combined multiclass spell slots (M3 C3, D04's M3 part; `docs/features/spellcasting.md`):** a character with two or more spellcasting classes gets its spell slots from the SRD Multiclass Spellcaster table instead of recording the total by hand. Full casters count every level, and half casters (Paladin, Ranger) count half: rounded down under 2014 rules, up under 2024 rules. The trace shows each class's share and the table row. Pact Magic stays its own pool. Characters built before this keep their pinned SRD classes (and the manual step) until they take the update.

### Changed

- **Plans only, nothing built (ROADMAP "M5 plan" and "M6 plan"; LIVING_SPECS D13):** by owner direction, M5 (creation power) starts before the M3 gate, which is unchanged and still not met. The template set and new effect types are provisional until the M3 gap notes arrive. The owner approved the plans and accepted ADR-010 (custom base classes, content schema v9). ADR-011 (the extension API) and ADR-012 (VTT export adapters) are proposed and wait for the owner's choices.
- **Development switches are off in the shipped app (audit 2026-09-28; LIVING_SPECS D11):** `TOMESTACK_DEV_FIXTURES=1` and `--devtools` now work only in a Debug build, a smoke run included. The installed app also drops WebView2's extra browser arguments from the environment, so a variable or a shortcut can no longer seed test content into your library, open the browser developer tools or open a debugging port.
- **The development transport times out like the app's (audit 2026-09-28):** in the browser dev setup, a command to the DevHost that gets no answer now fails after 30 seconds with "timeout", as in the desktop app, instead of waiting forever. Commands that wait for you (a native dialog) still wait.

- **Honest status (M2.1; README, MVP, ROADMAP, acceptance docs):** claims now say how far they are proven: implemented, fixture-verified, Windows-install verified, or accepted in real play. The README states the current class, species and background coverage. M2 is "checks passed, limited content", because the MVP goal of any SRD character 1–20 is not met yet. The ROADMAP adds M2.1 (data safety, done) and M2.2 (Fighter baseline and SRD armor), and puts M2.2 before the M3 Stardust Guardian run that depends on it. PDF candidate import is labeled **Experimental** in the app until a real third-party book and the SRD measurements pass.

### Fixed

- **A data folder always opens after an update (full-stack review 2026-09-28):** if your library already holds different content under the id of a revision a new TomeStack version bundles (for example from a package imported earlier), TomeStack now keeps your copy, skips the bundled one and says so at startup. Before, it refused to open the data folder at all, on every launch.
- **Archiving no longer pulls you back:** if you open something else while an archive or unarchive finishes, you stay where you went.

- **Fighter review fixes (M2.2, PR #12 review; `docs/features/equipment.md`, `docs/features/multiclass-and-attacks.md`):**
  - Armor training is checked only when every class the character has levels in records it (and is installed). A Paladin who took a level of Fighter is no longer told they lack training for their plate, and under 2024 rules a Cleric who takes a feat granting light armor keeps the shield's Armor Class. A Wizard who takes a level of Fighter is still warned about heavy armor.
  - A roll's bonus keeps its sign: a Strength 8 bonus is −1, not 0. The button shows it as "1d6 − 1", and the roll record calls it "<roll> bonus" instead of showing the formula. A bonus that cannot be worked out for the character is shown as a problem on the feature, and the roll is refused instead of rolled without it.
  - The critical range stays between 1 and 20, and the number of attacks is at least 1, with a warning when content goes past that. Homebrew can lower the critical range only with a bonus (a "set" or "replace" would keep the highest value, which is the wrong way round), and can't make an Armor Class replacement that applies only in armor (it could never apply).
  - Content that claims an older content version but carries the new Fighter fields (including the attack count and critical range) has those fields ignored, as an older TomeStack would, with a note on the sheet.

- **CI (M2.1):** the UI flow no longer fails at random. Five checks read the status line before it changed and saw the previous message (three failed runs on 2026-09-28). They now wait for the expected text, and lint refuses the old pattern in the e2e tests. A sixth race: the Barbarian flow read the long-rest proposal as soon as the panel had focus, before the proposal had arrived (reproduced by delaying it 400 ms), and that flow took 23 of its 30 seconds. It now waits for the proposal, and e2e tests have 60 seconds. The desktop smoke is now blocking: it passed on every hosted run that reached it. A new check starts two real TomeStack processes on one data folder. It passed three hosted runs in a row, so it is blocking too.

- **A second spellcasting class gets its bonuses, with a trace (M2.1; `docs/features/spellcasting.md`):** a bonus to spell attacks or spell save DCs (from an item or a feature) used to reach only the first spellcasting class. A multiclass character's other casters showed a bare proficiency bonus + ability modifier, without saying how it was calculated. Every caster now gets those bonuses, and the Spells panel explains each caster's attack bonus and save DC step by step.

- **One TomeStack per data folder (M2.1; ARCHITECTURE "Data folder"):** starting TomeStack again while it is open now brings the open window to the front instead of opening the same data a second time. Before, the second copy marked the first copy's running PDF import as interrupted, so resuming it could run two imports of one job at once. Its start-up clean-up could also delete a PDF the first copy was still attaching. TomeStack now holds `tomestack.lock` in the data folder while it runs, and Windows releases it when TomeStack exits or crashes. The DevHost is refused on a folder the app has open, and the app on one the DevHost has open.

- **Import review fixes (M4, independent review 2026-09-28; ADR-009, `docs/features/pdf-import.md`):**
  - A page that crashes the worker, runs out of time or memory, or cannot be read by OCR now fails alone: it is marked unreadable and the rest of the book is read. Resuming used to stop on the same page every time. After 5 such pages in one run the import stops, and "Resume" continues after them.
  - What one page can send to the app is now bounded: the character limit covers its blocks and lines too, and the app refuses any worker message longer than a page can hold. The app also checks that the worker really runs under its memory cap before it reads the PDF, watches committed memory as well as the working set, and a worker left behind by a crashed app now exits.
  - Finding candidates holds a bounded amount of text in memory. A job past it detects its first pages and says so in its audit; import the rest as a page range.
  - Cancelling an import, or removing its PDF, now also stops candidate detection.
  - Accepting a candidate saves the draft and marks the candidate accepted together, so a crash cannot lead to a duplicate draft.
  - Importing the same PDF into the same source again no longer proposes entries you already accepted or ignored.
  - A linked PDF that changed on disk is caught when a run starts, not only when it is resumed.
- **Accessibility (import review):** after you accept or ignore a candidate, focus returns to the candidate list instead of being lost, and the final import status ("completed") is announced.
- **Builder choices and spells:** ticking two options quickly (two skills of a choice, or two spells) could drop the first one. Each tick now applies to the latest draft. This also made an end-to-end test flaky.

### Migration

- **Database schema 7** (M5 slice 8) adds `character_snapshots`, insert-only (triggers refuse updates and deletes). The data folder is backed up first (`tomestack.db.v6.bak`). After the upgrade, older builds refuse the folder; restore that backup to go back. Packages and library backups are unchanged: snapshots are never in them.
- **Database schema 6** (M4 D2) adds the local import tables (`import_jobs`, `import_pages`, `import_candidates`, `import_audit`). The data folder is backed up first (`tomestack.db.v5.bak`). After the upgrade, older builds refuse the folder; restore that backup to go back. Packages are unchanged.
- **Content schema v7** (`docs/schemas/content-revision.v7.schema.json`) adds `spellcasting.multiclassCaster`. It is absent by default, so older revisions are unchanged. 0.3.0 refuses v7 revisions.
- **Content schema v8** (`docs/schemas/content-revision.v8.schema.json`, M2.2) adds the Fighter fields and `armor.none`. `content.publish` now writes the lowest version a revision needs (at least v3) instead of the current one; the draft keeps its version and no stored revision or hash changes (`docs/schemas/README.md` "Versioning rules", `docs/features/package-format.md`).
- **SRD packs (M2.2):** new v8 revisions of the Paladin and Ranger (Extra Attack, then a further revision adding armor training) and of the Wizard and Sorcerer (`armor.none`), in both families (insert-only: a data folder opened by an earlier build of this branch still opens).
- **SRD packs:** new revisions of the seven SRD slot casters and their Spellcasting features, in both families (insert-only; the earlier revisions stay for the characters that pin them).
- **Content schema v9** (`docs/schemas/content-revision.v9.schema.json`, M5 slice 1a, ADR-010) adds the `scale` effect, `SCALE.<id>` in formulas and `spellcasting.multiclassCasterTable`. Each is read only in a v9 revision. No stored revision, hash or bundled pack changes, and there is no database migration. Content published without these features keeps its lower version, and older builds refuse v9 content (`content.schema-unsupported`, `package.schema-unsupported`).

## 0.3.0 (M2 delivered)

M2 "Usable MVP" is delivered (ADR-008: MINOR for a delivered milestone). The owner checks passed on the installed 0.2.2 on 2026-09-28: the upgrade from 0.2.0, the keyboard and Narrator passes, the viewer landing on the cited page, a clean-VM install and an SRD play rehearsal (`docs/features/m2-acceptance.md`). This build also carries M3 B1–B3 below.

### Added

- **Session gap notes (M3 B3; `docs/features/gap-notes.md`):** in the sheet's "Gap notes" panel, the player writes down where TomeStack fell short on a feature or field, then marks each note resolved or deletes it after confirming.
  - Notes are stored only on this computer and never sent anywhere. A personal backup includes them; a share never does.
  - No error message or log quotes a note's text.

- **Toggled effects, shared resources and variable costs (M3 B2; `docs/features/m3-effects.md`):**
  - **Toggles:** content can declare a toggle (a stance, an aura) that the player switches on and off in "Attacks and actions". Turning it on can spend a use, in the same confirmed change. Modifiers bound to it apply only while it is on: for example +2 Armor Class, traced and still automatic. The long rest proposes switching active toggles off.
  - **Shared resources:** an action can spend another feature's resource.
  - **Variable costs:** an action can cost several uses, or let the player choose how many, in the roll's "Spend" control.

- **The Stardust Guardian acceptance (M3 B1; MVP definition of done 3; `docs/features/m3-stardust-guardian.md`):**
  - **The test:** it imports the owner's private backup of the character from the gitignored `tests/RulesFixtures/local/stardust-guardian/`. It checks the four DoD 3 mechanics and lists every mechanic as automatic, assisted or reference, with its manual step. It compares them with the owner's optional expectations, and writes the full report only inside that local folder.
  - **Without the material,** it is skipped. A synthetic stand-in runs the same pipeline in the gate.
  - **`character.mechanics`:** a new command that returns that inventory for any character.

### Fixed

- A new roll no longer inherits the amount typed for the previous roll's variable spend.
- "Spend" is disabled when an action's cost evaluates to 0, instead of failing with `play.amount-out-of-range` (review of #7).

### Migration

- **Content schema v6** (`docs/schemas/content-revision.v6.schema.json`) adds the `toggle` effect, `modifier.toggle`, and roll `resourceContent`, `cost` and `variableCost`. They are typed only in v6 revisions, and the new fields are absent by default, so older revisions are unchanged. 0.2.2 refuses v6 revisions.
- **Character schema v7** (`docs/schemas/character.v7.schema.json`) adds `play.toggles`. v1–v6 characters are upcast with every toggle off. There is no database migration.
- **Database schema 5** adds the `gap_notes` table. The data folder is backed up first (`tomestack.db.v4.bak`), as for every upgrade. After the upgrade, 0.2.2 refuses the data folder (`NewerDatabaseException`); restore that backup to go back.
- **Package format v5** (`docs/schemas/package-manifest.v5.schema.json`, `gap-note.v1.schema.json`) adds `gaps/` entries, in backups only. 0.2.2 refuses v5 packages.

## 0.2.2 (M2 exit candidate)

The build for the M2 owner checks (ADR-008: PATCH for a build given to a user; 0.3.0 once every MVP.md check passes on an installed build). Evidence per check: `docs/features/m2-acceptance.md`.

### Added

- **M2 acceptance evidence (`docs/features/m2-acceptance.md`):** every MVP.md check is mapped to its executable acceptance, plus the owner checks and what could not be verified here. A new `M2AcceptanceTests` plays a scripted encounter with an SRD Wizard in each family (damage, a cantrip attack, a weapon attack, a spell slot, a short rest with a hit die, a long rest), then round-trips it through a backup to a clean data folder.

- **The SRD spellcasting classes, levels 1–20, both families (owner decision 2026-09-27; `docs/licensing/srd-pack-review.md`):**
  - **Classes:** Bard, Cleric, Druid, Paladin, Ranger, Sorcerer, Warlock and Wizard. Each has every class feature from level 1 to 20, its SRD subclass, its spellcasting tables, and its multiclass prerequisites and proficiencies.
  - **Resources:** the main per-rest ones are tracked (Bardic Inspiration, Channel Divinity, Wild Shape, Lay on Hands, Sorcery Points, Arcane Recovery and others). Other features are shown as text.
  - **2014/2024 differences, as content:**
    - 2024 Paladins and Rangers cast from level 1;
    - 2014 prepared casters use a formula and 2024 casters a table;
    - 2014 known casters have fixed spell counts.
- **The SRD weapon tables and the Barbarian's multiclass data (`docs/licensing/srd-pack-review.md`):**
  - **Weapons:** the 37 SRD 5.1 and 38 SRD 5.2.1 weapons are items with their damage, properties and range (and the 2024 mastery property's name), ready to equip and attack with.
  - **Barbarian:** a new revision of the SRD Barbarian in each family records its weapon proficiencies, its multiclass prerequisite (Strength 13) and its multiclass proficiencies. Under 2024 rules, a later-class Barbarian gains martial weapons but not simple ones.
  - **Pickers:** the builder and the equipment list offer only the newest revision of each content. Older revisions stay for the characters that use them.
- **The SRD spells, both families (owner decision 2026-09-27; `docs/licensing/srd-pack-review.md`):** all 319 SRD 5.1 spells and all 339 SRD 5.2.1 spells ship as content (CC-BY-4.0, attributed like the other SRD content). Each has its level, school, casting time, range, components, duration, class lists, attack or save, base dice and full description, with its page. They are in two new bundled packs that share each family's source record. A caster lists them in the builder once the SRD caster classes are bundled (next).

- **Short rest, hit dice, death saves and inspiration (D01 follow-up, owner 2026-09-27: SRD rules, previewed; SPEC C-05; `docs/features/rests.md`, `docs/features/sheet-play.md`):**
  - **Hit dice:** the sheet shows the hit dice left per die size. "Short rest…" spends the hit dice the player picks, each rolled in TomeStack or entered from the table, and shows the hit points each restores (roll plus the Con modifier). Short-rest recoveries such as the 2024 Rage are ticked changes, as on the long rest. The long rest now also gives spent hit dice back.
  - **Death saving throws:** they appear at 0 hit points. Roll one or enter a physical roll, and TomeStack records the SRD outcome (a 1 is two failures, a 20 regains 1 hit point). "Add a failure" covers damage at 0. Regaining hit points clears them.
  - **Inspiration:** Inspiration (2014) or Heroic Inspiration (2024) is a checkbox.
  - Every change is still confirmed.
- **Three new rules-family differences, tested side by side:**
  - `LongRestHitDice`: half the hit dice (2014) or all of them (2024) come back on a long rest.
  - `HitDieHealingMinimum`: 0 (2014) or 1 (2024) hit point per die.
  - `ShortRestNeedsOneHitPoint`: no (2014) or yes (2024).
  - A long rest at 0 hit points is refused under both families, as the SRDs say (`rest.needs-hit-points`).

- **Spellcasting engine (D04; `docs/features/spellcasting.md`):**
  - **Casters:** a class (or subclass) can declare spellcasting with its ability, prepared or known spells, a spell list, and slot and count tables by class level (spell slots or Pact Magic).
  - **Spells** are a content kind with level, lists, attack or save, and dice.
  - **Sheet fields:** spell attack bonus, spell save DC, spell slots per level and Pact Magic slots, each traced and overridable.
  - **Builder:** picks spells per caster, from the caster's list and castable levels, with the counts shown. Going over a count is flagged, not blocked.
  - **"Spells and slots" panel:** spend and regain slots, cast (spends the lowest free slot), roll a spell's attack or dice (rolling spends nothing), and mark spells prepared.
  - **Rests:** the long rest restores slots; both rests restore Pact Magic slots.
  - **A second caster** is calculated separately. Combined multiclass slots are an assisted field; record the total as an override.
  - **Fixtures:** development builds get original fixture casters and spells. The SRD casters are not bundled yet.

- **Multiclass prerequisites, proficiency subsets, weapons and attacks (M2 item 2; D04; SPEC C-02, C-04; `docs/features/multiclass-and-attacks.md`):**
  - **Multiclass prerequisites:** a class can declare them ("Strength 13 or Dexterity 13"). With two or more classes, an unmet one warns on the class.
  - **Proficiency subsets:** grants and choices can apply only to the starting class (saving throws, the full skill choice) or only to a later class (the multiclass subset).
  - **Weapons:** items can be weapons. Each equipped weapon gives an attack with to-hit and damage (finesse uses the better of Strength and Dexterity, versatile has two-handed damage), traced and rollable with advantage, disadvantage and critical hits.
  - **Proficiency not recorded:** when no content records weapon proficiencies, the attack says so and leaves the bonus to the player.
  - **"Attacks and actions" panel:** feature rolls are grouped by action, bonus action, reaction and other.
  - **Fixtures:** original fixtures only; the SRD weapon table comes with the SRD content.

- **Import PDF pages as reference (M2 item 4; SPEC I-01, I-03; owner decision 2026-09-27: no text extraction in M2; `docs/features/pdf-attachments.md`):**
  - **Import:** on the Sources screen, a page range or the whole document of your own source's PDF becomes a draft reference entry that cites the pages. Nothing is read from the PDF.
  - **Review:** you review and publish it in the homebrew studio, where it can also get effects by hand; until then it does nothing.
  - **Play:** pinned, it shows "Open …, p. N" on the sheet.

### Fixed

- **The builder no longer shows choice options as "Missing content" while they load.** The choices appeared before the option list had arrived, so options were briefly disabled and named by id. The bundled spells made the list big enough for this to show up in the e2e test. A choice now says "Loading the options…" until the list is there.
- **Attaching a PDF in browser development no longer drops the file.** The Sources screen recorded which source the browser file picker was for only after the host's "no native dialog" reply, so a file picked before that was silently ignored. This was the cause of the intermittent e2e failure "attaches a PDF…". The target is now recorded when the button is pressed.

### Changed

- **The builder's "Other content" lists only content that is picked directly** (feats, items and the like), not class features or skill options, which arrive through their class. Content listings carry only the first 200 characters of each summary. With the bundled SRD classes, the full list made the builder slow.
- The "Remove PDF" confirmation lists the entries that cite the PDF in alphabetical order.
- Feature roll buttons moved from the features list to the new "Attacks and actions" panel, with the "Critical hit" toggle.

### Migration

- **Content schema v5** (`docs/schemas/content-revision.v5.schema.json`) adds the `spellcasting`, `spell` and `weapon` effects, the spell fields, weapon proficiency grants, `onlyAs`, restriction `multiclass` and `group`, and roll `activation`. The new fields on existing effect types are optional and absent by default, so existing revisions are unchanged. They are typed only in v5 revisions; in older ones they stay unknown and unchanged. No revision is upcast, and no database migration is needed. New revisions are written as v5, and 0.2.1 refuses them.
- **Character schema v6** (`docs/schemas/character.v6.schema.json`) adds `spells` and spent spell and Pact Magic slots. v1–v5 characters are upcast with none. There is no database migration.
- **Character schema v5** (`docs/schemas/character.v5.schema.json`) adds `play.hitDiceSpent`, `play.deathSaves` and `play.inspiration`. v1–v4 characters are upcast on read with nothing spent, no saves and no inspiration. There is no database migration, because characters are unhashed JSON. 0.2.1 refuses v5 characters and packages that contain them (`character.schema-unsupported`, `package.schema-unsupported`).

## 0.2.1 (M2 in progress; items 1–7)

The build handed over after M2 items 1–7 (ADR-008: PATCH for a build given to a user; MINOR when M2 is delivered).

### Added

- **Campaign profiles (M2 item 7; SPEC P-01; BACKLOG B12; `docs/features/campaigns.md`):** a Campaigns screen holds local profiles with a rules family, the allowed sources and house-rules notes. The builder picks a campaign, and content from other sources is listed as "not allowed in this campaign", so two profiles show different allowed content. Using such content needs a reason, which is recorded as an exception on the character (`campaignExceptions`). The sheet shows the campaign and warns about content outside it. Campaigns never change calculation, and they travel in packages.
- **Builder (M2 item 1; SPEC C-01, C-07; `docs/features/builder.md`):** create a character, level it up (in an existing class or a new one), and answer every choice it offers, including a subclass at its level. Every flow is a draft that the service previews (`character.preview`, `character.previewChoice`) without writing anything. It is saved in one step, or discarded with Cancel. Unresolved choices are flagged, and the sheet's "Choices to make" opens them in the builder. `character.create` also takes `classes` and `choices`.

- **Sheet for play (M2 item 2; SPEC C-04, C-05, I-05; `docs/features/sheet-play.md`):** a features list with text, source and automation status (pure text is shown as reference only). Resources show current/maximum, with the maximum calculated in the rules core and traced (Rage 3 at Barbarian 3). Hit points, temporary hit points, spent uses, conditions and exhaustion are stored on the character and change only through the confirmed `character.play` command. Every check, save, skill and initiative can be rolled (normal, advantage, disadvantage), and so can feature rolls (with critical doubling). The roll record shows each die, modifier and the source. Rolling never spends a resource; a linked resource gets its own "Spend" button.

- **Long rest (M2 item 3; SPEC C-05; D01 decided: long rest only in M2; `docs/features/rests.md`):** "Long rest…" previews every change (hit points to maximum, temporary hit points cleared, each resource by its long-rest recovery, one exhaustion level). The player unticks what does not apply, then confirms. `character.restPreview` writes nothing, and `character.rest` needs `confirm` and the current preview (`rest.preview-stale` otherwise). Recoveries it cannot calculate, and spent resources without a recovery rule, are listed as manual steps instead of being skipped silently. A new rules-family difference: under 2014 rules, the exhaustion reduction needs food and drink (`RulesFamilyPolicy.LongRestExhaustionNeedsFoodAndDrink`).

- **Equipment groundwork (M2 item 4; `docs/features/equipment.md`):** characters carry items and equip them (`equipment`, character schema v4). A new `armor` effect type covers light, medium and heavy armor and shields. Worn armor sets the Armor Class base, and while it is worn Unarmored Defense and other alternatives are traced as not used; a shield adds its bonus. The sheet has an Equipment panel. The SRD armor table is not bundled yet, because it needs the SRD pack review; development uses original fixture armor.
- **Ability scores stop at 20 (owner decision 2026-09-27):** bonuses cannot raise an ability score above 20 in either family, and the trace says when a bonus was capped. `set` effects and overrides may exceed it.

- **Homebrew studio (M2 item 5; SPEC I-04, I-06; `docs/features/homebrew-studio.md`):** create a personal homebrew source (not shareable by default), then author subclasses, features, feats and items with guided controls: modifiers, resources, recoveries, rolls and limited-use actions, granted features, armor, and reference-only text. Check, save drafts and publish. A homebrew subclass can be offered in an SRD class's subclass choice (content schema v4 `extendsChoice`). After publishing, the studio lists the characters on an older revision and opens a review (rule changes, values that change, overrides, open choices) with "Apply update". New commands: `source.list`, `source.createHomebrew`, `content.bySource`. `app.info` lists the calculated fields.

- **PDF attachments and page navigation (M2 item 6; ADR-005; SPEC S-04; `docs/features/pdf-attachments.md`):** a Sources screen attaches a PDF to any source. It is copied into the data folder by default (read-only, stored once per content hash), or linked where it is with a hash check on open. A feature that cites a page gets "Open …, p. N", which opens that page in the desktop app's offline PDF viewer window. Removing a PDF first says which entries cite it, and keeps all content. Exports never include PDFs or attachment ids. `--smoke` now also opens a generated PDF in the viewer, offline.

### Changed

- The new-character form is replaced by the builder: species, background and starting class are single picks, and "Next: choices" comes before "Create and save".

### Fixed (M2 review)

- **`armor` is content schema v4 only (ADR-003).** A revision stored by 0.2.0 with a `"type": "armor"` effect (an unknown effect then) was read as typed armor. Its hash changed, so re-importing the same package failed (`package.revision-conflict`), and published content started changing Armor Class. In a v2 or v3 revision, armor now stays unknown, reference-only and byte for byte. Validation refuses armor below v4 (`validate.requires-v4`). The original fixture armor is republished as v4 revisions (new revision ids; the content ids are unchanged).
- **The ability score cap no longer depends on effect order.** Increases apply first (capped at 20), then penalties. Before, 19 with +2 and −2 gave 18 or 19 depending on which effect came first; now it is 18 either way.
- **`character.save` no longer writes the play state (SPEC C-05).** It took the payload's `play` as is, so any save, including one from a stale copy, could change hit points, spent uses or conditions without a confirmation. It now keeps the stored play state; only the confirmed `character.play` and `character.rest` change it.
- **Removing or replacing a PDF that is open elsewhere now succeeds (ADR-005).** The change was committed, but the reply was an internal error and the unused copy was left behind. The file is now deleted best effort, and the next start deletes managed copies that no attachment uses, plus leftover `.partial` files.
- **Large libraries no longer slow every click.** Each calculation re-read the whole content table for every offered choice to find `extendsChoice` options: 4 table scans per calculation, and about 430 ms per play command with 5,000 revisions. The published extensions are now read once and kept in memory until a revision is added or a transaction rolls back, which brings a play command to about 5 ms. No database migration.
- **The PDF viewer loads only the one PDF (ADR-005).** It mapped the PDF's folder to its own host and allowed anything there. A link inside a linked PDF could open other files next to it, such as an HTML page in Downloads, with scripts on. Now no folder is mapped: the window loads exactly `https://pdf.tomestack.localhost/document.pdf#page=N`, answers it with the PDF's bytes, and refuses and reports everything else.
- **Changing the rules family drops picks that no longer fit.** In the builder, a species, background, class or other pin from the old family stayed checked but disabled, and was then silently not applied. In the campaign editor, allowed sources from the old family were hidden but still saved. Both are now dropped when the family changes, as the M1 form did.
- **Renaming a resource in the homebrew studio no longer unlinks it.** Its id followed the name, so a recovery or roll added earlier pointed at the old id: the resource never recovered, and validation only warned. The id is now set once, when the resource is added.

### Migration

- **Database schema 4** (M2 item 7): adds the `campaigns` table. An M1 data folder (schema 2) upgrades through 3 and 4 in one start, after one backup, `tomestack.db.v2.bak`.
- **Package format v4**: `campaigns/` entries. Older builds refuse v4 packages.
- **Database schema 3** (ADR-005): `tomestack.db.v2.bak` is written first. The migration adds the attachments table and turns each source's `pdfRef` into an attachment: a managed copy when the file is a readable PDF, otherwise linked and shown as missing. The old value stays in `sources.legacy_pdf_ref`. Opening a schema-3 data folder with an older build is refused with "update TomeStack", and nothing is changed.
- **Content schema v4** (`docs/schemas/content-revision.v4.schema.json`) adds the `armor` effect and `extendsChoice`. New revisions are written as v4. v2 and v3 revisions, including the bundled SRD packs, keep their version and hashes, so there is no database migration. Older builds refuse v4 revisions.
- **Character schema v4** (`docs/schemas/character.v4.schema.json`) adds `play` and `equipment`. v1–v3 characters are upcast on read with a fresh play state. There is no database migration, because characters are unhashed JSON. Builds before this one refuse v4 characters and packages that contain them, with a clear message.

## 0.2.0 (M1 delivered)

### Added

- **M1 exit gate passed (ROADMAP M1; `docs/features/m1-acceptance.md`):** two level-3 SRD characters, Korga (SRD 5.1 Half-Orc Acolyte Barbarian, Berserker) and Brenna (SRD 5.2.1 Dwarf Soldier Barbarian, Berserker), calculate the expected values. Every major number explains itself: sources and pages, rules steps, and an override that keeps the calculated value. The executable acceptance test is `M1AcceptanceTests`. The version is 0.2.0 (ADR-008 policy: MINOR goes up at milestone delivery).

- M0 foundation: repository layout, .NET 10 solution, React/TypeScript UI, CI workflow on Windows.
- Rules core with the `srd-5.1` and `srd-5.2.1` rules-family IDs and an explicit policy difference: ability score increases come from species in 2014 rules and from background in 2024 rules (SPEC S-02, C-01).
- Initiative is calculated with a source-aware trace. Each step lists its operation, amount, result, and the rules family, content revision, effect, source title and page behind it. Invalid, draft, wrong-family and missing content is isolated with diagnostics (SPEC C-03).
- Labeled user overrides are the final layer; the calculated value and its trace are kept (SPEC C-06).
- Local SQLite storage with numbered migrations and a backup before schema upgrades. Published content revisions are insert-only (SPEC I-06, ADR-002).
- Portable package export/import (format v1). It includes pinned revisions, sources, license notices and SHA-256 hashes, and imports go through a preview step. Packages are restricted to a fixed layout with size limits (SPEC P-02, Q-02; docs/features/package-format.md).
- Import-worker contracts. Candidates can only become inactive drafts (SPEC I-01).
- WPF + WebView2 desktop shell. It uses an in-process service over the WebView2 message bridge, blocks non-app network requests, and has a `--smoke` self-test (ADR-006).
- Original M0 test fixtures for both rules families (no SRD or third-party text).
- `ATTRIBUTION.md` lists the licenses of the third-party components that ship. ADR-007 (proposed) covers export and license policy. The SRD 5.1 and 5.2.1 CC-BY-4.0 attribution statements are drafted verbatim from the official documents for owner approval (`docs/licensing/srd-attribution-draft.md`). There is still no SRD content.
- The project's code is licensed under Apache-2.0: `LICENSE` and `NOTICE` at the repo root (LIVING_SPECS D07, owner decision 2026-09-26).
- `--smoke` also proves the M0 exit gate in the built app: it creates a character with fixture content, exports it and previews the package. The report includes `charactersAtStart`, so two runs on one data folder prove persistence. `scripts/smoke.ps1` checks the report, and `scripts/offline-check.ps1` covers a simulated missing WebView2 runtime and a manual airplane-mode run (ADR-006).
- UI flow test (`npm run test:e2e`) drives create → sheet → override → export → import, plus keyboard-only access, against the real DevHost. It uses Vitest with Testing Library and jsdom, which are dev-only. It is part of the gate and CI. There is an accessibility checklist for the working-default target (formal target D05 still open): `docs/features/accessibility-checklist.md`.
- ADR-003: a typed declarative effect model with explicit stacking (`stack` / `highestInGroup`), operations (`bonus` / `set` / `replace`) and timing, plus the bounded formula grammar (SPEC I-04, I-05, Q-02).
- A bounded formula parser and evaluator in the rules core (ADR-003 grammar: `+ - * /`, parentheses, `floor`, `ceil`, `min`, `max`, `abs`, and `PB`, `LEVEL`, `CLASS_LEVEL`, `<ABL>.MOD`, `<ABL>.SCORE`). Limits: 200 characters, 64 tokens, depth 8, literals ≤ 10,000, intermediates within ±1,000,000. A failing formula disables only its own effect, with an `effect.invalid-formula` diagnostic that names the feature, the effect and the error code (SPEC C-03, Q-02).
- Dependency-ordered calculation (item 11). The sheet has ability scores and modifiers, a proficiency bonus from level, all six saving throws, Stealth and initiative. Each field is a node in a graph whose edges are its base inputs plus every identifier its effects' formulas read. `grant` effects give proficiency or expertise. Stacking follows ADR-003 (highest `replace`, then bonuses with `highestInGroup`, then highest `set`). A formula that would create a cycle (a self-loop, or score → modifier → score) disables only the effects in the cycle, with `effect.dependency-cycle` naming the path; an effect that only repeats a base dependency is never disabled (SPEC C-02, C-03). A bonus that would take a value outside ±1,000,000 is not applied (`effect.out-of-range`). A field is `assisted` rather than `automatic` when it or an input has an effect the calculator could not apply.
- A second explicit rules-family difference, `RulesFamilyPolicy.BackgroundGrantsFeat`: 2024 backgrounds may grant a feat, 2014 backgrounds may not (`policy.background-feat`). Only feats are restricted; a 2014 background may still grant other content, such as a feature. `grant` effects of kind `content` bring in another revision, one level deep only (`grant.nested-ignored`), and the trace says "granted by …".
- Per-character cross-family exceptions (BACKLOG B06), stored as data (`crossFamilyExceptions`, with a required reason). Recorded content applies under the character's own family with a `content.cross-family-exception` warning. The UI for recording one is still to come.
- Original M1 fixtures (`tests/RulesFixtures/fixture-pack-m1.json`, test-only) with a deliberate cross-edition conflict: two different revisions named "Fixture Keen Senses". Side-by-side tests show the same inputs giving different, explained outputs per family (`tests/RulesFixtures/README.md`).
- Dice engine skeleton in the rules core (`docs/features/dice-engine.md`): bounded `NdM±K` expressions, advantage and disadvantage on a single d20, critical dice doubling, a seeded RNG for tests, and a CSPRNG for play. Roll records hold the formula, every die, modifiers with origins, and provenance. Rolling never consumes a resource (SPEC C-04). A request with both advantage and critical doubling is refused rather than silently dropping one. There is no UI yet.
- **Installer (ADR-008, accepted):** a per-user, self-contained, unsigned Velopack installer built by `scripts/pack-installer.ps1`, with `vpk` pinned as a repo-local tool in `.config/dotnet-tools.json`. The pack id is `TomeStack.App`, so uninstalling cannot delete the data folder `%LOCALAPPDATA%\TomeStack`. `LICENSE`, `NOTICE` and `ATTRIBUTION.md` ship next to `TomeStack.exe`. `scripts/installer-smoke.ps1 -Adapter Velopack` proves install → smoke → upgrade from a schema-1 build (with `tomestack.db.v1.bak`) → uninstall that keeps the data, under Windows PowerShell 5.1 and pwsh 7. The version policy is in ADR-008: `Directory.Build.props` is the single source and must go up with every build given to a user. This build is 0.1.1.
- ADR-008 first set out the installer requirements and the options. It records two data-loss risks: Velopack's default install folder is the data folder, and MSIX virtualizes AppData. `scripts/installer-smoke.ps1` is an installer-neutral install → smoke → upgrade → smoke → uninstall harness. Only an Xcopy adapter exists so far.
- ADR-001 (local-only Windows, accepted), ADR-004 (review before publish, accepted; evidence is the quarantine and draft tests) and ADR-005 (managed PDF copy vs. link).
- **Owner decisions (2026-09-26):** ADR-001 accepted with Windows 10 best-effort. ADR-005 accepted (D02): the data folder is `%LOCALAPPDATA%\TomeStack`, PDFs are managed copies by default, and the `pdfRef` → attachment migration is planned for M2. ADR-007 accepted (D03 and the SRD route: CC-BY-4.0 for both SRDs). The accessibility target is WCAG 2.2 AA for the sheet and builder (D05), and the owner's keyboard walkthrough passed.
- **Backup vs. share export (ADR-007, SPEC P-02, Q-03):** the sheet's export asks what the package is for. A *personal backup* includes everything and is named `…-personal-backup.tomestack.zip`. A *share* leaves out content from non-redistributable sources and lists what it left out before exporting (`package.exportPreview`) and in the manifest (`omitted[]`). The receiver gets a `package.content-omitted` warning naming the source and publisher, and the sheet shows that content as missing.
- **Sync-root warning (ADR-005):** if the data folder is inside OneDrive or another cloud sync root, the app shows a warning at the top of the window (`app.info` `warnings`, code `data-dir.sync-root`).
- Private homebrew fixtures (the owner's Stardust Guardian material) go in the gitignored `tests/RulesFixtures/local/`.
- **Levels and classes (M1 item 5, SPEC C-01, C-02, C-03; `docs/features/levels-and-classes.md`):** characters record levels per class. The total level is their sum and drives the proficiency bonus. `CLASS_LEVEL` resolves to the level in the class that content belongs to. Grants can apply from a class level (`grant.level`), so a level-3 feature is absent at level 2. New fields, each with a full trace:
  - **Hit points:** the starting class's hit die maximum, then the fixed value per level, plus Constitution × level. Traces cite the class, source and page.
  - **Armor class:** 10 + Dex, with `replace` alternatives; the highest wins.
  - **All 18 skills.**

  Original fixtures "Fixture Warden" and "Fixture Scholar" cover multiclass hit points and level gates. D04 (multiclass and spellcasting scope for MVP) is recorded in LIVING_SPECS.
- **SRD packs (M1 item 1, SPEC S-02, Q-03; ADR-007; `docs/licensing/srd-pack-review.md`):** SRD 5.1 and SRD 5.2.1 ship as two separate CC-BY-4.0 source packs, with separate sources, content IDs and revisions. Each source carries the approved attribution verbatim, a CC-BY §3 `modificationNotice` (which also travels in package notices) and the checked PDF's SHA-256. The slice per family:
  - one species (Half-Orc / Dwarf);
  - one background (Acolyte / Soldier, with its ability options and origin feat);
  - the Barbarian at levels 1–3, with its skill choices, level-gated features and the Path of the Berserker;
  - one feat (Grappler with its Strength 13 prerequisite / Savage Attacker).

  Every revision validates, and `SrdPackTests` checks the attribution against the approval page. `NOTICE` and `ATTRIBUTION.md` carry the statements.
- **Roll command (M1 item 7, SPEC C-04; `docs/features/dice-engine.md`):** `roll` rolls a content roll effect that applies to the character, or a sheet field (ability check, save, skill, initiative) as a d20 test. It returns a roll record with formula, dice, modifiers with their origins, and provenance (revision, source, page). It never changes the character and never spends a linked resource. The sheet now lists its active revisions (`active`).
- **Publishing and pin updates (M1 item 2, SPEC I-06; `docs/features/publishing-and-updates.md`):**
  - `content.saveDraft` stores an inactive draft. `content.publish` re-validates it and inserts a new immutable revision (same content id, new revision id), leaving the draft and older revisions untouched.
  - `content.revisions` lists a content id's history. `content.affected` lists the characters that use it, and how (pin, class, choice, or grant).
  - `character.reviewUpdate` shows the mechanics diff and recalculated fields without changing anything. `character.applyUpdate` moves the character only with an explicit `confirm`, carrying choices over and keeping overrides.
  - An updated character round-trips through a package.
- **Rules-family review for the SRD slice (M1 item 6; `docs/features/rules-family-policy.md`):** the slice needs no new `RulesFamilyPolicy` field. Every difference it exercises is content, or already covered by `AbilityIncreaseSource` and `BackgroundGrantsFeat`. New side-by-side tests use real SRD content: Half-Orc Strength under 2014 vs. 2024 rules, the Soldier's ability option and feat, the Acolyte's feature, and same-named Barbarians never merged. A cross-family exception recorded for a chosen option now admits it too.
- **Restrictions and validation (M1 item 3; `docs/features/validation-and-restrictions.md`):**
  - `restriction` effects are prerequisites. Content whose prerequisite is not met is not applied, with a `restriction.unmet` diagnostic scoped to it. Prerequisites are checked without the content itself, so a feat cannot qualify itself.
  - `content.validate` (`ContentValidator`) reports schema, reference, formula and dependency-cycle problems for a stored or unsaved revision before publish. Errors block publishing; warnings do not.
- **Choices (M1 item 4, SPEC C-01; `docs/features/choices.md`):** characters store selections for `choice` effects. Counts are enforced, and options must be listed and usable under the character's rules family. Every offered choice appears on the sheet, and unresolved ones are flagged ("Choices to make"). Chosen content becomes active with a "chosen from" trace; a chosen subclass follows its class level. `character.choose` records a validated selection. A feature chosen from a species or background follows that origin's ability-increase policy.
- JSON Schemas for source, content revision, character and package manifest (v1, plus v2 for content revisions, characters and manifests) in `docs/schemas/`. A test validates every fixture and a real exported package against them.

### Changed

- The shipped app seeds the bundled SRD packs and **no longer seeds the original test fixtures**. DevHost and tests still do, and `TOMESTACK_DEV_FIXTURES=1` enables them in the shell. Data folders that already have fixture content keep it. The GUI smoke now checks SRD content (a Half-Orc's Strength +2).
- `character.choose` refuses an option that is already selected for another choice (`choice.option-already-chosen`), as SRD wording such as "another skill" requires.

- Trace shape (ARCHITECTURE step 5): a field's trace now includes the steps of every field it reads, in dependency order. Each entry names its `field` and the `inputs` it read, and each value has `units`. Initiative gains an explicit "starts at the Dexterity modifier" step. A field's warnings include its inputs' warnings, so an ignored Dex increase still explains initiative. Overrides stay the final layer, and dependents read the overridden value.
- The ability-increase policy now restricts only *origin* content (species and background). Feats and class features may raise scores under both rules families; M0 blocked them by mistake. For origin content it covers every operation, so a `set` or `replace` on an ability score cannot bypass it.
- The sheet groups its fields and shows each one as an expandable card with its own override form. Override inputs are no longer shared between fields, and derived values no longer use `<output>` (an implicit live region).
- Effect values are formulas. The M0 rule "amount must be between -10 and 10" (`effect.invalid-amount`) is replaced by the formula bounds and `effect.invalid-formula`.
- CI records evidence for the GUI smoke (a WebView2 Runtime probe and the session), runs it through `scripts/smoke.ps1`, adds the missing-runtime check, and uploads the smoke report. The smoke stays non-blocking until it passes on hosted runners (ADR-006). `workflow_dispatch` was added for manual runs.
- Export in the desktop app opens a native Save dialog provided by the shell (`package.saveAs`) instead of WebView2's download flow, which saved silently to Downloads. Browser development against DevHost still falls back to a download (ADR-006).
- D06 resolved: the application service runs in-process behind the WebView2 message bridge instead of as a local ASP.NET Core service. The loopback host is development-only (ADR-006).
- Spec documents moved into `docs/`, and the diagram into `docs/diagrams/`.

### Fixed

- **M1 review (SPEC Q-02, C-03; ADR-004):**
  - Untrusted JSON with empty (null) list entries got an internal error instead of a diagnostic. Examples: `pins: [null]` on `character.save`, `effects: [null]` on `content.validate`, and a packaged character with `pins: [null]` on `package.preview`. Worse, `character.save` stored such a character before failing, and it could not be opened again. Now characters report `character.empty-entry` and revisions `validate.empty-entry` (also on `content.saveDraft` and import), and `character.choose` reports `choice.empty-entry`. `character.save` calculates the sheet before it writes, so nothing is stored on failure.
  - A published revision inside a package was imported without content validation, so, for example, a d1000000 hit die or a choice with count -1 became active. New published revisions in a package are now validated like `content.publish`. For content schema v3, which TomeStack only publishes after validation, errors block the import. Older revisions (published by v0.1, before validation) import with the problems as preview warnings. A reference to content that is missing on this machine is always a warning, because a share package may leave it out. The calculator also isolates such content wherever it comes from: a choice count below 1 (`choice.invalid-count`) and a hit die outside d6–d12 (`class.hit-die-invalid`) are reported, not applied.
  - `RulesFamilyPolicy` could be bypassed through choices and chains. Under SRD 5.1, a background could offer a feat as a choice, or offer a feature that grants one. A background could also grant a feature whose chosen option raised an ability score. Origin (species or background) now carries along any chain of features, so both `BackgroundGrantsFeat` and `AbilityIncreaseSource` hold however the content is reached (`policy.background-feat` now also covers "cannot offer a feat").
- The shell showed an unhandled exception when its UI bundle was missing. It now shows an error and exits cleanly (found by the spike's negative control).
- After an import, the summary (including where the replaced character was backed up) was cleared as soon as the character opened. It now stays visible. Found by the UI flow test.
- In dark mode, error, accent and warning text failed 4.5:1 contrast (2.9, 3.0 and 3.5 to 1). They now use `light-dark()` shades at 7.8 to 10.1 to 1.
- The database backup before a schema upgrade used to copy only `tomestack.db`. In WAL mode, committed data can still be in `tomestack.db-wal` after a crash, so the backup could silently miss it. It now uses SQLite's online backup API (`UpgradeTests`). Opening a data folder from a newer build now shows a clear "update TomeStack" message and changes no data.
- A character or content revision with a `schemaVersion` newer than the build supports used to be accepted silently. It is now refused on import (`package.schema-unsupported`) and on save (`character.schema-unsupported`), and the calculator isolates it (`content.schema-unsupported`).
- Package import used to replace an existing local character after only a preview warning. It now first saves the local copy to `backups/pre-import-*.tomestack.zip`, and importing that file restores it. If the backup cannot be written, the import is refused (SPEC C-07, Q-01; restore test in `PackageRoundTripTests`).
- Package import used to upsert source records, which could silently overwrite local license and redistribution metadata. The preview now shows a field-by-field diff for each differing source, and apply requires an explicit "keep local" or "use imported" choice per source (SPEC S-01, Q-03).
- Exports no longer include a source's `pdfRef`. It is a machine-local path that can contain the Windows user name. Imports never change the local `pdfRef` either.
- Package import bounds the total unpacked size (64 MB, `package.content-too-large`) and checks every entry name before unpacking anything. Before, 2,000 entries of 5 MB each could make a small crafted package exhaust memory. Every package limit and check now has its own test.
- A malformed effect in an imported revision (for example a schema v1 effect without an `id`) failed the import with an internal error. It is now kept as a reference-only effect, and any other reader failure is reported as `package.invalid-json`. v1 effects the v1 build could not have written are no longer mapped with data dropped (ADR-003).
- Export from the desktop app timed out after 30 seconds if the Save dialog stayed open, and reported a failure even though the file was then saved. The UI now waits for the dialog.
- `srd-5.1` blocked every content grant from a background as "cannot grant a feat", including non-feat content such as a background feature.
- Many bounded bonuses on one field could overflow its value (for example, 2,200 bonuses of 500,000 gave initiative −2,094,967,296).
- A dependency cycle also disabled an effect elsewhere in the same component that could not have caused it.
- Derived values always reported `automatic`, even when an effect on them was not applied.
- The UI lint rule caught only a bare `fetch`. It now also blocks `window.fetch`, `XMLHttpRequest`, `WebSocket`, `EventSource` and `window.chrome` outside the transport.
- `scripts/smoke.ps1` removes the throwaway data folder the app creates when no `-DataDir` is given.
- The missing-runtime check (`offline-check.ps1 -Mode MissingRuntime`) failed on a hosted GitHub runner: the WebView2 loader ignored `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` there. It now uses a smoke-only shell flag, `--simulate-missing-webview2`, that takes the same not-found path on any machine. It is proven under Windows PowerShell 5.1 and pwsh 7. The loader-override variant stays as a local diagnostic (`-Mode MissingRuntimeLoader`), and the smoke report records which loader overrides the process saw (ADR-006).
- Opening a sheet left keyboard focus on the page body. It now moves to the sheet's heading (WCAG 2.4.3, accessibility checklist #9).
- "New character" did nothing when clicked before the app had loaded its rules families. It is now disabled until then (checklist #11).
- Every commit warned about CRLF line endings. `.gitattributes` now keeps LF in the repository and in working copies.
- Unexpected command failures no longer send the exception message to the UI, because it could contain file paths or internals. The UI gets a generic message and a correlation id. The details (including the stack) go only to `<data dir>/logs/errors.log`, which rolls over at 1 MB (SPEC Q-02).

### Migration

- Database schema v1 (new). Package format v1 (new).
- **Content schema v2 (ADR-003):** effects are a typed union (`modifier`, `grant`, `resource`, `choice`, `restriction`, `recovery`, `roll`). v1 `abilityScoreIncrease` and `initiativeBonus` map to `modifier` bonuses on read. Unknown effect types are kept unchanged (whitespace and escaping normalized) and stay reference-only.
- **Database schema v2:** stored revisions are rewritten in the v2 representation, with new hashes and the original JSON in `legacy_json`. `tomestack.db.v1.bak` is written first. Without this, M0 data folders would have failed to open.
- **Character schema v2:** adds `level` (1–20) and `crossFamilyExceptions`. v1 characters are read as level 1 with no exceptions.
- **Package format v2:** content entries are schema v2. v1 packages still import. Older builds refuse v2 packages with a clear message.
- **Character schema v3:** adds `classes` (levels per class) and `choices` (selections). v1 and v2 characters are read with neither, which changes nothing.
- **Content schema v3 (ADR-003):** adds `grant.level`, `choice.level`, the `hitDie` effect, and the `armorClass` and `hitPoints` targets. v2 revisions are **not** upcast (v2 is a subset of v3), so stored hashes are unchanged and no database migration is needed. v1 still upcasts to v2. Builds that know only v2 refuse v3 revisions.
- Exports now also include content that included content grants (for example class features or a background's feat), so the receiving machine calculates the same sheet. Before, a granted revision that the receiver lacked showed as missing.
- **Package format v3 (ADR-007):** the manifest adds `purpose` (`backup` / `share`) and `omitted[]`. v1 and v2 packages import as backups. Older builds refuse v3. Export file names change: a backup is `<name>-personal-backup.tomestack.zip`.
