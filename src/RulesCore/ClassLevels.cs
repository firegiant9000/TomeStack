namespace TomeStack.RulesCore;

/// <summary>
/// Import Lab (D62): the one walk of a class revision's progression, shared by the coverage view and the class table. A feature
/// belongs to a class level when the class grants it (<see cref="GrantEffect.Level"/>, a content grant) or offers it as an option
/// of a choice (<see cref="ChoiceEffect.Level"/>, its listed options and the published revisions that extend the choice).
/// Pure: the catalog is read, never written.
/// </summary>
public static class ClassLevels
{
    /// <summary>
    /// The class's level-gated content in effect order: each content grant that has a level, then (in that choice's place) each option
    /// of a choice that has a level. Effects without a level, and grants that are not of content, are not listed. A content listed twice
    /// at one level appears once.
    /// </summary>
    public static IReadOnlyList<(int Level, Guid ContentId, bool IsChoiceOption)> Walk(ContentRevision classRevision, IContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(classRevision);
        ArgumentNullException.ThrowIfNull(catalog);
        var walk = new List<(int Level, Guid ContentId, bool IsChoiceOption)>();
        var seen = new HashSet<(int, Guid, bool)>();
        void Add(int level, Guid contentId, bool isChoiceOption)
        {
            if (seen.Add((level, contentId, isChoiceOption)))
                walk.Add((level, contentId, isChoiceOption));
        }

        foreach (var effect in classRevision.Effects)
        {
            switch (effect)
            {
                case GrantEffect { Grant: GrantKind.Content, Content: { } granted, Level: { } level }:
                    Add(level, granted.ContentId, false);
                    break;
                case ChoiceEffect { Level: { } level } choice:
                    foreach (var option in choice.Options)
                        Add(level, option.ContentId, true);
                    foreach (var extension in catalog.ChoiceExtensions(classRevision.ContentId, choice.ChoiceId))
                        Add(level, extension.ContentId, true);
                    break;
            }
        }
        return walk;
    }
}
