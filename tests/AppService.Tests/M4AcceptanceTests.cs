using System.Text;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;
using Xunit.Abstractions;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M4 exit gate (ROADMAP): "A third-party test PDF produces reviewable candidates; no unapproved active rules". The
/// imports run in the real worker process, as in the app (ADR-009). The automated run uses the original fixture book.
/// The third-party run reads any PDF in the gitignored <c>tests/RulesFixtures/local/third-party/</c> and skips when there
/// is none. Its output is counts only (candidates by kind, accepted, ignored, active without approval), never names or
/// text, because test output is a log; the same counts go to <c>report.md</c> in that folder.
/// </summary>
public class M4AcceptanceTests(ITestOutputHelper output)
{
    private static WorkerProcessExtractor Worker() => new(Path.Combine(AppContext.BaseDirectory, TomeStackApp.WorkerFileName));

    internal static string ThirdPartyFolder
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
                directory = directory.Parent;
            return directory is null ? "" : Path.Combine(directory.FullName, "tests", "RulesFixtures", "local", "third-party");
        }
    }

    public sealed class ThirdPartyPdfFactAttribute : FactAttribute
    {
        public ThirdPartyPdfFactAttribute()
        {
            if (!Directory.Exists(ThirdPartyFolder) || Directory.GetFiles(ThirdPartyFolder, "*.pdf").Length == 0)
                Skip = "No third-party test PDF in tests/RulesFixtures/local/third-party/ (it is private and gitignored).";
        }
    }

    /// <summary>What one import gave, in counts only.</summary>
    private sealed record Outcome(Guid SourceId, int Pages, int Ocr, IReadOnlyDictionary<ContentKind, int> ByKind, int Accepted, int AcceptedAsReference, int Ignored, int Pending, int ActiveWithoutApproval, string? Failure)
    {
        /// <summary>Item 11 (ADR-016): how many classes carry each kind of proposal, and their links, in counts only.</summary>
        public string Classes { get; init; } = "";

        public string Report() =>
            $"pages {Pages} ({Ocr} by OCR); candidates {ByKind.Values.Sum()} ({string.Join(", ", ByKind.OrderBy(k => k.Key).Select(k => $"{k.Key.ToString().ToLowerInvariant()} {k.Value}"))}); " +
            $"accepted {Accepted}, accepted as reference {AcceptedAsReference}, ignored {Ignored}, pending {Pending}; active without approval: {ActiveWithoutApproval}" +
            (Classes.Length > 0 ? $"; {Classes}" : "") +
            (Failure is null ? "" : $"; failure {Failure}");
    }

    /// <summary>Counts only: of the class candidates, how many propose each part, the links they make and the uncertainties they carry.</summary>
    private static string ClassCounts(IReadOnlyList<StoredCandidate> candidates)
    {
        var classes = candidates.Where(c => c.Candidate.ProposedKind == ContentKind.Class).Select(c => (c.Candidate, Links: c.GrantsCandidates ?? [])).ToList();
        if (classes.Count == 0)
            return "";
        int With(Func<Effect, bool> part) => classes.Count(c => c.Candidate.ProposedEffects.Any(part));
        bool Grant(Effect e, string prefix) => e is GrantEffect { Grant: GrantKind.Proficiency } g && g.Target?.StartsWith(prefix, StringComparison.Ordinal) == true;
        return $"classes {classes.Count}: hit die {With(e => e is HitDieEffect)}, saves {With(e => Grant(e, "save."))}, armor {With(e => Grant(e, "armor."))}, " +
            $"weapons {With(e => Grant(e, "weapon."))}, spellcasting {With(e => e is SpellcastingEffect)}, subclass choice {With(e => e is ChoiceEffect)}; " +
            $"links {classes.Sum(c => c.Links.Select(l => l.CandidateId).Distinct().Count())}; uncertainties {classes.Sum(c => c.Candidate.Uncertainties.Count)}";
    }

    /// <summary>
    /// Imports the PDF into a user source, reviews a sample (the first candidate of each kind accepted, as reference when
    /// it is blocked; the last one ignored) and counts what is active without a person publishing it.
    /// </summary>
    private static Outcome Run(TempApp temp, byte[] pdf, string family)
    {
        var source = temp.App.CreateHomebrewSource(new("Test Acceptance Book", [family]));
        temp.App.AttachPdf(source.Id, "book.pdf", pdf);
        var before = temp.App.ListContent(family).Count(o => o.SourceId == source.Id);
        var job = temp.App.StartImport(new(source.Id, WholeDocument: true));
        // Any end is reported (a failed read is carried in the outcome's failure code), but the job must have ended.
        job = temp.WaitForImport(job.Id, expected: null, timeout: TimeSpan.FromMinutes(30));
        var candidates = job.Status == ImportJobStatus.Completed ? temp.App.ListCandidates(new(job.Id)) : [];

        foreach (var first in candidates.GroupBy(c => c.Candidate.ProposedKind).Select(g => g.First()))
        {
            var check = temp.App.CheckCandidate(first.Id);
            if (check.CanAccept && first.Candidate.ProposedKind != ContentKind.Class)
                temp.Accept(new(first.Id, Confirm: true));
            else if (check.CanAcceptAsReference) // a class needs its hit die confirmed (ADR-016); this reviewer confirms nothing
                temp.Accept(new(first.Id, AsReference: true, Confirm: true));
        }
        if (candidates.Count > 1 && temp.App.ListCandidates(new(job.Id, Status: CandidateStatus.Pending)).LastOrDefault() is { } last)
            temp.Ignore(last.Id);

        var reviewed = temp.App.ListCandidates(new(job.Id));
        // Active without approval: anything from the source that the calculator would apply although nobody published it.
        var published = temp.App.Store.ListRevisions().Count(r => r.Provenance.SourceId == source.Id && r.Status == RevisionStatus.Published);
        var active = temp.App.ListContent(family).Count(o => o.SourceId == source.Id) - before;
        return new(
            source.Id, job.PagesDone, job.PagesFromOcr,
            candidates.GroupBy(c => c.Candidate.ProposedKind).ToDictionary(g => g.Key, g => g.Count()),
            reviewed.Count(c => c.Status == CandidateStatus.Accepted), reviewed.Count(c => c.Status == CandidateStatus.AcceptedAsReference),
            reviewed.Count(c => c.Status == CandidateStatus.Ignored), reviewed.Count(c => c.Status == CandidateStatus.Pending),
            published + active, job.FailureCode) { Classes = ClassCounts(candidates) };
    }

    [Fact]
    public void The_original_fixture_book_produces_reviewable_candidates_and_no_unapproved_active_rules()
    {
        using var temp = new TempApp(Worker());
        var pdf = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf"));

        var outcome = Run(temp, pdf, RulesFamilies.Srd521);
        output.WriteLine(outcome.Report());

        Assert.Null(outcome.Failure);
        Assert.Equal(7, outcome.Pages);
        Assert.Equal(new Dictionary<ContentKind, int> { [ContentKind.Class] = 2, [ContentKind.Feat] = 1, [ContentKind.Feature] = 10, [ContentKind.Item] = 4, [ContentKind.Spell] = 2 }, outcome.ByKind);
        Assert.Equal(5, outcome.Accepted + outcome.AcceptedAsReference); // one of each kind
        Assert.Equal(1, outcome.Ignored);
        Assert.Equal(0, outcome.ActiveWithoutApproval);
        // Every accepted candidate is a draft whose effects are reference only (ADR-004).
        var drafts = temp.App.Store.ListRevisions().Where(r => r.Provenance.SourceId == outcome.SourceId).ToList();
        Assert.All(drafts, d => Assert.Equal(RevisionStatus.Draft, d.Status));
        Assert.Equal(5, drafts.Count);
        Assert.All(drafts.SelectMany(d => d.Effects), e => Assert.Equal(AutomationStatus.Reference, e.Automation));
    }

    [ThirdPartyPdfFact]
    public void A_third_party_test_PDF_produces_reviewable_candidates_and_no_unapproved_active_rules()
    {
        var report = new StringBuilder("# M4 third-party PDF run (counts only)\n\n");
        var total = 0;
        foreach (var (file, index) in Directory.GetFiles(ThirdPartyFolder, "*.pdf").Order(StringComparer.Ordinal).Select((f, i) => (f, i + 1)))
        {
            using var temp = new TempApp(Worker());
            var outcome = Run(temp, File.ReadAllBytes(file), RulesFamilies.Srd521);
            // Positions, never file names: a file name can name the book.
            var line = $"PDF {index}: {outcome.Report()}";
            output.WriteLine(line);
            report.AppendLine($"- {line}");
            total += outcome.ByKind.Values.Sum();
            Assert.Null(outcome.Failure);
            Assert.Equal(0, outcome.ActiveWithoutApproval);
        }
        File.WriteAllText(Path.Combine(ThirdPartyFolder, "report.md"), report.ToString(), Encoding.UTF8);
        Assert.True(total > 0, "The third-party PDFs produced no candidates to review.");
    }
}
