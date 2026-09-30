using System.Text;
using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M6 exit gate (ROADMAP "M6 plan" slice 6), fixture-verified: homebrew written by following the class guide, a campaign
/// that allows it, a character in that campaign and the extension guide's sample, installed and granted. A source pack, a
/// campaign pack and a full library backup each go into a clean data folder and compare equal; the restored extension
/// comes back turned off, is granted again and writes the same file. The cross-machine part stays an owner check.
/// </summary>
public class M6ExitGateTests
{
    private static readonly Guid Srd521Source = Guid.Parse("52500000-0000-4000-8000-000000000001");

    private sealed record Library(SourceRecord Source, ContentReference Feature, ContentReference Class, Campaign Campaign, Guid Character, Guid Extension);

    private static Library Fill(TempApp temp)
    {
        var app = temp.App;
        var (made, feature, klass) = AuthoringGuideTests.FollowClassGuide(temp);
        var source = app.SetShareable(new(made.Id, Shareable: true, ConfirmOwnWork: true)); // source-pack guide, step 1
        var campaign = app.SaveCampaign(new() { Id = Guid.Empty, Name = "Test Lantern Table", RulesFamily = RulesFamilies.Srd521, AllowedSources = [Srd521Source, source.Id], HouseRules = "Test house rules: lanterns stay lit." });
        var character = app.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Lantern Keeper", RulesFamily = RulesFamilies.Srd521, Level = 5, CampaignId = campaign.Id,
            Classes = [new(klass, 5)], BaseAbilities = new(10, 12, 14, 10, 16, 13),
        }).Character.Id;
        var preview = app.PreviewExtensionInstall(AuthoringGuideTests.GuideExtension());
        Assert.True(preview.CanInstall, string.Join("; ", preview.Errors.Select(e => e.Code)));
        var extension = app.InstallExtension(preview.Token!.Value, ["read.sheet", "export.file"], confirm: true).Id;
        // What else a library holds (review fix): a draft, a gap note, and a source made import-derived by a managed PDF.
        app.SaveDraft(new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = "Test Unfinished Lantern Trick",
            RulesFamilies = [RulesFamilies.Srd521], Provenance = new(source.Id), Status = RevisionStatus.Draft, Summary = "Test draft text.",
        });
        app.AddGapNote(new(character, new(GapTargetKind.Field, FieldId: FieldIds.Initiative), "Test note: the table allows lantern light."));
        var scanned = app.CreateHomebrewSource(new("Test Scanned Notes", [RulesFamilies.Srd521]));
        app.AttachPdf(scanned.Id, "scanned-notes.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n% TomeStack M6 exit gate test PDF\n%%EOF\n"));
        Assert.True(app.Store.FindSource(scanned.Id)!.ImportDerived);
        return new(source, feature, klass, campaign, character, extension);
    }

    /// <summary>
    /// What the library holds, serialized, so two data folders compare as a whole: revisions (drafts included), sources,
    /// characters and their sheets, campaigns, gap notes, attachments and extensions. Left out: an extension's grants and
    /// on/off state, which never travel (asserted separately), and snapshots, which backups leave out (D14).
    /// </summary>
    private static string Snapshot(TomeStackApp app)
    {
        var store = app.Store;
        return JsonSerializer.Serialize(new
        {
            revisions = store.ListRevisionsInOrder().Select(r => $"{r.RevisionId}:{store.RevisionHash(r.RevisionId)}"),
            sources = store.ListSources(),
            characters = store.ListCharacters(),
            sheets = store.ListCharacters().Select(c => app.GetCharacter(c.Id).Sheet),
            campaigns = store.ListCampaigns(),
            gapNotes = store.ListAllGapNotes(),
            attachments = store.ListAttachments(),
            extensions = app.ListExtensions().Select(e => new { e.Id, e.Sha256, e.Manifest }),
        }, RulesJson.Options);
    }

    private static string Csv(TomeStackApp app, Guid extension, Guid character)
    {
        var run = app.PreviewExtensionRun(new(extension, "features-md", CharacterId: character));
        return Encoding.UTF8.GetString(app.ExtensionExportOutput(run.Token).Bytes);
    }

    [Fact]
    public void A_source_pack_round_trips_into_a_clean_data_folder_and_compares_equal()
    {
        using var author = new TempApp();
        var library = Fill(author);
        var pack = author.App.ExportSourcePack([library.Source.Id]).Content;

        using var clean = new TempApp();
        Assert.True(clean.App.PreviewImport(pack).CanApply);
        clean.App.ApplyImport(pack);
        var published = author.App.Store.ListRevisionsInOrder().Where(r => r.Provenance.SourceId == library.Source.Id && r.Status == RevisionStatus.Published).ToList();
        Assert.Equal(2, published.Count);
        Assert.Equal(
            published.Select(r => TempApp.Json(r)),
            clean.App.Store.ListRevisionsInOrder().Where(r => r.Provenance.SourceId == library.Source.Id).Select(r => TempApp.Json(r)));
        var received = clean.App.Store.FindSource(library.Source.Id)!;
        // Equal but for what the receiving machine records: that it was received, and no share confirmation of its own.
        Assert.Equal(TempApp.Json(library.Source with { Origin = null, ShareConfirmedAt = null }), TempApp.Json(received with { Origin = null, ShareConfirmedAt = null }));
        Assert.Equal((SourceOrigin.Received, (DateTimeOffset?)null), (received.Origin, received.ShareConfirmedAt));
    }

    [Fact]
    public void A_campaign_pack_round_trips_into_a_clean_data_folder_and_compares_equal()
    {
        using var author = new TempApp();
        var library = Fill(author);
        var pack = author.App.ExportCampaignPack(library.Campaign.Id);
        Assert.Empty(pack.Manifest.Characters); // a campaign pack never carries characters

        using var clean = new TempApp();
        Assert.True(clean.App.PreviewImport(pack.Content).CanApply);
        clean.App.ApplyImport(pack.Content);
        Assert.Equal(TempApp.Json(author.App.Store.FindCampaign(library.Campaign.Id)), TempApp.Json(Assert.Single(clean.App.ListCampaigns())));
        foreach (var reference in new[] { library.Feature, library.Class })
            Assert.Equal(TempApp.Json(author.App.Store.FindRevision(reference)), TempApp.Json(clean.App.Store.FindRevision(reference)));
        var received = clean.App.Store.FindSource(library.Source.Id)!;
        Assert.Equal((SourceOrigin.Received, (DateTimeOffset?)null), (received.Origin, received.ShareConfirmedAt));    }

    [Fact]
    public void A_library_backup_round_trips_into_a_clean_data_folder_and_compares_equal_and_the_extension_runs_again()
    {
        using var author = new TempApp();
        var library = Fill(author);
        var expected = Csv(author.App, library.Extension, library.Character);
        var backup = Path.Combine(author.Directory, "..", "m6-gate.tomestack.zip");
        using (var file = File.Create(backup))
            Assert.Equal(1, author.App.WriteLibraryBackup(file).Contents.Extensions);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(backup))
        using (var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open()))
            Assert.Equal(PackageManifest.LibraryExtensionsFormatVersion, manifest.RootElement.GetProperty("formatVersion").GetInt32());

        // A later clock: a restore must keep the original times, not stamp its own (review fix).
        using var clean = new TempApp(now: TempApp.Now.AddDays(1));
        Assert.True(clean.App.PreviewLibraryRestore(backup).CanApply);
        clean.App.ApplyLibraryRestore(backup);
        Assert.Equal(Snapshot(author.App), Snapshot(clean.App));

        var restored = Assert.Single(clean.App.ListExtensions());
        Assert.Equal((false, 0), (restored.Enabled, restored.Grants.Count)); // grants never travel
        var review = clean.App.PreviewInstalledExtension(restored.Id);
        clean.App.InstallExtension(review.Token!.Value, ["read.sheet", "export.file"], confirm: true);
        Assert.Equal(expected, Csv(clean.App, restored.Id, library.Character));
    }
}
