# M4: PDF import (extraction, jobs, candidates, review)

ROADMAP M4 "Import intelligence" · SPEC I-01, I-02, I-03, Q-02 · ADR-004, ADR-009 · status: **D1 extraction and D2 import jobs implemented**; detection (D3), acceptance validation (D4) and the review UI (D5) follow in this document.

## D2: import jobs (ARCHITECTURE "Import lifecycle")

Service: `src/AppService/ImportJobs.cs`, database migration v6 in `SqliteStore`. Acceptance: `tests/AppService.Tests/ImportJobTests.cs`.

| Command | Payload | Does |
| --- | --- | --- |
| `import.start` | `{ sourceId, firstPage?, lastPage? }` or `{ sourceId, wholeDocument: true }` | Starts a background job that extracts those pages of the source's PDF. **It changes no content.** Only the user's own sources (made in TomeStack) with an available PDF can be imported. Refused with `source.not-editable`, `source.no-pdf`, `attachment.missing`, `import.pdf-changed` (a linked file changed), `source.page-range-invalid`, or `import.busy` (one job at a time) |
| `import.status` | `{ jobId }` | The job: status (`queued`, `running`, `completed`, `cancelled`, `failed`, `interrupted`), page count, `nextPage`, pages done, unreadable, read by OCR, without text, and a failure code |
| `import.list` | `{ sourceId? }` | Jobs, newest first |
| `import.cancel` | `{ jobId }` | Stops the running job (the worker is killed). Pages already extracted stay |
| `import.resume` | `{ jobId }` | Continues a cancelled, failed or interrupted job at `nextPage`, on the same PDF (`import.pdf-changed` otherwise) |
| `import.audit` | `{ jobId }` | The local audit log: `created`, `started`, `resumed`, `cancelled`, `interrupted`, `failed` (with the code) and `completed` (with counts). **Codes and counts only, never text from the PDF** |
| `import.search` | `{ sourceId, query }` | SPEC I-03: pages of the source's PDF whose extracted text contains the query (2 to 100 characters, case-insensitive), with a snippet, at most 50. Only within one source; search across sources is M7 (BACKLOG B10) |
| `import.page` | `{ sourceId, page }` | One extracted page: text, blocks, warnings and error |

- **Progress and resume:** each page is stored as it arrives, in the same transaction as the job's counts. A cancelled or failed job resumes where it stopped. A job that was running when the app closed (or crashed) is `interrupted` at the next start, and resumes the same way. This is page-granular.
- **Limits:** the extraction limits of ADR-009 (c), per run.
- **The PDF is pinned by hash:** pages are stored per PDF (SHA-256 and page), so a re-import of the same file replaces them. Removing the PDF first stops a job that reads it.
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
  - `error`: `page.unreadable`, a code only, never text.
- **Document-level failures** are an `ExtractionException` with a stable code, and the message never quotes the document:
  - `pdf.missing`, `pdf.not-a-pdf`, `pdf.too-large`, `pdf.encrypted`, `pdf.unreadable`, `pdf.too-many-pages`;
  - from the worker: `worker.missing`, `worker.page-timeout`, `worker.timeout`, `worker.memory`, `worker.crashed`, `worker.protocol`.
- **OCR:** a page with no letters is rendered by Windows.Data.Pdf and read by Windows.Media.Ocr (`fromOcr: true`). Each recognized line is a block, with font size 0 because OCR has none. Without an OCR language, the page is reported with `ocr.unavailable` and stays empty. TomeStack never invents text.
- **Isolation:** the app never parses a PDF itself. `WorkerProcessExtractor` starts `TomeStack.ImportWorker.Host.exe` and sends one JSON request line on stdin; the worker answers with JSON lines on stdout. There is no socket (ADR-006). The parent enforces:
  - at most 1 GiB and 5,000 pages;
  - at most 200,000 characters and 5,000 blocks per page;
  - 60 s per page, 60 min per run;
  - a 1 GiB managed-heap cap in the child, and a 1.5 GiB working-set watchdog.
  
  Cancelling, or stopping early, kills the child's process tree. What the child writes to stderr is discarded, never logged.
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
| A decompression bomb (512 MB of whitespace in about 0.5 MB) | only its own run fails. Measured: the page timeout stops the child |
| The worker over its memory limit, silent past the page timeout, cancelled, stopped early, missing, or sent a malformed request | `worker.memory`, `worker.page-timeout`, the child killed, `worker.missing`, `worker.bad-request` |
