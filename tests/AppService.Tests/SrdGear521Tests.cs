using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Wave 3 G7 (task 7.1): the 2024-rules gear catalogue. Plain gear is v3 (the lowest a published revision can declare) and
/// has no effects; the seven equipment packs are v13 because they carry <c>contains</c>. Names and pack contents only.
/// </summary>
public class SrdGear521Tests
{
    private const string Resource = "TomeStack.Content.srd-5.2.1-gear.json";
    private static readonly ContentPack Gear = TomeStackApp.LoadBundledPack(Resource);

    private static bool IsPack(ContentRevision r) => r.Effects.OfType<ItemEffect>().Any(i => i.Contains is not null);

    [Fact]
    public void The_pack_is_bundled_after_the_armor_it_sits_beside()
    {
        var packs = TomeStackApp.BundledPacks.ToList();
        Assert.Contains(Resource, packs);
        Assert.True(packs.IndexOf(Resource) > packs.IndexOf("TomeStack.Content.srd-5.2.1-armor.json"));
    }

    [Fact]
    public void Every_revision_is_a_published_srd_5_2_1_item_that_validates_without_errors()
    {
        var all = TomeStackApp.BundledPacks.Select(TomeStackApp.LoadBundledPack).ToList();
        var catalog = new InMemoryContentCatalog([.. all.SelectMany(p => p.Sources).DistinctBy(s => s.Id)], [.. all.SelectMany(p => p.Revisions)]);

        Assert.Equal(89, Gear.Revisions.Count(r => !IsPack(r)));
        Assert.Equal(7, Gear.Revisions.Count(IsPack));
        foreach (var revision in Gear.Revisions)
        {
            Assert.Equal(ContentKind.Item, revision.Kind);
            Assert.Equal(RevisionStatus.Published, revision.Status);
            Assert.Equal(["srd-5.2.1"], revision.RulesFamilies);
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
            ["Burglar’s Pack", "Diplomat’s Pack", "Dungeoneer’s Pack", "Entertainer’s Pack", "Explorer’s Pack", "Priest’s Pack", "Scholar’s Pack"],
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

        Assert.Equal("1 Backpack; 1 Ball Bearings; 1 Bell; 10 Candle; 1 Crowbar; 1 Lantern, Hooded; 7 Oil (flask); 5 Rations (1 day); 1 Rope; 1 Tinderbox; 1 Waterskin", Contents("Burglar’s Pack"));
        Assert.Equal("1 Chest; 1 Clothes, Fine; 1 Ink; 5 Ink Pen; 1 Lamp; 2 Case, Map or Scroll; 4 Oil (flask); 5 Paper (one sheet); 5 Parchment (one sheet); 1 Perfume; 1 Tinderbox", Contents("Diplomat’s Pack"));
        Assert.Equal("1 Backpack; 1 Caltrops; 1 Crowbar; 2 Oil (flask); 10 Rations (1 day); 1 Rope; 1 Tinderbox; 10 Torch; 1 Waterskin", Contents("Dungeoneer’s Pack"));
        Assert.Equal("1 Backpack; 1 Bedroll; 1 Bell; 1 Lantern, Bullseye; 3 Costume; 1 Mirror; 8 Oil (flask); 9 Rations (1 day); 1 Tinderbox; 1 Waterskin", Contents("Entertainer’s Pack"));
        Assert.Equal("1 Backpack; 1 Bedroll; 2 Oil (flask); 10 Rations (1 day); 1 Rope; 1 Tinderbox; 10 Torch; 1 Waterskin", Contents("Explorer’s Pack"));
        Assert.Equal("1 Backpack; 1 Blanket; 1 Holy Water; 1 Lamp; 7 Rations (1 day); 1 Robe; 1 Tinderbox", Contents("Priest’s Pack"));
        Assert.Equal("1 Backpack; 1 Book; 1 Ink; 1 Ink Pen; 1 Lamp; 10 Oil (flask); 10 Parchment (one sheet); 1 Tinderbox", Contents("Scholar’s Pack"));
    }

    [Fact]
    public void Every_item_the_2024_class_and_background_kits_name_is_in_the_catalogue()
    {
        var names = Gear.Revisions.Select(r => r.Name).ToHashSet();
        string[] named =
        [
            "Quiver", "Pouch", "Book", "Spellbook", "Robe", "Crowbar", "Healer’s Kit", "Herbalism Kit", "Thieves’ Tools",
            "Calligrapher’s Supplies", "Clothes, Traveler’s", "Parchment (one sheet)", "Arrow",
            "Arcane Focus (Crystal)", "Arcane Focus (Orb)", "Arcane Focus (Staff)",
            "Druidic Focus (Sprig of Mistletoe)", "Druidic Focus (Wooden Staff)", "Holy Symbol (Amulet)",
            "Gaming Set (Dice)", "Musical Instrument (Lute)", "Bolt", "Bullet, Sling", "Needle",
        ];
        Assert.All(named, n => Assert.Contains(n, names));
    }

    [Fact]
    public void Ids_are_unique_in_the_block_and_names_are_unique()
    {
        Assert.Equal(Gear.Revisions.Count, Gear.Revisions.Select(r => r.ContentId).Distinct().Count());
        Assert.Equal(Gear.Revisions.Count, Gear.Revisions.Select(r => r.RevisionId).Distinct().Count());
        Assert.Equal(Gear.Revisions.Count, Gear.Revisions.Select(r => r.Name).Distinct().Count());
        Assert.All(Gear.Revisions, r =>
        {
            Assert.StartsWith("52c00000-0000-4000-8000-000000530", r.ContentId.ToString("D"));
            Assert.StartsWith("52e00000-0000-4000-8000-000000530", r.RevisionId.ToString("D"));
        });
    }

    [Fact]
    public void The_pack_carries_no_prose_beyond_one_short_original_line()
    {
        Assert.All(Gear.Revisions, r => Assert.Contains(r.Summary, new[] { "Adventuring gear.", "Equipment pack." }));
    }
}
