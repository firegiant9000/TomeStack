using System.Globalization;
using System.Security.Cryptography;

namespace TomeStack.AppService;

/// <summary>
/// Local-only record of unexpected failures. Details (exception type, message, stack) stay on this
/// machine; callers across the UI boundary only ever see the correlation id.
/// </summary>
public interface IErrorLog
{
    void Record(string correlationId, string command, Exception exception);
}

/// <summary>Appends to <c>&lt;data dir&gt;/logs/errors.log</c>, keeping one rolled-over file of at most <see cref="MaxBytes"/>.</summary>
public sealed class FileErrorLog(string directory, TimeProvider time) : IErrorLog
{
    public const string FileName = "errors.log";
    public const long MaxBytes = 1024 * 1024;

    private readonly Lock _gate = new();

    public string FilePath => Path.Combine(directory, FileName);

    public void Record(string correlationId, string command, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var entry = string.Create(
            CultureInfo.InvariantCulture,
            $"[{time.GetUtcNow():O}] {correlationId} command={command}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
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
}
