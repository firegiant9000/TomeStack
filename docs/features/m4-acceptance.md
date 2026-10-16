# M4 acceptance: Import intelligence, exit evidence

ROADMAP M4 exit gate: "A third-party test PDF produces reviewable candidates; no unapproved active rules" · SPEC I-01, I-02, I-03, Q-02 · ADR-004, ADR-009 · build **0.3.0** · status: **engineering done; gate not met.** The third-party test PDF was not on the development machine (2026-09-28), so its run is still owed. Following ADR-008, the version stays 0.3.0 until the gate is met. **Experimental until the gate passes:** the UI labels "Read the text and find candidates" experimental, and so do the README and ROADMAP. The label comes off only when both a real third-party PDF run and the SRD precision/recall floors pass, with dated counts recorded below. The fixture book alone does not qualify. Evidence level: fixture-verified (the original fixture book through the real worker in the gate).

Automated means it runs in the gate (CONTRIBUTING.md): `dotnet test`, `npm run test:e2e` against the real DevHost and worker, and `scripts/smoke.ps1` on the built shell.

## ROADMAP deliverables

| Deliverable | Executable acceptance | Status |
| --- | --- | --- |
| Page and whole-book extraction (text, layout, page coordinates) | `ExtractionTests`, `WorkerProcessTests`; the smoke imports a PDF through the shipped worker | ✅ |
| OCR fallback | `OcrTests` (Windows OCR on an image-only page, in the process and in the worker) | ✅ on this machine (it has an OCR language); skipped where Windows has none |
| Isolation and limits (SPEC Q-02) | `WorkerProcessTests` (page and run timeouts, memory watchdog, the heap cap reported and checked, line limit, killed on cancel, the bomb must fail); the malformed-input suite and the per-page caps in `ExtractionTests`; `ImportJobTests` (a page that kills the worker fails alone; the detection budget). Review fixes: ADR-009 "Review fixes" | ✅ |
| Cancellable, resumable jobs with progress and a local audit log, surviving a restart | `ImportJobTests` | ✅ |
| Searchable page text within one source (SPEC I-03) | `ImportJobTests.Imported_text_is_searchable_within_its_source`; the e2e search step | ✅ |
| Candidate and entity recognition | `DetectionTests`; `SrdDetectionMeasurementTests` (local, below) | ✅ |
| Confidence and dependency validation | `CandidateReviewTests`, `ImportQuarantineTests` | ✅ |
| Review UI | e2e "reads the fixture PDF, reviews its candidates, and publishes an accepted one through the studio" | ✅ automated · owner ☐: keyboard-only and Narrator pass |
| Extracted text never leaves the machine (ADR-009 (d)) | `ImportJobTests.Extracted_text_is_never_exported_in_a_backup_or_a_share` | ✅ |

## Exit gate runs

### The original fixture book (automated)

`M4AcceptanceTests.The_original_fixture_book_produces_reviewable_candidates_and_no_unapproved_active_rules` imports `tests/RulesFixtures/pdf/fixture-import.pdf` through the real worker process. It then accepts the first candidate of each kind (as reference when a blocker remains), ignores one more, and counts what is active without anyone publishing it. Run on 2026-09-28:

> pages 6 (1 by OCR); candidates 9 (class 1, feature 1, feat 1, spell 2, item 4); accepted 3, accepted as reference 2, ignored 1, pending 3; **active without approval: 0**

Re-run 2026-10-16 (item 11 added a seventh page, a class with a spell table, and the features it names): pages 7 (1 by OCR); candidates 18 (class 2, feature 9, feat 1, spell 2, item 4); 5 accepted (one of each kind, the class only as reference because the harness confirms nothing), ignored 1, pending 12; **active without approval: 0**.

Re-run 2026-10-16 after the Task 5 review fixes: candidates 19 (class 2, feature 10, feat 1, spell 2, item 4); the extra feature is a "Spellcasting" stub, because the SRD installs several features of that name, so it is not one installed feature (R67). 5 accepted as before, ignored 1, pending 13; **active without approval: 0**. The run now also prints class counts: classes 2, hit die 1, saves 1, armor 1, weapons 1, spellcasting 1, subclass choice 1; links 10; uncertainties 9.

