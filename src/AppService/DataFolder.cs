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

    /// <summary>
    /// A stable key for one data folder (case-insensitive full path, hashed so no path appears in an OS object name).
    /// The shell names its "show the window" event with it, so a second launch can find the first (M2.1).
    /// </summary>
    public static string InstanceKey(string dataDirectory) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Normalize(dataDirectory).ToUpperInvariant())))[..32];

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

/// <summary>
/// M2.1: one TomeStack process per data folder. Two processes on one folder race each other: the second marks the
/// first's running import as interrupted (so a resume starts a second worker on the same job) and its startup cleanup
/// deletes the first's half-written PDF copy, or a new managed PDF before its record is saved. The lock is
/// <c>tomestack.lock</c> held open without sharing for the app's lifetime. Windows releases it when the process ends,
/// even after a crash, so a stale lock cannot keep the folder closed.
/// </summary>
public sealed class DataFolderLock : IDisposable
{
    public const string FileName = "tomestack.lock";

    private readonly FileStream _file;

    private DataFolderLock(FileStream file) => _file = file;

    /// <exception cref="DataFolderInUseException">Another process holds the folder.</exception>
    public static DataFolderLock Acquire(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        try
        {
            return new(new FileStream(Path.Combine(dataDirectory, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33) // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION
        {
            throw new DataFolderInUseException(ex);
        }
    }

    public void Dispose() => _file.Dispose();
}

/// <summary>Another TomeStack (or DevHost) has this data folder open. Nothing was read or changed.</summary>
public sealed class DataFolderInUseException(Exception inner)
    : IOException("TomeStack is already open with this data folder. Switch to that window, or close it first.", inner);
