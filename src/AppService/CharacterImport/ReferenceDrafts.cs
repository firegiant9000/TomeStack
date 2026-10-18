using System.Globalization;
using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <summary>
/// One reference draft the import would write, as the summary lists it (item 16). The name and the first line are the
/// sheet's own words, shown to the user only; <c>ToString</c> prints the kind and the length.
/// </summary>
/// <param name="RowId">The match row it comes from (<c>feat:0</c>, <c>feature:3</c>).</param>
/// <param name="FirstLine">The first non-blank line of the text, cut to 200 characters.</param>
/// <param name="Length">The characters the draft holds (after any cut to 20,000).</param>
public sealed record ReferenceDraftInfo(string RowId, FeatureKind Kind, string Name, string FirstLine, int Length)
{
    public override string ToString() => $"ReferenceDraftInfo {{ Kind = {Kind}, Length = {Length} }}";
}

/// <summary>What the import writes for its reference drafts: the drafts, how the summary lists them, and the report lines.</summary>
internal sealed record ReferenceDraftSet(IReadOnlyList<ContentRevision> Drafts, IReadOnlyList<ReferenceDraftInfo> Info, IReadOnlyList<string> Lines)
{
    public static ReferenceDraftSet None { get; } = new([], [], []);
}

/// <summary>
/// Item 16 (D16(c), R104, R105): each feat or feature the planner left unmatched and the sheet gave text for becomes one
/// draft of the same kind, holding its name and its description (the revision's summary) and nothing else: no effects, so no
/// mechanics. Pure; nothing here stores anything. Names and texts never reach a message: the report lines carry the entry's
/// position and counts only.
/// </summary>
internal static class ReferenceDrafts
{
    /// <summary>The most characters one draft's text keeps; a longer one is cut (at a code point) with a report line.</summary>
    public const int MaxTextChars = 20_000;

    /// <summary>The most reference drafts one import makes; the rest keep their gap notes only.</summary>
    public const int MaxDrafts = 100;

    private const int FirstLineChars = 200;

    public static ReferenceDraftSet Build(DdbSheet sheet, IReadOnlyList<MatchRow> rows, string family, Guid sourceId)
    {
        if (sheet.FeatureTexts is not { Count: > 0 } texts)
            return ReferenceDraftSet.None;
        // A name listed twice takes its texts in sheet order, as the rows come in sheet order.
        var queues = texts.GroupBy(t => (t.Kind, Normalise.Name(t.Name))).ToDictionary(g => g.Key, g => new Queue<FeatureText>(g));
        var drafts = new List<ContentRevision>();
        var info = new List<ReferenceDraftInfo>();
        var lines = new List<string>();
        var skipped = 0;
        foreach (var row in rows.Where(r => r.Status == MatchStatus.NotFound && r.Kind is MatchKind.Feat or MatchKind.Feature))
        {
            var kind = row.Kind == MatchKind.Feat ? FeatureKind.Feat : FeatureKind.Feature;
            if (!queues.TryGetValue((kind, Normalise.Name(row.Label)), out var queue) || !queue.TryDequeue(out var found))
                continue;
            if (drafts.Count >= MaxDrafts)
            {
                skipped++;
                continue;
            }
            var text = found.Text;
            if (text.Length > MaxTextChars)
            {
                var end = char.IsHighSurrogate(text[MaxTextChars - 1]) ? MaxTextChars - 1 : MaxTextChars;
                text = text[..end];
                lines.Add($"{(kind == FeatureKind.Feat ? "Feat" : "Feature")} {Position(row.RowId)}'s text was cut to {MaxTextChars.ToString("N0", CultureInfo.InvariantCulture)} characters.");
            }
            drafts.Add(new ContentRevision
            {
                ContentId = Guid.NewGuid(),
                RevisionId = Guid.NewGuid(),
                Kind = kind == FeatureKind.Feat ? ContentKind.Feat : ContentKind.Feature,
                Name = row.Label,
                RulesFamilies = [family],
                Provenance = new(sourceId),
                Status = RevisionStatus.Draft,
                Summary = text,
            });
            info.Add(new(row.RowId, kind, row.Label, FirstLine(text), text.Length));
        }
        if (skipped > 0)
            lines.Add(skipped == 1 ? "1 more feature was left as a gap note." : $"{skipped.ToString(CultureInfo.InvariantCulture)} more features were left as gap notes.");
        return new(drafts, info, lines);
    }

    /// <summary>The 1-based position of an entry in its list, from its row id (<c>feature:2</c> is the third).</summary>
    private static int Position(string rowId) =>
        int.TryParse(rowId.AsSpan(rowId.IndexOf(':') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index + 1 : 0;

    private static string FirstLine(string text)
    {
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (line.Length <= FirstLineChars)
            return line;
        var end = char.IsHighSurrogate(line[FirstLineChars - 1]) ? FirstLineChars - 1 : FirstLineChars;
        return line[..end];
    }
}
