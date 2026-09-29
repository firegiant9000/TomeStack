using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M5 slice 2 (B02): the homebrew debugger over <see cref="ContentGraph"/>. Each test builds original content in code
/// (prefix 5fd0) on the Test Chronicler's fixture source and checks one kind of finding. The debugger is read-only.
/// </summary>
public class ContentDebuggerTests
{
    private static readonly Guid Source = Guid.Parse("5fc05000-0000-4000-8000-000000000001");

    private static ContentReference Ref(int n, int revision = 0) =>
        new(Guid.Parse($"5fd0c000-0000-4000-8000-{n:D12}"), Guid.Parse($"5fd0e{revision:D3}-0000-4000-8000-{n:D12}"));

    private static ContentRevision Revision(ContentReference reference, ContentKind kind, string name, params Effect[] effects) => new()
    {
        ContentId = reference.ContentId,
        RevisionId = reference.RevisionId,
        Kind = kind,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
        Provenance = new(Source),
        Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ContentRevision Published(ContentRevision revision) => revision with { Status = RevisionStatus.Published };

    private static HitDieEffect D8 => new() { Id = "hit-die", Die = 8 };

    private static ScaleEffect Scale(string id, string scaleId) => new() { Id = id, ScaleId = scaleId, Label = scaleId, Values = [.. Enumerable.Repeat(1, 20)] };

    private static ModifierEffect Reads(string id, string formula) => new() { Id = id, Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = formula };

    private static GrantEffect Grants(string id, ContentReference content, int? level = null) => new() { Id = id, Grant = GrantKind.Content, Content = content, Level = level };

    /// <summary>The scope is studied against the Chronicler catalog plus <paramref name="stored"/> (stored in order).</summary>
    private static DebugReport Diagnose(IReadOnlyList<ContentRevision> scope, params ContentRevision[] stored)
    {
        var pack = Fixtures.ChroniclerPack();
        var revisions = new List<ContentRevision>([.. pack.Revisions, .. stored]);
        var catalog = new InMemoryContentCatalog(pack.Sources, [.. revisions, .. scope.Where(s => !revisions.Any(r => r.Reference == s.Reference))]);
        return ContentDebugger.Diagnose(scope, revisions, catalog);
    }

    private static string Codes(DebugReport report) => string.Join("; ", report.Findings.Select(f => $"{f.Code} {f.EffectId}: {f.Message}"));

    [Fact]
    public void The_Test_Chronicler_source_has_no_findings()
    {
        var pack = Fixtures.ChroniclerPack();
        var report = ContentDebugger.Diagnose(pack.Revisions, pack.Revisions, new InMemoryContentCatalog(pack));

        Assert.True(report.Findings.Count == 0, Codes(report));
        Assert.Equal(pack.Revisions.Select(r => r.Reference), report.Scope);
    }

    [Fact]
    public void A_resource_nothing_spends_or_recovers_and_a_recovery_or_roll_for_another_resource_are_found()
    {
        var feature = Revision(Ref(1), ContentKind.Feature, "Debug Fury",
            new ResourceEffect { Id = "fury", ResourceId = "fury", Label = "Fury", Maximum = "PB" },
            new RecoveryEffect { Id = "rest", ResourceId = "rage", On = RestPeriod.LongRest, Amount = "all" },
            new RollEffect { Id = "strike", RollId = "strike", Label = "Strike", Dice = "1d6", ResourceId = "focus" });

        var report = Diagnose([feature]);

        Assert.Contains(report.Findings, f => (f.Code, f.EffectId, f.Severity) == ("debug.resource-dead", "fury", FindingSeverity.Warning));
        Assert.Contains(report.Findings, f => (f.Code, f.EffectId) == ("debug.recovery-orphan", "rest"));
        Assert.Contains(report.Findings, f => (f.Code, f.EffectId) == ("debug.roll-resource-unknown", "strike"));
        // The validator's softer "fine if another revision does" is replaced: a recovery counts only in its own revision.
        Assert.DoesNotContain(report.Findings, f => f.Code == "validate.recovery-resource");
        Assert.All(report.Findings, f => Assert.Equal((feature.Reference, "Debug Fury"), (f.Content, f.ContentName)));
    }

    [Fact]
    public void A_resource_spent_by_another_contents_roll_or_by_its_own_toggle_is_not_dead()
    {
        var pool = Revision(Ref(1), ContentKind.Feature, "Debug Pool",
            new ResourceEffect { Id = "pool", ResourceId = "pool", Label = "Pool", Maximum = "2" },
            new ResourceEffect { Id = "stance-uses", ResourceId = "stance", Label = "Stance", Maximum = "1" },
            new ToggleEffect { Id = "stance", ToggleId = "stance", Label = "Stance", ResourceId = "stance" });
        var spender = Revision(Ref(2), ContentKind.Feature, "Debug Spender",
            new RollEffect { Id = "draw", RollId = "draw", Label = "Draw", Dice = "1d4", ResourceId = "pool", ResourceContent = pool.ContentId });

        var report = Diagnose([pool, spender]);

        Assert.DoesNotContain(report.Findings, f => f.Code is "debug.resource-dead" or "debug.roll-resource-unknown");
    }

    [Fact]
    public void A_grant_from_granted_content_never_applies_and_what_it_grants_is_unreachable()
    {
        var deepPublished = Published(Revision(Ref(3, 1), ContentKind.Feature, "Debug Deep", Reads("deep-init", "CLASS_LEVEL")));
        var deep = Revision(Ref(3, 2), ContentKind.Feature, "Debug Deep", Reads("deep-init", "CLASS_LEVEL"));
        var middle = Published(Revision(Ref(2), ContentKind.Feature, "Debug Middle", Grants("middle-grant", Ref(3, 1))));
        var cls = Revision(Ref(1), ContentKind.Class, "Debug Class", D8, Grants("class-grant", Ref(2), level: 2));

        var report = Diagnose([cls, middle, deep], deepPublished, middle);

        Assert.Contains(report.Findings, f => (f.Code, f.EffectId, f.Content) == ("debug.grant-nested", "middle-grant", middle.Reference));
        var unreachable = Assert.Single(report.Findings, f => f.Code == "debug.feature-unreachable");
        Assert.Equal(deep.Reference, unreachable.Content);
        Assert.Contains("no class reaches it", unreachable.Message, StringComparison.Ordinal);

        // Offered in a choice instead, the middle feature is chosen content (a root): its grant is followed.
        var chooser = cls with { Effects = [D8, new ChoiceEffect { Id = "pick", ChoiceId = "pick", Options = [Ref(2)] }] };
        var fixedReport = Diagnose([chooser, middle, deep], deepPublished, middle);
        Assert.DoesNotContain(fixedReport.Findings, f => f.Code is "debug.grant-nested" or "debug.feature-unreachable");
    }

    [Fact]
    public void The_graph_follows_grants_one_level_deep_and_choices_from_anything_reached()
    {
        // Class -grant-> A (granted) -option-> B (chosen, a root) -grant-> C (granted) -grant-> D (never followed).
        var d = Published(Revision(Ref(4), ContentKind.Feature, "D"));
        var c = Published(Revision(Ref(3), ContentKind.Feature, "C", Grants("c-grant", Ref(4))));
        var b = Published(Revision(Ref(2), ContentKind.Subclass, "B", Grants("b-grant", Ref(3))));
        var a = Published(Revision(Ref(5), ContentKind.Feature, "A", new ChoiceEffect { Id = "a-pick", ChoiceId = "a-pick", Options = [Ref(2)] }));
        var cls = Published(Revision(Ref(1), ContentKind.Class, "Class", D8, Grants("grant-a", Ref(5))));

        var graph = ContentGraph.Build([d, c, b, a, cls]);

        Assert.Equal(new GraphReach(a.ContentId, cls.ContentId, null, AsRoot: false), Assert.Single(graph.Reaches(a.ContentId)));
        Assert.Equal(new GraphReach(b.ContentId, cls.ContentId, b.ContentId, AsRoot: true), Assert.Single(graph.Reaches(b.ContentId)));
        Assert.Equal(new GraphReach(c.ContentId, cls.ContentId, b.ContentId, AsRoot: false), Assert.Single(graph.Reaches(c.ContentId)));
        Assert.Empty(graph.Reaches(d.ContentId));
        Assert.Contains(graph.To(d.ContentId), e => e.Kind == GraphEdgeKind.Grant && e.EffectId == "c-grant");
    }

    [Fact]
    public void A_subclass_no_choice_offers_and_a_class_feature_nothing_grants_are_unreachable()
    {
        var subclass = Revision(Ref(1), ContentKind.Subclass, "Debug Lost Path");
        var feature = Revision(Ref(2), ContentKind.Feature, "Debug Lost Feature", Reads("init", "floor(CLASS_LEVEL / 4)"));
        var plain = Revision(Ref(3), ContentKind.Feature, "Debug Standalone Trinket", Reads("init", "1"));

        var report = Diagnose([subclass, feature, plain]);

        Assert.Contains(report.Findings, f => f.Code == "debug.subclass-unreachable" && f.Content == subclass.Reference);
        Assert.Contains(report.Findings, f => f.Code == "debug.feature-unreachable" && f.Content == feature.Reference && f.Message.Contains("Nothing grants", StringComparison.Ordinal));
        // Content that needs no class is standalone: a player can pin it directly.
        Assert.DoesNotContain(report.Findings, f => f.Content == plain.Reference);
    }

    [Fact]
    public void A_choice_with_no_options_is_empty_until_something_extends_it()
    {
        var cls = Revision(Ref(1), ContentKind.Class, "Debug Class", D8, new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 });

        var report = Diagnose([cls]);
        Assert.Contains(report.Findings, f => (f.Code, f.EffectId) == ("debug.choice-empty", "path"));

        var subclass = Revision(Ref(2), ContentKind.Subclass, "Debug Path") with { ExtendsChoice = new(cls.ContentId, "path") };
        var withSubclass = Diagnose([cls, subclass]);
        Assert.DoesNotContain(withSubclass.Findings, f => f.Code is "debug.choice-empty" or "debug.subclass-unreachable");
    }

