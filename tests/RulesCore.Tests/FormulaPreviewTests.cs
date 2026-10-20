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
    public void Only_automatic_scales_with_20_values_are_used()
    {
        Assert.NotNull(FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Class, Column(automation: AutomationStatus.Assisted))).Unavailable);
        Assert.NotNull(FormulaPreview.Preview("SCALE.column1", Revision(ContentKind.Class, Column(count: 19))).Unavailable);
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
