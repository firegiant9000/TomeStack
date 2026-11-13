using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Wave 3 G6 (task 6.2): the first six 2014-rules class creation guides (Barbarian, Bard, Cleric, Druid, Fighter, Monk).
/// Names, quantities and ability keys only.
/// </summary>
public class SrdGuides51Tests
{
    private const string Resource = "TomeStack.Content.srd-5.1-guides.json";
    private static readonly ContentPack Guides = TomeStackApp.LoadBundledPack(Resource);
    private static readonly List<ContentPack> All = [.. TomeStackApp.BundledPacks.Select(TomeStackApp.LoadBundledPack)];
    private static readonly InMemoryContentCatalog Catalog = new([.. All.SelectMany(p => p.Sources).DistinctBy(s => s.Id)], [.. All.SelectMany(p => p.Revisions)]);

    private static readonly Dictionary<string, (string ClassName, string[] Abilities)> Expected = new()
    {
        ["Barbarian starting guide"] = ("Barbarian", ["str"]),
        ["Bard starting guide"] = ("Bard", ["cha"]),
        ["Cleric starting guide"] = ("Cleric", ["wis"]),
        ["Druid starting guide"] = ("Druid", ["wis"]),
        ["Fighter starting guide"] = ("Fighter", ["str", "dex"]),
        ["Monk starting guide"] = ("Monk", ["dex", "wis"]),
    };

    private static ContentRevision ClassNamed(string name) =>
        All.SelectMany(p => p.Revisions).First(r => r.Kind == ContentKind.Class && r.Name == name && r.RulesFamilies.Contains("srd-5.1"));

    private static StartingEquipmentEffect Kit(ContentRevision guide) => guide.Effects.OfType<StartingEquipmentEffect>().Single();

    [Fact]
    public void The_pack_is_bundled_after_the_gear_pack_it_names()
    {
        var order = TomeStackApp.BundledPacks.ToList();
        Assert.Contains(Resource, order);
        Assert.True(order.IndexOf(Resource) > order.IndexOf("TomeStack.Content.srd-5.1-gear.json"));
    }

    [Fact]
    public void There_are_exactly_six_guides_in_the_unused_block_each_valid_and_published()
    {
        Assert.Equal(Expected.Keys.Order(), Guides.Revisions.Select(r => r.Name).Order());
        Assert.All(Guides.Revisions, r =>
        {
            Assert.Equal(ContentKind.CreationGuide, r.Kind);
            Assert.Equal(RevisionStatus.Published, r.Status);
            Assert.Equal(ContentRevision.CreationGuideSchemaVersion, r.SchemaVersion);
            Assert.Equal(["srd-5.1"], r.RulesFamilies);
            Assert.Equal("Starting equipment and key " + (Expected[r.Name].Abilities.Length == 1 ? "ability." : "abilities."), r.Summary);
            Assert.NotNull(r.Provenance?.Page);
            Assert.StartsWith("51c00000-0000-4000-8000-000000600", r.ContentId.ToString("D"));
            Assert.StartsWith("51e00000-0000-4000-8000-000000600", r.RevisionId.ToString("D"));
            var report = ContentValidator.Validate(r, Catalog);
            Assert.True(report.CanPublish, $"{r.Name}: {string.Join("; ", report.Errors.Select(e => e.Message))}");
            Assert.Empty(report.Errors);
        });
    }

    [Fact]
    public void Each_of_the_six_classes_has_exactly_one_guide_with_the_prerequisite_table_abilities()
    {
        foreach (var (guideName, (className, abilities)) in Expected)
        {
            var cls = ClassNamed(className);
            var found = Catalog.GuidesFor(cls.ContentId);
            var guide = Assert.Single(found);
            Assert.Equal(guideName, guide.Name);
            Assert.Equal(abilities, guide.Effects.OfType<PrimaryAbilitiesEffect>().Single().Abilities);
        }
    }

    [Fact]
    public void Every_option_of_every_pick_expands_without_problems()
    {
        foreach (var guide in Guides.Revisions)
        {
            var kit = Kit(guide);
            foreach (var pick in kit.Picks)
            {
                foreach (var option in pick.Options)
                {
                    var choices = kit.Picks.Select(p => p == pick ? new KitChoice(p.Id, option.Id, AnyOfFor(option)) : new KitChoice(p.Id, p.Options[0].Id, AnyOfFor(p.Options[0]))).ToList();
                    var result = KitExpansion.Expand(kit, choices, Catalog);
                    Assert.True(result.Problems.Count == 0, $"{guide.Name} {pick.Id}/{option.Id}: {string.Join("; ", result.Problems.Select(p => p.Message))}");
                    Assert.NotEmpty(result.Equipment);
                }
            }
        }
    }