    [Fact]
    public void Scales_read_but_undefined_defined_but_unread_and_defined_by_a_class_and_its_subclass_are_found_in_drafts()
    {
        // Drafts only, studied together, so each draft is checked against the others as they stand.
        var feature = Revision(Ref(2), ContentKind.Feature, "Debug Column Reader", Reads("reads-echo", "SCALE.echo"));
        var cls = Revision(Ref(1), ContentKind.Class, "Debug Class", D8, Scale("ink-column", "ink"), Scale("spare-column", "spare"),
            Reads("reads-ink", "SCALE.ink"), Grants("grant-reader", Ref(2), level: 1), new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 });
        var subclass = Revision(Ref(3), ContentKind.Subclass, "Debug Path", Scale("sub-ink", "ink"), Scale("echo-column", "echo"), Reads("reads-both", "SCALE.echo + SCALE.ink"))
            with { ExtendsChoice = new(cls.ContentId, "path") };

        var report = Diagnose([cls, feature, subclass]);

        // The class's own feature reads the subclass's column: it is undefined for characters of the class alone.
        var undefined = Assert.Single(report.Findings, f => f.Code == "debug.scale-undefined");
        Assert.Equal((feature.Reference, "reads-echo"), (undefined.Content, undefined.EffectId));
        Assert.Contains(report.Findings, f => (f.Code, f.EffectId, f.Severity) == ("debug.scale-unused", "spare-column", FindingSeverity.Note));
        Assert.DoesNotContain(report.Findings, f => f.Code == "debug.scale-unused" && f.EffectId is "ink-column" or "echo-column");
        // Validation of the batch already names the clash; it is listed once, as an error.
        var clash = Assert.Single(report.Findings, f => f.EffectId == "sub-ink" && f.Code is "validate.scale-duplicate" or "debug.scale-collision");
        Assert.Equal((FindingSeverity.Error, subclass.Reference), (clash.Severity, clash.Content));
    }

    [Fact]
    public void A_scale_clash_with_an_older_published_class_revision_is_a_warning()
    {
        var older = Published(Revision(Ref(1, 1), ContentKind.Class, "Debug Class", D8, Scale("ink-column", "ink"), Reads("reads-ink", "SCALE.ink"), new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 }));
        var newer = Published(Revision(Ref(1, 2), ContentKind.Class, "Debug Class", D8, new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 }));
        var subclass = Revision(Ref(3), ContentKind.Subclass, "Debug Path", Scale("sub-ink", "ink"), Reads("reads-ink", "SCALE.ink"))
            with { ExtendsChoice = new(older.ContentId, "path") };

        var report = Diagnose([subclass], older, newer);

        Assert.Contains(report.Findings, f => (f.Code, f.EffectId, f.Severity) == ("debug.scale-collision-older", "sub-ink", FindingSeverity.Warning));
        Assert.DoesNotContain(report.Findings, f => f.Code is "debug.scale-collision" or "validate.scale-duplicate-older");
    }

    [Fact]
    public void A_grant_of_an_older_revision_than_the_newest_published_is_a_note()
    {
        var first = Published(Revision(Ref(2, 1), ContentKind.Feature, "Debug Gift", Reads("init", "1")));
        var second = Published(Revision(Ref(2, 2), ContentKind.Feature, "Debug Gift", Reads("init", "2")));
        var cls = Revision(Ref(1), ContentKind.Class, "Debug Class", D8, Grants("gift", first.Reference, level: 1));

        var report = Diagnose([cls], first, second);

        Assert.Contains(report.Findings, f => (f.Code, f.EffectId, f.Severity) == ("debug.reference-stale", "gift", FindingSeverity.Note));
        Assert.DoesNotContain(Diagnose([cls with { Effects = [D8, Grants("gift", second.Reference, level: 1)] }], first, second).Findings, f => f.Code == "debug.reference-stale");
    }

    [Fact]
    public void A_level_gated_grant_outside_a_class_counts_character_levels_so_it_is_not_class_only()
    {
        // Review fix: outside a class the calculator gates by character level, so a feat's level-3 grant works.
        var gift = Published(Revision(Ref(2), ContentKind.Feature, "Debug Gift", Reads("init", "1")));
        var feat = Revision(Ref(1), ContentKind.Feat, "Debug Late Bloomer", Grants("later", Ref(2), level: 3));

        Assert.DoesNotContain(Diagnose([feat], gift).Findings, f => f.Code == "debug.feature-unreachable");
    }

    [Fact]
    public void Only_grants_the_calculator_follows_reach_and_a_granted_subclass_is_reachable()
    {
        // A reference-only grant never admits its content: the class feature that reads CLASS_LEVEL stays unreachable.
        var feature = Published(Revision(Ref(2), ContentKind.Feature, "Debug Class Feature", Reads("init", "CLASS_LEVEL")));
        var cls = Published(Revision(Ref(1), ContentKind.Class, "Debug Class", D8, Grants("grant", Ref(2), level: 1) with { Automation = AutomationStatus.Reference }));
        Assert.Contains(Diagnose([feature], cls).Findings, f => f.Code == "debug.feature-unreachable");
        Assert.Empty(ContentGraph.Build([feature, cls]).To(feature.ContentId));

        // A class that grants its subclass outright (no choice) still gives it to every character of the class.
        var subclass = Revision(Ref(3), ContentKind.Subclass, "Debug Fixed Path");
        var fixedClass = Published(Revision(Ref(4), ContentKind.Class, "Debug Fixed Class", D8, Grants("path", Ref(3), level: 1)));
        Assert.DoesNotContain(Diagnose([subclass], fixedClass).Findings, f => f.Code == "debug.subclass-unreachable");
    }

    [Fact]
    public void Extending_a_choice_the_class_no_longer_offers_is_found_and_does_not_reach()
    {
        var older = Published(Revision(Ref(1, 1), ContentKind.Class, "Debug Class", D8, new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3 }));
        var newer = Published(Revision(Ref(1, 2), ContentKind.Class, "Debug Class", D8));
        var subclass = Revision(Ref(3), ContentKind.Subclass, "Debug Path") with { ExtendsChoice = new(older.ContentId, "path") };

        var report = Diagnose([subclass], older, newer);

        Assert.Contains(report.Findings, f => f.Code == "debug.extension-choice-missing" && f.Content == subclass.Reference);
        Assert.Contains(report.Findings, f => f.Code == "debug.subclass-unreachable");
        Assert.DoesNotContain(report.Findings, f => f.Code == "validate.extends-choice-unknown"); // an older revision had it
    }

    [Fact]
    public void A_reference_only_scale_defines_nothing_and_a_scale_clash_through_a_declared_option_is_a_warning()
    {
        // The subclass is a declared option (no extendsChoice), so validation cannot see its class: only the debugger does,
        // and it never blocks publishing, so it is a warning.
        var subclass = Revision(Ref(2), ContentKind.Subclass, "Debug Option Path", Scale("sub-ink", "ink"), Reads("reads-ink", "SCALE.ink"));
        var cls = Revision(Ref(1), ContentKind.Class, "Debug Class", D8, Scale("ink-column", "ink"), Scale("dim-column", "dim") with { Automation = AutomationStatus.Reference },
            Reads("reads-dim", "SCALE.dim"), new ChoiceEffect { Id = "path", ChoiceId = "path", Level = 3, Options = [subclass.Reference] });

        var report = Diagnose([cls, subclass]);

        Assert.Contains(report.Findings, f => (f.Code, f.EffectId, f.Severity) == ("debug.scale-collision", "sub-ink", FindingSeverity.Warning));
        Assert.Contains(report.Findings, f => (f.Code, f.EffectId) == ("debug.scale-undefined", "reads-dim"));
        // Only validation errors are errors (here the class's own check of its declared option); they block publishing.
        Assert.All(report.Findings.Where(f => f.Severity == FindingSeverity.Error), f => Assert.StartsWith("validate.", f.Code, StringComparison.Ordinal));
    }

    [Fact]
    public void The_reach_walk_is_bounded_by_states_and_by_work_and_walks_the_scopes_classes_first()
    {
        var feature = Published(Revision(Ref(9), ContentKind.Feature, "Debug Shared", Reads("init", "CLASS_LEVEL")));
        ContentRevision Class(int n) => Published(Revision(Ref(n), ContentKind.Class, $"Debug Class {n}", D8, Grants("grant", Ref(9), level: 1)));
        var stored = new[] { feature, Class(1), Class(2), Class(3) };
        var draft = Revision(Ref(4), ContentKind.Class, "Debug Draft Class", D8, Grants("grant", Ref(9), level: 1));

        var byStates = ContentGraph.Build(stored, [draft], maxStates: 2);
        Assert.True(byStates.Truncated);
        Assert.Equal(draft.ContentId, Assert.Single(byStates.Reaches(feature.ContentId)).ClassId); // the author's class came first

        var byWork = ContentGraph.Build(stored, [draft], maxEdgeSteps: 1);
        Assert.True(byWork.Truncated);
        Assert.False(ContentGraph.Build(stored, [draft]).Truncated);

        // Truncated, the reach findings are left out rather than reported falsely for what the walk never got to.
        var lone = Revision(Ref(5), ContentKind.Feature, "Debug Lone", Reads("init", "CLASS_LEVEL"));
        var catalog = new InMemoryContentCatalog(Fixtures.ChroniclerPack().Sources, [.. stored, draft, lone]);
        var report = ContentDebugger.Diagnose([lone], ContentGraph.Build(stored, [draft, lone], maxStates: 1), catalog);
        Assert.True(report.Truncated);
        Assert.DoesNotContain(report.Findings, f => f.Code == "debug.feature-unreachable");
        Assert.Contains(ContentDebugger.Diagnose([lone], stored, catalog).Findings, f => f.Code == "debug.feature-unreachable");
    }

    [Fact]
    public void Context_revisions_shape_the_graph_but_are_not_reported()
    {
        var feature = Revision(Ref(2), ContentKind.Feature, "Debug Draft Feature", Reads("init", "CLASS_LEVEL"),
            new ResourceEffect { Id = "idle", ResourceId = "idle", Label = "Idle", Maximum = "1" });
        var cls = Revision(Ref(1), ContentKind.Class, "Debug Draft Class", D8, Grants("grant", Ref(2), level: 1), Scale("unread", "unread"));

        var alone = Diagnose([feature]);
        Assert.Contains(alone.Findings, f => f.Code == "debug.feature-unreachable");

        var pack = Fixtures.ChroniclerPack();
        var withContext = ContentDebugger.Diagnose([feature], pack.Revisions, new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, feature, cls]), context: [cls]);
        Assert.DoesNotContain(withContext.Findings, f => f.Code == "debug.feature-unreachable");
        Assert.All(withContext.Findings, f => Assert.Equal(feature.Reference, f.Content)); // the class's unread scale is not reported
        Assert.Equal([feature.Reference], withContext.Scope);
    }

    [Fact]
    public void Validation_problems_are_findings_too_and_the_draft_under_study_replaces_its_published_revision()
    {
        var broken = Published(Revision(Ref(1, 1), ContentKind.Feature, "Debug Broken", Reads("bad", "PB +"), Grants("far", Ref(9), level: 21)));
        var fixedDraft = Revision(Ref(1, 2), ContentKind.Feature, "Debug Broken", Reads("bad", "PB + 1"));

        var report = Diagnose([broken], broken);
        Assert.Contains(report.Findings, f => (f.Code, f.Severity, f.EffectId) == ("validate.formula-invalid", FindingSeverity.Error, "bad"));
        Assert.Contains(report.Findings, f => (f.Code, f.EffectId) == ("validate.level", "far"));
        Assert.Contains(report.Findings, f => (f.Code, f.EffectId) == ("validate.reference-missing", "far"));

        Assert.True(Diagnose([fixedDraft], broken).Findings.Count == 0, Codes(Diagnose([fixedDraft], broken)));
    }
}
