using System.Text.Json;
using System.Text.Json.Serialization;
using TomeStack.AppService.Persistence;
using TomeStack.ImportWorker;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

public enum ImportJobStatus { Queued, Running, Completed, Cancelled, Failed, Interrupted }

/// <summary>
/// M4 D2 (ARCHITECTURE "Import lifecycle"): a cancellable, resumable extraction of one source's PDF. Pages are stored as
/// they arrive (<see cref="StoredPage"/>, keyed by the PDF's hash), so a cancelled, failed or interrupted job resumes at
/// <see cref="NextPage"/>. <see cref="Sha256"/> pins the PDF the job started on. Local only; never exported.
/// </summary>
public sealed record ImportJobRecord
{
    public const int CurrentSchemaVersion = 1;

    public required Guid Id { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required Guid SourceId { get; init; }
    public required string Sha256 { get; init; }
    public int FirstPage { get; init; } = 1;
    /// <summary>Null: to the end of the document.</summary>
    public int? LastPage { get; init; }
    public bool WholeDocument { get; init; }
    public ImportJobStatus Status { get; init; } = ImportJobStatus.Queued;
    public int? PageCount { get; init; }
    /// <summary>The next page to extract; null once the scope is done.</summary>
    public int? NextPage { get; init; }
    public int PagesDone { get; init; }
    public int PagesFailed { get; init; }
    public int PagesFromOcr { get; init; }
    public int PagesWithoutText { get; init; }
    /// <summary>A code such as <c>pdf.encrypted</c> or <c>worker.memory</c>; never extracted text.</summary>
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
    public int Candidates { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>The last page of the scope once the page count is known.</summary>
    [JsonIgnore]
    public int? ScopeEnd => PageCount is { } count ? Math.Min(count, LastPage ?? count) : LastPage;

    [JsonIgnore]
    public bool Active => Status is ImportJobStatus.Queued or ImportJobStatus.Running;
}

/// <summary>One extracted page (ADR-009): its text, blocks with page coordinates, warnings and an error code.</summary>
public sealed record StoredPage(
    int Page,
    string Text,
    bool FromOcr,
    double Width,
    double Height,
    IReadOnlyList<TextBlock> Blocks,
    IReadOnlyList<string> Warnings,
    string? Error);

/// <summary>A local audit line: an event and a detail of codes and counts, never text from the PDF.</summary>
public sealed record ImportAuditEntry(DateTimeOffset At, string Event, string? Detail);

/// <param name="WholeDocument">Import every page; otherwise <paramref name="FirstPage"/> to <paramref name="LastPage"/>.</param>
public sealed record ImportStartRequest(Guid SourceId, int? FirstPage = null, int? LastPage = null, bool WholeDocument = false);

public sealed record ImportJobRequest(Guid JobId);

public sealed record ImportListRequest(Guid? SourceId = null);

/// <param name="Query">2 to 100 characters.</param>
public sealed record ImportSearchRequest(Guid SourceId, string Query);

public sealed record ImportSearchHit(int Page, string Snippet);

public sealed record ImportPageRequest(Guid SourceId, int Page);

public sealed partial class TomeStackApp
{
    /// <summary>The worker next to the app (ADR-009 (c)); tests and hosts can pass another extractor to <see cref="Open"/>.</summary>
    public const string WorkerFileName = "TomeStack.ImportWorker.Host.exe";

    public const int MaxSearchResults = 50;

    private readonly Lock _importGate = new();
    private IDocumentExtractor? _extractor;
    private (Guid JobId, CancellationTokenSource Cancel, Task Task)? _running;
    private volatile bool _disposing;

    private IDocumentExtractor Extractor => _extractor ??= new ImportWorker.Extraction.WorkerProcessExtractor(Path.Combine(AppContext.BaseDirectory, WorkerFileName));

    /// <summary>The running job's task (tests wait on it); null when no job runs.</summary>
    internal Task? RunningImport
    {
        get
        {
            lock (_importGate)
                return _running?.Task;
        }
    }

    /// <summary>
    /// <c>import.start</c> (SPEC I-01): extracts a page range, or the whole document, of one of the user's own sources'
    /// attached PDF in the background. It changes no content. One job runs at a time (<c>import.busy</c>).
    /// </summary>
    public ImportJobRecord StartImport(ImportStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = FindSourceOrThrow(request.SourceId);
        if (source.EditionVersion != "homebrew")
            throw new AppValidationException([new("source.not-editable", $"'{source.Title}' is a bundled source. Create your own source for the book in the studio, attach the PDF to it, then import it there.")]);
        var sha = AvailablePdf(source);
        var first = request.WholeDocument ? 1 : request.FirstPage ?? 1;
        int? last = request.WholeDocument ? null : request.LastPage ?? first;
        if (first < 1 || first > MaxPage || last is < 1 or > MaxPage || (last is { } end && end < first))
            throw new AppValidationException([new("source.page-range-invalid", $"Pages go from 1 to {MaxPage}, and the last page cannot come before the first.")]);

        var now = _time.GetUtcNow();
        var job = new ImportJobRecord
        {
            Id = Guid.NewGuid(), SourceId = source.Id, Sha256 = sha, FirstPage = first, LastPage = last, WholeDocument = request.WholeDocument,
            NextPage = first, CreatedAt = now, UpdatedAt = now,
        };
        lock (_importGate)
        {
            if (_running is not null)
                throw new AppValidationException([new("import.busy", "Another import is running. Wait for it, or cancel it, then start this one.")]);
            _store.InTransaction(() =>
            {
                _store.SaveImportJob(job);
                _store.AddImportAudit(job.Id, now, "created", request.WholeDocument ? "whole document" : $"pages {first}-{(last is { } l ? l.ToString(System.Globalization.CultureInfo.InvariantCulture) : "end")}");
            });
            Launch(job.Id, "started");
        }
        return job;
    }

    /// <summary><c>import.status</c>: the job as stored now. Writes nothing.</summary>
    public ImportJobRecord ImportStatus(Guid jobId) =>
        _store.FindImportJob(jobId) ?? throw new AppValidationException([new("import.not-found", $"Import job {jobId} does not exist.")]);

    /// <summary><c>import.list</c>: every job, newest first, optionally of one source. Writes nothing.</summary>
    public IReadOnlyList<ImportJobRecord> ListImports(Guid? sourceId = null) =>
        [.. _store.ListImportJobs().Where(j => sourceId is null || j.SourceId == sourceId).Reverse()];

    /// <summary><c>import.audit</c>: the job's local audit log (codes and counts only).</summary>
    public IReadOnlyList<ImportAuditEntry> ImportAudit(Guid jobId)
    {
        ImportStatus(jobId);
        return _store.ListImportAudit(jobId);
    }

    /// <summary><c>import.cancel</c>: stops the running job (the worker is killed). Extracted pages stay; <c>import.resume</c> continues.</summary>
    public ImportJobRecord CancelImport(Guid jobId)
    {
        var job = ImportStatus(jobId);
        Task? task = null;
        lock (_importGate)
        {
            if (_running is { } running && running.JobId == jobId)
            {
                running.Cancel.Cancel();
                task = running.Task;
            }
        }
        if (task is not null)
            WaitQuietly(task);
        else if (job.Active)
            Finish(job, ImportJobStatus.Cancelled, "cancelled", null, null);
        else
            throw new AppValidationException([new("import.not-running", "This import is not running.")]);
        return ImportStatus(jobId);
    }

    /// <summary>
    /// <c>import.resume</c>: continues a cancelled, failed or interrupted job at its next page, with the same PDF. A PDF
    /// that changed or was removed since is refused (<c>import.pdf-changed</c>).
    /// </summary>
    public ImportJobRecord ResumeImport(Guid jobId)
    {
        var job = ImportStatus(jobId);
        if (job.Status is not (ImportJobStatus.Cancelled or ImportJobStatus.Failed or ImportJobStatus.Interrupted))
            throw new AppValidationException([new("import.not-resumable", $"Only a cancelled, failed or interrupted import can be resumed; this one is {job.Status.ToString().ToLowerInvariant()}.")]);
        var sha = AvailablePdf(FindSourceOrThrow(job.SourceId));
        if (sha != job.Sha256)
            throw new AppValidationException([new("import.pdf-changed", "The source's PDF changed since this import started. Start a new import instead.")]);
        lock (_importGate)
        {
            if (_running is not null)
                throw new AppValidationException([new("import.busy", "Another import is running. Wait for it, or cancel it, then resume this one.")]);
            var resumed = job with { Status = ImportJobStatus.Queued, FailureCode = null, FailureMessage = null, UpdatedAt = _time.GetUtcNow() };
            _store.InTransaction(() => _store.SaveImportJob(resumed));
            Launch(job.Id, "resumed");
        }
        return ImportStatus(jobId);
    }

    /// <summary>
    /// <c>import.search</c> (SPEC I-03: a searchable reference source): pages of the source's PDF whose extracted text
    /// contains the query, with a short snippet around the first match. Search across sources is M7 (BACKLOG B10).
    /// </summary>
    public IReadOnlyList<ImportSearchHit> SearchImportedText(ImportSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = request.Query?.Trim() ?? "";
        if (query.Length is < 2 or > 100)
            throw new AppValidationException([new("import.query-invalid", "Search for 2 to 100 characters.")]);
        var source = FindSourceOrThrow(request.SourceId);
        if (source.AttachmentId is not { } id || _store.FindAttachment(id) is not { Sha256: { } sha })
            return [];
        return [.. _store.SearchImportPages(sha, query, MaxSearchResults).Select(hit => new ImportSearchHit(hit.Page, Snippet(hit.Text, query)))];
    }

    /// <summary><c>import.page</c>: the stored extraction of one page of the source's current PDF, or <c>import.page-not-extracted</c>.</summary>
    public StoredPage ImportedPage(ImportPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = FindSourceOrThrow(request.SourceId);
        if (source.AttachmentId is { } id && _store.FindAttachment(id) is { Sha256: { } sha } && _store.FindImportPage(sha, request.Page) is { } page)
            return page;
        throw new AppValidationException([new("import.page-not-extracted", $"Page {request.Page} of '{source.Title}' has not been extracted. Import it first; the page still opens in the PDF viewer.")]);
    }

    /// <summary>At start: a job that was running when the app closed is interrupted, and <c>import.resume</c> continues it.</summary>
    private void InterruptLeftoverImports()
    {
        foreach (var job in _store.ListImportJobs().Where(j => j.Active))
            Finish(job, ImportJobStatus.Interrupted, "interrupted", "the app closed while it ran", null);
    }

    /// <summary>Removing a source's PDF first stops a job that reads it (the worker holds the file open).</summary>
    private void CancelImportsOf(Guid sourceId)
    {
        Task? task = null;
        lock (_importGate)
        {
            if (_running is { } running && _store.FindImportJob(running.JobId)?.SourceId == sourceId)
            {
                running.Cancel.Cancel();
                task = running.Task;
            }
        }
        if (task is not null)
            WaitQuietly(task);
    }

    /// <summary>The source's PDF hash, when the file is there and unchanged; otherwise the reason.</summary>
    private string AvailablePdf(SourceRecord source)
    {
        if (source.AttachmentId is not { } id || _store.FindAttachment(id) is not { } attachment)
            throw new AppValidationException([new("source.no-pdf", $"'{source.Title}' has no PDF attached. Attach it first.")]);
        var info = Info(source.Id, attachment, checkHash: true);
        if (info.Status != "available" || attachment.Sha256 is null)
            throw new AppValidationException([new(info.Status == "changed" ? "import.pdf-changed" : "attachment.missing",
                info.Status == "changed" ? $"{attachment.OriginalFileName} changed since it was linked. Attach it again, then import." : $"The PDF for '{source.Title}' is missing. Attach it again.")]);
        return attachment.Sha256;
    }

    /// <summary>Starts the background run. The caller holds <see cref="_importGate"/>.</summary>
    private void Launch(Guid jobId, string eventName)
    {
        var cancel = new CancellationTokenSource();
        var task = Task.Run(() => RunImportAsync(jobId, eventName, cancel.Token));
        _running = (jobId, cancel, task);
    }

    private async Task RunImportAsync(Guid jobId, string eventName, CancellationToken cancellationToken)
    {
        var job = _store.FindImportJob(jobId)!;
        try
        {
            job = job with { Status = ImportJobStatus.Running, UpdatedAt = _time.GetUtcNow() };
            _store.InTransaction(() =>
            {
                _store.SaveImportJob(job);
                _store.AddImportAudit(jobId, _time.GetUtcNow(), eventName, $"from page {job.NextPage ?? job.FirstPage}");
            });
            var source = _store.FindSource(job.SourceId);
            var attachment = source?.AttachmentId is { } attachmentId ? _store.FindAttachment(attachmentId) : null;
            if (source is null || attachment?.Sha256 != job.Sha256)
                throw new ExtractionException("import.pdf-changed", "The source's PDF changed or was removed since this import started.");
            // The file itself, not only its record: a linked PDF can change on disk, and its pages are stored under this hash.
            var info = Info(source.Id, attachment, checkHash: true);
            if (info.Status == "changed")
                throw new ExtractionException("import.pdf-changed", "The source's PDF changed since this import started. Start a new import instead.");
            var path = AttachmentFiles.PathOf(_store, attachment);
            if (info.Status != "available" || !File.Exists(path))
                throw new ExtractionException("attachment.missing", "The PDF is missing. Attach it again.");

            var skipped = 0;
            while (true)
            {
                var opened = false;
                try
                {
                    await foreach (var item in Extractor.ExtractAsync(path, new PageScope(job.NextPage ?? job.FirstPage, job.LastPage), cancellationToken).ConfigureAwait(false))
                    {
                        switch (item)
                        {
                            case DocumentOpened document:
                                opened = true;
                                job = job with { PageCount = document.PageCount, UpdatedAt = _time.GetUtcNow() };
                                if (job.FirstPage > document.PageCount)
                                    throw new ExtractionException("source.page-range-invalid", $"The PDF has {document.PageCount} pages.");
                                _store.InTransaction(() => _store.SaveImportJob(job));
                                break;
                            case ExtractedPage page:
                                job = SavePage(job, new StoredPage(page.PageNumber, page.Text, page.FromOcr, page.Width, page.Height, page.Blocks ?? [], page.Warnings ?? [], page.Error));
                                break;
                        }
                    }
                    break;
                }
                catch (ExtractionException ex) when (opened && PageFailures.Contains(ex.Code) && skipped < MaxSkippedPagesPerRun && job.NextPage is { } bad && bad <= (job.ScopeEnd ?? bad))
                {
                    // The worker died on this page (a crash, a timeout, too much memory or output). Only the page fails:
                    // it is stored as unreadable, and a fresh worker continues with the next one (review 2026-09-28).
                    skipped++;
                    job = SavePage(job, new StoredPage(bad, "", false, 0, 0, [], [], ex.Code));
                    _store.AddImportAudit(job.Id, _time.GetUtcNow(), "page-skipped", $"page {bad.ToString(System.Globalization.CultureInfo.InvariantCulture)}: {ex.Code}");
                    if (job.ScopeEnd is { } end && job.NextPage > end)
                        break;
                }
            }
            job = OnPagesExtracted(job, cancellationToken);
            Finish(job with { NextPage = null }, ImportJobStatus.Completed, "completed",
                $"{job.PagesDone} pages, {job.PagesFailed} unreadable, {job.PagesFromOcr} by OCR, {job.PagesWithoutText} without text, {job.Candidates} candidates", null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Finish(job, _disposing ? ImportJobStatus.Interrupted : ImportJobStatus.Cancelled, _disposing ? "interrupted" : "cancelled", $"after {job.PagesDone} pages", null);
        }
        catch (ExtractionException ex)
        {
            Finish(job, ImportJobStatus.Failed, "failed", ex.Code, (ex.Code, ex.Message));
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // A detection pattern ran out of time on some page's text. The pages stay stored and searchable.
            Finish(job, ImportJobStatus.Failed, "failed", "detect.timeout", ("detect.timeout", "Finding entries in the text took too long. The pages were read and stay searchable; try importing a smaller page range."));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            ErrorLog.Record(id, "import.run", ex); // TomeStack's own code threw: no PDF text in it (the worker's never reach here)
            Finish(job, ImportJobStatus.Failed, "failed", "import.internal", ("import.internal", $"The import failed unexpectedly (reference {id})."));
        }
        finally
        {
            lock (_importGate)
            {
                if (_running?.JobId == jobId)
                {
                    _running.Value.Cancel.Dispose();
                    _running = null;
                }
            }
        }
    }

    /// <summary>Worker failures that end one page, not the job: a fresh worker continues with the next page.</summary>
    private static readonly HashSet<string> PageFailures = new(StringComparer.Ordinal) { "worker.page-timeout", "worker.crashed", "worker.memory", "worker.message-too-large" };

    /// <summary>After this many pages that killed the worker, the run fails; <c>import.resume</c> continues after them.</summary>
    public const int MaxSkippedPagesPerRun = 5;

    /// <summary>Stores one page and the job's progress past it, in one transaction.</summary>
    private ImportJobRecord SavePage(ImportJobRecord job, StoredPage page)
    {
        var next = job with
        {
            NextPage = page.Page + 1,
            PagesDone = job.PagesDone + 1,
            PagesFailed = job.PagesFailed + (page.Error is null ? 0 : 1),
            PagesFromOcr = job.PagesFromOcr + (page.FromOcr ? 1 : 0),
            PagesWithoutText = job.PagesWithoutText + (page.Error is null && page.Text.Length == 0 ? 1 : 0),
            UpdatedAt = _time.GetUtcNow(),
        };
        _store.InTransaction(() =>
        {
            _store.SaveImportPage(next.Sha256, page);
            _store.SaveImportJob(next);
        });
        return next;
    }

    /// <summary>After extraction, before the job completes: candidate detection (M4 D3), which stops when the job is cancelled.</summary>
    private ImportJobRecord OnPagesExtracted(ImportJobRecord job, CancellationToken cancellationToken) => DetectCandidates(job, cancellationToken);

    private void Finish(ImportJobRecord job, ImportJobStatus status, string eventName, string? detail, (string Code, string Message)? failure)
    {
        var finished = job with
        {
            Status = status,
            FailureCode = failure?.Code,
            FailureMessage = failure?.Message,
            UpdatedAt = _time.GetUtcNow(),
        };
        _store.InTransaction(() =>
        {
            _store.SaveImportJob(finished);
            _store.AddImportAudit(job.Id, _time.GetUtcNow(), eventName, detail);
        });
    }

    private static void WaitQuietly(Task task)
    {
        try
        {
            task.Wait(TimeSpan.FromSeconds(30));
        }
        catch (AggregateException)
        {
            // The run records its own outcome.
        }
    }

    /// <summary>Up to 160 characters around the first match, on one line.</summary>
    private static string Snippet(string text, string query)
    {
        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, at - 70);
        var length = Math.Min(text.Length - start, 160);
        var snippet = string.Join(' ', text.Substring(start, length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (start > 0 ? "…" : "") + snippet + (start + length < text.Length ? "…" : "");
    }

    private void StopImportsForDispose()
    {
        _disposing = true;
        Task? task;
        lock (_importGate)
        {
            task = _running?.Task;
            _running?.Cancel.Cancel();
        }
        if (task is not null)
            WaitQuietly(task);
    }
}
