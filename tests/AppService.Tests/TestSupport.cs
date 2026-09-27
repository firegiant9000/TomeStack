using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A throwaway data directory per test; deleted on dispose.</summary>
internal sealed class TempApp : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));

    // No sync roots: tests must not depend on this machine's OneDrive or registry (DataFolderTests covers discovery).
    public TempApp() => App = TomeStackApp.Open(_directory, new FixedTime(Now), syncRoots: [], devFixtures: true);

    public TomeStackApp App { get; private set; }

    public string Directory => _directory;

    public void Reopen()
    {
        App.Dispose();
        App = TomeStackApp.Open(_directory, new FixedTime(Now), syncRoots: [], devFixtures: true);
    }

    public void Dispose()
    {
        App.Dispose();
        try
        {
            // Managed PDF copies are read-only (ADR-005); clear that so the folder can be deleted.
            foreach (var file in System.IO.Directory.Exists(_directory) ? System.IO.Directory.GetFiles(_directory, "*", SearchOption.AllDirectories) : [])
                File.SetAttributes(file, FileAttributes.Normal);
            System.IO.Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort on Windows file locks */ }
    }

    /// <summary>Installs a fixture pack (sources and revisions) into this data folder, as if it had been imported.</summary>
    public ContentPack AddPack(string relativePath)
    {
        var pack = LoadFixture<ContentPack>(relativePath);
        App.Store.InTransaction(() =>
        {
            foreach (var source in pack.Sources)
                App.Store.UpsertSource(source);
            foreach (var revision in pack.Revisions)
                App.Store.AddRevision(revision);
        });
        return pack;
    }

    public static T LoadFixture<T>(string relativePath) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", relativePath)), RulesJson.Options)!;

    public static string Json<T>(T value) => JsonSerializer.Serialize(value, RulesJson.Options);
}
