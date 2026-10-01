using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>
/// T3 property 5 (ADR-007 packages, docs/features/package-format.md, extending <see cref="M6ExitGateTests"/>): for a
/// generated library, a library backup restored into a clean data folder (with a later clock) backs up again to the same
/// manifest and entry hashes; a source pack and a campaign pack import into a clean folder with every revision and hash
/// equal, and importing the same pack again changes nothing. A received source cannot be packed again (package-format
/// rule 11), so for those two scopes the second export is replaced by the second import.
/// </summary>
public class PackageProperties
{
    /// <summary>A content item: the initiative bonus of each published revision in turn, and whether a draft follows.</summary>
    public sealed record FeatSpec(string Name, IReadOnlyList<int> Revisions, bool Draft);

    public sealed record SourceSpec(string Title, IReadOnlyList<string> Families, IReadOnlyList<FeatSpec> Feats);

    /// <param name="Characters">Fixture characters to save, each pinning one published revision of its own rules family when there is one.</param>
    public sealed record LibrarySpec(IReadOnlyList<SourceSpec> Sources, IReadOnlyList<string> Characters, bool GapNote, string CampaignFamily)
    {
        public override string ToString() =>
            $"{Sources.Count} sources ({string.Join(", ", Sources.Select(s => $"{string.Join("+", s.Families)}: {string.Join(" ", s.Feats.Select(f => $"{f.Revisions.Count}{(f.Draft ? "+d" : "")}"))}"))}), characters [{string.Join(", ", Characters)}], gap note {GapNote}, campaign {CampaignFamily}";
    }

    private static readonly string[] CharacterFiles = ["characters/srd51-quickfoot.json", "characters/srd521-courier.json"];

    public static Gen<LibrarySpec> Libraries { get; } =
        from sources in (from title in Generators.Names
                         from families in Generators.ContentFamilies
                         from feats in (from name in Generators.Names
                                        from revisions in Gen.Choose(-3, 5).ArrayOf().Select(r => r.Take(3).DefaultIfEmpty(1).ToArray())
                                        from draft in Gen.Elements(true, false)
                                        select new FeatSpec("Test " + name, revisions, draft)).ArrayOf().Select(f => f.Take(3).DefaultIfEmpty(new FeatSpec("Test Steady Hands", [1], false)).ToArray())
                         select new SourceSpec("Test " + title, families, feats)).ArrayOf().Select(s => s.Take(2).ToArray())
        from count in Gen.Choose(0, 2)
        from characters in Gen.Elements(CharacterFiles).ArrayOf(count)
        from note in Gen.Elements(true, false)
        from campaignFamily in Gen.Elements(Generators.Families)
        select new LibrarySpec(sources.Length > 0 ? sources : [new SourceSpec("Test Lantern Notes", [RulesFamilies.Srd51], [new("Test Steady Hands", [1], false)])], [.. characters.Distinct()], note, campaignFamily);

    private sealed record Library(IReadOnlyList<Guid> Sources, IReadOnlyList<ContentReference> Published, Guid Campaign);

