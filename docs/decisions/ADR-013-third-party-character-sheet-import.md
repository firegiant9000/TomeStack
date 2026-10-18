# ADR-013: Third-party character sheet import

Status: accepted (owner decisions D16, 2026-10-02, and D37, 2026-10-14; written with item 19b in Wave 2)
Date: 2026-10-18

## Context

The owner's character lives on D&D Beyond, and players keep sheets in other forms (the fillable 2014 sheet, a printed PDF, notes). T2 needs them in TomeStack. The constraints are SPEC Q-02 (bounded, untrusted input) and Q-03 (no third-party text in the repo), ADR-006 (no socket) and ADR-009 (PDF bytes are parsed only in the isolated worker). `features/ddb-pdf-import.md` holds the behaviour; this ADR records the decisions behind it. It was claimed by D16 and written late, with the second slice.

## Decision

- **Form fields, read once, in the worker.** A character sheet PDF is read as its AcroForm fields by the worker's `formFields` request (`WorkerFormReader`), under sheet-sized limits (20 MB, 50 pages, 30 s, a 256 MiB managed heap, and `FormLimits` for the fields). The fields become a `DdbSheet` through a data-driven layout map (`CharacterImport/Layouts/*.json`, field names only); a layout is recognised by its required names, and an unknown one is refused (`ddb.layout-unknown`).
- **A PDF with no form fields is read as text.** When the form reader reports `ddb.no-form-fields`, `ddb.read` (and `ddb.readData`) asks the worker for the first 20 pages' text with the existing `extract` request, under the same sheet limits, and gives it to `TextSheetParser` (ADR-017). The result is the same read result and token, with layout `text` named "text from the PDF". Other form failures never fall back. `ddb.readText` stays text only; no PDF travels in a JSON payload. Nothing is stored: the worker stores nothing, and the import-job queue is not used.
- **No third request kind (R114).** The `extract` request is path-based, with a page scope and OCR, and the generated text-only fixture reads back one line per printed line in page order, which is what the parser needs. ADR-009's allowed set stays `extract` and `formFields`.
- **Bounded, with counts only.** Twenty pages are read and the rest counted in a report line. The parser's caps apply to the extracted text (`ddb.text-too-large`, `ddb.text-too-many-lines`, `ddb.text-timeout`); a page the worker cut short is refused as too large rather than read in part; fewer than 10 characters in all is `ddb.pdf-no-text` (a blank page, or a scan with no recognised text).
- **Review before anything is saved.** Name matches are proposals the user confirms, choices go through the builder's check, the base score absorbs what is not modelled, the import always makes a new character, and what is left out becomes gap notes (v2). Nothing of the PDF is kept.
- **The path rule (R116).** The worker's first file outside the attachments folder is a dialog path. Every dialog path the worker reads (`ddb.read`, `source.attachPdf`) first passes `LocalDrivePath` in AppService: a full path on a local drive letter, the required extension (`.pdf`), no `..`, no network (UNC) path and no device path. A refusal is `pdf.path-not-local` and names no path (it can hold a user name). `LibraryBackup.IsSafeLinkedPath` shares the rule. A UNC path makes Windows connect to another machine, sending the user's credentials, merely to open the file. A drive letter mapped to a network share is not detected.
- **Privacy.** Field values and page text are the user's own and may name real people. They are never in a log, error message, code or `ToString` (`CommandDispatcher` logs `Exception.ToString()`); messages and codes carry counts and layout ids. Fixtures are original and generated.

## Consequences

- One entry, "Import a character sheet (PDF)…", reads a fillable sheet exactly and a printed one approximately, and says which in the layout row.
- A text-read PDF can misread (columns, tables, a scan's recognition errors). The lines not read and the comparison step are the safety net, as for pasted text.
- A PDF on a network share can no longer be attached or imported; copy it to a local drive first.
- The layouts follow an unofficial format that can change; a new layout is a new map file, not code.
- No database, package or schema change.

## Alternatives considered

- The D&D Beyond JSON endpoint: rejected; it needs the network and is unofficial.
- Page-text parsing as the main route: rejected for fillable sheets, since the fields are exact; accepted only as the fallback for a PDF with no fields.
- A third worker request kind, `pageText`: rejected (R114); `extract` already gives the page's lines in reading order, with scope, OCR and the same limits.
- The path check in the WPF shell: rejected; one check in AppService covers the DevHost, the tests and every host.
- Reusing attachments and import jobs: rejected; it keeps the PDF and marks a source import-derived for no reason.

## Evidence

- `tests/ImportWorker.Tests/FormReaderTests.cs`, `WorkerProcessTests.cs` (the form request, and `extract` under the sheet limits: 20 of 25 pages, more than 50 pages, a page timeout) and `ExtractionTests.cs` (the generated text-only fixture's lines in order).
- `tests/AppService.Tests/TextPdfImportTests.cs` (the fallback, caps, empty and truncated text, no text in any error) and `HostServicesTests.cs` (UNC, device, relative, `..` and wrong-extension paths refused before the worker).
- The e2e flow "imports a PDF that has no form fields".
