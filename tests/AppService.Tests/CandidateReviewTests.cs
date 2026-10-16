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
    /// <summary>
    /// The fixture book's candidates: page 2 two, page 3 two, page 4 four, page 5 the Warden and its two feature stubs, page 7 the
    /// Lamplighter and the six features its table names that are not installed (Fixture Glow is a block of the book, the other five
    /// are stubs; "Spellcasting" is installed SRD content, so it gets no stub).
    /// </summary>
    private const int PageSevenCandidates = 7;

    private const int ExpectedCandidates = 2 + 2 + 4 + 3 + PageSevenCandidates;

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
    public void The_list_pages_in_page_then_name_order_and_the_total_counts_the_filtered_set()
    {
        var (temp, _, jobId) = Imported();
        using var _ = temp;
        var everything = temp.App.ListCandidates(new(jobId)).Select(c => c.Id).ToList();
        var total = everything.Count;
        Assert.Equal(ExpectedCandidates, total);

        var pages = Enumerable.Range(0, (total + 3) / 4).Select(i => temp.App.ListCandidatesPage(new(jobId, Offset: i * 4, Limit: 4))).ToList();
        var past = temp.App.ListCandidatesPage(new(jobId, Offset: total, Limit: 4));

        Assert.Equal(total, pages[0].Total);
        Assert.Equal(everything, [.. pages.SelectMany(p => p.Items).Select(c => c.Id)]);
        Assert.Equal([.. Enumerable.Repeat(4, total / 4), .. total % 4 == 0 ? [] : new[] { total % 4 }], pages.Select(p => p.Items.Count));
        Assert.Equal((0, total), (past.Items.Count, past.Total));

        var spells = temp.App.ListCandidatesPage(new(jobId, Kind: ContentKind.Spell, Offset: 1, Limit: 1));
        Assert.Equal(2, spells.Total); // the filtered set, not the job
        Assert.Equal(["Fixture Frost Veil"], spells.Items.Select(c => c.Current.ProposedName));

        // No paging asked for: the first 50 (all of them here).
        var defaults = temp.App.ListCandidatesPage(new(jobId));
        Assert.Equal((total, total), (defaults.Items.Count, defaults.Total));
    }

    [Fact]
    public void A_page_range_keeps_candidates_that_touch_it_and_page_is_the_same_as_both_ends()
    {
        var (temp, _, jobId) = Imported();
        using var _ = temp;

        Assert.Equal(2, temp.App.ListCandidatesPage(new(jobId, LastPage: 2)).Total);
        Assert.Equal(ExpectedCandidates - 4, temp.App.ListCandidatesPage(new(jobId, FirstPage: 4)).Total); // all but pages 2 and 3
        Assert.Equal(6, temp.App.ListCandidatesPage(new(jobId, FirstPage: 3, LastPage: 4)).Total);
        Assert.Equal(PageSevenCandidates, temp.App.ListCandidatesPage(new(jobId, FirstPage: 6)).Total); // page 6 has no text; page 7 is the class page
        Assert.Equal(0, temp.App.ListCandidatesPage(new(jobId, FirstPage: 8)).Total);
        Assert.Equal(0, temp.App.ListCandidatesPage(new(jobId, FirstPage: 4, LastPage: 3)).Total);
        Assert.Equal(
            temp.App.ListCandidatesPage(new(jobId, FirstPage: 4, LastPage: 4)).Items.Select(c => c.Id),
            temp.App.ListCandidatesPage(new(jobId, Page: 4)).Items.Select(c => c.Id));
        Assert.Equal(4, temp.App.ListCandidates(new(jobId, Page: 4)).Count);
        // A range plus a kind: both apply.
        Assert.Equal(1, temp.App.ListCandidatesPage(new(jobId, FirstPage: 3, LastPage: 4, Kind: ContentKind.Feat)).Total);
    }

    [Fact]
    public void A_limit_over_200_or_a_bad_offset_is_refused_and_nothing_is_read()
    {
        var (temp, _, jobId) = Imported();
        using var _ = temp;

        Assert.Equal(ExpectedCandidates, temp.App.ListCandidatesPage(new(jobId, Limit: 200)).Items.Count); // 200 is allowed
        Assert.Equal("import.limit-out-of-range", Code(() => temp.App.ListCandidatesPage(new(jobId, Limit: 201))));
        Assert.Equal("import.limit-out-of-range", Code(() => temp.App.ListCandidatesPage(new(jobId, Limit: 0))));
        Assert.Equal("import.offset-out-of-range", Code(() => temp.App.ListCandidatesPage(new(jobId, Offset: -1))));
    }

    [Fact]
    public void The_candidates_command_returns_items_and_total()
    {
        var (temp, _, jobId) = Imported();
        using var _ = temp;
        Assert.Contains("import.candidates", CommandDispatcher.Commands);

        using var response = System.Text.Json.JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(System.Text.Json.JsonSerializer.Serialize(
            new { id = "1", command = "import.candidates", payload = new { jobId, kind = "spell", offset = 1, limit = 1 } }, RulesJson.Compact)));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        var data = response.RootElement.GetProperty("result");
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.Equal(1, data.GetProperty("items").GetArrayLength());
    }

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
    public void Editing_clears_the_unsure_fields_and_a_class_whose_names_are_all_linked_can_then_be_accepted()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var warden = Named(temp, jobId, "Fixture Warden");
        Assert.Contains(temp.App.CheckCandidate(warden.Id).Blockers, b => b.Code == "candidate.low-confidence");

        // 2026-10-14 item 3: a two-family source gives the class no family, so the reviewer sets one.
        var edited = temp.App.EditCandidate(new(warden.Id, Name: "Fixture Warden (edited)", RulesFamilies: [RulesFamilies.Srd521], Effects: [new HitDieEffect { Id = "hit-die", Die = 10 }]));
        Assert.Empty(edited.Current.LowConfidenceFields);
        Assert.Empty(edited.Current.UnresolvedReferences); // item 11: the two names are feature stubs of the job now, linked from the class
        Assert.Equal("Fixture Warden", edited.Candidate.ProposedName); // the detected version stays as it was

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

    [Fact]
    public void An_edit_naming_a_family_the_source_does_not_declare_is_refused_and_nothing_is_saved()
    {
        // Review Focus 4: a stale or hostile client sends the command directly.
        var temp = new TempApp();
        using var _t = temp;
        var source = temp.App.CreateHomebrewSource(new("Test One Family Book", [RulesFamilies.Srd51]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = temp.App.StartImport(new(source.Id, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        var coat = Named(temp, job.Id, "Fixture Quilted Coat");

        var refused = Assert.Throws<AppValidationException>(() => temp.App.EditCandidate(new(coat.Id, Name: "Fixture Other", RulesFamilies: [RulesFamilies.Srd521])));

        Assert.Equal("import.family-not-in-source", refused.Problems[0].Code);
        Assert.All(refused.Problems, p => Assert.DoesNotContain("Fixture", p.Message, StringComparison.Ordinal));
        var after = Named(temp, job.Id, "Fixture Quilted Coat");
        Assert.Null(after.Edited);
        Assert.Equal([RulesFamilies.Srd51], after.Current.RulesFamilies);
        Assert.Equal("Fixture Quilted Coat", after.Current.ProposedName);
    }

    [Fact]
    public void A_duplicated_family_list_is_stored_once()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var coat = Named(temp, jobId, "Fixture Quilted Coat");

        var edited = temp.App.EditCandidate(new(coat.Id, RulesFamilies: [RulesFamilies.Srd51, RulesFamilies.Srd51, RulesFamilies.Srd521, RulesFamilies.Srd51]));

        Assert.Equal([RulesFamilies.Srd51, RulesFamilies.Srd521], edited.Current.RulesFamilies);
    }

    /// <summary>A 0.5.0 job's armour row: it was stored with a silent srd-5.2.1 on a two-family source (ruling R28).</summary>
    private static StoredCandidate AsKeptBy050(TempApp temp, StoredCandidate stored)
    {
        var old = stored with { Candidate = stored.Candidate with { RulesFamilies = [RulesFamilies.Srd521], LowConfidenceFields = [], Uncertainties = [] } };
        temp.App.Store.SaveImportCandidate(old);
        return old;
    }

    [Fact]
    public void An_old_jobs_pending_armour_and_class_table_ask_for_a_family_again()
    {
        var (temp, _, jobId) = Imported(); // no writtenFor, like a 0.5.0 job, on a two-family source
        using var _t = temp;
        var coat = AsKeptBy050(temp, Named(temp, jobId, "Fixture Quilted Coat"));
        var warden = AsKeptBy050(temp, Named(temp, jobId, "Fixture Warden"));
        Assert.Equal([RulesFamilies.Srd521], coat.Current.RulesFamilies);

        foreach (var old in new[] { coat, warden })
        {
            var check = temp.App.CheckCandidate(old.Id);
            Assert.Contains(check.Blockers, b => b.Code == "import.family-needed");
            Assert.False(check.CanAccept);
            Assert.False(check.CanAcceptAsReference);
            Assert.Equal("import.family-needed", Code(() => temp.App.AcceptCandidate(new(old.Id, Confirm: true))));
            Assert.Equal("import.family-needed", Code(() => temp.App.AcceptCandidate(new(old.Id, AsReference: true, Confirm: true))));
        }

        // A layout-decided kind in the same job is not asked: its family came from the page.
        Assert.DoesNotContain(temp.App.CheckCandidate(Named(temp, jobId, "Fixture Ember Lance").Id).Blockers, b => b.Code == "import.family-needed");

        // Saving an edit is the reviewer's answer.
        temp.App.EditCandidate(new(coat.Id, RulesFamilies: [RulesFamilies.Srd521]));
        var ready = temp.App.CheckCandidate(coat.Id);
        Assert.DoesNotContain(ready.Blockers, b => b.Code == "import.family-needed");
        Assert.Equal(CandidateStatus.Accepted, temp.App.AcceptCandidate(new(coat.Id, Confirm: true)).Status);
    }

    [Fact]
    public void An_old_armour_row_edited_in_050_still_asks_for_a_family_until_one_is_saved()
    {
        // Ruling R29: 0.5.0 never asked about families, so its edit (Edited set, no familiesReviewed) is not an answer.
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var coat = AsKeptBy050(temp, Named(temp, jobId, "Fixture Quilted Coat"));
        var edited050 = coat with { Edited = coat.Candidate with { ProposedName = "Fixture Quilted Coat" } };
        temp.App.Store.SaveImportCandidate(edited050);
        Assert.False(temp.App.Store.FindImportCandidate(coat.Id)!.FamiliesReviewed);

        Assert.Contains(temp.App.CheckCandidate(coat.Id).Blockers, b => b.Code == "import.family-needed");
        Assert.Equal("import.family-needed", Code(() => temp.App.AcceptCandidate(new(coat.Id, Confirm: true))));

        // An edit that sends no families is not an answer either.
        var renamed = temp.App.EditCandidate(new(coat.Id, Summary: "Fixture padding."));
        Assert.False(renamed.FamiliesReviewed);
        Assert.Contains(temp.App.CheckCandidate(coat.Id).Blockers, b => b.Code == "import.family-needed");

        // Saving a family (even the one it already has) is.
        var saved = temp.App.EditCandidate(new(coat.Id, RulesFamilies: [RulesFamilies.Srd521]));
        Assert.True(saved.FamiliesReviewed);
        Assert.True(temp.App.Store.FindImportCandidate(coat.Id)!.FamiliesReviewed);
        Assert.DoesNotContain(temp.App.CheckCandidate(coat.Id).Blockers, b => b.Code == "import.family-needed");
        Assert.Equal(CandidateStatus.Accepted, temp.App.AcceptCandidate(new(coat.Id, Confirm: true)).Status);
    }

    [Fact]
    public void Families_reviewed_is_absent_from_the_stored_json_until_true_and_reads_back_false_when_absent()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var stored = Named(temp, jobId, "Fixture Quilted Coat");

        var json = System.Text.Json.JsonSerializer.Serialize(stored, RulesJson.Compact);
        Assert.DoesNotContain("familiesReviewed", json);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<StoredCandidate>(json, RulesJson.Compact)!.FamiliesReviewed);

        var reviewed = System.Text.Json.JsonSerializer.Serialize(stored with { FamiliesReviewed = true }, RulesJson.Compact);
        Assert.Contains("\"familiesReviewed\":true", reviewed);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<StoredCandidate>(reviewed, RulesJson.Compact)!.FamiliesReviewed);
    }

    [Fact]
    public void A_class_stores_the_features_it_grants_by_candidate_and_level_and_every_other_row_stores_none()
    {
        // Item 11 (R39): grantsCandidates is omitted when empty; a row stored before it existed reads as no links; no version change.
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var lamplighter = Named(temp, jobId, "Fixture Lamplighter");
        var all = temp.App.ListCandidates(new(jobId)).ToDictionary(c => c.Id);

        var grants = lamplighter.GrantsCandidates!;
        Assert.Equal(8, grants.Count); // nine links in the table, less the installed "Spellcasting"
        Assert.Equal([1, 2, 5, 7, 9, 11, 15, 20], grants.Select(g => g.Level));
        Assert.All(grants, g => Assert.Equal(ContentKind.Feature, all[g.CandidateId].Candidate.ProposedKind));
        Assert.Equal(1, StoredCandidate.CurrentSchemaVersion);
        Assert.Equal(["Fixture Focus", "Fixture Resolve", "Fixture Stormcall"], Named(temp, jobId, "Fixture Warden").GrantsCandidates!.Select(g => all[g.CandidateId].Candidate.ProposedName));

        var json = System.Text.Json.JsonSerializer.Serialize(lamplighter, RulesJson.Compact);
        Assert.Contains("\"grantsCandidates\":[{\"candidateId\":", json);
        Assert.Equal(grants, System.Text.Json.JsonSerializer.Deserialize<StoredCandidate>(json, RulesJson.Compact)!.GrantsCandidates);

        var plain = System.Text.Json.JsonSerializer.Serialize(Named(temp, jobId, "Fixture Quilted Coat"), RulesJson.Compact);
        Assert.DoesNotContain("grantsCandidates", plain);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<StoredCandidate>(plain, RulesJson.Compact)!.GrantsCandidates);

        // An edit keeps the links.
        Assert.Equal(grants, temp.App.EditCandidate(new(lamplighter.Id, Summary: "Edited text.")).GrantsCandidates);
    }

    [Fact]
    public void A_job_that_recorded_what_the_book_is_written_for_does_not_ask_again()
    {
        var temp = new TempApp();
        using var _t = temp;
        var source = temp.App.CreateHomebrewSource(new("Test Review Book", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = temp.App.StartImport(new(source.Id, WholeDocument: true, WrittenFor: [RulesFamilies.Srd521]));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        var coat = Named(temp, job.Id, "Fixture Quilted Coat");

        Assert.Equal([RulesFamilies.Srd521], coat.Current.RulesFamilies);
        var check = temp.App.CheckCandidate(coat.Id);
        Assert.DoesNotContain(check.Blockers, b => b.Code == "import.family-needed");
        Assert.True(check.CanAccept, string.Join("; ", check.Blockers.Concat(check.Report.Errors).Select(d => d.Code)));
    }

    [Fact]
    public void An_entry_accepted_from_another_reading_of_the_same_document_is_not_accepted_twice()
    {
        var (temp, sourceId, firstJob) = Imported();
        using var _t = temp;
        var second = temp.App.StartImport(new(sourceId, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        var ember = Named(temp, firstJob, "Fixture Ember Lance");
        var again = Named(temp, second.Id, "Fixture Ember Lance");
        var stormcall = Named(temp, firstJob, "Fixture Stormcall");
        var stormAgain = Named(temp, second.Id, "Fixture Stormcall");
        Assert.True(temp.App.CheckCandidate(again.Id).CanAccept);

        temp.App.AcceptCandidate(new(ember.Id, Confirm: true));
        temp.App.AcceptCandidate(new(stormcall.Id, AsReference: true, Confirm: true)); // a reference counts too

        foreach (var twin in new[] { again, stormAgain })
        {
            var check = temp.App.CheckCandidate(twin.Id);
            Assert.False(check.CanAccept);
            Assert.False(check.CanAcceptAsReference);
            var blocker = Assert.Single(check.Blockers, b => b.Code == "import.already-accepted");
            Assert.Equal("This entry was already accepted from another reading of this document.", blocker.Message);
            Assert.Equal("import.already-accepted", Code(() => temp.App.AcceptCandidate(new(twin.Id, Confirm: true))));
            Assert.Equal("import.already-accepted", Code(() => temp.App.AcceptCandidate(new(twin.Id, AsReference: true, Confirm: true))));
        }
        Assert.Equal(1, temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft && r.Name == "Fixture Ember Lance"));

        // Nothing stored changed: the other reading's candidates are still pending, and ignoring one is still allowed.
        Assert.Equal(CandidateStatus.Pending, Named(temp, second.Id, "Fixture Ember Lance").Status);
        Assert.Equal(CandidateStatus.Ignored, temp.App.IgnoreCandidate(again.Id).Status);
    }

    [Fact]
    public void Renaming_a_twin_lifts_the_already_accepted_blocker_for_a_deliberate_second_copy()
    {
        var (temp, sourceId, firstJob) = Imported();
        using var _t = temp;
        var second = temp.App.StartImport(new(sourceId, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        temp.App.AcceptCandidate(new(Named(temp, firstJob, "Fixture Ember Lance").Id, Confirm: true));
        var twin = Named(temp, second.Id, "Fixture Ember Lance");
        Assert.Contains(temp.App.CheckCandidate(twin.Id).Blockers, b => b.Code == "import.already-accepted");

        temp.App.EditCandidate(new(twin.Id, Name: "Fixture Ember Lance Copy"));

        var check = temp.App.CheckCandidate(twin.Id);
        Assert.DoesNotContain(check.Blockers, b => b.Code == "import.already-accepted");
        Assert.Equal(CandidateStatus.Accepted, temp.App.AcceptCandidate(new(twin.Id, Confirm: true)).Status);
        Assert.Equal(1, temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft && r.Name == "Fixture Ember Lance"));
        Assert.Equal(1, temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft && r.Name == "Fixture Ember Lance Copy"));
    }

    [Fact]
    public void Accept_waits_for_a_family_on_a_two_family_source()
    {
        // Review Focus 1.
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var coat = Named(temp, jobId, "Fixture Quilted Coat");
        Assert.Empty(coat.Current.RulesFamilies);

        var check = temp.App.CheckCandidate(coat.Id);
        Assert.False(check.CanAccept);
        Assert.Contains(check.Blockers, b => b.Code == "import.family-needed");
        Assert.False(check.CanAcceptAsReference); // a reference entry needs a family too
        Assert.Equal("import.family-needed", Code(() => temp.App.AcceptCandidate(new(coat.Id, AsReference: true, Confirm: true))));
        var refused = Assert.Throws<AppValidationException>(() => temp.App.AcceptCandidate(new(coat.Id, Confirm: true)));
        Assert.Equal("import.family-needed", refused.Problems[0].Code);

        // Saving an edit that leaves the family empty is allowed (other fields can be fixed first); accept still waits.
        var renamed = temp.App.EditCandidate(new(coat.Id, Name: "Fixture Quilted Coat", RulesFamilies: []));
        Assert.Empty(renamed.Current.RulesFamilies);
        Assert.Contains(temp.App.CheckCandidate(coat.Id).Blockers, b => b.Code == "import.family-needed");
        Assert.Equal("import.family-needed", Code(() => temp.App.AcceptCandidate(new(coat.Id, Confirm: true))));

        temp.App.EditCandidate(new(coat.Id, RulesFamilies: [RulesFamilies.Srd51]));
        var ready = temp.App.CheckCandidate(coat.Id);
        Assert.True(ready.CanAccept, string.Join("; ", ready.Blockers.Concat(ready.Report.Errors).Select(d => d.Code)));
        var draft = temp.App.Store.FindRevision(temp.App.AcceptCandidate(new(coat.Id, Confirm: true)).Draft!)!;
        Assert.Equal([RulesFamilies.Srd51], draft.RulesFamilies);
    }

    [Fact]
    public void Accept_many_accepts_the_first_of_two_twins_from_two_readings_and_blocks_the_second()
    {
        // Fix round 1 (item 6): the accepted identities are read once per document and grow as the request goes (R27).
        var (temp, sourceId, firstJob) = Imported();
        using var _t = temp;
        var second = temp.App.StartImport(new(sourceId, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        var first = Named(temp, firstJob, "Fixture Ember Lance");
        var twin = Named(temp, second.Id, "Fixture Ember Lance");

        var result = temp.App.AcceptMany(new([first.Id, twin.Id], Confirm: true));

        Assert.Equal([first.Id], result.Accepted.Select(c => c.Id));
        Assert.Equal(["import.already-accepted"], result.Blocked.Single(b => b.CandidateId == twin.Id).Codes);
        Assert.Equal(1, temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft && r.Name == "Fixture Ember Lance"));
        Assert.Equal(CandidateStatus.Pending, temp.App.Store.FindImportCandidate(twin.Id)!.Status);

        // Renaming the twin still lifts it (the twin side uses the effective values).
        temp.App.EditCandidate(new(twin.Id, Name: "Fixture Ember Lance Copy"));
        Assert.Equal([twin.Id], temp.App.AcceptMany(new([twin.Id], Confirm: true)).Accepted.Select(c => c.Id));
    }

    [Fact]
    public void Accept_many_writes_the_clean_ones_in_one_go_and_lists_the_blocked_with_their_codes()
    {
        // Item 9, D33: blocked ones are returned, never forced.
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var ember = Named(temp, jobId, "Fixture Ember Lance");
        var stormcall = Named(temp, jobId, "Fixture Stormcall"); // an unresolved reference
        var coat = Named(temp, jobId, "Fixture Quilted Coat"); // no family
        var drafts = temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft);

        var result = temp.App.AcceptMany(new([ember.Id, stormcall.Id, coat.Id, ember.Id], AsReference: false, Confirm: true));

        Assert.Equal([ember.Id], result.Accepted.Select(c => c.Id));
        Assert.Equal(CandidateStatus.Accepted, result.Accepted[0].Status);
        Assert.Equal(drafts + 1, temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft));
        Assert.Equal([stormcall.Id, coat.Id], result.Blocked.Select(b => b.CandidateId));
        Assert.Contains("candidate.unresolved-reference", result.Blocked[0].Codes);
        Assert.Contains("import.family-needed", result.Blocked[1].Codes);
        Assert.Equal(CandidateStatus.Pending, temp.App.Store.FindImportCandidate(stormcall.Id)!.Status);
        Assert.Equal(CandidateStatus.Accepted, temp.App.Store.FindImportCandidate(ember.Id)!.Status);
        Assert.True(temp.App.Store.FindSource(temp.App.Store.FindRevision(result.Accepted[0].Draft!)!.Provenance.SourceId)!.ImportDerived);

        // Asking again: the accepted one is now blocked as already reviewed, nothing is duplicated.
        var again = temp.App.AcceptMany(new([ember.Id], AsReference: false, Confirm: true));
        Assert.Empty(again.Accepted);
        Assert.Equal(["candidate.already-reviewed"], again.Blocked.Single().Codes);
        Assert.Equal(drafts + 1, temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft));
    }

    [Fact]
    public void Accept_many_as_reference_lifts_the_review_blockers_but_not_the_provenance_ones()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var stormcall = Named(temp, jobId, "Fixture Stormcall");
        var coat = Named(temp, jobId, "Fixture Quilted Coat");

        var result = temp.App.AcceptMany(new([stormcall.Id, coat.Id], AsReference: true, Confirm: true));

        Assert.Equal([stormcall.Id], result.Accepted.Select(c => c.Id));
        Assert.Equal(CandidateStatus.AcceptedAsReference, result.Accepted[0].Status);
        Assert.Empty(temp.App.Store.FindRevision(result.Accepted[0].Draft!)!.Effects);
        Assert.Equal(["import.family-needed"], result.Blocked.Single().Codes);
    }

    [Fact]
    public void Accept_many_needs_the_confirmation_a_list_and_at_most_200_and_writes_nothing_otherwise()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var ember = Named(temp, jobId, "Fixture Ember Lance");
        var drafts = temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft);

        Assert.Equal("candidate.confirmation-required", Code(() => temp.App.AcceptMany(new([ember.Id]))));
        Assert.Equal("import.limit-out-of-range", Code(() => temp.App.AcceptMany(new([.. Enumerable.Range(0, 201).Select(_ => Guid.NewGuid())], Confirm: true))));
        Assert.Equal("import.limit-out-of-range", Code(() => temp.App.AcceptMany(new([], Confirm: true))));
        Assert.Equal(drafts, temp.App.Store.ListRevisions().Count(r => r.Status == RevisionStatus.Draft));
        Assert.Equal(CandidateStatus.Pending, temp.App.Store.FindImportCandidate(ember.Id)!.Status);

        // A candidate that does not exist is blocked, not thrown: the rest still go through.
        var ghost = Guid.NewGuid();
        var result = temp.App.AcceptMany(new([ghost, ember.Id], Confirm: true));
        Assert.Equal([ember.Id], result.Accepted.Select(c => c.Id));
        Assert.Equal(["candidate.not-found"], result.Blocked.Single().Codes);
    }

    [Fact]
    public void The_accept_many_command_returns_accepted_and_blocked()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        Assert.Contains("import.candidate.acceptMany", CommandDispatcher.Commands);
        var ember = Named(temp, jobId, "Fixture Ember Lance");
        var stormcall = Named(temp, jobId, "Fixture Stormcall");

        using var response = System.Text.Json.JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(System.Text.Json.JsonSerializer.Serialize(
            new { id = "1", command = "import.candidate.acceptMany", payload = new { candidateIds = new[] { ember.Id, stormcall.Id }, asReference = false, confirm = true } }, RulesJson.Compact)));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        var data = response.RootElement.GetProperty("result");
        Assert.Equal(1, data.GetProperty("accepted").GetArrayLength());
        var blocked = data.GetProperty("blocked");
        Assert.Equal(stormcall.Id, blocked[0].GetProperty("candidateId").GetGuid());
        Assert.Equal("candidate.unresolved-reference", blocked[0].GetProperty("codes")[0].GetString());
    }
}
