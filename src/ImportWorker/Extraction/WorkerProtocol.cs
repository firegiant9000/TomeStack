using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.ImportWorker.Extraction;

/// <summary>ADR-009 (c): the one request line the worker reads from stdin. <paramref name="Path"/> is a file the app resolved, never one from the UI.</summary>
public sealed record WorkerRequest(string Path, int? FirstPage, int? LastPage, ExtractionLimits Limits);

/// <summary>One line the worker writes to stdout: <c>document</c>, <c>page</c>, <c>error</c> (with a code) or <c>done</c>.</summary>
public sealed record WorkerMessage(string Type, int? PageCount = null, ExtractedPage? Page = null, string? Code = null, string? Message = null);

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
}

/// <summary>
/// The worker process's entry (<c>TomeStack.ImportWorker.Host.exe</c>): read one request, extract, write one line per event,
/// then <c>done</c>. It writes nothing else to stdout. Exit codes: 0 done, 2 a document-level failure (reported first as
/// an <c>error</c> line), 3 a malformed request.
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

    private static async Task WriteAsync(TextWriter output, WorkerMessage message)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(message, WorkerJson.Options)).ConfigureAwait(false);
        await output.FlushAsync().ConfigureAwait(false);
    }
}
