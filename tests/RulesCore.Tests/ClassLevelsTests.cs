namespace TomeStack.RulesCore.Tests;

/// <summary>Import Lab (D62): the one walk of a class revision's level-gated grants and choice options, shared by coverage and the class table.</summary>
public class ClassLevelsTests
{
    private static readonly Guid SourceId = Guid.NewGuid();

    private static ContentRevision Revision(ContentKind kind, string name, params Effect[] effects) => new()
    {
        ContentId = Guid.NewGuid(),
        RevisionId = Guid.NewGuid(),
        Kind = kind,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(SourceId),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static GrantEffect Grant(string id, ContentRevision content, int? level) =>
        new() { Id = id, Grant = GrantKind.Content, Content = content.Reference, Level = level };

    [Fact]
    public void Walk_lists_grants_and_choice_options_at_their_level()
    {
        var first = Revision(ContentKind.Feature, "Placeholder First");
        var second = Revision(ContentKind.Feature, "Placeholder Second");
        var optionA = Revision(ContentKind.Feature, "Placeholder Option A");
        var optionB = Revision(ContentKind.Feature, "Placeholder Option B");
        var extension = Revision(ContentKind.Subclass, "Placeholder Path") with { ExtendsChoice = new(Guid.Empty, "path") };
        var cls = Revision(ContentKind.Class, "Placeholder Class");
        extension = extension with { ExtendsChoice = new(cls.ContentId, "path") };
        cls = cls with
        {
            Effects =
            [
                Grant("g2", second, 2),
                Grant("g1", first, 1),
                new ChoiceEffect { Id = "c", ChoiceId = "path", Level = 3, Options = [optionA.Reference, optionB.Reference] },
            ],
        };
        var catalog = new InMemoryContentCatalog([], [cls, first, second, optionA, optionB, extension]);

        var walk = ClassLevels.Walk(cls, catalog);

        Assert.Equal(
            [
                (2, second.ContentId, false),
                (1, first.ContentId, false),
                (3, optionA.ContentId, true),
                (3, optionB.ContentId, true),
                (3, extension.ContentId, true),
            ],
            walk.Select(w => (w.Level, w.ContentId, w.IsChoiceOption)));
    }

    [Fact]
    public void Walk_ignores_effects_without_a_level()
    {
        var feature = Revision(ContentKind.Feature, "Placeholder Feature");
        var option = Revision(ContentKind.Feature, "Placeholder Option");
        var cls = Revision(
            ContentKind.Class, "Placeholder Class",
            Grant("no-level", feature, null),
            new ChoiceEffect { Id = "c", ChoiceId = "c", Options = [option.Reference] },
            new GrantEffect { Id = "prof", Grant = GrantKind.Proficiency, Target = "save.wis", Level = 1 },
            new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" });

        Assert.Empty(ClassLevels.Walk(cls, new InMemoryContentCatalog([], [cls, feature, option])));
    }
}
