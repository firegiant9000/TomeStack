using System.Text.Json;
using TomeStack.ImportWorker.Tests;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

public class HostServicesTests
{
    private sealed class FakeHost(string? chosenPath) : IHostServices
    {
        public string? Suggested { get; private set; }

        public string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension)
        {
            Suggested = suggestedFileName;
            return chosenPath;
        }
    }

    private static JsonElement SaveAs(CommandDispatcher dispatcher, Guid characterId) =>
        JsonDocument.Parse(dispatcher.Dispatch($$$"""{"id":"1","command":"package.saveAs","payload":{"characterIds":["{{{characterId}}}"]}}""")).RootElement;

    [Fact]
    public void Save_as_writes_the_package_to_the_path_chosen_in_the_native_dialog_and_returns_only_the_file_name()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var target = Path.Combine(temp.App.DataDirectory, "chosen", "Pell.tomestack.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var host = new FakeHost(target);

        var response = SaveAs(new CommandDispatcher(temp.App, host: host), saved.Character.Id);

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.True(response.GetProperty("result").GetProperty("saved").GetBoolean());
        Assert.Equal("Pell.tomestack.zip", response.GetProperty("result").GetProperty("fileName").GetString());
        Assert.DoesNotContain(temp.App.DataDirectory, response.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Pell--2014-fixture-personal-backup.tomestack.zip", host.Suggested); // default purpose: backup (ADR-007)
        Assert.True(temp.App.PreviewImport(File.ReadAllBytes(target)).CanApply);
        Assert.False(File.Exists(target + ".partial"));
    }

    [Fact]
    public void Cancelled_dialog_writes_nothing()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));

        var response = SaveAs(new CommandDispatcher(temp.App, host: new FakeHost(null)), saved.Character.Id);

        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.False(response.GetProperty("result").GetProperty("saved").GetBoolean());
    }

    [Fact]
    public void Host_without_native_dialogs_reports_unsupported_so_the_ui_can_fall_back()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));

        var response = SaveAs(new CommandDispatcher(temp.App), saved.Character.Id);

        Assert.Equal("unsupported", response.GetProperty("error").GetProperty("code").GetString());
    }

    // Item 19b (R116): every dialog path the worker reads is a full path to a .pdf on a local drive.

    public static TheoryData<string> RefusedPaths => new()
    {
        @"\\fixture-server\share\sheet.pdf",   // a network path: Windows would connect to another machine
        @"\\?\C:\fixture\sheet.pdf",          // a device path
        @"\\.\C:\fixture\sheet.pdf",
        "sheet.pdf",                           // relative: whatever is in the working folder
        @"fixture\sheet.pdf",
        @"C:sheet.pdf",                        // drive-relative
        @"C:\fixture\..\other\sheet.pdf",      // up and out
        @"C:\fixture\sheet.txt",               // not a PDF
        @"C:\fixture\sheet.pdf.exe",
        "",
    };

    private static JsonElement Dispatch(CommandDispatcher dispatcher, string command, object payload) =>
        JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

    [Theory]
    [MemberData(nameof(RefusedPaths))]
    public void ddb_read_refuses_a_path_that_is_not_a_local_pdf_and_it_never_reaches_the_worker(string path)
    {
        var reader = new FakeFormReader([]);
        using var temp = new TempApp(formReader: reader);

        var response = Dispatch(new CommandDispatcher(temp.App, host: new OpenDialogHost(path)), "ddb.read", new { });

        var error = response.GetProperty("error");
        Assert.Equal("pdf.path-not-local", error.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.Empty(reader.Paths);
        Assert.Equal(0, temp.App.DdbSessions.Count);
        if (path.Length > 0)
            Assert.DoesNotContain("fixture", response.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(RefusedPaths))]
    public void source_attachPdf_refuses_a_path_that_is_not_a_local_pdf_in_both_modes_and_attaches_nothing(string path)
    {
        using var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Book", [RulesFamilies.Srd51]));

        foreach (var mode in new[] { "managed", "linked" })
        {
            var response = Dispatch(new CommandDispatcher(temp.App, host: new OpenDialogHost(path)), "source.attachPdf", new { sourceId = source.Id, mode });

            Assert.Equal("pdf.path-not-local", response.GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());
            if (path.Length > 0)
                Assert.DoesNotContain("fixture", response.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Null(temp.App.GetAttachment(source.Id));
    }

    [Theory]
    [InlineData(@"C:\fixture\sheet.pdf", ".pdf", true)]
    [InlineData("d:/fixture/Sheet.PDF", ".pdf", true)]
    [InlineData(@"C:\fixture\notes.tomestack.zip", ".tomestack.zip", true)]
    [InlineData(@"C:\fixture\sheet.pdf", ".zip", false)]
    [InlineData(@"C:\fixture\a..b.pdf", ".pdf", false)]
    [InlineData(@"C:\", ".pdf", false)]
    [InlineData(@"\\fixture-server\share\sheet.pdf", ".pdf", false)]
    [InlineData(@"\\?\C:\fixture\sheet.pdf", ".pdf", false)]
    [InlineData(null, ".pdf", false)]
    public void The_local_drive_rule_takes_the_extension_as_a_parameter(string? path, string extension, bool expected) =>
        Assert.Equal(expected, LocalDrivePath.IsSafe(path, extension));

    [Fact]
    public void A_full_local_pdf_path_is_read_by_ddb_read()
    {
        var reader = new FakeFormReader(DdbParserTests.Fields(FixtureSheets.Fields2014));
        using var temp = new TempApp(formReader: reader);
        const string path = "C:/fixture/sheet.PDF";

        var response = Dispatch(new CommandDispatcher(temp.App, host: new OpenDialogHost(path)), "ddb.read", new { });

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.True(response.GetProperty("result").GetProperty("chosen").GetBoolean());
        Assert.Equal([path], reader.Paths);
    }

    [Fact]
    public void ddb_read_names_the_layout_in_words_for_the_choose_step()
    {
        using var temp = new TempApp(formReader: new FakeFormReader(() => throw new ImportWorker.ExtractionException("ddb.no-form-fields", "Fixture message.")));

        var response = Dispatch(new CommandDispatcher(temp.App, host: new OpenDialogHost(ImportWorker.Tests.FixturePdfs.TextSheetPath)), "ddb.read", new { });

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("text from the PDF", response.GetProperty("result").GetProperty("layoutName").GetString());
    }

    [Fact]
    public void Unwritable_location_is_a_validation_error_without_the_full_path()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        var missingFolder = Path.Combine(temp.App.DataDirectory, "does-not-exist", "Pell.tomestack.zip");

        var response = SaveAs(new CommandDispatcher(temp.App, host: new FakeHost(missingFolder)), saved.Character.Id);

        var error = response.GetProperty("error");
        Assert.Equal("validation", error.GetProperty("code").GetString());
        Assert.Equal("package.save-failed", error.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.DoesNotContain("does-not-exist", response.ToString(), StringComparison.Ordinal);
    }
}
