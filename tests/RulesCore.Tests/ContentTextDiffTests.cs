using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>M5 slice 4 (B07): the text half of the diff viewer, line by line and bounded. Original text only.</summary>
public class ContentTextDiffTests
{
    private static ContentRevision Revision(string name, string? summary, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse("5fd2c000-0000-4000-8000-000000000001"),
        RevisionId = Guid.NewGuid(),
        Kind = ContentKind.Feature,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.Parse("5fc05000-0000-4000-8000-000000000001")),
        Status = RevisionStatus.Published,
        Summary = summary,
        Effects = effects,
    };

    private static ModifierEffect Rule(string id, string? text) => new() { Id = id, Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1", Text = text };

    [Fact]
    public void Lines_are_aligned_so_only_what_changed_is_marked()
    {
        var change = ContentTextDiff.Lines("summary", "You hum.\nThe air shimmers.\nYou rest.", "You hum.\nThe air sings.\nYou rest.\nYou wake.");

        Assert.False(change.Whole);
        Assert.Equal(
            [
                (TextLineKind.Same, "You hum."),
                (TextLineKind.Removed, "The air shimmers."),
                (TextLineKind.Added, "The air sings."),
                (TextLineKind.Same, "You rest."),
                (TextLineKind.Added, "You wake."),
            ],
            change.Lines.Select(l => (l.Kind, l.Text)));
    }

    [Fact]
    public void A_revision_pair_reports_the_name_summary_and_each_rules_text_that_differ()
    {
        var before = Revision("Test Hum", "Old words.", Rule("kept", "Same text."), Rule("gone", "Removed rule."), Rule("edited", "Before."));
        var after = Revision("Test Hum Again", "Old words.", Rule("kept", "Same text."), Rule("edited", "After."), Rule("fresh", "New rule."));

        var changes = ContentTextDiff.Compare(before, after);

        Assert.Equal(["name", "effect:gone", "effect:edited", "effect:fresh"], changes.Select(c => c.Where));
        Assert.All(changes.Single(c => c.Where == "effect:fresh").Lines, l => Assert.Equal(TextLineKind.Added, l.Kind));
        Assert.All(changes.Single(c => c.Where == "effect:gone").Lines, l => Assert.Equal(TextLineKind.Removed, l.Kind));
    }

    [Fact]
    public void Texts_too_long_to_align_are_shown_whole_and_the_total_work_is_bounded()
    {
        var longText = string.Join('\n', Enumerable.Range(0, ContentTextDiff.MaxAlignedLines + 1).Select(i => $"line {i}"));
        var whole = ContentTextDiff.Lines("summary", longText, longText + "\nmore");
        Assert.True(whole.Whole);
        Assert.Equal(ContentTextDiff.MaxWholeLinesPerText, whole.Lines.Count);
        Assert.Equal((2 * ContentTextDiff.MaxAlignedLines) + 3 - ContentTextDiff.MaxWholeLinesPerText, whole.NotShown);

        // Many medium texts together: once the shared budget is spent, the rest are shown whole.
        var medium = string.Join('\n', Enumerable.Range(0, 900).Select(i => $"row {i}"));
        Effect[] Rules(string suffix) => [.. Enumerable.Range(0, 10).Select(i => Rule($"r{i}", medium + suffix))];
        var changes = ContentTextDiff.Compare(Revision("Test", null, Rules("")), Revision("Test", null, Rules("\nchanged")));
        Assert.Contains(changes, c => !c.Whole);
        Assert.Contains(changes, c => c.Whole);
    }

    [Fact]
    public void A_huge_text_shown_whole_stays_under_the_caps_and_reports_what_was_left_out()
    {
        var huge = new string('\n', 200_000); // 200,001 empty lines
        var single = ContentTextDiff.Lines("summary", huge, "x");
        Assert.True(single.Whole);
        Assert.True(single.Lines.Count <= ContentTextDiff.MaxWholeLinesPerText);
        Assert.Equal(200_002 - single.Lines.Count, single.NotShown);

        // Long lines are cut to the character cap, and the comparison as a whole is capped too.
        var wide = string.Join('\n', Enumerable.Range(0, 5_000).Select(_ => new string('w', 500)));
        var changes = ContentTextDiff.Compare(
            Revision("Test", huge, Rule("a", wide), Rule("b", wide), Rule("c", wide), Rule("d", wide), Rule("e", wide)),
            Revision("Test", "y", Rule("a", "z"), Rule("b", "z"), Rule("c", "z"), Rule("d", "z"), Rule("e", "z")));
        Assert.True(changes.Sum(c => c.Lines.Count) <= ContentTextDiff.MaxWholeLinesPerComparison);
        Assert.True(changes.Sum(c => c.Lines.Sum(l => (long)l.Text.Length)) <= ContentTextDiff.MaxWholeCharsPerComparison);
        Assert.All(changes.Where(c => c.Whole), c => Assert.True(c.NotShown > 0));
    }
}
