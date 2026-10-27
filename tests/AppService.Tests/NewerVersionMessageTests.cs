using Microsoft.Data.Sqlite;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Tester note "export from a previous version to a newer version", the reverse case: a file or data folder from a newer TomeStack
/// is refused with a message that names the version that made it and this one, and says which version to install. The version comes
/// from untrusted input (a package manifest, the data folder's stamp), so only a plain major.minor.patch newer than this build is named.
/// </summary>
public class NewerVersionMessageTests
{
    private static readonly string This = AppVersionStamp.CurrentVersion;

    private static byte[] CharacterPackage(TempApp origin)
    {
        var saved = origin.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        return origin.App.ExportCharacters([saved.Character.Id]).Content;
    }

    [Fact]
    public void A_newer_package_format_names_the_version_that_made_it_and_the_one_to_install()
    {
        using var origin = new TempApp();
        var future = PackageEditor.Edit(CharacterPackage(origin), _ => false, _ => { }, manifest =>
        {
            manifest["formatVersion"] = Packages.PackageManifest.CurrentFormatVersion + 1;
            manifest["appVersion"] = "99.1.0";
        });

        using var destination = new TempApp();
        var error = Assert.Single(destination.App.PreviewImport(future).Errors);

        Assert.Equal("package.unsupported-format", error.Code);
        Assert.Contains($"It was made by TomeStack 99.1.0; this is TomeStack {This}.", error.Message, StringComparison.Ordinal);
        Assert.Contains("Update TomeStack to 99.1.0 or newer to import it.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_entry_schema_names_the_version_that_made_the_package()
    {
        using var origin = new TempApp();
        var future = PackageEditor.Edit(CharacterPackage(origin), path => path.StartsWith("characters/", StringComparison.Ordinal),
            node => node["schemaVersion"] = 99, manifest => manifest["appVersion"] = "99.1.0");

        using var destination = new TempApp();
        var error = Assert.Single(destination.App.PreviewImport(future).Errors, e => e.Code == "package.schema-unsupported");

        Assert.Contains("character schema v99", error.Message, StringComparison.Ordinal);
        Assert.Contains($"It was made by TomeStack 99.1.0; this is TomeStack {This}. Update TomeStack to 99.1.0 or newer to import it.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<b>1.2.3</b>")]
    [InlineData("99.1.0.7.8")]
    [InlineData("0.0.1")] // older than this build: it cannot be the reason
    [InlineData("99999999.1.0")]
    public void A_version_that_is_not_a_plain_newer_one_is_never_named(string appVersion)
    {
        using var origin = new TempApp();
        var future = PackageEditor.Edit(CharacterPackage(origin), _ => false, _ => { }, manifest =>
        {
            manifest["formatVersion"] = Packages.PackageManifest.CurrentFormatVersion + 1;
            manifest["appVersion"] = appVersion;
        });

        using var destination = new TempApp();
        var error = Assert.Single(destination.App.PreviewImport(future).Errors);

        Assert.DoesNotContain(appVersion, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("It was made by", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("Update TomeStack to import it.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_data_folder_last_opened_by_a_newer_build_names_both_versions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        using (TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: [])) { }
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }
        File.WriteAllText(Path.Combine(directory, AppVersionStamp.FileName), "99.1.0\n"); // what the newer build wrote

        var ex = Assert.Throws<NewerDatabaseException>(() => TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: []));

        Assert.Contains("newer version of TomeStack", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"TomeStack 99.1.0 last opened it; this is TomeStack {This}.", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Update TomeStack to 99.1.0 or newer to open it. Nothing was changed.", ex.Message, StringComparison.Ordinal);
        Assert.Equal("99.1.0", File.ReadAllText(Path.Combine(directory, AppVersionStamp.FileName)).Trim()); // never downgraded
    }
}