    private static Library Fill(TomeStackApp app, LibrarySpec spec)
    {
        var sources = new List<Guid>();
        var published = new List<(ContentReference Reference, IReadOnlyList<string> Families)>();
        foreach (var s in spec.Sources)
        {
            var source = app.CreateHomebrewSource(new(s.Title, s.Families));
            foreach (var feat in s.Feats)
            {
                var contentId = Guid.NewGuid();
                ContentRevision Draft(int bonus) => new()
                {
                    ContentId = contentId, RevisionId = Guid.Empty, Kind = ContentKind.Feat, Name = feat.Name, RulesFamilies = s.Families,
                    Provenance = new(source.Id), Status = RevisionStatus.Draft, Summary = "Test property feat.",
                    Effects = [new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = $"{bonus}" }],
                };
                foreach (var bonus in feat.Revisions)
                    published.Add((app.Publish(app.SaveDraft(Draft(bonus))).Published, s.Families));
                if (feat.Draft)
                    app.SaveDraft(Draft(9));
            }
            sources.Add(app.SetShareable(new(source.Id, Shareable: true, ConfirmOwnWork: true)).Id);
        }
        var characterIds = new List<Guid>();
        foreach (var file in spec.Characters)
        {
            var fixture = TempApp.LoadFixture<Character>(file);
            var pin = published.Where(p => p.Families.Contains(fixture.RulesFamily)).Select(p => p.Reference).LastOrDefault();
            characterIds.Add(app.SaveCharacter(pin is null ? fixture : fixture with { Pins = [.. fixture.Pins, pin] }).Character.Id);
        }
        if (spec.GapNote && characterIds.Count > 0)
            app.AddGapNote(new(characterIds[0], new(GapTargetKind.Field, FieldId: FieldIds.Initiative), "Test note: a generated library."));
        var campaign = app.SaveCampaign(new() { Id = Guid.Empty, Name = "Test Generated Table", RulesFamily = spec.CampaignFamily, AllowedSources = sources, HouseRules = "Test house rules." });
        return new(sources, [.. published.Select(p => p.Reference)], campaign.Id);
    }

    /// <summary>Everything a library holds, as in <see cref="M6ExitGateTests"/>: revisions with their stored hashes, sources, characters and sheets, campaigns, gap notes and attachments.</summary>
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
        }, RulesJson.Options);
    }

    private static string Backup(TempApp temp, string name)
    {
        var path = Path.Combine(temp.Directory, "..", name);
        using var file = File.Create(path);
        temp.App.WriteLibraryBackup(file);
        return path;
    }

    /// <summary>The manifest without its creation time, which is the clock of the machine that wrote it.</summary>
    private static string Manifest(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("manifest.json")!.Open();
        var manifest = JsonNode.Parse(stream)!.AsObject();
        manifest.Remove("createdAt");
        return manifest.ToJsonString();
    }

    private static string Revisions(TomeStackApp app, IEnumerable<ContentReference> references) =>
        string.Join("\n", references.Select(r => $"{TempApp.Json(app.Store.FindRevision(r))}:{app.Store.RevisionHash(r.RevisionId)}"));

    [Property(MaxTest = 12)]
    public Property A_library_backup_restored_into_a_clean_folder_backs_up_to_the_same_manifest_and_hashes() =>
        Prop.ForAll(Libraries.ToArbitrary(), spec =>
        {
            using var author = new TempApp();
            Fill(author.App, spec);
            var first = Backup(author, "first.tomestack.zip");

            using var clean = new TempApp(now: TempApp.Now.AddDays(1));
            Assert.True(clean.App.PreviewLibraryRestore(first).CanApply);
            clean.App.ApplyLibraryRestore(first);
            var second = Backup(clean, "second.tomestack.zip");

            Assert.Equal(Snapshot(author.App), Snapshot(clean.App));
            Assert.Equal(Manifest(first), Manifest(second));
            return true;
        });

    [Property(MaxTest = 12)]
    public Property A_source_pack_imports_into_a_clean_folder_equal_and_a_second_import_changes_nothing() =>
        Prop.ForAll(Libraries.ToArbitrary(), spec =>
        {
            using var author = new TempApp();
            var library = Fill(author.App, spec);
            var pack = author.App.ExportSourcePack(library.Sources).Content;

            using var clean = new TempApp(now: TempApp.Now.AddDays(1));
            Assert.True(clean.App.PreviewImport(pack).CanApply);
            clean.App.ApplyImport(pack);
            Assert.Equal(Revisions(author.App, library.Published), Revisions(clean.App, library.Published));
            foreach (var id in library.Sources)
            {
                var received = clean.App.Store.FindSource(id)!;
                Assert.Equal(TempApp.Json(author.App.Store.FindSource(id)! with { Origin = null, ShareConfirmedAt = null }), TempApp.Json(received with { Origin = null, ShareConfirmedAt = null }));
                Assert.Equal(SourceOrigin.Received, received.Origin);
            }

            var before = Snapshot(clean.App);
            var again = clean.App.PreviewImport(pack);
            Assert.All(again.Items, i => Assert.Equal(PackageItemAction.Unchanged, i.Action));
            clean.App.ApplyImport(pack);
            Assert.Equal(before, Snapshot(clean.App));
            return true;
        });

    [Property(MaxTest = 12)]
    public Property A_campaign_pack_imports_into_a_clean_folder_equal_and_a_second_import_changes_nothing() =>
        Prop.ForAll(Libraries.ToArbitrary(), spec =>
        {
            using var author = new TempApp();
            var library = Fill(author.App, spec);
            var pack = author.App.ExportCampaignPack(library.Campaign);
            Assert.Empty(pack.Manifest.Characters);

            using var clean = new TempApp(now: TempApp.Now.AddDays(1));
            Assert.True(clean.App.PreviewImport(pack.Content).CanApply);
            clean.App.ApplyImport(pack.Content);
            Assert.Equal(TempApp.Json(author.App.Store.FindCampaign(library.Campaign)), TempApp.Json(Assert.Single(clean.App.ListCampaigns())));
            Assert.Equal(Revisions(author.App, library.Published), Revisions(clean.App, library.Published));

            var before = Snapshot(clean.App);
            Assert.All(clean.App.PreviewImport(pack.Content).Items, i => Assert.Equal(PackageItemAction.Unchanged, i.Action));
            clean.App.ApplyImport(pack.Content);
            Assert.Equal(before, Snapshot(clean.App));
            return true;
        });
}
