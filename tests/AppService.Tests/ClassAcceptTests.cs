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
        return temp.App.EditCandidate(new(stored.Id, Expected: temp.EtagOf(stored.Id), Name: stored.Current.ProposedName));
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

        Assert.Equal(["import.class-unconfirmed"], Codes(Refused(() => temp.Accept(new(lamplighter.Id, Confirm: true)))));
        Assert.Equal(["import.class-unconfirmed"], Codes(Refused(() => temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: ["hit-die"])))));
        Assert.Equal(["import.class-unconfirmed"], Codes(Refused(() => temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: ["spellcasting"])))));
        Assert.Equal(["candidate.confirm-unknown"], Codes(Refused(() => temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: [.. Both, "no-such-effect"])))));
        Assert.Equal(["candidate.confirm-unsupported"], Codes(Refused(() => temp.Accept(new(lamplighter.Id, AsReference: true, Confirm: true, ConfirmedEffectIds: Both)))));
        var hookblade = Named(temp, jobId, "Fixture Hookblade");
        Assert.Equal(["candidate.confirm-unsupported"], Codes(Refused(() => temp.Accept(new(hookblade.Id, Confirm: true, ConfirmedEffectIds: ["weapon"])))));

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
        // Six fixture features and "Spellcasting": the SRD installs several features of that name, so it is a stub too (R67).
        Assert.Equal(7, linked.Count);

        var check = temp.App.CheckCandidate(lamplighter.Id);
        Assert.True(check.CanAccept, string.Join("; ", check.Blockers.Concat(check.Report.Errors).Select(d => $"{d.Code} {d.Message}")));
        Assert.Equal(7, check.Dependencies.Count(d => d.Kind == "content")); // the feature drafts it would write, named before they exist

        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));

        Assert.Equal(CandidateStatus.Accepted, accepted.Status);
        var draft = temp.App.Store.FindRevision(accepted.Draft!)!;
        Assert.Equal((ContentKind.Class, RevisionStatus.Draft), (draft.Kind, draft.Status));
        var all = temp.App.ListCandidates(new(jobId));
        Assert.All(linked, id => Assert.Equal(CandidateStatus.Accepted, all.Single(c => c.Id == id).Status));

        // The grants pin the exact drafts of the linked candidates (content id and revision id), one per feature, at its lowest level.
        var grants = draft.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content).ToList();
        Assert.Equal(7, grants.Count);
        foreach (var grant in grants)
        {
            var feature = all.Single(c => c.Draft == grant.Content);
            Assert.Equal(ContentKind.Feature, feature.Candidate.ProposedKind);
            var featureDraft = temp.App.Store.FindRevision(grant.Content!)!;
            Assert.Equal((RevisionStatus.Draft, sourceId), (featureDraft.Status, featureDraft.Provenance.SourceId));
            Assert.Equal(lamplighter.GrantsCandidates!.Where(g => g.CandidateId == feature.Id).Min(g => g.Level), grant.Level);
        }
        Assert.Equal(["Fixture Beacon", "Fixture Dusk Step", "Fixture Final Light", "Fixture Glow", "Fixture Lantern Rite", "Fixture Sconce", "Spellcasting"], grants.Select(g => temp.App.Store.FindRevision(g.Content!)!.Name).Order(StringComparer.Ordinal));

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
        var first = temp.Accept(new(glow.Id, Confirm: true));

        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));

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
        temp.App.EditCandidate(new(warden.Id, Expected: temp.EtagOf(warden.Id), Effects: [new HitDieEffect { Id = "hit-die", Die = 10 }]));
        var before = temp.App.Store.ListRevisions().Count;

        var check = temp.App.CheckCandidate(warden.Id);
        Assert.False(check.CanAccept);
        Assert.Contains(check.Blockers, b => b.Code == "candidate.unresolved-reference" && b.Message.StartsWith("1 linked feature(s)", StringComparison.Ordinal));
        var refused = Refused(() => temp.Accept(new(warden.Id, Confirm: true, ConfirmedEffectIds: ["hit-die"])));
        Assert.Equal("candidate.needs-review", refused.Problems[0].Code);
        Assert.Contains("candidate.unresolved-reference", Codes(refused));

        Assert.Equal(before, temp.App.Store.ListRevisions().Count);
        Assert.All(temp.App.ListCandidates(new(jobId)), c => Assert.Equal(CandidateStatus.Pending, c.Status));

        // Accepting the feature as reference settles it; the class then pins that draft and writes the two stubs.
        var stormcall = temp.Accept(new(Named(temp, jobId, "Fixture Stormcall").Id, AsReference: true, Confirm: true));
        var accepted = temp.Accept(new(warden.Id, Confirm: true, ConfirmedEffectIds: ["hit-die"]));
        var draft = temp.App.Store.FindRevision(accepted.Draft!)!;
        Assert.Equal(3, draft.Effects.OfType<GrantEffect>().Count(g => g.Grant == GrantKind.Content));
        Assert.Contains(draft.Effects.OfType<GrantEffect>(), g => g.Content == stormcall.Draft);
        Assert.Equal(AutomationStatus.Automatic, draft.Effects.OfType<HitDieEffect>().Single().Automation);
    }

    [Fact]
    public void A_linked_feature_that_is_ignored_is_not_granted_and_does_not_hold_the_class_back()
    {
        // Ruling R66 (fix round 1, item 2): an ignored linked feature is dropped from the class draft's grants, shown as a note.
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        var beacon = temp.Ignore(Named(temp, jobId, "Fixture Beacon").Id);

        var check = temp.App.CheckCandidate(lamplighter.Id);

        Assert.True(check.CanAccept, string.Join("; ", check.Blockers.Select(b => b.Code)));
        Assert.DoesNotContain(check.Blockers, b => b.Code == "candidate.linked-ignored");
        Assert.Contains("1 linked feature is ignored and will not be granted.", check.Notes);
        Assert.Equal(CandidateStatus.Ignored, Assert.Single(check.Linked, l => l.CandidateId == beacon.Id).Status);

        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));
        var granted = temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content)
            .Select(g => temp.App.Store.FindRevision(g.Content!)!.Name).ToList();
        Assert.Equal(6, granted.Count);
        Assert.DoesNotContain("Fixture Beacon", granted);
        Assert.Equal(CandidateStatus.Ignored, Named(temp, jobId, "Fixture Beacon").Status);
    }

    [Fact]
    public void Accepting_a_class_as_reference_leaves_its_linked_features_pending()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        var reference = temp.Accept(new(lamplighter.Id, AsReference: true, Confirm: true));
        var draft = temp.App.Store.FindRevision(reference.Draft!)!;
        Assert.Empty(draft.Effects); // reference only: no effects, no grants, and the features stay pending
        Assert.Equal(CandidateStatus.Pending, Named(temp, jobId, "Fixture Glow").Status);
    }

    [Fact]
    public void The_check_names_every_linked_feature_with_its_level_and_status()
    {
        // Fix round 1, item 11: the server returns the names, so the panel needs no list query (which stopped at 200).
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        temp.Accept(new(Named(temp, jobId, "Fixture Glow").Id, Confirm: true));

        var linked = temp.App.CheckCandidate(lamplighter.Id).Linked;

        Assert.Equal(
            [("Fixture Lantern Rite", 1), ("Spellcasting", 1), ("Fixture Glow", 2), ("Fixture Beacon", 5), ("Fixture Sconce", 9), ("Fixture Dusk Step", 15), ("Fixture Final Light", 20)],
            linked.Select(l => (l.Name, l.Level)));
        Assert.Equal(CandidateStatus.Accepted, linked.Single(l => l.Name == "Fixture Glow").Status);
        Assert.All(linked.Where(l => l.Name != "Fixture Glow"), l => Assert.Equal(CandidateStatus.Pending, l.Status));
    }

    [Fact]
    public void A_linked_feature_published_alone_after_its_accept_is_pinned_at_its_published_revision()
    {
        // Fix round 1, item 1: the R55 pin is the feature's newest published revision, not the draft its accept wrote.
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        var glow = temp.Accept(new(Named(temp, jobId, "Fixture Glow").Id, Confirm: true));
        var published = temp.App.Publish(glow.Draft!).Published;

        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));

        var grants = temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content).ToList();
        Assert.Contains(grants, g => g.Content == published);
        Assert.DoesNotContain(grants, g => g.Content == glow.Draft);

        // Publish all: the class and its other features publish; the class keeps pinning the published Glow.
        var ids = temp.App.Store.ListRevisionsInOrder().Where(r => r.Provenance.SourceId == sourceId).GroupBy(r => r.ContentId)
            .Where(g => g.Last().Status == RevisionStatus.Draft).Select(g => g.Key).ToList();
        var preview = temp.App.PreviewPublishMany(ids);
        Assert.True(preview.CanPublish, string.Join("; ", preview.Entries.SelectMany(e => e.Report.Errors).Select(d => d.Code)));
        temp.App.PublishMany(new(ids, [.. preview.Entries.Where(e => ids.Contains(e.Draft.ContentId)).Select(e => e.Draft.RevisionId)], true));
        var klass = temp.App.Store.ListRevisionsInOrder().Last(r => r.Provenance.SourceId == sourceId && r.Name == "Fixture Lamplighter");
        Assert.Equal(RevisionStatus.Published, klass.Status);
        Assert.Contains(klass.Effects.OfType<GrantEffect>(), g => g.Content == published);
    }

    [Fact]
    public void A_linked_feature_re_drafted_after_its_accept_is_pinned_at_its_newest_draft()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        var glow = temp.Accept(new(Named(temp, jobId, "Fixture Glow").Id, Confirm: true));
        var old = temp.App.Store.FindRevision(glow.Draft!)!;
        var redrafted = temp.App.SaveDraft(old with { RevisionId = Guid.Empty, Summary = "Fixture text edited in the studio." });

        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));

        var grants = temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content).ToList();
        Assert.Contains(grants, g => g.Content == redrafted);
        Assert.DoesNotContain(grants, g => g.Content == glow.Draft);
    }

    private static ContentRevision PublishedFeature(TempApp temp, string sourceName, string name)
    {
        var source = temp.App.CreateHomebrewSource(new(sourceName, [RulesFamilies.Srd521]));
        var draft = temp.App.SaveDraft(new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Feature, Name = name, RulesFamilies = [RulesFamilies.Srd521],
            Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = [],
        });
        return temp.App.Store.FindRevision(temp.App.Publish(draft).Published)!;
    }

    private static (TempApp Temp, Guid JobId) ImportedInto(TempApp temp)
    {
        var source = temp.App.CreateHomebrewSource(new("Test Class Book", [RulesFamilies.Srd521]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = temp.App.StartImport(new(source.Id, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        return (temp, job.Id);
    }

    [Fact]
    public void A_table_name_that_matches_one_installed_feature_links_to_it_and_the_accept_pins_that_revision()
    {
        // Ruling R67 (fix round 1, item 3): one installed published feature of that name in the class's families: no stub, a pin.
        var temp = new TempApp();
        using var _t = temp;
        var installed = PublishedFeature(temp, "Test Installed Book", "Fixture Beacon");
        var (_, jobId) = ImportedInto(temp);
        Assert.DoesNotContain(temp.App.ListCandidates(new(jobId)), c => c.Candidate.ProposedName == "Fixture Beacon");
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");

        var check = temp.App.CheckCandidate(lamplighter.Id);
        var link = Assert.Single(check.Linked, l => l.Name == "Fixture Beacon");
        Assert.Equal((null, 5, true), (link.CandidateId, link.Level, link.Installed));
        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));

        var grant = Assert.Single(temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>(), g => g.Content == installed.Reference);
        Assert.Equal(5, grant.Level);
    }

    [Fact]
    public void A_table_name_that_matches_two_installed_features_becomes_a_stub()
    {
        var temp = new TempApp();
        using var _t = temp;
        var one = PublishedFeature(temp, "Test Installed Book", "Fixture Beacon");
        var two = PublishedFeature(temp, "Test Other Book", "Fixture Beacon");
        var (_, jobId) = ImportedInto(temp);

        var stub = Assert.Single(temp.App.ListCandidates(new(jobId)), c => c.Candidate.ProposedName == "Fixture Beacon");
        var accepted = temp.Accept(new(Reviewed(temp, jobId, "Fixture Lamplighter").Id, Confirm: true, ConfirmedEffectIds: Both));

        var pins = temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>().Select(g => g.Content).ToList();
        Assert.DoesNotContain(one.Reference, pins);
        Assert.DoesNotContain(two.Reference, pins);
        Assert.Contains(Named(temp, jobId, "Fixture Beacon").Draft, pins);
        Assert.Equal(stub.Id, Named(temp, jobId, "Fixture Beacon").Id);
    }

    [Fact]
    public void A_table_feature_whose_installed_match_no_longer_applies_at_accept_is_counted_in_a_note_and_not_granted()
    {
        // Fix round 2, item 1: detection linked it to the one installed match; the reviewer then changed the class's families.
        var temp = new TempApp();
        using var _t = temp;
        var installed = PublishedFeature(temp, "Test Installed Book", "Fixture Beacon");
        var (_, jobId) = ImportedInto(temp);
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        Assert.DoesNotContain(temp.App.CheckCandidate(lamplighter.Id).Notes, n => n.Contains("not linked", StringComparison.Ordinal));

        // The API refuses families outside the source's, so the changed family is written to the store directly.
        var stored = temp.App.Store.FindImportCandidate(lamplighter.Id)!;
        temp.App.Store.SaveImportCandidate(stored with { Edited = stored.Current with { RulesFamilies = [RulesFamilies.Srd51] } });
        var check = temp.App.CheckCandidate(lamplighter.Id);
        Assert.Contains("1 table feature is not linked and will not be granted; add it in the studio.", check.Notes);
        Assert.DoesNotContain(check.Notes, n => n.Contains("Beacon", StringComparison.Ordinal));
        // The check's plan does not pin it either (the accept itself is refused here: the other grants are srd-5.2.1; the next test accepts).
        Assert.DoesNotContain(check.Linked, l => l.Name == "Fixture Beacon");
        Assert.NotNull(installed);
    }

    [Fact]
    public void Renaming_a_linked_stub_keeps_it_linked_to_its_table_entry()
    {
        // Review fix (item 11, D34): the link was made on the detected name; one installed match published after detection must not add a second grant.
        var temp = new TempApp();
        using var _t = temp;
        var (_, jobId) = ImportedInto(temp);
        var installed = PublishedFeature(temp, "Test Installed Book", "Fixture Beacon");
        var beacon = Named(temp, jobId, "Fixture Beacon");
        temp.App.EditCandidate(new(beacon.Id, Expected: temp.EtagOf(beacon.Id), Name: "Fixture Beacon Renamed"));
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");

        var check = temp.App.CheckCandidate(lamplighter.Id);
        Assert.DoesNotContain(check.Notes, n => n.Contains("not linked", StringComparison.Ordinal));
        var renamed = Assert.Single(check.Linked, l => l.CandidateId == beacon.Id);
        Assert.Equal("Fixture Beacon Renamed", renamed.Name);
        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));

        var grants = temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>().ToList();
        Assert.DoesNotContain(grants, g => g.Content == installed.Reference);
        Assert.Single(grants, g => g.Content is { } c && temp.App.Store.FindRevision(c)!.Name == "Fixture Beacon Renamed");
    }

    [Fact]
    public void A_table_feature_with_two_installed_matches_at_accept_is_counted_in_a_note()
    {
        var temp = new TempApp();
        using var _t = temp;
        var one = PublishedFeature(temp, "Test Installed Book", "Fixture Beacon");
        var (_, jobId) = ImportedInto(temp);
        var two = PublishedFeature(temp, "Test Other Book", "Fixture Beacon"); // published after detection
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");

        var check = temp.App.CheckCandidate(lamplighter.Id);
        Assert.Contains("1 table feature is not linked and will not be granted; add it in the studio.", check.Notes);
        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));
        var pins = temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>().Select(g => g.Content).ToList();
        Assert.DoesNotContain(one.Reference, pins);
        Assert.DoesNotContain(two.Reference, pins);
    }

    [Fact]
    public void The_normal_one_match_case_has_no_unlinked_note()
    {
        var temp = new TempApp();
        using var _t = temp;
        PublishedFeature(temp, "Test Installed Book", "Fixture Beacon");
        var (_, jobId) = ImportedInto(temp);
        Assert.DoesNotContain(temp.App.CheckCandidate(Reviewed(temp, jobId, "Fixture Lamplighter").Id).Notes, n => n.Contains("not linked", StringComparison.Ordinal));
    }

    [Fact]
    public void A_table_name_that_matches_no_installed_feature_becomes_a_stub()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        Assert.Single(temp.App.ListCandidates(new(jobId)), c => c.Candidate.ProposedName == "Fixture Beacon");
        Assert.All(temp.App.CheckCandidate(Reviewed(temp, jobId, "Fixture Lamplighter").Id).Linked, l => Assert.False(l.Installed));
    }

    [Fact]
    public void Links_to_features_reviewed_in_an_earlier_import_of_the_document_become_an_uncertainty_on_the_class()
    {
        // Ruling R68 (fix round 1, item 4): not dropped silently, and not linked across jobs.
        var (temp, sourceId, firstJob) = Imported();
        using var _t = temp;
        temp.Accept(new(Named(temp, firstJob, "Fixture Glow").Id, Confirm: true));
        temp.Ignore(Named(temp, firstJob, "Fixture Beacon").Id);

        var second = temp.App.StartImport(new(sourceId, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        var lamplighter = Named(temp, second.Id, "Fixture Lamplighter");

        Assert.Contains("2 table features were reviewed in an earlier import and are not linked: add them in the studio.", lamplighter.Current.Uncertainties);
        Assert.Equal(5, lamplighter.GrantsCandidates!.Select(g => g.CandidateId).Distinct().Count());
        Assert.DoesNotContain("table features were reviewed", Named(temp, firstJob, "Fixture Lamplighter").Current.Uncertainties.FirstOrDefault() ?? "", StringComparison.Ordinal);
        // Fix round 3: the uncertainty already counts them, so the check's "not linked" note does not count them again.
        Assert.DoesNotContain(temp.App.CheckCandidate(lamplighter.Id).Notes, n => n.Contains("not linked", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failure_writing_the_class_draft_leaves_no_feature_draft_behind()
    {
        // Fix round 1, item 7: the feature drafts are written first; the class's own write failing rolls them back too.
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        temp.App.BeforeSaveDraft = draft =>
        {
            if (draft.Kind == ContentKind.Class)
                throw new InvalidOperationException("forced failure");
        };

        Assert.Throws<InvalidOperationException>(() => temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both)));

        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == sourceId);
        Assert.All(temp.App.ListCandidates(new(jobId)), c => Assert.Equal(CandidateStatus.Pending, c.Status));
    }

    [Fact]
    public void A_linked_feature_whose_status_changes_before_the_writes_refuses_the_accept_and_writes_nothing()
    {
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        var beacon = Named(temp, jobId, "Fixture Beacon");
        temp.App.BeforeAcceptWrites = () => temp.App.Store.SaveImportCandidate(beacon with { Status = CandidateStatus.Ignored });

        var refused = Refused(() => temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both)));

        Assert.Equal(["candidate.linked-changed"], Codes(refused));
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == sourceId);
        Assert.Equal(CandidateStatus.Pending, Named(temp, jobId, "Fixture Lamplighter").Status);
    }

    [Fact]
    public void A_linked_feature_edited_before_the_writes_refuses_the_accept_and_the_edit_survives()
    {
        // Task 8a fix round 1 (M1): the status alone is not enough; an edit leaves it Pending but the stale row would be saved Accepted.
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        var beacon = Named(temp, jobId, "Fixture Beacon");
        temp.App.BeforeAcceptWrites = () => temp.App.Store.SaveImportCandidate(beacon with { Edited = beacon.Current with { Summary = "Edited by a second window." } });

        var refused = Refused(() => temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both)));

        // R134: its own code, not `import.candidate-changed` (the class itself did not change), and its message names no feature.
        Assert.Equal(["candidate.linked-edited"], Codes(refused));
        Assert.DoesNotContain("Fixture", refused.Problems[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == sourceId);
        var stored = Named(temp, jobId, "Fixture Beacon");
        Assert.Equal(CandidateStatus.Pending, stored.Status);
        Assert.Equal("Edited by a second window.", stored.Current.Summary);
        Assert.Equal(CandidateStatus.Pending, Named(temp, jobId, "Fixture Lamplighter").Status);
    }

    [Fact]
    public void A_class_edited_before_the_writes_is_refused_as_changed_and_nothing_is_written()
    {
        // Wave 0/1 minors (d): the accept acts on the stored class at commit time, with the same compare-and-swap as an edit (R120).
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        temp.App.BeforeAcceptWrites = () => temp.App.Store.SaveImportCandidate(lamplighter with { Edited = lamplighter.Current with { Summary = "Edited by a second window." } });

        var refused = Refused(() => temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both)));

        Assert.Equal(["import.candidate-changed"], Codes(refused));
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == sourceId);
        var stored = Named(temp, jobId, "Fixture Lamplighter");
        Assert.Equal(CandidateStatus.Pending, stored.Status);
        Assert.Equal("Edited by a second window.", stored.Current.Summary);
    }

    [Fact]
    public void A_class_ignored_before_the_writes_is_refused_as_reviewed_and_nothing_is_written()
    {
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        temp.App.BeforeAcceptWrites = () => temp.App.Store.SaveImportCandidate(lamplighter with { Status = CandidateStatus.Ignored });

        var refused = Refused(() => temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both)));

        Assert.Equal(["candidate.already-reviewed"], Codes(refused));
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == sourceId);
        Assert.Equal(CandidateStatus.Ignored, Named(temp, jobId, "Fixture Lamplighter").Status);
    }

    [Fact]
    public void Confirmed_effect_ids_are_a_set_so_a_repeated_id_is_accepted_once()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");

        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: ["hit-die", "spellcasting", "hit-die", "save-wis", "save-wis"]));

        var draft = temp.App.Store.FindRevision(accepted.Draft!)!;
        Assert.Single(draft.Effects, e => e.Id == "hit-die");
        Assert.Single(draft.Effects, e => e.Id == "save-wis");
        Assert.All(draft.Effects.Where(e => e.Id is "hit-die" or "spellcasting" or "save-wis"), e => Assert.Equal(AutomationStatus.Automatic, e.Automation));
        Assert.Equal(AutomationStatus.Reference, draft.Effects.Single(e => e.Id == "save-cha").Automation);
    }

    [Fact]
    public void A_bulk_accept_never_accepts_a_class_with_everything_reference()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");

        var result = temp.AcceptMany(new([lamplighter.Id], Confirm: true));

        Assert.Empty(result.Accepted);
        Assert.Equal(["import.class-unconfirmed"], Assert.Single(result.Blocked).Codes);
        Assert.DoesNotContain(temp.App.Store.ListRevisions(), r => r.Provenance.SourceId == lamplighter.Candidate.SourceId);
        Assert.All(temp.App.ListCandidates(new(jobId)), c => Assert.Equal(CandidateStatus.Pending, c.Status));
        // As reference it is still accepted in bulk.
        Assert.Single(temp.AcceptMany(new([lamplighter.Id], AsReference: true, Confirm: true)).Accepted);
    }

    [Fact]
    public void A_two_family_source_answers_once_on_the_class_and_the_stubs_take_it()
    {
        var (temp, _, jobId) = Imported(oneFamily: false);
        using var _t = temp;
        var lamplighter = Reviewed(temp, jobId, "Fixture Lamplighter");
        Assert.Contains(temp.App.CheckCandidate(lamplighter.Id).Blockers, b => b.Code == "import.family-needed");

        temp.App.EditCandidate(new(lamplighter.Id, Expected: temp.EtagOf(lamplighter.Id), RulesFamilies: [RulesFamilies.Srd521]));
        var check = temp.App.CheckCandidate(lamplighter.Id);
        Assert.True(check.CanAccept, string.Join("; ", check.Blockers.Concat(check.Report.Errors).Select(d => $"{d.Code} {d.Message}")));

        var accepted = temp.Accept(new(lamplighter.Id, Confirm: true, ConfirmedEffectIds: Both));
        var drafts = temp.App.Store.FindRevision(accepted.Draft!)!.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content).Select(g => temp.App.Store.FindRevision(g.Content!)!).ToList();
        Assert.All(drafts, d => Assert.Equal([RulesFamilies.Srd521], d.RulesFamilies));
    }

    [Fact]
    public void A_class_and_the_feature_drafts_it_pins_publish_together()
    {
        var (temp, sourceId, jobId) = Imported();
        using var _t = temp;
        var accepted = temp.Accept(new(Reviewed(temp, jobId, "Fixture Lamplighter").Id, Confirm: true, ConfirmedEffectIds: Both));

        var drafts = temp.App.ListContent(RulesFamilies.Srd521).Count(o => o.SourceId == sourceId);
        Assert.Equal(0, drafts); // drafts are not active
        var ids = temp.App.Store.ListRevisions().Where(r => r.Provenance.SourceId == sourceId).Select(r => r.ContentId).ToList();
        var preview = temp.App.PreviewPublishMany(ids);
        Assert.True(preview.CanPublish);
        var result = temp.App.PublishMany(new(ids, [.. preview.Entries.Select(e => e.Draft.RevisionId)], true));

        Assert.Equal(8, result.Published.Count);
        var published = temp.App.Store.ListRevisions().Where(r => r.Provenance.SourceId == sourceId && r.Status == RevisionStatus.Published).ToList();
        var lamplighter = published.Single(r => r.Name == "Fixture Lamplighter");
        var pinned = lamplighter.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content).Select(g => g.Content!).ToList();
        Assert.Equal(7, pinned.Count);
        Assert.All(pinned, p => Assert.Contains(published, r => r.Reference == p)); // re-pointed at the published feature revisions
        Assert.NotNull(accepted.Draft);
    }
}
