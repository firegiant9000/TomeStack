using System.IO.Compression;
using System.Text.Json;
using TomeStack.AppService.Exports;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

internal enum PreflightSourceKind { Shareable, ImportDerived, NotRedistributable }

/// <param name="Feats">Published feats in the source.</param>
/// <param name="Draft">Whether an unpublished draft sits in the source too.</param>
internal sealed record PreflightSourceSpec(string Title, PreflightSourceKind Kind, int Feats, bool Draft = false);

internal sealed record PreflightLibrary(IReadOnlyList<Guid> Sources, IReadOnlyList<ContentReference> Published, Guid Character, Guid Campaign);

/// <summary>Import Lab L5 (design 9.1): <c>package.preflight</c> is the one rule for every share export. All content is original test data.</summary>
public class PreflightTests
{
    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% TomeStack test PDF\n%%EOF\n");

    /// <summary>Sources of each kind, with published feats (and a draft when asked), one character pinning every feat, and a campaign allowing every source.</summary>
    internal static PreflightLibrary Fill(TomeStackApp app, IReadOnlyList<PreflightSourceSpec> specs)
    {
        var sources = new List<Guid>();
        var published = new List<ContentReference>();
        foreach (var spec in specs)
        {
            var id = spec.Kind == PreflightSourceKind.Shareable
                ? app.CreateHomebrewSource(new(spec.Title, [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true)).Id
                : Guid.NewGuid();
            if (spec.Kind != PreflightSourceKind.Shareable)
            {
                app.Store.UpsertSource(new SourceRecord
                {
                    Id = id, Title = spec.Title, Publisher = "Test author", RulesFamilies = [RulesFamilies.Srd51], EditionVersion = "1", License = "Personal homebrew",
                    Redistributable = spec.Kind == PreflightSourceKind.ImportDerived, ImportDerived = spec.Kind == PreflightSourceKind.ImportDerived ? true : null,
                });
            }
            sources.Add(id);
            ContentRevision Feat(string name, RevisionStatus status) => new()
            {
                ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Feat, Name = name, RulesFamilies = [RulesFamilies.Srd51],
                Provenance = new(id), Status = status, SchemaVersion = ContentRevision.CurrentSchemaVersion,
                Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }],
            };
            for (var i = 0; i < spec.Feats; i++)
            {
                var feat = Feat($"Test Feat {spec.Title} {i}", RevisionStatus.Published);
                app.Store.AddRevision(feat);
                published.Add(feat.Reference);
            }
            if (spec.Draft)
                app.SaveDraft(Feat($"Test Draft {spec.Title}", RevisionStatus.Draft));
        }
        var fixture = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
        var character = app.SaveCharacter(fixture with { Pins = [.. fixture.Pins, .. published] }).Character.Id;
        var campaign = app.SaveCampaign(new() { Id = Guid.Empty, Name = "Test Preflight Table", RulesFamily = RulesFamilies.Srd51, AllowedSources = sources, HouseRules = "Test house rules." });
        return new(sources, published, character, campaign.Id);
    }

    internal static IReadOnlyList<PreflightSourceSpec> Mixed { get; } =
    [
        new("Test Lantern Notes", PreflightSourceKind.Shareable, 2, Draft: true),
        new("Test Ink Imports", PreflightSourceKind.ImportDerived, 1),
        new("Test Borrowed Rules", PreflightSourceKind.NotRedistributable, 1),
    ];

    private static IReadOnlyList<Guid> ContentIds(PackageManifest manifest) =>
        [.. manifest.Entries.Where(e => e.Kind == "contentRevision").Select(e => Guid.Parse(e.Path["content/".Length..^".json".Length]))];

    private static IReadOnlyList<Guid> Revisions(IEnumerable<PreflightRow> rows) => [.. rows.Where(r => r.RevisionId != Guid.Empty).Select(r => r.RevisionId).Order()];

    [Fact]
    public void Import_derived_is_omitted_with_reason()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, Mixed);
        var derived = temp.App.Store.ListSources().Single(s => s.Title == "Test Ink Imports");

        var report = temp.App.PackagePreflight(new(PreflightKind.Package, [library.Character]));

        var row = Assert.Single(report.Omitted, r => r.SourceName == derived.Title);
        Assert.Equal((Preflight.ImportDerivedReason, true), (row.Reason, row.ImportDerived));
        Assert.Contains(report.Omitted, r => r.SourceName == "Test Borrowed Rules" && r.Reason == Preflight.NotRedistributableReason);
        Assert.DoesNotContain(report.Included, r => r.SourceName is "Test Ink Imports" or "Test Borrowed Rules");
        Assert.Contains(report.Included, r => r.SourceName == "Test Lantern Notes");
        Assert.Empty(report.Blocking);
        // The export agrees: the derived source's revision is not in the package.
        Assert.DoesNotContain(library.Published[2].RevisionId, ContentIds(temp.App.ExportCharacters([library.Character], ExportPurpose.Share).Manifest));
    }

    [Fact]
    public void Not_redistributable_blocks_a_source_pack()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, Mixed);

        var report = temp.App.PackagePreflight(new(PreflightKind.SourcePack, [library.Sources[0], library.Sources[2]]));

        var blocked = Assert.Single(report.Blocking);
        Assert.Equal(("Test Borrowed Rules", Preflight.NotRedistributableReason, false), (blocked.Name, blocked.Reason, blocked.Redistributable));
        // ShareProblem parity: the export refuses the same source.
        var error = Assert.Single(Assert.Throws<PackageException>(() => temp.App.ExportSourcePack([library.Sources[0], library.Sources[2]])).Errors);
        Assert.Equal("pack.source-not-shareable", error.Code);

        var import = temp.App.PackagePreflight(new(PreflightKind.SourcePack, [library.Sources[1]]));
        Assert.Equal(Preflight.ImportDerivedReason, Assert.Single(import.Blocking).Reason);
        Assert.Empty(temp.App.PackagePreflight(new(PreflightKind.SourcePack, [library.Sources[0]])).Blocking);
    }

    [Fact]
    public void Attachments_and_extracted_text_never_included()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, [new("Test Lantern Notes", PreflightSourceKind.Shareable, 1), new("Test Plain Notes", PreflightSourceKind.Shareable, 1)]);
        var source = temp.App.Store.FindSource(library.Sources[0])!;
        temp.App.AttachPdf(source.Id, "book.pdf", Pdf); // attaching a PDF also makes the source import-derived, for good
        var attached = temp.App.Store.FindSource(source.Id)!;
        Assert.NotNull(attached.AttachmentId);

        // A source pack: the source is blocked, and its PDF is listed as left out, never as included.
        var report = temp.App.PackagePreflight(new(PreflightKind.SourcePack, [source.Id, library.Sources[1]]));
        var row = Assert.Single(report.Omitted, r => r.Reason == Preflight.AttachmentReason);
        Assert.Equal(attached.AttachmentId, row.ContentId);
        Assert.Equal(Preflight.ImportDerivedReason, Assert.Single(report.Blocking).Reason);
        Assert.DoesNotContain(report.Included, r => r.ContentId == attached.AttachmentId || r.SourceName == source.Title);

        // A character package: the PDF is listed, and the package holds no attachment record, PDF or the source's content.
        var package = temp.App.PackagePreflight(new(PreflightKind.Package, [library.Character]));
        Assert.Contains(package.Omitted, r => r.Reason == Preflight.AttachmentReason && r.ContentId == attached.AttachmentId);
        Assert.Contains(package.Omitted, r => r.Reason == Preflight.ImportDerivedReason && r.SourceName == source.Title);
        Assert.DoesNotContain(package.Included, r => r.SourceName == source.Title);
        var export = temp.App.ExportCharacters([library.Character], ExportPurpose.Share);
        Assert.DoesNotContain(export.Manifest.Entries, e => e.Path.StartsWith("attachments/", StringComparison.Ordinal) || e.Path.StartsWith("files/", StringComparison.Ordinal) || e.Path.EndsWith(".pdf", StringComparison.Ordinal));
        Assert.DoesNotContain(export.Manifest.Entries, e => e.Path == $"sources/{source.Id:D}.json");
        using var zip = new ZipArchive(new MemoryStream(export.Content));
        Assert.All(zip.Entries, e => Assert.DoesNotContain("pdf", e.FullName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Drafts_are_listed_as_omitted_and_never_carried()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, Mixed);

        var report = temp.App.PackagePreflight(new(PreflightKind.SourcePack, [library.Sources[0]]));

        Assert.Equal(("Test Draft Test Lantern Notes", Preflight.DraftReason), (Assert.Single(report.Omitted).Name, report.Omitted[0].Reason));
        Assert.Equal(2, report.Included.Count);
        Assert.Equal(Revisions(report.Included), [.. ContentIds(temp.App.ExportSourcePack([library.Sources[0]]).Manifest).Order()]);
    }

    [Fact]
    public void A_source_with_nothing_published_blocks_a_source_pack_and_is_left_out_of_a_campaign_pack()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, [new("Test Lantern Notes", PreflightSourceKind.Shareable, 0, Draft: true)]);

        Assert.Equal(Preflight.EmptyReason, Assert.Single(temp.App.PackagePreflight(new(PreflightKind.SourcePack, [library.Sources[0]])).Blocking).Reason);
        Assert.Equal("pack.source-empty", Assert.Single(Assert.Throws<PackageException>(() => temp.App.ExportSourcePack([library.Sources[0]])).Errors).Code);
        Assert.Contains(temp.App.PackagePreflight(new(PreflightKind.CampaignPack, [library.Campaign])).Omitted, r => r.Reason == Preflight.EmptyReason);
    }

    [Fact]
    public void Notices_listed_once_per_source()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, [new("Test Lantern Notes", PreflightSourceKind.Shareable, 3), new("Test Second Notes", PreflightSourceKind.Shareable, 2)]);

        var report = temp.App.PackagePreflight(new(PreflightKind.SourcePack, library.Sources));

        Assert.Equal(5, report.Included.Count);
        Assert.Equal(2, report.Notices.Count);
        Assert.Single(report.Notices, n => n.StartsWith("Test Lantern Notes", StringComparison.Ordinal));
        // Parity with Notice: the pack's manifest names the same sources, once each.
        var manifest = temp.App.ExportSourcePack(library.Sources).Manifest;
        Assert.Equal([.. manifest.Notices.Select(n => n.Title).Order()], [.. report.Notices.Select(n => n[..n.IndexOf(" (", StringComparison.Ordinal)]).Order()]);
    }

    [Fact]
    public void A_campaign_pack_lists_what_it_leaves_out_and_matches_the_export()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, Mixed);

        var report = temp.App.PackagePreflight(new(PreflightKind.CampaignPack, [library.Campaign]));

        Assert.Contains(report.Omitted, r => r.ContentId == library.Sources[1] && r.Reason == Preflight.ImportDerivedReason);
        Assert.Contains(report.Omitted, r => r.ContentId == library.Sources[2] && r.Reason == Preflight.NotRedistributableReason);
        Assert.Contains(report.Omitted, r => r.Reason == Preflight.DraftReason);
        Assert.Equal(Revisions(report.Included), [.. ContentIds(temp.App.ExportCampaignPack(library.Campaign).Manifest).Order()]);
    }

    [Fact]
    public void A_sheet_share_includes_what_it_lets_out_and_omits_the_rest()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, Mixed);

        var report = temp.App.PackagePreflight(new(PreflightKind.Sheet, [library.Character]));

        Assert.Contains(report.Included, r => r.RevisionId == library.Published[0].RevisionId);
        Assert.Contains(report.Omitted, r => r.RevisionId == library.Published[2].RevisionId && r.Reason == Preflight.ImportDerivedReason);
        Assert.Contains(report.Omitted, r => r.RevisionId == library.Published[3].RevisionId && r.Reason == Preflight.NotRedistributableReason);
        var view = temp.App.GetCharacter(library.Character);
        var sheet = SheetExportBuilder.Build(view.Character, view.Sheet, temp.App.Store, SheetPurpose.Share, new HashSet<Guid>(), TempApp.Now);
        Assert.Contains(sheet.Features, f => f.Ref.RevisionId == library.Published[0].RevisionId);
        Assert.DoesNotContain(sheet.Features, f => f.Ref.RevisionId == library.Published[2].RevisionId || f.Ref.RevisionId == library.Published[3].RevisionId);
    }

    [Fact]
    public void Backup_purpose_is_unchanged()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, Mixed);
        var store = temp.App.Store;

        // A backup is personal: every pinned revision, every source, nothing omitted, whatever the sharing rule says.
        var backup = temp.App.ExportCharacters([library.Character], ExportPurpose.Backup);
        var character = store.FindCharacter(library.Character)!;
        var expectedRevisions = character.AllReferences().Select(r => r.RevisionId).Distinct().Order().ToList();
        Assert.Equal(expectedRevisions, [.. ContentIds(backup.Manifest).Order()]);
        Assert.Contains(library.Published[2].RevisionId, ContentIds(backup.Manifest));
        Assert.Contains(library.Published[3].RevisionId, ContentIds(backup.Manifest));
        Assert.Empty(backup.Manifest.Omitted);
        Assert.All(library.Sources, id => Assert.Contains(backup.Manifest.Entries, e => e.Path == $"sources/{id:D}.json"));
        Assert.EndsWith("-personal-backup.tomestack.zip", backup.FileName, StringComparison.Ordinal);
        Assert.Equal(ExportPurpose.Backup, temp.App.PreviewExport([library.Character], ExportPurpose.Backup).Purpose);

        // The library backup ignores the sharing rule too: the import-derived and non-redistributable sources and their content are in.
        using var output = new MemoryStream();
        temp.App.WriteLibraryBackup(output);
        using var zip = new ZipArchive(new MemoryStream(output.ToArray()));
        Assert.All(library.Sources, id => Assert.NotNull(zip.GetEntry($"sources/{id:D}.json")));
        Assert.All(library.Published, r => Assert.NotNull(zip.GetEntry($"content/{r.RevisionId:D}.json")));

        // A personal sheet keeps its own rule: your own homebrew goes out in full, an import-derived source never does.
        var local = store.FindSource(library.Sources[2])!;
        store.UpsertSource(local with { Origin = SourceOrigin.Local });
        Assert.True(SheetExportBuilder.LetsOut(store.FindSource(library.Sources[2])!, SheetPurpose.Personal, new HashSet<Guid>()));
        Assert.False(SheetExportBuilder.LetsOut(store.FindSource(library.Sources[2])!, SheetPurpose.Share, new HashSet<Guid>()));
        Assert.False(SheetExportBuilder.LetsOut(store.FindSource(library.Sources[1])!, SheetPurpose.Personal, new HashSet<Guid>()));
    }

    [Fact]
    public void Export_replans_if_the_library_changed()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, [new("Test Lantern Notes", PreflightSourceKind.Shareable, 1)]);
        var feat = library.Published[0];
        var before = temp.App.PackagePreflight(new(PreflightKind.Package, [library.Character]));
        Assert.Contains(before.Included, r => r.RevisionId == feat.RevisionId);

        // Review Focus 5: the preview is stale. The source becomes import-derived before the export is made.
        var source = temp.App.Store.FindSource(library.Sources[0])!;
        temp.App.Store.UpsertSource(source with { ImportDerived = true, ShareConfirmedAt = null });

        var export = temp.App.ExportCharacters([library.Character], ExportPurpose.Share);
        Assert.DoesNotContain(feat.RevisionId, ContentIds(export.Manifest));
        Assert.Contains(export.Manifest.Omitted, o => o.SourceId == source.Id);
        Assert.Contains(temp.App.PackagePreflight(new(PreflightKind.Package, [library.Character])).Omitted, r => r.RevisionId == feat.RevisionId && r.Reason == Preflight.ImportDerivedReason);
        Assert.Equal(Preflight.ImportDerivedReason, Assert.Single(temp.App.PackagePreflight(new(PreflightKind.SourcePack, [source.Id])).Blocking).Reason);
        Assert.Equal("pack.source-import-derived", Assert.Single(Assert.Throws<PackageException>(() => temp.App.ExportSourcePack([source.Id])).Errors).Code);
        Assert.DoesNotContain(temp.App.ExportCampaignPack(library.Campaign).Manifest.Entries, e => e.Kind == "contentRevision");
    }

    [Fact]
    public void Preflight_writes_nothing()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, Mixed);
        string Snapshot() => JsonSerializer.Serialize(new
        {
            revisions = temp.App.Store.ListRevisionsInOrder().Select(r => $"{r.RevisionId}:{temp.App.Store.RevisionHash(r.RevisionId)}"),
            sources = temp.App.Store.ListSources(),
            characters = temp.App.Store.ListCharacters(),
            campaigns = temp.App.Store.ListCampaigns(),
            attachments = temp.App.Store.ListAttachments(),
            folder = System.IO.Directory.EnumerateFileSystemEntries(temp.App.DataDirectory, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".db", StringComparison.Ordinal) && !p.Contains(".db-", StringComparison.Ordinal)).Order(),
        }, RulesJson.Options);
        var before = Snapshot();

        foreach (var request in new PreflightRequest[]
        {
            new(PreflightKind.Package, [library.Character]), new(PreflightKind.Sheet, [library.Character]),
            new(PreflightKind.SourcePack, library.Sources), new(PreflightKind.CampaignPack, [library.Campaign]),
        })
            temp.App.PackagePreflight(request);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void The_command_answers_with_the_report_in_camel_case()
    {
        using var temp = new TempApp();
        var library = Fill(temp.App, Mixed);
        var dispatcher = new CommandDispatcher(temp.App);

        var response = JsonDocument.Parse(dispatcher.Dispatch($$$"""{"id":"1","command":"package.preflight","payload":{"kind":"sourcePack","selection":["{{{library.Sources[2]}}}"]}}""")).RootElement;

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var result = response.GetProperty("result");
        var blocking = Assert.Single(result.GetProperty("blocking").EnumerateArray());
        Assert.Equal("preflight.not-redistributable", blocking.GetProperty("reason").GetString());
        Assert.Equal("Test Borrowed Rules", blocking.GetProperty("sourceName").GetString());
        Assert.Equal(JsonValueKind.Array, result.GetProperty("notices").ValueKind);
        Assert.Contains("package.preflight", CommandDispatcher.Commands);
    }
}