    [Fact]
    public void Cleric_options_a_a_a_a_expand_to_the_SRD_kit()
    {
        var kit = Kit(Catalog.GuidesFor(ClassNamed("Cleric").ContentId).Single());
        var choices = new[] { "weapon", "armor", "ranged", "pack", "symbol" }.Select(p => new KitChoice(p, "a")).ToList();

        var result = KitExpansion.Expand(kit, choices, Catalog);

        Assert.Empty(result.Problems);
        var byRevision = All.SelectMany(p => p.Revisions).ToDictionary(r => r.RevisionId, r => r.Name);
        var actual = result.Equipment.ToDictionary(e => byRevision[e.Item.RevisionId], e => e.Quantity);
        var priest = All.SelectMany(p => p.Revisions).Single(r => r.Name == "Priest's pack").Effects.OfType<ItemEffect>().Single().Contains!;
        var byContent = All.SelectMany(p => p.Revisions).DistinctBy(r => r.ContentId).ToDictionary(r => r.ContentId, r => r.Name);
        var expected = new Dictionary<string, int>
        {
            ["Mace"] = 1, ["Scale mail"] = 1, ["Crossbow, light"] = 1, ["Crossbow bolt"] = 20, ["Shield"] = 1, ["Holy symbol (amulet)"] = 1,
        };
        foreach (var line in priest)
            expected[byContent[line.Item.ContentId]] = line.Quantity ?? 1;
        Assert.Equal(expected.OrderBy(kv => kv.Key), actual.OrderBy(kv => kv.Key));
        Assert.All(result.Equipment, e => Assert.False(e.Equipped));
        Assert.Equal(new Currency(), result.Coins);
    }

    [Fact]
    public void Proficiency_gated_options_use_weapon_and_armor_keys_only_where_the_SRD_says_if_proficient()
    {
        var cleric = Kit(Catalog.GuidesFor(ClassNamed("Cleric").ContentId).Single());
        Assert.Equal("weapon.martial", cleric.Picks.Single(p => p.Id == "weapon").Options.Single(o => o.Id == "b").RequiresProficiency);
        Assert.Equal("armor.heavy", cleric.Picks.Single(p => p.Id == "armor").Options.Single(o => o.Id == "c").RequiresProficiency);
        Assert.Equal(2, Guides.Revisions.SelectMany(r => Kit(r).Picks).SelectMany(p => p.Options).Count(o => o.RequiresProficiency is not null));
    }

    [Fact]
    public void Bard_instrument_is_one_pick_with_one_option_per_instrument_and_the_lute_first()
    {
        var bard = Kit(Catalog.GuidesFor(ClassNamed("Bard").ContentId).Single());
        var pick = bard.Picks.Single(p => p.Id == "instrument");
        var byContent = All.SelectMany(p => p.Revisions).DistinctBy(r => r.ContentId).ToDictionary(r => r.ContentId, r => r.Name);
        Assert.Equal(10, pick.Options.Count);
        Assert.Equal("Lute", byContent[pick.Options[0].Lines.Single().Item!.ContentId]);
        Assert.Equal(
            ["Bagpipes", "Drum", "Dulcimer", "Flute", "Horn", "Lute", "Lyre", "Pan flute", "Shawm", "Viol"],
            pick.Options.Select(o => byContent[o.Lines.Single().Item!.ContentId]).Order());
    }

    [Fact]
    public void Weapons_and_armor_reference_the_existing_bundled_items_not_new_ones()
    {
        var existing = All.SelectMany(p => p.Revisions).Where(r => r.Kind != ContentKind.CreationGuide).Select(r => r.ContentId).ToHashSet();
        foreach (var line in Guides.Revisions.SelectMany(r => Kit(r).Picks).SelectMany(p => p.Options).SelectMany(o => o.Lines))
        {
            if (line.Item is { } item)
                Assert.Contains(item.ContentId, existing);
            if (line.Pack is { } pack)
                Assert.Contains(pack.ContentId, existing);
        }
        Assert.Equal(Guides.Revisions.Count, Guides.Revisions.Select(r => r.ContentId).Distinct().Count());
    }

    // For each anyOf line of the option (by index), any published item of that category.
    private static Dictionary<int, Guid>? AnyOfFor(KitOption option)
    {
        Dictionary<int, Guid>? map = null;
        for (var i = 0; i < option.Lines.Count; i++)
        {
            if (option.Lines[i].AnyOf is not { } category)
                continue;
            var weaponCategory = category == "weapon.simple" ? WeaponCategory.Simple : WeaponCategory.Martial;
            var item = All.SelectMany(p => p.Revisions).First(r => r.Kind == ContentKind.Item && r.RulesFamilies.Contains("srd-5.1") && r.Effects.OfType<WeaponEffect>().Any(w => w.Category == weaponCategory));
            (map ??= [])[i] = item.ContentId;
        }
        return map;
    }
}
