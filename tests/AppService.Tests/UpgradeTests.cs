using Microsoft.Data.Sqlite;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// What an installer upgrade exercises, independent of installer technology (ADR-008): an older data folder opened
/// by a newer build is backed up before migrating, keeps its data, and a newer data folder is refused untouched.
/// </summary>
public class UpgradeTests
{
    private static readonly string[] FutureMigrations =
        [.. SqliteStore.Migrations, "ALTER TABLE characters ADD COLUMN simulated_future_column TEXT;"];

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));

    private static long Scalar(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Character Fixture() => TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");

    [Fact]
    public void Upgrading_the_schema_backs_up_the_database_first_and_keeps_the_data()
    {
        var directory = NewDirectory();
        using (var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now)))
            app.SaveCharacter(Fixture());
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);

        using (var upgraded = new SqliteStore(database, FutureMigrations))
        {
            Assert.Equal(FutureMigrations.Length, upgraded.SchemaVersion);
            Assert.NotNull(upgraded.FindCharacter(Fixture().Id));
        }

        var backup = SqliteStore.BackupPath(database, SqliteStore.LatestSchemaVersion);
        Assert.True(File.Exists(backup), "backup-before-migration file was not created");
        Assert.Equal(SqliteStore.LatestSchemaVersion, Scalar(backup, "PRAGMA user_version;"));
        Assert.Equal(1, Scalar(backup, "SELECT COUNT(*) FROM characters;"));
    }

    [Fact]
    public void Backup_includes_committed_data_still_in_the_wal_after_a_crash()
    {
        var live = NewDirectory();
        var crashed = NewDirectory();
        Directory.CreateDirectory(crashed);
        var app = TomeStackApp.Open(live, new FixedTime(TempApp.Now));
        try
        {
            app.SaveCharacter(Fixture());
            // Copy the files while the app is still open, like a power cut: the save is committed but not checkpointed.
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                File.Copy(Path.Combine(live, TomeStackApp.DatabaseFileName + suffix), Path.Combine(crashed, TomeStackApp.DatabaseFileName + suffix));
        }
        finally
        {
            app.Dispose();
        }
        var database = Path.Combine(crashed, TomeStackApp.DatabaseFileName);
        var mainFileOnly = Path.Combine(crashed, "main-file-only.db");
        File.Copy(database, mainFileOnly);
        // A plain copy of the main file loses the save (here even the schema, which is also still in the WAL).
        Assert.Equal(0, Scalar(mainFileOnly, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'characters';"));

        using (new SqliteStore(database, FutureMigrations)) { }

        Assert.Equal(1, Scalar(SqliteStore.BackupPath(database, SqliteStore.LatestSchemaVersion), "SELECT COUNT(*) FROM characters;"));
    }

    [Fact]
    public void Data_folder_from_a_newer_build_is_refused_and_left_untouched()
    {
        var directory = NewDirectory();
        using (TomeStackApp.Open(directory, new FixedTime(TempApp.Now))) { }
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<NewerDatabaseException>(() => TomeStackApp.Open(directory, new FixedTime(TempApp.Now)));

        Assert.Contains("newer version of TomeStack", ex.Message, StringComparison.Ordinal);
        Assert.Equal(99, Scalar(database, "PRAGMA user_version;"));
        Assert.Empty(Directory.GetFiles(directory, "*.bak"));
    }

    [Fact]
    public void Opening_a_current_database_takes_no_backup()
    {
        using var temp = new TempApp();
        temp.Reopen();

        Assert.Empty(Directory.GetFiles(temp.App.DataDirectory, "*.bak"));
    }
}
