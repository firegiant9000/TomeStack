using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Persistence;

/// <summary>
/// Local SQLite store. Entities are stored as versioned JSON documents with indexed key columns.
/// Published content revisions are insert-only: re-adding identical JSON is a no-op, different JSON is rejected.
/// </summary>
public sealed class SqliteStore : IContentCatalog, IDisposable
{
    internal static readonly string[] Migrations =
    [
        """
        CREATE TABLE sources (
            id TEXT PRIMARY KEY,
            json TEXT NOT NULL
        );
        CREATE TABLE content_revisions (
            revision_id TEXT PRIMARY KEY,
            content_id TEXT NOT NULL,
            status TEXT NOT NULL,
            sha256 TEXT NOT NULL,
            json TEXT NOT NULL
        );
        CREATE INDEX ix_content_revisions_content_id ON content_revisions (content_id);
        CREATE TABLE characters (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            rules_family TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            json TEXT NOT NULL
        );
        """,
    ];

    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();
    private readonly IReadOnlyList<string> _migrations;
    private SqliteTransaction? _transaction;

    public SqliteStore(string databasePath)
        : this(databasePath, Migrations)
    {
    }

    /// <summary>Test seam: run a different migration list (for example, a simulated future schema).</summary>
    internal SqliteStore(string databasePath, IReadOnlyList<string> migrations)
    {
        DatabasePath = databasePath;
        _migrations = migrations;
        BackupBeforeUpgrade(databasePath, migrations.Count);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;");
        Migrate();
    }

    public string DatabasePath { get; }

    public static int LatestSchemaVersion => Migrations.Length;

