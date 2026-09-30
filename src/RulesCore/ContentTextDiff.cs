namespace TomeStack.RulesCore;

public enum TextLineKind { Same, Added, Removed }

public sealed record TextLine(TextLineKind Kind, string Text);

/// <param name="Where"><c>name</c>, <c>summary</c>, or <c>effect:&lt;id&gt;</c> for an effect's rule text.</param>
/// <param name="Lines">The two texts line by line, in order. <paramref name="Whole"/>: too long to align, so old lines then new (capped, see <paramref name="NotShown"/>).</param>
/// <param name="NotShown">How many lines a shown-whole text left out to stay under the caps; 0 when nothing was cut.</param>
public sealed record TextChange(string Where, IReadOnlyList<TextLine> Lines, bool Whole = false, int NotShown = 0);

/// <summary>What a comparison may still show of texts too long to align (shared by all its texts).</summary>
public sealed class WholeBudget
{
    public int Lines { get; set; } = ContentTextDiff.MaxWholeLinesPerComparison;

    public long Chars { get; set; } = ContentTextDiff.MaxWholeCharsPerComparison;
}

/// <summary>
/// M5 slice 4 (B07): the texts of two revisions of a content, compared line by line: the name, the summary and each
/// effect's rule text (by effect id). Mechanics are <see cref="ContentDiff"/>'s. Pure, and bounded (SPEC Q-02): a text
/// longer than <see cref="MaxAlignedLines"/> lines, or a pair whose alignment would exceed <see cref="MaxCells"/>, is
/// shown whole instead of aligned.
/// </summary>
public static class ContentTextDiff
{
    public const int MaxAlignedLines = 2_000;

    public const long MaxCells = 1_000_000;

    /// <summary>The alignment work for all the texts of one comparison together.</summary>
    public const long MaxTotalCells = 4_000_000;

    /// <summary>Caps on the lines and characters returned for a text shown whole, per text and per comparison (SPEC Q-02).</summary>
    public const int MaxWholeLinesPerText = 1_000;

    public const int MaxWholeCharsPerText = 100_000;

    public const int MaxWholeLinesPerComparison = 4_000;

    public const long MaxWholeCharsPerComparison = 400_000;

    public static IReadOnlyList<TextChange> Compare(ContentRevision before, ContentRevision after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changes = new List<TextChange>();
        var whole = new WholeBudget();
        var budget = MaxTotalCells; // across all texts: many small texts must not add up to an unbounded alignment
        void Add(string where, string? a, string? b)
        {
            if (string.Equals(a ?? "", b ?? "", StringComparison.Ordinal))
                return;
            var change = Lines(where, a ?? "", b ?? "", budget, whole);
            if (!change.Whole)
                budget -= (long)CountLines(a ?? "") * CountLines(b ?? "");
            changes.Add(change);
        }
        Add("name", before.Name, after.Name);
        Add("summary", before.Summary, after.Summary);
        var old = before.Effects.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First().Text, StringComparer.Ordinal);
        var ids = before.Effects.Select(e => e.Id).Concat(after.Effects.Select(e => e.Id)).Distinct(StringComparer.Ordinal);
        var @new = after.Effects.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First().Text, StringComparer.Ordinal);
        foreach (var id in ids)
            Add($"effect:{id}", old.GetValueOrDefault(id), @new.GetValueOrDefault(id));
        return changes;
    }

    /// <summary>A line diff by longest common subsequence, or the whole texts when too large to align.</summary>
    /// <param name="budget">The alignment work still allowed (cells); beyond it the texts are shown whole.</param>
    public static TextChange Lines(string where, string before, string after, long budget = MaxCells, WholeBudget? whole = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        // Count first: a text of millions of lines must not be split into millions of strings.
        var countA = CountLines(before);
        var countB = CountLines(after);
        var cells = (long)countA * countB;
        if (countA > MaxAlignedLines || countB > MaxAlignedLines || cells > MaxCells || cells > budget)
            return ShownWhole(where, before, after, countA + (long)countB, whole ?? new WholeBudget());
        var a = Split(before);
        var b = Split(after);

        // Each distinct line becomes an int once, so a cell compares two ints, not two long strings (review fix).
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int[] Ids(string[] text) => [.. text.Select(l => ids.TryGetValue(l, out var id) ? id : ids[l] = ids.Count)];
        var ia = Ids(a);
        var ib = Ids(b);
        // lcs[i, j]: the common length of a[i..] and b[j..].
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = ia[i] == ib[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        }
        var lines = new List<TextLine>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (ia[x] == ib[y])
            {
                lines.Add(new(TextLineKind.Same, a[x++]));
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                lines.Add(new(TextLineKind.Removed, a[x++]));
            }
            else
            {
                lines.Add(new(TextLineKind.Added, b[y++]));
            }
        }
        lines.AddRange(a.Skip(x).Select(l => new TextLine(TextLineKind.Removed, l)));
        lines.AddRange(b.Skip(y).Select(l => new TextLine(TextLineKind.Added, l)));
        return new(where, lines);
    }

    /// <summary>The old lines then the new, up to the per-text and per-comparison caps; the rest is only counted.</summary>
    private static TextChange ShownWhole(string where, string before, string after, long total, WholeBudget whole)
    {
        var lines = new List<TextLine>();
        var linesLeft = Math.Min(MaxWholeLinesPerText, whole.Lines);
        var charsLeft = Math.Min(MaxWholeCharsPerText, whole.Chars);
        foreach (var (kind, text) in new[] { (TextLineKind.Removed, before), (TextLineKind.Added, after) })
        {
            foreach (var line in EnumerateLines(text))
            {
                if (linesLeft <= 0 || charsLeft <= 0)
                    break;
                var shown = line.Length > charsLeft ? line[..(int)charsLeft] : line;
                lines.Add(new(kind, shown));
                linesLeft--;
                charsLeft -= shown.Length;
            }
        }
        whole.Lines -= lines.Count;
        whole.Chars -= lines.Sum(l => (long)l.Text.Length);
        return new(where, lines, Whole: true, NotShown: (int)Math.Min(int.MaxValue, total - lines.Count));
    }

    private static int CountLines(string text) => text.Length == 0 ? 0 : 1 + text.AsSpan().Count('\n');

    private static IEnumerable<string> EnumerateLines(string text)
    {
        if (text.Length == 0)
            yield break;
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
                end = text.Length;
            var stop = end < text.Length && end > start && text[end - 1] == '\r' ? end - 1 : end;
            yield return text[start..stop];
            start = end + 1;
        }
    }

    private static string[] Split(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
