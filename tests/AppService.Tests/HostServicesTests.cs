using System.Text.Json;
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
