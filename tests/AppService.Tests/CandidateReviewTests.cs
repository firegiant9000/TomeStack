using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M4 D4 (SPEC I-02; ADR-004): accepting a candidate validates the draft it would become (schema, references, formulas,
/// cycles) and lists its dependencies. Low-confidence fields and unresolved references block acceptance until edited,
/// or until it is accepted as reference only. Accepting gives a draft only; publishing is the separate, re-validating
/// content.publish. The original fixture book only.
/// </summary>
public class CandidateReviewTests
{
    private static (TempApp Temp, Guid SourceId, Guid JobId) Imported()
    {
        var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Review Book", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = temp.App.StartImport(new(source.Id, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        return (temp, source.Id, job.Id);
    }

    private static StoredCandidate Named(TempApp temp, Guid jobId, string name) =>
        temp.App.ListCandidates(new(jobId)).Single(c => c.Candidate.ProposedName == name);

    private static string Code(Action action) => Assert.Throws<AppValidationException>(action).Problems[0].Code;

    [Fact]
    public void A_clean_candidate_passes_the_check_and_becomes_an_inactive_draft_with_reference_effects()
    {
        var (temp, sourceId, jobId) = Imported();
        using var _ = temp;
        var ember = Named(temp, jobId, "Fixture Ember Lance");

        var check = temp.App.CheckCandidate(ember.Id);
        Assert.True(check.CanAccept);
        Assert.Empty(check.Blockers);
        Assert.Empty(check.Report.Errors);
        Assert.Contains(check.Dependencies, d => d.Kind == "source" && d.Name == "Test Review Book");
        Assert.Equal("candidate.confirmation-required", Code(() => temp.App.AcceptCandidate(new(ember.Id))));

        var accepted = temp.App.AcceptCandidate(new(ember.Id, Confirm: true));

        Assert.Equal(CandidateStatus.Accepted, accepted.Status);
        var draft = temp.App.Store.FindRevision(accepted.Draft!)!;
        Assert.Equal((RevisionStatus.Draft, ContentKind.Spell, "Fixture Ember Lance", sourceId, new PageRef(2)), (draft.Status, draft.Kind, draft.Name, draft.Provenance.SourceId, draft.Provenance.Page));
        Assert.All(draft.Effects, e => Assert.Equal(AutomationStatus.Reference, e.Automation)); // ADR-004
        Assert.StartsWith("A lance of fixture flame", draft.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.App.ListContent(RulesFamilies.Srd521), o => o.Reference == accepted.Draft); // drafts are never active
        Assert.Equal("candidate.already-reviewed", Code(() => temp.App.AcceptCandidate(new(ember.Id, Confirm: true))));

        // Publishing is the separate command, and it validates again.
        var published = temp.App.Publish(accepted.Draft!);
        Assert.Equal(RevisionStatus.Published, temp.App.Store.FindRevision(published.Published)!.Status);
        Assert.Equal("candidate-accepted", temp.App.ImportAudit(jobId)[^1].Event);
    }

    [Fact]
    public void An_unresolved_reference_blocks_acceptance_until_dismissed_or_accepted_as_reference()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var stormcall = Named(temp, jobId, "Fixture Stormcall");

        var check = temp.App.CheckCandidate(stormcall.Id);
        Assert.False(check.CanAccept);
        Assert.True(check.CanAcceptAsReference);
        Assert.Contains(check.Blockers, b => b.Code == "candidate.unresolved-reference");
        Assert.Contains(check.Dependencies, d => d.Kind == "unresolved-name" && d.Name == "Fixture Thunder Word");
        // The error never quotes the PDF's text.
        var refused = Assert.Throws<AppValidationException>(() => temp.App.AcceptCandidate(new(stormcall.Id, Confirm: true)));
        Assert.Equal("candidate.needs-review", refused.Problems[0].Code);
        Assert.All(refused.Problems, p => Assert.DoesNotContain("Thunder", p.Message, StringComparison.Ordinal));

        var asReference = temp.App.AcceptCandidate(new(stormcall.Id, AsReference: true, Confirm: true));
        Assert.Equal(CandidateStatus.AcceptedAsReference, asReference.Status);
        var draft = temp.App.Store.FindRevision(asReference.Draft!)!;
        Assert.Empty(draft.Effects);
        Assert.Equal(new PageRef(3), draft.Provenance.Page);
    }

    [Fact]
    public void Editing_clears_the_unsure_fields_and_dismissing_a_reference_unblocks_it()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var warden = Named(temp, jobId, "Fixture Warden");
        Assert.Contains(temp.App.CheckCandidate(warden.Id).Blockers, b => b.Code == "candidate.low-confidence");

        var edited = temp.App.EditCandidate(new(warden.Id, Name: "Fixture Warden (edited)", Effects: [new HitDieEffect { Id = "hit-die", Die = 10 }]));
        Assert.Empty(edited.Current.LowConfidenceFields);
        Assert.Equal(["Fixture Focus", "Fixture Resolve"], edited.Current.UnresolvedReferences);
        Assert.Equal("Fixture Warden", edited.Candidate.ProposedName); // the detected version stays as it was
        Assert.False(temp.App.CheckCandidate(warden.Id).CanAccept);

        temp.App.EditCandidate(new(warden.Id, DismissReferences: ["fixture focus", "Fixture Resolve"]));
        var check = temp.App.CheckCandidate(warden.Id);
        Assert.True(check.CanAccept, string.Join("; ", check.Blockers.Concat(check.Report.Errors).Select(d => d.Code)));
        var draft = temp.App.Store.FindRevision(temp.App.AcceptCandidate(new(warden.Id, Confirm: true)).Draft!)!;
        Assert.Equal(("Fixture Warden (edited)", ContentKind.Class), (draft.Name, draft.Kind));
        Assert.Equal(10, draft.Effects.OfType<HitDieEffect>().Single().Die);
    }

    [Fact]
    public void A_candidate_that_fails_validation_is_refused_with_the_validators_errors()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var hookblade = Named(temp, jobId, "Fixture Hookblade");
        var weapon = hookblade.Current.ProposedEffects.OfType<WeaponEffect>().Single();

        temp.App.EditCandidate(new(hookblade.Id, Effects: [weapon with { Damage = "1d8 + + 1" }]));
        var check = temp.App.CheckCandidate(hookblade.Id);

        Assert.False(check.CanAccept);
        Assert.Contains(check.Report.Errors, e => e.Code == "validate.dice-invalid");
        var refused = Assert.Throws<AppValidationException>(() => temp.App.AcceptCandidate(new(hookblade.Id, Confirm: true)));
        Assert.Equal("candidate.validation-failed", refused.Problems[0].Code);
        Assert.Contains(refused.Problems, p => p.Code == "validate.dice-invalid");
    }

    [Fact]
    public void Dependencies_list_installed_content_and_what_is_missing()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var feat = Named(temp, jobId, "Fixture Keen Watcher");
        var installed = temp.App.Store.ListRevisions().First(r => r.Status == RevisionStatus.Published && r.Kind == ContentKind.Feature && r.RulesFamilies.Contains(RulesFamilies.Srd521));
        var missing = new ContentReference(Guid.NewGuid(), Guid.NewGuid());

        temp.App.EditCandidate(new(feat.Id, Effects: [.. feat.Current.ProposedEffects,
            new GrantEffect { Id = "grant-installed", Grant = GrantKind.Content, Content = installed.Reference },
            new GrantEffect { Id = "grant-missing", Grant = GrantKind.Content, Content = missing }]));
        var check = temp.App.CheckCandidate(feat.Id);

        Assert.Contains(check.Dependencies, d => d.Kind == "content" && d.Reference == installed.Reference);
        Assert.Contains(check.Dependencies, d => d.Kind == "missing-content" && d.Reference == missing);
        Assert.Contains(check.Report.Errors, e => e.Code.StartsWith("validate.", StringComparison.Ordinal)); // the missing reference is an error
        Assert.False(check.CanAccept);
    }

    [Fact]
    public void An_ignored_candidate_creates_nothing_and_cannot_be_accepted()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = Named(temp, jobId, "Fixture Frost Veil");
        var revisions = temp.App.Store.ListRevisions().Count;

        Assert.Equal(CandidateStatus.Ignored, temp.App.IgnoreCandidate(veil.Id).Status);

        Assert.Equal(revisions, temp.App.Store.ListRevisions().Count);
        Assert.Equal("candidate.already-reviewed", Code(() => temp.App.AcceptCandidate(new(veil.Id, Confirm: true))));
        Assert.Equal("candidate.already-reviewed", Code(() => temp.App.EditCandidate(new(veil.Id, Name: "Other"))));
        Assert.Single(temp.App.ListCandidates(new(jobId, Status: CandidateStatus.Ignored)));
    }

    [Fact]
    public void Edits_are_bounded()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = Named(temp, jobId, "Fixture Frost Veil");

        Assert.Equal("candidate.name-invalid", Code(() => temp.App.EditCandidate(new(veil.Id, Name: " "))));
        Assert.Equal("candidate.family-invalid", Code(() => temp.App.EditCandidate(new(veil.Id, RulesFamilies: ["srd-9"]))));
        Assert.Equal("candidate.summary-too-long", Code(() => temp.App.EditCandidate(new(veil.Id, Summary: new string('x', 20_001)))));
        Assert.Equal("candidate.not-found", Code(() => temp.App.CheckCandidate(Guid.NewGuid())));
    }
}
