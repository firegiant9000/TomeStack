using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Import Lab (D62 L2): <c>content.classTable</c>, the 20-row progression of one class revision, published or draft. Read-only.
/// All content is original.
/// </summary>
public class ClassTableCommandTests
{
    private static ContentRevision Draft(SourceRecord source, ContentKind kind, string name, Guid? contentId = null, params Effect[] effects) => new()
    {
        ContentId = contentId ?? Guid.NewGuid(),
        RevisionId = Guid.Empty,
        Kind = kind,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(source.Id),
        Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static GrantEffect Grant(string id, ContentReference feature, int level) =>
        new() { Id = id, Grant = GrantKind.Content, Content = feature, Level = level };

    [Fact]
    public void Moving_a_feature_in_a_draft_leaves_published_characters_unchanged()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Fixture Class Table Source", [RulesFamilies.Srd521]));
        var pool = app.Publish(app.SaveDraft(Draft(
            source, ContentKind.Feature, "Fixture Pool",
            effects: new ResourceEffect { Id = "res", ResourceId = "pool", Label = "Fixture Pool", Maximum = "2" }))).Published;
        var classId = Guid.NewGuid();
        var published = app.Publish(app.SaveDraft(Draft(source, ContentKind.Class, "Placeholder Class", classId, Grant("g5", pool, 5)))).Published;
        var character = app.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Placeholder", RulesFamily = RulesFamilies.Srd521, Level = 6, Classes = [new(published, 6)],
            BaseAbilities = new(13, 12, 14, 16, 14, 14),
        }).Character.Id;
        var recorded = JsonSerializer.Serialize(app.GetCharacter(character).Sheet, RulesJson.Compact);
        Assert.Contains("pool", recorded); // the level 5 grant is in the recorded sheet

        var draft = app.SaveDraft(Draft(source, ContentKind.Class, "Placeholder Class", classId, Grant("g5", pool, 6)));

        var before = app.ClassTable(new(published.RevisionId));
        var after = app.ClassTable(new(draft.RevisionId));
        Assert.Equal(["Fixture Pool"], before.Rows[4].Features.Select(f => f.Name));
        Assert.Empty(before.Rows[5].Features);
        Assert.Empty(after.Rows[4].Features);
        Assert.Equal(["Fixture Pool"], after.Rows[5].Features.Select(f => f.Name));
        Assert.Equal(recorded, JsonSerializer.Serialize(app.GetCharacter(character).Sheet, RulesJson.Compact));
    }

    [Fact]
    public void A_draft_only_feature_is_named_by_its_newest_revision_not_its_content_id()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Fixture Class Table Source", [RulesFamilies.Srd521]));
        var feature = app.SaveDraft(Draft(source, ContentKind.Feature, "Fixture Draft Feature"));
        var draft = app.SaveDraft(Draft(source, ContentKind.Class, "Placeholder Class", null, Grant("g3", new(feature.ContentId, feature.RevisionId), 3)));

        var view = app.ClassTable(new(draft.RevisionId));

        var granted = Assert.Single(view.Rows[2].Features);
        Assert.Equal("Fixture Draft Feature", granted.Name);
        Assert.Equal(feature.ContentId, granted.ContentId);
    }

    [Fact]
    public void A_class_in_each_bundled_family_has_the_shared_proficiency_bonus()
    {
        using var temp = new TempApp();
        foreach (var resource in new[] { "TomeStack.Content.srd-5.1-classes.json", "TomeStack.Content.srd-5.2.1-classes.json" })
        {
            var classes = TomeStackApp.LoadBundledPack(resource).Revisions.Where(r => r.Kind == ContentKind.Class).ToList();
            Assert.NotEmpty(classes);
            foreach (var cls in classes)
            {
                var view = temp.App.ClassTable(new(cls.RevisionId));
                Assert.Equal(20, view.Rows.Count);
                Assert.All(view.Rows, r => Assert.Equal(DesignFeedback.ProficiencyBonus(r.Level), r.ProficiencyBonus));
            }
        }
    }

    [Fact]
    public void Unknown_and_non_class_revisions_are_refused_and_the_command_is_listed()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Class Table Source", [RulesFamilies.Srd521]));
        var feature = temp.App.SaveDraft(Draft(source, ContentKind.Feature, "Fixture Pool"));

        Assert.Equal("content.not-found", Assert.Throws<AppValidationException>(() => temp.App.ClassTable(new(Guid.NewGuid()))).Problems.Single().Code);
        Assert.Equal("content.not-a-class", Assert.Throws<AppValidationException>(() => temp.App.ClassTable(new(feature.RevisionId))).Problems.Single().Code);
        Assert.Contains("content.classTable", CommandDispatcher.Commands);
    }

    [Fact]
    public void The_dispatcher_answers_with_camel_case_rows()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Class Table Source", [RulesFamilies.Srd521]));
        var draft = temp.App.SaveDraft(Draft(source, ContentKind.Class, "Placeholder Class"));

        var reply = new CommandDispatcher(temp.App).Dispatch($$$"""{"id":"1","command":"content.classTable","payload":{"revisionId":"{{{draft.RevisionId}}}"}}""");

        using var json = JsonDocument.Parse(reply);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        var rows = json.RootElement.GetProperty("result").GetProperty("rows");
        Assert.Equal(20, rows.GetArrayLength());
        Assert.Equal(2, rows[0].GetProperty("proficiencyBonus").GetInt32());
    }
}
