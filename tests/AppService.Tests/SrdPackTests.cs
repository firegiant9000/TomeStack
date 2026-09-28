using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M1 item 1: SRD 5.1 and SRD 5.2.1 ship as separate source packs (separate sources, content IDs and revisions) under
/// CC-BY-4.0, each carrying the approved attribution verbatim (docs/licensing/srd-attribution-draft.md) and a
/// modification notice. Every revision validates, and a fresh data folder has SRD content but no test fixtures.
/// </summary>
public class SrdPackTests
{
    private static readonly ContentPack Srd51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1.json");
    private static readonly ContentPack Srd521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1.json");

    public static TheoryData<string, string, string> Packs() => new()
    {
        { "srd-5.1", RulesFamilies.Srd51, "2504d2a0abb0a4d491a939be4f17910a2dde0312570ab8d208080225ccf0a1f0" },
        { "srd-5.2.1", RulesFamilies.Srd521, "8974902d109d6e63672d7c490bde9ccf052410503d9cfa768237154fbc5e3d87" },
    };

    private static ContentPack Pack(string id) => id == "srd-5.1" ? Srd51 : Srd521;

    [Theory]
    [MemberData(nameof(Packs))]
    public void Each_pack_has_one_CC_BY_source_with_the_approved_attribution_verbatim(string packId, string family, string pdfHash)
    {
        var source = Assert.Single(Pack(packId).Sources);
        var approved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "licensing", "srd-attribution-draft.md"));

        Assert.Equal(("CC-BY-4.0", true, pdfHash), (source.License, source.Redistributable, source.Sha256));
        Assert.Equal([family], source.RulesFamilies);
        Assert.Contains($"> {source.Attribution}", approved, StringComparison.Ordinal); // verbatim, as approved
        Assert.StartsWith("Modified: ", source.ModificationNotice, StringComparison.Ordinal);
        Assert.Contains(source.ModificationNotice!, approved, StringComparison.Ordinal); // the recorded §3 wording
        // "Do not include any other attribution to Wizards": only the attribution statement names them.
        Assert.DoesNotContain("Wizards", source.Publisher + source.Title + source.ModificationNotice, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void Every_revision_belongs_to_its_own_family_and_source_and_cites_a_page(string packId, string family, string _)
    {
        var pack = Pack(packId);
        var sourceId = Assert.Single(pack.Sources).Id;

        Assert.All(pack.Revisions, r =>
        {
            Assert.Equal([family], r.RulesFamilies);
            Assert.Equal(sourceId, r.Provenance.SourceId);
            Assert.NotNull(r.Provenance.Page);
            Assert.Equal(RevisionStatus.Published, r.Status);
        });
    }

    [Fact]
    public void The_two_packs_share_no_source_or_content_ids_even_where_names_match()
    {
        Assert.Empty(Srd51.Sources.Select(s => s.Id).Intersect(Srd521.Sources.Select(s => s.Id)));
        Assert.Empty(Srd51.Revisions.Select(r => r.ContentId).Intersect(Srd521.Revisions.Select(r => r.ContentId)));
        Assert.Empty(Srd51.Revisions.Select(r => r.RevisionId).Intersect(Srd521.Revisions.Select(r => r.RevisionId)));
        // "Barbarian", "Rage" and others exist in both: same name, different content (never merged by name, SPEC S-03).
        Assert.Contains("Barbarian", Srd51.Revisions.Select(r => r.Name).Intersect(Srd521.Revisions.Select(r => r.Name)));
    }

    [Fact]
    public void Every_revision_validates_without_errors()
    {
        var catalog = new InMemoryContentCatalog([.. Srd51.Sources, .. Srd521.Sources], [.. Srd51.Revisions, .. Srd521.Revisions]);

        foreach (var revision in Srd51.Revisions.Concat(Srd521.Revisions))
        {
            var report = ContentValidator.Validate(revision, catalog);
            Assert.True(report.CanPublish, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => e.Message))}");
        }
    }

    [Fact]
    public void The_slice_is_one_species_one_background_one_class_with_its_subclass_and_feats()
    {
        foreach (var pack in new[] { Srd51, Srd521 })
        {
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Species);
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Background);
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Class);
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Subclass);
            Assert.Single(pack.Revisions, r => r.Kind == ContentKind.Feat);
        }
    }

    [Fact]
    public void A_fresh_data_folder_gets_the_SRD_packs_and_no_test_fixtures()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: []);
        try
        {
            var sources = app.Store.ListSources();
            Assert.Equal(["System Reference Document 5.1", "System Reference Document 5.2.1"], sources.Select(s => s.Title).Order());
            Assert.Equal(Srd51.Revisions.Count + Srd521.Revisions.Count, app.Store.ListRevisions().Count);
            Assert.DoesNotContain(app.ListContent(RulesFamilies.Srd51), o => o.Name.StartsWith("Fixture", StringComparison.Ordinal));
        }
        finally
        {
            app.Dispose();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void Package_notices_carry_the_attribution_and_the_modification_notice()
    {
        using var temp = new TempApp();
        var halfOrc = Srd51.Revisions.Single(r => r.Name == "Half-Orc");
        var character = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json") with { Pins = [halfOrc.Reference] };
        var saved = temp.App.SaveCharacter(character);

        var notice = Assert.Single(temp.App.ExportCharacters([saved.Character.Id], Packages.ExportPurpose.Share).Manifest.Notices);

        Assert.Equal(Srd51.Sources[0].Attribution, notice.Attribution);
        Assert.Equal(Srd51.Sources[0].ModificationNotice, notice.ModificationNotice);
    }
}
