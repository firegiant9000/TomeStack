# PDF attachments and page navigation

SPEC S-01, S-04, I-01, I-03 · ADR-005 · BACKLOG B05 · MVP "Sources" ("open the cited page from a feature offline"; "import limited page ranges and whole documents as reference"), definition of done 4 and 5 · status: implemented (M2 item 6; page import M2 item 4).

Service: `src/AppService/Attachments.cs`, `src/AppService/Persistence/AttachmentFiles.cs`, database migration v3 in `SqliteStore`. Shell: `src/DesktopShell/PdfViewerWindow.cs`, `ShellHostServices`. UI: `src/Ui/src/components/SourcesPanel.tsx` and "Open … p. N" on sheet features. Acceptance: `tests/AppService.Tests/AttachmentTests.cs`, the e2e test "attaches a PDF to a source…", and the `--smoke` PDF viewer check.

## Attaching

The **Sources** screen lists every source with its license, whether it may be shared, and its PDF:

- **Attach PDF…** (default, managed): the native Open dialog picks the file. TomeStack checks the PDF signature and the size (at most 1 GiB), hashes it with SHA-256, and copies it to `<data dir>/attachments/<sha256>.pdf`, read-only. The same PDF is stored once, however many sources use it. In browser development (DevHost) the page's file picker sends the bytes instead (`source.attachPdfData`).
- **Link PDF where it is…**: records the path and the hash, and copies nothing. When opened, the hash is compared: a changed file opens with the warning `attachment.changed` (the pages may have moved), and a deleted one is refused with `attachment.missing`, "attach it again".
- The UI never receives a path: `source.list` and `source.attachment` return the file name, size, mode and status (`available`, `missing`, `changed`) only.
- A refused file (`attachment.not-a-pdf`, `attachment.too-large`, `attachment.file-name-invalid`, `attachment.unreadable`) changes nothing.

## Removing (SPEC S-04)

**Remove PDF…** first shows `source.detachPreview`: how many entries cite pages in that source, and which ones. Their "Open page" links will stop working, and all content stays. `source.detach` needs `confirm`. A managed file is deleted only when no attachment record uses the same PDF any more. The file is deleted after the database change commits, and only on a best-effort basis. If the file is open elsewhere (the viewer, antivirus, a sync client), removing or replacing the PDF still succeeds and says so. The unused copy stays read-only until the next start, which deletes managed copies that no attachment uses, and leftover `.partial` files (`AttachmentTests.Removing_or_replacing_a_pdf_that_is_open_elsewhere_succeeds_and_the_file_is_removed_later`; M2 review fix).

## Opening a cited page

A feature whose source has an available PDF and whose revision cites a page gets **Open <name>, p. N** on the sheet. `source.openPage { sourceId, page }` asks the shell to open it. It does not depend on text extraction (ARCHITECTURE):

- The shell opens a separate viewer window: WebView2's built-in PDF viewer, navigated to `https://pdf.tomestack.localhost/document.pdf#page=N`. No folder is mapped. The window answers that one URL with the PDF's bytes (with delete sharing, so the attachment can still be removed while it is open). It refuses and reports every other request and navigation, so a link inside a linked PDF cannot reach the files next to it (`src/AppService/PdfViewerRequests.cs`, `PdfViewerRequestsTests`; M2 review fix). The file's name and path never appear in the URL.
- Like the main window, it refuses every http(s) request outside that host and reports it. `--smoke` attaches a generated two-page PDF, opens page 2, and passes only when the viewer loaded it with no blocked request.
- Hosts without a viewer (DevHost, tests without a host) report `unsupported`, and the UI says it needs the desktop app.

**Not verified automatically:** that the viewer actually shows page N (the smoke proves that the PDF loads offline, not which page is on screen). That is an owner check on the installed app.

## Importing pages as reference (SPEC I-01, I-03; M2 item 4)

**Owner decision (2026-09-27): no text extraction in M2.** TomeStack does not read the PDF, and text extraction and search stay M4. An import records *which pages* matter:

- `source.importPages { sourceId, start, end?, title?, wholeDocument? }` (service `src/AppService/PageImport.cs`; UI: "Import pages of … as reference" on the Sources screen) creates a **draft** feature with no effects. It cites the pages (`provenance.page`), and its summary says it is a page reference whose text is in the PDF. A whole document cites page 1. The default name is the source title and the pages.
- **Review before it applies (ADR-004):** the draft is inactive. The player finds it in the homebrew studio, edits it (the name, a summary, or effects authored by hand, which is the "manual entry tied to pages" of MVP "Sources"), and publishes it. Published and pinned, it is a reference-only feature with "Open …, p. N" on the sheet.
- **Only the user's own sources** (made in TomeStack; the studio lists them) take imports, and only with an available or changed PDF: `source.not-editable` for a bundled source such as an SRD pack, `source.no-pdf`, and `source.page-range-invalid` (pages 1–100,000, the last not before the first). TomeStack cannot check the real last page without reading the PDF.
- The removal preview counts these entries like any content that cites pages. It lists the names sorted, so it reads the same every time.

Acceptance: `tests/AppService.Tests/PageImportTests.cs` and the import step of the e2e test "attaches a PDF to a source…".

## Database migration v3 (ADR-005 "Migration plan")

1. The pre-upgrade backup `tomestack.db.v2.bak` is taken first (SQLite online backup).
2. It adds the `attachments` table and the `sources.attachment_id` and `sources.legacy_pdf_ref` columns.
3. Each source with a `pdfRef`: a readable PDF within the limit becomes a managed copy (de-duplicated). Anything else (missing, unreadable, not a PDF) becomes a linked record without a hash, shown as "missing: attach it again". The migration never fails because of a file.
4. `pdfRef` is cleared from the source JSON, and the old value stays in `legacy_pdf_ref` for one release.

## Packages

Attachments are machine-local: exports carry neither `attachmentId` nor any path or PDF (ADR-007). A re-import keeps the local attachment. On a clean machine the source has no PDF, and the Sources screen says "No PDF attached". The exported source document is unchanged (source schema v1), so packages need no format change.
