using System.Runtime.CompilerServices;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Task 8a fix round 1 (I1): two classes whose tables share a page can list one feature name, and each gets a stub of its own
/// (CandidateDetector.FeatureStub). A stub's identity includes its class, so reviewing one never hides, or retargets a link to, the other.
/// Invented text only.
/// </summary>
public class ImportCandidateIdentityTests
{
    /// <summary>One page: Fixture Alpha's table over Fixture Beta's, both naming "Fixture Echo".</summary>
    private sealed class TwoClassPageExtractor : IDocumentExtractor
    {
        public async IAsyncEnumerable<ExtractionEvent> ExtractAsync(string path, PageScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new DocumentOpened(1);
            yield return new ExtractedPage(1, "Original test page 1.", false, Blocks, 612, 792);
        }

        private static readonly TextBlock[] Blocks =
        [
            new("Fixture Alpha Features", 50, 700, 200, 20, 10, false),
            new("Level", 50, 680, 40, 10, 10, false), new("Features", 150, 680, 100, 10, 10, false),
            new("1st", 50, 660, 40, 10, 10, false), new("Fixture Alpha Rite", 150, 660, 100, 10, 10, false),
            new("2nd", 50, 640, 40, 10, 10, false), new("Fixture Echo", 150, 640, 100, 10, 10, false),
            new("3rd", 50, 620, 40, 10, 10, false), new("Fixture Alpha Vigil", 150, 620, 100, 10, 10, false),
            new("Fixture Beta Features", 50, 500, 200, 20, 10, false),
            new("Level", 50, 480, 40, 10, 10, false), new("Features", 150, 480, 100, 10, 10, false),
            new("1st", 50, 460, 40, 10, 10, false), new("Fixture Beta Rite", 150, 460, 100, 10, 10, false),
            new("2nd", 50, 440, 40, 10, 10, false), new("Fixture Beta Vigil", 150, 440, 100, 10, 10, false),
            new("3rd", 50, 420, 40, 10, 10, false), new("Fixture Echo", 150, 420, 100, 10, 10, false),
        ];
    }

    private static (TempApp Temp, Guid SourceId, Guid JobId) Imported()
    {
        var temp = new TempApp(new TwoClassPageExtractor());
        var source = temp.App.CreateHomebrewSource(new("Test Identity Book", [RulesFamilies.Srd51]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = Run(temp, temp.App.StartImport(new(source.Id, WholeDocument: true)).Id);
        return (temp, source.Id, job.Id);
    }

    private static ImportJobRecord Run(TempApp temp, Guid jobId)
    {
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        return temp.App.ImportStatus(jobId);
    }

    private static StoredCandidate Class(TempApp temp, Guid jobId, string name) =>
        temp.App.ListCandidates(new(jobId)).Single(c => c.Candidate.ProposedKind == ContentKind.Class && c.Candidate.ProposedName == name);

    private static StoredCandidate EchoOf(TempApp temp, Guid jobId, string className) =>
        temp.App.ListCandidates(new(jobId)).Single(c => c.Candidate.ProposedName == "Fixture Echo" && c.Candidate.Fields["class"] == className);

    private static StoredCandidate AcceptAsReference(TempApp temp, StoredCandidate stub)
    {
        var edited = temp.App.EditCandidate(new(stub.Id, Expected: temp.EtagOf(stub.Id), RulesFamilies: [RulesFamilies.Srd51]));
        return temp.App.AcceptCandidate(new(edited.Id, AsReference: true, Confirm: true));
    }

    private static bool NotesEarlier(StoredCandidate candidate) =>
        candidate.Current.Uncertainties.Any(u => u.Contains("reviewed in an earlier import", StringComparison.Ordinal));

    [Fact]
    public void Two_same_page_classes_listing_one_name_have_stubs_with_different_identities()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var alpha = EchoOf(temp, jobId, "Fixture Alpha");
        var beta = EchoOf(temp, jobId, "Fixture Beta");

        Assert.NotEqual(alpha.Id, beta.Id);
        Assert.Equal(alpha.Candidate.Page, beta.Candidate.Page);
        Assert.NotEqual(TomeStackApp.CandidateIdentity(alpha.Candidate), TomeStackApp.CandidateIdentity(beta.Candidate));
    }

    [Fact]
    public void Reviewing_one_same_page_stub_and_detecting_again_leaves_the_other_pending_and_linked_to_its_own_stub()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var accepted = AcceptAsReference(temp, EchoOf(temp, jobId, "Fixture Alpha"));
        // Re-run detection over the same job (an interrupted job resumes and detects again).
        temp.App.Store.SaveImportJob(temp.App.ImportStatus(jobId) with { Status = ImportJobStatus.Interrupted });
        temp.App.ResumeImport(jobId);
        Run(temp, jobId);

        var beta = Class(temp, jobId, "Fixture Beta");
        var betaStub = EchoOf(temp, jobId, "Fixture Beta");
        Assert.Equal(CandidateStatus.Pending, betaStub.Status);
        Assert.Equal(CandidateStatus.AcceptedAsReference, temp.App.Store.FindImportCandidate(accepted.Id)!.Status);
        Assert.Contains(beta.GrantsCandidates!, g => g.CandidateId == betaStub.Id);
        Assert.DoesNotContain(beta.GrantsCandidates!, g => g.CandidateId == accepted.Id);
        Assert.False(NotesEarlier(beta));
        // The class that owns the reviewed stub still links to it.
        Assert.Contains(Class(temp, jobId, "Fixture Alpha").GrantsCandidates!, g => g.CandidateId == accepted.Id);
    }

