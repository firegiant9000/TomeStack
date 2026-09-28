using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.ImportWorker.Extraction;

/// <summary>
/// ADR-009 (c): the one request line the worker reads from stdin. <paramref name="Path"/> is a file the app resolved, never one from the UI.
/// <paramref name="ParentProcessId"/> is the app's process: the worker exits when it does, so a crashed app leaves no worker behind.
/// </summary>
public sealed record WorkerRequest(string Path, int? FirstPage, int? LastPage, ExtractionLimits Limits, int? ParentProcessId = null);

/// <summary>
/// One line the worker writes to stdout: <c>worker</c> first (with its managed-heap limit, <paramref name="HeapLimit"/>), then
/// <c>document</c>, <c>page</c>, <c>error</c> (with a code) or <c>done</c>.
/// </summary>
public sealed record WorkerMessage(string Type, int? PageCount = null, ExtractedPage? Page = null, string? Code = null, string? Message = null, long? HeapLimit = null);

/// <summary>JSON lines over the child's stdin and stdout: compact camelCase, one object per line.</summary>
public static class WorkerJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>A request line is small; anything longer is not a request.</summary>
    public const int MaxRequestChars = 64 * 1024;

    /// <summary>
    /// The longest line the app reads from the worker under these limits: three copies of a page's text (page, blocks,
    /// lines), each character escaped as at most six, plus the boxes of every block and line. Anything longer fails the run
    /// (<c>worker.message-too-large</c>) before it is held in the app's memory.
    /// </summary>
    public static int MaxMessageChars(ExtractionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return (int)Math.Min(int.MaxValue, (3L * 6 * limits.MaxTextPerPage) + (256L * (limits.MaxBlocksPerPage + limits.MaxLinesPerPage)) + (64 * 1024));
    }
}

/// <summary>
/// The worker process's entry (<c>TomeStack.ImportWorker.Host.exe</c>): read one request, report its heap limit, extract,
/// write one line per event, then <c>done</c>. It writes nothing else to stdout. Exit codes: 0 done, 2 a document-level
/// failure (reported first as an <c>error</c> line), 3 a malformed request, 4 the app that started it has exited.
/// </summary>
public static class WorkerMain
{
    public static async Task<int> RunAsync(TextReader input, TextWriter output, IOcrEngine? ocr, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        WorkerRequest? request;
        try
        {
            var line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            request = line is { Length: > 0 and <= WorkerJson.MaxRequestChars } ? JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Options) : null;
        }
        catch (JsonException)
        {
            request = null;
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Path) || request.Limits is null)
        {
            await WriteAsync(output, new("error", Code: "worker.bad-request", Message: "The worker received no valid request.")).ConfigureAwait(false);
            return 3;
        }
        if (request.ParentProcessId is { } parent)
            _ = ExitWithParentAsync(parent);

        // The app checks this against the cap it set (DOTNET_GCHeapHardLimit) before any PDF byte is parsed. The
        // collection makes the GC report its current limit rather than none.
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        await WriteAsync(output, new("worker", HeapLimit: GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)).ConfigureAwait(false);

        try
        {
            var extractor = new PdfPigExtractor(ocr, request.Limits);
            await foreach (var item in extractor.ExtractAsync(request.Path, new(request.FirstPage, request.LastPage), cancellationToken).ConfigureAwait(false))
            {
                await WriteAsync(output, item switch
                {
                    DocumentOpened opened => new WorkerMessage("document", PageCount: opened.PageCount),
                    ExtractedPage page => new WorkerMessage("page", Page: page),
                    _ => throw new InvalidOperationException("Unknown extraction event."),
                }).ConfigureAwait(false);
            }
        }
        catch (ExtractionException ex)
        {
            await WriteAsync(output, new("error", Code: ex.Code, Message: ex.Message)).ConfigureAwait(false);
            return 2;
        }
        await WriteAsync(output, new("done")).ConfigureAwait(false);
        return 0;
    }

    private static async Task ExitWithParentAsync(int parentProcessId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            await parent.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Already gone: stop as well.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return; // not observable; the app's own watchdog and timeouts still bound the run
        }
        Environment.Exit(4);
    }

    private static async Task WriteAsync(TextWriter output, WorkerMessage message)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(message, WorkerJson.Options)).ConfigureAwait(false);
        await output.FlushAsync().ConfigureAwait(false);
    }
}