    public int SchemaVersion
    {
        get
        {
            lock (_gate)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "PRAGMA user_version;";
                return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    public void InTransaction(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (_transaction is not null)
            {
                action();
                return;
            }
            using var transaction = _connection.BeginTransaction();
            _transaction = transaction;
            try
            {
                action();
                transaction.Commit();
            }
            finally
            {
                _transaction = null;
            }
        }
    }

    public void UpsertSource(SourceRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Execute(
            "INSERT INTO sources (id, json) VALUES ($id, $json) ON CONFLICT(id) DO UPDATE SET json = excluded.json;",
            ("$id", Key(source.Id)),
            ("$json", Serialize(source)));
    }

    public SourceRecord? FindSource(Guid sourceId) =>
        QuerySingle<SourceRecord>("SELECT json FROM sources WHERE id = $id;", ("$id", Key(sourceId)));

    public IReadOnlyList<SourceRecord> ListSources() => Query<SourceRecord>("SELECT json FROM sources ORDER BY id;");

    /// <summary>Adds a revision. Returns false if an identical revision already exists.</summary>
    /// <exception cref="ImmutableRevisionException">A different revision with the same ID exists.</exception>
    public bool AddRevision(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var json = Serialize(revision);
        var hash = Sha256(json);
        lock (_gate)
        {
            var existing = QueryScalar("SELECT sha256 FROM content_revisions WHERE revision_id = $id;", ("$id", Key(revision.RevisionId)));
            if (existing is not null)
            {
                return existing == hash
                    ? false
                    : throw new ImmutableRevisionException(revision.Reference);
            }
            Execute(
                "INSERT INTO content_revisions (revision_id, content_id, status, sha256, json) VALUES ($rid, $cid, $status, $hash, $json);",
                ("$rid", Key(revision.RevisionId)),
                ("$cid", Key(revision.ContentId)),
                ("$status", revision.Status.ToString()),
                ("$hash", hash),
                ("$json", json));
            return true;
        }
    }

    public string? RevisionHash(Guid revisionId) =>
        QueryScalar("SELECT sha256 FROM content_revisions WHERE revision_id = $id;", ("$id", Key(revisionId)));

    public ContentRevision? FindRevision(ContentReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return QuerySingle<ContentRevision>(
            "SELECT json FROM content_revisions WHERE revision_id = $rid AND content_id = $cid;",
            ("$rid", Key(reference.RevisionId)),
            ("$cid", Key(reference.ContentId)));
    }

    public IReadOnlyList<ContentRevision> ListRevisions() =>
        Query<ContentRevision>("SELECT json FROM content_revisions ORDER BY content_id, revision_id;");

    public void SaveCharacter(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        Execute(
            """
            INSERT INTO characters (id, name, rules_family, updated_at, json) VALUES ($id, $name, $family, $updated, $json)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, rules_family = excluded.rules_family, updated_at = excluded.updated_at, json = excluded.json;
            """,
            ("$id", Key(character.Id)),
            ("$name", character.Name),
            ("$family", character.RulesFamily),
            ("$updated", character.UpdatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            ("$json", Serialize(character)));
    }

    public Character? FindCharacter(Guid id) =>
        QuerySingle<Character>("SELECT json FROM characters WHERE id = $id;", ("$id", Key(id)));

    public IReadOnlyList<Character> ListCharacters() =>
        Query<Character>("SELECT json FROM characters ORDER BY updated_at DESC, id;");

    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, RulesJson.Compact);

    internal static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Key(Guid id) => id.ToString("D");

    /// <summary>File name of the copy taken before migrating a database from schema <paramref name="version"/>.</summary>
    public static string BackupPath(string databasePath, int version) => $"{databasePath}.v{version}.bak";

    /// <summary>
    /// ARCHITECTURE: migrations are numbered and the user database is backed up before upgrading. Uses SQLite's
    /// online backup rather than a file copy: in WAL mode, committed data can still be in <c>-wal</c> after a crash,
    /// and a copy of the main file alone would silently miss it.
    /// </summary>
    private static void BackupBeforeUpgrade(string databasePath, int latestVersion)
    {
        if (!File.Exists(databasePath))
            return;
        using var probe = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        probe.Open();
        int version;
        using (var command = probe.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version;";
            version = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
        if (version > latestVersion)
            throw new NewerDatabaseException(version, latestVersion);
        if (version is > 0 && version < latestVersion)
        {
            var backupPath = BackupPath(databasePath, version);
            File.Delete(backupPath);
            using var backup = new SqliteConnection($"Data Source={backupPath};Pooling=False");
            probe.BackupDatabase(backup);
        }
    }

    private void Migrate()
    {
        for (var version = SchemaVersion; version < _migrations.Count; version++)
        {
            InTransaction(() =>
            {
                Execute(_migrations[version]);
                Execute($"PRAGMA user_version = {version + 1};");
            });
        }
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, parameters);
            command.ExecuteNonQuery();
        }
    }

    private string? QueryScalar(string sql, params (string Name, object Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, parameters);
            return command.ExecuteScalar() as string;
        }
    }

    private T? QuerySingle<T>(string sql, params (string Name, object Value)[] parameters) where T : class =>
        QueryScalar(sql, parameters) is { } json ? JsonSerializer.Deserialize<T>(json, RulesJson.Compact) : null;

    private List<T> Query<T>(string sql)
    {
        lock (_gate)
        {
            using var command = Command(sql, []);
            using var reader = command.ExecuteReader();
            var results = new List<T>();
            while (reader.Read())
                results.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), RulesJson.Compact)!);
            return results;
        }
    }

    private SqliteCommand Command(string sql, (string Name, object Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }
}

/// <summary>The data folder was written by a newer TomeStack. Nothing is changed; the user must update the app.</summary>
public sealed class NewerDatabaseException(int version, int supported)
    : InvalidOperationException($"This data folder was created by a newer version of TomeStack (database schema v{version}; this version supports up to v{supported}). Update TomeStack to open it. Nothing was changed.")
{
    public int Version { get; } = version;
}

public sealed class ImmutableRevisionException(ContentReference reference)
    : InvalidOperationException($"Revision {reference.RevisionId} of content {reference.ContentId} is already stored with different data. Published revisions are immutable; create a new revision instead.")
{
    public ContentReference Reference { get; } = reference;
}