    [Fact]
    public void A_second_job_on_the_same_document_does_not_mistake_the_other_classes_stub_for_a_reviewed_one()
    {
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        AcceptAsReference(temp, EchoOf(temp, jobId, "Fixture Alpha"));

        var second = Run(temp, temp.App.StartImport(new(sourceId, WholeDocument: true)).Id);

        var betaStub = EchoOf(temp, second.Id, "Fixture Beta");
        Assert.Equal(CandidateStatus.Pending, betaStub.Status);
        Assert.False(NotesEarlier(Class(temp, second.Id, "Fixture Beta")));
        Assert.Contains(Class(temp, second.Id, "Fixture Beta").GrantsCandidates!, g => g.CandidateId == betaStub.Id);
        // Beta's stub is not blocked as already accepted from another reading; Alpha's own twin is (and is not proposed again).
        Assert.DoesNotContain(temp.App.CheckCandidate(betaStub.Id).Blockers, b => b.Code == "import.already-accepted");
        Assert.DoesNotContain(temp.App.ListCandidates(new(second.Id)), c => c.Candidate.ProposedName == "Fixture Echo" && c.Candidate.Fields["class"] == "Fixture Alpha");
        Assert.True(NotesEarlier(Class(temp, second.Id, "Fixture Alpha")));
    }

    [Fact]
    public void A_single_class_stub_reviewed_in_an_earlier_job_is_still_recognised()
    {
        var temp = new TempApp();
        using var _t = temp;
        var source = temp.App.CreateHomebrewSource(new("Test Class Book", [RulesFamilies.Srd521]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var first = Run(temp, temp.App.StartImport(new(source.Id, WholeDocument: true)).Id);
        var beacon = temp.App.ListCandidates(new(first.Id)).Single(c => c.Candidate.ProposedName == "Fixture Beacon");
        temp.App.IgnoreCandidate(beacon.Id);

        var second = Run(temp, temp.App.StartImport(new(source.Id, WholeDocument: true)).Id);

        Assert.DoesNotContain(temp.App.ListCandidates(new(second.Id)), c => c.Candidate.ProposedName == "Fixture Beacon");
        Assert.True(NotesEarlier(temp.App.ListCandidates(new(second.Id)).Single(c => c.Candidate.ProposedName == "Fixture Lamplighter")));
    }
}
