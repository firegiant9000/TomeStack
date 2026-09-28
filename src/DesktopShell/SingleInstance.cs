using System.IO;
using System.Text.Json;
using TomeStack.AppService;

namespace TomeStack.DesktopShell;

/// <summary>
/// M2.1: the data folder lock (<see cref="DataFolderLock"/>) keeps a second process out; this lets that second launch
/// bring the running window forward instead. The first instance waits on a named event keyed on the data folder
/// (<see cref="DataFolder.InstanceKey"/>, a hash: no path in the name), and the second sets it. Session-local, so it
/// never reaches another user's session.
/// </summary>
internal static class SingleInstance
{
    /// <summary>Exit code of a launch that found the folder in use (the smoke check expects it).</summary>
    public const int InUseExitCode = 3;

    private static string EventName(string dataDirectory) => $@"Local\TomeStack-show-{DataFolder.InstanceKey(dataDirectory)}";

    /// <summary>Signals the running instance. False when there is none to signal (it is still starting, or not TomeStack).</summary>
    public static bool ActivateExisting(string dataDirectory)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(EventName(dataDirectory), out var existing))
                return false;
            using (existing)
                return existing.Set();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    /// <summary>Calls <paramref name="show"/> (on a pool thread) each time a later launch signals this instance.</summary>
    public static IDisposable ListenForActivation(string dataDirectory, Action show)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName(dataDirectory));
        var registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);
        return new Listener(signal, registration);
    }

    /// <summary>The smoke report of a launch that found the folder in use; <c>detail</c> is <c>data-folder-in-use</c>.</summary>
    public static void WriteSmokeReport(ShellOptions options, string dataDirectory, bool activatedExisting)
    {
        var report = JsonSerializer.Serialize(new
        {
            success = false,
            detail = "data-folder-in-use",
            activatedExisting,
            blockedRequests = Array.Empty<string>(),
            dataDirectory,
        });
        File.WriteAllText(options.SmokeReport ?? Path.Combine(Path.GetTempPath(), "tomestack-smoke-in-use.json"), report);
    }

    private sealed class Listener(EventWaitHandle signal, RegisteredWaitHandle registration) : IDisposable
    {
        public void Dispose()
        {
            registration.Unregister(null);
            signal.Dispose();
        }
    }
}
