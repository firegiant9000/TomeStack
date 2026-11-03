using System.Text.RegularExpressions;
using TomeStack.AppService.Persistence;

namespace TomeStack.AppService;

/// <summary>
/// The advice at the end of every "made by a newer TomeStack" refusal (a package format or entry schema, a data folder's database): it
/// names the version that made the file and this one, and the version to install. The version comes from untrusted input (a package
/// manifest's <c>appVersion</c>, the data folder's stamp), so it is named only when it is a plain major.minor.patch newer than this
/// build; otherwise the advice is the older generic one.
/// </summary>
internal static partial class NewerVersionAdvice
{
    /// <summary>"It was made by TomeStack 0.8.0; this is TomeStack 0.7.1. Update TomeStack to 0.8.0 or newer to import it.", else <paramref name="otherwise"/>.</summary>
    public static string ForPackage(string? madeBy, string otherwise = "Update TomeStack to import it.") =>
        Newer(madeBy) is { } version
            ? $"It was made by TomeStack {version}; this is TomeStack {AppVersionStamp.CurrentVersion}. Update TomeStack to {version} or newer to import it."
            : otherwise;

    /// <summary>"TomeStack 0.8.0 last opened it; this is TomeStack 0.7.1. Update TomeStack to 0.8.0 or newer to open it."</summary>
    public static string ForDataFolder(string? lastOpenedBy) =>
        Newer(lastOpenedBy) is { } version
            ? $"TomeStack {version} last opened it; this is TomeStack {AppVersionStamp.CurrentVersion}. Update TomeStack to {version} or newer to open it."
            : "Update TomeStack to open it.";

    private static string? Newer(string? text)
    {
        if (text is null || !PlainVersion().IsMatch(text) || !Version.TryParse(text, out var version)
            || !Version.TryParse(AppVersionStamp.CurrentVersion, out var current) || version <= current)
        {
            return null;
        }
        return version.ToString(3);
    }

    [GeneratedRegex(@"^[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}$")]
    private static partial Regex PlainVersion();
}
