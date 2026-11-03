using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.AppService.Persistence;
using TomeStack.ImportWorker;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Import Lab (L3, design 10): a page turn of the candidate list allocates the same however many candidates the job holds, and
/// still returns what the in-memory filter and order returned (page, then name ignoring case, ties in storage order). Invented rows only.
/// </summary>
public class CandidatePagingTests
{
    private static readonly Guid Job = Guid.Parse("00000000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherJob = Guid.Parse("00000000-0000-4000-8000-0000000000b2");

    private static SqliteStore NewStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new SqliteStore(Path.Combine(directory, TomeStackApp.DatabaseFileName));
    }

    private static StoredCandidate Stored(Guid job, int n, int page, int? end, ContentKind kind, string name, double confidence, CandidateStatus status = CandidateStatus.Pending, string? editedName = null)
    {
        var id = new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 2]);
        var draft = new DraftCandidate(id, Guid.Empty, new PageRef(page, end), "Fixture excerpt", kind, name, [], [], confidence, []);
        return new StoredCandidate { Id = id, JobId = job, Candidate = draft, Edited = editedName is null ? null : draft with { ProposedName = editedName }, Status = status, UpdatedAt = TempApp.Now };
    }

    private static long Allocated(SqliteStore store, int offset)
    {
        for (var i = 0; i < 5; i++)
            store.QueryImportCandidates(Job, new(), offset, 50);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++)
            store.QueryImportCandidates(Job, new(), offset, 50);
        return (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
    }

    [Fact]
    public void A_page_turn_allocates_the_same_for_a_small_and_a_large_job()
    {
        using var small = NewStore();
        using var large = NewStore();
        foreach (var (store, count) in new[] { (small, 300), (large, 3000) })
        {
            store.InTransaction(() =>
            {
                for (var i = 0; i < count; i++)
                    store.SaveImportCandidate(Stored(Job, i, 1 + i / 4, null, ContentKind.Feature, $"Fixture Candidate {i:D4}", 0.5));
            });
        }

        var few = Allocated(small, offset: 100);
        var many = Allocated(large, offset: 100);

        // Ten times the candidates; the same page. A little slack for the page's own rows landing on different SQLite pages.
        Assert.True(many <= few * 1.2 + 4_096, $"A page turn allocated {few:N0} bytes over 300 candidates and {many:N0} over 3,000.");
    }

    public sealed record Case(IReadOnlyList<(int Page, int? End, ContentKind Kind, string Name, double Confidence, CandidateStatus Status, bool Edited)> Rows, ImportCandidateFilter Filter, int Offset, int? Limit)
    {
        public override string ToString() => $"{Rows.Count} rows, {Filter}, offset {Offset}, limit {Limit}";
    }

    private static readonly string[] Names = ["Fixture ember", "Fixture Ember", "fixture frost", "Fixture Éclair", "Fixture Eclair", "Fixture Zephyr", "Fixture ward"];

    private static Gen<Case> Cases { get; } =
        from rows in (from page in Gen.Choose(1, 6)
                      from span in Gen.Elements<int?>(null, 0, 1, 2)
                      from kind in Gen.Elements(ContentKind.Feature, ContentKind.Spell, ContentKind.Class)
                      from name in Gen.Elements(Names)
                      from confidence in Gen.Elements(0.3, 0.5, 0.7, 0.9)
                      from status in Gen.Elements(CandidateStatus.Pending, CandidateStatus.Pending, CandidateStatus.Accepted, CandidateStatus.Ignored)
                      from edited in Gen.Elements(true, false)
                      select (page, span is { } s ? page + s : (int?)null, kind, name, confidence, status, edited)).ArrayOf().Select(r => r.Take(40).ToArray())
        from first in Gen.Elements<int?>(null, 2, 4)
        from last in Gen.Elements<int?>(null, 3, 5)
        from kind in Gen.Elements<ContentKind?>(null, ContentKind.Feature, ContentKind.Spell)
        from min in Gen.Elements<double?>(null, 0.5)
        from max in Gen.Elements<double?>(null, 0.7)
        from status in Gen.Elements<CandidateStatus?>(null, CandidateStatus.Pending)
        from offset in Gen.Choose(0, 45)
        from limit in Gen.Elements<int?>(null, 1, 5, 17, 50)
        select new Case(rows, new(first, last, kind, min, max, status), offset, limit);

    /// <summary>The query this task replaced, as the oracle: filter in memory, order by page then name ignoring case (stable), skip and take.</summary>
    private static (List<Guid> Ids, int Total) InMemory(IReadOnlyList<StoredCandidate> all, ImportCandidateFilter f, int offset, int? limit)
    {
        var matching = all.Where(c =>
        {
            var d = c.Current;
            return (f.Status is null || c.Status == f.Status) && (f.Kind is null || d.ProposedKind == f.Kind)
                && (f.FirstPage is null || (d.Page.End ?? d.Page.Start) >= f.FirstPage) && (f.LastPage is null || d.Page.Start <= f.LastPage)
                && (f.MinConfidence is null || d.Confidence >= f.MinConfidence) && (f.MaxConfidence is null || d.Confidence <= f.MaxConfidence);
        }).OrderBy(c => c.Current.Page.Start).ThenBy(c => c.Current.ProposedName, StringComparer.CurrentCultureIgnoreCase).ToList();
        return ([.. matching.Skip(offset).Take(limit ?? int.MaxValue).Select(c => c.Id)], matching.Count);
    }

    [Property(MaxTest = 60)]
    public Property A_page_turn_returns_what_the_in_memory_filter_and_order_returned() =>
        Prop.ForAll(Cases.ToArbitrary(), spec =>
        {
            using var store = NewStore();
            var rows = spec.Rows.Select((r, i) => Stored(Job, i, r.Page, r.End, r.Kind, r.Name, r.Confidence, r.Status, r.Edited ? "Fixture Edited " + r.Name : null)).ToList();
            store.InTransaction(() =>
            {
                foreach (var row in rows)
                    store.SaveImportCandidate(row);
                store.SaveImportCandidate(Stored(OtherJob, 999, 3, null, ContentKind.Feature, "Fixture Other Job", 0.6)); // another job's row never shows
            });

            var (items, total) = store.QueryImportCandidates(Job, spec.Filter, spec.Offset, spec.Limit);
            var (expected, expectedTotal) = InMemory(rows, spec.Filter, spec.Offset, spec.Limit);

            return total == expectedTotal && items.Select(i => i.Id).SequenceEqual(expected);
        });
}
