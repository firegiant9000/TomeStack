using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Tester feedback item 10 (rulings R43 to R45): an imported or homebrew spell reaches a caster only through a list key the
/// caster's class names exactly. "Add to spell list" edits pending spell candidates in bulk, saves new drafts of published
/// spells (never publishing), and the existing keys are read from published content. The original fixture book only.
/// </summary>
public class SpellListTests
{
    private static (TempApp Temp, Guid SourceId, Guid JobId) Imported()
    {
        var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Spell List Book", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        temp.App.AttachPdf(source.Id, "fixture-import.pdf", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "pdf", "fixture-import.pdf")));
        var job = temp.App.StartImport(new(source.Id, WholeDocument: true));
        temp.App.RunningImport?.Wait(TimeSpan.FromSeconds(30));
        return (temp, source.Id, job.Id);
    }

    private static StoredCandidate Named(TempApp temp, Guid jobId, string name) =>
        temp.App.ListCandidates(new(jobId)).Single(c => c.Candidate.ProposedName == name);

    private static string Code(Action action) => Assert.Throws<AppValidationException>(action).Problems[0].Code;

    private static IReadOnlyList<string> ListsOf(StoredCandidate stored) =>
        Assert.IsType<SpellEffect>(Assert.Single(stored.Current.ProposedEffects)).Lists;

    // ---- candidates (R43) ----

    [Fact]
    public void Adding_a_key_to_spell_candidates_saves_an_edit_that_keeps_the_other_unsure_fields()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = Named(temp, jobId, "Fixture Frost Veil");
        var ember = Named(temp, jobId, "Fixture Ember Lance");
        // The reviewer has not seen the range yet: it stays unsure, and a bulk list add must not clear it.
        temp.App.Store.SaveImportCandidate(veil with { Edited = veil.Candidate with { LowConfidenceFields = ["range", "lists"] } });

        var result = temp.App.AddCandidatesToSpellList(new([veil.Id, ember.Id], "  Wizard "));

