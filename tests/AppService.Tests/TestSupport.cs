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

    private readonly string _directory;

    private readonly ImportWorker.IDocumentExtractor _extractor;

    // No sync roots: tests must not depend on this machine's OneDrive or registry (DataFolderTests covers discovery).
    // Imports extract in this process with PdfPig (the worker process itself is tested in ImportWorker.Tests).
    /// <param name="folder">A folder name inside the random test folder (M6 slice 3: a data folder named after a sentinel user).</param>
    public TempApp(ImportWorker.IDocumentExtractor? extractor = null, string? folder = null)
    {
        _directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"), folder ?? "data");
        _extractor = extractor ?? new ImportWorker.Extraction.PdfPigExtractor();
        App = TomeStackApp.Open(_directory, new FixedTime(Now), syncRoots: [], devFixtures: true, extractor: _extractor);
    }

    public TomeStackApp App { get; private set; }

    public string Directory => _directory;

    public void Reopen()
    {
        App.Dispose();
        App = TomeStackApp.Open(_directory, new FixedTime(Now), syncRoots: [], devFixtures: true, extractor: _extractor);
    }

    public void Dispose()
    {
        App.Dispose();
        try
        {
            // Managed PDF copies are read-only (ADR-005); clear that so the folder can be deleted.
            var root = Path.GetDirectoryName(_directory)!; // the random folder around the data folder
            foreach (var file in System.IO.Directory.Exists(root) ? System.IO.Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [])
                File.SetAttributes(file, FileAttributes.Normal);
            System.IO.Directory.Delete(root, recursive: true);
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