Every accepted candidate is a draft, and every effect in it is reference-only.

### The third-party test PDF (local)

`M4AcceptanceTests.A_third_party_test_PDF_produces_reviewable_candidates_and_no_unapproved_active_rules` reads every PDF in the gitignored `tests/RulesFixtures/local/third-party/`. It skips when there is none. Its output, and the `report.md` it writes in that folder, are counts only: pages, candidates by kind, accepted, accepted as reference, ignored, pending, and "active without approval". PDFs are numbered by position, never named.

To run it, put the PDF there and run `dotnet test --filter A_third_party_test_PDF`, then record the counts here. Counts only, never names:

| Date | Build | PDF | Candidates by kind | Accepted | As reference | Ignored | Active without approval |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-10-14, before | branch `wave-0`, before the detector change | 1 | 186: spell 116, item 69, feature 1 | 2 | 1 | 1 | 0 |
| 2026-10-14, after | branch `wave-0`, with the detector change | 1 | 238: spell 116, feat 47, item 66, class 8, feature 1 | 3 | 2 | 1 | 0 |

**2026-10-14 run (a third-party book, local, counts only).** 385 pages, 5 of them by OCR, 0 page errors; extraction about 20 s, detection 0.35 s. The book has 17 fonts; 7 carry a weight word, and 1 of those carries one only as Black, Heavy, Semibold or Demi (about 4,400 letters, none of it counted as bold before), so the extractor now accepts those words as style tokens (not as bare substrings, which would flag faces such as "Academic"); the same 7 fonts are still recognised after that narrowing, and the after-counts below did not move.

- **Before** the change: spell 116, item 69 (2 of them not items), feature 1 (spurious), class, feat, species, background and subclass 0. Spell recall was about 84 % (116 of about 138 spell headers).
- **After** the change: spell 116 (recall unchanged, about 84 %), item 66 (3 fewer: rows whose name held a price or read as a sentence), feat 47, class 8, feature 1. The feats and classes are new and their precision is **not reviewed**: the numbers show only that the detector now finds more, not that what it finds is right. Several of the 47 may be other kinds of entry in the same layout.
- **SRD floors re-run the same day** with both PDFs hash-checked: every floor holds, and every number is the same as before the change (spells 100 % / 100 % on both SRDs; weapons 97.2 % / 100 % (5.1) and 97.4 % / 100 % (5.2.1); armor 100 % / 100 %; classes 83.3 % / 100 %; class features recall 88.2 % (5.1) and 100 % (5.2.1); feats recall 100 % with 1 candidate (5.1) and 17 (5.2.1)). A first draft of the feat rule took the SRDs' Eldritch Invocations for feats (+21 and +23 candidates), and a first draft of the junk-row rule dropped the SRD's "Crossbow, hand" rows (5.1 weapon recall 91.4 %). Both were narrowed until the numbers matched the baseline again.
- **The gate stays not met.** It needs the owner to review precision on the book (accepted and ignored candidates, kind by kind), which this run does not replace.

**2026-10-16 run (the same third-party book, local, counts only; branch `wave-1`, assisted class import, item 11).** 385 pages, 5 of them by OCR; candidates 332: class 8, feature 95, feat 47, spell 116, item 66 (before: 238, with feature 1). The review sample (the first of each kind, a class only as reference because the harness confirms nothing) gave accepted 3, accepted as reference 2, ignored 1, pending 326; **active without approval: 0**. The 94 new feature candidates are mostly reference stubs for the feature names the class tables list, not features found on their own pages, so they are not detection recall and their precision is **not reviewed**. The gate stays not met.

**2026-10-16 re-run after the Task 5 review fixes (the same book, local, counts only).** The test now also prints, for the class candidates, how many carry each proposal, their links and their uncertainties. "Before" is the Task 5 build (`a581a02`) measured with the same counting; "after" is the fixed build.

