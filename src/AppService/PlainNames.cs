using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// D99: how play messages name a roll, pool, toggle or attack. The player sees the label the sheet shows (quoted); an effect with no
/// label is "that roll", "that pool" and so on. Never an author id, a content GUID or an error code.
/// </summary>
internal static class PlainNames
{
    /// <summary>The label, quoted, or <paramref name="fallback"/> (lower case, unquoted) when there is none.</summary>
    public static string Of(string? label, string fallback) => string.IsNullOrWhiteSpace(label) ? fallback : $"'{label}'";

    public static string Roll(string? label) => Of(label, "that roll");

    /// <summary>The pool's label as the sheet shows it, or "that pool" when the sheet has no such pool.</summary>
    public static string Pool(IReadOnlyList<ResourceValue>? pools, Guid contentId, string? resourceId) =>
        Of(pools?.FirstOrDefault(r => r.Content.ContentId == contentId && r.ResourceId == resourceId)?.Label, "that pool");

    /// <summary>A known rules family as "the 2014 rules" / "the 2024 rules"; an id this app does not know stays as typed.</summary>
    public static string Family(string id) => RulesFamilies.IsKnown(id) ? "the " + RulesFamilies.Get(id).ShortLabel : id;

    /// <summary>
    /// Source titles in order, for a list the player reads. A source nobody can name (null) is "an unknown source"; when there are
    /// two or more, they are numbered so they do not read as one.
    /// </summary>
    public static string SourceTitles(IEnumerable<string?> titles)
    {
        var list = titles.ToList();
        var unknown = list.Count(t => t is null);
        var seen = 0;
        var named = list.Select(t => t ?? (unknown == 1 ? "an unknown source" : $"an unknown source ({++seen})")).ToList();
        return string.Join(", ", named.Order(StringComparer.Ordinal));
    }

    /// <summary>Capitalises the first letter, for a name that starts a sentence.</summary>
    public static string Cap(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
