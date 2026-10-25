using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Import Lab (D62 L1): <c>content.coverage</c> counts what TomeStack really does with each effect of a source's content, from
/// the capability registry's effective automation. Read-only. All content is original.
/// </summary>
public class CoverageTests
{
    private static ContentRevision Draft(SourceRecord source, ContentKind kind, string name, string family, string? summary = null, Guid? contentId = null, params Effect[] effects) => new()
    {
        ContentId = contentId ?? Guid.NewGuid(),
        RevisionId = Guid.Empty,
        Kind = kind,
        Name = name,
        RulesFamilies = [family],
        Provenance = new(source.Id),
        Status = RevisionStatus.Draft,
        Summary = summary,
        Effects = effects,
    };

    private static ContentReference Publish(TomeStackApp app, ContentRevision draft) => app.Publish(app.SaveDraft(draft)).Published;

    private static ModifierEffect Modifier(string id, AutomationStatus automation = AutomationStatus.Automatic) =>
        new() { Id = id, Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1", Automation = automation };

    private static UnknownEffect Unknown(string id) =>
        (UnknownEffect)JsonSerializer.Deserialize<Effect>($$"""{"type":"fixture-unknown","id":"{{id}}"}""")!;

    private static GrantEffect Grant(string id, ContentReference feature, int level) =>
        new() { Id = id, Grant = GrantKind.Content, Content = feature, Level = level };

    /// <summary>
    /// A class with four level-gated features and 10 effects: 7 automatic, 2 assisted (a rollMode with a condition, and a modifier
    /// the author declared assisted), 1 of a type this build does not know.
    /// </summary>
    private static (SourceRecord Source, ContentReference Class, ContentReference[] Features) BuildClass(TomeStackApp app, string title = "Fixture Coverage Source")
    {
        var source = app.CreateHomebrewSource(new(title, [RulesFamilies.Srd521]));
        ContentReference Feature(string name, params Effect[] effects) => Publish(app, Draft(source, ContentKind.Feature, name, RulesFamilies.Srd521, effects: effects));
        var pool = Feature(
            "Fixture Pool",
            new ResourceEffect { Id = "res", ResourceId = "pool", Label = "Fixture Pool", Maximum = "2" },
            new RollEffect { Id = "roll", RollId = "roll", Label = "Fixture Roll", Dice = "1d6", ResourceId = "pool" },
            Modifier("m1"));
        var surge = Feature(
            "Fixture Surge",
            Modifier("m2"),
            new RollModeEffect { Id = "rm", Target = "save.wis", Mode = RollMode.Advantage, Against = ["frightened"] });
        var ward = Feature("Fixture Ward", Modifier("m3"), Modifier("m4", AutomationStatus.Assisted));
        var mystery = Feature("Fixture Mystery", Modifier("m5"), Modifier("m6"), Unknown("u"));
        var features = new[] { pool, surge, ward, mystery };
        var cls = Publish(app, Draft(
            source, ContentKind.Class, "Placeholder Class", RulesFamilies.Srd521,
            effects: [Grant("g5", mystery, 5), Grant("g1", pool, 1), Grant("g3", ward, 3), Grant("g2", surge, 2)]));
        return (source, cls, features);
    }

    private static string Serialize(CoverageReport report) => JsonSerializer.Serialize(report, RulesJson.Compact);

    [Fact]
    public void Synthetic_class_reports_exact_counts()
    {
        using var temp = new TempApp();
        var (source, cls, features) = BuildClass(temp.App);

        var report = temp.App.Coverage(new(source.Id));

        var expected = new CoverageCounts(Automatic: 7, Assisted: 2, Reference: 0, Unsupported: 1);
        Assert.Equal(expected, report.Totals);
        var reported = Assert.Single(report.Sources);
        Assert.Equal((source.Id, "Fixture Coverage Source", expected), (reported.SourceId, reported.Name, reported.Totals));
        Assert.Empty(reported.Other);
        var klass = Assert.Single(reported.Classes);
        Assert.Equal((cls.ContentId, "Placeholder Class", expected), (klass.ContentId, klass.Name, klass.Totals));
        Assert.Equal([1, 2, 3, 5], klass.Levels.Select(l => l.Level));
        Assert.Equal(
            [features[0].ContentId, features[1].ContentId, features[2].ContentId, features[3].ContentId],
            klass.Levels.Select(l => Assert.Single(l.Features).ContentId));
        var surge = klass.Levels[1].Features.Single();
        Assert.Equal(("Fixture Surge", new CoverageCounts(1, 1, 0, 0), RevisionStatus.Published), (surge.Name, surge.Totals, surge.Status));
        var rollMode = Assert.Single(surge.Effects, e => e.Type == "rollMode");
        Assert.Equal((AutomationStatus.Automatic, AutomationStatus.Assisted, AutomationStatus.Assisted, false), (rollMode.Author, rollMode.Ceiling, rollMode.Effective, rollMode.Unsupported));
        Assert.False(string.IsNullOrWhiteSpace(rollMode.Reason));
        Assert.Equal("RollModeTests", rollMode.Evidence);
        var unknown = Assert.Single(klass.Levels[3].Features.Single().Effects, e => e.Type == "fixture-unknown");
        Assert.True(unknown.Unsupported);
        Assert.Null(unknown.Effective);
        Assert.Null(unknown.Ceiling);
        Assert.True(klass.Levels[3].Features.Single().MissingMechanic);
    }

    [Fact]
    public void Counts_are_stable_across_reopen_and_export()
    {
        using var author = new TempApp();
        var (source, _, _) = BuildClass(author.App);
        author.App.SetShareable(new(source.Id, Shareable: true, ConfirmOwnWork: true));
        var first = Serialize(author.App.Coverage(new(source.Id)));

        author.Reopen();
        var reopened = Serialize(author.App.Coverage(new(source.Id)));

        var pack = author.App.ExportSourcePack([source.Id]).Content;
        using var friend = new TempApp();
        friend.App.ApplyImport(pack);
        var imported = Serialize(friend.App.Coverage(new(source.Id)));

        Assert.Equal(first, reopened);
        Assert.Equal(first, imported);
        Assert.Contains("\"automatic\":7", first);
    }

    [Fact]
    public void Published_only_by_default()
    {
        using var temp = new TempApp();
        var (source, _, features) = BuildClass(temp.App);
        var pool = features[0];
        var newer = Draft(
            source, ContentKind.Feature, "Fixture Pool", RulesFamilies.Srd521, contentId: pool.ContentId,
            effects: [Modifier("m1"), Modifier("extra"), Modifier("extra2"), Modifier("extra3")]);
        temp.App.SaveDraft(newer);

        var published = temp.App.Coverage(new(source.Id));
        var withDrafts = temp.App.Coverage(new(source.Id, IncludeDrafts: true));

        Assert.Equal(new CoverageCounts(7, 2, 0, 1), published.Totals);
        Assert.All(published.Sources.Single().Classes.Single().Levels.SelectMany(l => l.Features), f => Assert.Equal(RevisionStatus.Published, f.Status));
        Assert.Equal(new CoverageCounts(8, 2, 0, 1), withDrafts.Totals); // Fixture Pool: the draft's 4 automatic effects replace the published 3
        var shown = withDrafts.Sources.Single().Classes.Single().Levels.SelectMany(l => l.Features).Single(f => f.ContentId == pool.ContentId);
        Assert.Equal((RevisionStatus.Draft, new CoverageCounts(4, 0, 0, 0)), (shown.Status, shown.Totals));
    }

    [Fact]
    public void A_content_that_was_never_published_shows_only_with_drafts()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Draft Source", [RulesFamilies.Srd521]));
        temp.App.SaveDraft(Draft(source, ContentKind.Feature, "Fixture Idea", RulesFamilies.Srd521, effects: [Modifier("m")]));

        Assert.Equal(new CoverageCounts(0, 0, 0, 0), temp.App.Coverage(new(source.Id)).Totals);
        var withDrafts = temp.App.Coverage(new(source.Id, IncludeDrafts: true));
        var feature = Assert.Single(Assert.Single(withDrafts.Sources).Other);
        Assert.Equal((RevisionStatus.Draft, new CoverageCounts(1, 0, 0, 0)), (feature.Status, feature.Totals));
    }

