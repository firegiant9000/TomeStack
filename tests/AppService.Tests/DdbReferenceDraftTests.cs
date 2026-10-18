using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker.Forms;
using TomeStack.RulesCore;
using Microsoft.Data.Sqlite;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 16 (D16(c)): a feat or feature the sheet names that TomeStack does not have, and that carries text on the sheet,
/// becomes a reference draft (the name and the description, no mechanics) in the import-derived source. Ticking "publish"
/// publishes them in the same transaction; a published feat is linked to the character as a pin, a feature never is; the
/// gap note stays; the preview and Cancel write nothing. Every name and text is invented.
/// </summary>
public class DdbReferenceDraftTests
{
    private const string Knack = "Fixture Knack";
    private const string BareKnack = "Fixture Bare Knack";
    private const string QuietKnack = "Fixture Quiet Knack";
    private const string Wayfarer = "Fixture Wayfarer Feat";
    private const string KnackText = "First invented line about the knack.\nSecond invented line.";
    private const string QuietText = "A quiet invented line.";
    private const string WayfarerText = "A wayfarer description line.";

    private static ContentReference Srd(int n) => new(Guid.Parse($"52c00000-0000-4000-8000-{n:D12}"), Guid.Parse($"52e00000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Barbarian = Srd(11);
    private static readonly ContentReference SavageAttacker = Srd(10);

    /// <summary>Two features with text, one with none, and a feat with text.</summary>
    internal static string Block() => string.Join('\n',
        "=== FIXTURE FEATURES ===",
        $"* {Knack} • FX 1",
        "First invented line about the knack.",
        "Second invented line.",
        $"* {BareKnack} • FX 1",
        $"* {QuietKnack} • FX 1",
        QuietText,
        "",
        "=== FEATS ===",
        $"* {Wayfarer} •",
        WayfarerText);

    internal static List<FormField> Fields(DdbHarness h, string featuresText, string? second = null)
    {
        var fields = new SheetBuilder($"{h.Name(Barbarian)} 1").Build();
        List<FormField> result = [.. fields.Select(f => f.Name == "FeaturesTraits1" ? f with { Value = featuresText } : f)];
        if (second is not null)
            result.Add(new("FeaturesTraits2", "text", 2, second));
        return result;
    }

    private static Guid Read(DdbHarness h, string? block = null, string? second = null)
    {
        h.Fields = Fields(h, block ?? Block(), second);
        return h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;
    }

    private static DdbPreviewRequest Preview(Guid token) => new(token, RulesFamilies.Srd521, null, [], [], false);

    private static DdbApplyRequest Apply(Guid token, bool publish = false) =>
        new(token, RulesFamilies.Srd521, null, [], [], false, Confirm: true, PublishReferences: publish);

    /// <summary>The apply with the publish tick on, carrying the row ids the preview showed (the consent token, R106 round).</summary>
    private static DdbApplyRequest Publishing(DdbHarness h, Guid token) =>
        Apply(token, publish: true) with { ExpectedReferences = [.. h.Temp.App.PreviewDdbImport(Preview(token)).ReferenceDrafts?.Select(d => d.RowId) ?? []] };

    private static ContentRevision Latest(DdbHarness h, Guid contentId) => h.Temp.App.ListRevisions(contentId).Last();

    private static string Many(int count, int length = 20) =>
        "=== FIXTURE FEATURES ===\n" + string.Join('\n', Enumerable.Range(0, count).Select(i => $"* Fixture Knack {i:D3} • FX 1\n{new string('t', length)}"));

    // ---- the parser ----

    [Fact]
    public void The_parser_keeps_the_lines_after_each_item_as_its_text_and_a_feature_with_no_text_has_none()
    {
        using var h = new DdbHarness();

        var sheet = h.Temp.App.DdbSessions.Peek(Read(h))!;

        Assert.Equal(
            [(Knack, FeatureKind.Feature, KnackText), (QuietKnack, FeatureKind.Feature, QuietText), (Wayfarer, FeatureKind.Feat, WayfarerText)],
            sheet.FeatureTexts!.Select(t => (t.Name, t.Kind, t.Text)));
        Assert.Equal(3, sheet.Features.Count); // the names are unchanged: the bare feature is still listed
        Assert.Single(sheet.Feats);
    }

    [Fact]
    public void A_text_carries_on_into_the_next_numbered_box_and_a_heading_ends_it()
    {
        using var h = new DdbHarness();
        var first = $"=== FIXTURE FEATURES ===\n* {Knack} • FX 1\nline one";
        var second = $"line two\n* {QuietKnack} • FX 1\n=== FIXTURE MORE ===\nnot the text of a feature";

        var sheet = h.Temp.App.DdbSessions.Peek(Read(h, first, second))!;

        Assert.Equal([(Knack, "line one\nline two")], sheet.FeatureTexts!.Select(t => (t.Name, t.Text)));
    }

    [Fact]
    public void The_fillable_sheets_prose_block_yields_no_feature_texts()
    {
        using var h = new DdbHarness();
        h.Fields = DdbParserTests.Fields(ImportWorker.Tests.FixtureSheets.FieldsWotc2014);

        var sheet = h.Temp.App.DdbSessions.Peek(h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token)!;

        Assert.Empty(sheet.FeatureTexts ?? []);
    }

    [Fact]
    public void ToString_of_the_text_record_and_the_sheet_prints_counts_only()
    {
        using var h = new DdbHarness();
        var sheet = h.Temp.App.DdbSessions.Peek(Read(h))!;

        var printed = sheet.ToString() + string.Join("", sheet.FeatureTexts!.Select(t => t.ToString()));

        Assert.DoesNotContain("Fixture", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("invented", printed, StringComparison.Ordinal);
        Assert.Contains("FeatureTexts = 3", sheet.ToString(), StringComparison.Ordinal);
    }

    // ---- the preview ----

    [Fact]
    public void The_preview_lists_the_drafts_with_the_first_line_and_writes_nothing()
    {
        using var h = new DdbHarness();
        var before = DdbStubClassTests.Counts(h);
        var token = Read(h);

        var preview = h.Temp.App.PreviewDdbImport(Preview(token));

        Assert.Equal(
            [("feat:0", FeatureKind.Feat, Wayfarer, WayfarerText), ("feature:0", FeatureKind.Feature, Knack, "First invented line about the knack."), ("feature:2", FeatureKind.Feature, QuietKnack, QuietText)],
            preview.ReferenceDrafts!.Select(d => (d.RowId, d.Kind, d.Name, d.FirstLine)));
        Assert.DoesNotContain(preview.ReferenceDrafts!, d => d.Name == BareKnack);
        Assert.Empty(preview.Character.Pins);
        Assert.Equal(before, DdbStubClassTests.Counts(h));
    }

    [Fact]
    public void An_installed_feat_with_text_is_matched_not_drafted_and_a_sheet_with_no_text_offers_nothing()
    {
        using var h = new DdbHarness();
        var installed = $"=== FEATS ===\n* {h.Name(SavageAttacker)} •\nA line the installed feat does not need.";

        Assert.Empty(h.Temp.App.PreviewDdbImport(Preview(Read(h, installed))).ReferenceDrafts ?? []);
        Assert.Empty(h.Temp.App.PreviewDdbImport(Preview(Read(h, $"=== FIXTURE FEATURES ===\n* {BareKnack} • FX 1"))).ReferenceDrafts ?? []);
    }

    [Fact]
    public void Cancel_and_discard_at_every_step_write_nothing()
    {
        using var h = new DdbHarness();
        var before = DdbStubClassTests.Counts(h);

        var token = Read(h);
        Assert.Equal(before, DdbStubClassTests.Counts(h)); // read
        h.Temp.App.PreviewDdbImport(Preview(token));
        Assert.Equal(before, DdbStubClassTests.Counts(h)); // matches, numbers and summary
        Assert.True(h.Temp.App.DiscardDdbSheet(token));
        Assert.Equal(before, DdbStubClassTests.Counts(h));
        Assert.Equal("ddb.token-invalid", Assert.Throws<AppValidationException>(() => h.Temp.App.PreviewDdbImport(Preview(token))).Code);

        var refused = Read(h);
        Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(refused, publish: true) with { Confirm = false }));
        Assert.Equal(before, DdbStubClassTests.Counts(h));
        Assert.NotNull(h.Temp.App.DdbSessions.Peek(refused));
    }

    // ---- apply ----

    [Fact]
    public void Apply_unticked_writes_the_drafts_in_the_import_derived_source_links_nothing_and_keeps_every_gap_note()
    {
        using var h = new DdbHarness();

        var result = h.Temp.App.ApplyDdbImport(Apply(Read(h)));

        Assert.Equal(3, result.ReferenceDrafts!.Count);
        var drafts = result.ReferenceDrafts.Select(id => Latest(h, id)).ToList();
        Assert.All(drafts, d =>
        {
            Assert.Single(h.Temp.App.ListRevisions(d.ContentId));
            Assert.Equal(RevisionStatus.Draft, d.Status);
            Assert.Empty(d.Effects); // no mechanics
            Assert.Equal([RulesFamilies.Srd521], d.RulesFamilies);
            var source = h.Temp.App.Store.FindSource(d.Provenance.SourceId)!;
            Assert.Equal(("Characters imported from sheets", true, false, SourceOrigin.Local), (source.Title, source.ImportDerived, source.Redistributable, source.Origin));
        });
        Assert.Equal(
            [(Wayfarer, ContentKind.Feat, WayfarerText), (Knack, ContentKind.Feature, KnackText), (QuietKnack, ContentKind.Feature, QuietText)],
            drafts.Select(d => (d.Name, d.Kind, d.Summary!)));
        var character = h.Temp.App.GetCharacter(result.CharacterId).Character;
        Assert.Empty(character.Pins);
        Assert.DoesNotContain(character.AllReferences(), r => drafts.Any(d => d.ContentId == r.ContentId));
        var labels = h.Temp.App.ListGapNotes(result.CharacterId).Where(n => n.Target.Kind == GapTargetKind.Import).Select(n => n.Target.Label!).Order();
        Assert.Equal(new[] { BareKnack, Knack, QuietKnack, Wayfarer }.Order(), labels); // the gap note stays, drafted or not
    }

    [Fact]
    public void Apply_ticked_publishes_them_and_links_the_feat_as_a_pin_and_never_a_feature()
    {
        using var h = new DdbHarness();

        var result = h.Temp.App.ApplyDdbImport(Publishing(h, Read(h)));

        var all = result.ReferenceDrafts!.Select(id => h.Temp.App.ListRevisions(id)).ToList();
        Assert.All(all, revisions => Assert.Equal([RevisionStatus.Draft, RevisionStatus.Published], revisions.Select(r => r.Status)));
        var feat = all.Single(r => r[1].Kind == ContentKind.Feat)[1];
        var features = all.Where(r => r[1].Kind == ContentKind.Feature).Select(r => r[1]).ToList();
        Assert.Equal(2, features.Count);
        var view = h.Temp.App.GetCharacter(result.CharacterId);
        Assert.Equal([feat.Reference], view.Character.Pins);
        Assert.DoesNotContain(view.Character.AllReferences(), r => features.Any(f => f.ContentId == r.ContentId));
        Assert.DoesNotContain(view.Sheet.Diagnostics, d => d.Code.StartsWith("content.", StringComparison.Ordinal));
        Assert.Contains(view.Sheet.Features!, f => f.Name == Wayfarer);
        Assert.DoesNotContain(view.Sheet.Features!, f => f.Name == Knack); // a feature is reference content in the studio, not on the sheet
        Assert.Equal(4, h.Temp.App.ListGapNotes(result.CharacterId).Count(n => n.Target.Kind == GapTargetKind.Import));
    }

    [Fact]
    public void Features_alone_published_leave_the_character_with_no_new_reference()
    {
        using var h = new DdbHarness();
        var token = Read(h, $"=== FIXTURE FEATURES ===\n* {Knack} • FX 1\n{KnackText}");

        var result = h.Temp.App.ApplyDdbImport(Publishing(h, token));

        Assert.Equal(RevisionStatus.Published, Latest(h, Assert.Single(result.ReferenceDrafts!)).Status);
        Assert.Empty(h.Temp.App.GetCharacter(result.CharacterId).Character.Pins);
    }

    [Fact]
    public void A_later_edit_of_a_linked_feat_is_adopted_only_through_the_reviewed_update()
    {
        using var h = new DdbHarness();
        var result = h.Temp.App.ApplyDdbImport(Publishing(h, Read(h)));
        var feat = result.ReferenceDrafts!.Select(id => Latest(h, id)).Single(r => r.Kind == ContentKind.Feat);
        Assert.Empty(h.Temp.App.AvailableUpdates(result.CharacterId));

        var edited = feat with { RevisionId = Guid.NewGuid(), Status = RevisionStatus.Draft, Summary = "Edited invented text." };
        h.Temp.App.SaveDraft(edited);
        h.Temp.App.PublishMany(new PublishManyRequest([feat.ContentId], [edited.RevisionId], Confirm: true));

        Assert.Equal([feat.Reference], h.Temp.App.GetCharacter(result.CharacterId).Character.Pins); // never auto-advanced
        var offer = Assert.Single(h.Temp.App.AvailableUpdates(result.CharacterId));
        Assert.Equal(feat.Reference, offer.From);
    }

    [Fact]
    public void The_one_import_derived_source_is_reused_by_every_import()
    {
        using var h = new DdbHarness();

        h.Temp.App.ApplyDdbImport(Apply(Read(h)));
        h.Temp.App.ApplyDdbImport(Publishing(h, Read(h, Many(2))));

        Assert.Single(h.Temp.App.Store.ListSources(), s => s.ImportDerived == true);
    }

    // ---- the caps ----

    [Fact]
    public void A_text_over_20000_characters_is_cut_at_a_code_point_boundary_and_the_report_names_the_position_only()
    {
        using var h = new DdbHarness();
        var text = new string('a', 19_999) + "\U0001F600" + "tail"; // the pair would straddle the cut
        var token = Read(h, $"=== FIXTURE FEATURES ===\n* {Knack} • FX 1\n{text}");

        var preview = h.Temp.App.PreviewDdbImport(Preview(token));

        var info = Assert.Single(preview.ReferenceDrafts!);
        Assert.Equal(19_999, info.Length);
        var line = Assert.Single(preview.Report.Lines!, l => l.Contains("cut to 20,000", StringComparison.Ordinal));
        Assert.Equal("Feature 1's text was cut to 20,000 characters.", line);
        var written = Latest(h, Assert.Single(h.Temp.App.ApplyDdbImport(Apply(token)).ReferenceDrafts!)).Summary!;
        Assert.Equal(19_999, written.Length);
        Assert.False(char.IsSurrogate(written[^1]));
    }

    [Fact]
    public void A_text_of_exactly_20000_characters_is_kept_whole_with_no_report_line()
    {
        using var h = new DdbHarness();
        var token = Read(h, $"=== FEATS ===\n* {Wayfarer} •\n{new string('a', 20_000)}");

        var preview = h.Temp.App.PreviewDdbImport(Preview(token));

        Assert.Equal(20_000, Assert.Single(preview.ReferenceDrafts!).Length);
        Assert.DoesNotContain(preview.Report.Lines ?? [], l => l.Contains("cut to", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(105, 100, "5 more features were left as gap notes.")]
    [InlineData(101, 100, "1 more feature was left as a gap note.")]
    [InlineData(100, 100, null)]
    public void At_most_100_drafts_are_made_and_the_report_counts_the_rest(int features, int drafted, string? line)
    {
        using var h = new DdbHarness();
        var token = Read(h, Many(features));

        var preview = h.Temp.App.PreviewDdbImport(Preview(token));
        var result = h.Temp.App.ApplyDdbImport(Apply(token));

        Assert.Equal(drafted, preview.ReferenceDrafts!.Count);
        Assert.Equal(drafted, result.ReferenceDrafts!.Count);
        Assert.Equal("Fixture Knack 000", preview.ReferenceDrafts[0].Name); // sheet order
        Assert.Equal(line is null, !(preview.Report.Lines ?? []).Any(l => l.Contains("gap note", StringComparison.Ordinal)));
        if (line is not null)
            Assert.Contains(line, preview.Report.Lines!);
        Assert.Equal(features, h.Temp.App.ListGapNotes(result.CharacterId).Count(n => n.Target.Kind == GapTargetKind.Import)); // every one keeps its note
    }

    // ---- one transaction ----

    [Fact]
    public void A_failure_after_the_drafts_and_the_publish_rolls_everything_back_and_keeps_the_token()
    {
        using var h = new DdbHarness();
        var before = DdbStubClassTests.Counts(h);
        var token = Read(h);
        DdbStubClassTests.RefuseCharacterInserts(h);

        Assert.Throws<SqliteException>(() => h.Temp.App.ApplyDdbImport(Publishing(h, token)));

        Assert.Equal(before, DdbStubClassTests.Counts(h)); // no source, no draft, no published revision, no character
        Assert.NotNull(h.Temp.App.DdbSessions.Peek(token));
    }

    // ---- no sheet text in errors ----

    [Fact]
    public void Refusals_and_the_info_record_never_quote_a_name_or_a_text()
    {
        using var h = new DdbHarness();
        var token = Read(h);
        var preview = h.Temp.App.PreviewDdbImport(Preview(token));

        var refused = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(token) with { Confirm = false }));
        var spent = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(Guid.NewGuid(), publish: true)));

