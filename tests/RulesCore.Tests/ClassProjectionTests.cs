namespace TomeStack.RulesCore.Tests;

/// <summary>Import Lab (D62 L2): the 20-row progression grid of one class revision. Pure; every class here is an original placeholder.</summary>
public class ClassProjectionTests
{
    private static readonly Guid SourceId = Guid.NewGuid();

    private static ContentRevision Revision(ContentKind kind, string name, string family, params Effect[] effects) => new()
    {
        ContentId = Guid.NewGuid(),
        RevisionId = Guid.NewGuid(),
        Kind = kind,
        Name = name,
        RulesFamilies = [family],
        Provenance = new(SourceId),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static ContentRevision Feature(string name) => Revision(ContentKind.Feature, name, RulesFamilies.Srd521);

    private static GrantEffect Grant(string id, ContentRevision content, int level) =>
        new() { Id = id, Grant = GrantKind.Content, Content = content.Reference, Level = level };

    private static ClassTableView Project(ContentRevision cls, params ContentRevision[] others) =>
        ClassProjection.Of(cls, new InMemoryContentCatalog([], [cls, .. others]));

    [Fact]
    public void Twenty_rows_for_any_class()
    {
        var view = Project(Revision(ContentKind.Class, "Placeholder Class", RulesFamilies.Srd521));

        Assert.Equal(20, view.Rows.Count);
        Assert.Equal(Enumerable.Range(1, 20), view.Rows.Select(r => r.Level));
        Assert.Empty(view.Columns);
    }

    [Fact]
    public void Proficiency_bonus_matches_the_shared_rule()
    {
        foreach (var family in new[] { RulesFamilies.Srd51, RulesFamilies.Srd521 })
        {
            var view = Project(Revision(ContentKind.Class, "Placeholder Class", family));

            Assert.All(view.Rows, r => Assert.Equal(DesignFeedback.ProficiencyBonus(r.Level), r.ProficiencyBonus));
            Assert.Equal([2, 3, 4, 5, 6], new[] { 1, 5, 9, 13, 17 }.Select(l => view.Rows[l - 1].ProficiencyBonus));
        }

        // Never by family name: a difference between families would be a RulesFamilyPolicy field first.
        Assert.DoesNotContain("\"srd-", File.ReadAllText(SourceFile("src", "RulesCore", "ClassProjection.cs")));
    }

    private static string SourceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
            directory = directory.Parent;
        return Path.Combine([directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), .. parts]);
    }

    [Fact]
    public void Features_and_choices_land_on_their_level()
    {
        var first = Feature("Placeholder First");
        var third = Feature("Placeholder Third");
        var optionA = Feature("Placeholder Option A");
        var optionB = Feature("Placeholder Option B");
        var path = Revision(ContentKind.Subclass, "Placeholder Path", RulesFamilies.Srd521);
        var cls = Revision(ContentKind.Class, "Placeholder Class", RulesFamilies.Srd521);
        path = path with { ExtendsChoice = new(cls.ContentId, "path") };
        cls = cls with
        {
            Effects =
            [
                Grant("g3", third, 3),
                Grant("g1", first, 1),
                new ChoiceEffect { Id = "c", ChoiceId = "path", Level = 3, Options = [optionA.Reference, optionB.Reference] },
            ],
        };

        var view = Project(cls, first, third, optionA, optionB, path);

        Assert.Equal(["Placeholder First"], view.Rows[0].Features.Select(f => f.Name));
        Assert.Equal([first.ContentId], view.Rows[0].Features.Select(f => f.ContentId));
        Assert.Empty(view.Rows[0].Choices);
        Assert.Equal(["Placeholder Third"], view.Rows[2].Features.Select(f => f.Name));
        Assert.Equal(["Placeholder Option A", "Placeholder Option B", "Placeholder Path"], view.Rows[2].Choices.Select(c => c.Name));
        Assert.Equal([optionA.ContentId, optionB.ContentId, path.ContentId], view.Rows[2].Choices.Select(c => c.ContentId));
        Assert.All(view.Rows.Where(r => r.Level is not (1 or 3)), r => Assert.True(r.Features.Count == 0 && r.Choices.Count == 0));
    }

    [Fact]
    public void Scale_columns_show_all_twenty_values()
    {
        var values = Enumerable.Range(1, 20).Select(l => l * 10).ToArray();
        var cls = Revision(
            ContentKind.Class, "Placeholder Class", RulesFamilies.Srd521,
            new ScaleEffect { Id = "s1", ScaleId = "fixtureDice", Label = "Fixture Dice", Values = values },
            new ScaleEffect { Id = "s2", ScaleId = "fixtureUses", Label = "Fixture Uses", Values = [.. Enumerable.Repeat(2, 20)] });

        var view = Project(cls);

        Assert.Equal(["Fixture Dice", "Fixture Uses"], view.Columns);
        Assert.Equal(values.Select(v => v.ToString()), view.Rows.Select(r => r.Scales["Fixture Dice"]));
        Assert.All(view.Rows, r => Assert.Equal("2", r.Scales["Fixture Uses"]));
    }

    [Fact]
    public void Slots_rows_match_spellcasting_slots()
    {
        var slots = Enumerable.Range(1, 20).Select(l => (IReadOnlyList<int>)[Math.Min(4, l), l >= 3 ? 2 : 0]).ToArray();
        var cls = Revision(
            ContentKind.Class, "Placeholder Caster", RulesFamilies.Srd521,
            new SpellcastingEffect { Id = "sc", Ability = Ability.Wis, SpellList = "fixture-list", Slots = slots });

        var view = Project(cls);

        Assert.Equal(slots, view.Rows.Select(r => r.Slots));
        Assert.All(view.Rows, r => Assert.True(r.PactSlots is null && r.PactLevel is null));
    }

    [Fact]
    public void Pact_slots_show_their_count_and_level_and_leave_slots_empty()
    {
        var slots = Enumerable.Range(1, 20)
            .Select(l => (IReadOnlyList<int>)(l == 1 ? [1, 0, 0] : l < 11 ? [0, 2, 0] : [0, 0, 3]))
            .ToArray();
        var cls = Revision(
            ContentKind.Class, "Placeholder Pact Caster", RulesFamilies.Srd521,
            new SpellcastingEffect { Id = "sc", Ability = Ability.Cha, SpellList = "fixture-list", SlotKind = SpellSlotKind.PactMagic, Slots = slots });

        var view = Project(cls);

        Assert.All(view.Rows, r => Assert.Null(r.Slots));
        Assert.Equal((1, 1), (view.Rows[0].PactSlots, view.Rows[0].PactLevel));
        Assert.Equal((2, 2), (view.Rows[4].PactSlots, view.Rows[4].PactLevel));
        Assert.Equal((3, 3), (view.Rows[10].PactSlots, view.Rows[10].PactLevel));
    }

    [Fact]
    public void A_malformed_table_shows_what_it_has_and_never_throws()
    {
        var cls = Revision(
            ContentKind.Class, "Placeholder Class", RulesFamilies.Srd521,
            new ScaleEffect { Id = "s", ScaleId = "short", Label = "Short", Values = [1, 2, 3] },
            new SpellcastingEffect { Id = "sc", Ability = Ability.Wis, SpellList = "fixture-list", Slots = [[2]] });

        var view = Project(cls);

        Assert.Equal(20, view.Rows.Count);
        Assert.Equal(["1", "2", "3", ""], view.Rows.Take(4).Select(r => r.Scales["Short"]));
        Assert.Equal([2], view.Rows[0].Slots);
        Assert.Null(view.Rows[1].Slots);
    }
}
