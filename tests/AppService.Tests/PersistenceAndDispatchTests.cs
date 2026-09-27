using System.Text.Json;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

public class PersistenceAndDispatchTests
{
    [Fact]
    public void Saved_character_survives_reopening_the_data_directory()
    {
        using var temp = new TempApp();
        var created = temp.App.CreateCharacter(new CreateCharacterRequest(
            "Reopen Test", RulesFamilies.Srd521, new AbilityScores(10, 12, 10, 10, 10, 10), null));

        temp.Reopen();
        var reloaded = temp.App.GetCharacter(created.Character.Id);

        Assert.Equal(TempApp.Json(created.Character), TempApp.Json(reloaded.Character));
        Assert.Equal(SqliteStore.LatestSchemaVersion, temp.App.GetInfo().SchemaVersion);
    }

    [Fact]
    public void Fresh_data_directory_is_seeded_with_fixture_content_for_both_rules_families()
    {
        using var temp = new TempApp();

        var srd51 = temp.App.ListContent(RulesFamilies.Srd51);

        Assert.Contains(srd51, o => o.Name == "Fixture Quickfoot" && o.Compatible);
        Assert.Contains(srd51, o => o.Name == "Fixture Courier" && !o.Compatible && o.SourceTitle == "TomeStack Fixtures: 2024 Family");
        Assert.DoesNotContain(srd51, o => o.Name == "Fixture Unreviewed Trick");
    }

    [Fact]
    public void Published_revisions_are_immutable()
    {
        using var temp = new TempApp();
        var original = temp.App.Store.ListRevisions().First(r => r.Status == RevisionStatus.Published);

        Assert.False(temp.App.Store.AddRevision(original));
        Assert.Throws<ImmutableRevisionException>(() => temp.App.Store.AddRevision(original with { Name = "Edited" }));
    }

    [Fact]
    public void Invalid_character_is_rejected_with_diagnostics()
    {
        using var temp = new TempApp();

        var ex = Assert.Throws<AppValidationException>(() => temp.App.CreateCharacter(new CreateCharacterRequest(
            " ", "srd-5.0", new AbilityScores(10, 40, 10, 10, 10, 10), null)));

        Assert.Equal(
            ["character.name-required", "character.rules-family-unknown", "character.ability-out-of-range"],
            ex.Problems.Select(p => p.Code));
    }

    [Fact]
    public void Dispatcher_creates_and_calculates_through_the_json_protocol()
    {
        using var temp = new TempApp();
        var dispatcher = new CommandDispatcher(temp.App);

        var response = JsonDocument.Parse(dispatcher.Dispatch("""
            {"id":"7","command":"character.create","payload":{
              "name":"Bridge Test","rulesFamily":"srd-5.1",
              "baseAbilities":{"str":10,"dex":15,"con":10,"int":10,"wis":10,"cha":10},
              "pins":[{"contentId":"5f0dc000-0000-4000-8000-000000000001","revisionId":"5f0de000-0000-4000-8000-000000000001"}]}}
            """)).RootElement;

        Assert.Equal("7", response.GetProperty("id").GetString());
        Assert.True(response.GetProperty("ok").GetBoolean());
        var initiative = response.GetProperty("result").GetProperty("sheet").GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("field").GetString() == "initiative");
        Assert.Equal(3, initiative.GetProperty("value").GetInt32());
        Assert.Equal("modifier", initiative.GetProperty("units").GetString());
        Assert.Equal("content", initiative.GetProperty("trace")[1].GetProperty("origin").GetProperty("kind").GetString());
    }

    [Fact]
    public void Unexpected_failure_returns_a_generic_message_and_correlation_id_and_logs_details_locally()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), devFixtures: true);
        var log = new RecordingErrorLog();
        var dispatcher = new CommandDispatcher(app, log);
        app.Dispose(); // Every store call now fails with an exception the dispatcher did not anticipate.

        var json = dispatcher.Dispatch("""{"id":"9","command":"character.list"}""");
        var error = JsonDocument.Parse(json).RootElement.GetProperty("error");

        Assert.Equal("internal", error.GetProperty("code").GetString());
        var correlationId = error.GetProperty("correlationId").GetString();
        Assert.Matches("^[0-9A-F]{8}$", correlationId);
        Assert.Contains(correlationId!, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(directory, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tomestack.db", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", json, StringComparison.Ordinal);
        var (loggedId, command, exception) = Assert.Single(log.Entries);
        Assert.Equal(correlationId, loggedId);
        Assert.Equal("character.list", command);
        Assert.NotNull(exception.StackTrace);
    }

    [Fact]
    public void File_error_log_writes_under_the_data_directory()
    {
        using var temp = new TempApp();
        var log = (FileErrorLog)temp.App.ErrorLog;

        log.Record("ABCD1234", "character.list", new InvalidOperationException("boom"));

        var text = File.ReadAllText(log.FilePath);
        Assert.StartsWith(temp.App.DataDirectory, log.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ABCD1234 command=character.list", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: boom", text, StringComparison.Ordinal);
    }

    private sealed class RecordingErrorLog : IErrorLog
    {
        public List<(string CorrelationId, string Command, Exception Exception)> Entries { get; } = [];

        public void Record(string correlationId, string command, Exception exception) => Entries.Add((correlationId, command, exception));
    }

    [Theory]
    [InlineData("""{"id":"1","command":"content.list","payload":{"rulesFamily":"5e"}}""", "validation")]
    [InlineData("""{"id":"1","command":"package.preview","payload":{"base64":"%%%"}}""", "bad-request")]
    [InlineData("""{"id":"1","payload":{}}""", "bad-request")]
    [InlineData("""{"id":"1","command":"shell.exec","payload":{}}""", "bad-request")]
    [InlineData("""{"id":"1","command":"character.get"}""", "bad-request")]
    [InlineData("not json", "bad-request")]
    [InlineData("""{"id":"1","command":"character.get","payload":{"id":"00000000-0000-0000-0000-000000000000"}}""", "validation")]
    public void Dispatcher_reports_errors_without_throwing(string request, string expectedCode)
    {
        using var temp = new TempApp();

        var response = JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(request)).RootElement;

        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(expectedCode, response.GetProperty("error").GetProperty("code").GetString());
    }
}
