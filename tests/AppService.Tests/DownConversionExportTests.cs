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

    private sealed class SaveHost(string? chosen) : IHostServices
    {
        public int Opened { get; private set; }

        public string? Suggested { get; private set; }

        public string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension)
        {
            Opened++;
            Suggested = suggestedFileName;
            return chosen;
        }
    }

    private static Character Hero(Func<Character, Character> shape) =>
        shape(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json") with { Name = "Compat Hero" });

    private static Guid Save(TempApp app, Func<Character, Character> shape) => app.App.SaveCharacter(Hero(shape)).Character.Id;

    private static Character RunningEncounter(Character c) => c with { Play = (c.Play ?? new PlayState()) with { Encounter = new EncounterState(1) } };

    private static Character WithDetails(Character c) => c with { Details = new CharacterDetails(Backstory: "Compat backstory.") };

    private static Character WithRoundsLeft(Character c) =>
        c with { Play = (c.Play ?? new PlayState()) with { Toggles = [new ActiveToggle(Guid.Parse("c1000000-0000-4000-8000-0000000000aa"), "surge", 3)] } };

    private static string Payload(Guid id, string target, string? hash = null, string purpose = "share") =>
        $$"""{"characterIds":["{{id}}"],"purpose":"{{purpose}}","target":"{{target}}"{{(hash is null ? "" : $",\"changesHash\":\"{hash}\"")}}}""";

    private static JsonElement Entry(byte[] zipBytes, string path)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(zipBytes));
        using var reader = new StreamReader(zip.GetEntry(path)!.Open());
        return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
    }

    private static byte[] ExportBytes(JsonElement response) => Convert.FromBase64String(Result(response).GetProperty("base64").GetString()!);

    private static string Snapshot(TempApp app) =>
        string.Join("\n", Directory.EnumerateFiles(app.App.DataDirectory, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".lock", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(f =>
            {
                using var stream = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); // the open database is shared
                return $"{Path.GetRelativePath(app.App.DataDirectory, f)}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))}";
            }));

    [Fact]
    public void Preview_for_0_6_0_blocks_a_running_encounter()
    {
        using var app = new TempApp();
        var id = Save(app, RunningEncounter);

        var result = Result(Send(new CommandDispatcher(app.App), "package.exportPreview", Payload(id, "0.6.0")));

        var change = Assert.Single(result.GetProperty("changes").EnumerateArray());
        Assert.Equal($"characters/{id:D}.json", change.GetProperty("entry").GetString());
        Assert.Equal("character", change.GetProperty("kind").GetString());
        Assert.Equal("blocks", change.GetProperty("action").GetString());
        Assert.Equal("package.target-blocked", change.GetProperty("code").GetString());
        Assert.Equal("'Compat Hero': an encounter is running. End it, then export.", change.GetProperty("message").GetString());
        Assert.Equal("0.6.0", result.GetProperty("target").GetString());

        var plan = new PackageService.ExportPlan([Hero(RunningEncounter) with { Id = id }], [], [], [], "x.tomestack.zip");
        var lowered = DownConversion.Lower(plan, ReleaseLadder.Target("0.6.0")!, _ => false, _ => "");
        Assert.True(lowered.Blocked);
    }

    [Fact]
    public void Export_refuses_when_blocked()
    {
        using var app = new TempApp();
        var id = Save(app, RunningEncounter);
        var saved = Path.Combine(app.App.DataDirectory, "out", "x.tomestack.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        var host = new SaveHost(saved);
        var dispatcher = new CommandDispatcher(app.App, host: host);
        var hash = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.6.0"))).GetProperty("changesHash").GetString()!;
        var before = Snapshot(app);

        Assert.Equal("package.target-blocked", ErrorOf(Send(dispatcher, "package.export", Payload(id, "0.6.0", hash))).Code);
        Assert.Equal("package.target-blocked", ErrorOf(Send(dispatcher, "package.saveAs", Payload(id, "0.6.0", hash))).Code);

        Assert.Equal(0, host.Opened);
        Assert.False(File.Exists(saved));
        Assert.False(File.Exists(saved + ".partial"));
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public void Preview_for_0_6_0_drops_rounds_left_and_writes_v9()
    {
        using var app = new TempApp();
        var id = Save(app, c => WithDetails(WithRoundsLeft(c))); // details keep it a v9 character once the v10 field is gone
        var dispatcher = new CommandDispatcher(app.App);

        var preview = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.6.0")));
        var change = Assert.Single(preview.GetProperty("changes").EnumerateArray());
        Assert.Equal(("dropped", "package.target-drops"), (change.GetProperty("action").GetString(), change.GetProperty("code").GetString()));

        var bytes = ExportBytes(Send(dispatcher, "package.export", Payload(id, "0.6.0", preview.GetProperty("changesHash").GetString())));
        var character = Entry(bytes, $"characters/{id:D}.json");
        Assert.Equal(9, character.GetProperty("schemaVersion").GetInt32());
        Assert.DoesNotContain("roundsLeft", character.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_for_0_5_0_drops_details()
    {
        using var app = new TempApp();
        var id = Save(app, WithDetails);
        var dispatcher = new CommandDispatcher(app.App);

        var preview = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.5.0")));
        var change = Assert.Single(preview.GetProperty("changes").EnumerateArray());
        Assert.Equal(("dropped", "package.target-drops"), (change.GetProperty("action").GetString(), change.GetProperty("code").GetString()));

        var bytes = ExportBytes(Send(dispatcher, "package.export", Payload(id, "0.5.0", preview.GetProperty("changesHash").GetString())));
        var character = Entry(bytes, $"characters/{id:D}.json");
        Assert.Equal(8, character.GetProperty("schemaVersion").GetInt32());
        Assert.False(character.TryGetProperty("details", out _));
    }

    [Fact]
    public void Manifest_carries_madeFor_and_true_appVersion()
    {
        using var app = new TempApp();
        var id = Save(app, c => c);
        var dispatcher = new CommandDispatcher(app.App);
        var hash = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.6.0"))).GetProperty("changesHash").GetString();

        var manifest = Entry(ExportBytes(Send(dispatcher, "package.export", Payload(id, "0.6.0", hash))), "manifest.json");

        Assert.Equal("0.6.0", manifest.GetProperty("madeFor").GetString());
        Assert.Equal(AppVersionStamp.CurrentVersion, manifest.GetProperty("appVersion").GetString());
    }

    [Theory]
    [InlineData("share", "Compat-Hero-for-0.6.0.tomestack.zip")]
    [InlineData("backup", "Compat-Hero-for-0.6.0-personal-backup.tomestack.zip")]
    public void File_name_carries_the_target(string purpose, string expected)
    {
        using var app = new TempApp();
        var id = Save(app, c => c);
        var dispatcher = new CommandDispatcher(app.App);

        var preview = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.6.0", purpose: purpose)));
        Assert.Equal(expected, preview.GetProperty("fileName").GetString());
        var export = Result(Send(dispatcher, "package.export", Payload(id, "0.6.0", preview.GetProperty("changesHash").GetString(), purpose)));
        Assert.Equal(expected, export.GetProperty("fileName").GetString());
    }

    [Fact]
    public void Save_requires_matching_changesHash()
    {
        using var app = new TempApp();
        var id = Save(app, WithDetails);
        var saved = Path.Combine(app.App.DataDirectory, "out", "x.tomestack.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        var dispatcher = new CommandDispatcher(app.App, host: new SaveHost(saved));
        const string sentence = "The preview changed since you confirmed it. Review the list and try again.";
        var hash = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.5.0"))).GetProperty("changesHash").GetString()!;

        Assert.Equal(("package.target-changed", sentence), ErrorOf(Send(dispatcher, "package.export", Payload(id, "0.5.0"))));
        Assert.Equal(("package.target-changed", sentence), ErrorOf(Send(dispatcher, "package.saveAs", Payload(id, "0.5.0"))));
        Assert.Equal(("package.target-changed", sentence), ErrorOf(Send(dispatcher, "package.export", Payload(id, "0.5.0", new string('0', 64)))));
        Assert.Equal(("package.target-changed", sentence), ErrorOf(Send(dispatcher, "package.saveAs", Payload(id, "0.5.0", new string('0', 64)))));
        Assert.False(File.Exists(saved));

        Result(Send(dispatcher, "package.export", Payload(id, "0.5.0", hash)));
        Assert.True(Result(Send(dispatcher, "package.saveAs", Payload(id, "0.5.0", hash))).GetProperty("saved").GetBoolean());
        Assert.True(File.Exists(saved));
    }

    [Fact]
    public void Without_a_target_the_changesHash_is_ignored()
    {
        using var app = new TempApp();
        var id = Save(app, c => c);
        var dispatcher = new CommandDispatcher(app.App);

        Result(Send(dispatcher, "package.export", $$"""{"characterIds":["{{id}}"],"changesHash":"nonsense"}"""));
    }

    [Fact]
    public void Same_input_gives_identical_bytes()
    {
        using var app = new TempApp();
        var id = Save(app, WithDetails);
        var dispatcher = new CommandDispatcher(app.App);
        var hash = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.5.0"))).GetProperty("changesHash").GetString();

        var first = ExportBytes(Send(dispatcher, "package.export", Payload(id, "0.5.0", hash)));
        var second = ExportBytes(Send(dispatcher, "package.export", Payload(id, "0.5.0", hash)));

        Assert.Equal(first, second);
    }

    [Fact]
    public void An_export_with_nothing_to_lose_has_no_changes_and_a_hash_of_the_empty_list()
    {
        using var app = new TempApp();
        var id = Save(app, c => c);
        var dispatcher = new CommandDispatcher(app.App);

        var preview = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.6.0")));

        Assert.Empty(preview.GetProperty("changes").EnumerateArray());
        var hash = preview.GetProperty("changesHash").GetString();
        Assert.Equal(DownConversion.HashChanges([]), hash);
        Assert.Equal(64, hash!.Length);
        Result(Send(dispatcher, "package.export", Payload(id, "0.6.0", hash)));
    }

    [Fact]
    public void Two_blocked_characters_give_two_target_blocked_errors()
    {
        using var app = new TempApp();
        var first = Save(app, RunningEncounter);
        var second = app.App.SaveCharacter(Hero(RunningEncounter) with { Id = Guid.NewGuid(), Name = "Compat Second" }).Character.Id;
        var payload = $$"""{"characterIds":["{{first}}","{{second}}"],"purpose":"share","target":"0.6.0","changesHash":"x"}""";

        var response = Send(new CommandDispatcher(app.App), "package.export", payload);

        Assert.Equal("package.target-blocked", ErrorOf(response).Code);
        var problems = response.GetProperty("error").GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Equal("package.target-blocked", p.GetProperty("code").GetString()));
        Assert.Equal("'Compat Hero': an encounter is running. End it, then export.", problems[0].GetProperty("message").GetString());
        Assert.Equal("'Compat Second': an encounter is running. End it, then export.", problems[1].GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("0.4.0", "0.4.0")]
    [InlineData("v1\\nTomeStack", "v1TomeStack")]
    [InlineData("a\\u0007b\\ud83d\\ude00c", "abc")]
    public void Unknown_target_echo_keeps_only_letters_digits_dots_and_hyphens(string sent, string shown)
    {
        using var app = new TempApp();
        var id = SaveCharacter(app);

        var (code, message) = ErrorOf(Send(new CommandDispatcher(app.App), "package.exportPreview", Payload(id, sent)));

        Assert.Equal("package.target-unknown", code);
        Assert.StartsWith($"TomeStack {shown} is not a version", message, StringComparison.Ordinal);
        Assert.DoesNotContain("…", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_target_message_stays_short_for_a_huge_value()
    {
        using var app = new TempApp();
        var id = SaveCharacter(app);

        var (code, message) = ErrorOf(Send(new CommandDispatcher(app.App), "package.exportPreview", Payload(id, new string('x', 10_000))));

        Assert.Equal("package.target-unknown", code);
        Assert.True(message.Length < 200, message.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.StartsWith("TomeStack xxxxxxxxxxxxxxxx… is not", message, StringComparison.Ordinal);
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
