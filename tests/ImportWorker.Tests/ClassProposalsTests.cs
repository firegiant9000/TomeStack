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

    /// <summary>A whole level table with no spell slot columns: Level, Features and an invented "Glow Dice" column.</summary>
    private static ClassTable SlotlessTable() => new(
        [new("Level", ClassColumnRole.Level, 0, 60), new("Features", ClassColumnRole.Features, 0, 90), new("Glow Dice", ClassColumnRole.Other, 0, 200)],
        [.. Enumerable.Range(1, 20).Select(level => new ClassTableRow(level, [$"{level}", "—", $"{1 + level / 5}"], []))]);

    private const string SlotlessNote = "The class mentions spellcasting, but its table has no spell slot columns: add spellcasting by hand if it casts spells.";

    [Fact]
    public void A_class_that_mentions_spellcasting_without_slot_columns_says_to_add_it_by_hand()
    {
        var p = ClassProposals.Propose("Fixture Tester", SlotlessTable(), ["Hit Die: d8", "Glowcasting", "You learn to cast spells by spending glow dice."], NoFeatures, ["Spellcasting Focus: a lantern."]);
        Assert.DoesNotContain(p.Effects, e => e is SpellcastingEffect);
        Assert.Contains(SlotlessNote, p.Uncertainties);
    }

    [Fact]
    public void The_class_pages_own_mention_of_spellcasting_is_enough_for_the_note()
    {
        var p = ClassProposals.Propose("Fixture Tester", SlotlessTable(), ["Hit Die: d8", "Spellcasting", "You cast spells by spending glow dice."], NoFeatures);
        Assert.Contains(SlotlessNote, p.Uncertainties);
    }

    [Fact]
    public void A_table_with_slot_columns_carries_no_slotless_note()
    {
        var table = new ClassTable(
            [new("Level", ClassColumnRole.Level, 0, 60), new("Features", ClassColumnRole.Features, 0, 90), new("1st", ClassColumnRole.SpellSlot, 1, 200)],
            [.. Enumerable.Range(1, 20).Select(level => new ClassTableRow(level, [$"{level}", "—", "2"], []))]);
        var p = ClassProposals.Propose("Fixture Tester", table, ["Hit Die: d8", "Spellcasting", "Wisdom is your spellcasting ability."], NoFeatures);
        Assert.Contains(p.Effects, e => e is SpellcastingEffect);
        Assert.DoesNotContain(SlotlessNote, p.Uncertainties);
    }

    [Fact]
    public void A_slotless_class_that_never_mentions_spellcasting_carries_no_spellcasting_note()
    {
        var p = ClassProposals.Propose("Fixture Tester", SlotlessTable(), ["Hit Die: d10", "You hit things with a lantern."], NoFeatures);
        Assert.DoesNotContain(p.Uncertainties, u => u.Contains("spellcasting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_class_with_no_table_read_carries_no_slotless_note()
    {
        // With no table, the detector's own "could not be read whole" uncertainty already asks for a hand check.
        var p = ClassProposals.Propose("Fixture Tester", null, ["Spellcasting", "Wisdom is your spellcasting ability."], NoFeatures);
        Assert.DoesNotContain(SlotlessNote, p.Uncertainties);
    }
}
