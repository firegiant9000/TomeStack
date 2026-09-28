# ADR-009: PDF extraction and OCR

Status: **accepted** (owner decision 2026-09-28, choosing the proposed option for each of (a) to (d)).
Date: 2026-09-28

## Context

M4 "Import intelligence" (ROADMAP) is page and whole-book extraction, an OCR fallback, candidate recognition and a review UI. Its exit gate: "A third-party test PDF produces reviewable candidates; no unapproved active rules". The constraints:

- SPEC I-01, I-02 and I-03: import produces **draft candidates**, never active rules, and page navigation works even when parsing fails (ARCHITECTURE "Import lifecycle").
- SPEC Q-02: PDFs are untrusted. Restrict paths, sizes and decompression, and isolate parse failures.
- ADR-004: the worker produces only `DraftCandidate`s. `CandidateQuarantine.ToDraftRevision` is the only conversion, publishing is the separate `content.publish`, and confidence is a UI hint.
- ADR-006: the shipped app opens no listening socket. ADR-001: no network in normal use.
- ADR-007 and Q-03: third-party text must never leave the machine.
- D07: the project is Apache-2.0 and the repository is public, so every dependency must be license-compatible and recorded in `ATTRIBUTION.md`.

## Decision

### (a) Text extraction: PdfPig

