using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Wave 3 G6 (task 6.1): the 2014-rules gear catalogue. Plain gear is v3 (the lowest a published revision can declare) and
/// has no effects; the seven equipment packs are v13 because they carry <c>contains</c>. Names and pack contents only.
/// </summary>
public class SrdGear51Tests
{
    private const string Resource = "TomeStack.Content.srd-5.1-gear.json";
    private static readonly ContentPack Gear = TomeStackApp.LoadBundledPack(Resource);

    private static bool IsPack(ContentRevision r) => r.Effects.OfType<ItemEffect>().Any(i => i.Contains is not null);

    [Fact]
    public void The_pack_is_bundled_after_the_armor_and_classes_it_sits_beside()
    {
        Assert.Contains(Resource, TomeStackApp.BundledPacks);
        Assert.True(TomeStackApp.BundledPacks.ToList().IndexOf(Resource) > TomeStackApp.BundledPacks.ToList().IndexOf("TomeStack.Content.srd-5.1-armor.json"));
    }

    [Fact]
    public void Every_revision_is_a_published_srd_5_1_item_that_validates_without_errors()
    {
        var all = TomeStackApp.BundledPacks.Select(TomeStackApp.LoadBundledPack).ToList();
        var catalog = new InMemoryContentCatalog([.. all.SelectMany(p => p.Sources).DistinctBy(s => s.Id)], [.. all.SelectMany(p => p.Revisions)]);

        Assert.NotEmpty(Gear.Revisions);
        foreach (var revision in Gear.Revisions)
        {
            Assert.Equal(ContentKind.Item, revision.Kind);
            Assert.Equal(RevisionStatus.Published, revision.Status);
            Assert.Equal(["srd-5.1"], revision.RulesFamilies);
            Assert.NotNull(revision.Provenance?.Page);
            var report = ContentValidator.Validate(revision, catalog);
            Assert.True(report.CanPublish, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => e.Message))}");
            Assert.Empty(report.Errors);
        }
    }

    [Fact]
    public void No_revision_has_a_weapon_or_armor_effect_and_plain_gear_declares_the_lowest_schema()
    {
        Assert.DoesNotContain(Gear.Revisions, r => r.Effects.Any(e => e is WeaponEffect or ArmorEffect));
        Assert.All(Gear.Revisions.Where(r => !IsPack(r)), r =>
        {
            Assert.Empty(r.Effects);
            Assert.Equal(ContentValidator.MinimumPublishedSchemaVersion, r.SchemaVersion);
        });
        Assert.All(Gear.Revisions.Where(IsPack), r => Assert.Equal(ContentRevision.CreationGuideSchemaVersion, r.SchemaVersion));
    }

    [Fact]
    public void There_are_seven_packs_and_every_contained_line_resolves_in_this_file_to_plain_gear()
    {
        var byContent = Gear.Revisions.ToDictionary(r => r.ContentId);
        var packs = Gear.Revisions.Where(IsPack).ToList();

        Assert.Equal(
            ["Burglar's pack", "Diplomat's pack", "Dungeoneer's pack", "Entertainer's pack", "Explorer's pack", "Priest's pack", "Scholar's pack"],
            [.. packs.Select(p => p.Name).Order()]);
        foreach (var line in packs.SelectMany(p => p.Effects.OfType<ItemEffect>().SelectMany(i => i.Contains!)))
        {
            Assert.True(byContent.TryGetValue(line.Item!.ContentId, out var target), $"{line.Item.ContentId} is not in the gear pack");
            Assert.False(IsPack(target!));
            Assert.InRange(line.Quantity ?? 1, 1, 999);
        }
    }

    [Fact]
    public void Pack_contents_and_quantities_match_the_SRD_lists()
    {
        var byContent = Gear.Revisions.ToDictionary(r => r.ContentId, r => r.Name);
        string Contents(string pack) => string.Join("; ", Gear.Revisions.Single(r => r.Name == pack).Effects.OfType<ItemEffect>().Single().Contains!
            .Select(l => $"{l.Quantity ?? 1} {byContent[l.Item!.ContentId]}"));

        Assert.Equal("1 Backpack; 1 Ball bearings (bag of 1,000); 1 String (10 feet); 1 Bell; 5 Candle; 1 Crowbar; 1 Hammer; 10 Piton; 1 Lantern, hooded; 2 Oil (flask); 5 Rations (1 day); 1 Tinderbox; 1 Waterskin; 1 Rope, hempen (50 feet)", Contents("Burglar's pack"));
        Assert.Equal("1 Backpack; 1 Crowbar; 1 Hammer; 10 Piton; 10 Torch; 1 Tinderbox; 10 Rations (1 day); 1 Waterskin; 1 Rope, hempen (50 feet)", Contents("Dungeoneer's pack"));
        Assert.Equal("1 Backpack; 1 Bedroll; 1 Mess kit; 1 Tinderbox; 10 Torch; 10 Rations (1 day); 1 Waterskin; 1 Rope, hempen (50 feet)", Contents("Explorer's pack"));
        Assert.Equal("1 Backpack; 1 Book; 1 Ink (1 ounce bottle); 1 Ink pen; 10 Parchment (one sheet); 1 Sand (little bag); 1 Knife, small", Contents("Scholar's pack"));
    }

    [Fact]
    public void Ids_are_unique_in_the_block_and_names_are_unique()
    {
        Assert.Equal(Gear.Revisions.Count, Gear.Revisions.Select(r => r.ContentId).Distinct().Count());
        Assert.Equal(Gear.Revisions.Count, Gear.Revisions.Select(r => r.RevisionId).Distinct().Count());
        Assert.Equal(Gear.Revisions.Count, Gear.Revisions.Select(r => r.Name).Distinct().Count());
        Assert.All(Gear.Revisions, r =>
        {
            Assert.StartsWith("51c00000-0000-4000-8000-000000500", r.ContentId.ToString("D"));
            Assert.StartsWith("51e00000-0000-4000-8000-000000500", r.RevisionId.ToString("D"));
        });
    }

    [Fact]
    public void The_pack_carries_no_prose_beyond_one_short_original_line()
    {
        Assert.All(Gear.Revisions, r => Assert.Contains(r.Summary, new[] { "Adventuring gear.", "Equipment pack." }));
    }
}
