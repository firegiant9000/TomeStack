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

    /// <summary>Capitalises the first letter, for a name that starts a sentence.</summary>
    public static string Cap(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
