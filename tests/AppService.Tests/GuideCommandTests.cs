using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Wave 3 task 8.1: the content.guides command serves the bundled creation guides to the builder.</summary>
public class GuideCommandTests
{
    private static readonly Guid ClericId = Guid.Parse("51c00000-0000-4000-8000-000000401000");

    private static GuideView Cleric(TempApp temp, Guid? campaignId = null) =>
        temp.App.ListGuides(RulesFamilies.Srd51, campaignId).Single(g => g.TargetContentId == ClericId);

    [Fact]
    public void The_Cleric_guide_has_its_picks_abilities_and_texts()
    {
        using var temp = new TempApp();
        var guide = Cleric(temp);

        Assert.Equal(["wis"], guide.PrimaryAbilities);
        Assert.True(guide.Picks.Count >= 4);
        Assert.Equal(["weapon", "armor", "ranged", "pack", "shield", "symbol"], guide.Picks.Select(p => p.Id));
        Assert.Equal("Weapon", guide.Picks[0].Label);

        var weapon = guide.Picks[0];
        Assert.Equal("Mace", weapon.Options.Single(o => o.Id == "a").Text);
        Assert.Null(weapon.Options.Single(o => o.Id == "a").RequiresProficiency);
        Assert.Equal("weapon.martial", weapon.Options.Single(o => o.Id == "b").RequiresProficiency);

        var ranged = guide.Picks.Single(p => p.Id == "ranged");
        Assert.Equal("Light crossbow and 20 crossbow bolts", ranged.Options.Single(o => o.Id == "a").Text);
        Assert.Equal("Any simple weapon", ranged.Options.Single(o => o.Id == "b").Text);

        var pack = guide.Picks.Single(p => p.Id == "pack");
        Assert.Equal("Priest's pack", pack.Options.Single(o => o.Id == "a").Text);
        var packLine = pack.Options.Single(o => o.Id == "a").Lines.Single();
        Assert.Equal("pack", packLine.Kind);
        Assert.NotEmpty(packLine.Contents!);
    }

    [Fact]
    public void An_anyOf_line_lists_the_simple_weapons_of_the_catalogue_only()
    {
        using var temp = new TempApp();
        var line = Cleric(temp).Picks.Single(p => p.Id == "ranged").Options.Single(o => o.Id == "b").Lines.Single();

        Assert.Equal("anyOf", line.Kind);
        Assert.Equal("weapon.simple", line.AnyOf);
        var choices = line.Choices!;
        Assert.Contains(choices, c => c.Name == "Mace");
        Assert.DoesNotContain(choices, c => c.Name == "Warhammer"); // martial
        Assert.All(choices, c => Assert.Equal(ContentKind.Item, c.Kind));
    }

    [Fact]
    public void A_pick_without_a_label_is_Always_when_it_has_one_option_else_Choice_N()
    {
        var one = new KitPick("p", null, [new("a", [new(Quantity: 1)])]);
        var two = new KitPick("q", null, [new("a", []), new("b", [])]);
        Assert.Equal("Always", Guides.LabelOf(one, 1));
        Assert.Equal("Choice 2", Guides.LabelOf(two, 2));
        Assert.Equal("Given", Guides.LabelOf(new KitPick("r", "Given", []), 3));
    }

    [Fact]
    public void An_unknown_family_gives_an_empty_list()
    {
        using var temp = new TempApp();
        Assert.Empty(temp.App.ListGuides("not-a-family"));
    }

    [Fact]
    public void Each_family_gets_only_its_own_guides()
    {
        using var temp = new TempApp();
        Assert.All(temp.App.ListGuides(RulesFamilies.Srd51), g => Assert.StartsWith("51c", g.GuideContentId.ToString("D")));
        Assert.All(temp.App.ListGuides(RulesFamilies.Srd521), g => Assert.StartsWith("52c", g.GuideContentId.ToString("D")));
    }

    [Fact]
    public void A_missing_campaign_is_refused()
    {
        using var temp = new TempApp();
        Assert.Throws<AppValidationException>(() => temp.App.ListGuides(RulesFamilies.Srd51, Guid.NewGuid()));
    }

    private static readonly Guid Barbarian521 = Guid.Parse("52c00000-0000-4000-8000-000000000011");

    [Fact]
    public void Coins_beside_items_read_and_15_gp_and_coins_alone_read_Instead()
    {
        using var temp = new TempApp();
        var guide = temp.App.ListGuides(RulesFamilies.Srd521).Single(g => g.TargetContentId == Barbarian521);
        var options = guide.Picks.Single().Options;

        var a = options.Single(o => o.Id == "a").Text;
        Assert.EndsWith(" and 15 gp", a);
        Assert.DoesNotContain("Instead", a);
        Assert.Contains("4 handaxes", a);
        Assert.Equal("Instead: 75 gp", options.Single(o => o.Id == "b").Text);
    }

