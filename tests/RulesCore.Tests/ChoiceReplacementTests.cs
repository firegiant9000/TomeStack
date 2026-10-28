using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// MECH-X item 2 (R42, R43, R47): the sheet carries a choice's replace window, and <see cref="ChoiceReplacements.OutsideWindow"/>
/// says when a save swapped picks outside it. Choices match by content id, never revision id.
/// </summary>
public class ChoiceReplacementTests
{
    private static ContentReference Source(int generation = 1) =>
        new(Guid.Parse("6a7e00f2-0000-4000-8000-000000000001"), Guid.Parse($"6a7e00f2-0000-4000-8000-0000000001{generation:D2}"));

    private static ContentReference Option(int n, int generation = 1) =>
        new(Guid.Parse($"6a7e00f2-0000-4000-8000-0000000000{n:D2}"), Guid.Parse($"6a7e00f2-0000-4000-8000-00000000{generation:D2}{n:D2}"));

    private static ContentReference[] Picks(string ids, int generation = 1) =>
        [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => Option(int.Parse(n), generation))];

    private static Character At(int level, params ChoiceSelection[] choices) =>
        Fixtures.Load("srd521-ash-m1.json") with { Pins = [], Classes = [], Level = level, Choices = choices };

    private static ChoiceStatus Status(ChoiceReplaceable? replaceable, int generation = 1, string choiceId = "techniques") =>
        new(Source(generation), "Fixture Shifting Repertoire", choiceId, null, 2, [], [], true, replaceable);

    // before / after: option numbers; "repin" moves the source and every option to the next revision ids of the same content.
    [Theory]
    [InlineData(ReplaceWindow.LevelUp, 1, 0, "1,2", "1,3", false, 1)] // a swap at the same level
    [InlineData(ReplaceWindow.LevelUp, 1, 1, "1,2", "1,3", false, 0)] // the level rose
    [InlineData(ReplaceWindow.LevelUp, 1, 1, "1,2", "3,4", false, 1)] // two swapped, max 1, even though the level rose
    [InlineData(ReplaceWindow.LevelUp, 2, 1, "1,2", "3,4", false, 0)] // exactly max
    [InlineData(ReplaceWindow.LevelUp, 1, 1, "1,2,3", "1,4,5", false, 1)] // max + 1 of three
    [InlineData(ReplaceWindow.LongRest, 1, 0, "1,2", "1,3", false, 0)] // timing can't be seen
    [InlineData(ReplaceWindow.LongRest, 1, 0, "1,2", "3,4", false, 1)] // more than max
    [InlineData(ReplaceWindow.LevelUp, 1, 0, "", "1,2", false, 0)] // a first pick
    [InlineData(ReplaceWindow.LevelUp, 1, 0, "1", "1,2", false, 0)] // a grown choice
    [InlineData(ReplaceWindow.LevelUp, 1, 0, "1", "", false, 1)] // clearing a pick
    [InlineData(ReplaceWindow.LongRest, 1, 0, "1,2", "", false, 1)] // clearing two, max 1
    [InlineData(ReplaceWindow.LevelUp, 1, 0, "1,2", "1,2", true, 0)] // a content update re-pins source and options
    [InlineData(ReplaceWindow.LevelUp, 1, 0, "1,2", "1,3", true, 1)] // a re-pin that also swaps one
    public void Outside_window(ReplaceWindow window, int max, int levelRise, string before, string after, bool repin, int notices)
    {
        var generation = repin ? 2 : 1;
        var stored = At(3, new ChoiceSelection(Source(), "techniques", Picks(before)));
        var saved = At(3 + levelRise, new ChoiceSelection(Source(generation), "techniques", Picks(after, generation)));
        if (before.Length == 0)
            stored = stored with { Choices = [] };
        if (after.Length == 0)
            saved = saved with { Choices = [] };

        var result = ChoiceReplacements.OutsideWindow(stored, saved, [Status(new ChoiceReplaceable(window, max), generation)]);

        Assert.Equal(notices, result.Count);
        var replaced = Picks(before).Count(b => !Picks(after).Any(a => a.ContentId == b.ContentId));
        foreach (var notice in result)
        {
            Assert.Equal("choice.replaced-outside-window", notice.Code);
            Assert.Equal(Source(generation), notice.Content);
            Assert.Contains("'techniques'", notice.Message, StringComparison.Ordinal);
            Assert.Contains($"had {replaced} pick(s)", notice.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_choice_without_replaceable_gets_no_notice()
    {
        var stored = At(3, new ChoiceSelection(Source(), "techniques", Picks("1,2")));
        var saved = At(3, new ChoiceSelection(Source(), "techniques", Picks("1,3")));

        Assert.Empty(ChoiceReplacements.OutsideWindow(stored, saved, [Status(null)]));
        Assert.Empty(ChoiceReplacements.OutsideWindow(stored, saved, null));
    }

    [Fact]
    public void A_null_selection_entry_gets_no_notice_and_does_not_throw()
    {
        var real = new ChoiceSelection(Source(), "techniques", Picks("1,3"));
        var stored = At(3, null!, new ChoiceSelection(Source(), "techniques", Picks("1,2")));
        var saved = At(3, null!, real, new ChoiceSelection(Source(), "other", null!));

        var result = ChoiceReplacements.OutsideWindow(stored, saved, [null!, Status(new ChoiceReplaceable(ReplaceWindow.LevelUp)), Status(new ChoiceReplaceable(ReplaceWindow.LevelUp), choiceId: "other")]);

        Assert.Single(result); // the swap at the same level; the null entries and the null list are skipped
        Assert.Empty(ChoiceReplacements.OutsideWindow(At(3, null!), At(3, null!), [Status(new ChoiceReplaceable(ReplaceWindow.LevelUp))]));
    }

    [Fact]
    public void An_orphan_entry_on_an_older_revision_counts_as_stored_and_the_last_exact_entry_as_saved()
    {
        // S6: stored picks are the union over the same source content and choice; saved picks are the last entry of the exact source.
        var stored = At(3, new ChoiceSelection(Source(1), "techniques", Picks("1,2")), new ChoiceSelection(Source(2), "techniques", Picks("3")));
        var saved = At(3, new ChoiceSelection(Source(2), "techniques", Picks("1,2", 2)));

        var result = ChoiceReplacements.OutsideWindow(stored, saved, [Status(new ChoiceReplaceable(ReplaceWindow.LevelUp), 2)]);

        Assert.Single(result); // pick 3 is gone from the union
    }

    [Fact]
    public void The_same_orphan_entry_in_both_characters_gets_no_notice()
    {
        var orphan = new ChoiceSelection(Source(1), "techniques", Picks("3"));
        var stored = At(3, orphan, new ChoiceSelection(Source(2), "techniques", Picks("1,2", 2)));
        var saved = At(3, orphan, new ChoiceSelection(Source(2), "techniques", Picks("1,2", 2)));

        Assert.Empty(ChoiceReplacements.OutsideWindow(stored, saved, [Status(new ChoiceReplaceable(ReplaceWindow.LevelUp), 2)]));
    }

    [Fact]
    public void A_level_in_a_second_class_opens_the_level_up_window()
    {
        var stored = At(3, new ChoiceSelection(Source(), "techniques", Picks("1,2"))) with { Classes = [new(Fixtures.Warden, 3)] };
        var saved = stored with { Classes = [new(Fixtures.Warden, 3), new(Fixtures.Scholar, 1)], Choices = [new(Source(), "techniques", Picks("1,3"))] };
        var status = Status(new ChoiceReplaceable(ReplaceWindow.LevelUp));

        Assert.Empty(ChoiceReplacements.OutsideWindow(stored, saved, [status]));
        Assert.Single(ChoiceReplacements.OutsideWindow(stored, saved with { Classes = stored.Classes }, [status]));
    }

    [Fact]
    public void Clearing_then_re_picking_is_two_saves_and_the_clear_gets_the_notice()
    {
        var picked = At(3, new ChoiceSelection(Source(), "techniques", Picks("1,2")));
        var cleared = picked with { Choices = [] };
        var repicked = picked with { Choices = [new(Source(), "techniques", Picks("1,3"))] };
        var status = Status(new ChoiceReplaceable(ReplaceWindow.LevelUp));

        Assert.Single(ChoiceReplacements.OutsideWindow(picked, cleared, [status])); // R42: clearing is a replacement
        Assert.Empty(ChoiceReplacements.OutsideWindow(cleared, repicked, [status])); // from nothing it is a first pick
    }

    private static InMemoryContentCatalog GoldenCatalog(int featureVersion)
    {
        var m1 = Fixtures.M1Pack();
        var feature = RevisionHashProperties.Read(GoldenRevisions.ChoiceJson(0).Replace("\"schemaVersion\":12", $"\"schemaVersion\":{featureVersion}", StringComparison.Ordinal));
        var rest = Enumerable.Range(1, 5).Select(i => RevisionHashProperties.Read(GoldenRevisions.ChoiceJson(i)));
        var source = new SourceRecord
        {
            Id = Guid.Parse("6a7e0000-0000-4000-8000-000000000001"),
            Title = "Fixture Replaceable Choices",
            Publisher = "Fixture",
            RulesFamilies = [RulesFamilies.Srd521],
            EditionVersion = "1",
            License = "Fixture",
            Redistributable = false,
        };
        return new InMemoryContentCatalog([.. m1.Sources, source], [.. m1.Revisions, feature, .. rest]);
    }

    [Fact]
    public void Sheet_carries_replaceable()
    {
        var feature = new ContentReference(Guid.Parse("6a7e00f2-0000-4000-8000-000000000001"), Guid.Parse("6a7e00f2-0000-4000-8000-000000000101"));
        var character = Fixtures.Load("srd521-ash-m1.json") with { Pins = [feature], Classes = [] };

        var choices = CharacterCalculator.Calculate(character, GoldenCatalog(12)).Choices!;

        Assert.Equal(new ChoiceReplaceable(ReplaceWindow.LevelUp, 1), Assert.Single(choices, c => c.ChoiceId == "techniques").Replaceable);
        var stance = Assert.Single(choices, c => c.ChoiceId == "stance").Replaceable!;
        Assert.Equal(ReplaceWindow.LongRest, stance.When);
        Assert.Equal(1, stance.Max); // an absent max reads as 1

        var v11 = CharacterCalculator.Calculate(character, GoldenCatalog(11)).Choices!;
        Assert.Equal(2, v11.Count);
        Assert.All(v11, c => Assert.Null(c.Replaceable));
    }
}
