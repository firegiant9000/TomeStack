using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Original homebrew content for the character-sheet import tests (SPEC Q-03: every name starts with "Fixture"),
/// published through the studio commands as a user would.
/// </summary>
internal static class DdbTestContent
{
    public static Guid Source(TempApp temp, string title, params string[] families) =>
        temp.App.CreateHomebrewSource(new(title, families)).Id;

    public static ContentReference Publish(TempApp temp, Guid source, ContentKind kind, string name, IReadOnlyList<string> families, IReadOnlyList<Effect>? effects = null, ChoiceExtension? extendsChoice = null) =>
        temp.App.Publish(temp.App.SaveDraft(new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.Empty,
            Kind = kind,
            Name = name,
            RulesFamilies = families,
            Provenance = new(source),
            Status = RevisionStatus.Draft,
            Summary = "Fixture content for the character-sheet import tests.",
            Effects = effects ?? [],
            ExtendsChoice = extendsChoice,
        })).Published;

    public static ModifierEffect Modifier(string id, string target, ModifierOperation operation, int value) =>
        new() { Id = id, Operation = operation, Target = target, Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    public static Character Character(string family, params ContentReference[] pins) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Testy McFixture",
        RulesFamily = family,
        BaseAbilities = new(10, 10, 10, 10, 10, 10),
        Pins = pins,
    };
}
