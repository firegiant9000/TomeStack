using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>D61: <c>content.previewFormula</c>, a read-only formula preview at sample levels.</summary>
public class FormulaPreviewCommandTests
{
    private static ContentRevision ClassDraft() => new()
    {
        ContentId = Guid.NewGuid(),
        RevisionId = Guid.NewGuid(),
        Kind = ContentKind.Class,
        Name = "Test Class",
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Guid.NewGuid()),
        Status = RevisionStatus.Draft,
        Effects = [new ScaleEffect { Id = "col", ScaleId = "column1", Label = "Column 1", Values = [.. Enumerable.Range(1, 20).Select(i => i * 10)], Automation = AutomationStatus.Automatic }],
    };

    [Fact]
    public void Previews_a_formula_through_the_dispatcher()
    {
        using var temp = new TempApp();
        var reply = new CommandDispatcher(temp.App).Dispatch("""{"id":"1","command":"content.previewFormula","payload":{"formula":"PB"}}""");
        using var json = JsonDocument.Parse(reply);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        var values = json.RootElement.GetProperty("result").GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetInt32());
        Assert.Equal([2, 3, 4, 5, 6], values);
    }

    [Fact]
    public void Reads_the_unsaved_draft_scales()
    {
        using var temp = new TempApp();
        var result = temp.App.PreviewFormula(new("SCALE.column1", ClassDraft()));
        Assert.Equal<int?>([10, 50, 110, 160, 200], result.Points.Select(p => p.Value));
    }

    [Fact]
    public void Passes_floorAtZero_through()
    {
        using var temp = new TempApp();
        Assert.Equal<int?>([-1, 0, 1, 2, 3], temp.App.PreviewFormula(new("PB - 3")).Points.Select(p => p.Value));
        Assert.Equal<int?>([0, 0, 1, 2, 3], temp.App.PreviewFormula(new("PB - 3", FloorAtZero: true)).Points.Select(p => p.Value));
        var reply = new CommandDispatcher(temp.App).Dispatch("""{"id":"1","command":"content.previewFormula","payload":{"formula":"PB - 3","floorAtZero":true}}""");
        using var json = JsonDocument.Parse(reply);
        Assert.Equal(0, json.RootElement.GetProperty("result").GetProperty("points")[0].GetProperty("value").GetInt32());
    }

    [Fact]
    public void Writes_nothing()
    {
        using var temp = new TempApp();
        var before = temp.App.Store.ListRevisionsInOrder().Count();
        temp.App.PreviewFormula(new("SCALE.column1", ClassDraft()));
        Assert.Equal(before, temp.App.Store.ListRevisionsInOrder().Count());
    }

    [Fact]
    public void A_missing_formula_is_a_validation_error()
    {
        using var temp = new TempApp();
        var ex = Assert.Throws<AppValidationException>(() => temp.App.PreviewFormula(new(null)));
        Assert.Equal("formula-preview.formula", ex.Problems.Single().Code);
    }

    [Fact]
    public void Levels_outside_1_to_20_or_more_than_20_are_refused()
    {
        using var temp = new TempApp();
        foreach (var levels in new[] { new[] { 0 }, [21], [.. Enumerable.Repeat(1, 21)], [] })
        {
            var ex = Assert.Throws<AppValidationException>(() => temp.App.PreviewFormula(new("PB", Levels: levels)));
            Assert.Equal("formula-preview.levels", ex.Problems.Single().Code);
        }
    }

    [Fact]
    public void The_command_is_listed()
    {
        Assert.Contains("content.previewFormula", CommandDispatcher.Commands);
    }
}
