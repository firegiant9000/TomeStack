namespace TomeStack.RulesCore;

public enum TextLineKind { Same, Added, Removed }

public sealed record TextLine(TextLineKind Kind, string Text);

/// <param name="Where"><c>name</c>, <c>summary</c>, or <c>effect:&lt;id&gt;</c> for an effect's rule text.</param>
/// <param name="Lines">The two texts line by line, in order. <paramref name="Whole"/>: too long to align, so all old lines then all new.</param>
public sealed record TextChange(string Where, IReadOnlyList<TextLine> Lines, bool Whole = false);

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

    public static IReadOnlyList<TextChange> Compare(ContentRevision before, ContentRevision after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changes = new List<TextChange>();
        var budget = MaxTotalCells; // across all texts: many small texts must not add up to an unbounded alignment
        void Add(string where, string? a, string? b)
        {
            if (string.Equals(a ?? "", b ?? "", StringComparison.Ordinal))
                return;
            var change = Lines(where, a ?? "", b ?? "", budget);
            if (!change.Whole)
                budget -= (long)Split(a ?? "").Length * Split(b ?? "").Length;
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
    public static TextChange Lines(string where, string before, string after, long budget = MaxCells)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var a = Split(before);
        var b = Split(after);
        var cells = (long)a.Length * b.Length;
        if (a.Length > MaxAlignedLines || b.Length > MaxAlignedLines || cells > MaxCells || cells > budget)
            return new(where, [.. a.Select(l => new TextLine(TextLineKind.Removed, l)), .. b.Select(l => new TextLine(TextLineKind.Added, l))], Whole: true);

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

    private static string[] Split(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
