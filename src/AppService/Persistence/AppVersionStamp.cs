using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TomeStack.AppService.Persistence;

/// <summary>
/// R75, R76: the one-time copy of the database a newer build takes the first time it opens a data folder an older build used.
/// A stamp file in the data folder (<see cref="FileName"/>, one line: the version that last opened it) says which. When the database
/// exists and the stamp is missing or older, the copy <c>tomestack.db.app-&lt;from&gt;-to-&lt;to&gt;.bak</c> is made before anything
/// writes (before <c>BackupBeforeUpgrade</c> and the migrations), so the player can go back to the build they came from. Copies are
/// never pruned and may be deleted by hand.
/// </summary>
/// <remarks>
/// <c>&lt;from&gt;</c> is the version being left: the stamp's. A folder with no stamp (0.5.0 and older wrote none) uses
/// <c>pre-&lt;this version&gt;</c> instead, for example <c>tomestack.db.app-pre-0.6.0-to-0.6.0.bak</c>. The copy is skipped only when that
/// exact file exists (a retried launch). A new folder is stamped and not copied; a stamp from a newer version is neither copied for nor
/// downgraded. An unreadable stamp counts as missing. Versions compare numerically (major.minor.patch).
/// </remarks>
internal static class AppVersionStamp
{
    public const string FileName = "tomestack.app-version";

    /// <summary>The running build's version, as <see cref="TomeStackApp.GetInfo"/> reports it.</summary>
    public static string CurrentVersion => typeof(AppVersionStamp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// The copy of <paramref name="databasePath"/> taken when <paramref name="target"/> first opens a folder last opened by
    /// <paramref name="left"/> (a version, or <c>pre-</c> and one). Naming both ends (R87) means a stamp that could not be updated cannot
    /// make a later version skip its own copy.
    /// </summary>
    public static string CopyPath(string databasePath, string left, string target) => $"{databasePath}.app-{left}-to-{target}.bak";

    /// <summary>
    /// Takes the copy when due. Call it before the database opens, with the data folder locked. Uses SQLite's online backup, never a file
    /// copy: committed data can still be in the <c>-wal</c> file. A database newer than this build (refused when it opens) is not copied.
    /// </summary>
    public static void CopyBeforeOpen(string dataDirectory, string databasePath, string appVersion, int latestSchemaVersion)
    {
        if (Parse(appVersion) is not { } current || !File.Exists(databasePath))
            return;
        var stamped = Read(dataDirectory);
        if (stamped is not null && stamped >= current)
            return;
        using var probe = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        probe.Open();
        int schema;
        using (var command = probe.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version;";
            schema = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        if (schema is 0 || schema > latestSchemaVersion)
            return; // nothing stored yet, or a newer data folder that the store refuses untouched
        var copy = CopyPath(databasePath, stamped is null ? $"pre-{Format(current)}" : Format(stamped), Format(current));
        if (File.Exists(copy))
            return; // a retry of this very step after a failed launch: the first copy is from before this build wrote anything
        var partial = copy + ".tmp"; // complete or absent: a crash mid-copy must not leave a half file under the real name
        File.Delete(partial);
        using (var target = new SqliteConnection($"Data Source={partial};Pooling=False"))
            probe.BackupDatabase(target);
        File.Move(partial, copy);
    }

    /// <summary>Records <paramref name="appVersion"/> after a successful open, unless the stamp is already that version or a newer one.</summary>
    public static void Write(string dataDirectory, string appVersion)
    {
        if (Parse(appVersion) is not { } current || (Read(dataDirectory) is { } stamped && stamped >= current))
            return;
        var path = Path.Combine(dataDirectory, FileName);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, Format(current) + "\n");
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary); // nothing to delete after a successful move; a failed move leaves no stray file
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best effort; the original failure is the one that matters
            }
        }
    }

    private static Version? Read(string dataDirectory)
    {
        try
        {
            var path = Path.Combine(dataDirectory, FileName);
            if (!File.Exists(path))
                return null;
            using var reader = new StreamReader(path);
            return Parse(reader.ReadLine());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // an unreadable stamp counts as a missing one (R88)
        }
    }

    /// <summary>major.minor.patch only (a missing patch is 0, so <c>0.5</c> and <c>0.5.0</c> are the same version); null when it is not a version.</summary>
    private static Version? Parse(string? text) =>
        Version.TryParse(text?.Trim(), out var version) ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0)) : null;

    private static string Format(Version version) => version.ToString(3);
}
