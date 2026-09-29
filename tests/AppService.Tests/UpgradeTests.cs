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
    private static readonly SqliteStore.Migration[] FutureMigrations =
        [.. SqliteStore.Migrations, new("ALTER TABLE characters ADD COLUMN simulated_future_column TEXT;")];

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
        using (var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), devFixtures: true))
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
        var app = TomeStackApp.Open(live, new FixedTime(TempApp.Now), devFixtures: true);
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
    public void A_stored_revision_under_a_bundled_id_is_kept_and_the_folder_still_opens()
    {
        // Full-stack review: a package imported into an older build can carry a revision under an id that a later build
        // bundles. Seeding that build must not fail to open the data folder on every launch.
        var directory = NewDirectory();
        var padded = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-armor.json").Revisions.First();
        using (TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: [])) { }
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // Another revision's data under the bundled id, as the older build would have stored it.
            command.CommandText = "UPDATE content_revisions SET json = replace(json, $name, $planted), sha256 = 'planted' WHERE revision_id = $id;";
            command.Parameters.AddWithValue("$name", $"\"name\":\"{padded.Name}\"");
            command.Parameters.AddWithValue("$planted", "\"name\":\"Test Planted Name\"");
            command.Parameters.AddWithValue("$id", padded.RevisionId.ToString("D"));
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        using var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: []);

        var warning = Assert.Single(app.GetInfo().Warnings, w => w.Code == "content.bundled-conflict");
        Assert.Equal(padded.Reference, warning.Content);
        Assert.DoesNotContain("Test Planted Name", warning.Message, StringComparison.Ordinal); // never the stored text
        Assert.Equal("Test Planted Name", app.Store.FindRevision(padded.Reference)!.Name); // kept, not overwritten
        // The rest of the bundled content is still there.
        Assert.NotNull(app.Store.FindRevision(TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-armor.json").Revisions.Last().Reference));
    }

    [Fact]
    public void Data_folder_from_a_newer_build_is_refused_and_left_untouched()
    {
        var directory = NewDirectory();
        using (TomeStackApp.Open(directory, new FixedTime(TempApp.Now), devFixtures: true)) { }
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<NewerDatabaseException>(() => TomeStackApp.Open(directory, new FixedTime(TempApp.Now), devFixtures: true));

        Assert.Contains("newer version of TomeStack", ex.Message, StringComparison.Ordinal);
        Assert.Equal(99, Scalar(database, "PRAGMA user_version;"));
        Assert.Empty(Directory.GetFiles(directory, "*.bak"));
    }

    /// <summary>
    /// An M0 (schema v1) data folder stores revisions as v1 JSON, and its hashes are over those bytes. Opening it with a
    /// build whose effect model is typed must neither trip the insert-only check when re-seeding fixtures nor lose
    /// the original JSON.
    /// </summary>
    [Fact]
    public void Schema_v1_data_folder_with_v1_revision_json_migrates_to_typed_effects()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        var pack = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "fixture-pack.json")));
        var v1Rows = new Dictionary<string, string>();
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            void Run(string sql, params (string, object)[] parameters)
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }
            Run(SqliteStore.Migrations[0].Sql);
            Run("PRAGMA user_version = 1;");
            foreach (var source in pack.RootElement.GetProperty("sources").EnumerateArray())
                Run("INSERT INTO sources (id, json) VALUES ($id, $json);", ("$id", source.GetProperty("id").GetString()!), ("$json", source.GetRawText()));
            foreach (var revision in pack.RootElement.GetProperty("revisions").EnumerateArray())
            {
                var json = System.Text.Json.JsonSerializer.Serialize(revision); // compact v1 JSON, as M0 stored it
                v1Rows[revision.GetProperty("revisionId").GetString()!] = json;
                Run(
                    "INSERT INTO content_revisions (revision_id, content_id, status, sha256, json) VALUES ($rid, $cid, $status, $hash, $json);",
                    ("$rid", revision.GetProperty("revisionId").GetString()!),
                    ("$cid", revision.GetProperty("contentId").GetString()!),
                    ("$status", revision.GetProperty("status").GetString() == "published" ? "Published" : "Draft"),
                    ("$hash", SqliteStore.Sha256(json)),
                    ("$json", json));
            }
        }

        using var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), devFixtures: true); // re-seeds the fixture pack

        Assert.Equal(SqliteStore.LatestSchemaVersion, app.GetInfo().SchemaVersion);
        Assert.True(File.Exists(SqliteStore.BackupPath(database, 1)));
        var quickfoot = app.Store.ListRevisions().Single(r => r.Name == "Fixture Quickfoot");
        var bonus = Assert.IsType<ModifierEffect>(Assert.Single(quickfoot.Effects));
        Assert.Equal((ModifierOperation.Bonus, "ability.dex.score", "2"), (bonus.Operation, bonus.Target, bonus.Value));
        Assert.Equal(ContentRevision.TypedEffectsSchemaVersion, quickfoot.SchemaVersion); // v1 upcasts to v2, never further
        Assert.Equal(SqliteStore.Sha256(SqliteStore.Serialize(quickfoot)), app.Store.RevisionHash(quickfoot.RevisionId));
        Assert.Equal(v1Rows[quickfoot.RevisionId.ToString("D")], Text(database, $"SELECT legacy_json FROM content_revisions WHERE revision_id = '{quickfoot.RevisionId:D}';"));
        Assert.Equal(4, app.SaveCharacter(Fixture()).Sheet.Field(CharacterCalculator.InitiativeField).Value);
    }

    private static string? Text(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    [Fact]
    public void Opening_a_current_database_takes_no_backup()
    {
        using var temp = new TempApp();
        temp.Reopen();

        Assert.Empty(Directory.GetFiles(temp.App.DataDirectory, "*.bak"));
    }
}
