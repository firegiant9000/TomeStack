# M4 acceptance: Import intelligence, exit evidence

ROADMAP M4 exit gate: "A third-party test PDF produces reviewable candidates; no unapproved active rules" · SPEC I-01, I-02, I-03, Q-02 · ADR-004, ADR-009 · build **0.3.0** · status: **engineering done; gate not met.** The third-party test PDF was not on the development machine (2026-09-28), so its run is still owed. Following ADR-008, the version stays 0.3.0 until the gate is met.

Automated means it runs in the gate (CLAUDE.md): `dotnet test`, `npm run test:e2e` against the real DevHost and worker, and `scripts/smoke.ps1` on the built shell.

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

Every accepted candidate is a draft, and every effect in it is reference-only.

### The third-party test PDF (local)

`M4AcceptanceTests.A_third_party_test_PDF_produces_reviewable_candidates_and_no_unapproved_active_rules` reads every PDF in the gitignored `tests/RulesFixtures/local/third-party/`. It skips when there is none. Its output, and the `report.md` it writes in that folder, are counts only: pages, candidates by kind, accepted, accepted as reference, ignored, pending, and "active without approval". PDFs are numbered by position, never named.

**Not run:** the folder is not on the development machine. To run it, put the PDF there and run `dotnet test --filter A_third_party_test_PDF`, then record the counts here:

| Date | Build | PDF | Candidates by kind | Accepted | As reference | Ignored | Active without approval |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | |

## Precision and recall (D3)

Measured 2026-09-28 on both SRD PDFs, whose hashes match `licensing/srd-pack-review.md`, against the bundled packs (`SrdDetectionMeasurementTests`; the PDFs stay outside the repository). Details and the match rule are in `pdf-import.md`. The test now fails below floors set a little under these numbers. The floors were added after this measurement and have not run yet: the PDFs were not on the machine when they were added.

| Kind | SRD 5.1 precision / recall | SRD 5.2.1 precision / recall |
| --- | --- | --- |
| Spells | 100 % / 100 % (levels 319/319) | 100 % / 100 % (levels 339/339) |
| Weapons | 97.2 % / 100 % (damage 35/35) | 97.4 % / 100 % (damage 37/37) |
| Classes | 75 % / 100 % | 75 % / 100 % |
| Class features | n/a / 87.0 % | n/a / 100 % |

The extra weapons (the blowgun) and classes (Fighter, Monk, Rogue) are real SRD entries the packs do not bundle. Feature precision cannot be computed against packs that bundle only some classes. By hand, about 20 of the 179 SRD 5.1 feature candidates are subclass names or sub-section headings. Feats and armor are not fully bundled, so they are counted rather than measured: all 17 SRD 5.2.1 feats, and both SRDs' 13 armor rows.

## Could not be verified here

- **The third-party test PDF run** (above). Detection was written against SRD layouts, so a third-party book will score lower, especially on class features and tables. Every candidate is still only a proposal (ADR-004).
- **The review screen by keyboard only and with Narrator** (`accessibility-checklist.md`, items 3 and 15).
- **OCR on a clean machine:** it needs an installed Windows OCR language. Without one, pages without text are reported (`ocr.unavailable`) and stay empty.
- **The installer growth** was measured on a self-contained publish (+31.4 MB, ADR-009), not on the Velopack package.
- **Before a public release:** confirm that the Windows SDK projection DLLs are on the SDK's REDIST.TXT list, and add the end-user terms the SDK license asks for (`ATTRIBUTION.md`).
