using System.Runtime.CompilerServices;
using System.Text;

namespace TomeStack.ImportWorker.Extraction;

/// <summary>
/// ADR-009 (c): the app's side of extraction. Each run starts <c>TomeStack.ImportWorker.Host.exe</c> as a child process, sends
/// one request on stdin and reads JSON lines from stdout; no socket is opened (ADR-006). The parent enforces the limits
/// from outside the child: a page timeout (no line for <see cref="ExtractionLimits.PageTimeout"/>), a run timeout, a
/// memory watchdog, the child's managed-heap cap (<c>DOTNET_GCHeapHardLimit</c>, which the child must confirm before it
/// parses anything), and a length limit on every line it reads (<see cref="WorkerSession"/>). It trusts nothing the child
/// sends: pages must arrive in order and inside the scope. Cancelling, or stopping the enumeration early, kills the
/// child's whole process tree. Nothing the child writes to stderr is kept or logged.
/// </summary>
public sealed class WorkerProcessExtractor(string workerPath, ExtractionLimits? limits = null) : IDocumentExtractor
{
    private readonly ExtractionLimits _limits = limits ?? ExtractionLimits.Default;

    /// <summary>How often the watchdog reads the child's memory.</summary>
    public static TimeSpan WatchInterval => WorkerSession.WatchInterval;

    /// <summary>Called with the child's process id once it runs (tests check that cancelling ends it).</summary>
    public Action<int>? ProcessStarted { get; init; }

    /// <summary>Called with the managed-heap limit the child reported (tests check that the cap applies).</summary>
    public Action<long>? HeapLimitReported { get; init; }

    public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(scope);
        await using var session = WorkerSession.Start(workerPath, _limits, WorkerJson.MaxMessageChars(_limits), cancellationToken);
        ProcessStarted?.Invoke(session.ProcessId);
        await session.SendAsync(new WorkerRequest(Path.GetFullPath(path), scope.FirstPage, scope.LastPage, _limits, Environment.ProcessId)).ConfigureAwait(false);
        var heap = await session.ReadHelloAsync(cancellationToken).ConfigureAwait(false);
        HeapLimitReported?.Invoke(heap);

        // Pages must come in order, inside the scope, after the document; "done" only once they all came.
        (int First, int Last)? pages = null;
        var expected = 0;
        var finished = false;
        while (!finished)
        {
            var message = await session.NextAsync(_limits.PageTimeout, cancellationToken).ConfigureAwait(false);
            switch (message.Type)
            {
                case "document" when pages is null && message.PageCount is { } count && count >= 0:
                    pages = scope.Resolve(count);
                    expected = pages.Value.First;
                    yield return new DocumentOpened(count);
                    break;
                case "page" when message.Page is { } page && pages is { } range && page.PageNumber == expected && expected <= range.Last:
                    expected++;
                    yield return page;
                    break;
                case "error":
                    throw new ExtractionException(message.Code ?? "worker.failed", message.Message ?? "The import worker failed.");
                case "done" when pages is { } range && expected > range.Last:
                    finished = true;
                    break;
                default:
                    throw session.Fail("worker.protocol", "The import worker sent an unexpected message.");
            }
        }
    }
}

/// <summary>
/// Reads lines of at most a given length. <see cref="TextReader.ReadLineAsync(CancellationToken)"/> would hold a line of any
/// length in memory, so a child that never writes a newline could exhaust the app's.
/// </summary>
internal sealed class BoundedLineReader(TextReader reader, int maxChars)
{
    private readonly char[] _buffer = new char[64 * 1024];
    private int _start;
    private int _end;

    /// <summary>The next line without its line break, or null at the end. Throws <see cref="InvalidDataException"/> past the limit.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        while (true)
        {
            if (_start == _end)
            {
                _start = 0;
                _end = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (_end == 0)
                    return line.Length > 0 ? line.ToString() : null;
            }
            var newline = Array.IndexOf(_buffer, '\n', _start, _end - _start);
            var stop = newline < 0 ? _end : newline;
            if (line.Length + (stop - _start) > maxChars)
                throw new InvalidDataException("The line is longer than the limit.");
            line.Append(_buffer, _start, stop - _start);
            if (newline < 0)
            {
                _start = _end;
                continue;
            }
            _start = newline + 1;
            if (line.Length > 0 && line[^1] == '\r')
                line.Length--;
            return line.ToString();
        }
    }
}
