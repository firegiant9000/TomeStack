using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>D85 part 1, lane C1: <c>package.targets</c> and <c>target</c> on the export commands, through the dispatcher.</summary>
public class DownConversionExportTests
{
    private const string UnknownSentence = "TomeStack {0} is not a version this export can be made for. Choose one of: 0.6.0, 0.5.0.";

    private static JsonElement Send(CommandDispatcher dispatcher, string command, string payload) =>
        JsonDocument.Parse(dispatcher.Dispatch($$"""{"id":"1","command":"{{command}}","payload":{{payload}}}""")).RootElement.Clone();

    private static (string Code, string Message) ErrorOf(JsonElement response)
    {
        Assert.False(response.GetProperty("ok").GetBoolean(), response.ToString());
        var error = response.GetProperty("error");
        return (error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!);
    }

    private static Guid SaveCharacter(TempApp app) =>
        app.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json")).Character.Id;

    private static JsonElement Result(JsonElement response)
    {
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        return response.GetProperty("result");
    }

    [Fact]
    public void Targets_lists_offered_rows_newest_first()
    {
        using var app = new TempApp();
        var result = Result(Send(new CommandDispatcher(app.App), "package.targets", "{}"));

        Assert.Equal(AppVersionStamp.CurrentVersion, result.GetProperty("current").GetString());
        Assert.Equal("0.7.0", result.GetProperty("currentRow").GetString());
        Assert.Equal("0.6.0", result.GetProperty("previousMinor").GetString());
        var targets = result.GetProperty("targets").EnumerateArray().ToList();
        Assert.Equal(
            [("0.6.0", 9, 10), ("0.5.0", 8, 9)],
            targets.Select(t => (t.GetProperty("release").GetString()!, t.GetProperty("characterSchema").GetInt32(), t.GetProperty("contentSchema").GetInt32())));
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.7.0")]
    [InlineData("0.7.1")]
    [InlineData("0.6")]
    public void Preview_with_unknown_target_fails(string target)
    {
        using var app = new TempApp();
        var id = SaveCharacter(app);
        var dispatcher = new CommandDispatcher(app.App);

        var (code, message) = ErrorOf(Send(dispatcher, "package.exportPreview", $$"""{"characterIds":["{{id}}"],"target":"{{target}}"}"""));

        Assert.Equal("package.target-unknown", code);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, UnknownSentence, target), message);
        // The same request without a target still succeeds.
        Result(Send(dispatcher, "package.exportPreview", $$"""{"characterIds":["{{id}}"]}"""));
    }

    [Fact]
    public void Preview_without_target_is_unchanged()
    {
        using var app = new TempApp();
        var id = SaveCharacter(app);

        var result = Result(Send(new CommandDispatcher(app.App), "package.exportPreview", $$"""{"characterIds":["{{id}}"]}"""));

        Assert.Empty(result.GetProperty("changes").EnumerateArray());
        Assert.False(result.TryGetProperty("target", out var target) && target.ValueKind != JsonValueKind.Null);
        Assert.False(result.TryGetProperty("changesHash", out var hash) && hash.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public void Export_and_save_with_an_unknown_target_fail_before_writing()
    {
        using var app = new TempApp();
        var id = SaveCharacter(app);
        var dispatcher = new CommandDispatcher(app.App);

        Assert.Equal("package.target-unknown", ErrorOf(Send(dispatcher, "package.export", $$"""{"characterIds":["{{id}}"],"target":"0.7.0"}""")).Code);
        Assert.Equal("package.target-unknown", ErrorOf(Send(dispatcher, "package.saveAs", $$"""{"characterIds":["{{id}}"],"target":"0.4.0"}""")).Code);
    }

    [Fact]
    public void Source_pack_preview_refuses_a_target_and_is_unchanged_without_one()
    {
        using var app = new TempApp();
        var source = app.App.CreateHomebrewSource(new("Compat Homebrew A", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        app.App.SaveDraft(new ContentRevision
        {
            ContentId = Guid.Parse("c1000000-0000-4000-8000-000000000001"),
            RevisionId = Guid.Parse("c100e000-0000-4000-8000-000000000001"),
            Kind = ContentKind.Feat,
            Name = "Compat Steady Hands",
            RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id),
            Status = RevisionStatus.Draft,
            Effects = [new ModifierEffect { Id = "steady", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }],
        });
        app.App.Publish(new ContentReference(Guid.Parse("c1000000-0000-4000-8000-000000000001"), Guid.Parse("c100e000-0000-4000-8000-000000000001")));
        var dispatcher = new CommandDispatcher(app.App);

        var unknown = ErrorOf(Send(dispatcher, "package.sourcePackPreview", $$"""{"sourceIds":["{{source.Id}}"],"target":"0.4.0"}"""));
        Assert.Equal("package.target-unknown", unknown.Code);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, UnknownSentence, "0.4.0"), unknown.Message);

        var valid = ErrorOf(Send(dispatcher, "package.sourcePackPreview", $$"""{"sourceIds":["{{source.Id}}"],"target":"0.6.0"}"""));
        Assert.Equal(("package.target-blocked", "Saving a source pack for an older TomeStack is not available yet."), valid);

        Assert.Equal("package.target-blocked", ErrorOf(Send(dispatcher, "package.sourcePackExport", $$"""{"sourceIds":["{{source.Id}}"],"target":"0.6.0"}""")).Code);

        var plain = Result(Send(dispatcher, "package.sourcePackPreview", $$"""{"sourceIds":["{{source.Id}}"]}"""));
        Assert.Empty(plain.GetProperty("changes").EnumerateArray());
        Assert.False(plain.TryGetProperty("target", out var target) && target.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public void Campaign_pack_preview_refuses_a_target_and_is_unchanged_without_one()
    {
        using var app = new TempApp();
        var campaign = app.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Compat Campaign", RulesFamily = RulesFamilies.Srd521, AllowedSources = [] });
        var dispatcher = new CommandDispatcher(app.App);

        var unknown = ErrorOf(Send(dispatcher, "package.campaignPackPreview", $$"""{"campaignId":"{{campaign.Id}}","target":"0.7.1"}"""));
        Assert.Equal("package.target-unknown", unknown.Code);

        var valid = ErrorOf(Send(dispatcher, "package.campaignPackPreview", $$"""{"campaignId":"{{campaign.Id}}","target":"0.6.0"}"""));
        Assert.Equal(("package.target-blocked", "Saving a campaign pack for an older TomeStack is not available yet."), valid);

        Assert.Equal("package.target-blocked", ErrorOf(Send(dispatcher, "package.campaignPackSaveAs", $$"""{"campaignId":"{{campaign.Id}}","target":"0.5.0"}""")).Code);

        var plain = Send(dispatcher, "package.campaignPackPreview", $$"""{"campaignId":"{{campaign.Id}}"}""");
        Assert.True(plain.GetProperty("ok").GetBoolean(), plain.ToString());
        Assert.Empty(plain.GetProperty("result").GetProperty("changes").EnumerateArray());
    }
}
