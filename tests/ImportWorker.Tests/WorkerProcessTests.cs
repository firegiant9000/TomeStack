using System.Diagnostics;
using TomeStack.ImportWorker.Extraction;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// M4 D1, ADR-009 (c): extraction in the isolated worker process, as the app runs it. The parent enforces the limits
/// from outside (page and run timeouts, a working-set watchdog, the child's heap cap) and kills the child on cancel. A
/// hostile PDF can fail only its own run.
/// </summary>
public class WorkerProcessTests
{
    private static string Worker => Path.Combine(AppContext.BaseDirectory, "TomeStack.ImportWorker.Host.exe");

    private static async Task<List<ExtractionEvent>> Run(string path, ExtractionLimits? limits = null, PageScope? scope = null, Action<int>? started = null, CancellationToken cancellationToken = default)
    {
        var events = new List<ExtractionEvent>();
        var extractor = new WorkerProcessExtractor(Worker, limits) { ProcessStarted = started };
        await foreach (var item in extractor.ExtractAsync(path, scope ?? PageScope.WholeDocument, cancellationToken))
            events.Add(item);
        return events;
    }

    private static bool Gone(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit(5_000);
        }
        catch (ArgumentException)
        {
            return true; // no such process
        }
    }

    [Fact]
    public async Task The_worker_extracts_the_fixture_book_exactly_as_the_extractor_does()
    {
        var inWorker = await Run(FixturePdfs.ImportPath);
        var inProcess = new List<ExtractionEvent>();
        await foreach (var item in new PdfPigExtractor().ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
            inProcess.Add(item);

        Assert.Equal(FixturePdfs.ImportPageCount, Assert.IsType<DocumentOpened>(inWorker[0]).PageCount);
        Assert.Equal(inProcess.OfType<ExtractedPage>().Select(p => p.Text), inWorker.OfType<ExtractedPage>().Select(p => p.Text));
        Assert.Equal(inProcess.OfType<ExtractedPage>().Select(p => p.Blocks!.Count), inWorker.OfType<ExtractedPage>().Select(p => p.Blocks!.Count));
    }

    [Fact]
    public async Task A_document_failure_in_the_worker_arrives_as_its_code()
    {
        using var file = FixturePdfs.Write(FixturePdfs.Encrypted());
        var refused = await Assert.ThrowsAsync<ExtractionException>(() => Run(file.Path));
        Assert.Equal("pdf.encrypted", refused.Code);
    }

    [Fact]
    public async Task Cancelling_kills_the_worker()
    {
        using var cancel = new CancellationTokenSource();
        var pid = 0;
        var extractor = new WorkerProcessExtractor(Worker) { ProcessStarted = id => pid = id };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in extractor.ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, cancel.Token))
            {
                if (item is DocumentOpened)
                    await cancel.CancelAsync();
            }
        });
        Assert.NotEqual(0, pid);
        Assert.True(Gone(pid), "the worker process ended");
    }

    [Fact]
    public async Task Stopping_early_kills_the_worker()
    {
        var pid = 0;
        var extractor = new WorkerProcessExtractor(Worker) { ProcessStarted = id => pid = id };
        await foreach (var item in extractor.ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
            break;
        Assert.True(Gone(pid));
    }

    [Fact]
    public async Task A_worker_that_sends_nothing_in_time_is_stopped()
    {
        var refused = await Assert.ThrowsAsync<ExtractionException>(() => Run(FixturePdfs.ImportPath, new ExtractionLimits { PageTimeout = TimeSpan.FromMilliseconds(1) }));
        Assert.Equal("worker.page-timeout", refused.Code);
    }

    [Fact]
    public async Task A_worker_over_the_memory_limit_is_killed()
    {
        var pid = 0;
        var refused = await Assert.ThrowsAsync<ExtractionException>(() => Run(FixturePdfs.ImportPath, new ExtractionLimits { MaxWorkingSet = 1 << 20 }, started: id => pid = id));
        Assert.Equal("worker.memory", refused.Code);
        Assert.True(Gone(pid));
    }

    [Fact]
    public async Task A_decompression_bomb_fails_only_its_own_run()
    {
        // 512 MB of spaces in about half a megabyte of Flate data, against a 64 MB heap cap in the child. Measured
        // 2026-09-28: PdfPig does not inflate it all at once (the heap cap never trips); it churns through it until the
        // page timeout stops the child. The test uses a 5 s page timeout instead of the default 60 s.
        using var file = FixturePdfs.Write(FixturePdfs.Bomb(512 << 20));
        var limits = new ExtractionLimits { HeapHardLimit = 64 << 20, PageTimeout = TimeSpan.FromSeconds(5) };

        var outcome = await Record.ExceptionAsync(() => Run(file.Path, limits));

        // Out of memory (crashed or killed), too slow (stopped), or the page unreadable: only this run fails.
        if (outcome is ExtractionException refused)
            Assert.True(new[] { "worker.crashed", "worker.memory", "worker.page-timeout", "pdf.unreadable" }.Contains(refused.Code), $"unexpected code {refused.Code}");
        else
            Assert.Null(outcome);
        Assert.Equal(FixturePdfs.ImportPageCount, Assert.IsType<DocumentOpened>((await Run(FixturePdfs.ImportPath))[0]).PageCount);
    }

    [Fact]
    public async Task A_missing_worker_is_reported()
    {
        var extractor = new WorkerProcessExtractor(Path.Combine(AppContext.BaseDirectory, "no-such-worker.exe"));
        var refused = await Assert.ThrowsAsync<ExtractionException>(async () =>
        {
            await foreach (var _ in extractor.ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
            {
            }
        });
        Assert.Equal("worker.missing", refused.Code);
    }

    [Fact]
    public async Task The_worker_refuses_a_malformed_request()
    {
        var output = new StringWriter();
        var exit = await WorkerMain.RunAsync(new StringReader("{ not json"), output, null, CancellationToken.None);
        Assert.Equal(3, exit);
        Assert.Contains("worker.bad-request", output.ToString(), StringComparison.Ordinal);
    }
}
