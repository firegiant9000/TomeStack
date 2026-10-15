using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Item 1 (2026-10-14 plan): the builder's hint counts content the builder hides because it is not published.</summary>
public class DraftCountTests
{
    private static ContentRevision Draft(SourceRecord source, ContentKind kind, string name, string family) => new()
    {
        ContentId = Guid.NewGuid(),
        RevisionId = Guid.Empty,
        Kind = kind,
        Name = name,
        RulesFamilies = [family],
        Provenance = new(source.Id),
        Status = RevisionStatus.Draft,
        Effects = [],
    };

    [Fact]
    public void Counts_content_with_no_published_revision_by_kind_in_the_family()
    {
        using var t = new TempApp();
        var source = t.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        t.App.SaveDraft(Draft(source, ContentKind.Class, "Fixture Warden", RulesFamilies.Srd51));
        t.App.SaveDraft(Draft(source, ContentKind.Class, "Fixture Seer", RulesFamilies.Srd51));
        t.App.SaveDraft(Draft(source, ContentKind.Feat, "Fixture Knack", RulesFamilies.Srd51));

        var counts = t.App.DraftCounts(RulesFamilies.Srd51);

        Assert.Equal(2, counts["class"]);
        Assert.Equal(1, counts["feat"]);
        Assert.False(counts.ContainsKey("species"));
    }

    [Fact]
    public void Published_content_is_not_counted_even_with_a_newer_draft()
    {
        using var t = new TempApp();
        var source = t.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd51]));
        var draft = t.App.SaveDraft(Draft(source, ContentKind.Class, "Fixture Warden", RulesFamilies.Srd51));
        t.App.Publish(draft);
        // A newer draft of the same content: the builder still offers the published revision, so nothing is hidden.
        t.App.SaveDraft(Draft(source, ContentKind.Class, "Fixture Warden", RulesFamilies.Srd51) with { ContentId = draft.ContentId });

        Assert.False(t.App.DraftCounts(RulesFamilies.Srd51).ContainsKey("class"));
    }

    [Fact]
    public void Other_family_drafts_are_not_counted_and_an_unknown_family_is_refused()
    {
        using var t = new TempApp();
        var source = t.App.CreateHomebrewSource(new("Fixture Shelf", [RulesFamilies.Srd521]));
        t.App.SaveDraft(Draft(source, ContentKind.Class, "Fixture Warden", RulesFamilies.Srd521));

        Assert.False(t.App.DraftCounts(RulesFamilies.Srd51).ContainsKey("class"));
        Assert.Equal("rules-family.unknown", Assert.Throws<AppValidationException>(() => t.App.DraftCounts("srd-9")).Problems[0].Code);
    }

    [Fact]
    public void The_command_is_dispatched()
    {
        using var t = new TempApp();
        Assert.Contains("content.draftCounts", CommandDispatcher.Commands);
        var dispatcher = new CommandDispatcher(t.App);
        using var response = JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(
            new { id = "1", command = "content.draftCounts", payload = new { rulesFamily = RulesFamilies.Srd51 } }, RulesJson.Compact)));
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
    }
}