| | Candidates | Classes | Hit die | Saves | Armor | Weapons | Spellcasting | Subclass choice | Links | Uncertainties | Active without approval |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Before | 332 (feature 95) | 8 | 8 | 8 | 7 | 8 | 0 | 7 | 95 | 16 | 0 |
| After | 336 (feature 99) | 8 | 8 | 8 | 7 | 8 | 0 | 7 | 104 | 16 | 0 |

The other kinds did not move (class 8, feat 47, spell 116, item 66); review sample accepted 3, as reference 2, ignored 1, pending 330. The 4 extra feature stubs and 9 extra links are names that match installed content but not exactly one installed feature in the class's families, which R67 now stubs instead of dropping. No class on this book gets a `spellcasting` proposal before or after: why (no slot table read, or a casting sentence the reader does not match) is **not reviewed**, and neither is the precision of any proposal here.

## Precision and recall (D3)

Measured 2026-09-28 on both SRD PDFs, whose hashes match `licensing/srd-pack-review.md`, against the bundled packs (`SrdDetectionMeasurementTests`; the PDFs stay outside the repository). Details and the match rule are in `pdf-import.md`. The test now fails below floors set a little under these numbers. The floors were added after this measurement, and first ran later the same day (below).

| Kind | SRD 5.1 precision / recall | SRD 5.2.1 precision / recall |
| --- | --- | --- |
| Spells | 100 % / 100 % (levels 319/319) | 100 % / 100 % (levels 339/339) |
| Weapons | 97.2 % / 100 % (damage 35/35) | 97.4 % / 100 % (damage 37/37) |
| Classes | 75 % / 100 % | 75 % / 100 % |
| Class features | n/a / 87.0 % | n/a / 100 % |

The extra weapons (the blowgun) and classes (Fighter, Monk, Rogue) are real SRD entries the packs do not bundle. Feature precision cannot be computed against packs that bundle only some classes. By hand, about 20 of the 179 SRD 5.1 feature candidates are subclass names or sub-section headings. Feats and armor are not fully bundled, so they are counted rather than measured: all 17 SRD 5.2.1 feats, and both SRDs' 13 armor rows.

### The floors ran (2026-09-28, build on branch `m2.2-fighter`)

`SrdDetectionMeasurementTests` ran with `TOMESTACK_SRD_PDF_DIR` pointing to both SRD PDFs, hash-checked again on this date and kept outside the repository. **Every floor passed.** The Fighter and the armor table are now bundled (M2.2), so both count as truth, and armor has its own measurement with a recall floor and an exact Armor Class check.

| Kind | SRD 5.1 precision / recall | SRD 5.2.1 precision / recall |
| --- | --- | --- |
| Spells | 100 % / 100 % (levels 319/319) | 100 % / 100 % (levels 339/339) |
| Weapons | 97.2 % / 100 % (damage 35/35) | 97.4 % / 100 % (damage 37/37) |
| Armor | 100 % / 100 % (Armor Class 13/13) | 100 % / 100 % (Armor Class 13/13) |
| Classes | 83.3 % / 100 % (the extras: Monk, Rogue) | 83.3 % / 100 % |
| Class features | n/a / 88.2 % | n/a / 100 % |
| Feats | n/a / 100 % (1 of 1) | n/a / 100 % (5 of 5) |

The class precision floor went from 70 % to 80 %; the other floors are unchanged. These are the SRDs, the layouts detection was tuned on. **They do not stand in for the third-party run**, which is still owed, so M4 stays experimental and its gate is not met.

## Could not be verified here

- **The third-party test PDF's precision** (above: the run of 2026-10-14 has counts only; the owner has not reviewed the candidates). Detection was written against SRD layouts, so a third-party book will score lower, especially on class features and tables. Every candidate is still only a proposal (ADR-004).
- **The review screen by keyboard only and with Narrator** (`accessibility-checklist.md`, items 3 and 15).
- **OCR on a clean machine:** it needs an installed Windows OCR language. Without one, pages without text are reported (`ocr.unavailable`) and stay empty.
- **The installer growth** was measured on a self-contained publish (+31.4 MB, ADR-009), not on the Velopack package.
- **Before a public release:** confirm that the Windows SDK projection DLLs are on the SDK's REDIST.TXT list, and add the end-user terms the SDK license asks for (`ATTRIBUTION.md`).
