using TomeStack.ImportWorker.Detection;

namespace TomeStack.ImportWorker.Tests;

/// <summary>Fix round 1, item 6: a 2024-style "Core Traits" table, a label cell with its value in the cell to its right. Invented text.</summary>
public class ClassProposalsLabelTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> NoFeatures = new Dictionary<int, IReadOnlyList<string>>();

    [Fact]
    public void A_core_traits_label_cell_is_joined_with_the_value_cell_to_its_right_and_its_wrapped_lines()
    {
        // A two-column traits table: labels at x 50, values at x 150, a value wrapping onto a second line.
        IReadOnlyList<TextLine> lines =
        [
            new("Saving Throw Proficiencies", 50, 500, 80, 10),
            new("Wisdom and Charisma", 150, 500, 90, 10),
            new("Weapon Proficiencies", 50, 480, 80, 10),
            new("Simple weapons and", 150, 480, 90, 10),
            new("Martial weapons", 150, 469, 90, 10),
            new("Armor Training", 50, 450, 80, 10),
            new("Light armor", 150, 450, 90, 10),
        ];
        var joined = ClassProposals.LabelledLines(lines);

        Assert.Contains("Saving Throw Proficiencies Wisdom and Charisma", joined);
        Assert.Contains("Weapon Proficiencies Simple weapons and Martial weapons", joined);
        Assert.Contains("Armor Training Light armor", joined);
        var p = ClassProposals.Propose("Fixture Tester", null, joined, NoFeatures);
        Assert.Equal(["save-wis", "save-cha", "armor-light", "weapon-simple", "weapon-martial"], p.Effects.Select(e => e.Id));
    }

    [Fact]
    public void A_label_fused_with_its_value_keeps_the_value_lines_that_wrap_under_it()
    {
        // The extractor fused label and value ("Weapon Proficiencies Simple weapons and"); the value wraps in its own column (x 200),
        // beside prose in the other column (x 20) that is not part of it.
        IReadOnlyList<TextLine> lines =
        [
            new("Weapon Proficiencies Simple weapons and", 100, 600, 200, 7),
            new("Prose in the other column about weapons", 20, 598, 70, 7),
            new("Martial weapons that have the", 200, 588, 110, 7),
            new("Glow property", 200, 575, 40, 7),
            new("Tool Proficiencies", 100, 560, 70, 7),
            new("Choose one Fixture Lens", 200, 560, 90, 7),
        ];
        var joined = ClassProposals.LabelledLines(lines);

        Assert.Contains("Weapon Proficiencies Simple weapons and Martial weapons that have the Glow property", joined);
        var p = ClassProposals.Propose("Fixture Tester", null, joined, NoFeatures);
        Assert.DoesNotContain(p.Effects, e => e.Id.StartsWith("weapon-", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_traits_printed_on_the_previous_page_are_read_only_under_this_classes_heading()
    {
        IReadOnlyList<TextLine> previous =
        [
            new("Core Fixture Tester Traits", 50, 520, 120, 10),
            new("Saving Throw Proficiencies", 50, 500, 80, 10),
            new("Wisdom and Charisma", 150, 500, 90, 10),
        ];
        Assert.Contains("Saving Throw Proficiencies Wisdom and Charisma", ClassProposals.LabelledLines([], previous, "Fixture Tester"));
        Assert.DoesNotContain("Saving Throw Proficiencies Wisdom and Charisma", ClassProposals.LabelledLines([], previous, "Fixture Other"));
    }

    [Fact]
    public void Core_traits_are_read_only_from_the_headings_own_block_not_the_whole_previous_page()
    {
        // Wave 0/1 minors (d): two classes' Core Traits tables on one page, and a stray label above both.
        IReadOnlyList<TextLine> previous =
        [
            new("Saving Throw Proficiencies", 50, 580, 80, 10),
            new("Intelligence and Wisdom", 150, 580, 90, 10),
            new("Core Fixture Tester Traits", 50, 520, 120, 10),
            new("Saving Throw Proficiencies", 50, 500, 80, 10),
            new("Wisdom and Charisma", 150, 500, 90, 10),
            new("Core Fixture Other Traits", 50, 400, 120, 10),
            new("Saving Throw Proficiencies", 50, 380, 80, 10),
            new("Strength and Dexterity", 150, 380, 90, 10),
        ];
        var tester = ClassProposals.LabelledLines([], previous, "Fixture Tester");
        var other = ClassProposals.LabelledLines([], previous, "Fixture Other");

        Assert.Contains("Saving Throw Proficiencies Wisdom and Charisma", tester);
        Assert.DoesNotContain("Saving Throw Proficiencies Strength and Dexterity", tester);
        Assert.DoesNotContain("Saving Throw Proficiencies Intelligence and Wisdom", tester);
        Assert.Contains("Saving Throw Proficiencies Strength and Dexterity", other);
        Assert.DoesNotContain("Saving Throw Proficiencies Wisdom and Charisma", other);
    }
}
