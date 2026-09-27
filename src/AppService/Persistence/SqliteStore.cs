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
    /// <summary>A numbered, forward-only migration: SQL, plus an optional data step in the same transaction.</summary>
    internal sealed record Migration(string Sql, Action<SqliteStore>? Code = null);

    internal static readonly Migration[] Migrations =
    [
        new("""
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
        """),
        // v2 (ADR-003): content revisions move to typed effects. Stored JSON and hashes are rewritten in the new
        // representation so the insert-only check keeps working. The original bytes stay in legacy_json.
        new("ALTER TABLE content_revisions ADD COLUMN legacy_json TEXT;", store => store.RewriteUpgradedRevisions()),
        // v3 (ADR-005, M2 item 6): PDF attachments. Each source's free-form pdfRef becomes an attachment record (a managed
        // copy when the file is a readable PDF, otherwise "linked" and flagged missing). The old value stays in
        // legacy_pdf_ref for one release. Never fails because of a missing file.
        new("""
        CREATE TABLE attachments (
            attachment_id TEXT PRIMARY KEY,
            sha256 TEXT,
            original_file_name TEXT NOT NULL,
            byte_length INTEGER NOT NULL,
            mode TEXT NOT NULL,
            linked_path TEXT,
            created_at TEXT NOT NULL
        );
        CREATE INDEX ix_attachments_sha256 ON attachments (sha256);
        ALTER TABLE sources ADD COLUMN attachment_id TEXT;
        ALTER TABLE sources ADD COLUMN legacy_pdf_ref TEXT;
        """, store => store.MigratePdfReferences()),
        // v4 (SPEC P-01, BACKLOG B12, M2 item 7): local campaign profiles.
        new("""
        CREATE TABLE campaigns (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            json TEXT NOT NULL
        );
        """),
    ];

    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();
    private readonly IReadOnlyList<Migration> _migrations;
    private SqliteTransaction? _transaction;

    public SqliteStore(string databasePath)
        : this(databasePath, Migrations)
    {
    }

    /// <summary>Test seam: run a different migration list (for example, a simulated future schema).</summary>
    internal SqliteStore(string databasePath, IReadOnlyList<Migration> migrations)
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
            "INSERT INTO sources (id, json, attachment_id) VALUES ($id, $json, $attachment) ON CONFLICT(id) DO UPDATE SET json = excluded.json, attachment_id = excluded.attachment_id;",
            ("$id", Key(source.Id)),
            ("$json", Serialize(source)),
            ("$attachment", source.AttachmentId is { } a ? Key(a) : DBNull.Value));
    }

    // ---- campaigns (SPEC P-01) ----

    public void SaveCampaign(Campaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        Execute(
            "INSERT INTO campaigns (id, name, json) VALUES ($id, $name, $json) ON CONFLICT(id) DO UPDATE SET name = excluded.name, json = excluded.json;",
            ("$id", Key(campaign.Id)),
            ("$name", campaign.Name),
            ("$json", Serialize(campaign)));
    }

    public Campaign? FindCampaign(Guid id) => QuerySingle<Campaign>("SELECT json FROM campaigns WHERE id = $id;", ("$id", Key(id)));

    public IReadOnlyList<Campaign> ListCampaigns() => Query<Campaign>("SELECT json FROM campaigns ORDER BY name, id;");

    public void DeleteCampaign(Guid id) => Execute("DELETE FROM campaigns WHERE id = $id;", ("$id", Key(id)));

    // ---- attachments (ADR-005) ----

    /// <summary>The folder for managed copies: <c>&lt;data dir&gt;/attachments/&lt;sha256&gt;.pdf</c>.</summary>
    public string AttachmentsDirectory => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!, "attachments");

    public void AddAttachment(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        Execute(
            "INSERT INTO attachments (attachment_id, sha256, original_file_name, byte_length, mode, linked_path, created_at) VALUES ($id, $sha, $name, $length, $mode, $path, $created);",
            ("$id", Key(attachment.AttachmentId)),
            ("$sha", (object?)attachment.Sha256 ?? DBNull.Value),
            ("$name", attachment.OriginalFileName),
            ("$length", attachment.ByteLength),
            ("$mode", attachment.Mode.ToString()),
            ("$path", (object?)attachment.LinkedPath ?? DBNull.Value),
            ("$created", attachment.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
    }

    public Attachment? FindAttachment(Guid attachmentId) =>
        Attachments("WHERE attachment_id = $id", ("$id", Key(attachmentId))).SingleOrDefault();

    /// <summary>A managed attachment with this content hash, so the same PDF is stored once.</summary>
    public Attachment? FindManagedAttachment(string sha256) =>
        Attachments("WHERE sha256 = $sha AND mode = 'Managed'", ("$sha", sha256)).FirstOrDefault();

    /// <summary>A linked attachment for this exact path (the migration de-duplicates missing files this way).</summary>
    public Attachment? FindLinkedAttachment(string path) =>
        Attachments("WHERE linked_path = $path AND mode = 'Linked'", ("$path", path)).FirstOrDefault();

    public void DeleteAttachment(Guid attachmentId) =>
        Execute("DELETE FROM attachments WHERE attachment_id = $id;", ("$id", Key(attachmentId)));

    /// <summary>How many sources still point at this attachment.</summary>
    public int SourcesUsing(Guid attachmentId) =>
        Convert.ToInt32(Scalar("SELECT COUNT(*) FROM sources WHERE attachment_id = $id;", ("$id", Key(attachmentId))), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>How many attachment records share this content hash (the managed file is deleted only at zero).</summary>
    public int AttachmentsWithHash(string sha256) =>
        Convert.ToInt32(Scalar("SELECT COUNT(*) FROM attachments WHERE sha256 = $sha;", ("$sha", sha256)), System.Globalization.CultureInfo.InvariantCulture);

    private List<Attachment> Attachments(string where, params (string Name, object Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command($"SELECT attachment_id, sha256, original_file_name, byte_length, mode, linked_path, created_at FROM attachments {where};", parameters);
            using var reader = command.ExecuteReader();
            var results = new List<Attachment>();
            while (reader.Read())
            {
                results.Add(new(
                    Guid.Parse(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    Enum.Parse<AttachmentMode>(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture)));
            }
            return results;
        }
    }

    private object? Scalar(string sql, params (string Name, object Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, parameters);
            return command.ExecuteScalar();
        }
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

    /// <summary>
    /// Published revisions that extend one choice. The LIKE pre-filter keeps calculation from reading every revision;
    /// the typed comparison afterwards is the real test.
    /// </summary>
    public IEnumerable<ContentRevision> ChoiceExtensions(Guid contentId, string choiceId) =>
        Query<ContentRevision>("SELECT json FROM content_revisions WHERE status = 'Published' AND json LIKE '%\"extendsChoice\"%' ORDER BY rowid;")
            .Where(r => r.ExtendsChoice == new ChoiceExtension(contentId, choiceId));

    public IEnumerable<ContentRevision> RevisionsOf(Guid contentId) => ListRevisions(contentId);

    /// <summary>Every stored revision in the order it was added (the homebrew studio's history).</summary>
    public IReadOnlyList<ContentRevision> ListRevisionsInOrder() =>
        Query<ContentRevision>("SELECT json FROM content_revisions ORDER BY rowid;");

    public IReadOnlyList<ContentRevision> ListRevisions() =>
        Query<ContentRevision>("SELECT json FROM content_revisions ORDER BY content_id, revision_id;");

    /// <summary>Every stored revision (draft or published) of one content entity, in insertion order.</summary>
    public IReadOnlyList<ContentRevision> ListRevisions(Guid contentId) =>
        Query<ContentRevision>("SELECT json FROM content_revisions WHERE content_id = $cid ORDER BY rowid;", ("$cid", Key(contentId)));

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
                Execute(_migrations[version].Sql);
                _migrations[version].Code?.Invoke(this);
                Execute($"PRAGMA user_version = {version + 1};");
            });
        }
    }

    /// <summary>
    /// The one sanctioned rewrite of published revisions: a lossless change of representation, not of content
    /// (ADR-003 "Migration"). Runs inside the migration's transaction.
    /// </summary>
    private void RewriteUpgradedRevisions()
    {
        var rows = new List<(string Id, string Json)>();
        lock (_gate)
        {
            using var command = Command("SELECT revision_id, json FROM content_revisions;", []);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        foreach (var (id, json) in rows)
        {
            var revision = JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Compact)!;
            if (revision.UpgradedFrom is null)
                continue;
            var upgraded = Serialize(revision);
            Execute(
                "UPDATE content_revisions SET json = $json, sha256 = $hash, legacy_json = $legacy WHERE revision_id = $id;",
                ("$json", upgraded),
                ("$hash", Sha256(upgraded)),
                ("$legacy", json),
                ("$id", id));
        }
    }

    /// <summary>Migration v3 data step (ADR-005 "Migration plan"). Runs inside the migration's transaction.</summary>
    private void MigratePdfReferences()
    {
        var rows = new List<(string Id, string Json)>();
        lock (_gate)
        {
            using var command = Command("SELECT id, json FROM sources;", []);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        foreach (var (id, json) in rows)
        {
            var source = JsonSerializer.Deserialize<SourceRecord>(json, RulesJson.Compact)!;
            if (string.IsNullOrWhiteSpace(source.PdfRef))
                continue;
            var attachment = AttachmentFiles.AdoptLegacyPath(this, source.PdfRef, DateTimeOffset.UtcNow);
            Execute(
                "UPDATE sources SET json = $json, attachment_id = $attachment, legacy_pdf_ref = $legacy WHERE id = $id;",
                ("$json", Serialize(source with { PdfRef = null, AttachmentId = attachment.AttachmentId })),
                ("$attachment", Key(attachment.AttachmentId)),
                ("$legacy", source.PdfRef),
                ("$id", id));
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

    private List<T> Query<T>(string sql, params (string Name, object Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, parameters);
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
