using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>M5 slice 5 (B19): the relationship tree over <see cref="ContentGraph"/>. The Test Chronicler, and original edge cases (prefix 5fd3).</summary>
public class ContentTreeTests
{
    private static readonly Guid Source = Guid.Parse("5fc05000-0000-4000-8000-000000000001");

    private static ContentReference Ref(int n) => new(Guid.Parse($"5fd3c000-0000-4000-8000-{n:D12}"), Guid.Parse($"5fd3e000-0000-4000-8000-{n:D12}"));

    private static ContentRevision Revision(int n, ContentKind kind, string name, params Effect[] effects) => new()
    {
        ContentId = Ref(n).ContentId,
        RevisionId = Ref(n).RevisionId,
        Kind = kind,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(Source),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static GrantEffect Grants(string id, int n, int? level = null) => new() { Id = id, Grant = GrantKind.Content, Content = Ref(n), Level = level };

    private static IEnumerable<TreeNode> All(TreeNode node) => node.Children.SelectMany(All).Prepend(node);

    [Fact]
    public void The_Test_Chronicler_shows_its_levels_features_resources_rolls_recoveries_and_columns()
    {
        var pack = Fixtures.ChroniclerPack();
        var tree = ContentTree.Build(ContentGraph.Build(pack.Revisions), Fixtures.Chronicler.ContentId);

        Assert.False(tree.Truncated);
        var root = tree.Root;
        Assert.Equal((TreeNodeKind.Content, Fixtures.Chronicler), (root.Kind, root.Content));
        var levels = root.Children.Where(c => c.Kind == TreeNodeKind.Level).Select(c => c.Label).ToList();
        Assert.Equal(["Class level 2", "Class level 3"], levels);
        var level2 = root.Children.Single(c => c.Label == "Class level 2");
        Assert.Contains(level2.Children, c => c.Content == Fixtures.ChroniclerMarginalia && c.Label.StartsWith("Grants Test Chronicler: Marginalia", StringComparison.Ordinal));
        var choice = root.Children.Single(c => c.Label == "Class level 3").Children.Single();
        Assert.Equal(TreeNodeKind.Choice, choice.Kind);
        var archive = Assert.Single(choice.Children);
        Assert.Equal(Fixtures.ArchiveOfEchoes, archive.Content);
        Assert.Contains(archive.Children, c => c.Kind == TreeNodeKind.Scale && c.Label.Contains("SCALE.echo", StringComparison.Ordinal));

        var ink = root.Children.Single(c => c.Kind == TreeNodeKind.Resource);
        Assert.Equal([TreeNodeKind.Roll, TreeNodeKind.Recovery, TreeNodeKind.Recovery], ink.Children.Select(c => c.Kind));
        Assert.Equal(2, root.Children.Count(c => c.Kind == TreeNodeKind.Scale));
        Assert.Equal(All(root).Count(), All(root).Select(n => n.Id).Distinct().Count()); // ids are unique
    }

    [Fact]
    public void Grants_of_granted_content_are_shown_as_not_followed_and_cycles_end()
    {
        var deep = Revision(3, ContentKind.Feature, "Tree Deep");
        var middle = Revision(2, ContentKind.Feature, "Tree Middle", Grants("deeper", 3), new ChoiceEffect { Id = "back", ChoiceId = "back", Options = [Ref(1)] });
        var cls = Revision(1, ContentKind.Class, "Tree Class", Grants("middle", 2, level: 1), new ChoiceEffect { Id = "loop", ChoiceId = "loop", Options = [Ref(1)] });

        var tree = ContentTree.Build(ContentGraph.Build([deep, middle, cls]), cls.ContentId);

        var granted = All(tree.Root).Single(n => n.EffectId == "middle");
        var nested = granted.Children.Single(c => c.Kind == TreeNodeKind.Level).Children.Single(c => c.EffectId == "deeper");
        Assert.Empty(nested.Children);
        Assert.Contains("Not followed", nested.Note, StringComparison.Ordinal);
        Assert.All(All(tree.Root).Where(n => n.Content == cls.Reference && n.Id != "0" && n.Kind == TreeNodeKind.Content), n => Assert.Equal("Already shown above", n.Note));
    }

    [Fact]
    public void Grants_that_name_nothing_or_never_apply_loose_toggles_and_repeated_resources_are_shown_as_the_calculator_reads_them()
    {
        var gift = Revision(2, ContentKind.Feature, "Tree Gift");
        var cls = Revision(1, ContentKind.Class, "Tree Class",
            new GrantEffect { Id = "empty", Grant = GrantKind.Content, Content = null },
            Grants("reference", 2, level: 1) with { Automation = AutomationStatus.Reference },
            new ToggleEffect { Id = "stance", ToggleId = "stance", Label = "Stance" },
            new ResourceEffect { Id = "first", ResourceId = "ink", Label = "Ink", Maximum = "1" },
            new ResourceEffect { Id = "again", ResourceId = "ink", Label = "Ink again", Maximum = "2" },
            new RecoveryEffect { Id = "stray", ResourceId = "nothing", On = RestPeriod.LongRest, Amount = "all" });

        var all = All(ContentTree.Build(ContentGraph.Build([gift, cls]), cls.ContentId).Root).ToList();

        Assert.Contains(all, n => n.EffectId == "empty" && n.Kind == TreeNodeKind.Missing && n.Note!.Contains("names no content", StringComparison.Ordinal));
        Assert.Contains(all, n => n.EffectId == "reference" && n.Children.Count == 0 && n.Note!.StartsWith("Never applied", StringComparison.Ordinal));
        Assert.Contains(all, n => n.EffectId == "stance" && n.Kind == TreeNodeKind.Toggle);
        Assert.Single(all, n => n.Kind == TreeNodeKind.Resource && n.Note is null);
        Assert.Contains(all, n => n.EffectId == "again" && n.Note!.StartsWith("Ignored", StringComparison.Ordinal));
        Assert.Contains(all, n => n.EffectId == "stray" && n.Note!.StartsWith("Never applies", StringComparison.Ordinal));
    }

    [Fact]
    public void A_grant_shows_the_revision_it_pins_owned_by_the_granting_content_and_ids_are_positional()
    {
        var older = Revision(2, ContentKind.Feature, "Tree Gift", new ResourceEffect { Id = "old", ResourceId = "old", Label = "Old", Maximum = "1" });
        var newer = older with { RevisionId = Guid.NewGuid(), Effects = [new ResourceEffect { Id = "new", ResourceId = "new", Label = "New", Maximum = "1" }] };
        // Two grants with the same effect id (a draft can have that) still get distinct node ids.
        var cls = Revision(1, ContentKind.Class, "Tree Class", Grants("dup", 2, level: 1), Grants("dup", 2, level: 1),
            new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 });
        var extension = Revision(3, ContentKind.Subclass, "Tree Path") with { ExtendsChoice = new(cls.ContentId, "path") };

        var tree = ContentTree.Build(ContentGraph.Build([older, newer, cls, extension]), cls.ContentId);

        var grants = All(tree.Root).Where(n => n.EffectId == "dup").ToList();
        Assert.Equal(2, grants.Select(g => g.Id).Distinct().Count());
        var grant = grants[0];
        Assert.Equal((older.Reference, cls.Reference), (grant.Content, grant.Owner));
        Assert.Contains("newer revision", grant.Note, StringComparison.Ordinal);
        Assert.Contains(grant.Children, c => c.EffectId == "old");
        Assert.DoesNotContain(All(grant), c => c.EffectId == "new");
        Assert.Contains(All(tree.Root), n => n.Content == extension.Reference && n.Kind == TreeNodeKind.Content);
        Assert.All(All(tree.Root), n => Assert.Matches("^0(\\.[0-9]+)*$", n.Id));
    }

