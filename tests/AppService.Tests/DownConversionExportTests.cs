using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>D85 part 1, lane C1: <c>package.targets</c> and <c>target</c> on the export commands, through the dispatcher.</summary>
public class DownConversionExportTests
{
    private static string UnknownSentence(string target) => $"TomeStack {target} is not a version this export can be made for. Choose one of: 0.7.0, 0.6.0, 0.5.0.";

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
        var lowered = DownConversion.Lower(plan, ReleaseLadder.Target("0.6.0")!, _ => false, _ => "", new EmptyCatalog());
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
        Assert.Equal("0.8.0", result.GetProperty("currentRow").GetString());
        Assert.Equal("0.7.0", result.GetProperty("previousMinor").GetString());
        var targets = result.GetProperty("targets").EnumerateArray().ToList();
        Assert.Equal(
            [("0.7.0", 10, 11), ("0.6.0", 9, 10), ("0.5.0", 8, 9)],
            targets.Select(t => (t.GetProperty("release").GetString()!, t.GetProperty("characterSchema").GetInt32(), t.GetProperty("contentSchema").GetInt32())));
    }

    [Theory]
    [InlineData("0.4.0")]
    [InlineData("0.7.1")]
    [InlineData("0.8.0")]
    [InlineData("0.6")]
    public void Preview_with_unknown_target_fails(string target)
    {
        using var app = new TempApp();
        var id = SaveCharacter(app);
        var dispatcher = new CommandDispatcher(app.App);

        var (code, message) = ErrorOf(Send(dispatcher, "package.exportPreview", $$"""{"characterIds":["{{id}}"],"target":"{{target}}"}"""));

        Assert.Equal("package.target-unknown", code);
        Assert.Equal(UnknownSentence(target), message);
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

        Assert.Equal("package.target-unknown", ErrorOf(Send(dispatcher, "package.export", $$"""{"characterIds":["{{id}}"],"target":"0.8.0"}""")).Code);
        Assert.Equal("package.target-unknown", ErrorOf(Send(dispatcher, "package.saveAs", $$"""{"characterIds":["{{id}}"],"target":"0.4.0"}""")).Code);
    }

    [Fact]
    public void Source_pack_commands_take_a_target_and_are_unchanged_without_one()
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
        Assert.Equal(UnknownSentence("0.4.0"), unknown.Message);

        var valid = Result(Send(dispatcher, "package.sourcePackPreview", $$"""{"sourceIds":["{{source.Id}}"],"target":"0.6.0"}"""));
        Assert.Equal("0.6.0", valid.GetProperty("target").GetString());
        Assert.False(string.IsNullOrEmpty(valid.GetProperty("changesHash").GetString()));

        Assert.Equal("package.target-changed", ErrorOf(Send(dispatcher, "package.sourcePackExport", $$"""{"sourceIds":["{{source.Id}}"],"target":"0.6.0"}""")).Code);

        var plain = Result(Send(dispatcher, "package.sourcePackPreview", $$"""{"sourceIds":["{{source.Id}}"]}"""));
        Assert.Empty(plain.GetProperty("changes").EnumerateArray());
        Assert.False(plain.TryGetProperty("target", out var target) && target.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public void Campaign_pack_commands_take_a_target_and_are_unchanged_without_one()
    {
        using var app = new TempApp();
        var campaign = app.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Compat Campaign", RulesFamily = RulesFamilies.Srd521, AllowedSources = [] });
        var dispatcher = new CommandDispatcher(app.App);

        var unknown = ErrorOf(Send(dispatcher, "package.campaignPackPreview", $$"""{"campaignId":"{{campaign.Id}}","target":"0.7.1"}"""));
        Assert.Equal("package.target-unknown", unknown.Code);

        var valid = Send(dispatcher, "package.campaignPackPreview", $$"""{"campaignId":"{{campaign.Id}}","target":"0.6.0"}""");
        Assert.True(valid.GetProperty("ok").GetBoolean(), valid.ToString());
        Assert.Equal("0.6.0", valid.GetProperty("result").GetProperty("target").GetString());

        Assert.Equal("package.target-changed", ErrorOf(Send(dispatcher, "package.campaignPackExport", $$"""{"campaignId":"{{campaign.Id}}","target":"0.5.0"}""")).Code);

        var plain = Send(dispatcher, "package.campaignPackPreview", $$"""{"campaignId":"{{campaign.Id}}"}""");
        Assert.True(plain.GetProperty("ok").GetBoolean(), plain.ToString());
        Assert.Empty(plain.GetProperty("result").GetProperty("changes").EnumerateArray());
    }

    [Fact]
    public void Pack_save_as_commands_resolve_the_target_and_need_the_confirmed_hash()
    {
        using var app = new TempApp();
        var source = app.App.CreateHomebrewSource(new("Compat Homebrew A", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        var feat = new ContentRevision
        {
            ContentId = Guid.Parse("c1000000-0000-4000-8000-0000000000e1"),
            RevisionId = Guid.Parse("c100e000-0000-4000-8000-0000000000e1"),
            Kind = ContentKind.Feat,
            Name = "Compat Steady Hands",
            RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(source.Id),
            Status = RevisionStatus.Draft,
            Effects = [new ModifierEffect { Id = "steady", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }],
        };
        app.App.SaveDraft(feat);
        app.App.Publish(feat.Reference);
        var campaign = app.App.SaveCampaign(new() { Id = Guid.Empty, Name = "Compat Campaign", RulesFamily = RulesFamilies.Srd51, AllowedSources = [source.Id] });
        var host = new SaveHost(null);
        var dispatcher = new CommandDispatcher(app.App, host: host);
        var packs = new (string Command, string Body)[]
        {
            ("package.sourcePackSaveAs", $$"""{"sourceIds":["{{source.Id}}"],"target":"TARGET"}"""),
            ("package.campaignPackSaveAs", $$"""{"campaignId":"{{campaign.Id}}","target":"TARGET"}"""),
        };

        foreach (var (command, body) in packs)
        {
            Assert.Equal("package.target-unknown", ErrorOf(Send(dispatcher, command, body.Replace("TARGET", "0.4.0", StringComparison.Ordinal))).Code);
            Assert.Equal("package.target-changed", ErrorOf(Send(dispatcher, command, body.Replace("TARGET", "0.6.0", StringComparison.Ordinal))).Code);
        }
        Assert.Equal(0, host.Opened);
    }

    private static ContentRevision CompatFeat(Guid revisionId, Dictionary<string, JsonElement>? extensions) => new()
    {
        ContentId = Guid.Parse("c1000000-0000-4000-8000-000000000009"),
        RevisionId = revisionId,
        Kind = ContentKind.Feat,
        Name = "Compat Steady Hands",
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(Guid.Parse("c2000000-0000-4000-8000-000000000009")),
        Status = RevisionStatus.Published,
        Effects = [],
        Extensions = extensions,
    };

    [Fact]
    public void Uuid5_matches_known_vector()
    {
        // RFC 4122 NAMESPACE_DNS with "www.example.com" (Python uuid.uuid5 gives the same value).
        Assert.Equal(new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"), DownConversion.Uuid5(new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8"), "www.example.com"));
    }

    [Fact]
    public void CopyId_is_stable_and_changes_with_bytes()
    {
        var root = Guid.Parse("c1000000-0000-4000-8000-000000000001");
        var id = DownConversion.CopyId(root, 9, "{\"a\":1}"u8);

        Assert.Equal(id, DownConversion.CopyId(root, 9, "{\"a\":1}"u8));
        Assert.NotEqual(id, DownConversion.CopyId(root, 9, "{\"a\":2}"u8));
        Assert.NotEqual(id, DownConversion.CopyId(root, 8, "{\"a\":1}"u8));
        Assert.NotEqual(id, DownConversion.CopyId(Guid.Parse("c1000000-0000-4000-8000-000000000002"), 9, "{\"a\":1}"u8));
        Assert.Equal(5, id.Version);
    }

    [Fact]
    public void CanonicalBytes_zeroes_revisionId_and_removes_marker()
    {
        var marker = JsonDocument.Parse("""{"revisionId":"c100e000-0000-4000-8000-000000000001","schemaVersion":9}""").RootElement.Clone();
        var other = JsonDocument.Parse("1").RootElement.Clone();
        var marked = CompatFeat(Guid.Parse("c100e000-0000-4000-8000-0000000000aa"), new() { [DownConversion.MarkerKey] = marker, ["someOtherKey"] = other });
        var plain = CompatFeat(Guid.Parse("c100e000-0000-4000-8000-0000000000bb"), new() { ["someOtherKey"] = other });

        var bytes = DownConversion.CanonicalBytes(marked);

        Assert.Equal(bytes, DownConversion.CanonicalBytes(plain));
        using var doc = JsonDocument.Parse(bytes);
        Assert.Equal("00000000-0000-0000-0000-000000000000", doc.RootElement.GetProperty("revisionId").GetString());
        Assert.False(doc.RootElement.TryGetProperty(DownConversion.MarkerKey, out _));
        Assert.Equal(1, doc.RootElement.GetProperty("someOtherKey").GetInt32());
    }

    // Pinned once from the implementation. A change here means every saved copy id changes: do not update casually.
    private const string PinnedDigest = "31223da1d19aa359f5bc251a8213547d78067240427b4f215e0b6dd07f2f5a02";
    private static readonly Guid PinnedCopyId = new("ea8b2cc8-293e-52f0-8f99-4110fe16bc66");

    [Fact]
    public void CanonicalBytes_and_CopyId_are_pinned()
    {
        var root = Guid.Parse("c1000000-0000-4000-8000-000000000001");
        var copy = CompatFeat(Guid.Parse("c100e000-0000-4000-8000-0000000000cc"), new() { ["someOtherKey"] = JsonDocument.Parse("1").RootElement.Clone() }) with { SchemaVersion = 11 }; // fixed, not the moving current version

        var bytes = DownConversion.CanonicalBytes(copy);

        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal(PinnedDigest, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        Assert.Equal(PinnedCopyId, DownConversion.CopyId(root, 10, bytes));
    }

    // ---- C2-2: content copies, the cascade and the bundled check -----------------------------------------------------------

    private static readonly Guid HomebrewId = Guid.Parse("c2000000-0000-4000-8000-0000000000a1");
    private static readonly Guid FeatureContent = Guid.Parse("c1000000-0000-4000-8000-0000000000f1");
    private static readonly Guid FeatureRevision = Guid.Parse("c100e000-0000-4000-8000-0000000000f1");
    private static readonly Guid PathContent = Guid.Parse("c1000000-0000-4000-8000-0000000000b1");
    private static readonly Guid PathRevision = Guid.Parse("c100e000-0000-4000-8000-0000000000b1");
    private static readonly ContentReference FeatureRef = new(FeatureContent, FeatureRevision);
    private static readonly ContentReference PathRef = new(PathContent, PathRevision);
    private const string PathCopiedSentence = "'Compat Path of Tides' (Compat Homebrew A): copied because it grants a changed feature; nothing of its own is left out.";

    private sealed class HomebrewCatalog : IContentCatalog
    {
        public ContentRevision? FindRevision(ContentReference reference) => null;

        public SourceRecord? FindSource(Guid sourceId) => new()
        {
            Id = sourceId, Title = "Compat Homebrew A", Publisher = "", RulesFamilies = [RulesFamilies.Srd51], EditionVersion = "", License = "", Redistributable = true,
        };

        // Knows no revisions, so it holds no guides.
        public IReadOnlyList<ContentRevision> GuidesFor(Guid targetContentId) => [];
    }

    private sealed class EmptyCatalog : IContentCatalog
    {
        public ContentRevision? FindRevision(ContentReference reference) => null;

        public SourceRecord? FindSource(Guid sourceId) => null;

        // Empty catalogue: no guides.
        public IReadOnlyList<ContentRevision> GuidesFor(Guid targetContentId) => [];
    }

    /// <summary>A v11 feature whose fly-speed bonus a v10 reader has no field for, so lowering drops it.</summary>
    private static ContentRevision V11Feature(Guid? revisionId = null) => new()
    {
        ContentId = FeatureContent,
        RevisionId = revisionId ?? FeatureRevision,
        SchemaVersion = 11,
        Kind = ContentKind.Feature,
        Name = "Compat Steady Hands",
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(HomebrewId),
        Status = RevisionStatus.Published,
        Effects =
        [
            new ModifierEffect { Id = "steady", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" },
            new ModifierEffect { Id = "fly", Operation = ModifierOperation.Bonus, Target = FieldIds.FlySpeed, Value = "10" },
        ],
    };

    /// <summary>A v9 subclass: nothing of its own to lower, so only the cascade can change it.</summary>
    private static ContentRevision V9Path(Guid contentId, Guid revisionId, params Effect[] effects) => new()
    {
        ContentId = contentId,
        RevisionId = revisionId,
        SchemaVersion = 9,
        Kind = ContentKind.Subclass,
        Name = "Compat Path of Tides",
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(HomebrewId),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static GrantEffect GrantsFeature() => new() { Id = "grant-hands", Grant = GrantKind.Content, Content = FeatureRef, Level = 3 };

    private static LoweredPlan LowerFor(LadderRow row, Character hero, params ContentRevision[] revisions) =>
        DownConversion.Lower(new([hero], [.. revisions], [], [], "x.tomestack.zip"), row, id => false, _ => "Compat Homebrew A", new HomebrewCatalog());

    private static LadderRow Row(string release) => ReleaseLadder.Target(release)!;

    [Fact]
    public void Lower_copies_a_v11_feature_with_marker_and_new_id()
    {
        var original = V11Feature();

        var lowered = LowerFor(Row("0.6.0"), Hero(c => c), original);

        var copy = Assert.Single(lowered.Plan.Revisions);
        Assert.False(lowered.Blocked);
        Assert.Equal(10, copy.SchemaVersion);
        Assert.Equal((original.ContentId, original.Provenance.SourceId, original.Status), (copy.ContentId, copy.Provenance.SourceId, copy.Status));
        Assert.Equal(new DownConvertedFrom(FeatureRevision, 11), DownConversion.ReadMarker(copy));
        Assert.Equal(DownConversion.CopyId(FeatureRevision, 10, DownConversion.CanonicalBytes(copy)), copy.RevisionId);
        Assert.NotEqual(FeatureRevision, copy.RevisionId);
        var change = Assert.Single(lowered.Changes, c => c.Kind == "content");
        Assert.Equal(("dropped", DownConversion.DropsCode, $"content/{FeatureRevision:D}.json", FeatureRevision, "Compat Steady Hands", "Compat Homebrew A"),
            (change.Action, change.Code, change.Entry, change.Id, change.Name, change.SourceTitle));
        Assert.StartsWith("'Compat Steady Hands' (Compat Homebrew A): ", change.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Lower_rewrites_grants_and_choice_options_and_cascades()
    {
        var choiceRevision = Guid.Parse("c100e000-0000-4000-8000-0000000000b2");
        var granting = V9Path(PathContent, PathRevision, GrantsFeature());
        var offering = V9Path(Guid.Parse("c1000000-0000-4000-8000-0000000000b2"), choiceRevision,
            new ChoiceEffect { Id = "pick-hands", ChoiceId = "hands", Options = [FeatureRef] }) with { Name = "Compat Path of Waves" };

        var lowered = LowerFor(Row("0.6.0"), Hero(c => c), V11Feature(), granting, offering);

        var featureCopy = lowered.Plan.Revisions[0];
        var pathCopy = lowered.Plan.Revisions[1];
        var waveCopy = lowered.Plan.Revisions[2];
        Assert.Equal(featureCopy.RevisionId, Assert.IsType<GrantEffect>(Assert.Single(pathCopy.Effects)).Content!.RevisionId);
        Assert.Equal(FeatureContent, ((GrantEffect)pathCopy.Effects[0]).Content!.ContentId);
        Assert.Equal([featureCopy.RevisionId], Assert.IsType<ChoiceEffect>(Assert.Single(waveCopy.Effects)).Options.Select(o => o.RevisionId));
        Assert.Equal(DownConversion.CopyId(PathRevision, 9, DownConversion.CanonicalBytes(pathCopy)), pathCopy.RevisionId);
        Assert.Equal(new DownConvertedFrom(PathRevision, 9), DownConversion.ReadMarker(pathCopy));
        var copied = Assert.Single(lowered.Changes, c => c.Id == PathRevision);
        Assert.Equal(("copied", "package.target-copied", PathCopiedSentence), (copied.Action, copied.Code, copied.Message));
        Assert.Equal("copied", Assert.Single(lowered.Changes, c => c.Id == choiceRevision).Action);
    }

    [Fact]
    public void Lower_never_rewrites_content_id_references()
    {
        var extending = V9Path(PathContent, PathRevision, GrantsFeature()) with { ExtendsChoice = new ChoiceExtension(FeatureContent, "hands") };

        var lowered = LowerFor(Row("0.6.0"), Hero(c => c), V11Feature(), extending);

        var copy = lowered.Plan.Revisions[1];
        Assert.NotEqual(PathRevision, copy.RevisionId);
        Assert.Equal(new ChoiceExtension(FeatureContent, "hands"), copy.ExtendsChoice);
        Assert.Equal(FeatureContent, ((GrantEffect)copy.Effects[0]).Content!.ContentId);
    }

    [Fact]
    public void Character_pins_follow_the_copies_via_ReplaceReference()
    {
        var hero = Hero(c => c with
        {
            Pins = [FeatureRef],
            Classes = [new ClassLevel(PathRef, 1)],
            Choices = [new ChoiceSelection(PathRef, "hands", [FeatureRef])],
            Equipment = [new EquipmentEntry(FeatureRef)],
            Spells = [new KnownSpell(PathContent, FeatureRef)],
            CrossFamilyExceptions = [new CrossFamilyException(FeatureRef, "Compat reason")],
            CampaignExceptions = [new CampaignException(FeatureRef, "Compat reason")],
            Play = (c.Play ?? new PlayState()) with { Concentration = new Concentration(FeatureRef, "Compat Steady Hands") },
        });

        var lowered = LowerFor(Row("0.6.0"), hero, V11Feature(), V9Path(PathContent, PathRevision, GrantsFeature()));

        var featureId = lowered.Plan.Revisions[0].RevisionId;
        var pathId = lowered.Plan.Revisions[1].RevisionId;
        var moved = Assert.Single(lowered.Plan.Characters);
        Assert.Equal([featureId], moved.Pins.Select(r => r.RevisionId));
        Assert.Equal([pathId], moved.Classes.Select(c => c.Class.RevisionId));
        Assert.Equal((pathId, featureId), (moved.Choices[0].Source.RevisionId, moved.Choices[0].Selected[0].RevisionId));
        Assert.Equal(featureId, moved.Equipment[0].Item.RevisionId);
        Assert.Equal(featureId, moved.Spells[0].Spell.RevisionId);
        Assert.Equal(featureId, moved.CrossFamilyExceptions[0].Content.RevisionId);
        Assert.Equal(featureId, moved.CampaignExceptions[0].Content.RevisionId);
        Assert.Equal(featureId, moved.Play.Concentration!.Spell.RevisionId);
    }

    [Fact]
    public void Copy_of_a_copy_collapses_to_the_root()
    {
        var original = V11Feature();
        var first = Assert.Single(LowerFor(Row("0.6.0"), Hero(c => c), original).Plan.Revisions);

        var viaCopy = Assert.Single(LowerFor(Row("0.5.0"), Hero(c => c), first).Plan.Revisions);
        var direct = Assert.Single(LowerFor(Row("0.5.0"), Hero(c => c), original).Plan.Revisions);

        Assert.Equal(new DownConvertedFrom(FeatureRevision, 11), DownConversion.ReadMarker(viaCopy));
        Assert.Equal(direct.RevisionId, viaCopy.RevisionId);
        Assert.Equal(PackageService.Json(direct), PackageService.Json(viaCopy));
    }

    [Fact]
    public void Forged_marker_is_not_trusted_at_export()
    {
        var other = Guid.Parse("c100e000-0000-4000-8000-0000000000ee");
        var forged = V11Feature() with
        {
            Extensions = new() { [DownConversion.MarkerKey] = JsonDocument.Parse($$"""{"revisionId":"{{other}}","schemaVersion":11}""").RootElement.Clone() },
        };

        var copy = Assert.Single(LowerFor(Row("0.6.0"), Hero(c => c), forged).Plan.Revisions);

        Assert.Equal(new DownConvertedFrom(FeatureRevision, 11), DownConversion.ReadMarker(copy));
        Assert.Equal(DownConversion.CopyId(FeatureRevision, 10, DownConversion.CanonicalBytes(copy)), copy.RevisionId);
        Assert.Equal((FeatureRevision, 11), DownConversion.VerifiedRoot(forged));
        Assert.Equal((FeatureRevision, 11), DownConversion.VerifiedRoot(copy));
    }

    [Fact]
    public void Lower_is_independent_of_plan_order()
    {
        var feature = V11Feature();
        var path = V9Path(PathContent, PathRevision, GrantsFeature());

        var forward = LowerFor(Row("0.6.0"), Hero(c => c), feature, path).Plan.Revisions;
        var reversed = LowerFor(Row("0.6.0"), Hero(c => c), path, feature).Plan.Revisions;

        Assert.Equal(forward.Select(r => r.RevisionId).Order(), reversed.Select(r => r.RevisionId).Order());
        Assert.Equal(PackageService.Json(forward[0]), PackageService.Json(reversed[1]));
        Assert.Equal(PackageService.Json(forward[1]), PackageService.Json(reversed[0]));
    }

    [Fact]
    public void Copy_of_a_copy_collapses_to_the_root_with_a_cascade_parent()
    {
        var feature = V11Feature();
        var path = V9Path(PathContent, PathRevision, GrantsFeature());
        var first = LowerFor(Row("0.6.0"), Hero(c => c), feature, path).Plan.Revisions;

        var viaCopy = LowerFor(Row("0.5.0"), Hero(c => c), [.. first]).Plan.Revisions;
        var direct = LowerFor(Row("0.5.0"), Hero(c => c), feature, path).Plan.Revisions;

        Assert.Equal(new DownConvertedFrom(PathRevision, 9), DownConversion.ReadMarker(viaCopy[1]));
        Assert.Equal(direct.Select(r => r.RevisionId), viaCopy.Select(r => r.RevisionId));
        Assert.Equal(PackageService.Json(direct[1]), PackageService.Json(viaCopy[1]));
    }

    [Fact]
    public void ReadMarker_returns_null_for_any_malformed_shape()
    {
        static ContentRevision With(string json) => V11Feature() with { Extensions = new() { [DownConversion.MarkerKey] = JsonDocument.Parse(json).RootElement.Clone() } };

        Assert.NotNull(DownConversion.ReadMarker(With("""{"revisionId":"c100e000-0000-4000-8000-0000000000f1","schemaVersion":11}""")));
        Assert.Null(DownConversion.ReadMarker(V11Feature()));
        Assert.Null(DownConversion.ReadMarker(With("""{"revisionId":"c100e000-0000-4000-8000-0000000000f1"}""")));
        Assert.Null(DownConversion.ReadMarker(With("""{"revisionId":"c100e000-0000-4000-8000-0000000000f1","schemaVersion":11,"x":1}""")));
        Assert.Null(DownConversion.ReadMarker(With("""{"revisionId":"nope","schemaVersion":11}""")));
        Assert.Null(DownConversion.ReadMarker(With("""{"revisionId":"c100e000-0000-4000-8000-0000000000f1","schemaVersion":""" + (ContentRevision.CurrentSchemaVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")));
        Assert.Null(DownConversion.ReadMarker(With("""{"revisionId":"c100e000-0000-4000-8000-0000000000f1","schemaVersion":0}""")));
        Assert.Null(DownConversion.ReadMarker(With("""{"revisionId":"c100e000-0000-4000-8000-0000000000f1","schemaVersion":"11"}""")));
        Assert.Null(DownConversion.ReadMarker(With("[1]")));
        Assert.Null(DownConversion.ReadMarker(With("null")));
    }

    [Fact]
    public void Bundled_revision_in_set_is_written_as_is()
    {
        var bundled = V11Feature();
        var row = Row("0.6.0") with { BundledRevisionIds = new HashSet<Guid> { FeatureRevision } };

        var lowered = DownConversion.Lower(new([Hero(c => c with { Pins = [FeatureRef] })], [bundled], [], [], "x.tomestack.zip"), row, id => id == HomebrewId, _ => "Compat Homebrew A", new HomebrewCatalog());

        Assert.Same(bundled, Assert.Single(lowered.Plan.Revisions));
        Assert.False(lowered.Blocked);
        Assert.Empty(lowered.Changes);
    }

    [Fact]
    public void Bundled_revision_outside_set_blocks()
    {
        using var app = new TempApp();
        var srd = app.App.ListSources().First(s => s.Title.StartsWith("System Reference Document 5.1", StringComparison.Ordinal));
        var bundled = app.App.Store.ListRevisions().First(r => r.Provenance.SourceId == srd.Id && r.Status == RevisionStatus.Published);
        var id = Save(app, c => c with { Pins = [new ContentReference(bundled.ContentId, bundled.RevisionId)] });
        var row = Row("0.6.0") with { BundledRevisionIds = new HashSet<Guid>() };

        var preview = app.App.PreviewExport([id], ExportPurpose.Share, row);

        Assert.NotEmpty(preview.Changes);
        Assert.All(preview.Changes, c => Assert.Equal(("blocks", "package.target-blocked", "content"), (c.Action, c.Code, c.Kind)));
        const string tail = "Update the receiver's TomeStack; this content cannot be copied into the file.";
        var uses = preview.Changes.Where(c => c.Message.StartsWith("'Compat Hero' uses ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(uses);
        Assert.All(uses, c => Assert.Equal($"'Compat Hero' uses '{c.Name}' from the bundled SRD 5.1 pack, which TomeStack 0.6.0 does not have. {tail}", c.Message));
        Assert.All(preview.Changes.Except(uses), c => Assert.Equal($"'{c.Name}' from the bundled SRD 5.1 pack is not in TomeStack 0.6.0. {tail}", c.Message));
    }

    private static readonly Guid BundledSourceId = Guid.Parse("c2000000-0000-4000-8000-0000000000b0");

    private static ContentRevision CompatBundled(Guid n, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"c1000000-0000-4000-8000-{n.ToString("N")[20..]}"),
        RevisionId = n,
        SchemaVersion = ContentRevision.CurrentSchemaVersion,
        Kind = ContentKind.Feature,
        Name = $"Compat Bundled {n.ToString("N")[28..]}",
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(BundledSourceId),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static ContentRevision CompatGranting(Guid n, string name, ContentReference granted, bool blocker) => V11Feature(n) with
    {
        ContentId = Guid.Parse($"c1000000-0000-4000-8000-{n.ToString("N")[20..]}"),
        Name = name,
        SchemaVersion = ContentRevision.CurrentSchemaVersion,
        Effects =
        [
            new GrantEffect { Id = "grant-bundled", Grant = GrantKind.Content, Content = granted, Level = 1 },
            .. blocker ? new Effect[] { new ToggleEffect { Id = "form", ToggleId = "form", Label = "Form", Cost = "1", ResourceId = "focus" } } : [],
        ],
    };

    [Fact]
    public void Bundled_revision_left_out_with_its_referrer_gets_no_line()
    {
        var bundledId = Guid.Parse("c100e000-0000-4000-8000-0000000000c1");
        var referrerId = Guid.Parse("c100e000-0000-4000-8000-0000000000c2");
        var bundled = CompatBundled(bundledId);
        var referrer = CompatBundled(referrerId, new GrantEffect { Id = "grant-bundled", Grant = GrantKind.Content, Content = new(bundled.ContentId, bundledId), Level = 1 });
        var row = Row("0.6.0") with { BundledRevisionIds = new HashSet<Guid>() };

        var lowered = DownConversion.Lower(new([Hero(c => c)], [bundled, referrer], [], [], "x.tomestack.zip"), row, id => id == BundledSourceId, _ => "Compat Bundle", new HomebrewCatalog(), _ => true);

        Assert.Empty(lowered.Changes);
        Assert.False(lowered.Blocked);
        Assert.Empty(lowered.Plan.Revisions);
    }

    [Fact]
    public void Bundled_block_names_a_referrer_that_is_written()
    {
        var bundledId = Guid.Parse("c100e000-0000-4000-8000-0000000000d1");
        var leftId = Guid.Parse("c100e000-0000-4000-8000-0000000000d2");
        var writtenId = Guid.Parse("c100e000-0000-4000-8000-0000000000d3");
        var bundled = CompatBundled(bundledId);
        var reference = new ContentReference(bundled.ContentId, bundledId);
        var left = CompatGranting(leftId, "Compat Left Out", reference, blocker: true);
        var written = CompatGranting(writtenId, "Compat Written", reference, blocker: false);
        var row = Row("0.6.0") with { BundledRevisionIds = new HashSet<Guid>() };

        var lowered = DownConversion.Lower(new([Hero(c => c)], [left, written, bundled], [], [], "x.tomestack.zip"), row, id => id == BundledSourceId, id => id == BundledSourceId ? "Compat Bundle" : "Compat Homebrew A", new HomebrewCatalog(), r => r.RevisionId == leftId);

        Assert.Contains(lowered.Changes, c => c.Action == "leftOut" && c.Name == "Compat Left Out");
        var block = Assert.Single(lowered.Changes, c => c.Action == "blocks" && c.Id == bundledId);
        Assert.StartsWith("'Compat Written' (Compat Homebrew A) uses ", block.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_creation_guide_is_left_out_of_the_copy_with_one_listed_change()
    {
        var guide = new ContentRevision
        {
            ContentId = Guid.Parse("c1000000-0000-4000-8000-0000000000e1"),
            RevisionId = Guid.Parse("c100e000-0000-4000-8000-0000000000e1"),
            SchemaVersion = ContentRevision.CreationGuideSchemaVersion,
            Kind = ContentKind.CreationGuide,
            Name = "Compat Guide",
            RulesFamilies = [RulesFamilies.Srd51],
            Provenance = new(HomebrewId),
            Status = RevisionStatus.Published,
            Effects = [new PrimaryAbilitiesEffect { Id = "primary", Abilities = ["wis"] }],
        };
        var feature = V11Feature();

        var lowered = LowerFor(Row("0.7.0"), Hero(c => c with { Pins = [FeatureRef] }), guide, feature);

        Assert.False(lowered.Blocked);
        Assert.Equal(feature.RevisionId, Assert.Single(lowered.Plan.Revisions).RevisionId);
        var line = Assert.Single(lowered.Changes);
        Assert.Equal(("dropped", "content", guide.RevisionId, "Compat Guide"), (line.Action, line.Kind, line.Id, line.Name));
        Assert.Contains("creation guide", line.Message, StringComparison.Ordinal);
        Assert.Contains(FeatureRef.RevisionId, lowered.Plan.Characters.Single().AllReferences().Select(r => r.RevisionId));
    }

    [Fact]
    public void Bundled_revision_is_never_lowered()
    {
        var bundled = V11Feature();
        var row = Row("0.5.0") with { BundledRevisionIds = new HashSet<Guid>() };

        var lowered = DownConversion.Lower(new([Hero(c => c with { Pins = [FeatureRef] })], [bundled], [], [], "x.tomestack.zip"), row, id => id == HomebrewId, _ => "System Reference Document 5.2.1", new HomebrewCatalog());

        Assert.True(lowered.Blocked);
        Assert.Same(bundled, Assert.Single(lowered.Plan.Revisions));
        var change = Assert.Single(lowered.Changes);
        Assert.Equal("'Compat Hero' uses 'Compat Steady Hands' from the bundled SRD 5.2.1 pack, which TomeStack 0.5.0 does not have. Update the receiver's TomeStack; this content cannot be copied into the file.", change.Message);
    }

    [Fact]
    public void Reference_cycle_among_changed_revisions_blocks()
    {
        var first = V11Feature() with { Effects = [.. V11Feature().Effects, new GrantEffect { Id = "to-path", Grant = GrantKind.Content, Content = PathRef, Level = 1 }] };
        var second = V9Path(PathContent, PathRevision, GrantsFeature());

        var lowered = LowerFor(Row("0.6.0"), Hero(c => c), first, second);

        Assert.True(lowered.Blocked);
        var cycle = lowered.Changes.Where(c => c.Message.EndsWith("it refers to itself through other changed content, so a copy cannot be made.", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, cycle.Count);
        Assert.All(cycle, c => Assert.Equal(("blocks", "package.target-blocked"), (c.Action, c.Code)));
        Assert.Contains(cycle, c => c.Message.StartsWith("'Compat Path of Tides' (Compat Homebrew A): it refers", StringComparison.Ordinal));
    }

    [Fact]
    public void Rounds_left_names_the_toggle_label()
    {
        var toggleContent = Guid.Parse("c1000000-0000-4000-8000-0000000000c1");
        var toggle = V9Path(toggleContent, Guid.Parse("c100e000-0000-4000-8000-0000000000c1"),
            new ToggleEffect { Id = "surge-toggle", ToggleId = "surge", Label = "Surge of Tides" });
        var hero = WithDetails(Hero(c => c with
        {
            Play = (c.Play ?? new PlayState()) with { Toggles = [new ActiveToggle(toggleContent, "surge", 3)] },
        }));

        var lowered = LowerFor(Row("0.6.0"), hero, toggle);

        var change = Assert.Single(lowered.Changes, c => c.Kind == "character");
        Assert.Equal("'Compat Hero': the rounds left on 'Surge of Tides'.", change.Message);
    }

    private static Guid PublishFlyFeature(TempApp app)
    {
        var source = app.App.CreateHomebrewSource(new("Compat Homebrew A", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        app.App.SaveDraft(V11Feature() with { Provenance = new(source.Id), Status = RevisionStatus.Draft });
        app.App.Publish(FeatureRef);
        return source.Id;
    }

    [Theory]
    [InlineData("0.6.0")]
    [InlineData("0.5.0")]
    public void Lowered_plan_that_is_not_blocked_opens_in_the_target(string target)
    {
        using var app = new TempApp();
        PublishFlyFeature(app);
        var id = Save(app, c => c with { Pins = [.. c.Pins, FeatureRef] });
        var dispatcher = new CommandDispatcher(app.App);

        var preview = Result(Send(dispatcher, "package.exportPreview", Payload(id, target)));

        Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => c.GetProperty("kind").GetString() == "content");
        Assert.Equal(target, preview.GetProperty("opensIn").GetString());
    }

    [Fact]
    public void Content_copies_are_written_and_the_same_input_gives_identical_bytes()
    {
        using var app = new TempApp();
        PublishFlyFeature(app);
        var id = Save(app, c => c with { Pins = [.. c.Pins, FeatureRef] });
        var dispatcher = new CommandDispatcher(app.App);
        var hash = Result(Send(dispatcher, "package.exportPreview", Payload(id, "0.6.0"))).GetProperty("changesHash").GetString();
        var before = Snapshot(app);

        var first = ExportBytes(Send(dispatcher, "package.export", Payload(id, "0.6.0", hash)));
        var second = ExportBytes(Send(dispatcher, "package.export", Payload(id, "0.6.0", hash)));

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(first));
        Assert.Null(zip.GetEntry($"content/{FeatureRevision:D}.json"));
        var copyEntry = Assert.Single(zip.Entries, e => e.FullName.StartsWith("content/", StringComparison.Ordinal) && e.FullName != $"content/{FeatureRevision:D}.json"
            && Entry(first, e.FullName).TryGetProperty("downConvertedFrom", out _));
        Assert.Equal(first, second);
        Assert.Contains(copyEntry.FullName[8..^5], Entry(first, $"characters/{id:D}.json").ToString(), StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(app));
    }

    // ---- Task 5.7: plain gear the target lacks becomes a custom item (Q3c, D98) ----------------------------------------------

    private static ContentRevision GearItem(int n, string name, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"c1000000-0000-4000-8000-{n:D12}"),
        RevisionId = Guid.Parse($"c100e000-0000-4000-8000-{n:D12}"),
        SchemaVersion = ContentRevision.CurrentSchemaVersion,
        Kind = ContentKind.Item,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd51],
        Provenance = new(BundledSourceId),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static EquipmentEntry Held(ContentRevision item, int quantity = 1, bool equipped = false, Guid? id = null) =>
        new(new ContentReference(item.ContentId, item.RevisionId), equipped, quantity, id ?? Guid.NewGuid());

    private static LoweredPlan LowerGear(Character hero, string release, HashSet<Guid>? inSet, params ContentRevision[] revisions) =>
        DownConversion.Lower(new([hero], [.. revisions], [], [], "x.tomestack.zip"), Row(release) with { BundledRevisionIds = inSet ?? [] },
            id => id == BundledSourceId, _ => "Compat Bundled Pack", new HomebrewCatalog());

    [Fact]
    public void Gear_outside_the_set_becomes_a_custom_item_with_its_name_and_quantity()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var entryId = Guid.Parse("c1000000-0000-4000-8000-00000000ee01");
        var hero = Hero(c => c with { Equipment = [Held(rope, 3, equipped: true, id: entryId)] });

        var lowered = LowerGear(hero, "0.6.0", null, rope);

        Assert.False(lowered.Blocked);
        var item = Assert.Single(lowered.Plan.Characters[0].CustomItems!);
        Assert.Equal(new CustomItem(DownConversion.Uuid5(DownConversion.Namespace, $"gear:{entryId:D}"), "Compat Rope Coil", 3, null, false), item);
        Assert.Empty(lowered.Plan.Characters[0].Equipment);
        Assert.Empty(lowered.Plan.Revisions);
        var change = Assert.Single(lowered.Changes);
        Assert.Equal(("dropped", DownConversion.DropsCode, "character"), (change.Action, change.Code, change.Kind));
        Assert.Contains("'Compat Rope Coil'", change.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pack_with_contents_becomes_one_custom_item_named_after_the_pack()
    {
        var pack = GearItem(2, "Compat Wayfarer Pack", new ItemEffect { Id = "item", Contains = [new PackLine(new GuideTarget(Guid.NewGuid()), 10)] });
        var hero = Hero(c => c with { Equipment = [Held(pack)] });

        var lowered = LowerGear(hero, "0.6.0", null, pack);

        Assert.False(lowered.Blocked);
        var item = Assert.Single(lowered.Plan.Characters[0].CustomItems!);
        Assert.Equal(("Compat Wayfarer Pack", 1), (item.Name, item.Quantity));
    }

    [Fact]
    public void An_item_in_the_set_is_left_alone()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var hero = Hero(c => c with { Equipment = [Held(rope)] });

        var lowered = LowerGear(hero, "0.6.0", [rope.RevisionId], rope);

        Assert.False(lowered.Blocked);
        Assert.Empty(lowered.Changes);
        Assert.Single(lowered.Plan.Characters[0].Equipment);
        Assert.Null(lowered.Plan.Characters[0].CustomItems);
    }

    [Fact]
    public void A_weapon_a_charged_item_and_an_attuned_item_outside_the_set_still_block()
    {
        var sword = GearItem(3, "Compat Blade", new WeaponEffect { Id = "weapon", WeaponKey = "compat-blade", Category = WeaponCategory.Simple, Attack = WeaponAttack.Melee, Damage = "1d6", DamageType = "slashing" });
        var wand = GearItem(4, "Compat Wand", new ItemEffect { Id = "item", Charges = new ItemCharges { Max = 3 } });
        var ring = GearItem(5, "Compat Ring", new ItemEffect { Id = "item", RequiresAttunement = true });
        foreach (var item in new[] { sword, wand, ring })
        {
            var lowered = LowerGear(Hero(c => c with { Equipment = [Held(item)] }), "0.6.0", null, item);

            Assert.True(lowered.Blocked, item.Name);
            Assert.Null(lowered.Plan.Characters[0].CustomItems);
            Assert.All(lowered.Changes, c => Assert.Equal("blocks", c.Action));
        }
    }

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void The_custom_item_limit_counts_existing_and_converted_items(int total, bool passes)
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var existing = Enumerable.Range(0, total - 1).Select(i => new CustomItem(Guid.NewGuid(), $"Compat Thing {i}", 1, null, false)).ToList();
        var hero = Hero(c => c with { Equipment = [Held(rope)], CustomItems = existing });

        var lowered = LowerGear(hero, "0.6.0", null, rope);

        Assert.Equal(!passes, lowered.Blocked);
        if (passes)
            Assert.Equal(total, lowered.Plan.Characters[0].CustomItems!.Count);
        else
            Assert.Contains(lowered.Changes, c => c.Action == "blocks" && c.Kind == "character" && c.Message.Contains("a character can keep 50", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowering_twice_gives_the_same_custom_item_and_the_same_list()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var hero = Hero(c => c with { Equipment = [Held(rope, 2)] });

        var first = LowerGear(hero, "0.6.0", null, rope);
        var second = LowerGear(hero, "0.6.0", null, rope);

        Assert.Equal(first.Plan.Characters[0].CustomItems, second.Plan.Characters[0].CustomItems);
        Assert.Equal(first.ChangesHash, second.ChangesHash);
    }

    [Fact]
    public void A_custom_item_id_that_is_already_taken_gets_another_id()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var entryId = Guid.Parse("c1000000-0000-4000-8000-00000000ee02");
        var taken = DownConversion.Uuid5(DownConversion.Namespace, $"gear:{entryId:D}");
        var hero = Hero(c => c with { Equipment = [Held(rope, id: entryId)], CustomItems = [new CustomItem(taken, "Compat Thing", 1, null, false)] });

        var lowered = LowerGear(hero, "0.6.0", null, rope);

        Assert.False(lowered.Blocked);
        var items = lowered.Plan.Characters[0].CustomItems!;
        Assert.Equal(2, items.Count);
        Assert.Equal(2, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void Gear_that_is_also_granted_by_written_content_still_blocks()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var granting = CompatGranting(Guid.Parse("c100e000-0000-4000-8000-0000000000d1"), "Compat Granter", new ContentReference(rope.ContentId, rope.RevisionId), blocker: false) with { Provenance = new(HomebrewId) };
        var hero = Hero(c => c with { Equipment = [Held(rope)] });

        var lowered = LowerGear(hero, "0.6.0", null, rope, granting);

        Assert.True(lowered.Blocked);
    }

    [Fact]
    public void Gear_held_as_a_mod_by_a_kept_entry_stays_blocking()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var kept = GearItem(6, "Compat Frame");
        var ropeRef = new ContentReference(rope.ContentId, rope.RevisionId);
        var keeper = new EquipmentEntry(new ContentReference(kept.ContentId, kept.RevisionId), false, 1, Guid.NewGuid(), Mods: [ropeRef]);
        var hero = Hero(c => c with { Equipment = [Held(rope), keeper] });

        var lowered = LowerGear(hero, "0.6.0", [kept.RevisionId], rope, kept);

        // At this target (character schema below 11) mods do not survive lowering anyway, so this only shows the item is not converted.
        // The schema-11 test below covers the case the fix exists for: mods that survive.
        Assert.True(lowered.Blocked);
        Assert.Contains(lowered.Changes, c => c.Action == "blocks" && c.Message.Contains("Compat Rope Coil", StringComparison.Ordinal));
    }

    [Fact]
    public void Gear_held_as_a_mod_by_a_kept_entry_stays_blocking_where_mods_survive()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var kept = GearItem(6, "Compat Frame");
        var ropeRef = new ContentReference(rope.ContentId, rope.RevisionId);
        var keeper = new EquipmentEntry(new ContentReference(kept.ContentId, kept.RevisionId), false, 1, Guid.NewGuid(), Mods: [ropeRef]);
        var hero = Hero(c => c with { Equipment = [Held(rope), keeper] });
        var row = Row("0.6.0") with { CharacterSchema = 11, ContentSchema = 12, BundledRevisionIds = new HashSet<Guid> { kept.RevisionId } };

        var lowered = DownConversion.Lower(new([hero], [rope, kept], [], [], "x.tomestack.zip"), row,
            id => id == BundledSourceId, _ => "Compat Bundled Pack", new HomebrewCatalog());

        // The rope's content stays blocked: the kept entry's mod still points at its revision, which the file would not carry, so nothing is written.
        Assert.True(lowered.Blocked);
        Assert.Contains(lowered.Changes, c => c.Action == "blocks" && c.Message.Contains("Compat Rope Coil", StringComparison.Ordinal));
        Assert.Contains(lowered.Changes, c => c.Action == "blocks" && c.Kind == "content" && c.Name == "Compat Rope Coil");
    }

    [Fact]
    public void A_blocked_preview_lists_a_held_rope_once_as_a_custom_item_when_something_else_blocks()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var sword = GearItem(3, "Compat Blade", new WeaponEffect { Id = "weapon", WeaponKey = "compat-blade", Category = WeaponCategory.Simple, Attack = WeaponAttack.Melee, Damage = "1d6", DamageType = "slashing" });
        var hero = Hero(c => c with { Equipment = [Held(rope), Held(sword)] });

        var lowered = LowerGear(hero, "0.6.0", null, rope, sword);

        Assert.True(lowered.Blocked);
        var line = Assert.Single(lowered.Changes, c => c.Message.Contains("Compat Rope Coil", StringComparison.Ordinal) || c.Name == "Compat Rope Coil");
        Assert.Equal("dropped", line.Action);
    }

    [Fact]
    public void A_blocked_preview_lists_a_held_rope_once_as_blocking_when_the_rope_itself_blocks()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var kept = GearItem(6, "Compat Frame");
        var ropeRef = new ContentReference(rope.ContentId, rope.RevisionId);
        var keeper = new EquipmentEntry(new ContentReference(kept.ContentId, kept.RevisionId), false, 1, Guid.NewGuid(), Mods: [ropeRef]);
        var hero = Hero(c => c with { Equipment = [Held(rope), keeper] });
        var row = Row("0.6.0") with { CharacterSchema = 11, ContentSchema = 12, BundledRevisionIds = new HashSet<Guid> { kept.RevisionId } };

        var lowered = DownConversion.Lower(new([hero], [rope, kept], [], [], "x.tomestack.zip"), row,
            id => id == BundledSourceId, _ => "Compat Bundled Pack", new HomebrewCatalog());

        Assert.True(lowered.Blocked);
        var line = Assert.Single(lowered.Changes, c => c.Message.Contains("Compat Rope Coil", StringComparison.Ordinal) || c.Name == "Compat Rope Coil");
        Assert.Equal("blocks", line.Action);
    }

    [Fact]
    public void A_blocked_preview_lists_an_item_converted_in_one_character_and_held_in_another_once()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var ropeRef = new ContentReference(rope.ContentId, rope.RevisionId);
        var first = Hero(c => c with { Equipment = [Held(rope)] });
        var second = Hero(c => c with { Id = Guid.NewGuid(), Name = "Second Hero", Equipment = [new EquipmentEntry(ropeRef, Attuned: true)] });

        var lowered = DownConversion.Lower(new([first, second], [rope], [], [], "x.tomestack.zip"), Row("0.6.0") with { BundledRevisionIds = new HashSet<Guid>() },
            id => id == BundledSourceId, _ => "Compat Bundled Pack", new HomebrewCatalog());

        Assert.True(lowered.Blocked);
        Assert.DoesNotContain(lowered.Changes, c => c.Action == "dropped" && c.Message.Contains("becomes a custom item", StringComparison.Ordinal));
    }

    [Fact]
    public void A_blank_content_name_is_not_converted_and_blocks()
    {
        var blank = GearItem(7, "   ");
        var hero = Hero(c => c with { Equipment = [Held(blank)] });

        var lowered = LowerGear(hero, "0.6.0", null, blank);

        Assert.True(lowered.Blocked);
        Assert.Null(lowered.Plan.Characters[0].CustomItems);
        Assert.Single(lowered.Plan.Characters[0].Equipment);
        Assert.Contains(lowered.Changes, c => c.Action == "blocks" && c.Message.Contains("cannot be kept as a custom item", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_entries_without_an_id_for_one_item_get_two_stable_ids()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var ropeRef = new ContentReference(rope.ContentId, rope.RevisionId);
        var hero = Hero(c => c with { Equipment = [new EquipmentEntry(ropeRef, false, 1, null), new EquipmentEntry(ropeRef, false, 2, null)] });

        var first = LowerGear(hero, "0.6.0", null, rope).Plan.Characters[0].CustomItems!;
        var second = LowerGear(hero, "0.6.0", null, rope).Plan.Characters[0].CustomItems!;

        Assert.Equal(2, first.Select(i => i.Id).Distinct().Count());
        Assert.Equal(first, second);
    }

    [Fact]
    public void An_unconverted_entry_of_the_same_item_in_another_character_keeps_the_block()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var ropeRef = new ContentReference(rope.ContentId, rope.RevisionId);
        var first = Hero(c => c with { Equipment = [Held(rope)] });
        var second = Hero(c => c with { Id = Guid.NewGuid(), Equipment = [new EquipmentEntry(ropeRef, Attuned: true)] });

        var lowered = DownConversion.Lower(new([first, second], [rope], [], [], "x.tomestack.zip"), Row("0.6.0") with { BundledRevisionIds = new HashSet<Guid>() },
            id => id == BundledSourceId, _ => "Compat Bundled Pack", new HomebrewCatalog());

        Assert.True(lowered.Blocked);
    }

    [Fact]
    public void A_target_below_character_schema_9_still_blocks_plain_gear()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var hero = Hero(c => c with { Equipment = [Held(rope)] });

        var lowered = LowerGear(hero, "0.5.0", null, rope);

        Assert.True(Row("0.5.0").CharacterSchema < Character.DetailsSchemaVersion);
        Assert.True(lowered.Blocked);
        Assert.Null(lowered.Plan.Characters[0].CustomItems);
    }

    [Theory]
    [InlineData("attuned")]
    [InlineData("charges-spent")]
    [InlineData("item-charges")]
    [InlineData("item-attunement")]
    public void Entry_state_and_item_effects_each_keep_the_block(string which)
    {
        var plain = GearItem(1, "Compat Rope Coil");
        var item = which switch
        {
            "item-charges" => GearItem(4, "Compat Wand", new ItemEffect { Id = "item", Charges = new ItemCharges { Max = 3 } }),
            "item-attunement" => GearItem(5, "Compat Ring", new ItemEffect { Id = "item", RequiresAttunement = true }),
            _ => plain,
        };
        var entry = which switch
        {
            "attuned" => Held(item) with { Attuned = true },
            "charges-spent" => Held(item) with { ChargesSpent = 1 },
            _ => Held(item),
        };

        var lowered = LowerGear(Hero(c => c with { Equipment = [entry] }), "0.6.0", null, item);

        Assert.True(lowered.Blocked);
        Assert.Null(lowered.Plan.Characters[0].CustomItems);
        Assert.Single(lowered.Plan.Characters[0].Equipment);
    }

    [Fact]
    public void A_quantity_a_custom_item_cannot_hold_blocks_and_is_not_changed()
    {
        var rope = GearItem(1, "Compat Rope Coil");
        var hero = Hero(c => c with { Equipment = [Held(rope, EquipmentEntry.MaxQuantity + 1)] });

        var lowered = LowerGear(hero, "0.6.0", null, rope);

        Assert.True(lowered.Blocked);
        Assert.Null(lowered.Plan.Characters[0].CustomItems);
        Assert.Equal(EquipmentEntry.MaxQuantity + 1, Assert.Single(lowered.Plan.Characters[0].Equipment).Quantity);
    }

    [Fact]
    public void A_whole_export_writes_the_custom_item_and_no_equipment_entry_or_pin_for_the_converted_gear()
    {
        using var app = new TempApp();
        var rope = GearItem(1, "Compat Rope Coil");
        app.App.Store.UpsertSource(new SourceRecord { Id = BundledSourceId, Title = "Compat Bundled Pack", Publisher = "Compat", RulesFamilies = [RulesFamilies.Srd51], EditionVersion = "1", License = "test", Redistributable = true });
        app.App.Store.AddRevision(rope);
        var id = Save(app, c => c with { Equipment = [Held(rope, 2)] });
        var packages = new PackageService(app.App.Store, TimeProvider.System, Path.Combine(app.App.DataDirectory, PackageService.BackupFolderName));
        packages.SetBundledSources(new HashSet<Guid> { BundledSourceId });
        var row = Row("0.6.0") with { BundledRevisionIds = new HashSet<Guid>() };

        var preview = packages.PreviewExport([id], ExportPurpose.Share, row);
        var bytes = packages.Export([id], ExportPurpose.Share, row, preview.ChangesHash).Content;

        var character = Entry(bytes, $"characters/{id:D}.json");
        var custom = Assert.Single(character.GetProperty("customItems").EnumerateArray());
        Assert.Equal("Compat Rope Coil", custom.GetProperty("name").GetString());
        Assert.Equal(2, custom.GetProperty("quantity").GetInt32());
        Assert.True(!character.TryGetProperty("equipment", out var equipment) || equipment.GetArrayLength() == 0);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            Assert.DoesNotContain(rope.RevisionId.ToString("D"), reader.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- C2-5: the way back (guarded re-pin on import) ---------------------------------------------------------------------

    private const string RepinnedSentence = "'Compat Steady Hands': re-pinned to your original revision (the file held a copy made for 0.6.0).";

    private static (SourceRecord Source, ContentReference Draft, ContentReference Published) PublishFlyOriginal(TempApp app)
    {
        var source = app.App.CreateHomebrewSource(new("Compat Homebrew A", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        var draft = app.App.SaveDraft(V11Feature(Guid.NewGuid()) with { Provenance = new(source.Id), Status = RevisionStatus.Draft });
        return (source, draft, app.App.Publish(draft).Published);
    }

    private static ContentReference PublishPathGranting(TempApp app, SourceRecord source, ContentReference fly)
    {
        var grant = new GrantEffect { Id = "grant-hands", Grant = GrantKind.Content, Content = fly };
        var draft = app.App.SaveDraft(V9Path(PathContent, Guid.NewGuid(), grant) with { Kind = ContentKind.Feat, Provenance = new(source.Id), Status = RevisionStatus.Draft });
        return app.App.Publish(draft).Published;
    }

    private static byte[] ExportFor(TomeStackApp app, Guid characterId, string target = "0.6.0")
    {
        var row = Row(target);
        var preview = app.PreviewExport([characterId], ExportPurpose.Share, row);
        return app.ExportCharacters([characterId], ExportPurpose.Share, row, preview.ChangesHash).Content;
    }

    private static void ReplaceText(System.Text.Json.Nodes.JsonNode node, Guid from, Guid to)
    {
        var target = node.AsObject();
        var swapped = System.Text.Json.Nodes.JsonNode.Parse(target.ToJsonString().Replace(from.ToString("D"), to.ToString("D"), StringComparison.OrdinalIgnoreCase))!.AsObject();
        target.Clear();
        foreach (var (key, value) in swapped.ToList())
        {
            swapped.Remove(key);
            target[key] = value;
        }
    }

    /// <summary>Edits the copy named <paramref name="copyName"/>; with <paramref name="newRoot"/> its marker names that revision and its id is recomputed honestly (so the id check passes) and the characters follow.</summary>
    private static byte[] ForgeCopy(byte[] bytes, string copyName, Action<System.Text.Json.Nodes.JsonNode> edit, Guid? newRoot = null)
    {
        string copyPath;
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes)))
        {
            copyPath = zip.Entries.Single(e => e.FullName.StartsWith("content/", StringComparison.Ordinal)
                && Entry(bytes, e.FullName) is var entry && entry.TryGetProperty("downConvertedFrom", out _) && entry.GetProperty("name").GetString() == copyName).FullName;
        }
        var oldId = Guid.Parse(copyPath["content/".Length..^".json".Length]);
        var newId = oldId;
        var edited = PackageEditor.Edit(bytes, p => p == copyPath, node =>
        {
            edit(node);
            if (newRoot is not { } root)
                return;
            node["downConvertedFrom"]!["revisionId"] = root.ToString("D");
            var revision = node.Deserialize<ContentRevision>(RulesJson.Options)!;
            newId = DownConversion.CopyId(root, revision.SchemaVersion, DownConversion.CanonicalBytes(revision));
            node["revisionId"] = newId.ToString("D");
        });
        if (newId == oldId)
            return edited;
        var followed = PackageEditor.Edit(edited, p => p.StartsWith("characters/", StringComparison.Ordinal), node => ReplaceText(node, oldId, newId));
        return Rename(followed, copyPath, $"content/{newId:D}.json");
    }

    /// <summary>The package with one entry under another path (the parser expects an entry to be named by its id).</summary>
    private static byte[] Rename(byte[] package, string from, string to)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(package), System.IO.Compression.ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                using var buffer = new MemoryStream();
                using (var stream = entry.Open())
                    stream.CopyTo(buffer);
                files[entry.FullName == from ? to : entry.FullName] = buffer.ToArray();
            }
        }
        var manifest = JsonNode.Parse(files["manifest.json"])!;
        manifest["entries"]!.AsArray().Single(e => (string?)e!["path"] == from)!["path"] = to;
        files["manifest.json"] = System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString(RulesJson.Options) + "\n");
        using var output = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in files)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(bytes);
            }
        }
        return output.ToArray();
    }

    [Fact]
    public void Import_repins_a_verified_copy_to_the_original()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);
        var stored = app.App.Store.ListRevisions().Count;

        var preview = app.App.PreviewImport(bytes);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        var repinned = Assert.Single(preview.Repinned!);
        Assert.Equal((fly.RevisionId, "Compat Steady Hands"), (repinned.OriginalId, repinned.Name));
        Assert.NotEqual(fly.RevisionId, repinned.CopyId);
        var warning = Assert.Single(preview.Warnings, w => w.Code == "package.down-converted-repinned");
        Assert.Equal(RepinnedSentence, warning.Message);
        Assert.DoesNotContain(preview.Items, i => i.Kind == "contentRevision" && i.Name == "Compat Steady Hands");

        app.App.ApplyImport(bytes);

        Assert.Equal(stored, app.App.Store.ListRevisions().Count);
        var pins = app.App.Store.FindCharacter(id)!.Pins;
        Assert.Contains(fly, pins);
        Assert.DoesNotContain(pins, p => p.RevisionId == repinned.CopyId);
        AssertNoPublishedRevisionCarriesMarker(app);
    }

    [Fact]
    public void Import_runs_repin_before_pin_missing_and_own_source()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);

        var preview = app.App.PreviewImport(bytes);

        Assert.DoesNotContain(preview.Errors, e => e.Code is "package.own-source" or "package.pin-missing");
        Assert.Empty(preview.Errors);
        app.App.ApplyImport(bytes);
        Assert.Contains(fly, app.App.Store.FindCharacter(id)!.Pins);
        AssertNoPublishedRevisionCarriesMarker(app);
    }

    [Fact]
    public void Import_repins_a_cascade_closure()
    {
        using var app = new TempApp();
        var (source, _, fly) = PublishFlyOriginal(app);
        var path = PublishPathGranting(app, source, fly);
        var id = Save(app, c => c with { Pins = [.. c.Pins, path] });
        var bytes = ExportFor(app.App, id);
        var stored = app.App.Store.ListRevisions().Count;

        var preview = app.App.PreviewImport(bytes);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.Equal(new HashSet<Guid> { fly.RevisionId, path.RevisionId }, preview.Repinned!.Select(r => r.OriginalId).ToHashSet());
        app.App.ApplyImport(bytes);
        Assert.Equal(stored, app.App.Store.ListRevisions().Count);
        Assert.Contains(path, app.App.Store.FindCharacter(id)!.Pins);
        AssertNoPublishedRevisionCarriesMarker(app);
    }

    /// <summary>A file with copy C (of the fly feature) and an ordinary revision H that grants C: the path copy with its marker removed and its source made foreign.</summary>
    private static (byte[] Bytes, Guid CopyId, Guid HId) FileWithKeptReferrer(TempApp origin, out Guid characterId)
    {
        var (source, _, fly) = PublishFlyOriginal(origin);
        var path = PublishPathGranting(origin, source, fly);
        characterId = Save(origin, c => c with { Pins = [.. c.Pins, path] });
        var bytes = ExportFor(origin.App, characterId);
        var flyCopy = Guid.Empty;
        var hId = Guid.Empty;
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes)))
        {
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("content/", StringComparison.Ordinal)))
            {
                var json = Entry(bytes, entry.FullName);
                var id = Guid.Parse(entry.FullName["content/".Length..^".json".Length]);
                var name = json.GetProperty("name").GetString();
                if (name == "Compat Steady Hands")
                    flyCopy = id;
                else if (name == "Compat Path of Tides")
                    hId = id;
            }
        }
        var other = ForgeCopyByKeep(bytes, hId);
        return (other, flyCopy, hId);
    }

    /// <summary>Strips the marker from the revision stored under <paramref name="id"/>: an ordinary revision.</summary>
    private static byte[] ForgeCopyByKeep(byte[] bytes, Guid id) =>
        PackageEditor.Edit(bytes, p => p == $"content/{id:D}.json", node => node.AsObject().Remove("downConvertedFrom"));

    [Fact]
    public void Repin_never_rewrites_a_kept_revision()
    {
        using var origin = new TempApp();
        var (bytes, copyId, hId) = FileWithKeptReferrer(origin, out var characterId);
        using var app = new TempApp();
        app.App.ApplyImport(origin.App.ExportCharacters([characterId], ExportPurpose.Share).Content); // the original is stored here
        var fileJson = Entry(bytes, $"content/{hId:D}.json").ToString();

        var preview = app.App.PreviewImport(bytes);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.Empty(preview.Repinned ?? []);
        Assert.Contains(preview.Warnings, w => w.Code == "package.down-converted");
        Assert.DoesNotContain(preview.Warnings, w => w.Code == "package.down-converted-repinned");
        Assert.Contains(preview.Items, i => i.Kind == "contentRevision" && i.Id == copyId);
        Assert.Contains(preview.Items, i => i.Kind == "contentRevision" && i.Id == hId);

        app.App.ApplyImport(bytes);

        var stored = app.App.Store.ListRevisions().Single(r => r.RevisionId == hId);
        Assert.Equal(fileJson, System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(stored, RulesJson.Options))!.ToString());
        Assert.NotNull(app.App.Store.ListRevisions().SingleOrDefault(r => r.RevisionId == copyId));
    }

    [Fact]
    public void Reimport_with_a_stored_referrer_still_applies()
    {
        using var origin = new TempApp();
        var (bytes, _, _) = FileWithKeptReferrer(origin, out var characterId);
        using var fresh = new TempApp();
        fresh.App.ApplyImport(bytes); // C and H arrive first, without the original

        var plain = origin.App.ExportCharacters([characterId], ExportPurpose.Share).Content; // then the original arrives
        fresh.App.ApplyImport(plain);

        var again = fresh.App.PreviewImport(bytes);
        Assert.DoesNotContain(again.Errors, e => e.Code == "package.revision-conflict");
        Assert.True(again.CanApply, string.Join("; ", again.Errors.Select(e => e.Message)));
        fresh.App.ApplyImport(bytes);
    }

    [Fact]
    public void Import_adds_a_copy_whose_original_is_absent_with_warning()
    {
        using var origin = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(origin);
        var id = Save(origin, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(origin.App, id);
        using var fresh = new TempApp();

        var preview = fresh.App.PreviewImport(bytes);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.Empty(preview.Repinned ?? []);
        var warning = Assert.Single(preview.Warnings, w => w.Code == "package.down-converted");
        Assert.Equal($"'Compat Steady Hands' in this file was made for TomeStack 0.6.0 by {preview.Manifest!.AppVersion} and imports as it is (no original here matches it); the original has more.", warning.Message);
        var item = Assert.Single(preview.Items, i => i.Kind == "contentRevision" && i.Name == "Compat Steady Hands");
        Assert.Equal(PackageItemAction.Add, item.Action);
        fresh.App.ApplyImport(bytes);
        Assert.NotNull(fresh.App.Store.FindRevision(new ContentReference(FeatureContent, item.Id)));
    }

    [Theory]
    [InlineData("name-changed", "package.marker-ignored")]
    [InlineData("other-content", "package.down-converted")]
    [InlineData("bundled", "package.down-converted")]
    [InlineData("draft", "package.down-converted")]
    public void Import_ignores_a_forged_marker(string kind, string expected)
    {
        using var app = new TempApp();
        var (source, draft, fly) = PublishFlyOriginal(app);
        var otherDraft = app.App.SaveDraft(V11Feature(Guid.NewGuid()) with { ContentId = Guid.NewGuid(), Name = "Compat Other", Provenance = new(source.Id), Status = RevisionStatus.Draft });
        var other = app.App.Publish(otherDraft).Published;
        var bundled = TomeStackApp.BundledPacks.SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).First().RevisionId;
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);
        Guid? root = kind switch { "other-content" => other.RevisionId, "bundled" => bundled, "draft" => draft.RevisionId, _ => null };

        var forged = ForgeCopy(bytes, "Compat Steady Hands", node =>
        {
            if (root is null)
                node["name"] = "Compat Steady Hands X";
        }, root);
        var preview = app.App.PreviewImport(forged);

        Assert.Empty(preview.Repinned ?? []);
        Assert.Contains(preview.Warnings, w => w.Code == expected);
        Assert.DoesNotContain(preview.Warnings, w => w.Code == (expected == "package.down-converted" ? "package.marker-ignored" : "package.down-converted"));
        Assert.DoesNotContain(preview.Warnings, w => w.Code == "package.down-converted-repinned");
        if (expected == "package.marker-ignored")
            Assert.Equal("'Compat Steady Hands X': the note saying which revision it was copied from does not match its content, so it was ignored.", preview.Warnings.Single(w => w.Code == expected).Message);
        var item = Assert.Single(preview.Items, i => i.Kind == "contentRevision" && i.Name.StartsWith("Compat Steady Hands", StringComparison.Ordinal));
        Assert.Equal(PackageItemAction.Add, item.Action); // an ordinary revision: today's rules (here the own-source refusal) apply to it
        Assert.Contains(preview.Errors, e => e.Code == "package.own-source");
    }

    // ---- C2-8: input bounds (a malformed marker never throws) -----------------------------------------------------------------

    [Theory]
    [InlineData("\"x\"")]
    [InlineData("{\"revisionId\":\"not-a-guid\"}")]
    [InlineData("{\"revisionId\":\"{GUID}\",\"schemaVersion\":0}")]
    [InlineData("{\"revisionId\":\"{GUID}\",\"schemaVersion\":99}")]
    [InlineData("99")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"revisionId\":\"{GUID}\",\"schemaVersion\":10,\"extra\":1}")]
    [InlineData("LONG")]
    public void Malformed_markers_never_throw(string raw)
    {
        using var origin = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(origin);
        var id = Save(origin, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(origin.App, id);
        var json = raw == "LONG" ? $"\"{new string('a', 10_000)}\"" : raw.Replace("{GUID}", Guid.NewGuid().ToString("D"), StringComparison.Ordinal);
        var forged = ForgeCopy(bytes, "Compat Steady Hands", node => node.AsObject()["downConvertedFrom"] = JsonNode.Parse(json));
        using var fresh = new TempApp();

        var preview = fresh.App.PreviewImport(forged);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(preview.Errors, e => e.Code == "package.invalid-json");
        Assert.Contains(preview.Warnings, w => w.Code == "package.marker-ignored");
        Assert.Empty(preview.Repinned ?? []);
        fresh.App.ApplyImport(forged);
        var item = Assert.Single(preview.Items, i => i.Kind == "contentRevision" && i.Name == "Compat Steady Hands");
        Assert.NotNull(fresh.App.Store.FindRevision(new ContentReference(FeatureContent, item.Id)));
    }

    [Theory]
    [InlineData("\"abc\"", true)]
    [InlineData("LONG", true)]
    [InlineData("5", true)]
    [InlineData("true", true)]
    [InlineData("{}", true)]
    [InlineData("[]", true)]
    [InlineData("null", false)]
    public void Malformed_made_for_never_throws(string raw, bool warns)
    {
        using var origin = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(origin);
        var id = Save(origin, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(origin.App, id);
        var json = raw == "LONG" ? $"\"{new string('9', 200)}\"" : raw;
        var forged = PackageEditor.Edit(bytes, _ => false, _ => { }, manifest => manifest.AsObject()["madeFor"] = JsonNode.Parse(json));
        using var fresh = new TempApp();

        var preview = fresh.App.PreviewImport(forged);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(preview.Errors, e => e.Code == "package.invalid-json");
        Assert.Equal(warns, preview.Warnings.Any(w => w.Code == "package.marker-ignored"));
        Assert.Null(preview.Manifest!.MadeFor);
        fresh.App.ApplyImport(forged);
    }

    [Fact]
    public void Import_repins_a_closure_all_or_nothing()
    {
        using var app = new TempApp();
        var (source, _, fly) = PublishFlyOriginal(app);
        var path = PublishPathGranting(app, source, fly);
        var id = Save(app, c => c with { Pins = [.. c.Pins, path] });
        var bytes = ExportFor(app.App, id);
        // Z's original is absent here (its marker names a revision nobody has), its id still verifies; X is untouched.
        var forged = ForgeCopy(bytes, "Compat Path of Tides", _ => { }, Guid.NewGuid());

        var preview = app.App.PreviewImport(forged);

        Assert.Empty(preview.Repinned ?? []);
        // C2-Q5: one line for the file, with the count, not one per copy.
        var warning = Assert.Single(preview.Warnings, w => w.Code == "package.down-converted");
        Assert.Equal($"2 revisions in this file were made for TomeStack 0.6.0 by {preview.Manifest!.AppVersion} and import as they are (no original here matches them); the originals have more.", warning.Message);
        Assert.Null(warning.Content);
        Assert.Equal(2, preview.Items.Count(i => i.Kind == "contentRevision" && i.Action == PackageItemAction.Add));
        Assert.DoesNotContain(preview.Errors, e => e.Code is "validate.reference-missing" or "package.pin-missing");
    }

    private static Guid CopyIdOf(byte[] bytes, string copyName)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var path = zip.Entries.Single(e => e.FullName.StartsWith("content/", StringComparison.Ordinal)
            && Entry(bytes, e.FullName) is var entry && entry.TryGetProperty("downConvertedFrom", out _) && entry.GetProperty("name").GetString() == copyName).FullName;
        return Guid.Parse(path["content/".Length..^".json".Length]);
    }

    private static void AssertOrdinaryWithWarning(PackagePreview preview)
    {
        Assert.Empty(preview.Repinned ?? []);
        Assert.Single(preview.Warnings, w => w.Code == "package.down-converted");
        Assert.DoesNotContain(preview.Warnings, w => w.Code is "package.down-converted-repinned" or "package.marker-ignored");
        var item = Assert.Single(preview.Items, i => i.Kind == "contentRevision" && i.Name == "Compat Steady Hands");
        Assert.Equal(PackageItemAction.Add, item.Action);
    }

    [Fact]
    public void Import_does_not_repin_a_copy_whose_bytes_differ_from_the_relowered_original()
    {
        // The marker names the real published original and the id is recomputed honestly: only the re-lowering comparison can refuse it.
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);

        var forged = ForgeCopy(bytes, "Compat Steady Hands", node => node["effects"]![0]!["value"] = "2", fly.RevisionId);

        AssertOrdinaryWithWarning(app.App.PreviewImport(forged));
    }

    [Fact]
    public void Import_does_not_repin_to_an_original_in_another_source()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var elsewhere = app.App.CreateHomebrewSource(new("Compat Homebrew B", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        var twin = V11Feature(Guid.NewGuid()) with { Provenance = new(elsewhere.Id), Status = RevisionStatus.Published };
        app.App.Store.AddRevision(twin);
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);

        var forged = ForgeCopy(bytes, "Compat Steady Hands", _ => { }, twin.RevisionId);

        AssertOrdinaryWithWarning(app.App.PreviewImport(forged));
    }

    [Fact]
    public void Import_does_not_repin_to_a_bundled_original_even_when_the_lookup_succeeds()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var bundled = TomeStackApp.BundledPacks.SelectMany(p => TomeStackApp.LoadBundledPack(p).Revisions).First();
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);

        var forged = ForgeCopy(bytes, "Compat Steady Hands", node =>
        {
            node["contentId"] = bundled.ContentId.ToString("D");
            node["provenance"]!["sourceId"] = bundled.Provenance.SourceId.ToString("D");
        }, bundled.RevisionId);

        AssertOrdinaryWithWarning(app.App.PreviewImport(forged));
    }

    [Fact]
    public void Import_does_not_repin_when_the_marker_schema_is_not_the_originals()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);

        var forged = ForgeCopy(bytes, "Compat Steady Hands", node => node["downConvertedFrom"]!["schemaVersion"] = 9);

        AssertOrdinaryWithWarning(app.App.PreviewImport(forged));
    }

    [Fact]
    public void Import_repoints_a_choice_source_that_is_the_characters_only_mention()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);
        var copy = CopyIdOf(bytes, "Compat Steady Hands");
        var moved = PackageEditor.Edit(bytes, p => p.StartsWith("characters/", StringComparison.Ordinal), node =>
        {
            var pins = node["pins"]!.AsArray();
            foreach (var pin in pins.Where(p => (string?)p!["revisionId"] == copy.ToString("D")).ToList())
                pins.Remove(pin);
            node["choices"]!.AsArray().Add(JsonNode.Parse($$"""{"source":{"contentId":"{{fly.ContentId}}","revisionId":"{{copy}}"},"choiceId":"pick","selected":[]}"""));
        });

        var preview = app.App.PreviewImport(moved);

        Assert.Single(preview.Repinned!);
        app.App.ApplyImport(moved);
        var choice = Assert.Single(app.App.Store.FindCharacter(id)!.Choices, c => c.ChoiceId == "pick");
        Assert.Equal(fly, choice.Source);
    }

    [Fact]
    public void Import_handles_a_pin_to_both_copy_and_original()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });
        var bytes = ExportFor(app.App, id);
        var both = PackageEditor.Edit(bytes, p => p.StartsWith("characters/", StringComparison.Ordinal),
            node => node["pins"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse($$"""{"contentId":"{{fly.ContentId}}","revisionId":"{{fly.RevisionId}}"}""")));

        var preview = app.App.PreviewImport(both);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        app.App.ApplyImport(both);
        var pins = app.App.Store.FindCharacter(id)!.Pins;
        Assert.Single(pins, p => p == fly);
        Assert.Equal(pins.Count, pins.Distinct().Count());
        AssertNoPublishedRevisionCarriesMarker(app);
    }

    // ---- C2-6: marker strip, the newest rule (Q14), the character loss warning ----------------------------------------------

    /// <summary>After a re-pin nothing in the store is a stored copy: no published revision carries the marker.</summary>
    private static void AssertNoPublishedRevisionCarriesMarker(TempApp app) =>
        Assert.DoesNotContain(app.App.Store.ListRevisions(), r => r.Status == RevisionStatus.Published && r.Extensions?.ContainsKey(DownConversion.MarkerKey) == true);

    private static ContentRevision StoreCopyOf(TempApp app, ContentReference original)
    {
        var copy = LowerFor(Row("0.6.0"), Hero(c => c), app.App.Store.FindRevision(original)!).Plan.Revisions.Single();
        Assert.NotNull(DownConversion.ReadMarker(copy));
        app.App.Store.AddRevision(copy);
        return copy;
    }

    [Fact]
    public void Publish_and_saveDraft_strip_downConvertedFrom()
    {
        using var app = new TempApp();
        var source = app.App.CreateHomebrewSource(new("Compat Homebrew A", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        var marker = JsonDocument.Parse($$"""{"revisionId":"{{Guid.NewGuid():D}}","schemaVersion":11}""").RootElement.Clone();
        var note = JsonDocument.Parse("\"kept\"").RootElement.Clone();
        var draft = V11Feature(Guid.NewGuid()) with
        {
            Provenance = new(source.Id),
            Status = RevisionStatus.Draft,
            Extensions = new Dictionary<string, JsonElement> { [DownConversion.MarkerKey] = marker, ["compat-note"] = note },
        };

        var saved = app.App.SaveDraft(draft);

        var storedDraft = app.App.Store.FindRevision(saved)!;
        Assert.DoesNotContain(DownConversion.MarkerKey, storedDraft.Extensions!.Keys);
        Assert.Equal("kept", storedDraft.Extensions!["compat-note"].GetString());
        var published = app.App.Store.FindRevision(app.App.Publish(saved).Published)!;
        Assert.DoesNotContain(DownConversion.MarkerKey, published.Extensions!.Keys);
        Assert.Equal("kept", published.Extensions!["compat-note"].GetString());
        AssertNoPublishedRevisionCarriesMarker(app);
    }

    [Fact]
    public void Publish_strips_a_marker_a_draft_already_carries()
    {
        using var app = new TempApp();
        var source = app.App.CreateHomebrewSource(new("Compat Homebrew A", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        var marker = JsonDocument.Parse($$"""{"revisionId":"{{Guid.NewGuid():D}}","schemaVersion":11}""").RootElement.Clone();
        var draft = V11Feature(Guid.NewGuid()) with { Provenance = new(source.Id), Status = RevisionStatus.Draft, Extensions = new Dictionary<string, JsonElement> { [DownConversion.MarkerKey] = marker } };
        app.App.Store.AddRevision(draft);

        var published = app.App.Store.FindRevision(app.App.Publish(draft.Reference).Published)!;

        Assert.Null(published.Extensions);
    }

    [Fact]
    public void Copy_never_counts_as_newest_while_an_unmarked_revision_exists()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var copy = StoreCopyOf(app, fly);
        var id = Save(app, c => c with { Pins = [.. c.Pins, fly] });

        Assert.Empty(app.App.AvailableUpdates(id));
        var options = app.App.ListContent(RulesFamilies.Srd51).Where(o => o.Reference.ContentId == fly.ContentId).ToList();
        Assert.False(Assert.Single(options, o => o.Reference == fly).Superseded);
        Assert.True(Assert.Single(options, o => o.Reference.RevisionId == copy.RevisionId).Superseded);
    }

    [Fact]
    public void A_source_pack_whose_newest_is_a_copy_raises_no_newest_changes_from_it()
    {
        using var origin = new TempApp();
        var (source, _, fly) = PublishFlyOriginal(origin);
        var copy = StoreCopyOf(origin, fly);
        var pack = origin.App.ExportSourcePack([source.Id]).Content;

        using var destination = new TempApp();
        destination.App.Store.UpsertSource(origin.App.Store.FindSource(source.Id)! with { Origin = SourceOrigin.Received, ShareConfirmedAt = null });
        destination.App.Store.AddRevision(copy);

        Assert.DoesNotContain(destination.App.PreviewImport(pack).Warnings, w => w.Code == "pack.newest-changes");
    }

    [Fact]
    public void Newest_is_unchanged_for_a_store_without_copies()
    {
        using var app = new TempApp();
        var (source, _, first) = PublishFlyOriginal(app);
        var secondDraft = app.App.SaveDraft(V11Feature(Guid.NewGuid()) with { Provenance = new(source.Id), Status = RevisionStatus.Draft, Name = "Compat Steady Hands" });
        var second = app.App.Publish(secondDraft).Published;
        var id = Save(app, c => c with { Pins = [.. c.Pins, first] });

        var revisions = app.App.Store.ListRevisions(first.ContentId).Where(r => r.Status == RevisionStatus.Published).ToList();
        Assert.Equal(revisions[^1], DownConversion.Newest(revisions));
        Assert.Null(DownConversion.Newest([]));
        Assert.Equal(second, Assert.Single(app.App.AvailableUpdates(id)).To);
        var options = app.App.ListContent(RulesFamilies.Srd51).Where(o => o.Reference.ContentId == first.ContentId).ToList();
        Assert.True(Assert.Single(options, o => o.Reference == first).Superseded);
        Assert.False(Assert.Single(options, o => o.Reference == second).Superseded);
    }

    [Fact]
    public void Newest_of_only_copies_is_the_last_one_and_forged_markers_count_as_unmarked()
    {
        using var app = new TempApp();
        var (_, _, fly) = PublishFlyOriginal(app);
        var original = app.App.Store.FindRevision(fly)!;
        var copy = LowerFor(Row("0.6.0"), Hero(c => c), original).Plan.Revisions.Single();
        var secondCopy = copy with { RevisionId = Guid.NewGuid() }; // marker no longer verifies against its id
        var onlyCopies = new[] { copy with { RevisionId = DownConversion.CopyId(fly.RevisionId, copy.SchemaVersion, DownConversion.CanonicalBytes(copy)) } };

        Assert.Equal(onlyCopies[0], DownConversion.Newest(onlyCopies));
        Assert.Equal(secondCopy, DownConversion.Newest([original, secondCopy]));
        Assert.Equal(original, DownConversion.Newest([original, onlyCopies[0]]));
    }

    private static ContentReference PublishRoundsToggle(TempApp app, SourceRecord source)
    {
        var toggle = new ToggleEffect { Id = "surge-toggle", ToggleId = "surge", Label = "Surge of Tides" };
        var draft = app.App.SaveDraft(V9Path(Guid.Parse("c1000000-0000-4000-8000-0000000000c1"), Guid.NewGuid(), toggle) with { Kind = ContentKind.Feat, Provenance = new(source.Id), Status = RevisionStatus.Draft });
        return app.App.Publish(draft).Published;
    }

    private const string LossSentence = "'Compat Hero' in this file was made for TomeStack 0.6.0 and has less than your copy: the rounds left on 'Surge of Tides'. Importing replaces your copy; a backup is taken first.";

    [Fact]
    public void Import_warns_when_a_lowered_character_replaces_a_fuller_one()
    {
        using var app = new TempApp();
        var source = app.App.CreateHomebrewSource(new("Compat Homebrew A", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        var toggle = PublishRoundsToggle(app, source);
        var id = Save(app, c => WithDetails(c with
        {
            Pins = [.. c.Pins, toggle],
            Play = (c.Play ?? new PlayState()) with { Toggles = [new ActiveToggle(toggle.ContentId, "surge", 3)] },
        }));
        var bytes = ExportFor(app.App, id);

        var preview = app.App.PreviewImport(bytes);

        Assert.True(preview.CanApply, string.Join("; ", preview.Errors.Select(e => e.Message)));
        Assert.Equal(LossSentence, Assert.Single(preview.Warnings, w => w.Code == "package.down-converted-character").Message);
        Assert.Single(preview.Warnings, w => w.Code == "package.character-replace");
        var result = app.App.ApplyImport(bytes);
        Assert.NotNull(result.BackupFile);
    }

    [Fact]
    public void Loss_warning_says_an_older_TomeStack_when_the_file_does_not_say_for_which()
    {
        using var app = new TempApp();
        var source = app.App.CreateHomebrewSource(new("Compat Homebrew A", [RulesFamilies.Srd51], Redistributable: true, ConfirmOwnWork: true));
        var toggle = PublishRoundsToggle(app, source);
        var id = Save(app, c => WithDetails(c with
        {
            Pins = [.. c.Pins, toggle],
            Play = (c.Play ?? new PlayState()) with { Toggles = [new ActiveToggle(toggle.ContentId, "surge", 3)] },
        }));
        var bytes = PackageEditor.Edit(ExportFor(app.App, id), _ => false, _ => { }, manifest => manifest.AsObject().Remove("madeFor"));

        var warning = Assert.Single(app.App.PreviewImport(bytes).Warnings, w => w.Code == "package.down-converted-character");

        Assert.StartsWith("'Compat Hero' in this file was made for an older TomeStack and has less than your copy: ", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_loss_warning_when_the_local_copy_has_nothing_more()
    {
        using var app = new TempApp();
        var id = Save(app, c => WithDetails(c));
        var bytes = ExportFor(app.App, id);

        var preview = app.App.PreviewImport(bytes);

        Assert.Contains(preview.Warnings, w => w.Code == "package.character-replace");
        Assert.DoesNotContain(preview.Warnings, w => w.Code == "package.down-converted-character");
    }
}
