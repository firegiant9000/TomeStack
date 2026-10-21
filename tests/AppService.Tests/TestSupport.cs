using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class AppTestExtensions
{
    /// <summary>
    /// R182: saves a hand-built character as the page would, naming the version now stored (a character with no stored row is
    /// new and keeps what it carries). For tests that save one fixture again and again.
    /// </summary>
    public static CharacterView SaveCurrent(this TomeStackApp app, Character character) =>
        app.SaveCharacter(app.ListCharacters().FirstOrDefault(c => c.Id == character.Id) is { } stored ? character with { UpdatedAt = stored.UpdatedAt } : character);
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

    /// <summary>The compare-and-swap token of a candidate as stored now (what the UI would send back with its edit).</summary>
    public string? EtagOf(Guid candidateId) => App.Store.FindImportCandidate(candidateId)?.Etag;

    /// <summary>R134: accepts as the panel does, sending the etag the candidate has now (a stored one; none for an unknown id).</summary>
    public StoredCandidate Accept(CandidateAcceptRequest request) => App.AcceptCandidate(request with { Expected = EtagOf(request.CandidateId) });

    /// <summary>R134: bulk accept as the panel does, one current etag per id that exists.</summary>
    public AcceptManyResult AcceptMany(CandidateAcceptManyRequest request) =>
        App.AcceptMany(request with { Expected = request.CandidateIds.Distinct().Where(id => EtagOf(id) is not null).ToDictionary(id => id, id => EtagOf(id)!) });

    /// <summary>R134: ignores as the panel does, sending the etag the candidate has now.</summary>
    public StoredCandidate Ignore(Guid candidateId) => App.IgnoreCandidate(new(candidateId, EtagOf(candidateId)));

    /// <summary>
    /// Waits for the running import to end (30 s unless <paramref name="timeout"/> says otherwise), then fails the test unless the wait
    /// finished and the job's stored status is <paramref name="expected"/> (a test that expects any end passes <c>null</c>: it must still
    /// finish). The failure names the status, the failure code, the page counts and whether the wait timed out; never document text.
    /// </summary>
    public ImportJobRecord WaitForImport(Guid jobId, ImportJobStatus? expected = ImportJobStatus.Completed, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(30);
        var finished = true;
        string? faulted = null;
        try
        {
            if (App.RunningImport is { } task)
                finished = task.Wait(limit);
        }
        catch (AggregateException e)
        {
            faulted = e.InnerException?.GetType().Name ?? e.GetType().Name;
        }
        var job = App.ImportStatus(jobId);
        if (!finished || faulted is not null || (expected is { } want && job.Status != want))
        {
            Assert.Fail(
                $"Import job {jobId:D}: expected {(expected?.ToString() ?? "any end")}, status {job.Status}, failure code {job.FailureCode ?? "none"}, " +
                $"wait timed out: {!finished} (limit {limit.TotalSeconds:F0} s), task faulted: {faulted ?? "no"}, " +
                $"pages done {job.PagesDone} of {job.PageCount?.ToString() ?? "?"}, failed {job.PagesFailed}, next {job.NextPage?.ToString() ?? "none"}, candidates {job.Candidates}.");
        }
        return job;
    }

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
