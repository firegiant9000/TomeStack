using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Wave 3 G7 (task 7.2): the 2024-rules creation guides for Barbarian, Bard, Cleric, Druid, Fighter and Monk.
/// Each guide is one pick ("kit") whose options are the SRD's A, B (Fighter also C); a kit with an "of your choice" item has one option per choice.
/// </summary>
public class SrdGuides521Tests
{
    private const string Resource = "TomeStack.Content.srd-5.2.1-guides.json";
    private static readonly ContentPack Guides = TomeStackApp.LoadBundledPack(Resource);
    private static readonly List<ContentPack> All = [.. TomeStackApp.BundledPacks.Select(TomeStackApp.LoadBundledPack)];
    private static readonly InMemoryContentCatalog Catalog = new([.. All.SelectMany(p => p.Sources).DistinctBy(s => s.Id)], [.. All.SelectMany(p => p.Revisions)]);

    private static readonly string[] Classes = ["Barbarian", "Bard", "Cleric", "Druid", "Fighter", "Monk"];

    private static readonly Dictionary<string, string[]> Abilities = new()
    {
        ["Barbarian"] = ["str"], ["Bard"] = ["cha"], ["Cleric"] = ["wis"], ["Druid"] = ["wis"], ["Fighter"] = ["str", "dex"], ["Monk"] = ["dex", "wis"],
    };

    private static ContentRevision ClassNamed(string name) =>
        All.SelectMany(p => p.Revisions).First(r => r.Kind == ContentKind.Class && r.Name == name && r.RulesFamilies.Contains("srd-5.2.1"));

    private static StartingEquipmentEffect Kit(ContentRevision guide) => guide.Effects.OfType<StartingEquipmentEffect>().Single();

    private static ContentRevision GuideFor(string className) => Assert.Single(Catalog.GuidesFor(ClassNamed(className).ContentId));

    private static string Plain(string name) => name.Replace('’', '\'');

    private static Dictionary<Guid, string> NamesByContent() =>
        All.SelectMany(p => p.Revisions).DistinctBy(r => r.ContentId).ToDictionary(r => r.ContentId, r => Plain(r.Name));

