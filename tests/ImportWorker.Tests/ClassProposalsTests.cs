using TomeStack.ImportWorker.Detection;
using TomeStack.RulesCore;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// Fix round 1, item 6: the label forms a class page prints its core traits in, with or without a colon (the 2014 "Label: value"
/// lines and the 2024 "Core Traits" cells), and an uncertainty whenever saves, armour or weapons are not found. Invented text.
/// </summary>
public class ClassProposalsTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> NoFeatures = new Dictionary<int, IReadOnlyList<string>>();

    private static ClassProposals.Proposal Propose(params string[] lines) => ClassProposals.Propose("Fixture Tester", null, lines, NoFeatures);

    private static List<string> Ids(ClassProposals.Proposal p) => [.. p.Effects.Select(e => e.Id)];

    [Theory]
    [InlineData("Saving Throws: Strength, Constitution")]
    [InlineData("Saving Throw Proficiencies: Strength and Constitution")]
    [InlineData("Saving Throw Proficiencies Strength and Constitution")]
    public void Saving_throws_are_read_with_or_without_a_colon(string line)
    {
        var p = Propose(line);
        Assert.Equal(["save-str", "save-con"], Ids(p).Where(i => i.StartsWith("save-", StringComparison.Ordinal)));
        Assert.DoesNotContain(p.Uncertainties, u => u.Contains("saving throw", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Armor: Light armor, medium armor, shields")]
    [InlineData("Armor Training: Light and Medium armor and Shields")]
    [InlineData("Armor Training Light and Medium armor and Shields")]
    public void Armor_is_read_with_or_without_a_colon(string line)
    {
        var p = Propose(line);
        Assert.Equal(["armor-light", "armor-medium", "armor-shield"], Ids(p));
        Assert.DoesNotContain(p.Uncertainties, u => u.Contains("armor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void All_armor_is_light_medium_and_heavy()
    {
        Assert.Equal(["armor-light", "armor-medium", "armor-heavy", "armor-shield"], Ids(Propose("Armor: All armor, shields")));
    }

    [Theory]
    [InlineData("Weapons: Simple weapons, martial weapons")]
    [InlineData("Weapon Proficiencies: Simple and Martial weapons")]
    [InlineData("Weapon Proficiencies Simple and Martial weapons")]
    public void Unqualified_weapons_are_proposed_with_or_without_a_colon(string line)
    {
        var p = Propose(line);
        Assert.Equal(["weapon-simple", "weapon-martial"], Ids(p));
        Assert.DoesNotContain(p.Uncertainties, u => u.Contains("weapon", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Skills: Choose two from Arcana, History and Insight")]
    [InlineData("Skill Proficiencies: Choose 2: Arcana, History or Insight")]
    [InlineData("Skill Proficiencies Choose 2: Arcana, History or Insight")]
    public void The_skill_choice_is_shown_with_or_without_a_colon(string line)
    {
        var p = Propose(line);
        Assert.Contains("Insight", p.Fields["skills"], StringComparison.Ordinal);
        Assert.Contains(p.Uncertainties, u => u.StartsWith("The skill choice", StringComparison.Ordinal));
    }

    [Fact]
    public void A_qualified_weapon_line_proposes_no_weapons_and_says_why()
    {
        var p = Propose("Weapon Proficiencies: Simple weapons and Martial weapons that have the Light property");
        Assert.DoesNotContain(Ids(p), i => i.StartsWith("weapon-", StringComparison.Ordinal));
        Assert.Contains(p.Uncertainties, u => u.Contains("condition", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_saves_armor_and_weapons_each_carry_an_uncertainty()
    {
        var p = Propose("A page of prose with no traits on it.");
        Assert.DoesNotContain(Ids(p), i => i.StartsWith("save-", StringComparison.Ordinal) || i.StartsWith("armor-", StringComparison.Ordinal) || i.StartsWith("weapon-", StringComparison.Ordinal));
        Assert.Contains(p.Uncertainties, u => u.Contains("saving throw", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(p.Uncertainties, u => u.Contains("armor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(p.Uncertainties, u => u.Contains("weapon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Armor_none_is_a_reading_and_later_prose_does_not_add_armor()
    {
        var p = Propose("Armor: None", "Armor training with light armor is granted by a subclass.");
        Assert.DoesNotContain(Ids(p), i => i.StartsWith("armor-", StringComparison.Ordinal));
        Assert.DoesNotContain(p.Uncertainties, u => u.Contains("armor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Prose_that_starts_like_the_saves_label_is_not_a_reading()
    {
        var p = Propose("Saving throws against your glow are rare.", "Saving Throw Proficiencies Wisdom and Charisma");
        Assert.Equal(["save-wis", "save-cha"], Ids(p).Where(i => i.StartsWith("save-", StringComparison.Ordinal)));
    }

}