    [Theory]
    [InlineData("Torch", 3, "3 torches")]
    [InlineData("Ration (1 day)", 5, "5 rations (1 day)")]
    [InlineData("Rations (1 day)", 5, "5 × Rations (1 day)")]
    [InlineData("Crossbow, light", 2, "2 light crossbows")]
    [InlineData("Light Crossbow", 2, "2 Light Crossbows")]
    [InlineData("Crossbow bolt", 20, "20 crossbow bolts")]
    [InlineData("Dagger", 2, "2 daggers")]
    [InlineData("Clothes, common", 2, "2 × Common clothes")]
    [InlineData("Vial of acid", 2, "2 × Vial of acid")]
    [InlineData("Priest's pack", 2, "2 × Priest's pack")]
    [InlineData("Caltrops", 2, "2 × Caltrops")]
    [InlineData("Mace", 1, "Mace")]
    [InlineData("Crossbow, light", 1, "Light crossbow")]
    public void Several_of_an_item_keep_the_whole_name_with_a_sure_plural_or_use_a_times_sign(string name, int quantity, string expected) =>
        Assert.Equal(expected, Guides.ItemText(name, quantity));

    public static TheoryData<string, string, int> BundledMultiples()
    {
        var data = new TheoryData<string, string, int>();
        using var temp = new TempApp();
        var seen = new HashSet<(string, string, int)>();
        foreach (var family in new[] { RulesFamilies.Srd51, RulesFamilies.Srd521 })
        {
            foreach (var line in temp.App.ListGuides(family).SelectMany(g => g.Picks).SelectMany(p => p.Options).SelectMany(o => o.Lines).Where(l => l.Kind == "item" && l.Quantity > 1))
            {
                if (seen.Add((family, line.Name!, line.Quantity)))
                    data.Add(family, line.Name!, line.Quantity);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(BundledMultiples))]
    public void Every_bundled_item_line_with_several_reads_sensibly(string family, string name, int quantity)
    {
        var text = Guides.ItemText(name, quantity);
        Assert.StartsWith(quantity + " ", text);
        Assert.DoesNotMatch(@"\)s", text); // the plural never lands after a bracket
        Assert.DoesNotMatch(@"[a-z]ss\b|\bs\b|chs\b|xs\b", text);
        Assert.True(text.Length > quantity.ToString().Length + 2, $"{family}: '{name}' x{quantity} -> '{text}'");
    }

    private static ContentRevision Item(string name, params string[] families) => new()
    {
        ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Item, Name = name,
        RulesFamilies = families, Provenance = new(Guid.NewGuid(), new(1)), Status = RevisionStatus.Published, Effects = [],
    };

    [Fact]
    public void A_line_that_does_not_resolve_or_is_in_another_family_is_marked_missing_and_the_rest_still_shows()
    {
        var mace = Item("Mace", RulesFamilies.Srd51);
        var other = Item("Warhammer", RulesFamilies.Srd521);
        var unknown = Guid.NewGuid();
        var known = new Dictionary<Guid, ContentRevision> { [mace.ContentId] = mace, [other.ContentId] = other };
        var kit = new StartingEquipmentEffect
        {
            Id = "kit",
            Picks = [new("p", "Gear", [new("a", [new(new GuideTarget(mace.ContentId)), new(new GuideTarget(unknown)), new(new GuideTarget(other.ContentId))])])],
        };
        var guide = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.CreationGuide, Name = "Test guide",
            RulesFamilies = [RulesFamilies.Srd51], Provenance = new(Guid.NewGuid(), new(1)), Status = RevisionStatus.Published,
            Effects = [new GuideForEffect { Id = "for", Content = new(Guid.NewGuid()) }, kit],
        };

        var view = Guides.Build(guide, RulesFamilies.Srd51, id => known.GetValueOrDefault(id), _ => []);

        var option = view.Picks.Single().Options.Single();
        Assert.Equal(["item", "missing", "missing"], option.Lines.Select(l => l.Kind));
        Assert.Equal(unknown, option.Lines[1].ItemContentId);
        Assert.Null(option.Lines[1].Name);
        Assert.Equal("Warhammer", option.Lines[2].Name);
        Assert.Equal("Mace and (unavailable: unknown item) and (unavailable: Warhammer)", option.Text);
    }

    [Fact]
    public void A_pack_with_an_unresolved_inner_item_still_shows_and_counts_the_miss()
    {
        var rope = Item("Rope", RulesFamilies.Srd51);
        var pack = Item("Test pack", RulesFamilies.Srd51) with
        {
            Effects = [new ItemEffect { Id = "i", Contains = [new(new GuideTarget(rope.ContentId)), new(new GuideTarget(Guid.NewGuid()))] }],
        };
        var known = new Dictionary<Guid, ContentRevision> { [rope.ContentId] = rope, [pack.ContentId] = pack };
        var guide = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.CreationGuide, Name = "Test guide",
            RulesFamilies = [RulesFamilies.Srd51], Provenance = new(Guid.NewGuid(), new(1)), Status = RevisionStatus.Published,
            Effects = [new GuideForEffect { Id = "for", Content = new(Guid.NewGuid()) }, new StartingEquipmentEffect { Id = "kit", Picks = [new("p", null, [new("a", [new(Pack: new GuideTarget(pack.ContentId))])])] }],
        };

