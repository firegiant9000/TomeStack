using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TomeStack.AppService.CharacterImport;
using TomeStack.AppService.Exports;
using TomeStack.AppService.Packages;
using TomeStack.ImportWorker.Forms;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Item 15 (D35): a class the sheet names that is not installed can be created as a minimal homebrew stub (hit die and
/// saving throws only) for this one import. The stub is a draft in an import-derived source; ticking "publish" publishes it
/// in the same transaction; the preview and Cancel write nothing; no outbound path carries it. Every value is invented.
/// </summary>
public class DdbStubClassTests
{
    private const string Tinkerer = "Fixture Tinkerer";
    private const string Chanter = "Fixture Whittler"; // invented, and not installed
    private const string Row0 = "class:0";

    private static readonly ContentReference Barbarian = new(Guid.Parse("52c00000-0000-4000-8000-000000000011"), Guid.Parse("52e00000-0000-4000-8000-000000000011"));

    private static List<FormField> Wotc() => DdbParserTests.Fields(FixtureSheets.FieldsWotc2014);

    private static List<FormField> With(List<FormField> fields, string name, string? value) =>
        [.. fields.Select(f => f.Name == name ? f with { Value = value } : f)];

    /// <summary>The fillable sheet with the class text and the hit dice total set (the left box "HD" stays as the fixture has it).</summary>
    private static List<FormField> Sheet(string classLevel = $"{Tinkerer} LVL 3", string? hitDice = "3d8") =>
        With(With(Wotc(), "ClassLevel", classLevel), "HDTotal", hitDice);

    private static DdbPreviewRequest Preview(Guid token, string? create = null, string family = RulesFamilies.Srd51) =>
        new(token, family, null, [], [], false, CreateClassRow: create);

    private static DdbApplyRequest Apply(Guid token, string? create = null, bool publish = false, string family = RulesFamilies.Srd51) =>
        new(token, family, null, [], [], false, Confirm: true, CreateClassRow: create, PublishClass: publish);

    private static (int Sources, int Revisions, int Characters, int Notes) Counts(DdbHarness h) =>
        (h.Temp.App.Store.ListSources().Count, h.Temp.App.Store.ListRevisions().Count, h.Temp.App.ListCharacters().Count, h.Temp.App.ListAllGapNotes().Count);

    private static Guid Read(DdbHarness h, List<FormField> fields)
    {
        h.Fields = fields;
        return h.Temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;
    }

    private static ContentRevision StubOf(DdbHarness h, Guid contentId) => h.Temp.App.ListRevisions(contentId).Last();

    // ---- the preview ----

    [Fact]
    public void The_preview_offers_the_stub_for_a_class_that_is_not_installed_and_writes_nothing()
    {
        using var h = new DdbHarness();
        var before = Counts(h);
        var token = Read(h, Sheet());

        var preview = h.Temp.App.PreviewDdbImport(Preview(token));

        var offer = Assert.Single(preview.StubOffers!);
        Assert.Equal((Row0, Tinkerer, 8, null), (offer.RowId, offer.Name, offer.HitDie, offer.Note));
        Assert.Equal(MatchStatus.NotFound, preview.Matches.Single(m => m.RowId == Row0).Status);
        Assert.Null(preview.Stub);
        Assert.Empty(preview.Character.Classes);
        Assert.Equal(before, Counts(h));
    }

    [Fact]
    public void The_preview_with_the_stub_chosen_has_hit_points_from_its_die_shows_its_whole_content_and_writes_nothing()
    {
        using var h = new DdbHarness();
        var before = Counts(h);
        var token = Read(h, Sheet());

        var preview = h.Temp.App.PreviewDdbImport(Preview(token, Row0));

        var row = preview.Matches.Single(m => m.RowId == Row0);
        Assert.Equal(MatchStatus.Matched, row.Status);
        Assert.Equal(Tinkerer, row.Chosen!.Name);
        var stub = preview.Stub!;
        Assert.Equal((Row0, Tinkerer, RulesFamilies.Srd51, 8), (stub.RowId, stub.Name, stub.RulesFamily, stub.HitDie));
        Assert.Equal(["str"], stub.Saves);
        var entry = Assert.Single(preview.Character.Classes);
        Assert.Equal((3, row.Chosen.Reference), (entry.Level, entry.Class));
        Assert.True(preview.Comparison.Single(c => c.Field == FieldIds.HitPoints).Calculated > 0);
        Assert.True(preview.CanApply);
        Assert.Equal(before, Counts(h));
        Assert.Equal("wotc-2014", h.Temp.App.DdbSessions.Peek(token)!.Layout);
    }

    [Fact]
    public void Cancel_and_discard_at_every_step_write_nothing()
    {
        using var h = new DdbHarness();
        var before = Counts(h);

        var read = Read(h, Sheet());
        Assert.Equal(before, Counts(h)); // read
        h.Temp.App.PreviewDdbImport(Preview(read));
        Assert.Equal(before, Counts(h)); // matches (the offer)
        h.Temp.App.PreviewDdbImport(Preview(read, Row0));
        Assert.Equal(before, Counts(h)); // numbers and summary (the stub chosen)
        Assert.True(h.Temp.App.DiscardDdbSheet(read));
        Assert.Equal(before, Counts(h));
        Assert.Equal("ddb.token-invalid", Assert.Throws<AppValidationException>(() => h.Temp.App.PreviewDdbImport(Preview(read, Row0))).Code);

        // A refused apply writes nothing either, and keeps the token.
        var refused = Read(h, Sheet());
        Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(refused) with { Confirm = false }));
        Assert.Equal(before, Counts(h));
        Assert.NotNull(h.Temp.App.DdbSessions.Peek(refused));
    }

    // ---- apply ----

    [Fact]
    public void Apply_with_publish_ticked_writes_an_import_derived_source_a_published_class_and_a_character_on_it()
    {
        using var h = new DdbHarness();
        var token = Read(h, Sheet());

        var result = h.Temp.App.ApplyDdbImport(Apply(token, Row0, publish: true));

        var character = h.Temp.App.GetCharacter(result.CharacterId);
        var entry = Assert.Single(character.Character.Classes);
        Assert.Equal(result.CreatedClass, entry.Class.ContentId);
        var revisions = h.Temp.App.ListRevisions(entry.Class.ContentId);
        Assert.Equal([RevisionStatus.Draft, RevisionStatus.Published], revisions.Select(r => r.Status));
        var published = revisions[1];
        Assert.Equal(published.Reference, entry.Class);
        Assert.Equal((Tinkerer, ContentKind.Class), (published.Name, published.Kind));
        Assert.Equal([RulesFamilies.Srd51], published.RulesFamilies);
        Assert.Equal(8, published.Effects.OfType<HitDieEffect>().Single().Die);
        Assert.Equal(["save.str"], published.Effects.OfType<GrantEffect>().Select(g => g.Target));
        Assert.Equal(2, published.Effects.Count); // the hit die and the saves, nothing else
        var source = h.Temp.App.Store.FindSource(published.Provenance.SourceId)!;
        Assert.Equal((true, false, SourceOrigin.Local), (source.ImportDerived, source.Redistributable, source.Origin));
        Assert.DoesNotContain(character.Sheet.Diagnostics, d => d.Code.StartsWith("content.", StringComparison.Ordinal));
        Assert.True(character.Sheet.Field(FieldIds.HitPoints).Value > 0);
        Assert.Contains(character.Sheet.Field(FieldIds.Save(Ability.Str)).Trace, t => t.Operation == "add");
        // No gap note for the class the stub covers; the others (species, background) keep theirs.
        var notes = JsonSerializer.Serialize(h.Temp.App.ListGapNotes(result.CharacterId));
        Assert.DoesNotContain(Tinkerer, notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_with_publish_unticked_leaves_the_class_a_draft_and_the_character_loads_and_gets_nothing_from_it()
    {
        using var h = new DdbHarness();
        var token = Read(h, Sheet());

        var result = h.Temp.App.ApplyDdbImport(Apply(token, Row0, publish: false));

        var revisions = h.Temp.App.ListRevisions(result.CreatedClass!.Value);
        var draft = Assert.Single(revisions);
        Assert.Equal(RevisionStatus.Draft, draft.Status);
        var view = h.Temp.App.GetCharacter(result.CharacterId); // loads and calculates
        Assert.Equal(draft.Reference, Assert.Single(view.Character.Classes).Class);
        Assert.Contains(view.Sheet.Diagnostics, d => d.Code == "content.unpublished");
        Assert.DoesNotContain(view.Sheet.Fields, f => f.Field == FieldIds.Save(Ability.Str) && f.Trace.Any(t => t.Operation == "add"));
        Assert.Single(h.Temp.App.ListCharacters());
        Assert.Equal(result.CharacterId, h.Temp.App.ListCharacters().Single().Id);
    }

    [Fact]
    public void Publishing_the_draft_in_the_studio_offers_an_update_and_accepting_it_activates_the_class_for_the_character()
    {
        using var h = new DdbHarness();
        var result = h.Temp.App.ApplyDdbImport(Apply(Read(h, Sheet()), Row0, publish: false));
        var draft = Assert.Single(h.Temp.App.ListRevisions(result.CreatedClass!.Value));
        Assert.Empty(h.Temp.App.AvailableUpdates(result.CharacterId)); // a draft offers nothing

        var published = h.Temp.App.PublishMany(new PublishManyRequest([draft.ContentId], [draft.RevisionId], Confirm: true));

        // R102: the character still points at the draft until it accepts the offered update (characters opt in).
        Assert.Contains(h.Temp.App.GetCharacter(result.CharacterId).Sheet.Diagnostics, d => d.Code == "content.unpublished");
        var offer = Assert.Single(h.Temp.App.AvailableUpdates(result.CharacterId));
        Assert.Equal(draft.Reference, offer.From);
        Assert.Equal(h.Temp.App.ListRevisions(draft.ContentId).Last(r => r.Status == RevisionStatus.Published).Reference, offer.To);
        Assert.Equal(offer.To, Assert.Single(published.Published).Published);
        Assert.NotEmpty(h.Temp.App.ReviewUpdate(result.CharacterId, offer.From, offer.To).Fields);

        var updated = h.Temp.App.ApplyUpdate(result.CharacterId, offer.From, offer.To, confirm: true);

        Assert.DoesNotContain(updated.Sheet.Diagnostics, d => d.Code == "content.unpublished");
        Assert.True(updated.Sheet.Field(FieldIds.HitPoints).Value > 0);
        Assert.Contains(updated.Sheet.Field(FieldIds.Save(Ability.Str)).Trace, t => t.Operation == "add");
        Assert.Empty(h.Temp.App.AvailableUpdates(result.CharacterId));
    }

    [Fact]
    public void A_campaign_that_does_not_allow_the_stub_source_gives_the_created_row_the_campaign_note_in_the_preview()
    {
        using var h = new DdbHarness();
        var campaign = h.Temp.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Test Table", RulesFamily = RulesFamilies.Srd51, AllowedSources = [Guid.Parse("52500000-0000-4000-8000-000000000001")] });

        var preview = h.Temp.App.PreviewDdbImport(Preview(Read(h, Sheet()), Row0) with { CampaignId = campaign.Id });

        var row = preview.Matches.Single(m => m.RowId == Row0);
        Assert.Equal((MatchStatus.Matched, "campaign.source-not-allowed"), (row.Status, row.Note));
        Assert.False(row.Chosen!.AllowedInCampaign);
        Assert.NotNull(preview.Stub);
    }

    [Fact]
    public void A_backup_export_and_the_library_backup_carry_the_stub_by_design()
    {
        using var h = new DdbHarness();
        var before = h.Temp.App.PreviewLibraryBackup();
        var (id, source, _) = Created(h);

        var export = h.Temp.App.ExportCharacters([id], ExportPurpose.Backup);
        Assert.Contains(export.Manifest.Notices, n => n.SourceId == source.Id);
        Assert.DoesNotContain(export.Manifest.Omitted, o => o.SourceId == source.Id);
        Assert.DoesNotContain(h.Temp.App.PreviewExport([id], ExportPurpose.Backup).Omitted, o => o.SourceId == source.Id);

        using var file = new MemoryStream();
        var contents = h.Temp.App.WriteLibraryBackup(file).Contents;
        Assert.Equal(
            (before.Sources + 1, before.PublishedRevisions + 1, before.DraftRevisions + 1, before.Characters + 1),
            (contents.Sources, contents.PublishedRevisions, contents.DraftRevisions, contents.Characters));
    }

    [Fact]
    public void A_second_import_for_another_rules_family_reuses_the_import_derived_source_and_adds_the_family()
    {
        using var h = new DdbHarness();
        var first = h.Temp.App.ApplyDdbImport(Apply(Read(h, Sheet()), Row0));
        var sourceId = h.Temp.App.ListRevisions(first.CreatedClass!.Value).Single().Provenance.SourceId;

        var second = h.Temp.App.ApplyDdbImport(Apply(Read(h, Sheet()), Row0, family: RulesFamilies.Srd521));

        Assert.Equal(sourceId, h.Temp.App.ListRevisions(second.CreatedClass!.Value).Single().Provenance.SourceId);
        var sources = h.Temp.App.Store.ListSources().Where(s => s.Title == "Characters imported from sheets").ToList();
        var only = Assert.Single(sources);
        Assert.Equal((true, false), (only.ImportDerived, only.Redistributable));
        Assert.Equal([RulesFamilies.Srd51, RulesFamilies.Srd521], only.RulesFamilies.Order());
    }

    [Fact]
    public void A_failure_after_the_draft_and_the_publish_rolls_everything_back_and_keeps_the_token()
    {
        using var h = new DdbHarness();
        var before = Counts(h);
        var token = Read(h, Sheet());
        using (var connection = new SqliteConnection($"Data Source={h.Temp.App.Store.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fixture_refuse_characters BEFORE INSERT ON characters BEGIN SELECT RAISE(ABORT, 'fixture refusal'); END;";
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => h.Temp.App.ApplyDdbImport(Apply(token, Row0, publish: true)));

        Assert.Equal(before, Counts(h)); // no source, no draft, no published revision, no character
        Assert.NotNull(h.Temp.App.DdbSessions.Peek(token));
    }

    [Fact]
    public void A_second_import_reuses_the_one_import_derived_source()
    {
        using var h = new DdbHarness();

        h.Temp.App.ApplyDdbImport(Apply(Read(h, Sheet()), Row0));
        h.Temp.App.ApplyDdbImport(Apply(Read(h, Sheet()), Row0, publish: true));

        Assert.Single(h.Temp.App.Store.ListSources(), s => s.ImportDerived == true);
    }

    [Fact]
    public void Applying_a_row_that_is_not_offered_is_refused_without_quoting_the_class_and_writes_nothing()
    {
        using var h = new DdbHarness();
        var before = Counts(h);
        var barbarian = h.Name(Barbarian, RulesFamilies.Srd51);
        var matched = Read(h, Sheet($"{barbarian} LVL 3"));
        var unreadable = Read(h, Sheet(hitDice: "banana"));

        foreach (var (token, row) in new[] { (matched, Row0), (unreadable, Row0), (matched, "class:7") })
        {
            var refused = Assert.Throws<AppValidationException>(() => h.Temp.App.ApplyDdbImport(Apply(token, row)));
            Assert.Equal("ddb.class-not-offered", refused.Code);
            Assert.DoesNotContain(Tinkerer, refused.Message + string.Join("", refused.Problems.Select(p => p.Message)), StringComparison.Ordinal);
        }

        Assert.Equal(before, Counts(h));
    }

    // ---- the hit die (R-hit-die) ----

    [Theory]
    [InlineData("5d8", 0, 5, 8, null)]
    [InlineData("5 d8", 0, 5, 8, null)]
    [InlineData("5D8", 0, 5, 8, null)]
    [InlineData("3d12 + 5d8", 3, 5, 8, null)]
    [InlineData("5d8, 3d12", 3, 5, 8, null)]
    [InlineData("5d8 3d12", 3, 5, 8, null)]
    [InlineData("3d12+5d8", 3, 5, 8, null)]
    [InlineData("3d12", 3, 5, null, "stub.hit-die-unreadable")] // nothing left of the stub's die
    [InlineData("3d12 + 4d8", 3, 5, null, "stub.hit-die-unreadable")] // counts that do not add up
    [InlineData("3d12 + 2d8 + 3d6", 3, 5, null, "stub.hit-die-unreadable")] // two groups left
    [InlineData("2d12", 3, 5, null, "stub.hit-die-unreadable")] // fewer than the matched class's levels
    [InlineData("5d7", 0, 5, null, "stub.hit-die-unreadable")]
    [InlineData("5d20", 0, 5, null, "stub.hit-die-unreadable")]
    [InlineData("eight", 0, 5, null, "stub.hit-die-unreadable")]
    [InlineData("5d8 and more", 0, 5, null, "stub.hit-die-unreadable")]
    [InlineData("", 0, 5, null, "stub.hit-die-missing")]
    [InlineData("   ", 0, 5, null, "stub.hit-die-missing")]
    [InlineData(null, 0, 5, null, "stub.hit-die-missing")]
    public void The_hit_die_is_what_is_left_after_the_matched_classes_or_unreadable(string? text, int matchedLevels, int stubLevel, int? die, string? note)
    {
        var read = StubClasses.ReadHitDie(text, matchedLevels > 0 ? [(matchedLevels, 12)] : [], stubLevel);

        Assert.Equal((die, note), (read.Die, read.Note));
    }

    [Fact]
    public void A_stub_only_sheet_reads_its_own_die_and_a_multiclass_sheet_takes_its_own_share()
    {
        Assert.Equal((8, null), StubClasses.ReadHitDie("5d8", [], 5));
        Assert.Equal((8, null), StubClasses.ReadHitDie("3d10 + 2d8", [(3, 10)], 2));
        // Two classes the same die as a matched one: the share is what is left.
        Assert.Equal((10, null), StubClasses.ReadHitDie("5d10", [(3, 10)], 2));
        // A matched class whose die is unknown cannot be taken out.
        Assert.Equal((null, "stub.hit-die-unreadable"), StubClasses.ReadHitDie("3d10 + 2d8", [(3, 0)], 2));
    }

    [Fact]
    public void A_multiclass_sheet_with_one_matched_class_gives_the_stub_its_own_die_and_an_unreadable_total_offers_none()
    {
        using var h = new DdbHarness();
        var barbarian = h.Name(Barbarian, RulesFamilies.Srd51);
        var mixed = $"{barbarian} LVL 2 / {Tinkerer} LVL 2";

        var offer = Assert.Single(h.Temp.App.PreviewDdbImport(Preview(Read(h, Sheet(mixed, "2d12 + 2d8")))).StubOffers!);
        Assert.Equal((Tinkerer, 8, null), (offer.Name, offer.HitDie, offer.Note));

        var bad = h.Temp.App.PreviewDdbImport(Preview(Read(h, Sheet(mixed, "2d12 + 1d8 + 1d6")), "class:1"));
        offer = Assert.Single(bad.StubOffers!);
        Assert.Equal((null, "stub.hit-die-unreadable"), (offer.HitDie, offer.Note));
        Assert.Null(bad.Stub); // never offered, so never created
        Assert.Equal(MatchStatus.NotFound, bad.Matches.Single(m => m.RowId == "class:1").Status);
        Assert.Contains(bad.Report.Lines!, l => l.Contains("hit dice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Tinkerer, string.Join("\n", bad.Report.Lines!), StringComparison.Ordinal);
        Assert.DoesNotContain("1d8", string.Join("\n", bad.Report.Lines!), StringComparison.Ordinal);

        var missing = h.Temp.App.PreviewDdbImport(Preview(Read(h, Sheet(mixed, hitDice: ""))));
        Assert.Equal("stub.hit-die-missing", Assert.Single(missing.StubOffers!).Note);
    }

    // ---- R100: one stub per import ----

    [Fact]
    public void When_two_classes_are_not_installed_each_is_offered_and_choosing_one_holds_the_other()
    {
        using var h = new DdbHarness();
        var token = Read(h, Sheet($"{Tinkerer} LVL 3 / {Chanter} LVL 2", "5d8"));

        var open = h.Temp.App.PreviewDdbImport(Preview(token));
        Assert.All(open.StubOffers!, o => Assert.Null(o.Note));
        Assert.Equal(2, open.StubOffers!.Count);

        var chosen = h.Temp.App.PreviewDdbImport(Preview(token, "class:1"));
        Assert.Equal(Chanter, chosen.Stub!.Name);
        var other = chosen.StubOffers!.Single(o => o.RowId == Row0);
        Assert.Equal("stub.one-per-import", other.Note);
        Assert.Equal(MatchStatus.NotFound, chosen.Matches.Single(m => m.RowId == Row0).Status); // the existing gap-note path

        var result = h.Temp.App.ApplyDdbImport(Apply(token, "class:1"));
        var notes = JsonSerializer.Serialize(h.Temp.App.ListGapNotes(result.CharacterId));
        Assert.Contains(Tinkerer, notes, StringComparison.Ordinal);
        Assert.DoesNotContain(Chanter, notes, StringComparison.Ordinal);
    }

    // ---- R101: saving throws ----

    [Fact]
    public void The_stub_takes_the_ticked_saves_only_as_the_first_class_and_otherwise_none_with_a_report_line()
    {
        using var h = new DdbHarness();
        const string line = "Saving throws come from the first class; the created class has none.";
        var barbarian = h.Name(Barbarian, RulesFamilies.Srd51);

        var first = h.Temp.App.PreviewDdbImport(Preview(Read(h, Sheet()), Row0));
        Assert.Equal(["str"], first.Stub!.Saves);
        Assert.DoesNotContain(line, first.Report.Lines ?? []);

        var second = h.Temp.App.PreviewDdbImport(Preview(Read(h, Sheet($"{barbarian} LVL 2 / {Tinkerer} LVL 2", "2d12 + 2d8")), "class:1"));
        Assert.Empty(second.Stub!.Saves);
        Assert.Contains(line, second.Report.Lines!);

        var result = h.Temp.App.ApplyDdbImport(Apply(Read(h, Sheet($"{barbarian} LVL 2 / {Tinkerer} LVL 2", "2d12 + 2d8")), "class:1", publish: true));
        var stub = StubOf(h, result.CreatedClass!.Value);
        Assert.Empty(stub.Effects.OfType<GrantEffect>());
        Assert.Single(stub.Effects.OfType<HitDieEffect>());
    }

    // ---- every outbound path refuses or omits the source and the stub ----

    /// <summary>Applies the sheet with the stub published and returns the character and the stub's source.</summary>
    private static (Guid CharacterId, SourceRecord Source, ContentRevision Published) Created(DdbHarness h)
    {
        var result = h.Temp.App.ApplyDdbImport(Apply(Read(h, Sheet()), Row0, publish: true));
        var published = h.Temp.App.ListRevisions(result.CreatedClass!.Value).Last();
        return (result.CharacterId, h.Temp.App.Store.FindSource(published.Provenance.SourceId)!, published);
    }

    [Fact]
    public void Outbound_character_share_package_omits_the_stub_and_lists_it_as_left_out()
    {
        using var h = new DdbHarness();
        var (id, source, published) = Created(h);

        var preview = h.Temp.App.PreviewExport([id], ExportPurpose.Share);
        var export = h.Temp.App.ExportCharacters([id], ExportPurpose.Share);

        Assert.Contains(preview.Omitted, o => o.SourceId == source.Id);
        Assert.Contains(export.Manifest.Omitted, o => o.SourceId == source.Id && o.Revisions.Any(r => r.Reference == published.Reference));
        Assert.DoesNotContain(export.Manifest.Notices, s => s.SourceId == source.Id);
        Assert.DoesNotContain(Encoding.UTF8.GetString(export.Content), published.RevisionId.ToString("D"), StringComparison.Ordinal);
    }

    [Fact]
    public void Outbound_source_pack_refuses_the_import_derived_source()
    {
        using var h = new DdbHarness();
        var (_, source, _) = Created(h);

        var codes = Assert.Throws<PackageException>(() => h.Temp.App.ExportSourcePack([source.Id])).Errors.Select(e => e.Code);

        Assert.Contains("pack.source-import-derived", codes);
        Assert.Contains(Assert.Throws<PackageException>(() => h.Temp.App.PreviewSourcePack([source.Id])).Errors, e => e.Code == "pack.source-import-derived");
    }

    [Fact]
    public void Outbound_mark_as_shareable_refuses_the_import_derived_source()
    {
        using var h = new DdbHarness();
        var (_, source, _) = Created(h);

        var refused = Assert.Throws<AppValidationException>(() => h.Temp.App.SetShareable(new(source.Id, true, ConfirmOwnWork: true)));

        Assert.Equal("source.import-derived", refused.Problems[0].Code);
        Assert.False(h.Temp.App.Store.FindSource(source.Id)!.Redistributable);
    }

    [Fact]
    public void Outbound_campaign_pack_leaves_out_the_import_derived_source_and_carries_none_of_its_content()
    {
        using var h = new DdbHarness();
        var (_, source, published) = Created(h);
        var campaign = h.Temp.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Test Tinker Table", RulesFamily = RulesFamilies.Srd51, AllowedSources = [source.Id] });

        var preview = h.Temp.App.PreviewCampaignPack(campaign.Id);
        Assert.Contains(preview.LeftOut, l => l.SourceId == source.Id && l.Reason.Code == "pack.source-import-derived");
        Assert.DoesNotContain(preview.Included, i => i.SourceId == source.Id);

        var pack = h.Temp.App.ExportCampaignPack(campaign.Id);
        Assert.Contains(pack.Manifest.Omitted, o => o.SourceId == source.Id && o.Revisions.Count == 0);
        Assert.DoesNotContain(Encoding.UTF8.GetString(pack.Content), published.RevisionId.ToString("D"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FoundryDnd5e.Target, SheetPurpose.Share)]
    [InlineData(FoundryDnd5e.Target, SheetPurpose.Personal)]
    [InlineData(SheetJson.Target, SheetPurpose.Share)]
    [InlineData(SheetJson.Target, SheetPurpose.Personal)]
    public void Outbound_sheet_and_foundry_exports_drop_the_stub_even_for_a_personal_copy(string target, SheetPurpose purpose)
    {
        using var h = new DdbHarness();
        var (id, source, _) = Created(h);

        var preview = h.Temp.App.PreviewVttExport(id, target, purpose);
        var text = Encoding.UTF8.GetString(h.Temp.App.VttExportOutput(preview.Token).Bytes);

        Assert.Contains(preview.Dropped, d => d.Source == source.Title);
        Assert.Contains(preview.Notices, n => n.Title == source.Title && n.TotalsOnly);
        Assert.DoesNotContain(Tinkerer, text, StringComparison.Ordinal);
    }
}
