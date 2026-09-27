using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// ADR-005 (D02): the data folder should not live inside a cloud sync root. A sync client racing SQLite's WAL files
/// can corrupt the database or create conflicting copies. TomeStack warns rather than refusing, because the user
/// chose the folder deliberately (<c>--data-dir</c> or <c>TOMESTACK_DATA_DIR</c>).
/// </summary>
public static class DataFolder
{
    public const string SyncRootWarningCode = "data-dir.sync-root";

    /// <summary>The sync root that contains <paramref name="dataDirectory"/> (or equals it), or null.</summary>
    public static string? FindSyncRoot(string dataDirectory, IEnumerable<string> syncRoots)
    {
        ArgumentNullException.ThrowIfNull(syncRoots);
        var folder = Normalize(dataDirectory);
        return syncRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .FirstOrDefault(root =>
            {
                var normalized = Normalize(root);
                return folder.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                    || folder.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>The warning shown in <c>app.info</c>. It names the sync root's folder name only, not the full path.</summary>
    public static Diagnostic? SyncRootWarning(string dataDirectory, IEnumerable<string> syncRoots) =>
        FindSyncRoot(dataDirectory, syncRoots) is { } root
            ? new(
                SyncRootWarningCode,
                $"Your TomeStack data folder is inside a synced folder ('{Path.GetFileName(Normalize(root))}'). Sync can corrupt the database or create conflicting copies. "
                + "Move it to a folder that is not synced (the default is %LOCALAPPDATA%\\TomeStack), or pause sync while TomeStack is open. Use exports for backups.")
            : null;

    /// <summary>
    /// Known sync roots on this machine: the OneDrive environment variables, OneDrive account folders, and every root
    /// registered with the Windows cloud files API (<c>SyncRootManager</c>; OneDrive, Dropbox, Google Drive and others).
    /// </summary>
    public static IReadOnlyList<string> DiscoverSyncRoots()
    {
        var roots = new List<string>();
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value)
                roots.Add(value);
        }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using (var accounts = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts"))
                {
                    foreach (var name in accounts?.GetSubKeyNames() ?? [])
                    {
                        using var account = accounts!.OpenSubKey(name);
                        if (account?.GetValue("UserFolder") is string folder && folder.Length > 0)
                            roots.Add(folder);
                    }
                }
                using var manager = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager");
                foreach (var name in manager?.GetSubKeyNames() ?? [])
                {
                    using var userRoots = manager!.OpenSubKey($@"{name}\UserSyncRoots");
                    foreach (var valueName in userRoots?.GetValueNames() ?? [])
                    {
                        if (userRoots!.GetValue(valueName) is string folder && folder.Length > 0)
                            roots.Add(folder);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Best effort: a warning we cannot compute is not a reason to fail startup.
            }
        }
        return [.. roots.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
