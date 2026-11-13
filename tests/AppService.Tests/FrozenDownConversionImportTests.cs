using System.Text.Json;
using TomeStack.AppService.Packages;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D103: the build offers only the previous minor, so "for 0.7.0" is no longer written. 0.8.1 did write it, so users hold such files.
/// These are the zips 0.8.1 made, frozen (<c>tests/RulesFixtures/downconvert/frozen/</c>, never regenerated), loaded through the
/// current build: import only, the same sheet facts the retired 0.7.0 golden cases asserted.
/// </summary>
public class FrozenDownConversionImportTests
{
    private static readonly Guid HeroId = Guid.Parse("c1070000-0000-4000-8000-0000000000a1");

    private static readonly (string Field, int Value)[] ExpectedFields =
    [
        ("ability.str.score", 12), ("ability.dex.score", 14), ("ability.con.score", 12), ("ability.int.score", 10),
        ("ability.wis.score", 13), ("ability.cha.score", 8), ("ability.dex.mod", 2), ("proficiencyBonus", 2),
        ("save.dex", 2), ("skill.acrobatics", 2), ("skill.perception", 1), ("passive.perception", 11),
        ("speed", 30), ("initiative", 2), ("armorClass", 13), ("hitPoints", 9),
    ];

    private static string Frozen(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TomeStack.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("The repository root was not found."),
            "tests", "RulesFixtures", "downconvert", "frozen", "made-by-0.8.1-for-0.7.0", name);
    }

    private static void AssertSheet(CommandDispatcher dispatcher)
    {
        var response = JsonDocument.Parse(dispatcher.Dispatch($$"""{"id":"1","command":"character.get","payload":{"id":"{{HeroId}}"} }""")).RootElement.Clone();
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var fields = response.GetProperty("result").GetProperty("sheet").GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("field").GetString()!, f => f.GetProperty("value").GetInt32());
        foreach (var (field, value) in ExpectedFields)
            Assert.Equal(value, fields[field]);
    }

    [Fact]
    public void The_character_share_0_8_1_made_for_0_7_0_still_imports_with_its_sheet()
    {
        using var app = new TempApp(fresh: true);
        var dispatcher = new CommandDispatcher(app.App);
        var base64 = Convert.ToBase64String(File.ReadAllBytes(Frozen("Compat-Hero-for-0.7.0.tomestack.zip")));

        var response = JsonDocument.Parse(dispatcher.Dispatch($$"""{"id":"1","command":"package.apply","payload":{"base64":"{{base64}}"} }""")).RootElement.Clone();
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        AssertSheet(dispatcher);
    }

    [Fact]
    public void The_library_backup_0_8_1_made_for_0_7_0_still_restores_with_the_same_sheet()
    {
        using var app = new TempApp(fresh: true);
        var path = Frozen("library-backup-for-0.7.0.tomestack.zip");

        Assert.Empty(app.App.PreviewLibraryRestore(path).Errors);
        app.App.ApplyLibraryRestore(path);

        AssertSheet(new CommandDispatcher(app.App));
    }
}