        var option = Guides.Build(guide, RulesFamilies.Srd51, id => known.GetValueOrDefault(id), _ => []).Picks.Single().Options.Single();

        Assert.Equal("Test pack (1 pack item unavailable)", option.Text);
        Assert.Equal(["item", "missing"], option.Lines.Single().Contents!.Select(l => l.Kind));
    }

    // Task 8.2: character.kit.

    private static Character Draft(string className)
    {
        var classes = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-classes.json");
        // The pack holds each class once per schema version; the newest is the one that records armour training.
        var cls = classes.Revisions.Where(r => r.Kind == ContentKind.Class && r.Name == className).MaxBy(r => r.SchemaVersion)!;
        return new Character
        {
            Id = Guid.NewGuid(), Name = "Kit draft", RulesFamily = RulesFamilies.Srd51, Level = 1,
            Classes = [new(cls.Reference, 1)],
            BaseAbilities = new(10, 10, 10, 10, 10, 10),
        };
    }

    private static KitRequest ClericKit(TempApp temp, Character draft, params KitChoice[] overrides)
    {
        var guide = Cleric(temp);
        var choices = guide.Picks.Select(p => overrides.FirstOrDefault(o => o.PickId == p.Id) ?? new KitChoice(p.Id, "a")).ToList();
        return new(draft, [new(guide.GuideContentId, choices)]);
    }

    private static readonly Lazy<Dictionary<Guid, string>> ItemNames = new(() =>
        new[] { "equipment", "armor", "gear" }
            .SelectMany(p => TomeStackApp.LoadBundledPack($"TomeStack.Content.srd-5.1-{p}.json").Revisions)
            .Where(r => r.Kind == ContentKind.Item)
            .ToDictionary(r => r.RevisionId, r => r.Name));

    private static string NameOf(TempApp temp, EquipmentEntry entry) => ItemNames.Value[entry.Item.RevisionId];

    [Fact]
    public void The_Cleric_kit_a_equips_scale_mail_and_shield_and_gives_Armor_Class_16()
    {
        using var temp = new TempApp();
        var view = temp.App.Kit(ClericKit(temp, Draft("Cleric")));

        Assert.Empty(view.Problems);
        Assert.Equal(16, view.Preview.Sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Contains(view.Preview.Sheet.Attacks!, a => a.Name == "Mace");
        var equipped = view.Equipment.Where(e => e.Equipped).Select(e => NameOf(temp, e)).ToList();
        Assert.Contains("Scale mail", equipped);
        Assert.Contains("Shield", equipped);
        Assert.Contains("Mace", equipped);
        Assert.Contains("Crossbow, light", equipped);
        Assert.Equal(view.Equipment, view.Preview.Character.Equipment!.Select(e => e with { Id = view.Equipment.First(x => x.Item == e.Item).Id }));
    }

    [Fact]
    public void Without_shield_training_the_shield_stays_unequipped()
    {
        using var temp = new TempApp();
        var view = temp.App.Kit(ClericKit(temp, Draft("Wizard")));

        var worn = view.Equipment.Where(e => e.Equipped).Select(e => NameOf(temp, e)).ToList();
        Assert.DoesNotContain("Shield", worn);
        Assert.DoesNotContain("Scale mail", worn);
        Assert.Contains(view.Equipment, e => NameOf(temp, e) == "Shield"); // still owned
        Assert.Contains(view.Equipment, e => e.Equipped && NameOf(temp, e) == "Mace");
    }

    [Fact]
    public void An_unresolved_line_is_a_problem_and_the_rest_is_returned()
    {
        using var temp = new TempApp();
        // Option b of the ranged pick is "any simple weapon" and no item is chosen for it.
        var view = temp.App.Kit(ClericKit(temp, Draft("Cleric"), new KitChoice("ranged", "b")));

        Assert.NotEmpty(view.Problems);
        Assert.Contains(view.Equipment, e => NameOf(temp, e) == "Mace");
        Assert.DoesNotContain(view.Equipment, e => NameOf(temp, e) == "Light crossbow");
    }

    [Fact]
    public void An_unknown_guide_is_a_problem_and_nothing_is_saved()
    {
        using var temp = new TempApp();
        var draft = Draft("Cleric");
        var view = temp.App.Kit(new(draft, [new(Guid.NewGuid(), [])]));

        Assert.Contains(view.Problems, p => p.Code == "kit.guide-unresolved");
        Assert.Empty(view.Equipment);
        Assert.Empty(temp.App.ListCharacters());
    }

    [Fact]
    public void The_command_is_registered_and_writes_nothing()
    {
        Assert.Contains("character.kit", CommandDispatcher.Commands);
        using var temp = new TempApp();
        temp.App.Kit(ClericKit(temp, Draft("Cleric")));
        Assert.Empty(temp.App.ListCharacters());
    }
}
