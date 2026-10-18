namespace TomeStack.AppService;

/// <summary>
/// Item 19b (R116, ADR-013): the one rule for a file path the import worker (or an attachment) will read: a full path on a
/// local drive letter, no network (UNC) or device path, no <c>..</c>, and the extension the caller needs. A UNC path would make
/// Windows connect to another machine, sending the user's credentials, just by being opened; a relative path would mean whatever
/// file sits in the working folder. A refusal (<see cref="Require"/>) never names the path: it can hold a user name.
/// </summary>
internal static class LocalDrivePath
{
    /// <summary>The code of a refused path.</summary>
    public const string RefusedCode = "pdf.path-not-local";

    /// <param name="extension">With its dot (<c>.pdf</c>); compared without regard to case.</param>
    public static bool IsSafe(string? path, string extension) =>
        path is { Length: > 3 and <= 32_767 }
        && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
        && Path.IsPathFullyQualified(path)
        && !path.Contains("..", StringComparison.Ordinal)
        && path.IndexOf(':', 2) < 0                      // an alternate data stream (file.txt:name.pdf) or a second drive
        && !path[3..].Split('\\', '/').Any(IsReservedDeviceName)
        && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Windows treats <c>CON</c>, <c>nul.txt</c> and <c>COM1.anything</c> as a device, in any folder, so the name before the first dot decides.</summary>
    private static bool IsReservedDeviceName(string segment)
    {
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        return DeviceNames.Contains((dot < 0 ? segment : segment[..dot]).TrimEnd(' '));
    }

    /// <summary>Refuses a path <see cref="IsSafe"/> does not allow, with a code and a message that name no path.</summary>
    public static void Require(string? path, string extension)
    {
        if (!IsSafe(path, extension))
            throw new AppValidationException([new(RefusedCode, $"TomeStack reads only a {extension.TrimStart('.').ToUpperInvariant()} file on a local drive: a network share, a device path or a relative path cannot be used. Copy the file to this computer and choose it again.")], RefusedCode);
    }
}
