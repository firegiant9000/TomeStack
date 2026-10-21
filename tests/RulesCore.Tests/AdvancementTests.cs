using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Wave 3 item 27 (R162, R177): the experience point table is one shared rules constant, and the sheet says only whether a
/// level up is available (display only; nothing here ever levels a character).
/// </summary>
public class AdvancementTests
{
    private static readonly int[] Expected =
    [
        0, 300, 900, 2_700, 6_500, 14_000, 23_000, 34_000, 48_000, 64_000,
        85_000, 100_000, 120_000, 140_000, 165_000, 195_000, 225_000, 265_000, 305_000, 355_000,
    ];

    public static TheoryData<string> Families => new() { RulesFamilies.Srd51, RulesFamilies.Srd521 };

    [Fact]
    public void The_table_has_the_twenty_thresholds_exactly_and_they_only_rise()
    {
        Assert.Equal(Expected, Advancement.XpThresholds);
        Assert.Equal(0, Advancement.XpThresholds[0]);
        for (var i = 1; i < Advancement.XpThresholds.Count; i++)
            Assert.True(Advancement.XpThresholds[i] > Advancement.XpThresholds[i - 1]);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, 300)]
    [InlineData(3, 2_700)]
    [InlineData(19, 355_000)]
    [InlineData(20, null)]
    [InlineData(21, null)]
    [InlineData(-1, null)]
    public void The_next_threshold_is_the_one_for_the_following_level(int level, int? expected) =>
        Assert.Equal(expected, Advancement.XpForNextLevel(level));

    private static CharacterSheet Sheet(string family, int level, int? xp, params ClassLevel[] classes)
    {
        var character = Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with
        {
            Level = level, Classes = classes, Xp = xp,
        };
        return CharacterCalculator.Calculate(character, Fixtures.AllCatalog());
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void Nothing_is_offered_or_written_when_xp_is_not_tracked(string family)
    {
        var sheet = Sheet(family, 3, null);

        Assert.Null(sheet.LevelUpAvailable);
        Assert.Null(sheet.XpForNextLevel);
        var json = JsonSerializer.Serialize(sheet, RulesJson.Options);
        Assert.DoesNotContain("levelUpAvailable", json, StringComparison.Ordinal);
        Assert.DoesNotContain("xpForNextLevel", json, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void At_level_three_the_hint_turns_on_at_exactly_2700(string family)
    {
        var below = Sheet(family, 3, 2_699);
        Assert.Null(below.LevelUpAvailable);
        Assert.Equal(2_700, below.XpForNextLevel);

        var at = Sheet(family, 3, 2_700);
        Assert.True(at.LevelUpAvailable);
        Assert.Equal(2_700, at.XpForNextLevel);
        Assert.True(Sheet(family, 3, 1_000_000).LevelUpAvailable);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void At_level_twenty_there_is_never_a_hint_or_a_next_threshold(string family)
    {
        var sheet = Sheet(family, 20, 1_000_000);

        Assert.Null(sheet.LevelUpAvailable);
        Assert.Null(sheet.XpForNextLevel);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_multiclass_character_is_judged_by_its_total_level(string family)
    {
        // Two levels in one class and one in another: total level 3, so the next threshold is the level 4 one. The stored level is ignored.
        ClassLevel[] classes = [new(Fixtures.Loremaster, 2), new(Fixtures.Wayfinder, 1)];

        Assert.Null(Sheet(family, 20, 2_699, classes).LevelUpAvailable);
        Assert.True(Sheet(family, 20, 2_700, classes).LevelUpAvailable);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void A_character_with_no_level_yet_never_has_a_hint(string family)
    {
        var sheet = Sheet(family, 0, 999_999);

        Assert.Null(sheet.LevelUpAvailable);
        Assert.Null(sheet.XpForNextLevel);
    }
}