`PdfPig` 0.1.16 (NuGet `PdfPig`, https://github.com/UglyToad/PdfPig):

| Check | Result (2026-09-28) |
| --- | --- |
| License | **Apache-2.0** (the package's `licenseExpression`), the same as TomeStack |
| Maintenance | 0.1.16 released 2026-08-22, after 0.1.15 (2026-06-25) and 0.1.14 (2026-03-22); last push 2026-09-28; not archived; about 2,600 stars |
| Dependencies | none for net8.0 or net9.0 (pure managed code, no native binaries) |
| Behavior | Reads text, letters and words with bounding boxes (`page.GetWords()`, `BoundingBox`) in PDF coordinates. It never executes JavaScript and never fetches remote resources, because it has no scripting engine and no network code. Encrypted documents without a password throw `PdfDocumentEncryptedException`. Its `PdfDocumentBuilder` also writes PDFs, which the tests use to generate the **original** fixture PDF at test time (no binary in the repo) |

Docs checked through Context7 (`/uglytoad/pdfpig`): `PdfDocument.Open`, `ParsingOptions` (lenient parsing on by default, `Password`), `GetWords`, `NumberOfPages`.

### (b) OCR fallback: Windows.Media.Ocr, rendered by Windows.Data.Pdf

- For a page with **no text layer** (PdfPig finds no letters), the worker renders the page with **`Windows.Data.Pdf`** (`PdfDocument.LoadFromStreamAsync`, `PdfPage.RenderToStreamAsync`). It then reads the image with **`Windows.Media.Ocr`** (`OcrEngine.TryCreateFromUserProfileLanguages`, `RecognizeAsync`; lines and words with boxes). Both are part of Windows 10 and 11, work offline, and add no package. PdfPig cannot render pages, so OCR needs a renderer, and Windows has one.
- **Cost:** the process that runs OCR must target `net10.0-windows10.0.19041.0`, which brings the C#/WinRT projection assembly (`Microsoft.Windows.SDK.NET.dll`). The installer growth is measured in D1 and recorded below. The estimate is about 25 MB on the 145 MB self-contained build.
- **Accuracy:** good on clean printed text. It needs an installed OCR language (`ocr.unavailable` when there is none) and gives **no per-word confidence**, so candidate confidence comes from the detectors, never from OCR. An OCR'd page is marked `fromOcr`, and its candidates carry an "OCR text" uncertainty.

### (c) Isolation: a child process over stdin/stdout

- Extraction runs in **`TomeStack.ImportWorker.exe`** (`src/ImportWorker.Host`), a separate child process started by the application service. Requests and results are JSON lines on stdin and stdout, not a socket, so ADR-006 holds. The request names a **managed attachment file** (`attachments/<sha256>.pdf`) resolved by the service, never a path from the UI. A linked PDF is used only after its hash check.
- **Limits:**
  - at most 1 GiB (the attachment limit) and **5,000 pages** per document;
  - **60 s per page** without output, and **60 min per run**;
  - a managed-heap cap for the child (`DOTNET_GCHeapHardLimit`, 1 GiB), plus a parent watchdog that kills the child above **1.5 GiB** working set;
  - at most **200,000 characters of text per page**, truncated with a warning.
  
  A decompression bomb therefore ends as an out-of-memory failure or a kill of the child, never of the app.
- **Cancel kills the child** (the whole process tree). A killed or crashed child fails only its job: pages already extracted stay, and `import.resume` continues from the next page.
- **Detection stays in the service process.** The detectors read already extracted text (strings, not PDF bytes), with bounded regular expressions (`RegexOptions.NonBacktracking` or a match timeout), and produce only `DraftCandidate`s (ADR-004).
- Error messages and the local audit log carry codes, page numbers and counts, **never extracted text** (SPEC "errors and logs never quote user or third-party text").

### (d) Storage: a forward-only migration; extracted text is never exported

- **Database migration v6** adds `import_jobs`, `import_pages` (text and word boxes per page, keyed by the PDF's SHA-256 and page number, so re-importing the same PDF reuses them), `import_candidates` and `import_audit`. It is forward-only, and the usual `tomestack.db.v5.bak` is taken first.
- **Extracted page text and pending candidates are never in any package**, neither a share nor a personal backup. They are a local cache, re-extracted from the PDF after a restore. The PDF itself is never exported either (ADR-007). So third-party text cannot leave the machine, even in a backup of a user-made source that wraps a bought book. **Accepted** candidates become draft revisions, which travel like any other content, under ADR-007's backup and share rules.

## Consequences

- The shell, the DevHost and the worker target `net10.0-windows10.0.19041.0`. `RulesCore` and `AppService` stay `net10.0` with no Windows references, and the worker contracts in `src/ImportWorker` stay `net10.0`.
- One more executable ships. Velopack packs it with the rest (`scripts/pack-installer.ps1`).
- OCR quality depends on the Windows language packs of the user's machine, and OCR has no confidence. A clean VM without the OCR language is an owner check.
- The service keeps page navigation independent of extraction. A job that fails leaves the attachment and "Open page" working (ARCHITECTURE).

## Alternatives considered

| Option | Why not |
| --- | --- |
| **(a)** iText 7 / iText Core | AGPL-3.0 or commercial: incompatible with an Apache-2.0 public project |
| **(a)** PDFsharp | MIT, but a writer first. Text extraction is limited and gives no word positions |
| **(a)** Docnet.Core (PDFium) | MIT with a native PDFium binary. It renders and extracts, but it is native code parsing untrusted input, and the wrapper is maintained sporadically. PdfPig is managed and actively released |
| **(b)** Tesseract via the `Tesseract` NuGet wrapper | Apache-2.0, but the wrapper's last release is 5.2.0 (2022). It needs native leptonica and tesseract binaries (about 10 MB for x64) plus trained data (4 MB fast or 15 MB best for English), and a renderer, meaning PDFium again. It does give per-word confidence and needs no Windows language pack, but it means more native attack surface and weaker maintenance |
| **(b)** No OCR in M4 | Keeps the TFM and installer unchanged, but misses ROADMAP M4's "OCR fallback" deliverable |
| **(b)** Windows AI `TextRecognizer` (Windows App SDK) | NPU-only (Copilot+ PCs) and the Windows App SDK: not available on a typical machine |
| **(c)** Extraction in the app's process | A parser crash or memory bomb would take the sheet down with it (Q-02 asks to isolate parse failures) |
| **(c)** A loopback socket to a worker | Rejected by ADR-006 |
| **(d)** Text in a backup when the source is the user's own | Needs a new "this is my own work" flag, because user-made sources also wrap bought books, and it risks exporting third-party text through a mislabelled source. The text can be re-extracted from the PDF anyway |

## Evidence

Recorded as D1 to D6 land (`docs/features/pdf-import.md`, `docs/features/m4-acceptance.md`).

Supersedes: none. Extends ADR-004 (the worker's candidates) and ADR-006 (a child process, still no socket).
