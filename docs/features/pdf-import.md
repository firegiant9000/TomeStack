# M4: PDF import (extraction, jobs, candidates, review)

ROADMAP M4 "Import intelligence" · SPEC I-01, I-02, I-03, Q-02 · ADR-004, ADR-009 · status: **implemented (D1–D5)**. The exit evidence is in [m4-acceptance.md](m4-acceptance.md).

## D5: the review UI (SPEC I-02)

UI: `src/Ui/src/components/ImportPanel.tsx` and `CandidateReviewPanel.tsx`, on the **Sources** screen, for each of the user's own sources with a PDF. Acceptance: the e2e test "reads the fixture PDF, reviews its candidates, and publishes an accepted one through the studio", run against the real DevHost and worker.

- **Read the text and find candidates:** "Read these pages" (from and to) or "Read the whole document" starts a job. Its line shows progress while it runs, with Cancel and Resume. The line is a polite live region whatever the job's state, so the final status ("completed, …") is announced too. When it completes, it shows "Review N candidates".
- **Search the text of <source>** (SPEC I-03): pages with a snippet and "Open page N".
- **Candidates from <source>:** filters for page, kind, confidence (unsure first: below 80 %) and status (to review by default). Each candidate shows its name, kind, page, confidence and status.
- **A candidate** (a region named "Candidate: <name>", focused when chosen) shows:
  - its kind, page, family, confidence (labelled "a hint, not a check") and status;
  - **Open page N** (the PDF viewer; the desktop app only);
  - the **excerpt** beside it;
  - **what was read**, with unsure fields marked "(unsure)";
  - its **uncertainties**;
  - its **unresolved references**, each with "Dismiss";
  - an edit form: name, kind, text, and the proposed effects as JSON (they stay reference-only);
  - the **check** (blockers, the validator's problems, what it depends on);
  - **Accept as a draft**, which is disabled until the check passes;
  - **Accept as reference** and **Ignore**.
- **Focus (WCAG 2.4.3):** a candidate that leaves the filtered list once reviewed (the default filter shows those to review) takes its details with it, so focus goes back to the "Candidates from <source>" heading.
- **Publishing** is the homebrew studio's, with its own validation: an accepted candidate is a draft in the source, listed there like any other.

## D4: dependency and confidence validation (SPEC I-02, ADR-004)

Service: `src/AppService/ImportCandidates.cs`. Acceptance: `tests/AppService.Tests/CandidateReviewTests.cs`, `ImportQuarantineTests`.

| Command | Payload | Does |
| --- | --- | --- |
| `import.candidate.check` | `{ candidateId }` | **Writes nothing.** Builds the draft the candidate would become (through `CandidateQuarantine.ToDraftRevision`, the only conversion) and runs `ContentValidator` on it: schema, references, formulas and cycles. It also lists **dependencies**: the source, installed content it grants or offers (by name and pin), content it names that is missing, and unresolved names. And it lists **blockers**: each low-confidence field and unresolved reference. The result is `canAccept` and `canAcceptAsReference` |
| `import.candidate.edit` | `{ candidateId, name?, kind?, rulesFamilies?, summary?, effects?, dismissReferences? }` | Saves the reviewer's version next to the detected one, which stays unchanged. Saving clears the low-confidence flags: the reviewer has seen the fields. References go only when dismissed by name, or when content of that name is installed. It is still a proposal |
| `import.candidate.accept` | `{ candidateId, asReference?, confirm: true }` | Creates a **draft** revision in the source. Refused without `confirm` (`candidate.confirmation-required`), while any blocker remains (`candidate.needs-review`), or when validation reports an error (`candidate.validation-failed`, with the validator's errors). **As reference:** the text and page without the proposed effects; blockers do not apply, and validation still runs |
| `import.candidate.ignore` | `{ candidateId }` | Sets it aside; nothing is created |

- **Accepting never activates anything.** The draft is inactive (`content.unpublished`). Every effect is forced to `reference` by the quarantine, and the calculator never applies a draft. Making an effect automatic, and publishing, happen in the homebrew studio. Publishing is the existing, re-validating `content.publish`.
- **Accepting is one transaction:** the draft and the candidate's "accepted" status are saved together, so a crash cannot leave a draft behind a candidate that could then be accepted again.
- **Reviewed candidates stay reviewed** (`candidate.already-reviewed`), and each review step is in the job's audit log (`candidate-edited`, `candidate-accepted`, `candidate-accepted-as-reference`, `candidate-ignored`), without text.
- **Error messages never quote the PDF's text.** A blocker counts the unresolved references instead of naming them; the review shows the names from the candidate itself.
- **Fixed during D4, before any release (ADR-004):** effect types added after content v3 (spell, weapon, armor) are typed only inside a revision of their schema version (ADR-003). A candidate read from storage, or sent by the UI, therefore carried them as unknown effects. The quarantine's "force to reference" did not reach their raw JSON, and the draft, once read back, had an **automatic** weapon or armor effect. The quarantine now reads the draft back before forcing every effect to reference, and stored candidates are typed on read. `ImportQuarantineTests.A_stored_candidates_versioned_effects_stay_reference_only_in_the_draft_and_after_it_is_read_back` covers it, through publish.

## D3: candidate detection

Code: `src/ImportWorker/Detection/CandidateDetector.cs`; the service runs it when a job's extraction completes (`src/AppService/ImportCandidates.cs`). Acceptance: `tests/ImportWorker.Tests/DetectionTests.cs` (the original fixture book), `ImportJobTests.A_completed_import_lists_its_candidates…`, and the measurement below.

**Deterministic and rule-based**, with no AI (an optional local model is M7). It reads the extracted blocks and their positioned lines, not PDF bytes, so it runs in the app. Every pattern is anchored and has a 100 ms match timeout.

| Detector | Recognizes | Proposes |
| --- | --- | --- |
| Spell | A name, then "Level 2 Evocation (Sorcerer, Wizard)" / "Evocation Cantrip (…)" (SRD 5.2.1 layout) or "2nd-level evocation (ritual)" / "Evocation cantrip" (SRD 5.1), then the Casting Time, Range, Components and Duration lines | A `spell` effect: level, school, the four details, concentration, ritual, class lists (5.2.1), the attack kind, a saving throw and the first dice, plus the description |
| Feat | A name, then "Origin Feat" / "General Feat (Prerequisite: …)" (5.2.1), or a heading followed by "Prerequisite: …" (5.1) | "+N bonus to initiative / AC" modifiers and "proficiency in the X skill" grants read from the text |
| Class feature | "Level 3: Name" (5.2.1) or a name with "Level 3 X Feature"; in SRD 5.1 a bold heading or a run-in heading whose first sentence gives the level, and bold headings inside a "Class Features" section | Text only (the level and class as fields). Grants, resources and uses are added by hand |
| Weapon row | A row whose damage cell is exactly "1d8 slashing". Tables print as columns, so rows are rebuilt from line positions and work across a page break without the header | A `weapon` effect: category from the "Martial Melee Weapons" line above, attack, damage, type, properties, range, versatile dice and the 2024 mastery |
| Armor row | On a page with an armor section: "11 + Dex modifier", "14 + Dex modifier (max 2)", "17" or "+2" | An `armor` effect: category (from the section, or the column), Armor Class and the medium Dexterity cap |
| Class table | A "The Bard" or "Bard Features" title and a "Level … Features" table | A class with its features per level as fields, and the feature names found nowhere as unresolved references |

- **Every candidate** carries an excerpt (at most 4,000 characters), its page or page range, the proposed kind, name and rules family, the proposed effects, a confidence (0.05 to 0.99), uncertainties, the fields it read, **low-confidence fields**, and **unresolved references**: names it refers to that are neither installed (published content of the source's families) nor another candidate of the job. For example, "Fixture Stormcall" casts "Fixture Thunder Word".
- **Rules family:** the layout suggests one (the 2014 or 2024 style), within the source's declared families. A layout the source does not declare falls back to the source's families, with an uncertainty.
- **Running headers and footers** (the same text, digits ignored, on at least a third of the pages) are ignored. **Known limit:** a heading printed on that many pages (for example a chapter title repeated on each of its pages) is ignored as well. No test covers that case yet.
- **Nothing becomes content:** candidates are stored per job for review (`import_candidates`, local only). A re-run replaces only candidates still pending. A later import of the **same PDF into the same source** does not propose again what an earlier import's review accepted or ignored (same kind, name and page; `ImportJobTests.A_second_import_of_the_same_PDF_does_not_propose_what_was_already_reviewed`).
- **Bounded in the app's memory:** detection reads the stored blocks and lines, not the joined text, up to 64 million stored characters (about 4,000 typical pages; `TomeStackApp.MaxDetectionChars`). A job past that detects its first pages only, and its audit says where it stopped (`detection-limited`); importing the rest as a page range detects them.
- **Cancellable:** detection stops when its job is cancelled, its PDF is removed or the app closes, and the job is `cancelled` or `interrupted`. Resuming it detects again.
- **A pattern that times out** fails the job with `detect.timeout`. The pages stay stored and searchable.

`import.candidates { jobId, page?, kind?, minConfidence?, maxConfidence?, status? }` lists them in page order and writes nothing.

### Precision and recall on the SRDs (measured 2026-09-28)

`SrdDetectionMeasurementTests` extracts both SRD PDFs, the same files whose hashes `licensing/srd-pack-review.md` records (the test checks them), and compares the candidates with the bundled packs. A match is the same kind, the same name (case, spacing and apostrophes aside), and a page within one of the pack's. The PDFs stay outside the repository, and the test skips without `TOMESTACK_SRD_PDF_DIR`. When it runs, it fails below floors set a little under the numbers below: precision 99 % and recall 99 % for spells, 95 % and 99 % for weapons, 70 % and 99 % for classes, and class-feature recall 85 % (5.1) and 99 % (5.2.1). Every matched spell's level and weapon's damage must be right.

| Kind | SRD 5.1: candidates / truth, precision, recall | SRD 5.2.1: candidates / truth, precision, recall | Notes |
| --- | --- | --- | --- |
| Spells | 319 / 319, **100 %**, **100 %**; level right 319/319 | 339 / 339, **100 %**, **100 %**; level right 339/339 | The packs bundle every SRD spell |
| Weapons | 36 / 35, 97.2 %, **100 %**; damage right 35/35 | 38 / 37, 97.4 %, **100 %**; damage right 37/37 | The one extra in each is the blowgun, which the packs leave out on purpose (its damage is not a dice roll) |
| Classes | 12 / 9, 75 %, **100 %** | 12 / 9, 75 %, **100 %** | The extras are the Fighter, Monk and Rogue, real SRD classes the packs do not bundle |
| Class features | 179 / 115, precision n/a, recall **87.0 %** | 240 / 145, precision n/a, recall **100 %** | The packs hold only some classes and levels, so precision cannot be computed against them. By hand, the 5.2.1 extras are the "Level N:" features of the unbundled classes and levels. About two-thirds of the 5.1 extras are real features (Barbarian 4–20, Fighter, Monk, Rogue). The rest are subclass names and sub-section headings (circle terrains, "… Spells" lists), about 20 of 179. The 5.1 misses are subclass features with no level in their first sentence |
| Feats | 1 / 1 bundled; 1 candidate | 1 / 1 bundled; 17 candidates | The 17 are exactly the SRD 5.2.1 feats (the packs bundle one) |
| Armor | 13 rows | 13 rows | Not bundled, so not measured; 13 is each SRD's armor table (12 armors and the shield) |

**What these numbers do not show:** they measure the SRD layouts the detectors were written against. A third-party book with its own layout will score lower, especially on class features and tables. That is why every candidate needs review (ADR-004), and why the M4 exit gate runs a third-party test PDF (D6).

## D2: import jobs (ARCHITECTURE "Import lifecycle")

Service: `src/AppService/ImportJobs.cs`, database migration v6 in `SqliteStore`. Acceptance: `tests/AppService.Tests/ImportJobTests.cs`.

| Command | Payload | Does |
| --- | --- | --- |
| `import.start` | `{ sourceId, firstPage?, lastPage? }` or `{ sourceId, wholeDocument: true }` | Starts a background job that extracts those pages of the source's PDF. **It changes no content.** Only the user's own sources (made in TomeStack) with an available PDF can be imported. Refused with `source.not-editable`, `source.no-pdf`, `attachment.missing`, `import.pdf-changed` (a linked file changed), `source.page-range-invalid`, or `import.busy` (one job at a time) |
| `import.status` | `{ jobId }` | The job: status (`queued`, `running`, `completed`, `cancelled`, `failed`, `interrupted`), page count, `nextPage`, pages done, unreadable, read by OCR, without text, and a failure code |
| `import.list` | `{ sourceId? }` | Jobs, newest first |
| `import.cancel` | `{ jobId }` | Stops the running job (the worker is killed). Pages already extracted stay |
| `import.resume` | `{ jobId }` | Continues a cancelled, failed or interrupted job at `nextPage`, on the same PDF (`import.pdf-changed` otherwise) |
| `import.audit` | `{ jobId }` | The local audit log: `created`, `started`, `resumed`, `page-skipped` (page and code), `detection-limited`, `cancelled`, `interrupted`, `failed` (with the code) and `completed` (with counts). **Codes, page numbers and counts only, never text from the PDF** |
| `import.search` | `{ sourceId, query }` | SPEC I-03: pages of the source's PDF whose extracted text contains the query (2 to 100 characters, case-insensitive), with a snippet, at most 50. Only within one source; search across sources is M7 (BACKLOG B10) |
| `import.page` | `{ sourceId, page }` | One extracted page: text, blocks, warnings and error |

- **Progress and resume:** each page is stored as it arrives, in the same transaction as the job's counts. A cancelled or failed job resumes where it stopped. A job that was running when the app closed (or crashed) is `interrupted` at the next start, and resumes the same way. This is page-granular.
- **A page that kills the worker fails alone:** when the worker crashes, times out on a page, runs out of memory or sends more than a page can hold (`worker.crashed`, `worker.page-timeout`, `worker.memory`, `worker.message-too-large`), that page is stored as unreadable with the code as its error, and a fresh worker continues with the next page. After 5 such pages in one run (`TomeStackApp.MaxSkippedPagesPerRun`), the job fails with the last code, and `import.resume` continues after them. A failure before the document opens (for example `pdf.encrypted`) still fails the job. Acceptance: `ImportJobTests.A_page_that_kills_the_worker_fails_alone_and_the_rest_of_the_book_is_read` and `…After_too_many_pages_that_kill_the_worker…`.
- **Limits:** the extraction limits of ADR-009 (c), per run.
- **The PDF is pinned by hash:** pages are stored per PDF (SHA-256 and page), so a re-import of the same file replaces them. Each run hashes the file before it starts, so a linked PDF that changed on disk fails with `import.pdf-changed` instead of storing its pages under the old hash. A change *during* a run is not detected. Removing the PDF first stops a job that reads it.
- **Page navigation does not depend on it (ARCHITECTURE):** a failed or cancelled job leaves the attachment, "Open page", page import as reference and the removal preview as they were.
- **Never exported (ADR-009 (d)):** `import_jobs`, `import_pages`, `import_candidates` and `import_audit` are local. No backup or share contains them, and `ImportJobTests.Extracted_text_is_never_exported_in_a_backup_or_a_share` checks both. After a restore on another machine, attach the PDF and import again.

**The shipped worker in the smoke:** `scripts/smoke.ps1` has the built shell import its generated two-page PDF with `import.start`. The job must complete with both pages through `TomeStack.ImportWorker.Host.exe` next to `TomeStack.exe`, with no blocked request.

### Database migration v6

It adds the four tables above. As with every upgrade, `tomestack.db.v5.bak` is taken first. It is forward-only: a v5 build refuses the upgraded folder (`NewerDatabaseException`).

## D1: extraction (ADR-009 (a) to (c))

Code: `src/ImportWorker/Extraction/` (`PdfPigExtractor`, `WorkerProtocol`, `WorkerProcessExtractor`), `src/ImportWorker.Host/` (`TomeStack.ImportWorker.Host.exe`, `WindowsOcrEngine`), and the contracts in `src/ImportWorker/ImportContracts.cs`. Acceptance: `tests/ImportWorker.Tests` (`ExtractionTests`, `WorkerProcessTests`, `OcrTests`).

- **`IDocumentExtractor.ExtractAsync(path, scope)`** yields `DocumentOpened(pageCount)`, then one `ExtractedPage` per page in the scope:
  - `text`: the page's blocks in reading order, separated by blank lines, so a two-column page reads one column at a time;
  - `blocks`: text, a box in PDF points from the bottom-left corner, font size and a bold hint (page coordinates, ARCHITECTURE);
  - `width` and `height`;
  - `fromOcr`;
  - `warnings`: `page.no-text`, `ocr.unavailable`, `page.text-truncated`, `page.blocks-truncated`;
  - `error`: `page.unreadable`, or `page.ocr-failed` when the renderer or OCR throws on the page; a code only, never text.
- **Document-level failures** are an `ExtractionException` with a stable code, and the message never quotes the document:
  - `pdf.missing`, `pdf.not-a-pdf`, `pdf.too-large`, `pdf.encrypted`, `pdf.unreadable`, `pdf.too-many-pages`;
  - from the worker: `worker.missing`, `worker.limits` (its heap cap did not apply), `worker.page-timeout`, `worker.timeout`, `worker.memory`, `worker.crashed`, `worker.message-too-large`, `worker.protocol` (a malformed message, or pages out of order or outside the scope).
- **OCR:** a page with no letters is rendered by Windows.Data.Pdf and read by Windows.Media.Ocr (`fromOcr: true`). Each recognized line is a block, with font size 0 because OCR has none. Without an OCR language, the page is reported with `ocr.unavailable` and stays empty. TomeStack never invents text.
- **Isolation:** the app never parses a PDF itself. `WorkerProcessExtractor` starts `TomeStack.ImportWorker.Host.exe` and sends one JSON request line on stdin; the worker answers with JSON lines on stdout. There is no socket (ADR-006). The parent enforces:
  - at most 1 GiB and 5,000 pages;
  - per page, at most 5,000 blocks and 20,000 lines, and at most 200,000 characters in each copy of its text: the page text, the blocks' text and the lines' text;
  - a length limit on every line the app reads from the worker, derived from the page limits (about 10 million characters by default), checked before the line is held;
  - 60 s per page, 60 min per run;
  - a 1 GiB managed-heap cap in the child, which the child reports before it parses anything (the app refuses to go on otherwise), and a 1.5 GiB watchdog on the larger of its working set and private bytes;
  - pages in order and inside the requested scope, and `done` only after all of them.
  
  Cancelling, or stopping early, kills the child's process tree. The child also exits when the app does, so a crashed app leaves no worker behind. What the child writes to stderr is discarded, never logged.
- **Shipping:** the shell references the worker, so `TomeStack.ImportWorker.Host.exe` and its assemblies sit next to `TomeStack.exe`. They are published self-contained with it. The shell now targets `net10.0-windows10.0.19041.0` and keeps its output folder `bin/<config>/net10.0-windows/`.

### The original fixture book

`tests/RulesFixtures/pdf/fixture-import.pdf` (6 pages, about 5 KB) is generated by `tests/ImportWorker.Tests/FixturePdfs.cs` with PdfPig's writer, and all its text is invented (SPEC Q-03):

- two spells in two columns, one in each SRD's layout style;
- a feat, and a feature that names a spell that is not installed;
- weapon and armor table rows, and a class feature table;
- a page with no text layer.

`ExtractionTests.The_committed_fixture_book_has_exactly_the_generators_original_text` checks the committed file against the generator. Run it with `TOMESTACK_WRITE_FIXTURES=1` to regenerate. The OCR tests make an image-only page at test time from the fixture's own text.

### Malformed input (SPEC Q-02, Q-04)

| Input | Result |
| --- | --- |
| Not a PDF, or empty | `pdf.not-a-pdf`, before parsing |
| Over the size limit | `pdf.too-large`, before parsing |
| Truncated at 10 %, 50 % and 90 % | `pdf.unreadable`, or the pages lenient parsing recovers; never invented text |
| Encrypted (standard handler, unknown password) | `pdf.encrypted` |
| 5,001 pages (or more than a configured limit) | `pdf.too-many-pages`, before any page is read |
| A page tree whose `/Count` claims 900 million pages | no hang, no crash |
| A content stream that is not valid Flate data | a page error, not a failed book |
| A decompression bomb (512 MB of whitespace in about 0.5 MB) | the run must fail with a worker limit (`worker.page-timeout`, `worker.memory` or `worker.crashed`), and the child ends. Measured: the page timeout stops it. In an import, that page alone fails |
| The worker over its memory limit, silent past the page timeout, cancelled, stopped early, missing, or sent a malformed request | `worker.memory`, `worker.page-timeout`, the child killed, `worker.missing`, `worker.bad-request` |
| The worker's heap cap | reported by the child and checked by the app (`WorkerProcessTests.The_worker_runs_under_the_heap_cap_the_app_sets`) |
| A worker line longer than the limit | refused before it is held (`worker.message-too-large`) |
| A page the OCR cannot read | `page.ocr-failed` for that page; the others extract |
