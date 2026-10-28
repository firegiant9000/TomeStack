namespace TomeStack.RulesCore;

/// <summary>
/// MECH-X item 2 (R42, R43, R47): the advisory notice for a save that replaces picks of a replaceable choice outside its window.
/// Pure and read-only: it compares two characters and the saved sheet's choices, writes nothing and never throws.
/// </summary>
public static class ChoiceReplacements
{
    public const string NoticeCode = "choice.replaced-outside-window";

    /// <summary>
    /// One notice per replaceable choice of <paramref name="choices"/> (the saved sheet's) whose picks were replaced outside the window.
    /// Both sides read the union over every entry of the same source content id and choice id (so an orphan on an older revision
    /// that both characters carry cancels out). Choices match by (source content id, choice id) and picks by option content id, never by revision id, so a content update that
    /// re-pins a source or an option is not a replacement. Replaced = option content ids in the stored selection that the saved one
    /// no longer holds (clearing a pick counts; a first pick or a grown choice does not). Level-up window: a notice when something was
    /// replaced and the total level did not rise, or when more than <c>max</c> were replaced. Long-rest window: only more than <c>max</c>.
    /// The message holds ids and counts only.
    /// </summary>
    public static IReadOnlyList<Diagnostic> OutsideWindow(Character before, Character after, IReadOnlyList<ChoiceStatus>? choices)
    {
        if (before is null || after is null || choices is null)
            return [];
        var notices = new List<Diagnostic>();
        foreach (var choice in choices)
        {
            if (choice?.Replaceable is not { } window)
                continue;
            var max = window.Max ?? 1;

            // Both sides: the union of option content ids over every entry of the same source content and choice, so an
            // orphan on an older revision that both characters carry cancels out instead of looking like removed picks.
            var savedIds = Union(after, choice);
            var storedIds = Union(before, choice);

            var replaced = storedIds.Count(id => !savedIds.Contains(id));
            if (replaced == 0)
                continue;
            var outside = window.When == ReplaceWindow.LevelUp
                ? after.TotalLevel <= before.TotalLevel || replaced > max
                : replaced > max;
            if (outside)
                notices.Add(new(NoticeCode, $"Choice '{choice.ChoiceId}' had {replaced} pick(s) replaced outside its window.", choice.Source));
        }
        return notices;
    }

    private static HashSet<Guid> Union(Character character, ChoiceStatus choice)
    {
        var ids = new HashSet<Guid>();
        foreach (var entry in character.Choices ?? [])
        {
            if (entry is not null && entry.Source?.ContentId == choice.Source.ContentId && entry.ChoiceId == choice.ChoiceId)
                ids.UnionWith(ContentIds(entry));
        }
        return ids;
    }

    private static HashSet<Guid> ContentIds(ChoiceSelection? entry) =>
        entry?.Selected is { } selected ? [.. selected.Where(s => s is not null).Select(s => s.ContentId)] : [];
}
