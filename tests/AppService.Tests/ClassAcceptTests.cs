using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 11 (D34, ADR-016, rulings R39, R52, R55): accepting a class candidate. The hit die (and the spell slot table, when the
/// class has one) must be confirmed by id; the features the class links become drafts in the same transaction, before the
/// class, which pins them; a linked feature that is blocked or ignored holds the class back. The original fixture book only
/// ("Fixture Lamplighter" on page 7; "Fixture Warden" on page 5).
/// </summary>
public class ClassAcceptTests
{
    private static (TempApp Temp, Guid SourceId, Guid JobId) Imported(bool oneFamily = true)
    {
        var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Class Book", oneFamily ? [RulesFamilies.Srd521] : [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = temp.App.StartImport(new(source.Id, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        return (temp, source.Id, job.Id);
    }

    private static StoredCandidate Named(TempApp temp, Guid jobId, string name) =>
        temp.App.ListCandidates(new(jobId)).Single(c => c.Candidate.ProposedName == name);

    /// <summary>The reviewer looked at the candidate and saved it: the detector's unsure fields (the spell list name) are settled.</summary>
    private static StoredCandidate Reviewed(TempApp temp, Guid jobId, string name)
    {
        var stored = Named(temp, jobId, name);
        return temp.App.EditCandidate(new(stored.Id, Name: stored.Current.ProposedName));
    }

    private static AppValidationException Refused(Action action) => Assert.Throws<AppValidationException>(action);

    private static string[] Codes(AppValidationException ex) => [.. ex.Problems.Select(p => p.Code)];

    private static readonly string[] Both = ["hit-die", "spellcasting"];

    [Fact]
    public void A_class_is_not_accepted_as_a_draft_until_its_hit_die_and_slot_table_are_confirmed()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        // Fixture Glow is the one linked block of the book; settling it leaves only the confirmation to give.
        Assert.Contains(lamplighter.Current.ProposedEffects, e => e.Id == "spellcasting");

        Assert.Equal(["import.class-unconfirmed"], Codes(Refused(() => temp.App.AcceptCandidate(new(lamplighter.Id, Confirm: true)))));
        Assert.Equal(["import.class-unconfirmed"], Codes(Refused(() => temp.App.AcceptCandidate(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: ["hit-die"])))));
        Assert.Equal(["import.class-unconfirmed"], Codes(Refused(() => temp.App.AcceptCandidate(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: ["spellcasting"])))));
        Assert.Equal(["candidate.confirm-unknown"], Codes(Refused(() => temp.App.AcceptCandidate(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: [.. Both, "no-such-effect"])))));
        Assert.Equal(["candidate.confirm-unsupported"], Codes(Refused(() => temp.App.AcceptCandidate(new(lamplighter.Id, AsReference: true, Confirm: true, ConfirmedEffectIds: Both)))));
        var hookblade = Named(temp, jobId, "Fixture Hookblade");
        Assert.Equal(["candidate.confirm-unsupported"], Codes(Refused(() => temp.App.AcceptCandidate(new(hookblade.Id, Confirm: true, ConfirmedEffectIds: ["weapon"])))));

        // Nothing was written by any refusal.
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == lamplighter.Candidate.SourceId);
        Assert.All(temp.App.ListCandidates(new(jobId)), c => Assert.Equal(CandidateStatus.Pending, c.Status));
    }

    [Fact]
    public void A_confirmed_class_becomes_a_draft_that_pins_the_feature_drafts_written_before_it()
    {
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        var linked = lamplighter.GrantsCandidates!.Select(g => g.CandidateId).Distinct().ToList();
        Assert.Equal(6, linked.Count);

        var check = temp.App.CheckCandidate(lamplighter.Id);
        Assert.True(check.CanAccept, string.Join("; ", check.Blockers.Concat(check.Report.Errors).Select(d => $"{d.Code} {d.Message}")));
        Assert.Equal(6, check.Dependencies.Count(d => d.Kind == "content")); // the feature drafts it would write, named before they exist

        var accepted = temp.App.AcceptCandidate(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));

        Assert.Equal(CandidateStatus.Accepted, accepted.Status);
        var draft = temp.App.Store.FindRevision(accepted.Draft!)!;
        Assert.Equal((ContentKind.Class, RevisionStatus.Draft), (draft.Kind, draft.Status));
        var all = temp.App.ListCandidates(new(jobId));
        Assert.All(linked, id => Assert.Equal(CandidateStatus.Accepted, all.Single(c => c.Id == id).Status));

        // The grants pin the exact drafts of the linked candidates (content id and revision id), one per feature, at its lowest level.
        var grants = draft.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content).ToList();
        Assert.Equal(6, grants.Count);
        foreach (var grant in grants)
        {
            var feature = all.Single(c => c.Draft == grant.Content);
            Assert.Equal(ContentKind.Feature, feature.Candidate.ProposedKind);
            var featureDraft = temp.App.Store.FindRevision(grant.Content!)!;
            Assert.Equal((RevisionStatus.Draft, sourceId), (featureDraft.Status, featureDraft.Provenance.SourceId));
            Assert.Equal(lamplighter.GrantsCandidates!.Where(g => g.CandidateId == feature.Id).Min(g => g.Level), grant.Level);
        }
        Assert.Equal(["Fixture Beacon", "Fixture Dusk Step", "Fixture Final Light", "Fixture Glow", "Fixture Lantern Rite", "Fixture Sconce"], grants.Select(g => temp.App.Store.FindRevision(g.Content!)!.Name).Order(StringComparer.Ordinal));

        // Confirmed effects are automatic after read-back; every other proposal is reference (the counterpart is in ImportQuarantineTests).
        Assert.Equal(AutomationStatus.Automatic, Assert.IsType<HitDieEffect>(draft.Effects.Single(e => e.Id == "hit-die")).Automation);
        var casting = Assert.IsType<SpellcastingEffect>(draft.Effects.Single(e => e.Id == "spellcasting"));
        Assert.Equal((AutomationStatus.Automatic, "fixture-lamplighter", 20), (casting.Automation, casting.SpellList, casting.Slots.Count));
        Assert.All(draft.Effects.Where(e => e is not GrantEffect { Grant: GrantKind.Content } && e.Id is not ("hit-die" or "spellcasting")), e => Assert.Equal(AutomationStatus.Reference, e.Automation));
        Assert.Contains(draft.Effects, e => e.Id == "save-wis" && e.Automation == AutomationStatus.Reference);

        // The linked features' own effects are reference; the whole set publishes together, the class pinning what it grants.
        Assert.All(grants.SelectMany(g => temp.App.Store.FindRevision(g.Content!)!.Effects), e => Assert.Equal(AutomationStatus.Reference, e.Automation));
    }

    [Fact]
    public void A_linked_feature_that_is_already_accepted_is_pinned_not_written_again()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        var glow = Named(temp, jobId, "Fixture Glow");
        var first = temp.App.AcceptCandidate(new(glow.Id, Confirm: true));

        var accepted = temp.App.AcceptCandidate(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));

        var draft = temp.App.Store.FindRevision(accepted.Draft!)!;
        Assert.Contains(draft.Effects.OfType<GrantEffect>(), g => g.Content == first.Draft);
        Assert.Single(temp.App.Store.ListRevisions(), r => r.Name == "Fixture Glow");
    }

    [Fact]
    public void A_linked_feature_that_is_blocked_holds_the_class_back_with_its_codes_and_nothing_is_written()
    {
        // The Warden links Fixture Stormcall, which names a spell that is not installed: its blocker is the class's.
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var warden = Named(temp, jobId, "Fixture Warden");
        temp.App.EditCandidate(new(warden.Id, Effects: [new HitDieEffect { Id = "hit-die", Die = 10 }]));
        var before = temp.App.Store.ListRevisions().Count;

        var check = temp.App.CheckCandidate(warden.Id);
        Assert.False(check.CanAccept);
        Assert.Contains(check.Blockers, b => b.Code == "candidate.unresolved-reference" && b.Message.StartsWith("1 linked feature(s)", StringComparison.Ordinal));
        var refused = Refused(() => temp.App.AcceptCandidate(new(warden.Id, Confirm: true, ConfirmedEffectIds: ["hit-die"])));
        Assert.Equal("candidate.needs-review", refused.Problems[0].Code);
        Assert.Contains("candidate.unresolved-reference", Codes(refused));

        Assert.Equal(before, temp.App.Store.ListRevisions().Count);
        Assert.All(temp.App.ListCandidates(new(jobId)), c => Assert.Equal(CandidateStatus.Pending, c.Status));

        // Accepting the feature as reference settles it; the class then pins that draft and writes the two stubs.
        var stormcall = temp.App.AcceptCandidate(new(Named(temp, jobId, "Fixture Stormcall").Id, AsReference: true, Confirm: true));
        var accepted = temp.App.AcceptCandidate(new(warden.Id, Confirm: true, ConfirmedEffectIds: ["hit-die"]));
        var draft = temp.App.Store.FindRevision(accepted.Draft!)!;
        Assert.Equal(3, draft.Effects.OfType<GrantEffect>().Count(g => g.Grant == GrantKind.Content));
        Assert.Contains(draft.Effects.OfType<GrantEffect>(), g => g.Content == stormcall.Draft);
        Assert.Equal(AutomationStatus.Automatic, draft.Effects.OfType<HitDieEffect>().Single().Automation);
    }

    [Fact]
    public void A_linked_feature_that_is_ignored_blocks_the_class_but_not_accepting_it_as_reference()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        temp.App.IgnoreCandidate(Named(temp, jobId, "Fixture Beacon").Id);

        var check = temp.App.CheckCandidate(lamplighter.Id);

        Assert.False(check.CanAccept);
        var blocker = Assert.Single(check.Blockers, b => b.Code == "candidate.linked-ignored");
        Assert.StartsWith("1 linked feature(s) are ignored", blocker.Message, StringComparison.Ordinal);
        Assert.Contains("candidate.linked-ignored", Codes(Refused(() => temp.App.AcceptCandidate(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both)))));
        Assert.True(check.CanAcceptAsReference);
        var reference = temp.App.AcceptCandidate(new(lamplighter.Id, AsReference: true, Confirm: true));
        var draft = temp.App.Store.FindRevision(reference.Draft!)!;
        Assert.Empty(draft.Effects); // reference only: no effects, no grants, and the other features stay pending
        Assert.Equal(CandidateStatus.Pending, Named(temp, jobId, "Fixture Glow").Status);
    }

    [Fact]
    public void A_bulk_accept_never_accepts_a_class_with_everything_reference()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");

        var result = temp.App.AcceptMany(new([lamplighter.Id], Confirm: true));

        Assert.Empty(result.Accepted);
        Assert.Equal(["import.class-unconfirmed"], Assert.Single(result.Blocked).Codes);
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == lamplighter.Candidate.SourceId);
        Assert.All(temp.App.ListCandidates(new(jobId)), c => Assert.Equal(CandidateStatus.Pending, c.Status));
        // As reference it is still accepted in bulk.
        Assert.Single(temp.App.AcceptMany(new([lamplighter.Id], AsReference: true, Confirm: true)).Accepted);
    }

    [Fact]
    public void A_two_family_source_answers_once_on_the_class_and_the_stubs_take_it()
    {
        var (temp, _, jobId) = Imported(oneFamily: false);
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        Assert.Contains(temp.App.CheckCandidate(lamplighter.Id).Blockers, b => b.Code == "import.family-needed");

        temp.App.EditCandidate(new(lamplighter.Id, RulesFamilies: [RulesFamilies.Srd521]));
        var check = temp.App.CheckCandidate(lamplighter.Id);
        Assert.True(check.CanAccept, string.Join("; ", check.Blockers.Concat(check.Report.Errors).Select(d => $"{d.Code} {d.Message}")));

        var accepted = temp.App.AcceptCandidate(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));
        var drafts = temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content).Select(g => temp.App.Store.FindRevision(g.Content!)!).ToList();
        Assert.All(drafts, d => Assert.Equal([RulesFamilies.Srd521], d.RulesFamilies));
    }

    [Fact]
    public void A_class_and_the_feature_drafts_it_pins_publish_together()
    {
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var accepted = temp.App.AcceptCandidate(new(Reviewed(temp, jobId, "Fixture Lamplighter").Id, Confirm: true, ConfirmedEffectIds: Both));

        var drafts = temp.App.ListContent(RulesFamilies.Srd521).Count(o => o.SourceId == sourceId);
        Assert.Equal(0, drafts); // drafts are not active
        var ids = temp.App.Store.ListRevisions().Where(r => r.Provenance.SourceId == sourceId).Select(r => r.ContentId).ToList();
        var preview = temp.App.PreviewPublishMany(ids);
        Assert.True(preview.CanPublish);
        var result = temp.App.PublishMany(new(ids, [.. preview.Entries.Select(e => e.Draft.RevisionId)], true));

        Assert.Equal(7, result.Published.Count);
        var published = temp.App.Store.ListRevisions().Where(r => r.Provenance.SourceId == sourceId && r.Status == RevisionStatus.Published).ToList();
        var lamplighter = published.Single(r => r.Name == "Fixture Lamplighter");
        var pinned = lamplighter.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content).Select(g => g.Content!).ToList();
        Assert.Equal(6, pinned.Count);
        Assert.All(pinned, p => Assert.Contains(published, r => r.Reference == p)); // re-pointed at the published feature revisions
        Assert.NotNull(accepted.Draft);
    }
}
