using TomeStack.ImportWorker.Detection;

namespace TomeStack.ImportWorker.Tests;

/// <summary>
/// The text after a class's table page is read for its spellcasting ability (item 11) and ends where the next class begins,
/// so a class without slot columns never takes the next class's "Spellcasting" and gets the "add spellcasting by hand" note.
/// </summary>
public class ClassFollowingTextTests
{
    private static TextBlock Line(string text, double y, double x = 50) => new(text, x, y, 200, 10, 10, false);

    private static DetectionPage TablePage(int page, string name, string featuresHeader) => new(page, "",
        [Line($"{name} Features", 700), Line("Level", 680), Line("Proficiency Bonus", 680, 100), Line(featuresHeader, 680, 200), Line("1st", 660), Line("Fixture Glow", 660, 200)], false);

    private static DetectionPage Prose(int page, params string[] lines) => new(page, "", [.. lines.Select((l, i) => Line(l, 700 - i * 12))], false);

    private static List<string> Following(params DetectionPage[] pages) => CandidateDetector.FollowingText(pages, pages[0], []);

    [Fact]
    public void The_next_class_table_headed_Class_Features_ends_the_text()
    {
        var following = Following(
            TablePage(1, "Fixture Warden", "Features"),
            Prose(2, "Level 2: Fixture Stride", "You move like fixture wind."),
            TablePage(3, "Fixture Seer", "Class Features"),
            Prose(4, "Level 1: Spellcasting", "Wisdom is your spellcasting ability."));

        Assert.Contains("You move like fixture wind.", following);
        Assert.DoesNotContain(following, l => l.Contains("spellcasting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_next_class_Core_Traits_heading_ends_the_text_before_its_table_page()
    {
        // The 2024 layout opens a class with its "Core … Traits" block and first features a page before its table.
        var following = Following(
            TablePage(1, "Fixture Warden", "Class Features"),
            Prose(2, "Level 2: Fixture Stride", "You move like fixture wind."),
            Prose(3, "Core Fixture Seer Traits", "Hit Point Die D6 per Fixture Seer level", "Level 1: Spellcasting", "Wisdom is your spellcasting ability."),
            TablePage(4, "Fixture Seer", "Class Features"));

        Assert.Contains("You move like fixture wind.", following);
        Assert.DoesNotContain(following, l => l.Contains("spellcasting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_features_line_without_a_level_cell_a_subclass_features_heading_and_prose_naming_core_traits_do_not_end_the_text()
    {
        var following = Following(
            TablePage(1, "Fixture Seer", "Class Features"),
            Prose(2, "Fixture Seer Class Features", "Level 1: Spellcasting"),
            Prose(3, "Level", "Subclass Features", "Each seer has Core Fixture Traits", "Wisdom is your spellcasting ability."));

        Assert.Contains("Wisdom is your spellcasting ability.", following);
    }

    [Fact]
    public void The_class_own_spellcasting_text_on_the_following_pages_is_kept()
    {
        var following = Following(
            TablePage(1, "Fixture Seer", "Class Features"),
            Prose(2, "Level 1: Spellcasting", "Wisdom is your spellcasting ability."),
            Prose(3, "Level 3: Fixture Insight"));

        Assert.Contains("Wisdom is your spellcasting ability.", following);
        Assert.Contains("Level 3: Fixture Insight", following);
    }
}