    private static string Snapshot(TomeStackApp app) =>
        TempApp.Json(new
        {
            revisions = app.Store.ListRevisionsInOrder(),
            characters = app.Store.ListCharacters(),
            sources = app.Store.ListSources(),
            campaigns = app.Store.ListCampaigns(),
            gapNotes = app.Store.ListAllGapNotes(),
        });

    [Fact]
    public void Reads_without_writing()
    {
        using var temp = new TempApp();
        var (source, cls, _) = BuildClass(temp.App);
        temp.App.SaveDraft(Draft(source, ContentKind.Feature, "Fixture Idea", RulesFamilies.Srd521, effects: [Modifier("m")]));
        var before = Snapshot(temp.App);

        temp.App.Coverage(new());
        temp.App.Coverage(new(source.Id, IncludeDrafts: true));
        temp.App.Coverage(new(ContentId: cls.ContentId));

        Assert.Equal(before, Snapshot(temp.App));
    }

    [Fact]
    public void Families_stay_separate()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Both Families", [RulesFamilies.Srd51, RulesFamilies.Srd521]));
        var one = Publish(temp.App, Draft(source, ContentKind.Feature, "Fixture Same Name", RulesFamilies.Srd51, effects: [Modifier("m")]));
        var two = Publish(temp.App, Draft(source, ContentKind.Feature, "Fixture Same Name", RulesFamilies.Srd521, effects: [Modifier("m"), Modifier("n")]));

