using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M4 D2 (ARCHITECTURE "Import lifecycle", SPEC I-01, I-03, ADR-009 (d)): cancellable, resumable import jobs with
/// limits, progress and a local audit log. A job survives a restart, page navigation never depends on it, and
/// extracted text never leaves the machine. The original fixture book only.
/// </summary>
public class ImportJobTests
{
    private static byte[] FixtureBook => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf"));

    private static Guid SourceWithBook(TempApp temp)
    {
        var source = temp.App.CreateHomebrewSource(new("Test Import Book", [RulesFamilies.Srd521]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", FixtureBook);
        return source.Id;
    }

    private static ImportJobRecord Settle(TempApp temp, Guid jobId)
    {
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        return temp.App.ImportStatus(jobId);
    }

    private static ImportJobRecord WaitFor(TempApp temp, Guid jobId, Func<ImportJobRecord, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var job = temp.App.ImportStatus(jobId);
            if (condition(job))
                return job;
            Thread.Sleep(20);
        }
        throw new TimeoutException("The import did not reach the expected state.");
    }

    /// <summary>Real extraction that waits for the test before handing over each page.</summary>
    private sealed class GatedExtractor : IDocumentExtractor
    {
        private readonly PdfPigExtractor _inner = new();

        public SemaphoreSlim Gate { get; } = new(0);

        public List<PageScope> Scopes { get; } = [];

        public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Scopes.Add(scope);
            await foreach (var item in _inner.ExtractAsync(path, scope, cancellationToken))
            {
                if (item is ExtractedPage)
                    await Gate.WaitAsync(cancellationToken);
                yield return item;
            }
        }
    }

