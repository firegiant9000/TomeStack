using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Detection;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Import Lab (L3): an edit of a candidate asks "is this reference an installed name?" for each unresolved reference. It used to
/// read the whole library each time; it now builds one <see cref="InstalledNames"/> set per edit. Invented names only.
/// </summary>
public class InstalledNamesTests
{
    private static readonly string[] NamePool =
    [
        "Fixture Ember", "fixture  ember", "FIXTURE EMBER", "Fixture Frost", "Fixture Ward’s Edge", "Fixture Ward's Edge", "  Fixture   Frost ", "Fixture Other",
    ];

    private static readonly string[] FamilyPool = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    public sealed record RevisionSpec(string Name, RevisionStatus Status, IReadOnlyList<string> Families)
    {
        public override string ToString() => $"{Name} {Status} [{string.Join(",", Families)}]";
    }

    public sealed record Library(IReadOnlyList<RevisionSpec> Revisions, IReadOnlyList<string> Families, string Name)
    {
        public override string ToString() => $"asking {Name} for [{string.Join(",", Families)}] over {Revisions.Count}: {string.Join("; ", Revisions)}";
    }

    private static Gen<IReadOnlyList<string>> FamilySets { get; } =
        Gen.Elements<IReadOnlyList<string>>([], [FamilyPool[0]], [FamilyPool[1]], [.. FamilyPool]);

    private static Gen<Library> Libraries { get; } =
        from revisions in (from name in Gen.Elements(NamePool)
                           from status in Gen.Elements(RevisionStatus.Draft, RevisionStatus.Published)
                           from families in FamilySets
                           select new RevisionSpec(name, status, families)).ArrayOf().Select(r => r.Take(8).ToArray())
        from families in FamilySets
        from name in Gen.Elements(NamePool.Append("Fixture Absent").ToArray())
        select new Library(revisions, families, name);

    private static ContentRevision Revision(RevisionSpec spec, int index) => new()
    {
        ContentId = new Guid(index + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]),
        RevisionId = Guid.Empty,
        Kind = ContentKind.Feature,
        Name = spec.Name,
        RulesFamilies = spec.Families,
        Provenance = new(Guid.Empty),
        Status = spec.Status,
    };

    /// <summary>The per-name query this task deleted from the edit path, kept here as the oracle.</summary>
    private static bool PerNameQuery(IReadOnlyList<ContentRevision> library, string name, IReadOnlyList<string> families) =>
        library.Any(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Any(families.Contains) && CandidateDetector.Key(r.Name) == CandidateDetector.Key(name));

    [Property(MaxTest = 200)]
    public Property Same_answers_as_the_per_name_query() =>
        Prop.ForAll(Libraries.ToArbitrary(), spec =>
        {
            var library = spec.Revisions.Select(Revision).ToList();
            var names = InstalledNames.Build(library, spec.Families);
            return NamePool.Append("Fixture Absent").All(name => names.Contains(name) == PerNameQuery(library, name, spec.Families))
                && names.Contains(spec.Name) == PerNameQuery(library, spec.Name, spec.Families);
        });

    [Fact]
    public void An_edit_reads_the_library_once()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Edit Book", [RulesFamilies.Srd521]));
        var job = new ImportJobRecord { Id = new Guid(7, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), SourceId = source.Id, Sha256 = new string('a', 64) };
        temp.App.Store.SaveImportJob(job);
        var installed = temp.App.Store.ListRevisions().First(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Contains(RulesFamilies.Srd521));
        // Twenty references that name nothing installed, and one (in other letters) that names an installed feature.
        string[] references = [.. Enumerable.Range(1, 20).Select(i => $"Fixture Missing {i:D2}"), installed.Name.ToUpperInvariant()];
        var id = new Guid(8, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);
        var draft = new DraftCandidate(id, source.Id, new PageRef(1), "Fixture excerpt", ContentKind.Feature, "Fixture Edited Feature", [RulesFamilies.Srd521], [], 0.9, [])
        {
            UnresolvedReferences = references,
        };
        temp.App.Store.SaveImportCandidate(new StoredCandidate { Id = id, JobId = job.Id, Candidate = draft, UpdatedAt = TempApp.Now });
        var etag = temp.App.Store.FindImportCandidate(id)!.Etag;

        var before = temp.App.Store.LibraryReads;
        var saved = temp.App.EditCandidate(new(id, Summary: "Fixture words.", Expected: etag));

        Assert.Equal(1, temp.App.Store.LibraryReads - before);
        Assert.Equal(20, saved.Current.UnresolvedReferences.Count); // the installed name is resolved, the twenty others stay
        Assert.DoesNotContain(saved.Current.UnresolvedReferences, r => r.Equals(installed.Name, StringComparison.OrdinalIgnoreCase));
    }
}
