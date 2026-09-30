using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M5 slice 2 (B02): <c>content.diagnose</c>, the homebrew debugger, over a source (the latest revision of each content,
/// drafts too), one stored revision, or one unsaved revision. It writes nothing. All content is original.
/// </summary>
public class ContentDiagnoseTests
{
    private static ContentRevision Draft(SourceRecord source, Guid contentId, string name, params Effect[] effects) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Empty,
        Kind = ContentKind.Feature,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(source.Id),
        Status = RevisionStatus.Draft,
        Effects = effects,
    };

    private static ResourceEffect Sparks => new() { Id = "sparks", ResourceId = "sparks", Label = "Sparks", Maximum = "PB" };

    private static RecoveryEffect SparksBack => new() { Id = "sparks-back", ResourceId = "sparks", On = RestPeriod.LongRest, Amount = "all", Timing = EffectTiming.OnLongRest };

    [Fact]
    public void A_source_is_studied_at_the_latest_revision_of_each_content_drafts_included()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Debugger Source", [RulesFamilies.Srd521]));
        var contentId = Guid.NewGuid();
        var published = app.Publish(app.SaveDraft(Draft(source, contentId, "Test Spark Well", Sparks))).Published;

        var before = app.Diagnose(new(SourceId: source.Id));
        var dead = Assert.Single(before.Findings);
        Assert.Equal(("debug.resource-dead", "sparks", published), (dead.Code, dead.EffectId, dead.Content));

        // A newer draft with a recovery is what the author is working on: the finding goes away.
        var draft = app.SaveDraft(Draft(source, contentId, "Test Spark Well", Sparks, SparksBack));
        var after = app.Diagnose(new(SourceId: source.Id));
        Assert.Empty(after.Findings);
        Assert.Equal([draft], after.Scope);

        // One stored revision, or one unsaved one, can be studied alone.
        Assert.Single(app.Diagnose(new(Reference: published)).Findings);
        Assert.Empty(app.Diagnose(new(Revision: Draft(source, contentId, "Test Spark Well", Sparks, SparksBack) with { RevisionId = Guid.NewGuid() })).Findings);
    }

    [Fact]
    public void One_revision_is_studied_among_its_sources_drafts_so_both_buttons_agree()
    {
        // Review fix: a draft class that grants a draft feature reading CLASS_LEVEL. Studied alone, among published content
        // only, the feature would look unreachable.
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Debugger Source", [RulesFamilies.Srd521]));
        var featureId = Guid.NewGuid();
        var feature = app.SaveDraft(Draft(source, featureId, "Test Class Feature",
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "CLASS_LEVEL" }));
        app.SaveDraft(Draft(source, Guid.NewGuid(), "Test Draft Class",
            new HitDieEffect { Id = "hit-die", Die = 8 },
            new GrantEffect { Id = "grant", Grant = GrantKind.Content, Content = feature, Level = 1 }) with { Kind = ContentKind.Class });

        var single = app.Diagnose(new(Reference: feature));
        var whole = app.Diagnose(new(SourceId: source.Id));

        Assert.DoesNotContain(single.Findings, f => f.Code == "debug.feature-unreachable");
        Assert.Equal([feature], single.Scope);
        Assert.Equal(
            whole.Findings.Where(f => f.Content == feature).Select(f => f.Code),
            single.Findings.Select(f => f.Code));
    }

    [Fact]
    public void Diagnose_writes_nothing()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Debugger Source", [RulesFamilies.Srd521]));
        app.SaveDraft(Draft(source, Guid.NewGuid(), "Test Spark Well", Sparks));
        string Snapshot() => TempApp.Json(new { revisions = app.Store.ListRevisionsInOrder(), sources = app.Store.ListSources(), characters = app.Store.ListCharacters() });
        var before = Snapshot();

        app.Diagnose(new(SourceId: source.Id));
        app.Diagnose(new(Revision: Draft(source, Guid.NewGuid(), "Test Unsaved", Sparks) with { RevisionId = Guid.NewGuid() }));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Diagnose_needs_exactly_one_scope_and_refuses_malformed_or_unknown_input()
    {
        using var temp = new TempApp();
        var app = temp.App;
        var source = app.CreateHomebrewSource(new("Test Debugger Source", [RulesFamilies.Srd521]));

        Assert.Equal("diagnose.scope", Assert.Throws<AppValidationException>(() => app.Diagnose(new())).Problems.Single().Code);
        Assert.Equal("diagnose.scope", Assert.Throws<AppValidationException>(() => app.Diagnose(new(SourceId: source.Id, Reference: new(Guid.NewGuid(), Guid.NewGuid())))).Problems.Single().Code);
        Assert.Equal("source.not-found", Assert.Throws<AppValidationException>(() => app.Diagnose(new(SourceId: Guid.NewGuid()))).Problems.Single().Code);
        Assert.Equal("content.not-found", Assert.Throws<AppValidationException>(() => app.Diagnose(new(Reference: new(Guid.NewGuid(), Guid.NewGuid())))).Problems.Single().Code);
        var malformed = Draft(source, Guid.NewGuid(), "Test Malformed") with { Effects = [null!] };
        Assert.Equal("validate.empty-entry", Assert.Throws<AppValidationException>(() => app.Diagnose(new(Revision: malformed))).Problems.Single().Code);
    }

    [Fact]
    public void The_bundled_SRD_sources_have_no_debugger_findings()
    {
        using var temp = new TempApp();
        var srd = temp.App.ListSources().Where(s => s.Title.StartsWith("System Reference Document", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, srd.Count);
        foreach (var source in srd)
        {
            var report = temp.App.Diagnose(new(SourceId: source.Id));
            Assert.True(report.Scope.Count > 500, $"{source.Title}: {report.Scope.Count} revisions");
            Assert.True(report.Findings.Count == 0, $"{source.Title}: " + string.Join("; ", report.Findings.Take(10).Select(f => $"{f.ContentName} {f.Code} {f.EffectId}")));
            Assert.False(report.Truncated);
        }
    }

    [Fact]
    public void The_command_returns_findings_with_their_severity_and_effect()
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Debugger Source", [RulesFamilies.Srd521]));
        temp.App.SaveDraft(Draft(source, Guid.NewGuid(), "Test Spark Well", Sparks));
        var dispatcher = new CommandDispatcher(temp.App);

        using var response = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command = "content.diagnose", payload = new { sourceId = source.Id } })));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean(), response.RootElement.ToString());
        var result = response.RootElement.GetProperty("result");
        var finding = result.GetProperty("findings").EnumerateArray().Single();
        Assert.Equal(("debug.resource-dead", "warning", "sparks", "Test Spark Well"), (finding.GetProperty("code").GetString(), finding.GetProperty("severity").GetString(), finding.GetProperty("effectId").GetString(), finding.GetProperty("contentName").GetString()));
        Assert.Equal((0, 1), (result.GetProperty("errors").GetInt32(), result.GetProperty("warnings").GetInt32()));
    }
}
