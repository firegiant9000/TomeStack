using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TomeStack.AppService;

/// <summary>
/// Local-only record of unexpected failures. Details (exception type, message, stack) stay on this
/// machine; callers across the UI boundary only ever see the correlation id, except the redacted digest the user asks
/// for with <c>diagnostics.copy</c> (<see cref="Diagnostics.DiagnosticsCopy"/>: types and frames with file names, no messages or folders; ADR-006).
/// </summary>
public interface IErrorLog
{
    void Record(string correlationId, string command, Exception exception);

    /// <summary>Same entry format for a failure that has no exception to hand (the shell's crash handlers, WebView2 process failures).</summary>
    void Record(string correlationId, string command, string detail);
}

/// <summary>Appends to <c>&lt;data dir&gt;/logs/errors.log</c>, keeping one rolled-over file of at most <see cref="MaxBytes"/>.</summary>
public sealed partial class FileErrorLog(string directory, TimeProvider time) : IErrorLog
{
    public const string FileName = "errors.log";
    public const long MaxBytes = 1024 * 1024;

    private readonly Lock _gate = new();

    public string FilePath => Path.Combine(directory, FileName);

    public void Record(string correlationId, string command, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write(correlationId, command, exception.ToString());
    }

    public void Record(string correlationId, string command, string detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        Write(correlationId, command, detail);
    }

    private void Write(string correlationId, string command, string detail)
    {
        var entry = string.Create(
            CultureInfo.InvariantCulture,
            $"[{time.GetUtcNow():O}] {correlationId} command={command}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}");
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never turn a handled failure into a crash.
        }
    }

    /// <summary>Short, unguessable-enough reference the user can quote; not a secret.</summary>
    public static string NewCorrelationId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4));

    /// <summary>
    /// The newest <paramref name="maxEntries"/> entries, oldest first, from the rolled-over file and the current one. Empty
    /// when there is no log; null when it exists but cannot be read. The detail is returned as written: callers that show
    /// it redact it (<see cref="Diagnostics.DiagnosticsCopy"/>).
    /// </summary>
    public IReadOnlyList<ErrorLogEntry>? ReadRecent(int maxEntries)
    {
        if (maxEntries <= 0)
            return [];
        string text;
        try
        {
            lock (_gate)
                text = ReadIfExists(FilePath + ".1") + ReadIfExists(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return [.. Parse(text).TakeLast(maxEntries)];
    }

    private static string ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    private static List<ErrorLogEntry> Parse(string text)
    {
        var entries = new List<ErrorLogEntry>();
        (string At, string Id, string Command)? header = null;
        var detail = new List<string>();
        void Flush()
        {
            if (header is { } h)
            {
                while (detail.Count > 0 && detail[^1].Length == 0)
                    detail.RemoveAt(detail.Count - 1);
                entries.Add(new ErrorLogEntry(h.At, h.Id, h.Command, string.Join('\n', detail)));
            }
            detail.Clear();
        }
        // Write ends every entry with an empty line, so a header counts only at the start or after one: a header-shaped
        // line inside a message stays part of that entry's detail.
        var previousEmpty = true;
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            var match = previousEmpty ? HeaderLine().Match(line) : Match.Empty;
            if (match.Success)
            {
                Flush();
                header = (match.Groups["at"].Value, match.Groups["id"].Value, match.Groups["command"].Value);
            }
            else if (header is not null)
                detail.Add(line);
            previousEmpty = line.Length == 0;
        }
        Flush();
        return entries;
    }

    /// <remarks>Lower-case ids are accepted: earlier builds wrote import failures that way.</remarks>
    [GeneratedRegex(@"^\[(?<at>[^\]]+)\] (?<id>[0-9A-Fa-f]{8}) command=(?<command>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderLine();
}

/// <summary>One <c>errors.log</c> entry as written: the UTC time, the correlation id, the command and the detail lines.</summary>
public sealed record ErrorLogEntry(string At, string CorrelationId, string Command, string Detail);
