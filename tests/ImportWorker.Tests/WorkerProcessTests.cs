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
    public async Task The_worker_runs_under_the_heap_cap_the_app_sets()
    {
        // ADR-009 (c): the child reports its managed-heap limit before it parses anything, and the app refuses to go on
        // unless the cap applies (worker.limits). Review 2026-09-28: nothing showed DOTNET_GCHeapHardLimit took effect.
        long reported = 0;
        const long cap = 256L << 20;
        var extractor = new WorkerProcessExtractor(Worker, new ExtractionLimits { HeapHardLimit = cap }) { HeapLimitReported = limit => reported = limit };
        await foreach (var _ in extractor.ExtractAsync(FixturePdfs.ImportPath, PageScope.WholeDocument, CancellationToken.None))
        {
        }
        Assert.InRange(reported, 1, cap);
    }

    [Fact]
    public async Task A_decompression_bomb_fails_only_its_own_run_and_never_succeeds()
    {
        // 512 MB of spaces in about half a megabyte of Flate data, against a 64 MB heap cap in the child. Measured
        // 2026-09-28: PdfPig does not inflate it all at once, so the heap cap does not trip; it churns until the page
        // timeout stops the child. The test uses a 5 s page timeout instead of the default 60 s. The run must fail with
        // one of the worker limits (review 2026-09-28: the test used to accept a success too).
        using var file = FixturePdfs.Write(FixturePdfs.Bomb(512 << 20));
        var limits = new ExtractionLimits { HeapHardLimit = 64 << 20, PageTimeout = TimeSpan.FromSeconds(5) };
        var pid = 0;

        var refused = await Assert.ThrowsAsync<ExtractionException>(() => Run(file.Path, limits, started: id => pid = id));

        Assert.Contains(refused.Code, new[] { "worker.page-timeout", "worker.memory", "worker.crashed" });
        Assert.True(Gone(pid), "the worker process ended");
    }

    [Fact]
    public async Task A_line_longer_than_the_limit_is_refused_before_it_is_held()
    {
        // The app reads worker lines of bounded length only; ReadLineAsync would hold a line of any length.
        var reader = new BoundedLineReader(new StringReader("{\"type\":\"done\"}\r\n" + new string('x', 200_000) + "\nlast"), 100_000);
        Assert.Equal("{\"type\":\"done\"}", await reader.ReadLineAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadLineAsync(CancellationToken.None));

        var partial = new BoundedLineReader(new StringReader("a\nb"), 10);
        Assert.Equal("a", await partial.ReadLineAsync(CancellationToken.None));
        Assert.Equal("b", await partial.ReadLineAsync(CancellationToken.None));
        Assert.Null(await partial.ReadLineAsync(CancellationToken.None));
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