        Assert.Equal("wizard", result.ListKey);
        Assert.Equal([veil.Id, ember.Id], result.Updated.Select(c => c.Id));
        Assert.Empty(result.Skipped);
        var savedVeil = temp.App.Store.FindImportCandidate(veil.Id)!;
        Assert.Equal(["wizard"], ListsOf(savedVeil));
        Assert.Equal(["range"], savedVeil.Current.LowConfidenceFields);
        Assert.Equal("wizard", savedVeil.Current.Fields["lists"]);
        Assert.NotNull(savedVeil.Edited);
        Assert.DoesNotContain(savedVeil.Current.Uncertainties, u => u.Contains("class lists", StringComparison.Ordinal));
        var savedEmber = temp.App.Store.FindImportCandidate(ember.Id)!;
        Assert.Equal(["fixture-mage", "wizard"], ListsOf(savedEmber)); // the header's list is a normalised key too (review fix 4)
        Assert.Equal(CandidateStatus.Pending, savedEmber.Status);
        Assert.Empty(Assert.IsType<SpellEffect>(Assert.Single(savedVeil.Candidate.ProposedEffects)).Lists); // the detected proposal never changes
    }

    [Fact]
    public void A_spell_candidate_with_a_list_and_no_other_unsure_field_can_then_be_accepted_with_the_key()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = Named(temp, jobId, "Fixture Frost Veil");

        temp.App.AddCandidatesToSpellList(new([veil.Id], "wizard"));
        var accepted = temp.App.AcceptCandidate(new(veil.Id, Confirm: true));

        var draft = temp.App.Store.FindRevision(accepted.Draft!)!;
        Assert.Equal(["wizard"], Assert.IsType<SpellEffect>(Assert.Single(draft.Effects)).Lists);
        Assert.Equal(RevisionStatus.Draft, draft.Status);
    }

    [Fact]
    public void Candidates_that_are_not_pending_spells_are_skipped_with_a_code_and_a_repeat_is_confirmed_not_skipped()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = Named(temp, jobId, "Fixture Frost Veil");
        var ember = Named(temp, jobId, "Fixture Ember Lance");
        var stormcall = Named(temp, jobId, "Fixture Stormcall");
        var ghost = Guid.NewGuid();
        temp.App.IgnoreCandidate(veil.Id);
        temp.App.AddCandidatesToSpellList(new([ember.Id], "fixture-mage"));
        // R59: the key is already there and every key is it, so the repeat is a confirmation (updated), not a skip.
        var again = temp.App.AddCandidatesToSpellList(new([ember.Id], "fixture-mage"));

        var result = temp.App.AddCandidatesToSpellList(new([veil.Id, stormcall.Id, ghost], "wizard"));

        Assert.Empty(result.Updated);
        Assert.Equal(
            [(veil.Id, "candidate.already-reviewed"), (stormcall.Id, "spell-list.not-a-spell"), (ghost, "candidate.not-found")],
            result.Skipped.Select(s => (s.CandidateId, Assert.Single(s.Codes))));
        Assert.Equal(ember.Id, Assert.Single(again.Updated).Id);
        Assert.Empty(again.Skipped);
        Assert.Empty(again.Notes);
        Assert.Equal(["fixture-mage"], ListsOf(temp.App.Store.FindImportCandidate(ember.Id)!));
        Assert.Empty(ListsOf(temp.App.Store.FindImportCandidate(veil.Id)!));
    }

    /// <summary>A candidate the way the list-page pass leaves it: a proposed key, marked unsure, with the note.</summary>
    private static StoredCandidate WithProposedKey(TempApp temp, StoredCandidate stored, string key)
    {
        var effect = Assert.IsType<SpellEffect>(Assert.Single(stored.Candidate.ProposedEffects));
        var proposed = stored.Candidate with
        {
            ProposedEffects = [effect with { Lists = [key] }],
            Fields = new Dictionary<string, string>(stored.Candidate.Fields) { ["lists"] = key },
            LowConfidenceFields = [.. stored.Candidate.LowConfidenceFields, "lists"],
            Uncertainties = [.. stored.Candidate.Uncertainties, $"Spell list '{key}' was read from page 5; check it."],
        };
        var saved = stored with { Candidate = proposed };
        temp.App.Store.SaveImportCandidate(saved);
        return saved;
    }

    [Fact]
    public void Confirming_the_key_the_detector_proposed_clears_the_mark_so_the_candidate_can_be_accepted()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = WithProposedKey(temp, Named(temp, jobId, "Fixture Frost Veil"), "fixture-seer");
        Assert.Contains("candidate.low-confidence", temp.App.CheckCandidate(veil.Id).Blockers.Select(b => b.Code));

        var result = temp.App.AddCandidatesToSpellList(new([veil.Id], "Fixture Seer"));

        Assert.Equal(veil.Id, Assert.Single(result.Updated).Id);
        Assert.Empty(result.Skipped);
        Assert.Empty(result.Notes);
        var saved = temp.App.Store.FindImportCandidate(veil.Id)!;
        Assert.Equal(["fixture-seer"], ListsOf(saved));
        Assert.DoesNotContain("lists", saved.Current.LowConfidenceFields);
        Assert.DoesNotContain(temp.App.CheckCandidate(veil.Id).Blockers, b => b.Code == "candidate.low-confidence");
        Assert.NotNull(temp.App.AcceptCandidate(new(veil.Id, Confirm: true)).Draft);
    }

    [Fact]
    public void Adding_another_key_keeps_the_mark_on_the_proposed_one_says_so_and_accepting_stays_blocked()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = WithProposedKey(temp, Named(temp, jobId, "Fixture Frost Veil"), "fixture-seer");

        var result = temp.App.AddCandidatesToSpellList(new([veil.Id], "wizard"));

        Assert.Equal(veil.Id, Assert.Single(result.Updated).Id);
        Assert.Empty(result.Skipped);
        var note = Assert.Single(result.Notes);
        Assert.Equal(veil.Id, note.CandidateId);
        Assert.Equal(["spell-list.other-lists-unreviewed"], note.Codes);
        var saved = temp.App.Store.FindImportCandidate(veil.Id)!;
        Assert.Equal(["fixture-seer", "wizard"], ListsOf(saved));
        Assert.Contains("lists", saved.Current.LowConfidenceFields);
        Assert.Contains("candidate.low-confidence", temp.App.CheckCandidate(veil.Id).Blockers.Select(b => b.Code));
        Assert.Throws<AppValidationException>(() => temp.App.AcceptCandidate(new(veil.Id, Confirm: true)));
    }

    [Fact]
    public void A_bad_key_or_a_bad_count_is_refused_and_nothing_is_written()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = Named(temp, jobId, "Fixture Frost Veil");

        Assert.Equal("spell-list.invalid-key", Code(() => temp.App.AddCandidatesToSpellList(new([veil.Id], "Seer's List"))));
        Assert.Equal("spell-list.invalid-key", Code(() => temp.App.AddCandidatesToSpellList(new([veil.Id], "   "))));
        Assert.Equal("spell-list.invalid-key", Code(() => temp.App.AddCandidatesToSpellList(new([veil.Id], new string('a', 61)))));
        Assert.Equal("import.limit-out-of-range", Code(() => temp.App.AddCandidatesToSpellList(new([], "wizard"))));
        Assert.Equal("import.limit-out-of-range", Code(() => temp.App.AddCandidatesToSpellList(new([.. Enumerable.Range(0, 201).Select(_ => Guid.NewGuid())], "wizard"))));
        Assert.Null(temp.App.Store.FindImportCandidate(veil.Id)!.Edited);
    }

    [Fact]
    public void The_audit_names_counts_only()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = Named(temp, jobId, "Fixture Frost Veil");

        temp.App.AddCandidatesToSpellList(new([veil.Id], "fixture-seer-list"));

        var entry = temp.App.ImportAudit(jobId).Last(e => e.Event == "candidate-edited");
        Assert.Equal("spell list added to 1 spell candidates", entry.Detail);
        Assert.DoesNotContain("Frost", entry.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture-seer-list", entry.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_candidate_command_is_dispatched()
    {
        var (temp, _, jobId) = Imported();
        using var _t = temp;
        var veil = Named(temp, jobId, "Fixture Frost Veil");
        Assert.Contains("import.candidate.addToSpellList", CommandDispatcher.Commands);

        using var response = JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(
            new { id = "1", command = "import.candidate.addToSpellList", payload = new { candidateIds = new[] { veil.Id }, listKey = "wizard" } }, RulesJson.Compact)));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        var data = response.RootElement.GetProperty("result");
        Assert.Equal("wizard", data.GetProperty("listKey").GetString());
        Assert.Equal(1, data.GetProperty("updated").GetArrayLength());
    }

    // ---- published and studio spells (R44) ----

    private static ContentRevision Spell(SourceRecord source, string name, params string[] lists) => new()
    {
        ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Spell, Name = name, RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = [new SpellEffect { Id = "spell", Level = 1, Lists = lists }],
    };

    private static ContentReference Published(TempApp temp, ContentRevision draft)
    {
        var saved = temp.App.SaveDraft(draft);
        temp.App.Publish(saved);
        return saved;
    }

    [Fact]
    public void Adding_a_key_to_published_spells_saves_new_drafts_and_never_publishes()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var ward = Published(temp, Spell(source, "Fixture Quick Ward"));
        var spark = Published(temp, Spell(source, "Fixture Spark", "fixture-extra"));
        var before = temp.App.Store.ListRevisions().Count;

        var result = temp.App.AddToSpellList(new([ward.ContentId, spark.ContentId], " Wizard"));

        Assert.Equal("wizard", result.ListKey);
        Assert.Equal([ward.ContentId, spark.ContentId], result.Drafts.Select(d => d.ContentId));
        Assert.Empty(result.Skipped);
        Assert.Equal(before + 2, temp.App.Store.ListRevisions().Count);
        Assert.Contains("keep the spell revisions", result.Note, StringComparison.Ordinal);
        var wardDraft = temp.App.Store.FindRevision(result.Drafts[0])!;
        Assert.Equal(RevisionStatus.Draft, wardDraft.Status);
        Assert.Equal(["wizard"], Assert.IsType<SpellEffect>(Assert.Single(wardDraft.Effects)).Lists);
        Assert.Equal(["fixture-extra", "wizard"], Assert.IsType<SpellEffect>(Assert.Single(temp.App.Store.FindRevision(result.Drafts[1])!.Effects)).Lists);
        // The published revisions are untouched; only drafts were added.
        var publishedWard = temp.App.Store.ListRevisions(ward.ContentId).Single(r => r.Status == RevisionStatus.Published);
        Assert.Empty(Assert.IsType<SpellEffect>(Assert.Single(publishedWard.Effects)).Lists);
        Assert.All(result.Drafts, d => Assert.Equal(RevisionStatus.Draft, temp.App.Store.FindRevision(d)!.Status));
    }

    [Fact]
    public void A_spell_that_was_never_published_starts_from_its_newest_draft_and_a_key_it_has_is_skipped()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var first = temp.App.SaveDraft(Spell(source, "Fixture Quick Ward"));
        var newer = temp.App.SaveDraft(temp.App.Store.FindRevision(first)! with
        {
            RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Effects = [new SpellEffect { Id = "spell", Level = 1, Lists = ["fixture-extra"] }],
        });

        var result = temp.App.AddToSpellList(new([first.ContentId], "wizard"));

        Assert.Equal(["fixture-extra", "wizard"], Assert.IsType<SpellEffect>(Assert.Single(temp.App.Store.FindRevision(result.Drafts.Single())!.Effects)).Lists);
        Assert.NotEqual(newer.RevisionId, result.Drafts[0].RevisionId);

        var again = temp.App.AddToSpellList(new([first.ContentId], "wizard"));
        Assert.Empty(again.Drafts);
        Assert.Equal(["spell-list.already-on-list"], Assert.Single(again.Skipped).Codes);
    }

    [Fact]
    public void A_published_spell_with_a_newer_draft_is_skipped_so_the_drafts_other_edits_are_not_published_by_surprise()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var published = Published(temp, Spell(source, "Fixture Quick Ward"));
        // R60, case 1: the newer draft has other, unreviewed edits (a changed level). A second draft from it would carry them
        // into the publish panel ticked.
        temp.App.SaveDraft(temp.App.Store.FindRevision(published)! with
        {
            RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Effects = [new SpellEffect { Id = "spell", Level = 3, Lists = ["fixture-extra"] }],
        });
        var before = temp.App.Store.ListRevisions().Count;

        var result = temp.App.AddToSpellList(new([published.ContentId], "wizard"));

        Assert.Empty(result.Drafts);
        Assert.Equal(["spell-list.has-unpublished-draft"], Assert.Single(result.Skipped).Codes);
        Assert.Equal(before, temp.App.Store.ListRevisions().Count); // no D2
    }

    [Fact]
    public void A_key_that_is_only_on_the_newer_draft_does_not_count_as_already_on_the_list()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var published = Published(temp, Spell(source, "Fixture Quick Ward"));
        temp.App.SaveDraft(temp.App.Store.FindRevision(published)! with
        {
            RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Effects = [new SpellEffect { Id = "spell", Level = 1, Lists = ["wizard"] }],
        });

        // R60, case 2: the published spell is NOT on the list; saying "already on this list" would be false.
        var result = temp.App.AddToSpellList(new([published.ContentId], "wizard"));

        Assert.Equal(["spell-list.has-unpublished-draft"], Assert.Single(result.Skipped).Codes);
        Assert.Empty(result.Drafts);
    }

    [Fact]
    public void A_mixed_request_with_a_bundled_spell_is_refused_whole_and_the_revision_count_is_unchanged()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var ward = Published(temp, Spell(source, "Fixture Quick Ward"));
        var bundled = temp.App.Store.ListRevisions().First(r => r.Kind == ContentKind.Spell && r.Status == RevisionStatus.Published && r.Provenance.SourceId != source.Id);
        var before = temp.App.Store.ListRevisions().Count;

        Assert.Equal("content.source-not-editable", Code(() => temp.App.AddToSpellList(new([ward.ContentId, bundled.ContentId], "fixture-seer"))));

        Assert.Equal(before, temp.App.Store.ListRevisions().Count); // the homebrew spell's draft, made first, was rolled back
    }

    [Fact]
    public void The_new_drafts_stored_hash_is_that_of_its_own_json_and_the_published_revision_is_unchanged()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var ward = Published(temp, Spell(source, "Fixture Quick Ward"));
        var publishedRevision = temp.App.Store.ListRevisions(ward.ContentId).Single(r => r.Status == RevisionStatus.Published);
        var publishedHash = temp.App.Store.RevisionHash(publishedRevision.RevisionId);

        var draft = temp.App.AddToSpellList(new([ward.ContentId], "wizard")).Drafts.Single();

        var stored = temp.App.Store.FindRevision(draft)!;
        var json = JsonSerializer.Serialize(stored, RulesJson.Compact);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        Assert.Equal(expected, temp.App.Store.RevisionHash(draft.RevisionId));
        Assert.NotEqual(publishedHash, temp.App.Store.RevisionHash(draft.RevisionId));
        Assert.Equal(publishedHash, temp.App.Store.RevisionHash(publishedRevision.RevisionId));
    }

    [Fact]
    public void Content_that_is_not_a_spell_or_does_not_exist_is_skipped_and_bad_requests_are_refused()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var feat = temp.App.SaveDraft(new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Feat, Name = "Fixture Knack", RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id), Status = RevisionStatus.Draft, Effects = [],
        });
        var ghost = Guid.NewGuid();

        var result = temp.App.AddToSpellList(new([feat.ContentId, ghost], "wizard"));

        Assert.Empty(result.Drafts);
        Assert.Equal([(feat.ContentId, "spell-list.not-a-spell"), (ghost, "content.not-found")], result.Skipped.Select(s => (s.ContentId, Assert.Single(s.Codes))));
        Assert.Equal("spell-list.invalid-key", Code(() => temp.App.AddToSpellList(new([feat.ContentId], "No Good!"))));
        Assert.Equal("content.limit-out-of-range", Code(() => temp.App.AddToSpellList(new([], "wizard"))));
        Assert.Equal("content.limit-out-of-range", Code(() => temp.App.AddToSpellList(new([.. Enumerable.Range(0, TomeStackApp.MaxPublishMany + 1).Select(_ => Guid.NewGuid())], "wizard"))));
    }

    [Fact]
    public void A_bundled_spell_is_refused_and_nothing_is_written()
    {
        using var temp = new TempApp();
        var bundled = temp.App.Store.ListRevisions().First(r => r.Kind == ContentKind.Spell && r.Status == RevisionStatus.Published);
        var before = temp.App.Store.ListRevisions().Count;

        Assert.Equal("content.source-not-editable", Code(() => temp.App.AddToSpellList(new([bundled.ContentId], "fixture-seer"))));
        Assert.Equal(before, temp.App.Store.ListRevisions().Count);
    }

    [Fact]
    public void The_drafts_publish_through_publish_many_with_exactly_those_ids_and_reach_the_keys()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var ward = Published(temp, Spell(source, "Fixture Quick Ward"));
        var other = temp.App.SaveDraft(Spell(source, "Fixture Other Draft")); // a pending draft the panel must not sweep in
        var result = temp.App.AddToSpellList(new([ward.ContentId], "fixture-seer"));

        var preview = temp.App.PreviewPublishMany([ward.ContentId]);
        Assert.Equal(result.Drafts, preview.Entries.Select(e => e.Draft));
        Assert.True(preview.CanPublish);
        temp.App.PublishMany(new([ward.ContentId], [.. result.Drafts.Select(d => d.RevisionId)], Confirm: true));

        var keys = temp.App.SpellListKeys();
        Assert.Contains(new SpellListKeyCount("fixture-seer", 1), keys);
        Assert.Equal(RevisionStatus.Draft, temp.App.Store.FindRevision(other)!.Status); // untouched
    }

    // ---- the keys that exist (R45) ----

    [Fact]
    public void The_keys_count_spells_on_the_newest_published_revision_and_add_class_lists_with_none()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var ward = Published(temp, Spell(source, "Fixture Quick Ward", "fixture-seer", "fixture-extra"));
        Published(temp, Spell(source, "Fixture Spark", "fixture-seer"));
        Published(temp, Spell(source, "Fixture Twin", "fixture-seer", "fixture-seer")); // counted once for the spell
        temp.App.SaveDraft(Spell(source, "Fixture Unpublished", "fixture-secret")); // drafts are not keys
        // A newer draft of the ward drops "fixture-extra", but the newest *published* revision is what counts.
        temp.App.SaveDraft(temp.App.Store.FindRevision(ward)! with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Effects = [new SpellEffect { Id = "spell", Level = 1, Lists = ["fixture-new"] }] });
        Published(temp, new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.Empty, Kind = ContentKind.Feature, Name = "Fixture Casting", RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id), Status = RevisionStatus.Draft,
            Effects = [new SpellcastingEffect { Id = "casting", Ability = Ability.Int, SpellList = "fixture-lone", Slots = [.. Enumerable.Range(1, 20).Select(_ => (IReadOnlyList<int>)[2])] }],
        });

        var keys = temp.App.SpellListKeys();

        Assert.Contains(new SpellListKeyCount("fixture-seer", 3), keys);
        Assert.Contains(new SpellListKeyCount("fixture-extra", 1), keys);
        Assert.Contains(new SpellListKeyCount("fixture-lone", 0), keys);
        Assert.DoesNotContain(keys, k => k.Key is "fixture-secret" or "fixture-new");
        Assert.Contains(keys, k => k.Key == "wizard" && k.Spells > 0); // the bundled lists
        Assert.Equal(keys.Select(k => k.Key).OrderBy(k => k, StringComparer.Ordinal), keys.Select(k => k.Key));
    }

    [Fact]
    public void The_content_commands_are_dispatched()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var ward = Published(temp, Spell(source, "Fixture Quick Ward"));
        Assert.Contains("content.addToSpellList", CommandDispatcher.Commands);
        Assert.Contains("content.spellListKeys", CommandDispatcher.Commands);
        var dispatcher = new CommandDispatcher(temp.App);

        using var added = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(
            new { id = "1", command = "content.addToSpellList", payload = new { contentIds = new[] { ward.ContentId }, listKey = "wizard" } }, RulesJson.Compact)));
        using var keys = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "2", command = "content.spellListKeys", payload = new { } }, RulesJson.Compact)));

        Assert.True(added.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, added.RootElement.GetProperty("result").GetProperty("drafts").GetArrayLength());
        Assert.True(keys.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(keys.RootElement.GetProperty("result").EnumerateArray(), k => k.GetProperty("key").GetString() == "wizard");
    }
}