    [Fact]
    public void A_deep_chain_of_choices_is_cut_at_the_depth_bound()
    {
        var chain = Enumerable.Range(1, ContentTree.MaxDepth + 5)
            .Select(n => Revision(n, n == 1 ? ContentKind.Class : ContentKind.Feature, $"Tree Link {n}", new ChoiceEffect { Id = "next", ChoiceId = "next", Options = [Ref(n + 1)] }))
            .ToList();

        var tree = ContentTree.Build(ContentGraph.Build(chain), chain[0].ContentId);

        Assert.True(tree.Truncated);
    }

    [Fact]
    public void A_large_graph_is_cut_at_the_node_bound()
    {
        var options = Enumerable.Range(10, ContentTree.MaxNodes + 10).Select(n => Revision(n, ContentKind.Feature, $"Tree Option {n}")).ToList();
        var cls = Revision(1, ContentKind.Class, "Tree Wide Class", new ChoiceEffect { Id = "wide", ChoiceId = "wide", Options = [.. options.Select(o => o.Reference)] });

        var tree = ContentTree.Build(ContentGraph.Build([.. options, cls]), cls.ContentId);

        Assert.True(tree.Truncated);
        // At most the bound, plus the ancestors that were still open when it was reached.
        Assert.InRange(All(tree.Root).Count(), ContentTree.MaxNodes / 2, ContentTree.MaxNodes + ContentTree.MaxDepth);
    }
}
