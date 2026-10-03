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

    private readonly TimeProvider _time;

    private readonly ImportWorker.Forms.IFormReader? _formReader;

    // No sync roots: tests must not depend on this machine's OneDrive or registry (DataFolderTests covers discovery).
    // Imports extract in this process with PdfPig (the worker process itself is tested in ImportWorker.Tests).
    /// <param name="folder">A folder name inside the random test folder (M6 slice 3: a data folder named after a sentinel user).</param>
    /// <param name="now">The app's clock (default <see cref="Now"/>); a later one shows a restore keeps the original times.</param>
    /// <param name="formReader">Character-sheet import: the form reader (a fake, so no worker runs).</param>
    /// <param name="time">A clock that can move (token expiry); overrides <paramref name="now"/>.</param>
    public TempApp(ImportWorker.IDocumentExtractor? extractor = null, string? folder = null, DateTimeOffset? now = null, ImportWorker.Forms.IFormReader? formReader = null, TimeProvider? time = null)
    {
        _time = time ?? new FixedTime(now ?? Now);
        _formReader = formReader;
        _directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"), folder ?? "data");
        _extractor = extractor ?? new ImportWorker.Extraction.PdfPigExtractor();
        App = TomeStackApp.Open(_directory, _time, syncRoots: [], devFixtures: true, extractor: _extractor, formReader: _formReader);
    }

    public TomeStackApp App { get; private set; }

    public string Directory => _directory;

    private bool _closed;

    public void Reopen()
    {
        if (!_closed)
            App.Dispose();
        _closed = false;
        App = TomeStackApp.Open(_directory, _time, syncRoots: [], devFixtures: true, extractor: _extractor, formReader: _formReader);
    }

    /// <summary>Closes the app and releases the folder, keeping it for inspection until <see cref="Dispose"/> (T6 drill).</summary>
    public void Close()
    {
        if (!_closed)
            App.Dispose();
        _closed = true;
    }

    public void Dispose()
    {
        if (!_closed)
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