        var other = Assert.Single(temp.App.Coverage(new(source.Id)).Sources).Other;

        Assert.Equal(2, other.Count);
        Assert.Equal(new[] { one.ContentId, two.ContentId }.Order(), other.Select(f => f.ContentId).Order());
        Assert.Equal(
            new[] { 1, 2 },
            other.OrderBy(f => f.Totals.Automatic).Select(f => f.Totals.Automatic));
    }

    [Fact]
    public void Feature_without_effects_matching_a_mechanic_pattern_is_warned()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Fixture Text Source", [RulesFamilies.Srd521]));
        Publish(temp.App, Draft(source, ContentKind.Feature, "Fixture Surge Text", RulesFamilies.Srd521, summary: "You can use this once per long rest."));
        Publish(temp.App, Draft(source, ContentKind.Feature, "Fixture Plain Text", RulesFamilies.Srd521, summary: "A story about a lantern."));

        var other = temp.App.Coverage(new(source.Id)).Sources.Single().Other;

        Assert.Equal(
            [("Fixture Plain Text", false), ("Fixture Surge Text", true)],
            other.Select(f => (f.Name, f.MissingMechanic)));
        Assert.All(other, f => Assert.Equal(new CoverageCounts(0, 0, 1, 0), f.Totals)); // a text-only feature is one reference mechanic
    }

    [Fact]
    public void Patterns_are_a_fixed_generic_list()
    {
        Assert.Equal(["once per", "regain", "expend", "as a reaction", "bonus action"], Coverage.MechanicPatterns);
    }

    [Fact]
    public void A_missing_source_is_refused_and_the_command_is_registered()
    {
        using var temp = new TempApp();

        var ex = Assert.Throws<AppValidationException>(() => temp.App.Coverage(new(Guid.NewGuid())));
        Assert.Equal("source.not-found", Assert.Single(ex.Problems).Code);
        Assert.Contains("content.coverage", CommandDispatcher.Commands);
    }

    [Fact]
    public void The_dispatcher_answers_content_coverage()
    {
        using var temp = new TempApp();
        var (source, _, _) = BuildClass(temp.App);

        var reply = new CommandDispatcher(temp.App).Dispatch($$$"""{"id":"1","command":"content.coverage","payload":{"sourceId":"{{{source.Id}}}"}}""");

        using var json = JsonDocument.Parse(reply);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        var totals = json.RootElement.GetProperty("result").GetProperty("totals");
        Assert.Equal((7, 2, 1), (totals.GetProperty("automatic").GetInt32(), totals.GetProperty("assisted").GetInt32(), totals.GetProperty("unsupported").GetInt32()));
    }
}
