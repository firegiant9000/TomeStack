using Microsoft.Data.Sqlite;
using TomeStack.AppService.Persistence;

namespace TomeStack.AppService.Tests;

/// <summary>Database 13 (D104): the database itself refuses any change or removal of a content revision, drafts included.</summary>
public class ContentRevisionInsertOnlyTests
{
    private const string Message = "content revisions are insert-only";

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static long Count(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Fingerprint(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) || ':' || ifnull(group_concat(sha256, ','), '') FROM (SELECT sha256 FROM content_revisions ORDER BY revision_id);";
        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public void A_direct_update_on_a_published_revision_is_refused()
    {
        using var temp = new TempApp();
        using var connection = Open(temp.App.Store.DatabasePath);
        var before = Fingerprint(connection);
        Assert.True(Count(connection, "SELECT COUNT(*) FROM content_revisions WHERE status = 'Published';") > 0);

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE content_revisions SET json = json WHERE status = 'Published';";
        var thrown = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        Assert.Contains(Message, thrown.Message, StringComparison.Ordinal);
        Assert.Equal(before, Fingerprint(connection));
    }

    [Fact]
    public void A_direct_delete_on_a_revision_is_refused()
    {
        using var temp = new TempApp();
        using var connection = Open(temp.App.Store.DatabasePath);
        var before = Fingerprint(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM content_revisions WHERE revision_id = (SELECT revision_id FROM content_revisions WHERE status = 'Published' LIMIT 1);";
        var thrown = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        Assert.Contains(Message, thrown.Message, StringComparison.Ordinal);
        Assert.Equal(before, Fingerprint(connection));
    }

    [Fact]
    public void A_direct_update_or_delete_on_a_draft_revision_is_refused()
    {
        using var temp = new TempApp();
        var id = Guid.Parse("5f4dc000-0000-4000-8000-0000000000dd").ToString("D");
        using var connection = Open(temp.App.Store.DatabasePath);
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO content_revisions (revision_id, content_id, status, sha256, json) VALUES ($id, $id, 'Draft', 'x', '{}');";
            insert.Parameters.AddWithValue("$id", id);
            insert.ExecuteNonQuery(); // inserting stays allowed
        }
        var before = Fingerprint(connection);

        foreach (var sql in new[] { "UPDATE content_revisions SET json = '{\"a\":1}' WHERE revision_id = $id;", "DELETE FROM content_revisions WHERE revision_id = $id;" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id);
            Assert.Contains(Message, Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).Message, StringComparison.Ordinal);
        }

        Assert.Equal(before, Fingerprint(connection));
    }

    [Fact]
    public void Reopening_a_v13_database_is_a_no_op()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        using (TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: [])) { }
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        var copiesBefore = Directory.GetFiles(directory, "*.bak").Length;

        using (var again = new SqliteStore(database))
            Assert.Equal(13, again.SchemaVersion);

        Assert.Equal(13, SqliteStore.LatestSchemaVersion);
        Assert.Equal(copiesBefore, Directory.GetFiles(directory, "*.bak").Length);
        using var connection = Open(database);
        Assert.Equal(2, Count(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'content_revisions_insert_only_%';"));
    }
}
