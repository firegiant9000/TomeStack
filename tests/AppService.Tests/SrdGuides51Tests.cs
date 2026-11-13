using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Wave 3 G6 (tasks 6.2 and 6.3): the 2014-rules creation guides, one per class (all twelve) plus the Acolyte background.
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
        ["Paladin starting guide"] = ("Paladin", ["str", "cha"]),
        ["Ranger starting guide"] = ("Ranger", ["dex", "wis"]),
        ["Rogue starting guide"] = ("Rogue", ["dex"]),
        ["Sorcerer starting guide"] = ("Sorcerer", ["cha"]),
        ["Warlock starting guide"] = ("Warlock", ["cha"]),
        ["Wizard starting guide"] = ("Wizard", ["int"]),
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
    public void There_are_exactly_the_twelve_class_guides_and_the_Acolyte_guide_in_the_unused_block_each_valid_and_published()
    {
        Assert.Equal(Expected.Keys.Append("Acolyte starting guide").Order(), Guides.Revisions.Select(r => r.Name).Order());
        Assert.All(Guides.Revisions, r =>
        {
            Assert.Equal(ContentKind.CreationGuide, r.Kind);
            Assert.Equal(RevisionStatus.Published, r.Status);
            Assert.Equal(ContentRevision.CreationGuideSchemaVersion, r.SchemaVersion);
            Assert.Equal(["srd-5.1"], r.RulesFamilies);
            Assert.Equal(
                Expected.TryGetValue(r.Name, out var e) ? "Starting equipment and key " + (e.Abilities.Length == 1 ? "ability." : "abilities.") : "Starting equipment.",
                r.Summary);
            Assert.NotNull(r.Provenance?.Page);
            Assert.StartsWith("51c00000-0000-4000-8000-000000600", r.ContentId.ToString("D"));
            Assert.StartsWith("51e00000-0000-4000-8000-000000600", r.RevisionId.ToString("D"));
            var report = ContentValidator.Validate(r, Catalog);
            Assert.True(report.CanPublish, $"{r.Name}: {string.Join("; ", report.Errors.Select(e => e.Message))}");
            Assert.Empty(report.Errors);
        });
    }

    [Fact]
    public void Each_of_the_twelve_classes_has_exactly_one_guide_with_the_prerequisite_table_abilities()
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

    // Expands every option of every pick of the guide (the other picks at their first option): no problems and some equipment.
    private static void AssertEveryOptionExpands(ContentRevision guide)
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

    [Fact]
    public void Every_option_of_every_pick_expands_without_problems()
    {
        foreach (var guide in Guides.Revisions)
            AssertEveryOptionExpands(guide);
    }

    // Row: guide, pick, option, expected lines "qty Name" joined by "; ", Acolyte gold. "pack:Name" stands for that pack's contents
    // (pinned in SrdGear51Tests); "any:weapon.simple" or "any:weapon.martial" for the weapon the test supplies for that anyOf line.
    public static TheoryData<string, string, string, string, int> OptionRows()
    {
        var rows = new TheoryData<string, string, string, string, int>();
        void Row(string guide, string pick, string option, string lines, int gp = 0) => rows.Add(guide, pick, option, lines, gp);
        void Each(string guide, string pick, params string[] names)
        {
            for (var i = 0; i < names.Length; i++)
                Row(guide, pick, ((char)('a' + i)).ToString(), "1 " + names[i]);
        }
        const string Simple = "1 any:weapon.simple";
        const string Martial = "1 any:weapon.martial";
        string[] arcane = ["Component pouch", "Arcane focus (crystal)", "Arcane focus (orb)", "Arcane focus (rod)", "Arcane focus (staff)", "Arcane focus (wand)"];
        string[] symbols = ["Holy symbol (amulet)", "Holy symbol (emblem)", "Holy symbol (reliquary)"];

        Row("Barbarian", "melee", "a", "1 Greataxe");
        Row("Barbarian", "melee", "b", Martial);
        Row("Barbarian", "second", "a", "2 Handaxe");
        Row("Barbarian", "second", "b", Simple);
        Row("Barbarian", "gear", "a", "1 pack:Explorer's pack; 4 Javelin");

        Row("Bard", "weapon", "a", "1 Rapier");
        Row("Bard", "weapon", "b", "1 Longsword");
        Row("Bard", "weapon", "c", Simple);
        Row("Bard", "pack", "a", "1 pack:Diplomat's pack");
        Row("Bard", "pack", "b", "1 pack:Entertainer's pack");
        Each("Bard", "instrument", "Lute", "Bagpipes", "Drum", "Dulcimer", "Flute", "Horn", "Lyre", "Pan flute", "Shawm", "Viol");
        Row("Bard", "armor", "a", "1 Leather; 1 Dagger");

        Row("Cleric", "weapon", "a", "1 Mace");
        Row("Cleric", "weapon", "b", "1 Warhammer");
        Row("Cleric", "armor", "a", "1 Scale mail");
        Row("Cleric", "armor", "b", "1 Leather");
        Row("Cleric", "armor", "c", "1 Chain mail");
        Row("Cleric", "ranged", "a", "1 Crossbow, light; 20 Crossbow bolt");
        Row("Cleric", "ranged", "b", Simple);
        Row("Cleric", "pack", "a", "1 pack:Priest's pack");
        Row("Cleric", "pack", "b", "1 pack:Explorer's pack");
        Row("Cleric", "shield", "a", "1 Shield");
        Each("Cleric", "symbol", symbols);

        Row("Druid", "shield", "a", "1 Shield");
        Row("Druid", "shield", "b", Simple);
        Row("Druid", "melee", "a", "1 Scimitar");
        Row("Druid", "melee", "b", Simple);
        Row("Druid", "gear", "a", "1 Leather; 1 pack:Explorer's pack");
        Each("Druid", "focus", "Druidic focus (sprig of mistletoe)", "Druidic focus (totem)", "Druidic focus (wooden staff)", "Druidic focus (yew wand)");

        Row("Fighter", "armor", "a", "1 Chain mail");
        Row("Fighter", "armor", "b", "1 Leather; 1 Longbow; 20 Arrow");
        Row("Fighter", "weapons", "a", Martial + "; 1 Shield");
        Row("Fighter", "weapons", "b", "2 any:weapon.martial");
        Row("Fighter", "ranged", "a", "1 Crossbow, light; 20 Crossbow bolt");
        Row("Fighter", "ranged", "b", "2 Handaxe");
        Row("Fighter", "pack", "a", "1 pack:Dungeoneer's pack");
        Row("Fighter", "pack", "b", "1 pack:Explorer's pack");

        Row("Monk", "weapon", "a", "1 Shortsword");
        Row("Monk", "weapon", "b", Simple);
        Row("Monk", "pack", "a", "1 pack:Dungeoneer's pack");
        Row("Monk", "pack", "b", "1 pack:Explorer's pack");
        Row("Monk", "darts", "a", "10 Dart");

        Row("Paladin", "weapons", "a", Martial + "; 1 Shield");
        Row("Paladin", "weapons", "b", "2 any:weapon.martial");
        Row("Paladin", "second", "a", "5 Javelin");
        Row("Paladin", "second", "b", Simple);
        Row("Paladin", "pack", "a", "1 pack:Priest's pack");
        Row("Paladin", "pack", "b", "1 pack:Explorer's pack");
        Row("Paladin", "armor", "a", "1 Chain mail");
        Each("Paladin", "symbol", symbols);

        Row("Ranger", "armor", "a", "1 Scale mail");
        Row("Ranger", "armor", "b", "1 Leather");
        Row("Ranger", "weapons", "a", "2 Shortsword");
        Row("Ranger", "weapons", "b", "2 any:weapon.simple");
        Row("Ranger", "pack", "a", "1 pack:Dungeoneer's pack");
        Row("Ranger", "pack", "b", "1 pack:Explorer's pack");
        Row("Ranger", "bow", "a", "1 Longbow; 1 Quiver; 20 Arrow");

        Row("Rogue", "weapon", "a", "1 Rapier");
        Row("Rogue", "weapon", "b", "1 Shortsword");
        Row("Rogue", "ranged", "a", "1 Shortbow; 1 Quiver; 20 Arrow");
        Row("Rogue", "ranged", "b", "1 Shortsword");
        Row("Rogue", "pack", "a", "1 pack:Burglar's pack");
        Row("Rogue", "pack", "b", "1 pack:Dungeoneer's pack");
        Row("Rogue", "pack", "c", "1 pack:Explorer's pack");
        Row("Rogue", "gear", "a", "1 Leather; 2 Dagger; 1 Thieves' tools");

        foreach (var caster in new[] { "Sorcerer", "Warlock" })
        {
            Row(caster, "ranged", "a", "1 Crossbow, light; 20 Crossbow bolt");
            Row(caster, "ranged", "b", Simple);
            Each(caster, "focus", arcane);
        }
        Row("Sorcerer", "pack", "a", "1 pack:Dungeoneer's pack");
        Row("Sorcerer", "pack", "b", "1 pack:Explorer's pack");
        Row("Sorcerer", "daggers", "a", "2 Dagger");

        Row("Warlock", "pack", "a", "1 pack:Scholar's pack");
        Row("Warlock", "pack", "b", "1 pack:Dungeoneer's pack");
        Row("Warlock", "gear", "a", "1 Leather; " + Simple + "; 2 Dagger");

        Row("Wizard", "weapon", "a", "1 Quarterstaff");
        Row("Wizard", "weapon", "b", "1 Dagger");
        Each("Wizard", "focus", arcane);
        Row("Wizard", "pack", "a", "1 pack:Scholar's pack");
        Row("Wizard", "pack", "b", "1 pack:Explorer's pack");
        Row("Wizard", "book", "a", "1 Spellbook");

        Each("Acolyte", "symbol", symbols);
        Each("Acolyte", "devotion", "Prayer book", "Prayer wheel");
        Row("Acolyte", "gear", "a", "5 Incense stick; 1 Vestments; 1 Clothes, common; 1 Pouch", 15);
        return rows;
    }

    [Theory]
    [MemberData(nameof(OptionRows))]
    public void Each_option_expands_to_exactly_the_SRD_items_and_quantities(string target, string pickId, string optionId, string lines, int gp)
    {
        var guide = Assert.Single(Catalog.GuidesFor(
            All.SelectMany(p => p.Revisions).First(r => r.Name == target && r.Kind is ContentKind.Class or ContentKind.Background && r.RulesFamilies.Contains("srd-5.1")).ContentId));
        var kit = Kit(guide);
        var pick = kit.Picks.Single(p => p.Id == pickId);
        var option = pick.Options.Single(o => o.Id == optionId);

        // Expand this one option alone.
        var single = kit with { Picks = [pick with { Options = [option] }] };
        var result = KitExpansion.Expand(single, [new KitChoice(pickId, optionId, AnyOfFor(option))], Catalog);

        Assert.Empty(result.Problems);
        var byRevision = All.SelectMany(p => p.Revisions).ToDictionary(r => r.RevisionId, r => r.Name);
        var byContent = All.SelectMany(p => p.Revisions).DistinctBy(r => r.ContentId).ToDictionary(r => r.ContentId, r => r.Name);
        var anyOf = AnyOfFor(option);
        var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in result.Equipment)
            actual[byRevision[e.Item.RevisionId]] = actual.GetValueOrDefault(byRevision[e.Item.RevisionId]) + e.Quantity;

        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Add(string name, int quantity) => expected[name] = expected.GetValueOrDefault(name) + quantity;
        var anyOfNames = new Dictionary<string, string>();
        foreach (var (index, id) in anyOf ?? [])
            anyOfNames[option.Lines[index].AnyOf!] = byContent[id];
        foreach (var part in lines.Split("; "))
        {
            var (qty, what) = (int.Parse(part[..part.IndexOf(' ')]), part[(part.IndexOf(' ') + 1)..]);
            if (what.StartsWith("pack:", StringComparison.Ordinal))
            {
                var pack = All.SelectMany(p => p.Revisions).Single(r => r.Name == what[5..]).Effects.OfType<ItemEffect>().Single().Contains!;
                foreach (var line in pack)
                    Add(byContent[line.Item!.ContentId], qty * (line.Quantity ?? 1));
            }
            else
            {
                Add(what.StartsWith("any:", StringComparison.Ordinal) ? anyOfNames[what[4..]] : what, qty);
            }
        }

        Assert.Equal(expected, actual);
        Assert.Equal(new Currency { Gp = gp }, result.Coins);
        Assert.All(result.Equipment, e => Assert.False(e.Equipped));

        // A same-named 5.2.1 item must not pass: every resolved revision belongs to the 2014 rules.
        var byRevisionRow = All.SelectMany(p => p.Revisions).ToDictionary(r => r.RevisionId);
        Assert.All(result.Equipment, e => Assert.Contains("srd-5.1", byRevisionRow[e.Item.RevisionId].RulesFamilies));
    }

    [Fact]
    public void Option_rows_cover_every_pick_and_option_of_every_srd_5_1_guide_exactly()
    {
        var rows = OptionRows().Select(r => ((string)r[0], (string)r[1], (string)r[2])).ToList();
        Assert.Equal(rows.Count, rows.Distinct().Count());

        var targets = All.SelectMany(p => p.Revisions)
            .Where(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Contains("srd-5.1") && r.Kind is ContentKind.Class or ContentKind.Background)
            .DistinctBy(r => r.ContentId);
        var actual = targets
            .SelectMany(t => Kit(Assert.Single(Catalog.GuidesFor(t.ContentId))).Picks.SelectMany(p => p.Options.Select(o => (t.Name, p.Id, o.Id))))
            .ToList();

        Assert.Equal(actual.Order(), rows.Order());
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

    [Fact]
    public void Every_bundled_class_and_background_has_a_guide_whose_every_line_resolves()
    {
        var published = All.SelectMany(p => p.Revisions)
            .Where(r => r.Status == RevisionStatus.Published && r.RulesFamilies.Contains("srd-5.1") && r.Kind is ContentKind.Class or ContentKind.Background)
            .DistinctBy(r => r.ContentId)
            .ToList();
        Assert.Equal(13, published.Count);
        foreach (var target in published)
        {
            AssertEveryOptionExpands(Assert.Single(Catalog.GuidesFor(target.ContentId)));
        }
        var acolyte = Kit(Catalog.GuidesFor(Guid.Parse("51c00000-0000-4000-8000-000000000002")).Single());
        var coins = KitExpansion.Expand(acolyte, acolyte.Picks.Select(p => new KitChoice(p.Id, "a")).ToList(), Catalog).Coins;
        Assert.Equal(new Currency { Gp = 15 }, coins);
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
