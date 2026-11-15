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
        Assert.Equal("Light crossbow and 20 bolts", ranged.Options.Single(o => o.Id == "a").Text);
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
}
