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
/// working-set watchdog, and the child's managed-heap cap (<c>DOTNET_GCHeapHardLimit</c>). Cancelling, or stopping the
/// enumeration early, kills the child's whole process tree. Nothing the child writes to stderr is kept or logged.
/// </summary>
public sealed class WorkerProcessExtractor(string workerPath, ExtractionLimits? limits = null) : IDocumentExtractor
{
    private readonly ExtractionLimits _limits = limits ?? ExtractionLimits.Default;

    /// <summary>How often the watchdog reads the child's working set.</summary>
    public static TimeSpan WatchInterval { get; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Called with the child's process id once it runs (tests check that cancelling ends it).</summary>
    public Action<int>? ProcessStarted { get; init; }

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
        try
        {
            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new WorkerRequest(Path.GetFullPath(path), scope.FirstPage, scope.LastPage, _limits), WorkerJson.Options)).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The child is already gone (killed by the watchdog, or crashed); reading stdout reports why.
            }

            var finished = false;
            while (!finished)
            {
                var message = await NextAsync(process, stopped, run, cancellationToken).ConfigureAwait(false);
                switch (message.Type)
                {
                    case "document":
                        yield return new DocumentOpened(message.PageCount ?? 0);
                        break;
                    case "page" when message.Page is not null:
                        yield return message.Page;
                        break;
                    case "error":
                        throw new ExtractionException(message.Code ?? "worker.failed", message.Message ?? "The import worker failed.");
                    case "done":
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

    private async Task<WorkerMessage> NextAsync(Process process, Stopped stopped, CancellationTokenSource run, CancellationToken cancellationToken)
    {
        using var page = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        page.CancelAfter(_limits.PageTimeout);
        string? line;
        try
        {
            line = await process.StandardOutput.ReadLineAsync(page.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Fail(process, stopped.Reason ?? (run.IsCancellationRequested ? "worker.timeout" : "worker.page-timeout"),
                "The import worker took too long and was stopped.");
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

    /// <summary>The working-set watchdog: above the limit the child is killed and the run fails with <c>worker.memory</c>.</summary>
    private async Task WatchAsync(Process process, Stopped stopped, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                process.Refresh();
                if (process.WorkingSet64 > _limits.MaxWorkingSet)
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
