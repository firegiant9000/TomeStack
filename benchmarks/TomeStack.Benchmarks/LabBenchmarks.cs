using BenchmarkDotNet.Attributes;
using TomeStack.AppService;
using TomeStack.AppService.Persistence;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Detection;
using TomeStack.RulesCore;

namespace TomeStack.Benchmarks;

/// <summary>
/// Import Lab (L3, design 10): library-scale costs against the real store. The library is the bundled SRD plus N original
/// published features of one homebrew source, and a job with N pending candidates. Allocations are the stable signal
/// (docs/performance.md); times vary about 40 % on the owner's machine. The targets:
/// <see cref="Coverage"/> up to 300 ms at 1000 with linear allocations, <see cref="CandidatesPage"/> up to 50 ms with allocations
/// that do not grow with N, <see cref="DetectWithLibrary"/> linear (a baseline), and <see cref="EditCandidate"/> linear in N and the
/// same for 20 references as for one.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 15)]
public class LabBenchmarks
{
    private const int DetectionPages = 50;

    private TomeStackApp _app = null!;
    private SqliteStore _reader = null!;
    private Guid _pageJob;
    private Guid _manyRefsJob;
    private Guid _oneRefJob;
    private IReadOnlyList<DetectionPage> _pages = [];
    private Guid _source;

    [Params(250, 500, 1000)]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var author = BenchmarkLibrary.Open(BenchmarkLibrary.NewFolder());
        var folder = author.DataDirectory;
        var source = author.CreateHomebrewSource(new("Fixture Lab Library", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        _source = source.Id;
        foreach (var feature in BenchmarkLibrary.Features(source.Id, N))
            BenchmarkLibrary.Publish(author, feature);
        author.Dispose();

        // Jobs and candidates are written beside the app (the store is public), then the app opens the folder again.
        _pageJob = Guid.Parse("7be0b000-0000-4000-8000-000000000001");
        _manyRefsJob = Guid.Parse("7be0b000-0000-4000-8000-000000000002");
        _oneRefJob = Guid.Parse("7be0b000-0000-4000-8000-000000000003");
        using (var store = new SqliteStore(Path.Combine(folder, TomeStackApp.DatabaseFileName)))
        {
            foreach (var job in new[] { _pageJob, _manyRefsJob, _oneRefJob })
                store.SaveImportJob(new ImportJobRecord { Id = job, SourceId = source.Id, Sha256 = new string('b', 64) });
            store.InTransaction(() =>
            {
                foreach (var candidate in BenchmarkLibrary.Candidates(_pageJob, source.Id, N))
                    store.SaveImportCandidate(candidate);
            });
            store.SaveImportCandidate(Edited(_manyRefsJob, source.Id, 20));
            store.SaveImportCandidate(Edited(_oneRefJob, source.Id, 1));
        }
        _app = BenchmarkLibrary.Open(folder);
        _reader = new SqliteStore(Path.Combine(folder, TomeStackApp.DatabaseFileName));

        // 50 pages of invented spell-shaped blocks named by index, so detection has real work to do.
        _pages = [.. Enumerable.Range(1, DetectionPages).Select(p => new DetectionPage(p, "",
            [new TextBlock($"Fixture Feature {p:D2}\nLevel 1 Evocation (Fixture Mage)\nCasting Time: Action\nRange: 60 feet\nComponents: V, S\nDuration: Instantaneous\nA fixture effect number {p}.", 50, 600, 200, 60, 10, false)],
            false))];
        var found = DetectWithLibrary();
        Console.WriteLine($"// detection fixture: {found} candidates from {DetectionPages} pages, {N} installed features");
        if (found == 0)
            throw new InvalidOperationException("The detection fixture found no candidates.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader.Dispose();
        BenchmarkLibrary.Delete(_app);
    }

    private static StoredCandidate Edited(Guid job, Guid source, int references)
    {
        var id = Guid.Parse($"7be0e000-0000-4000-8000-{references:D12}");
        return new StoredCandidate
        {
            Id = id,
            JobId = job,
            Candidate = new DraftCandidate(id, source, new PageRef(1), "Fixture excerpt.", ContentKind.Feature, "Fixture Edited Feature", [RulesFamilies.Srd51, RulesFamilies.Srd521], [], 0.9, [])
            {
                UnresolvedReferences = [.. Enumerable.Range(1, references).Select(i => $"Fixture Missing {i:D2}")],
            },
            UpdatedAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
        };
    }

    /// <summary><c>content.coverage</c> for every source: the SRD and the N features of the homebrew source. One store read, grouped in memory.</summary>
    [Benchmark]
    public int Coverage() => _app.Coverage(new CoverageRequest()).Sources.Count;

    /// <summary><c>import.candidates</c>: page 3 of 50 from N pending candidates.</summary>
    [Benchmark]
    public int CandidatesPage() => _app.ListCandidatesPage(new(_pageJob, Offset: 100, Limit: 50)).Items.Count;

    /// <summary><c>content.duplicates</c> for a job: its N pending candidates against the SRD and the N features of the homebrew source (target up to 500 ms at 1000).</summary>
    [Benchmark]
    public int Duplicates() => _app.ContentDuplicates(new(JobId: _pageJob)).Count;

    /// <summary><c>content.duplicates</c> over the whole library: every published entry against every other of its kind and family.</summary>
    [Benchmark]
    public int DuplicatesLibrary() => _app.ContentDuplicates(new()).Count;

    /// <summary>What detection does with the library (<c>DetectOnce</c>): one name set over the published revisions, then 50 pages.</summary>
    [Benchmark]
    public int DetectWithLibrary()
    {
        var families = new[] { RulesFamilies.Srd51, RulesFamilies.Srd521 };
        var installed = _reader.ListRevisions()
            .Where(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Any(families.Contains))
            .Select(r => CandidateDetector.Key(r.Name))
            .ToHashSet(StringComparer.Ordinal);
        return CandidateDetector.DetectWithLimit(_pages, new(_source, families, name => installed.Contains(CandidateDetector.Key(name)))).Candidates.Count;
    }

    /// <summary><c>import.candidate.edit</c> of a candidate with 20 unresolved references, none of them installed.</summary>
    [Benchmark]
    public int EditCandidate() => Edit(_manyRefsJob);

    /// <summary>The same edit with one reference: the difference from <see cref="EditCandidate"/> is what the references cost.</summary>
    [Benchmark]
    public int EditCandidateOneReference() => Edit(_oneRefJob);

    private int Edit(Guid job)
    {
        var stored = _app.ListCandidates(new(job)).Single();
        return _app.EditCandidate(new(stored.Id, Summary: "Fixture words.", Expected: stored.Etag)).Current.UnresolvedReferences.Count;
    }
}
