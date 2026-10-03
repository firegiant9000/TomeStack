using System.Text.Json;
using TomeStack.AppService.CharacterImport;
using TomeStack.ImportWorker;
using TomeStack.ImportWorker.Forms;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>A form reader that returns a field list (or throws), and records what it was asked to read.</summary>
internal sealed class FakeFormReader(Func<IReadOnlyList<FormField>> read) : IFormReader
{
    public FakeFormReader(IReadOnlyList<FormField> fields) : this(() => fields)
    {
    }

    public List<string> Paths { get; } = [];

    /// <summary>Whether each file existed while it was read.</summary>
    public List<bool> Existed { get; } = [];

    public Task<IReadOnlyList<FormField>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        Paths.Add(path);
        Existed.Add(File.Exists(path));
        return Task.FromResult(read());
    }
}

internal sealed class MutableTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A host whose Open dialog returns a fixed path (or none).</summary>
internal sealed class OpenDialogHost(string? path) : IHostServices
{
    public List<(string Filter, string Extension)> Asked { get; } = [];

    public bool CanOpenFiles => true;

    public string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension) => null;

    public string? ChooseOpenFile(string filterDescription, string extension)
    {
        Asked.Add((filterDescription, extension));
        return path;
    }
}

/// <summary>
/// Character-sheet import S3 (<c>features/ddb-pdf-import.md</c> "Commands", "Not stored"): <c>ddb.read</c>,
/// <c>ddb.readData</c> and <c>ddb.discard</c>, the one-use tokens, and the temporary file. Nothing is written but that
/// file, which never outlives the command. Every value is invented.
/// </summary>
public class DdbSessionTests
{
    private static List<FormField> Sheet() => DdbParserTests.Fields(FixtureSheets.Fields2014);

    private static string TmpFolder(TempApp temp) => Path.Combine(temp.Directory, "tmp");

    private static JsonElement Dispatch(CommandDispatcher dispatcher, string command, object payload) =>
        JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

    [Fact]
    public void A_read_returns_a_token_the_layout_the_suggested_family_and_counts()
    {
        using var temp = new TempApp(formReader: new FakeFormReader(Sheet()));

        var read = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf");

        Assert.NotEqual(Guid.Empty, read.Token);
        Assert.Equal(("ddb-2014", RulesFamilies.Srd51), (read.Layout, read.SuggestedFamily));
        Assert.Equal(new DdbSummary("Testy McFixture", "Fixture Arcanist 3 / Fixture Chanter 2", 2, 3, 4), read.Summary);
        Assert.NotNull(temp.App.DdbSessions.Peek(read.Token));
    }

    [Fact]
    public void An_unknown_layout_is_refused_with_its_field_count_and_no_name()
    {
        using var temp = new TempApp(formReader: new FakeFormReader([new FormField("fixture.other.name", "text", 1, "Testy McFixture")]));

        var refused = Assert.Throws<AppValidationException>(() => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));

