using Microsoft.Data.Sqlite;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// R75, R76: the first time a newer build opens a data folder an older build used, it takes one copy of the database
/// (<c>tomestack.db.app-&lt;from&gt;-to-&lt;to&gt;.bak</c>, named for the version being left and the one opening it) before anything writes, so a release the player does
/// not like can be backed out of. A stamp file records the version; copies are never pruned.
/// </summary>
public class AppVersionCopyTests
{
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));

    private static TomeStackApp Open(string directory, string version, bool save = false)
    {
        var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: [], devFixtures: true, appVersion: version);
        if (save)
        {
            // R182: a second save of the same fixture names the version already stored.
            var fixture = TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json");
            app.SaveCharacter(fixture with { UpdatedAt = app.ListCharacters().FirstOrDefault(c => c.Id == fixture.Id)?.UpdatedAt ?? fixture.UpdatedAt });
        }
        return app;
    }

    private static string Stamp(string directory) => Path.Combine(directory, AppVersionStamp.FileName);

    private static string[] Copies(string directory) => [.. Directory.GetFiles(directory, "*.bak").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    private static long Scalar(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void A_new_folder_writes_the_stamp_and_takes_no_copy()
    {
        var directory = NewDirectory();

        using (Open(directory, "0.6.0")) { }

        Assert.Equal("0.6.0", File.ReadAllText(Stamp(directory)).Trim());
        Assert.Empty(Copies(directory));
    }

    [Fact]
    public void Reopening_at_the_same_version_takes_no_copy()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.6.0")) { }

        using (Open(directory, "0.6.0")) { }
        using (Open(directory, "0.6.0")) { }

        Assert.Empty(Copies(directory));
    }

    [Fact]
    public void A_folder_with_no_stamp_gets_one_copy_named_for_the_unknown_version_and_then_a_stamp()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.5.0", save: true)) { }
        File.Delete(Stamp(directory)); // what a 0.5.0 or older build left: no stamp

        using (Open(directory, "0.6.0")) { }

        var copy = Assert.Single(Copies(directory));
        Assert.Equal("tomestack.db.app-pre-0.6.0-to-0.6.0.bak", copy);
        Assert.Equal("0.6.0", File.ReadAllText(Stamp(directory)).Trim());
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        Assert.Equal(1, Scalar(Path.Combine(directory, copy), "SELECT COUNT(*) FROM characters;"));
        Assert.Equal(SqliteStore.LatestSchemaVersion, Scalar(Path.Combine(directory, copy), "PRAGMA user_version;"));

        var written = File.GetLastWriteTimeUtc(Path.Combine(directory, copy));
        using (Open(directory, "0.6.0")) { }
        using (Open(directory, "0.6.0")) { }
        Assert.Equal([copy], Copies(directory)); // once
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(directory, copy)));
        Assert.True(File.Exists(database));
    }

    [Fact]
    public void An_older_stamp_names_the_copy_after_the_version_being_left()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.5.0", save: true)) { }

        using (Open(directory, "0.6.0", save: true)) { }

        Assert.Equal(["tomestack.db.app-0.5.0-to-0.6.0.bak"], Copies(directory));
        Assert.Equal("0.6.0", File.ReadAllText(Stamp(directory)).Trim());

        using (Open(directory, "0.7.0")) { }
        Assert.Equal(["tomestack.db.app-0.5.0-to-0.6.0.bak", "tomestack.db.app-0.6.0-to-0.7.0.bak"], Copies(directory)); // never pruned (R76)
    }

    [Fact]
    public void Versions_compare_numerically_not_as_text()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.9.0")) { }

        using (Open(directory, "0.10.0")) { } // "0.10.0" sorts before "0.9.0" as text

        Assert.Equal(["tomestack.db.app-0.9.0-to-0.10.0.bak"], Copies(directory));
        Assert.Equal("0.10.0", File.ReadAllText(Stamp(directory)).Trim());
    }

    [Fact]
    public void A_stamp_from_a_newer_version_takes_no_copy_and_is_not_downgraded()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.10.0")) { }

        using (Open(directory, "0.9.0")) { }

        Assert.Empty(Copies(directory));
        Assert.Equal("0.10.0", File.ReadAllText(Stamp(directory)).Trim());
    }

    [Fact]
    public void An_unreadable_stamp_counts_as_missing()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.5.0")) { }
        File.WriteAllText(Stamp(directory), "not a version\n");

        using (Open(directory, "0.6.0")) { }

        Assert.Equal(["tomestack.db.app-pre-0.6.0-to-0.6.0.bak"], Copies(directory));
        Assert.Equal("0.6.0", File.ReadAllText(Stamp(directory)).Trim());
    }

    [Fact]
    public void The_copy_is_taken_before_the_schema_migration_and_includes_data_still_in_the_wal()
    {
        // A version 9 database whose last save is still in the WAL (a crash), upgraded to this build's schema.
        var live = NewDirectory();
        var crashed = NewDirectory();
        Directory.CreateDirectory(live);
        Directory.CreateDirectory(crashed);
        var liveDatabase = Path.Combine(live, TomeStackApp.DatabaseFileName);
        using (var old = new SqliteStore(liveDatabase, SqliteStore.Migrations[..9]))
        {
            old.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                File.Copy(liveDatabase + suffix, Path.Combine(crashed, TomeStackApp.DatabaseFileName + suffix));
        }

        using (Open(crashed, "0.6.0")) { }

        Assert.Equal(["tomestack.db.app-pre-0.6.0-to-0.6.0.bak", "tomestack.db.v9.bak"], Copies(crashed));
        var copy = Path.Combine(crashed, "tomestack.db.app-pre-0.6.0-to-0.6.0.bak");
        Assert.Equal(9, Scalar(copy, "PRAGMA user_version;")); // before any migration ran
        Assert.Equal(1, Scalar(copy, "SELECT COUNT(*) FROM characters;")); // WAL data included (a BackupDatabase copy, not a file copy)
    }

    [Fact]
    public void A_folder_a_newer_build_wrote_is_refused_with_no_copy_and_no_stamp()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.5.0")) { }
        File.Delete(Stamp(directory));
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        Assert.Throws<NewerDatabaseException>(() => Open(directory, "0.6.0"));

        Assert.Empty(Copies(directory));
        Assert.False(File.Exists(Stamp(directory)));
    }

    [Fact]
    public void A_retry_after_a_failed_launch_keeps_the_first_copy()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.5.0", save: true)) { }
        File.Delete(Stamp(directory));
        using (Open(directory, "0.6.0")) { }
        var copy = Path.Combine(directory, "tomestack.db.app-pre-0.6.0-to-0.6.0.bak");
        Assert.Equal(1, Scalar(copy, "SELECT COUNT(*) FROM characters;"));

        // The new build ran, wrote more, and the launch failed before the stamp was kept: the next launch finds no stamp again.
        using (var app = Open(directory, "0.6.0"))
            app.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json"));
        File.Delete(Stamp(directory));
        using (Open(directory, "0.6.0")) { }

        Assert.Equal(["tomestack.db.app-pre-0.6.0-to-0.6.0.bak"], Copies(directory));
        Assert.Equal(1, Scalar(copy, "SELECT COUNT(*) FROM characters;")); // still what the old build left
        Assert.Equal(2, Scalar(Path.Combine(directory, TomeStackApp.DatabaseFileName), "SELECT COUNT(*) FROM characters;"));
        Assert.Equal("0.6.0", File.ReadAllText(Stamp(directory)).Trim());
    }

    [Fact]
    public void A_stamp_that_cannot_be_written_does_not_stop_the_app_opening_and_is_logged()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(Stamp(directory)); // a directory where the file goes: the move onto it fails

        using (var app = Open(directory, "0.6.0", save: true))
            Assert.Single(app.ListCharacters());

        var log = File.ReadAllText(Path.Combine(directory, "logs", FileErrorLog.FileName));
        Assert.Contains("app.version-stamp", log, StringComparison.Ordinal);
        Assert.False(File.Exists(Stamp(directory) + ".tmp")); // no leftover temporary file
    }

    [Fact]
    public void A_stamp_that_keeps_failing_to_update_does_not_make_a_later_version_skip_its_copy()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.6.0", save: true)) { }
        Directory.CreateDirectory(Stamp(directory) + ".tmp"); // the stamp can no longer be rewritten: the temporary file cannot be created

        using (Open(directory, "0.7.0")) { }
        Assert.Equal("0.6.0", File.ReadAllText(Stamp(directory)).Trim()); // still the old stamp
        using (Open(directory, "0.8.0")) { }

        Assert.Equal(["tomestack.db.app-0.6.0-to-0.7.0.bak", "tomestack.db.app-0.6.0-to-0.8.0.bak"], Copies(directory));
        Assert.Equal(1, Scalar(Path.Combine(directory, "tomestack.db.app-0.6.0-to-0.8.0.bak"), "SELECT COUNT(*) FROM characters;"));
    }

    [Fact]
    public void A_stamp_that_is_denied_to_the_reader_counts_as_missing_and_does_not_stop_the_app_opening()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.5.0", save: true)) { }
        var info = new FileInfo(Stamp(directory));
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny);
        var security = info.GetAccessControl();
        security.AddAccessRule(deny);
        info.SetAccessControl(security);
        try
        {
            using (Open(directory, "0.6.0")) { }

            Assert.Equal(["tomestack.db.app-pre-0.6.0-to-0.6.0.bak"], Copies(directory)); // unreadable = missing
        }
        finally
        {
            security = info.GetAccessControl();
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void A_stale_temporary_file_from_a_crashed_copy_is_replaced_and_the_copy_completes()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.5.0", save: true)) { }
        File.Delete(Stamp(directory));
        var copy = Path.Combine(directory, "tomestack.db.app-pre-0.6.0-to-0.6.0.bak");
        File.WriteAllText(copy + ".tmp", "half a copy");

        using (Open(directory, "0.6.0")) { }

        Assert.False(File.Exists(copy + ".tmp"));
        Assert.Equal(1, Scalar(copy, "SELECT COUNT(*) FROM characters;"));
    }

    [Fact]
    public void A_copy_that_fails_leaves_nothing_under_the_final_name_and_stops_the_open()
    {
        var directory = NewDirectory();
        using (Open(directory, "0.5.0", save: true)) { }
        File.Delete(Stamp(directory));
        var copy = Path.Combine(directory, "tomestack.db.app-pre-0.6.0-to-0.6.0.bak");
        Directory.CreateDirectory(copy + ".tmp"); // the copy cannot be started

        Assert.ThrowsAny<Exception>(() => Open(directory, "0.6.0"));

        Assert.False(File.Exists(copy));
        Assert.False(File.Exists(Stamp(directory))); // the failed open wrote no stamp
    }

    [Fact]
    public void The_real_app_version_is_the_assembly_version()
    {
        using var temp = new TempApp();

        Assert.Equal(temp.App.GetInfo().Version, AppVersionStamp.CurrentVersion);
        Assert.Equal(AppVersionStamp.CurrentVersion, File.ReadAllText(Stamp(temp.Directory)).Trim());
    }
}