    private sealed class FailingExtractor(string code) : IDocumentExtractor
    {
        public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new ExtractionException(code, "Refused for the test.");
#pragma warning disable CS0162 // an iterator needs a yield
            yield break;
#pragma warning restore CS0162
        }
    }

    [Fact]
    public void A_whole_document_import_stores_every_page_with_its_layout_and_an_audit_without_text()
    {
        using var temp = new TempApp();
        var sourceId = SourceWithBook(temp);

        var started = temp.App.StartImport(new(sourceId, WholeDocument: true));
        var job = Settle(temp, started.Id);

        Assert.Equal((ImportJobStatus.Completed, 6, 6, 0, 1), (job.Status, job.PageCount, job.PagesDone, job.PagesFailed, job.PagesWithoutText));
        Assert.Null(job.NextPage);
        var page = temp.App.ImportedPage(new(sourceId, 2));
        Assert.Contains("Fixture Ember Lance", page.Text, StringComparison.Ordinal);
        Assert.Contains(page.Blocks, b => b.Bold && b.Text.StartsWith("Fixture Ember Lance", StringComparison.Ordinal));
        Assert.Contains("page.no-text", temp.App.ImportedPage(new(sourceId, 6)).Warnings);

        var audit = temp.App.ImportAudit(job.Id);
        Assert.Equal(["created", "started", "completed"], audit.Select(a => a.Event));
        Assert.Equal("6 pages, 0 unreadable, 0 by OCR, 1 without text, 9 candidates", audit[^1].Detail);
        Assert.All(audit, a => Assert.DoesNotContain("Fixture", a.Detail ?? "", StringComparison.Ordinal));
        // Extraction changes no content: no revision cites the source.
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == sourceId);
    }

    [Fact]
    public void A_completed_import_lists_its_candidates_filtered_by_page_kind_and_confidence_and_changes_no_content()
    {
        // M4 D3: detection runs after extraction; the candidates are proposals only (ADR-004).
        using var temp = new TempApp();
        var sourceId = SourceWithBook(temp);
        var revisions = temp.App.Store.ListRevisions().Count;

        var job = Settle(temp, temp.App.StartImport(new(sourceId, WholeDocument: true)).Id);
        var all = temp.App.ListCandidates(new(job.Id));

        Assert.Equal(9, job.Candidates);
        Assert.Equal(9, all.Count);
        Assert.All(all, c => Assert.Equal(CandidateStatus.Pending, c.Status));
        Assert.Equal(["Fixture Ember Lance", "Fixture Frost Veil"], temp.App.ListCandidates(new(job.Id, Kind: ContentKind.Spell)).Select(c => c.Current.ProposedName));
        Assert.Equal(4, temp.App.ListCandidates(new(job.Id, Page: 4)).Count);
        Assert.All(temp.App.ListCandidates(new(job.Id, MaxConfidence: 0.7)), c => Assert.True(c.Current.Confidence <= 0.7));
        Assert.Equal(revisions, temp.App.Store.ListRevisions().Count); // no content was created
        // The source's families (SRD 5.2.1 only) bound the candidates' families.
        Assert.All(all, c => Assert.Equal([RulesFamilies.Srd521], c.Current.RulesFamilies));

        // Detecting again (a resumed or re-run job) replaces only what is still pending.
        Assert.Equal(9, temp.App.ListCandidates(new(job.Id)).Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void A_page_range_imports_only_its_pages()
    {
        using var temp = new TempApp();
        var sourceId = SourceWithBook(temp);

        var job = Settle(temp, temp.App.StartImport(new(sourceId, 2, 3)).Id);

        Assert.Equal((ImportJobStatus.Completed, 2), (job.Status, job.PagesDone));
        Assert.Equal("import.page-not-extracted", Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.ImportedPage(new(sourceId, 4))).Problems).Code);
    }

    [Fact]
    public void Imports_are_refused_for_bundled_sources_missing_PDFs_bad_ranges_and_while_another_runs()
    {
        var gated = new GatedExtractor();
        using var temp = new TempApp(gated);
        string Code(Action action) => Assert.Single(Assert.Throws<AppValidationException>(action).Problems).Code;
        var srd = temp.App.Store.ListSources().First(s => s.Title.Contains("5.2.1", StringComparison.Ordinal)).Id;
        var empty = temp.App.CreateHomebrewSource(new("Test Empty Book", [RulesFamilies.Srd521])).Id;
        var sourceId = SourceWithBook(temp);

        Assert.Equal("source.not-editable", Code(() => temp.App.StartImport(new(srd, WholeDocument: true))));
        Assert.Equal("source.no-pdf", Code(() => temp.App.StartImport(new(empty, WholeDocument: true))));
        Assert.Equal("source.page-range-invalid", Code(() => temp.App.StartImport(new(sourceId, 5, 2))));
        Assert.Equal("source.page-range-invalid", Code(() => temp.App.StartImport(new(sourceId, 0, 2))));

        var running = temp.App.StartImport(new(sourceId, WholeDocument: true));
        Assert.Equal("import.busy", Code(() => temp.App.StartImport(new(sourceId, 1, 1))));
        temp.App.CancelImport(running.Id);
    }

    [Fact]
    public void A_cancelled_import_keeps_its_pages_and_resumes_at_the_next_page()
    {
        var gated = new GatedExtractor();
        using var temp = new TempApp(gated);
        var sourceId = SourceWithBook(temp);
        var job = temp.App.StartImport(new(sourceId, WholeDocument: true));

        gated.Gate.Release(2);
        WaitFor(temp, job.Id, j => j.PagesDone == 2);
        var cancelled = temp.App.CancelImport(job.Id);

        Assert.Equal((ImportJobStatus.Cancelled, 2, 3), (cancelled.Status, cancelled.PagesDone, cancelled.NextPage));
        Assert.Contains("Fixture Ember Lance", temp.App.ImportedPage(new(sourceId, 2)).Text, StringComparison.Ordinal);
        Assert.Equal("import.not-running", Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.CancelImport(job.Id)).Problems).Code);

        gated.Gate.Release(100);
        var resumed = temp.App.ResumeImport(job.Id);
        var done = Settle(temp, resumed.Id);
        Assert.Equal((ImportJobStatus.Completed, 6), (done.Status, done.PagesDone));
        Assert.Equal(3, gated.Scopes[^1].FirstPage); // it did not read pages 1-2 again
        Assert.Equal(["created", "started", "cancelled", "resumed", "completed"], temp.App.ImportAudit(job.Id).Select(a => a.Event));
        Assert.Equal("import.not-resumable", Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.ResumeImport(job.Id)).Problems).Code);
    }

    [Fact]
    public void An_import_survives_a_restart_as_interrupted_and_resumes()
    {
        var gated = new GatedExtractor();
        using var temp = new TempApp(gated);
        var sourceId = SourceWithBook(temp);
        var job = temp.App.StartImport(new(sourceId, WholeDocument: true));
        gated.Gate.Release(1);
        WaitFor(temp, job.Id, j => j.PagesDone == 1);

        temp.Reopen(); // the app closes while the job runs

        var interrupted = temp.App.ImportStatus(job.Id);
        Assert.Equal((ImportJobStatus.Interrupted, 1, 2), (interrupted.Status, interrupted.PagesDone, interrupted.NextPage));
        gated.Gate.Release(100);
        Assert.Equal(ImportJobStatus.Completed, Settle(temp, temp.App.ResumeImport(job.Id).Id).Status);
    }

    [Fact]
    public void A_job_left_running_by_a_crash_is_interrupted_at_the_next_start()
    {
        using var temp = new TempApp();
        var sourceId = SourceWithBook(temp);
        var job = Settle(temp, temp.App.StartImport(new(sourceId, 1, 1)).Id);
        temp.App.Store.SaveImportJob(job with { Status = ImportJobStatus.Running, NextPage = 1 }); // as if the process died

        temp.Reopen();

        Assert.Equal(ImportJobStatus.Interrupted, temp.App.ImportStatus(job.Id).Status);
        Assert.Equal("interrupted", temp.App.ImportAudit(job.Id)[^1].Event);
    }

    [Fact]
    public void A_failed_import_leaves_the_PDF_and_page_navigation_working()
    {
        using var temp = new TempApp(new FailingExtractor("pdf.encrypted"));
        var sourceId = SourceWithBook(temp);

        var job = Settle(temp, temp.App.StartImport(new(sourceId, WholeDocument: true)).Id);

        Assert.Equal((ImportJobStatus.Failed, "pdf.encrypted"), (job.Status, job.FailureCode));
        Assert.Equal("failed", temp.App.ImportAudit(job.Id)[^1].Event);
        // ARCHITECTURE: page navigation never depends on parsing. The attachment, page import and removal preview still work.
        Assert.Equal("available", temp.App.GetAttachment(sourceId)!.Status);
        Assert.Equal(new PageRef(2), temp.App.ImportPages(new(sourceId, 2)).Provenance.Page);
        Assert.Equal(1, temp.App.PreviewDetach(sourceId).PageLinks);
    }

    [Fact]
    public void Removing_the_PDF_stops_a_running_import_and_a_changed_PDF_cannot_be_resumed()
    {
        var gated = new GatedExtractor();
        using var temp = new TempApp(gated);
        var sourceId = SourceWithBook(temp);
        var job = temp.App.StartImport(new(sourceId, WholeDocument: true));
        gated.Gate.Release(1);
        WaitFor(temp, job.Id, j => j.PagesDone == 1);

        temp.App.Detach(sourceId, confirm: true);

        Assert.Equal(ImportJobStatus.Cancelled, temp.App.ImportStatus(job.Id).Status);
        temp.App.AttachPdf(sourceId, "other.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n% a different file\n"));
        Assert.Equal("import.pdf-changed", Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.ResumeImport(job.Id)).Problems).Code);
    }

    [Fact]
    public void Imported_text_is_searchable_within_its_source()
    {
        using var temp = new TempApp();
        var sourceId = SourceWithBook(temp);
        Settle(temp, temp.App.StartImport(new(sourceId, WholeDocument: true)).Id);
        var other = temp.App.CreateHomebrewSource(new("Test Other Book", [RulesFamilies.Srd521])).Id;

        var hit = Assert.Single(temp.App.SearchImportedText(new(sourceId, "hookblade")));
        Assert.Equal(4, hit.Page);
        Assert.Contains("Fixture Hookblade", hit.Snippet, StringComparison.Ordinal);
        Assert.Equal([2, 3], temp.App.SearchImportedText(new(sourceId, "fixture ember")).Select(h => h.Page).Concat(temp.App.SearchImportedText(new(sourceId, "keen watcher")).Select(h => h.Page)));
        Assert.Empty(temp.App.SearchImportedText(new(other, "hookblade")));
        Assert.Equal("import.query-invalid", Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.SearchImportedText(new(sourceId, "x"))).Problems).Code);
    }

    [Fact]
    public void Extracted_text_is_never_exported_in_a_backup_or_a_share()
    {
        using var temp = new TempApp();
        var sourceId = SourceWithBook(temp);
        Settle(temp, temp.App.StartImport(new(sourceId, WholeDocument: true)).Id);
        var draft = temp.App.ImportPages(new(sourceId, 2)); // content that cites the source, so the source is exported
        temp.App.Publish(draft.Reference);
        var fixture = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        var character = temp.App.SaveCharacter(fixture with { Pins = [.. fixture.Pins, temp.App.ListRevisions(draft.ContentId).Last().Reference] }).Character;

        foreach (var purpose in new[] { ExportPurpose.Backup, ExportPurpose.Share })
        {
            var package = temp.App.ExportCharacters([character.Id], purpose);
            using var zip = new ZipArchive(new MemoryStream(package.Content));
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd();
                Assert.DoesNotContain("Fixture Ember Lance", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Hookblade", text, StringComparison.Ordinal);
                Assert.False(entry.FullName.Contains("import", StringComparison.OrdinalIgnoreCase), entry.FullName);
            }
        }
    }

    [Fact]
    public void The_commands_round_trip_over_the_dispatcher()
    {
        using var temp = new TempApp();
        var sourceId = SourceWithBook(temp);
        var dispatcher = new CommandDispatcher(temp.App);
        JsonElement Call(string command, object payload) =>
            JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

        var started = Call("import.start", new { sourceId, wholeDocument = true });
        Assert.True(started.GetProperty("ok").GetBoolean());
        var jobId = started.GetProperty("result").GetProperty("id").GetGuid();
        Settle(temp, jobId);

        Assert.Equal("completed", Call("import.status", new { jobId }).GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(1, Call("import.list", new { sourceId }).GetProperty("result").GetArrayLength());
        Assert.Equal(4, Call("import.search", new { sourceId, query = "Hookblade" }).GetProperty("result")[0].GetProperty("page").GetInt32());
        Assert.Contains("Ember", Call("import.page", new { sourceId, page = 2 }).GetProperty("result").GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("import.not-resumable", Call("import.resume", new { jobId }).GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    /// <summary>A worker that dies on some pages, as a hostile page kills the real one. Other pages are short original text.</summary>
    private sealed class DyingExtractor(int pageCount, IReadOnlySet<int> deadly) : IDocumentExtractor
    {
        public List<int> Starts { get; } = [];

        public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            var (first, last) = scope.Resolve(pageCount);
            Starts.Add(first);
            yield return new DocumentOpened(pageCount);
            for (var number = first; number <= last; number++)
            {
                if (deadly.Contains(number))
                    throw new ExtractionException("worker.crashed", "The import worker stopped unexpectedly (exit code 1).");
                yield return new ExtractedPage(number, $"Original test page {number}.", false, [], 612, 792);
            }
        }
    }

    [Fact]
    public void A_page_that_kills_the_worker_fails_alone_and_the_rest_of_the_book_is_read()
    {
        // Review 2026-09-28: a page that crashed the worker failed the job, and resuming started on that same page again.
        var worker = new DyingExtractor(6, new HashSet<int> { 3 });
        using var temp = new TempApp(worker);
        var job = Settle(temp, temp.App.StartImport(new(SourceWithBook(temp), WholeDocument: true)).Id);

        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal((6, 1), (job.PagesDone, job.PagesFailed));
        Assert.Equal("worker.crashed", temp.App.Store.FindImportPage(job.Sha256, 3)!.Error);
        Assert.Equal([1, 4], worker.Starts); // a fresh worker continued after the page
        Assert.Contains(temp.App.ImportAudit(job.Id), e => e.Event == "page-skipped" && e.Detail == "page 3: worker.crashed");
    }

    [Fact]
    public void After_too_many_pages_that_kill_the_worker_the_run_fails_and_resume_continues_after_them()
    {
        using var temp = new TempApp(new DyingExtractor(6, new HashSet<int> { 1, 2, 3, 4, 5, 6 }));
        var job = Settle(temp, temp.App.StartImport(new(SourceWithBook(temp), WholeDocument: true)).Id);

        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Equal("worker.crashed", job.FailureCode);
        Assert.Equal((TomeStackApp.MaxSkippedPagesPerRun, TomeStackApp.MaxSkippedPagesPerRun + 1), (job.PagesFailed, job.NextPage));

        temp.App.ResumeImport(job.Id);
        job = Settle(temp, job.Id);
        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.Equal((6, 6), (job.PagesDone, job.PagesFailed));
    }

    [Fact]
    public void A_second_import_of_the_same_PDF_does_not_propose_what_was_already_reviewed()
    {
        using var temp = new TempApp();
        var sourceId = SourceWithBook(temp);
        var first = Settle(temp, temp.App.StartImport(new(sourceId, WholeDocument: true)).Id);
        var ignored = temp.App.ListCandidates(new(first.Id))[0];
        temp.App.IgnoreCandidate(ignored.Id);

        var second = Settle(temp, temp.App.StartImport(new(sourceId, WholeDocument: true)).Id);

        Assert.Equal(first.Candidates - 1, second.Candidates);
        Assert.DoesNotContain(temp.App.ListCandidates(new(second.Id)), c => c.Candidate.ProposedName == ignored.Candidate.ProposedName && c.Candidate.Page.Start == ignored.Candidate.Page.Start);
    }

    [Fact]
    public void Detection_reads_blocks_only_up_to_its_memory_budget_and_says_where_it_stopped()
    {
        using var temp = new TempApp();
        var job = Settle(temp, temp.App.StartImport(new(SourceWithBook(temp), WholeDocument: true)).Id);

        var all = temp.App.Store.ListImportPagesForDetection(job.Sha256, 1, int.MaxValue, long.MaxValue, out var none);
        Assert.Equal(6, all.Count);
        Assert.Null(none);
        Assert.All(all, p => Assert.Equal("", p.Text)); // detection never needs the joined text

        var range = temp.App.Store.ListImportPagesForDetection(job.Sha256, 2, 5, long.MaxValue, out _);
        Assert.Equal([2, 3, 4, 5], range.Select(p => p.Page));
        var over = temp.App.Store.ListImportPagesForDetection(job.Sha256, 2, 5, 1, out var stopped);
        Assert.Empty(over);
        Assert.Equal(2, stopped);
    }
}
