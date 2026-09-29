using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>M5 slice 5 (B19): <c>content.tree</c>, the relationship tree of one revision among its source's drafts. Read-only; original content.</summary>
public class ContentTreeCommandTests
{
    private static ContentRevision Draft(SourceRecord source, Guid contentId, ContentKind kind, string name, params Effect[] effects) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Empty,
        Kind = kind,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(source.Id),
        Status = RevisionStatus.Draft,
        Effects = effects,
    };

    [Fact]
    public void The_unsaved_class_shows_the_draft_feature_it_grants_and_nothing_is_written()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Tree Source", [RulesFamilies.Srd521]));
        var feature = app.SaveDraft(Draft(source, Guid.NewGuid(), ContentKind.Feature, "Test Tree Feature",
            new ResourceEffect { Id = "sparks", ResourceId = "sparks", Label = "Sparks", Maximum = "PB" },
            new RollEffect { Id = "zap", RollId = "zap", Label = "Zap", Dice = "1d6", ResourceId = "sparks" }));
        var cls = Draft(source, Guid.NewGuid(), ContentKind.Class, "Test Tree Class",
            new GrantEffect { Id = "gift", Grant = GrantKind.Content, Content = feature, Level = 2 }) with { RevisionId = Guid.NewGuid() };
        var before = TempApp.Json(app.Store.ListRevisionsInOrder());

        var tree = app.Tree(new(Revision: cls));

        var level = tree.Root.Children.Single(c => c.Kind == TreeNodeKind.Level);
        Assert.Equal("Class level 2", level.Label);
        var granted = level.Children.Single();
        Assert.Equal((feature, "gift"), (granted.Content, granted.EffectId));
        // The calculator refuses a draft (content.unpublished), so the tree says so instead of showing it as what characters get.
        Assert.Equal(ContentTree.DraftPinNote, granted.Note);
        var resource = granted.Children.Single(c => c.Kind == TreeNodeKind.Resource);
        Assert.Equal("zap", resource.Children.Single().EffectId);
        Assert.Equal(before, TempApp.Json(app.Store.ListRevisionsInOrder()));

        // Review fix: "Grant a feature" before any feature is published leaves a grant that names nothing; the tree shows it.
        var unfinished = cls with { Effects = [new GrantEffect { Id = "grant-1", Grant = GrantKind.Content, Content = null, Level = 1 }] };
        Assert.Contains(app.Tree(new(Revision: unfinished)).Root.Children.Single().Children, n => n.EffectId == "grant-1" && n.Kind == TreeNodeKind.Missing);

        // A published feature is what characters get: no draft note.
        var published = app.Publish(app.SaveDraft(Draft(source, Guid.NewGuid(), ContentKind.Feature, "Test Tree Published",
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }))).Published;
        var live = cls with { Effects = [new GrantEffect { Id = "live", Grant = GrantKind.Content, Content = published, Level = 1 }] };
        Assert.Null(app.Tree(new(Revision: live)).Root.Children.Single().Children.Single().Note);

        Assert.Equal("tree.scope", Assert.Throws<AppValidationException>(() => app.Tree(new(SourceId: source.Id))).Problems.Single().Code);
        Assert.Equal("tree.scope", Assert.Throws<AppValidationException>(() => app.Tree(new())).Problems.Single().Code);
    }
}
