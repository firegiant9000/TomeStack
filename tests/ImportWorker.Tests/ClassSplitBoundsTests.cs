using System.Diagnostics;
using TomeStack.ImportWorker.Detection;
using TomeStack.ImportWorker.Extraction;
using TomeStack.RulesCore;

namespace TomeStack.ImportWorker.Tests;

/// <summary>Task 9 fix wave, F1/R127: the per-page class split is bounded and cancellable.</summary>
public class ClassSplitBoundsTests
{
    private static readonly Guid Source = Guid.Parse("5fad5000-0000-4000-8000-000000000002");

    private static readonly string[] Words = ["Alpha", "Bravo", "Cedar", "Delta", "Ember", "Flint", "Grove", "Haven", "Ivory", "Jasper", "Kestrel", "Lumen", "Mosaic", "Nimbus"];

    /// <summary>One invented class table per title, stacked down the page; <paramref name="withTable"/> says which titles get a table.</summary>
    private static List<TextBlock> Page(int classes, Func<int, bool> withTable, int fillerPerClass = 0)
    {
        var blocks = new List<TextBlock>();
        for (var i = 0; i < classes; i++)
        {
            var top = 10_000 - i * 400;
            var name = Words[i % Words.Length] + new string((char)('a' + i / Words.Length), i / Words.Length);
            blocks.Add(new TextBlock($"Fixture {Titled(name)} Features", 50, top, 200, 20, 10, false));
            if (withTable(i))
            {
                blocks.Add(new TextBlock("Level", 50, top - 20, 40, 10, 10, false));
                blocks.Add(new TextBlock("Features", 150, top - 20, 100, 10, 10, false));
                for (var level = 1; level <= 3; level++)
                {
                    blocks.Add(new TextBlock(level == 1 ? "1st" : level == 2 ? "2nd" : "3rd", 50, top - 20 - level * 20, 40, 10, 10, false));
                    blocks.Add(new TextBlock($"Fixture Rite {name} {level}".Replace("1", "One").Replace("2", "Two").Replace("3", "Three"), 150, top - 20 - level * 20, 100, 10, 10, false));
                }
            }
            for (var f = 0; f < fillerPerClass; f++)
                blocks.Add(new TextBlock("Fixture filler words here", 300, top - 100 - f * 2, 100, 1, 1, false));
        }
        return blocks;
    }

    private static string Titled(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private static IReadOnlyList<DraftCandidate> Detect(List<TextBlock> blocks, CancellationToken token = default) =>
        CandidateDetector.Detect([new DetectionPage(1, "", blocks, false)], new(Source, [RulesFamilies.Srd51], _ => false), token);

    [Theory]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    public void A_page_with_up_to_eight_class_titles_is_split_into_its_classes(int titles, int expected)
    {
        var found = Detect(Page(titles, _ => true));

        Assert.Equal(expected, found.Count(c => c.ProposedKind == ContentKind.Class));
    }

    [Fact]
    public void A_page_with_more_than_eight_class_titles_takes_the_merged_read()
    {
        var found = Detect(Page(9, _ => true));

        // The old merged read: the page is read whole as one class, never split nine ways.
        Assert.True(found.Count(c => c.ProposedKind == ContentKind.Class) <= 1);
    }

    [Fact]
    public void A_cancelled_token_stops_the_split()
    {
        var blocks = Page(3, _ => true);
        var page = new DetectionPage(1, "", blocks, false);
        var lines = blocks.Select(b => (1, new TextLine(b.Text, b.X, b.Y, b.Width, b.Height))).ToList();
        var units = blocks.Select((b, i) => new CandidateDetector.Unit(1, i, b, [b.Text], false)).Where(u => u.Lines[0].EndsWith("Features", StringComparison.Ordinal) && u.Lines[0] != "Features").ToList();
        using var cts = new CancellationTokenSource();

        // Not cancelled: the three classes split.
        Assert.Equal(3, CandidateDetector.SplitClasses(page, lines, units, "Fixture Alpha", "Fixture Alpha Features", cts.Token)!.Count);

        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => CandidateDetector.SplitClasses(page, lines, units, "Fixture Alpha", "Fixture Alpha Features", cts.Token));
    }

    [Fact]
    public void A_cancelled_token_stops_the_merged_read_of_a_class_table()
    {
        // G8/H1: the legacy header scan (a table the whole-table reader cannot complete) is quadratic in the window's lines, so
        // it checks the token itself. The token is cancelled after the last window filter, so only the scan's own checks can see it.
        var blocks = Page(9, _ => true, fillerPerClass: 20);
        var page = new DetectionPage(1, "", blocks, false);
        var lines = blocks.Select(b => (1, new TextLine(b.Text, b.X, b.Y, b.Width, b.Height))).ToList();
        var whole = blocks.Select(b => new TextLine(b.Text, b.X, b.Y, b.Width, b.Height)).ToList();

        // Dry run: count the window predicate's calls, and prove the window reaches the legacy readers (no whole table).
        var calls = 0;
        var read = CandidateDetector.ReadClassTable(page, lines, _ => { calls++; return true; }, "Fixture Alpha Features", whole, CancellationToken.None);
        Assert.NotNull(read);
        Assert.Null(read.Table);
        Assert.Equal(lines.Count + whole.Count, calls);

        using var cts = new CancellationTokenSource();
        var seen = 0;
        bool CancelOnLast(TextLine _)
        {
            if (++seen == calls)
                cts.Cancel();
            return true;
        }

        Assert.Throws<OperationCanceledException>(() => CandidateDetector.ReadClassTable(page, lines, CancelOnLast, "Fixture Alpha Features", whole, cts.Token));
        Assert.Equal(calls, seen); // it was the last filter call that cancelled, not an earlier check
    }

    [Fact]
    public void A_page_with_many_headings_is_read_in_bounded_time()
    {
        var blocks = Page(60, i => i == 0, fillerPerClass: 40);
        var watch = Stopwatch.StartNew();

        var found = Detect(blocks);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed.TotalSeconds:F1}s");
        Assert.True(found.Count(c => c.ProposedKind == ContentKind.Class) <= 1);
    }
}