        Assert.Equal("ddb.layout-unknown", refused.Code);
        Assert.Contains("1 form field", refused.Problems.Single().Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fixture.other", refused.Problems.Single().Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Testy", refused.Problems.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("worker.timeout", "ddb.read-failed")]
    [InlineData("worker.crashed", "ddb.read-failed")]
    [InlineData("pdf.encrypted", "pdf.encrypted")]
    [InlineData("ddb.no-form-fields", "ddb.no-form-fields")]
    public void A_reader_failure_keeps_its_pdf_or_form_code_and_a_worker_failure_is_ddb_read_failed(string code, string expected)
    {
        using var temp = new TempApp(formReader: new FakeFormReader(() => throw new ExtractionException(code, "Fixture message.")));

        var refused = Assert.Throws<AppValidationException>(() => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf"));

        Assert.Equal(expected, refused.Code);
        Assert.Equal(expected, refused.Problems.Single().Code);
    }

    [Fact]
    public void A_ninth_read_drops_the_oldest_token()
    {
        using var temp = new TempApp(formReader: new FakeFormReader(Sheet()));

        var tokens = Enumerable.Range(0, 9).Select(_ => temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token).ToList();

        Assert.Null(temp.App.DdbSessions.Peek(tokens[0]));
        Assert.All(tokens.Skip(1), t => Assert.NotNull(temp.App.DdbSessions.Peek(t)));
        Assert.Equal(ImportSessions.MaxLive, temp.App.DdbSessions.Count);
    }

    [Fact]
    public void A_token_expires_after_thirty_minutes_by_the_apps_clock()
    {
        var clock = new MutableTime(TempApp.Now);
        using var temp = new TempApp(formReader: new FakeFormReader(Sheet()), time: clock);
        var token = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;

        clock.Now = TempApp.Now.AddMinutes(30).AddSeconds(-1);
        Assert.NotNull(temp.App.DdbSessions.Peek(token));
        clock.Now = TempApp.Now.AddMinutes(30);
        Assert.Null(temp.App.DdbSessions.Peek(token));
    }

    [Fact]
    public void Take_spends_a_token_and_Peek_does_not()
    {
        using var temp = new TempApp(formReader: new FakeFormReader(Sheet()));
        var token = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;

        Assert.NotNull(temp.App.DdbSessions.Peek(token));
        Assert.NotNull(temp.App.DdbSessions.Take(token));
        Assert.Null(temp.App.DdbSessions.Take(token));
    }

    [Fact]
    public void Discard_and_Dispose_drop_every_token()
    {
        using var temp = new TempApp(formReader: new FakeFormReader(Sheet()));
        var first = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;
        var second = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf").Token;

        Assert.True(temp.App.DiscardDdbSheet(first));
        Assert.False(temp.App.DiscardDdbSheet(first));
        Assert.Null(temp.App.DdbSessions.Peek(first));

        var sessions = temp.App.DdbSessions;
        temp.Close();
        Assert.Null(sessions.Peek(second));
        Assert.Equal(0, sessions.Count);
    }

    [Fact]
    public void ReadData_writes_the_temporary_file_under_the_data_folder_and_deletes_it_even_when_the_reader_throws()
    {
        var reader = new FakeFormReader(Sheet());
        using (var temp = new TempApp(formReader: reader))
        {
            temp.App.ReadDdbSheetData("fixture.pdf", "%PDF-1.7 fixture"u8.ToArray());

            var path = Assert.Single(reader.Paths);
            Assert.True(Assert.Single(reader.Existed));
            Assert.Equal(TmpFolder(temp), Path.GetDirectoryName(path));
            Assert.Matches("^ddb-[0-9a-f]{32}\\.pdf$", Path.GetFileName(path));
            Assert.False(File.Exists(path));
        }

        var failing = new FakeFormReader(() => throw new ExtractionException("worker.crashed", "Fixture message."));
        using (var temp = new TempApp(formReader: failing))
        {
            Assert.Throws<AppValidationException>(() => temp.App.ReadDdbSheetData("fixture.pdf", "%PDF-1.7 fixture"u8.ToArray()));
            Assert.True(Assert.Single(failing.Existed));
            Assert.False(File.Exists(Assert.Single(failing.Paths)));
        }
    }

    [Fact]
    public void Opening_the_app_deletes_leftover_ddb_temporary_files()
    {
        using var temp = new TempApp();
        Directory.CreateDirectory(TmpFolder(temp));
        var leftover = Path.Combine(TmpFolder(temp), $"ddb-{Guid.NewGuid():N}.pdf");
        var other = Path.Combine(TmpFolder(temp), "fixture-other.txt");
        File.WriteAllText(leftover, "%PDF-1.7 fixture");
        File.WriteAllText(other, "Fixture");

        temp.Reopen();

        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void ReadData_refuses_more_than_twenty_megabytes_before_writing_anything()
    {
        var reader = new FakeFormReader(Sheet());
        using var temp = new TempApp(formReader: reader);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.ReadDdbSheetData("fixture.pdf", new byte[(20 << 20) + 1]));

        Assert.Equal("pdf.too-large", refused.Code);
        Assert.Empty(reader.Paths);
        Assert.False(Directory.Exists(TmpFolder(temp)) && Directory.EnumerateFiles(TmpFolder(temp)).Any());
        temp.App.ReadDdbSheetData("fixture.pdf", new byte[20 << 20]);
    }

    [Fact]
    public void An_unmapped_field_never_reaches_the_token()
    {
        const string player = "Testy Player Sentinel";
        var fields = Sheet();
        fields.Add(new FormField("fixture.2014.playerName", "text", 1, player));
        using var temp = new TempApp(formReader: new FakeFormReader(fields));

        var read = temp.App.ReadDdbSheet("C:/fixture/sheet.pdf");

        Assert.DoesNotContain(player, DdbParserTests.Json(temp.App.DdbSessions.Peek(read.Token)!), StringComparison.Ordinal);
        Assert.DoesNotContain(player, JsonSerializer.Serialize(read, RulesJson.Compact), StringComparison.Ordinal);
    }

    [Fact]
    public void ddb_read_answers_unsupported_without_a_host_and_reads_through_the_hosts_Open_dialog_with_one()
    {
        using var temp = new TempApp(formReader: new FakeFormReader(Sheet()));

        var unsupported = Dispatch(new CommandDispatcher(temp.App), "ddb.read", new { });
        Assert.Equal("unsupported", unsupported.GetProperty("error").GetProperty("code").GetString());

        var host = new OpenDialogHost("C:/fixture/chosen.pdf");
        var read = Dispatch(new CommandDispatcher(temp.App, host: host), "ddb.read", new { });
        Assert.True(read.GetProperty("ok").GetBoolean(), read.ToString());
        Assert.Equal("ddb-2014", read.GetProperty("result").GetProperty("layout").GetString());
        Assert.Equal((".pdf"), Assert.Single(host.Asked).Extension);

        var cancelled = Dispatch(new CommandDispatcher(temp.App, host: new OpenDialogHost(null)), "ddb.read", new { });
        Assert.False(cancelled.GetProperty("result").GetProperty("chosen").GetBoolean());
    }

    [Fact]
    public void ddb_readData_and_ddb_discard_work_through_the_dispatcher()
    {
        using var temp = new TempApp(formReader: new FakeFormReader(Sheet()));
        var dispatcher = new CommandDispatcher(temp.App);

        var read = Dispatch(dispatcher, "ddb.readData", new { fileName = "fixture.pdf", data = Convert.ToBase64String("%PDF-1.7 fixture"u8.ToArray()) });
        Assert.True(read.GetProperty("ok").GetBoolean(), read.ToString());
        var token = read.GetProperty("result").GetProperty("token").GetGuid();

        var discarded = Dispatch(dispatcher, "ddb.discard", new { token });
        Assert.True(discarded.GetProperty("result").GetProperty("discarded").GetBoolean());
        Assert.Null(temp.App.DdbSessions.Peek(token));
    }
}