    // Expands one option alone (a kit with one pick) into "qty Name" lines plus coins.
    private static (SortedDictionary<string, int> Items, Currency Coins, IReadOnlyList<string> Problems) Expand(string className, string optionId)
    {
        var kit = Kit(GuideFor(className));
        var pick = Assert.Single(kit.Picks);
        var result = KitExpansion.Expand(kit, [new KitChoice(pick.Id, optionId)], Catalog);
        var byRevision = All.SelectMany(p => p.Revisions).ToDictionary(r => r.RevisionId, r => Plain(r.Name));
        var items = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in result.Equipment)
            items[byRevision[e.Item.RevisionId]] = items.GetValueOrDefault(byRevision[e.Item.RevisionId]) + e.Quantity;
        return (items, result.Coins, [.. result.Problems.Select(p => p.Message)]);
    }

    [Fact]
    public void The_pack_is_bundled_after_the_gear_pack_it_names()
    {
        var order = TomeStackApp.BundledPacks.ToList();
        Assert.Contains(Resource, order);
        Assert.True(order.IndexOf(Resource) > order.IndexOf("TomeStack.Content.srd-5.2.1-gear.json"));
    }

    [Fact]
    public void There_are_exactly_six_class_guides_in_the_unused_block_each_valid_and_published()
    {
        Assert.Equal(Classes.Select(c => c + " starting guide").Order(), Guides.Revisions.Select(r => r.Name).Order());
        Assert.All(Guides.Revisions, r =>
        {
            Assert.Equal(ContentKind.CreationGuide, r.Kind);
            Assert.Equal(RevisionStatus.Published, r.Status);
            Assert.Equal(ContentRevision.CreationGuideSchemaVersion, r.SchemaVersion);
            Assert.Equal(["srd-5.2.1"], r.RulesFamilies);
            Assert.NotNull(r.Provenance?.Page);
            Assert.StartsWith("52c00000-0000-4000-8000-000000600", r.ContentId.ToString("D"));
            Assert.StartsWith("52e00000-0000-4000-8000-000000600", r.RevisionId.ToString("D"));
            var report = ContentValidator.Validate(r, Catalog);
            Assert.True(report.CanPublish, $"{r.Name}: {string.Join("; ", report.Errors.Select(e => e.Message))}");
            Assert.Empty(report.Errors);
        });
        Assert.Equal(Guides.Revisions.Count, Guides.Revisions.Select(r => r.ContentId).Distinct().Count());
    }

    [Fact]
    public void Each_class_has_exactly_one_guide_with_the_SRD_primary_abilities()
    {
        foreach (var className in Classes)
        {
            var guide = GuideFor(className);
            Assert.Equal(className + " starting guide", guide.Name);
            Assert.Equal(Abilities[className], guide.Effects.OfType<PrimaryAbilitiesEffect>().Single().Abilities);
        }
    }

    [Fact]
    public void Every_option_of_every_guide_expands_without_a_kit_problem()
    {
        foreach (var guide in Guides.Revisions)
        {
            var kit = Kit(guide);
            foreach (var option in Assert.Single(kit.Picks).Options)
            {
                var result = KitExpansion.Expand(kit, [new KitChoice(kit.Picks[0].Id, option.Id)], Catalog);
                Assert.True(result.Problems.Count == 0, $"{guide.Name} {option.Id}: {string.Join("; ", result.Problems.Select(p => p.Message))}");
                Assert.True(result.Equipment.Count > 0 || result.Coins != new Currency(), $"{guide.Name} {option.Id} is empty");
            }
        }
    }

    [Fact]
    public void Every_item_and_pack_is_a_bundled_2024_revision()
    {
        var existing = All.SelectMany(p => p.Revisions).Where(r => r.Kind != ContentKind.CreationGuide && r.RulesFamilies.Contains("srd-5.2.1")).Select(r => r.ContentId).ToHashSet();
        foreach (var line in Guides.Revisions.SelectMany(r => Kit(r).Picks).SelectMany(p => p.Options).SelectMany(o => o.Lines))
        {
            if (line.Item is { } item)
                Assert.Contains(item.ContentId, existing);
            if (line.Pack is { } pack)
                Assert.Contains(pack.ContentId, existing);
        }
    }

    [Fact]
    public void Fighter_option_c_is_the_gold_alone_and_option_b_the_light_kit()
    {
        var (items, coins, problems) = Expand("Fighter", "c");
        Assert.Empty(problems);
        Assert.Empty(items);
        Assert.Equal(new Currency { Gp = 155 }, coins);

        var (kit, kitCoins, _) = Expand("Fighter", "b");
        Assert.Equal(new Currency { Gp = 11 }, kitCoins);
        Assert.Equal(1, kit["Studded Leather Armor"]);
        Assert.Equal(20, kit["Arrow"]);
        Assert.Equal(1, kit["Quiver"]);
    }

    [Fact]
    public void Cleric_option_a_amulet_expands_to_the_SRD_kit()
    {
        var (items, coins, problems) = Expand("Cleric", "a-amulet");
        Assert.Empty(problems);
        var names = NamesByContent();
        var priest = All.SelectMany(p => p.Revisions).Single(r => r.Name == "Priest’s Pack").Effects.OfType<ItemEffect>().Single().Contains!;
        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["Chain Shirt"] = 1, ["Shield"] = 1, ["Mace"] = 1, ["Holy Symbol (Amulet)"] = 1,
        };
        foreach (var line in priest)
            expected[names[line.Item!.ContentId]] = line.Quantity ?? 1;
        Assert.Equal(expected, items);
        Assert.Equal(new Currency { Gp = 7 }, coins);
        Assert.Equal(new Currency { Gp = 110 }, Expand("Cleric", "b").Coins);
    }

    [Theory]
    [InlineData("Barbarian", "a", 15, "Greataxe=1;Handaxe=4")]
    [InlineData("Barbarian", "b", 75, "")]
    [InlineData("Bard", "a-lute", 19, "Leather Armor=1;Dagger=2;Musical Instrument (Lute)=1")]
    [InlineData("Bard", "b", 90, "")]
    [InlineData("Druid", "a", 9, "Leather Armor=1;Shield=1;Sickle=1;Quarterstaff=1;Herbalism Kit=1")]
    [InlineData("Druid", "b", 50, "")]
    [InlineData("Fighter", "a", 4, "Chain Mail=1;Greatsword=1;Flail=1;Javelin=8")]
    [InlineData("Monk", "a-smith", 11, "Spear=1;Dagger=5;Smith’s Tools=1")]
    [InlineData("Monk", "b", 50, "")]
    public void Options_expand_to_the_SRD_items_and_gold(string className, string optionId, int gp, string loose)
    {
        var (items, coins, problems) = Expand(className, optionId);
        Assert.Empty(problems);
        Assert.Equal(new Currency { Gp = gp }, coins);
        var expected = loose.Length == 0 ? [] : loose.Split(';').Select(s => s.Split('=')).ToDictionary(p => Plain(p[0]), p => int.Parse(p[1]));
        // Each named item is present with its quantity (the pack's own contents come on top); the gold-only options carry no items.
        foreach (var (name, qty) in expected)
            Assert.Equal(qty, items[name]);
        Assert.Equal(expected.Count == 0, items.Count == 0);
    }

    [Fact]
    public void Of_your_choice_kits_have_one_option_per_catalogue_item()
    {
        Assert.Equal(11, Assert.Single(Kit(GuideFor("Bard")).Picks).Options.Count);
        Assert.Equal(4, Assert.Single(Kit(GuideFor("Cleric")).Picks).Options.Count);
        // The Monk's musical-instrument choice is not modelled: a pick holds at most 20 options (17 artisan's tools + gold here).
        Assert.Equal(18, Assert.Single(Kit(GuideFor("Monk")).Picks).Options.Count);
        Assert.Equal(3, Assert.Single(Kit(GuideFor("Fighter")).Picks).Options.Count);
    }
}