        var said = string.Join("\n", new[] { refused.Message, spent.Message }.Concat(refused.Problems.Concat(spent.Problems).Select(p => p.Message)))
            + string.Join("\n", preview.ReferenceDrafts!.Select(d => d.ToString()))
            + string.Join("\n", preview.Report.Lines ?? []);
        Assert.DoesNotContain("Fixture", said, StringComparison.Ordinal);
        Assert.DoesNotContain("invented", said, StringComparison.Ordinal);
    }

    // ---- fix round 1 ----

    private static int Revisions(DdbHarness h) => h.Temp.App.Store.ListRevisions().Count;

    [Fact]
    public void Importing_the_same_sheet_twice_adds_no_second_draft_and_the_report_counts_them_without_names()
    {
        using var h = new DdbHarness();
        h.Temp.App.ApplyDdbImport(Apply(Read(h)));
        var revisions = Revisions(h);

        var token = Read(h);
        var preview = h.Temp.App.PreviewDdbImport(Preview(token));
        var second = h.Temp.App.ApplyDdbImport(Apply(token));

        Assert.Empty(preview.ReferenceDrafts ?? []);
        Assert.Null(second.ReferenceDrafts);
        Assert.Equal(revisions, Revisions(h));
        Assert.Contains("3 features already had reference drafts and were not added again.", preview.Report.Lines!);
        Assert.DoesNotContain(preview.Report.Lines!, l => l.Contains("Fixture", StringComparison.Ordinal));
        Assert.Equal(4, h.Temp.App.ListGapNotes(second.CharacterId).Count(n => n.Target.Kind == GapTargetKind.Import)); // the gap notes stay
    }

    [Fact]
    public void A_published_copy_counts_too_and_the_name_is_compared_without_case()
    {
        using var h = new DdbHarness();
        h.Temp.App.ApplyDdbImport(Publishing(h, Read(h)));
        var revisions = Revisions(h);

        var shouting = Block().Replace(Knack, Knack.ToUpperInvariant(), StringComparison.Ordinal);
        var preview = h.Temp.App.PreviewDdbImport(Preview(Read(h, shouting)));

        Assert.Empty(preview.ReferenceDrafts ?? []);
        Assert.Equal(revisions, Revisions(h));
        // The published feat now matches as installed content, so it is not a candidate; the two features are counted, one by its upper-case spelling.
        Assert.Contains("2 features already had reference drafts and were not added again.", preview.Report.Lines!);
    }

    [Fact]
    public void One_already_there_leaves_the_new_ones_and_the_line_says_one_feature()
    {
        using var h = new DdbHarness();
        h.Temp.App.ApplyDdbImport(Apply(Read(h, $"=== FIXTURE FEATURES ===\n* {Knack} • FX 1\n{KnackText}")));

        var preview = h.Temp.App.PreviewDdbImport(Preview(Read(h)));

        Assert.Equal([Wayfarer, QuietKnack], preview.ReferenceDrafts!.Select(d => d.Name).Order(StringComparer.Ordinal).Reverse());
        Assert.Contains("1 feature already had a reference draft and was not added again.", preview.Report.Lines!);
    }

    [Fact]
    public void A_name_is_cut_to_200_characters_at_a_code_point_in_the_draft_and_in_the_listing()
    {
        using var h = new DdbHarness();
        var name = new string('n', 199) + "\U0001F600" + "tail"; // the pair would straddle the cut
        var token = Read(h, $"=== FIXTURE FEATURES ===\n* {name} • FX 1\nAn invented line.");

        var info = Assert.Single(h.Temp.App.PreviewDdbImport(Preview(token)).ReferenceDrafts!);
        var written = Latest(h, Assert.Single(h.Temp.App.ApplyDdbImport(Apply(token)).ReferenceDrafts!));

        Assert.Equal(199, info.Name.Length);
        Assert.Equal(info.Name, written.Name);
        Assert.False(char.IsSurrogate(written.Name[^1]));
    }

    [Fact]
    public void A_text_belongs_to_its_own_row_when_two_entries_share_a_name_and_the_first_is_left_out()
    {
        using var h = new DdbHarness();
        var token = Read(h, $"=== FIXTURE FEATURES ===\n* {Knack} • FX 1\nThe first invented text.\n* {Knack} • FX 1\nThe second invented text.");

        var preview = h.Temp.App.PreviewDdbImport(Preview(token) with { Resolutions = [new("feature:0", null, true)] });

        var info = Assert.Single(preview.ReferenceDrafts!);
        Assert.Equal(("feature:1", "The second invented text."), (info.RowId, info.FirstLine));
        var written = Latest(h, Assert.Single(h.Temp.App.ApplyDdbImport(Apply(token) with { Resolutions = [new("feature:0", null, true)] }).ReferenceDrafts!));
        Assert.Equal("The second invented text.", written.Summary);
    }

    [Fact]
    public void Publishing_refuses_when_the_drafts_are_not_the_ones_shown_and_writes_nothing()
    {
        using var h = new DdbHarness();
        var before = DdbStubClassTests.Counts(h);
        var token = Read(h);

        var changed = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(token, publish: true) with { ExpectedReferences = ["feature:0"] }));
        var missing = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(token, publish: true)));

        Assert.Equal("ddb.references-changed", changed.Code);
        Assert.Equal("ddb.references-changed", missing.Code);
        Assert.DoesNotContain("Fixture", changed.Message + string.Join("", changed.Problems.Select(p => p.Message)), StringComparison.Ordinal);
        Assert.Equal(before, DdbStubClassTests.Counts(h));
        Assert.NotNull(h.Temp.App.DdbSessions.Peek(token)); // the token is kept
    }

    [Fact]
    public void Publishing_accepts_the_shown_set_in_any_order_and_unticked_needs_no_token()
    {
        using var h = new DdbHarness();
        var token = Read(h);
        var shown = h.Temp.App.PreviewDdbImport(Preview(token)).ReferenceDrafts!.Select(d => d.RowId).Reverse().ToList();

        var result = h.Temp.App.ApplyDdbImport(Apply(token, publish: true) with { ExpectedReferences = shown });

        Assert.Equal(3, result.ReferenceDrafts!.Count);
        Assert.NotNull(h.Temp.App.ApplyDdbImport(Apply(Read(h, Many(1)))).ReferenceDrafts);
    }

    [Fact]
    public void A_campaign_that_does_not_allow_the_source_is_told_before_create_and_the_feat_is_still_pinned()
    {
        using var h = new DdbHarness();
        var campaign = h.Temp.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Fixture Table", RulesFamily = RulesFamilies.Srd521, AllowedSources = [Guid.Parse("52500000-0000-4000-8000-000000000001")] });
        var token = Read(h);

        var preview = h.Temp.App.PreviewDdbImport(Preview(token) with { CampaignId = campaign.Id });
        var none = h.Temp.App.PreviewDdbImport(Preview(token));
        var featuresOnly = h.Temp.App.PreviewDdbImport(Preview(Read(h, $"=== FIXTURE FEATURES ===\n* {Knack} • FX 1\n{KnackText}")) with { CampaignId = campaign.Id });
        var result = h.Temp.App.ApplyDdbImport(Publishing(h, token) with { CampaignId = campaign.Id });

        Assert.Equal("campaign.source-not-allowed", preview.ReferenceNote);
        Assert.Null(none.ReferenceNote); // no campaign
        Assert.Null(featuresOnly.ReferenceNote); // nothing would be pinned
        Assert.Single(h.Temp.App.GetCharacter(result.CharacterId).Character.Pins); // pinning is not skipped
    }
}
