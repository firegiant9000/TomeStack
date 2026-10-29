using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>D61: the formula preview evaluates a formula at sample levels in a hypothetical context (scores 10, PB by level).</summary>
public class FormulaPreviewTests
{
    private static ContentRevision Revision(ContentKind kind, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse("5fd6c000-0000-4000-8000-000000000001"),
        RevisionId = Guid.Parse("5fd6e000-0000-4000-8000-000000000001"),
        Kind = kind,
        Name = "Test Class",
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(Fixtures.SourceShared),
        Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ScaleEffect Column(int count = 20, AutomationStatus automation = AutomationStatus.Automatic) =>
        new() { Id = "col", ScaleId = "column1", Label = "Column 1", Values = [.. Enumerable.Range(1, count)], Automation = automation };

    private static int?[] Values(FormulaPreviewResult result) => [.. result.Points.Select(p => p.Value)];

    [Fact]
    public void PB_at_the_sample_levels_is_2_3_4_5_6()
    {
        var result = FormulaPreview.Preview("PB", null);
        Assert.Equal([1, 5, 11, 16, 20], result.Points.Select(p => p.Level));
        Assert.Equal<int?>([2, 3, 4, 5, 6], Values(result));
        Assert.Null(result.Error);
        Assert.Null(result.Unavailable);
    }

    [Fact]
    public void Ability_reads_use_scores_of_10()
    {
        Assert.Equal<int?>([2, 3, 4, 5, 6], Values(FormulaPreview.Preview("PB + CON.MOD", null)));
        Assert.All(FormulaPreview.Preview("STR.SCORE", null).Points, p => Assert.Equal(10, p.Value));
    }

    [Fact]
    public void A_scale_of_this_draft_is_read_at_each_level()
    {
        var revision = Revision(ContentKind.Class, Column());
        Assert.Equal<int?>([1, 5, 11, 16, 20], Values(FormulaPreview.Preview("SCALE.column1", revision)));
    }

    [Fact]
    public void A_scale_that_is_not_a_column_of_this_entry_is_not_previewable()
    {
        var result = FormulaPreview.Preview("SCALE.column1 + 1", Revision(ContentKind.Feature));
        Assert.Equal("SCALE.column1 is not a column of this entry, so it can't be previewed here.", result.Unavailable);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void A_named_value_this_draft_declares_is_previewed_and_any_other_is_not()
    {
        var own = new ValueEffect { Id = "v", ValueId = "bump", Formula = "PB + SCALE.column1" };
        var revision = Revision(ContentKind.Class, Column(), own) with { SchemaVersion = 11 };

        Assert.Equal<int?>([3, 8, 15, 21, 26], Values(FormulaPreview.Preview("VALUE.bump", revision)));
        Assert.Equal<int?>([4, 9, 16, 22, 27], Values(FormulaPreview.Preview("VALUE.bump + 1", revision)));

        var foreign = FormulaPreview.Preview("VALUE.elsewhere", revision);
        Assert.Null(foreign.Error);
        Assert.NotNull(foreign.Unavailable);
        Assert.Empty(foreign.Points);

        var cycle = FormulaPreview.Preview("VALUE.loop", Revision(ContentKind.Class, new ValueEffect { Id = "v", ValueId = "loop", Formula = "VALUE.loop + 1" }) with { SchemaVersion = 11 });
        Assert.NotNull(cycle.Unavailable);

        // Below v11 the draft cannot use VALUE at all: it is a parse error, as in the validator.
        Assert.NotNull(FormulaPreview.Preview("VALUE.bump", revision with { SchemaVersion = 10 }).Error);
    }

    private static ContentRevision ValueChain(int links, string read)
    {
        var effects = new List<Effect>();
        for (var i = 0; i < links; i++)
            effects.Add(new ValueEffect { Id = $"c{i}", ValueId = $"c{i}", Formula = i == links - 1 ? "PB" : string.Format(System.Globalization.CultureInfo.InvariantCulture, read, $"VALUE.c{i + 1}") });
        return Revision(ContentKind.Class, [.. effects]) with { SchemaVersion = 11 };
    }

    [Fact]
    public void A_long_chain_of_values_each_read_several_times_previews_promptly()
    {
        var result = FormulaPreview.Preview("VALUE.c0", ValueChain(30, "{0} - {0} + {0}"));

        Assert.Null(result.Unavailable);
        Assert.Equal<int?>([2, 3, 4, 5, 6], Values(result)); // the last link is PB
    }

    [Fact]
    public void A_chain_of_values_nested_deeper_than_the_limit_is_not_previewable()
    {
        Assert.NotNull(FormulaPreview.Preview("VALUE.c0", ValueChain(3000, "{0}")).Unavailable);
        Assert.Null(FormulaPreview.Preview("VALUE.c0", ValueChain(25, "{0}")).Unavailable);
    }

    [Fact]
    public void Only_automatic_scales_with_20_values_are_used()
    {
        Assert.NotNull(FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Class, Column(automation: AutomationStatus.Assisted))).Unavailable);
        Assert.NotNull(FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Class, Column(count: 19))).Unavailable);
    }

    [Fact]
    public void Only_a_class_or_subclass_column_is_used()
    {
        Assert.NotNull(FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Feature, Column())).Unavailable);
        Assert.Null(FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Subclass, Column())).Unavailable);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(FormulaLimits.MaxLiteral + 1)]
    public void A_column_with_a_value_outside_0_to_the_literal_limit_is_not_used(int bad)
    {
        var column = new ScaleEffect { Id = "col", ScaleId = "column1", Label = "Column 1", Values = [.. Enumerable.Range(1, 19), bad], Automation = AutomationStatus.Automatic };
        Assert.NotNull(FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Class, column)).Unavailable);
        var edge = new ScaleEffect { Id = "col", ScaleId = "column1", Label = "Column 1", Values = [.. Enumerable.Range(1, 18), 0, FormulaLimits.MaxLiteral], Automation = AutomationStatus.Automatic };
        Assert.Null(FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Class, edge)).Unavailable);
    }

    [Fact]
    public void Floor_at_zero_shows_what_the_sheet_shows()
    {
        Assert.Equal<int?>([-1, 0, 1, 2, 3], Values(FormulaPreview.Preview("PB - 3", null)));
        Assert.Equal<int?>([0, 0, 1, 2, 3], Values(FormulaPreview.Preview("PB - 3", null, floorAtZero: true)));
    }

    [Fact]
    public void A_failure_at_one_level_leaves_the_other_levels()
    {
        var points = FormulaPreview.Preview("10 / (CLASS_LEVEL - 5)", null).Points;
        var five = points.Single(p => p.Level == 5);
        Assert.Null(five.Value);
        Assert.NotNull(five.Problem);
        Assert.Equal(-3, points.Single(p => p.Level == 1).Value); // floor of -2.5
        Assert.Equal(0, points.Single(p => p.Level == 20).Value);
    }

    [Fact]
    public void A_formula_over_the_limit_reports_the_parse_error()
    {
        var result = FormulaPreview.Preview(string.Concat(Enumerable.Repeat("1", 201)), null);
        Assert.Equal("formula.too-long", result.Error!.Code);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void A_syntax_error_reports_the_parse_error()
    {
        var result = FormulaPreview.Preview("PB +", null);
        Assert.Equal("formula.syntax", result.Error!.Code);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void Custom_levels_are_respected()
    {
        var result = FormulaPreview.Preview("LEVEL", null, [3, 7]);
        Assert.Equal([3, 7], result.Points.Select(p => p.Level));
        Assert.Equal<int?>([3, 7], Values(result));
    }

    [Fact]
    public void A_scale_read_outside_levels_1_to_20_is_a_problem_not_an_exception()
    {
        var points = FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Class, Column()), [0, 21]).Points;
        Assert.All(points, p => Assert.NotNull(p.Problem));
    }

    [Fact]
    public void Preview_matches_the_calculator_at_level_16()
    {
        var pack = Fixtures.ChroniclerPack();
        var chronicler = pack.Revisions.Single(r => r.Reference == Fixtures.Chronicler);
        var draft = chronicler with
        {
            Effects = [.. chronicler.Effects.Select(e => e is ResourceEffect { ResourceId: "ink" } r ? r with { Maximum = "PB + SCALE.ink" } : e)],
        };
        var catalog = new InMemoryContentCatalog(pack.Sources, [draft, .. pack.Revisions.Where(r => r.Reference != Fixtures.Chronicler)]);
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = [new ClassLevel(Fixtures.Chronicler, 16)],
            Level = 16,
            BaseAbilities = new(10, 10, 10, 10, 10, 10),
            Spells = [],
            Choices = [new(Fixtures.Chronicler, "chronicler-archive", [Fixtures.ArchiveOfEchoes])],
        };

        var sheet = CharacterCalculator.Calculate(character, catalog);

        var calculated = sheet.Resources!.Single(r => r.ResourceId == "ink").Maximum;
        var preview = FormulaPreview.Preview("PB + SCALE.ink", draft).Points.Single(p => p.Level == 16).Value;
        Assert.NotNull(calculated);
        Assert.Equal(calculated, preview);
    }
}
