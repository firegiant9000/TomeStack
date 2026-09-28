using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace TomeStack.ImportWorker.Extraction;

/// <summary>
/// ADR-009 (c): the app's side of extraction. Each run starts <c>TomeStack.ImportWorker.Host.exe</c> as a child process, sends
/// one request on stdin and reads JSON lines from stdout; no socket is opened (ADR-006). The parent enforces the limits
/// from outside the child: a page timeout (no line for <see cref="ExtractionLimits.PageTimeout"/>), a run timeout, a
/// memory watchdog, the child's managed-heap cap (<c>DOTNET_GCHeapHardLimit</c>, which the child must confirm before it
/// parses anything), and a length limit on every line it reads. It trusts nothing the child sends: pages must arrive in
/// order and inside the scope. Cancelling, or stopping the enumeration early, kills the child's whole process tree.
/// Nothing the child writes to stderr is kept or logged.
/// </summary>
public sealed class WorkerProcessExtractor(string workerPath, ExtractionLimits? limits = null) : IDocumentExtractor
{
    private readonly ExtractionLimits _limits = limits ?? ExtractionLimits.Default;

    /// <summary>How often the watchdog reads the child's memory.</summary>
    public static TimeSpan WatchInterval { get; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Called with the child's process id once it runs (tests check that cancelling ends it).</summary>
    public Action<int>? ProcessStarted { get; init; }

    /// <summary>Called with the managed-heap limit the child reported (tests check that the cap applies).</summary>
    public Action<long>? HeapLimitReported { get; init; }

    public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(scope);
        if (!File.Exists(workerPath))
            throw new ExtractionException("worker.missing", "The import worker is missing from the TomeStack folder. Reinstall TomeStack.");

        using var process = Start();
        ProcessStarted?.Invoke(process.Id);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        run.CancelAfter(_limits.RunTimeout);
        var stopped = new Stopped();
        var watchdog = WatchAsync(process, stopped, run.Token);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        var stdout = new BoundedLineReader(process.StandardOutput, WorkerJson.MaxMessageChars(_limits));
        try
        {
            try
            {
                var request = new WorkerRequest(Path.GetFullPath(path), scope.FirstPage, scope.LastPage, _limits, Environment.ProcessId);
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, WorkerJson.Options)).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The child is already gone (killed by the watchdog, or crashed); reading stdout reports why.
            }

            var hello = await NextAsync(process, stdout, stopped, run, cancellationToken).ConfigureAwait(false);
            if (hello.Type == "error")
                throw new ExtractionException(hello.Code ?? "worker.failed", hello.Message ?? "The import worker failed.");
            if (hello.Type != "worker" || hello.HeapLimit is not { } heap || heap <= 0 || heap > _limits.HeapHardLimit)
                throw Fail(process, "worker.limits", "The import worker did not start with its memory limit, so it was stopped.");
            HeapLimitReported?.Invoke(heap);

            // Pages must come in order, inside the scope, after the document; "done" only once they all came.
            (int First, int Last)? pages = null;
            var expected = 0;
            var finished = false;
            while (!finished)
            {
                var message = await NextAsync(process, stdout, stopped, run, cancellationToken).ConfigureAwait(false);
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
                        throw Fail(process, "worker.protocol", "The import worker sent an unexpected message.");
                }
            }
        }
        finally
        {
            Kill(process);
            run.Cancel();
            await Task.WhenAll(watchdog, stderr).ConfigureAwait(false);
        }
    }

    /// <summary>Why the parent stopped the child, when it did.</summary>
    private sealed class Stopped
    {
        public volatile string? Reason;
    }

    private Process Start()
    {
        var start = new ProcessStartInfo(workerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(workerPath)!,
        };
        start.Environment["DOTNET_GCHeapHardLimit"] = _limits.HeapHardLimit.ToString("X", CultureInfo.InvariantCulture);
        return Process.Start(start) ?? throw new ExtractionException("worker.crashed", "The import worker did not start.");
    }

    private async Task<WorkerMessage> NextAsync(Process process, BoundedLineReader stdout, Stopped stopped, CancellationTokenSource run, CancellationToken cancellationToken)
    {
        using var page = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        page.CancelAfter(_limits.PageTimeout);
        string? line;
        try
        {
            line = await stdout.ReadLineAsync(page.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Fail(process, stopped.Reason ?? (run.IsCancellationRequested ? "worker.timeout" : "worker.page-timeout"),
                "The import worker took too long and was stopped.");
        }
        catch (InvalidDataException)
        {
            throw Fail(process, "worker.message-too-large", "The import worker sent more than a page can hold, so it was stopped.");
        }
        if (line is null)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw stopped.Reason is { } reason
                ? Fail(process, reason, "The import worker used too much memory and was stopped.")
                : Fail(process, "worker.crashed", $"The import worker stopped unexpectedly (exit code {process.ExitCode}).");
        }
        try
        {
            return JsonSerializer.Deserialize<WorkerMessage>(line, WorkerJson.Options) ?? throw Fail(process, "worker.protocol", "The import worker sent an empty message.");
        }
        catch (JsonException)
        {
            throw Fail(process, "worker.protocol", "The import worker sent a malformed message.");
        }
    }

    /// <summary>
    /// The memory watchdog: above the limit the child is killed and the run fails with <c>worker.memory</c>. It reads the
    /// larger of the working set and the private bytes, so memory that is committed but paged out also counts.
    /// </summary>
    private async Task WatchAsync(Process process, Stopped stopped, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                process.Refresh();
                if (Math.Max(process.WorkingSet64, process.PrivateMemorySize64) > _limits.MaxWorkingSet)
                {
                    stopped.Reason = "worker.memory";
                    Kill(process);
                    return;
                }
                await Task.Delay(WatchInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the read.
        }
    }

    private static ExtractionException Fail(Process process, string code, string message)
    {
        Kill(process);
        return new ExtractionException(code, message);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
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
