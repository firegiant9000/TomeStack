using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TomeStack.ImportWorker.Extraction;

/// <summary>
/// ADR-009 (c): one run of <c>TomeStack.ImportWorker.Host.exe</c> as a child process, shared by
/// <see cref="WorkerProcessExtractor"/> and <see cref="Forms.WorkerFormReader"/>. It starts the child under its heap cap,
/// sends the request on stdin, and reads JSON lines of bounded length from stdout, each within a timeout and the whole
/// run within <see cref="ExtractionLimits.RunTimeout"/>. A memory watchdog kills the child above
/// <see cref="ExtractionLimits.MaxWorkingSet"/>. Disposing kills the child's whole process tree. Nothing the child writes to
/// stderr is kept or logged. What a message means is the caller's protocol; the session only reads and parses lines.
/// </summary>
internal sealed class WorkerSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ExtractionLimits _limits;
    private readonly CancellationTokenSource _run;
    private readonly Stopped _stopped = new();
    private readonly BoundedLineReader _stdout;
    private readonly Task _watchdog;
    private readonly Task _stderr;

    private WorkerSession(Process process, ExtractionLimits limits, int maxLineChars, CancellationToken cancellationToken)
    {
        _process = process;
        _limits = limits;
        _run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _run.CancelAfter(limits.RunTimeout);
        _watchdog = WatchAsync(_run.Token);
        _stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        _stdout = new BoundedLineReader(process.StandardOutput, maxLineChars);
    }

    /// <summary>How often the watchdog reads the child's memory.</summary>
    public static TimeSpan WatchInterval { get; } = TimeSpan.FromMilliseconds(200);

    public int ProcessId => _process.Id;

    /// <summary>Starts the child. <paramref name="maxLineChars"/> bounds every line read from it (<c>worker.message-too-large</c>).</summary>
    public static WorkerSession Start(string workerPath, ExtractionLimits limits, int maxLineChars, CancellationToken cancellationToken)
    {
        if (!File.Exists(workerPath))
            throw new ExtractionException("worker.missing", "The import worker is missing from the TomeStack folder. Reinstall TomeStack.");
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
        start.Environment["DOTNET_GCHeapHardLimit"] = limits.HeapHardLimit.ToString("X", CultureInfo.InvariantCulture);
        var process = Process.Start(start) ?? throw new ExtractionException("worker.crashed", "The import worker did not start.");
        return new WorkerSession(process, limits, maxLineChars, cancellationToken);
    }

    /// <summary>Writes the one request line and closes stdin.</summary>
    public async Task SendAsync(WorkerRequest request)
    {
        try
        {
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, WorkerJson.Options)).ConfigureAwait(false);
            _process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child is already gone (killed by the watchdog, or crashed); reading stdout reports why.
        }
    }

    /// <summary>
    /// Reads the child's first line, which must report a managed-heap limit inside the cap the app set, before the child
    /// parses anything (<c>worker.limits</c> otherwise). An <c>error</c> line becomes its code; with
    /// <paramref name="knownError"/>, only a code it knows, with the text it gives (never the child's), and any other is
    /// <c>worker.protocol</c>. Returns the reported limit.
    /// </summary>
    public async Task<long> ReadHelloAsync(CancellationToken cancellationToken, Func<string, string?>? knownError = null)
    {
        var hello = await NextAsync(_limits.PageTimeout, cancellationToken).ConfigureAwait(false);
        if (hello.Type == "error" && knownError is not null)
            throw hello.Code is { } code && knownError(code) is { } text ? new ExtractionException(code, text) : Fail("worker.protocol", "The import worker sent an unexpected message.");
        if (hello.Type == "error")
            throw new ExtractionException(hello.Code ?? "worker.failed", hello.Message ?? "The import worker failed.");
        if (hello.Type != "worker" || hello.HeapLimit is not { } heap || heap <= 0 || heap > _limits.HeapHardLimit)
            throw Fail("worker.limits", "The import worker did not start with its memory limit, so it was stopped.");
        return heap;
    }

    /// <summary>
    /// The next message, which must arrive within <paramref name="timeout"/> (<c>worker.page-timeout</c>) and the run's
    /// timeout (<c>worker.timeout</c>). A line past the length limit, a malformed one, or the end of the output fails the run.
    /// </summary>
    public async Task<WorkerMessage> NextAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var line = CancellationTokenSource.CreateLinkedTokenSource(_run.Token);
        line.CancelAfter(timeout);
        string? text;
        try
        {
            text = await _stdout.ReadLineAsync(line.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Fail(_stopped.Reason ?? (_run.IsCancellationRequested ? "worker.timeout" : "worker.page-timeout"),
                "The import worker took too long and was stopped.");
        }
        catch (InvalidDataException)
        {
            throw Fail("worker.message-too-large", "The import worker sent more than a page can hold, so it was stopped.");
        }
        if (text is null)
        {
            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw _stopped.Reason is { } reason
                ? Fail(reason, "The import worker used too much memory and was stopped.")
                : Fail("worker.crashed", $"The import worker stopped unexpectedly (exit code {_process.ExitCode}).");
        }
        try
        {
            return JsonSerializer.Deserialize<WorkerMessage>(text, WorkerJson.Options) ?? throw Fail("worker.protocol", "The import worker sent an empty message.");
        }
        catch (JsonException)
        {
            throw Fail("worker.protocol", "The import worker sent a malformed message.");
        }
    }

    /// <summary>Kills the child and returns the failure to throw.</summary>
    public ExtractionException Fail(string code, string message)
    {
        Kill();
        return new ExtractionException(code, message);
    }

    public async ValueTask DisposeAsync()
    {
        Kill();
        await _run.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_watchdog, _stderr).ConfigureAwait(false);
        _run.Dispose();
        _process.Dispose();
    }

    /// <summary>Why the parent stopped the child, when it did.</summary>
    private sealed class Stopped
    {
        public volatile string? Reason;
    }

    /// <summary>
    /// The memory watchdog: above the limit the child is killed and the run fails with <c>worker.memory</c>. It reads the
    /// larger of the working set and the private bytes, so memory that is committed but paged out also counts.
    /// </summary>
    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_process.HasExited)
            {
                _process.Refresh();
                if (Math.Max(_process.WorkingSet64, _process.PrivateMemorySize64) > _limits.MaxWorkingSet)
                {
                    _stopped.Reason = "worker.memory";
                    Kill();
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

    private void Kill()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
