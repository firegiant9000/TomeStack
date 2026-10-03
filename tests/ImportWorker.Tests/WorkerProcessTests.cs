using System.Diagnostics;
using System.Text.Json;
using TomeStack.ImportWorker.Extraction;
using TomeStack.ImportWorker.Forms;

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

    // The character-sheet importer's reads (features/ddb-pdf-import.md S1): the same child, a second request kind.

    private static readonly FormPdfWriter.FormSpec[] FixtureForm =
    [
        new("fixture.name", "Testy McFixture"),
        new("fixture.inspired", Checked: true, Page: 2),
    ];

    private static async Task<(int Exit, List<WorkerMessage> Lines)> RunInProcess(WorkerRequest request)
    {
        var output = new StringWriter();
        var exit = await WorkerMain.RunAsync(new StringReader(JsonSerializer.Serialize(request, WorkerJson.Options)), output, null, CancellationToken.None);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonSerializer.Deserialize<WorkerMessage>(l, WorkerJson.Options)!).ToList();
        return (exit, lines);
    }

    /// <summary>
    /// A stand-in worker that ignores its request and writes <paramref name="lines"/> (a batch file that types a file), for
    /// what the real worker never sends.
    /// </summary>
    private static FixturePdfs.TempFile FakeWorker(params string[] lines)
    {
        var script = FixturePdfs.Write("@type \"%~dp0lines.txt\"\r\n"u8.ToArray(), "fake-worker.cmd");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(script.Path)!, "lines.txt"), string.Join('\n', lines) + "\n");
        return script;
    }

    [Fact]
    public async Task A_formFields_request_gets_worker_fields_and_done_through_the_child_process()
    {
        using var file = FixturePdfs.Write(FormPdfWriter.Write(FixtureForm));

        var (exit, lines) = await RunInProcess(new WorkerRequest(file.Path, null, null, WorkerFormReader.DefaultLimits, Kind: "formFields"));
        Assert.Equal(0, exit);
        Assert.Equal(["worker", "fields", "done"], lines.Select(l => l.Type));

        var inWorker = await new WorkerFormReader(Worker).ReadAsync(file.Path, CancellationToken.None);
        Assert.Equal(AcroFormReader.Read(file.Path, WorkerFormReader.DefaultLimits, FormLimits.Default), inWorker);
        Assert.Equal(new FormField("fixture.inspired", "checkbox", 2, Checked: true, OnState: "Yes"), inWorker[1]);
    }

    [Fact]
    public async Task A_formFields_request_on_a_file_without_a_form_gets_an_error_line_and_exit_code_2()
    {
        using var file = FixturePdfs.Write(FixturePdfs.Pages(1));

        var (exit, lines) = await RunInProcess(new WorkerRequest(file.Path, null, null, WorkerFormReader.DefaultLimits, Kind: "formFields"));
        Assert.Equal(2, exit);
        Assert.Equal(["worker", "error"], lines.Select(l => l.Type));
        Assert.Equal("ddb.no-form-fields", lines[1].Code);

        var refused = await Assert.ThrowsAsync<ExtractionException>(() => new WorkerFormReader(Worker).ReadAsync(file.Path, CancellationToken.None));
        Assert.Equal("ddb.no-form-fields", refused.Code);
    }

    [Fact]
    public async Task A_formFields_request_uses_the_form_limits_it_carries()
    {
        using var file = FixturePdfs.Write(FormPdfWriter.Write(FixtureForm));
        var refused = await Assert.ThrowsAsync<ExtractionException>(() => new WorkerFormReader(Worker, form: new FormLimits { MaxFields = 1 }).ReadAsync(file.Path, CancellationToken.None));
        Assert.Equal("ddb.too-many-fields", refused.Code);
    }

    [Fact]
    public async Task An_unknown_request_kind_is_a_bad_request()
    {
        using var file = FixturePdfs.Write(FormPdfWriter.Write(FixtureForm));
        var (exit, lines) = await RunInProcess(new WorkerRequest(file.Path, null, null, WorkerFormReader.DefaultLimits, Kind: "fixture-kind"));
        Assert.Equal(3, exit);
        Assert.Equal("worker.bad-request", Assert.Single(lines).Code);
    }

    [Fact]
    public async Task A_fields_line_longer_than_the_form_limit_is_refused_before_it_is_held()
    {
        // The real worker stops at the limits, so a stand-in sends what a compromised one could.
        var form = new FormLimits { MaxFields = 1, MaxTotalValueChars = 10 };
        var value = new string('F', FormLimits.MaxFieldsMessageChars(form));
        using var fake = FakeWorker("{\"type\":\"worker\",\"heapLimit\":1}", $"{{\"type\":\"fields\",\"fields\":[{{\"name\":\"fixture.a\",\"type\":\"text\",\"value\":\"{value}\"}}]}}", "{\"type\":\"done\"}");
        using var file = FixturePdfs.Write(FormPdfWriter.Write(FixtureForm));

        var refused = await Assert.ThrowsAsync<ExtractionException>(() => new WorkerFormReader(fake.Path, form: form).ReadAsync(file.Path, CancellationToken.None));

        Assert.Equal("worker.message-too-large", refused.Code);
    }

    [Theory]
    [InlineData("{\"type\":\"page\",\"page\":{\"pageNumber\":1,\"text\":\"\",\"fromOcr\":false}}")]
    [InlineData("{\"type\":\"done\"}")]
    [InlineData("{\"type\":\"fields\"}")]
    [InlineData("{\"type\":\"fields\",\"fields\":[{\"name\":\"fixture.a\",\"type\":\"text\"},{\"name\":\"fixture.b\",\"type\":\"text\"}]}")]
    [InlineData("{\"type\":\"fields\",\"fields\":[{\"name\":\"fixture.a\",\"type\":\"text\",\"value\":\"Fixture value over ten\"}]}")]
    [InlineData("{\"type\":\"fields\",\"fields\":[]}\n{\"type\":\"fields\",\"fields\":[]}")]
    public async Task A_formFields_worker_that_sends_anything_but_worker_fields_done_or_error_is_a_protocol_error(string sent)
    {
        var form = new FormLimits { MaxFields = 1, MaxValueChars = 10 };
        using var fake = FakeWorker("{\"type\":\"worker\",\"heapLimit\":1}", sent, "{\"type\":\"done\"}");
        using var file = FixturePdfs.Write(FormPdfWriter.Write(FixtureForm));

        var refused = await Assert.ThrowsAsync<ExtractionException>(() => new WorkerFormReader(fake.Path, form: form).ReadAsync(file.Path, CancellationToken.None));

        Assert.Equal("worker.protocol", refused.Code);
        Assert.DoesNotContain("Fixture value", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pdf.encrypted", "pdf.encrypted")]
    [InlineData("ddb.no-form-fields", "ddb.no-form-fields")]
    [InlineData("pdf.too-many-pages", "pdf.too-many-pages")]
    [InlineData("fixture.made-up", "worker.protocol")]
    [InlineData("worker.crashed", "worker.protocol")]
    public async Task A_formFields_error_keeps_only_a_known_code_and_the_apps_own_message(string sent, string expected)
    {
        // A stand-in for a compromised worker: its code is checked against the reader's own, and its text is never shown.
        using var fake = FakeWorker("{\"type\":\"worker\",\"heapLimit\":1}", $"{{\"type\":\"error\",\"code\":\"{sent}\",\"message\":\"Fixture worker text\"}}");
        using var file = FixturePdfs.Write(FormPdfWriter.Write(FixtureForm));

        var refused = await Assert.ThrowsAsync<ExtractionException>(() => new WorkerFormReader(fake.Path).ReadAsync(file.Path, CancellationToken.None));

        Assert.Equal(expected, refused.Code);
        Assert.DoesNotContain("Fixture worker text", refused.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(refused.Message));
    }

    [Fact]
    public async Task A_formFields_read_that_sends_no_line_for_the_timeout_is_stopped_with_worker_page_timeout()
    {
        using var file = FixturePdfs.Write(FormPdfWriter.Write(FixtureForm));
        var limits = WorkerFormReader.DefaultLimits with { PageTimeout = TimeSpan.FromMilliseconds(1) };

        var refused = await Assert.ThrowsAsync<ExtractionException>(() => new WorkerFormReader(Worker, limits).ReadAsync(file.Path, CancellationToken.None));

        Assert.Equal("worker.page-timeout", refused.Code);
    }

    [Fact]
    public async Task A_formFields_read_with_a_missing_worker_is_reported()
    {
        using var file = FixturePdfs.Write(FormPdfWriter.Write(FixtureForm));
        var refused = await Assert.ThrowsAsync<ExtractionException>(() => new WorkerFormReader(Path.Combine(AppContext.BaseDirectory, "no-such-worker.exe")).ReadAsync(file.Path, CancellationToken.None));
        Assert.Equal("worker.missing", refused.Code);
    }

    [Fact]
    public void The_form_reader_defaults_are_the_sheet_limits()
    {
        // features/ddb-pdf-import.md "Limits": 20 MB, 50 pages, 30 s per read; 2,000 fields, 20,000 characters per value, 1 MB in total.
        var limits = WorkerFormReader.DefaultLimits;
        Assert.Equal((20L << 20, 50, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)), (limits.MaxBytes, limits.MaxPages, limits.PageTimeout, limits.RunTimeout));
        Assert.Equal((ExtractionLimits.Default.HeapHardLimit, ExtractionLimits.Default.MaxWorkingSet), (limits.HeapHardLimit, limits.MaxWorkingSet));
        Assert.Equal((2_000, 20_000, 1_048_576), (FormLimits.Default.MaxFields, FormLimits.Default.MaxValueChars, FormLimits.Default.MaxTotalValueChars));
    }
}
