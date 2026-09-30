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
        // v5 (M3 B3): session gap notes. Local only; they leave the machine only inside a personal backup.
        new("""
        CREATE TABLE gap_notes (
            id TEXT PRIMARY KEY,
            character_id TEXT NOT NULL,
            json TEXT NOT NULL
        );
        CREATE INDEX ix_gap_notes_character_id ON gap_notes (character_id);
        """),
        // v6 (M4 D2, ADR-009 (d)): import jobs, extracted page text keyed by the PDF's hash, candidates and an audit log.
        // Local only: no package ever includes these tables (a share or a backup).
        new("""
        CREATE TABLE import_jobs (
            id TEXT PRIMARY KEY,
            source_id TEXT NOT NULL,
            status TEXT NOT NULL,
            json TEXT NOT NULL
        );
        CREATE INDEX ix_import_jobs_source_id ON import_jobs (source_id);
        CREATE TABLE import_pages (
            sha256 TEXT NOT NULL,
            page INTEGER NOT NULL,
            text TEXT NOT NULL,
            json TEXT NOT NULL,
            PRIMARY KEY (sha256, page)
        );
        CREATE TABLE import_candidates (
            id TEXT PRIMARY KEY,
            job_id TEXT NOT NULL,
            status TEXT NOT NULL,
            json TEXT NOT NULL
        );
        CREATE INDEX ix_import_candidates_job_id ON import_candidates (job_id);
        CREATE TABLE import_audit (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id TEXT NOT NULL,
            at TEXT NOT NULL,
            event TEXT NOT NULL,
            detail TEXT
        );
        CREATE INDEX ix_import_audit_job_id ON import_audit (job_id);
        """),
        // v7 (M5 slice 8, BACKLOG B08): character snapshots, insert-only. The triggers refuse any change or removal, so a
        // snapshot is a fixed record. Local only: no package, share or library backup includes them (owner decision
        // LIVING_SPECS D14). Forward-only, with the usual copy of the v6 database first.
        new("""
        CREATE TABLE character_snapshots (
            id TEXT PRIMARY KEY,
            character_id TEXT NOT NULL,
            created_at TEXT NOT NULL,
            json TEXT NOT NULL
        );
        CREATE INDEX ix_character_snapshots_character_id ON character_snapshots (character_id);
        CREATE TRIGGER character_snapshots_insert_only_update BEFORE UPDATE ON character_snapshots
        BEGIN SELECT RAISE(ABORT, 'character snapshots are insert-only'); END;
        CREATE TRIGGER character_snapshots_insert_only_delete BEFORE DELETE ON character_snapshots
        BEGIN SELECT RAISE(ABORT, 'character snapshots are insert-only'); END;
        """),
        // v8 (M6 slice 1, LIVING_SPECS D14 item 6): the durable import-derived flag on sources. The column mirrors
        // SourceRecord.ImportDerived and can only go up (UpsertSource). The data step marks every source that shows an
        // import already: a PDF attached now or before migration v3 (legacy_pdf_ref), or an import job (jobs and their
        // pages survive removing the PDF). Older builds refuse a v8 database, so none can rewrite a source without the
        // flag. Forward-only, with the usual copy first (BackupBeforeUpgrade: one copy, at the version the database was
        // opened with, so a v6 database upgraded here leaves tomestack.db.v6.bak and no v7 copy).
        new("ALTER TABLE sources ADD COLUMN import_derived INTEGER NOT NULL DEFAULT 0;", store => store.BackfillImportDerived()),
        // v9 (M6 slice 3, ADR-011): installed extensions. The row holds the manifest, the SHA-256 of the file the grants are
        // bound to, the grants and whether it is enabled; the file itself is <data dir>/extensions/<sha256>.zip, read-only.
        // Forward-only, with the usual single copy first (at the version the database was opened with).
        new("""
        CREATE TABLE extensions (
            id TEXT PRIMARY KEY,
            sha256 TEXT NOT NULL,
            json TEXT NOT NULL
        );
        """),
    ];

    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();
    private readonly IReadOnlyList<Migration> _migrations;
    private SqliteTransaction? _transaction;

    /// <summary>Published choice extensions by the choice they extend; null until read or after a change (see <see cref="ChoiceExtensions"/>).</summary>
    private Dictionary<ChoiceExtension, ContentRevision[]>? _choiceExtensions;

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
            catch
            {
                _choiceExtensions = null; // it may hold a revision the rollback removes
                throw;
            }
            finally
            {
                _transaction = null;
            }
        }
    }

    /// <summary>
    /// Adds or replaces a source. M6 slice 1: whatever the caller passes, the import-derived flag never goes down, an
    /// import-derived source is never redistributable or confirmed as shareable, and an origin once recorded stays (so no
    /// package, restore or later write can launder a source).
    /// </summary>
    public void UpsertSource(SourceRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            var existing = FindSource(source.Id);
            var merged = source with
            {
                ImportDerived = source.ImportDerived == true || existing?.ImportDerived == true ? true : null,
                Origin = existing?.Origin ?? source.Origin,
            };
            if (merged.ImportDerived == true)
                merged = merged with { Redistributable = false, ShareConfirmedAt = null };
            Execute(
                """
                INSERT INTO sources (id, json, attachment_id, import_derived) VALUES ($id, $json, $attachment, $derived)
                ON CONFLICT(id) DO UPDATE SET json = excluded.json, attachment_id = excluded.attachment_id,
                    import_derived = MAX(sources.import_derived, excluded.import_derived);
                """,
                ("$id", Key(merged.Id)),
                ("$json", Serialize(merged)),
                ("$attachment", merged.AttachmentId is { } a ? Key(a) : DBNull.Value),
                ("$derived", merged.ImportDerived == true ? 1 : 0));
        }
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

    // ---- extensions (M6 slice 3, ADR-011) ----

    public void SaveExtension(Extensions.InstalledExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        Execute(
            "INSERT INTO extensions (id, sha256, json) VALUES ($id, $sha, $json) ON CONFLICT(id) DO UPDATE SET sha256 = excluded.sha256, json = excluded.json;",
            ("$id", Key(extension.Id)),
            ("$sha", extension.Sha256),
            ("$json", Serialize(extension)));
    }

    public Extensions.InstalledExtension? FindExtension(Guid id) => QuerySingle<Extensions.InstalledExtension>("SELECT json FROM extensions WHERE id = $id;", ("$id", Key(id)));

    public IReadOnlyList<Extensions.InstalledExtension> ListExtensions() => Query<Extensions.InstalledExtension>("SELECT json FROM extensions ORDER BY id;");

    public void DeleteExtension(Guid id) => Execute("DELETE FROM extensions WHERE id = $id;", ("$id", Key(id)));

    /// <summary>Whether any installed extension still uses the file with this hash (a file is removed only when none does).</summary>
    public bool ExtensionFileInUse(string sha256) =>
        QueryScalar("SELECT id FROM extensions WHERE sha256 = $sha LIMIT 1;", ("$sha", sha256)) is not null;

    // ---- gap notes (M3 B3) ----

    public void SaveGapNote(GapNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        Execute(
            "INSERT INTO gap_notes (id, character_id, json) VALUES ($id, $character, $json) ON CONFLICT(id) DO UPDATE SET character_id = excluded.character_id, json = excluded.json;",
            ("$id", Key(note.Id)),
            ("$character", Key(note.CharacterId)),
            ("$json", Serialize(note)));
    }

    public GapNote? FindGapNote(Guid id) => QuerySingle<GapNote>("SELECT json FROM gap_notes WHERE id = $id;", ("$id", Key(id)));

    public IReadOnlyList<GapNote> ListGapNotes(Guid characterId) =>
        Query<GapNote>("SELECT json FROM gap_notes WHERE character_id = $character ORDER BY rowid;", ("$character", Key(characterId)));

    public IReadOnlyList<GapNote> ListAllGapNotes() => Query<GapNote>("SELECT json FROM gap_notes ORDER BY rowid;");

    public void DeleteGapNote(Guid id) => Execute("DELETE FROM gap_notes WHERE id = $id;", ("$id", Key(id)));

    // ---- character snapshots (M5 slice 8): insert-only, local only ----

    /// <summary>Adds a snapshot. There is no update or delete: the table refuses both (migration v7).</summary>
    public void AddSnapshot(CharacterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Execute(
            "INSERT INTO character_snapshots (id, character_id, created_at, json) VALUES ($id, $character, $created, $json);",
            ("$id", Key(snapshot.Id)),
            ("$character", Key(snapshot.CharacterId)),
            ("$created", snapshot.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            ("$json", Serialize(snapshot)));
    }

    public CharacterSnapshot? FindSnapshot(Guid id) =>
        QuerySingle<CharacterSnapshot>("SELECT json FROM character_snapshots WHERE id = $id;", ("$id", Key(id)));

    /// <summary>The most snapshots one page of a list returns (snapshots are never removed, so the history only grows; older ones come by paging).</summary>
    public const int MaxListedSnapshots = 100;

    /// <summary>
    /// A page of a character's snapshots, newest first (insertion order breaks ties), at most <paramref name="limit"/>. With
    /// <paramref name="before"/>, only snapshots stored before that one (the oldest of the page before).
    /// </summary>
    public IReadOnlyList<CharacterSnapshot> ListSnapshots(Guid characterId, Guid? before = null, int limit = MaxListedSnapshots) =>
        before is { } cursor
            ? Query<CharacterSnapshot>(
                "SELECT json FROM character_snapshots WHERE character_id = $character AND rowid < (SELECT rowid FROM character_snapshots WHERE id = $before AND character_id = $character) ORDER BY rowid DESC LIMIT $limit;",
                ("$character", Key(characterId)), ("$before", Key(cursor)), ("$limit", limit))
            : Query<CharacterSnapshot>(
                "SELECT json FROM character_snapshots WHERE character_id = $character ORDER BY rowid DESC LIMIT $limit;",
                ("$character", Key(characterId)), ("$limit", limit));

    // ---- imports (M4 D2, ADR-009 (d)): local only, never exported ----

    public void SaveImportJob(ImportJobRecord job)
    {
        ArgumentNullException.ThrowIfNull(job);
        Execute(
            "INSERT INTO import_jobs (id, source_id, status, json) VALUES ($id, $source, $status, $json) ON CONFLICT(id) DO UPDATE SET status = excluded.status, json = excluded.json;",
            ("$id", Key(job.Id)),
            ("$source", Key(job.SourceId)),
            ("$status", job.Status.ToString()),
            ("$json", Serialize(job)));
    }

    public ImportJobRecord? FindImportJob(Guid id) => QuerySingle<ImportJobRecord>("SELECT json FROM import_jobs WHERE id = $id;", ("$id", Key(id)));

    public IReadOnlyList<ImportJobRecord> ListImportJobs() => Query<ImportJobRecord>("SELECT json FROM import_jobs ORDER BY rowid;");

    /// <summary>One extracted page of the PDF with this hash; a later extraction of the same page replaces it.</summary>
    public void SaveImportPage(string sha256, StoredPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Execute(
            "INSERT INTO import_pages (sha256, page, text, json) VALUES ($sha, $page, $text, $json) ON CONFLICT(sha256, page) DO UPDATE SET text = excluded.text, json = excluded.json;",
            ("$sha", sha256),
            ("$page", page.Page),
            ("$text", page.Text),
            ("$json", Serialize(page with { Text = "" })));
    }

    public StoredPage? FindImportPage(string sha256, int page)
    {
        lock (_gate)
        {
            using var command = Command("SELECT text, json FROM import_pages WHERE sha256 = $sha AND page = $page;", [("$sha", sha256), ("$page", page)]);
            using var reader = command.ExecuteReader();
            return reader.Read() ? JsonSerializer.Deserialize<StoredPage>(reader.GetString(1), RulesJson.Compact)! with { Text = reader.GetString(0) } : null;
        }
    }

    public IReadOnlyList<StoredPage> ListImportPages(string sha256)
    {
        lock (_gate)
        {
            using var command = Command("SELECT text, json FROM import_pages WHERE sha256 = $sha ORDER BY page;", [("$sha", sha256)]);
            using var reader = command.ExecuteReader();
            var pages = new List<StoredPage>();
            while (reader.Read())
                pages.Add(JsonSerializer.Deserialize<StoredPage>(reader.GetString(1), RulesJson.Compact)! with { Text = reader.GetString(0) });
            return pages;
        }
    }

    /// <summary>
    /// Pages <paramref name="first"/> to <paramref name="last"/> for detection: blocks and lines only (detection never reads
    /// the joined text), in page order, until their stored size passes <paramref name="maxChars"/>. <paramref name="stoppedBefore"/>
    /// is the first page left out, or null when every page fits. This bounds what detection holds in the app's memory.
    /// </summary>
    public IReadOnlyList<StoredPage> ListImportPagesForDetection(string sha256, int first, int last, long maxChars, out int? stoppedBefore)
    {
        stoppedBefore = null;
        lock (_gate)
        {
            using var command = Command(
                "SELECT page, json FROM import_pages WHERE sha256 = $sha AND page >= $first AND page <= $last ORDER BY page;",
                [("$sha", sha256), ("$first", first), ("$last", last)]);
            using var reader = command.ExecuteReader();
            var pages = new List<StoredPage>();
            long used = 0;
            while (reader.Read())
            {
                var json = reader.GetString(1);
                used += json.Length;
                if (used > maxChars)
                {
                    stoppedBefore = reader.GetInt32(0);
                    break;
                }
                pages.Add(JsonSerializer.Deserialize<StoredPage>(json, RulesJson.Compact)!);
            }
            return pages;
        }
    }

    /// <summary>SPEC I-03: pages of one PDF whose text contains <paramref name="query"/> (case-insensitive for ASCII), in page order.</summary>
    public IReadOnlyList<(int Page, string Text)> SearchImportPages(string sha256, string query, int limit)
    {
        lock (_gate)
        {
            using var command = Command(
                "SELECT page, text FROM import_pages WHERE sha256 = $sha AND instr(lower(text), lower($query)) > 0 ORDER BY page LIMIT $limit;",
                [("$sha", sha256), ("$query", query), ("$limit", limit)]);
            using var reader = command.ExecuteReader();
            var results = new List<(int, string)>();
            while (reader.Read())
                results.Add((reader.GetInt32(0), reader.GetString(1)));
            return results;
        }
    }

    public void SaveImportCandidate(StoredCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Execute(
            "INSERT INTO import_candidates (id, job_id, status, json) VALUES ($id, $job, $status, $json) ON CONFLICT(id) DO UPDATE SET status = excluded.status, json = excluded.json;",
            ("$id", Key(candidate.Id)),
            ("$job", Key(candidate.JobId)),
            ("$status", candidate.Status.ToString()),
            ("$json", Serialize(candidate)));
    }

    public StoredCandidate? FindImportCandidate(Guid id) =>
        QuerySingle<StoredCandidate>("SELECT json FROM import_candidates WHERE id = $id;", ("$id", Key(id))) is { } found ? Typed(found) : null;

    public IReadOnlyList<StoredCandidate> ListImportCandidates(Guid jobId) =>
        [.. Query<StoredCandidate>("SELECT json FROM import_candidates WHERE job_id = $job ORDER BY rowid;", ("$job", Key(jobId))).Select(Typed)];

    /// <summary>Versioned effect types are typed only inside a revision, so a stored candidate's effects are typed on read (ADR-003).</summary>
    private static StoredCandidate Typed(StoredCandidate stored) => stored with
    {
        Candidate = ImportWorker.CandidateQuarantine.WithTypedEffects(stored.Candidate),
        Edited = stored.Edited is { } edited ? ImportWorker.CandidateQuarantine.WithTypedEffects(edited) : null,
    };

    /// <summary>Candidates still pending review are replaced when a job detects again; reviewed ones stay.</summary>
    public void DeletePendingImportCandidates(Guid jobId) =>
        Execute("DELETE FROM import_candidates WHERE job_id = $job AND status = 'Pending';", ("$job", Key(jobId)));

    public void AddImportAudit(Guid jobId, DateTimeOffset at, string eventName, string? detail) => Execute(
        "INSERT INTO import_audit (job_id, at, event, detail) VALUES ($job, $at, $event, $detail);",
        ("$job", Key(jobId)),
        ("$at", at.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
        ("$event", eventName),
        ("$detail", (object?)detail ?? DBNull.Value));

    public IReadOnlyList<ImportAuditEntry> ListImportAudit(Guid jobId)
    {
        lock (_gate)
        {
            using var command = Command("SELECT at, event, detail FROM import_audit WHERE job_id = $job ORDER BY id;", [("$job", Key(jobId))]);
            using var reader = command.ExecuteReader();
            var entries = new List<ImportAuditEntry>();
            while (reader.Read())
                entries.Add(new(DateTimeOffset.Parse(reader.GetString(0), System.Globalization.CultureInfo.InvariantCulture), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            return entries;
        }
    }

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

    public SourceRecord? FindSource(Guid sourceId) => Sources("WHERE id = $id", ("$id", Key(sourceId))).SingleOrDefault();

    public IReadOnlyList<SourceRecord> ListSources() => Sources("ORDER BY id");

    /// <summary>Sources with the <c>import_derived</c> column applied (database v8), which only ever raises the flag.</summary>
    private List<SourceRecord> Sources(string clause, params (string Name, object Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command($"SELECT json, import_derived FROM sources {clause};", parameters);
            using var reader = command.ExecuteReader();
            var sources = new List<SourceRecord>();
            while (reader.Read())
            {
                var source = JsonSerializer.Deserialize<SourceRecord>(reader.GetString(0), RulesJson.Compact)!;
                sources.Add(reader.GetInt64(1) != 0 && source.ImportDerived != true
                    ? source with { ImportDerived = true, Redistributable = false, ShareConfirmedAt = null }
                    : source);
            }
            return sources;
        }
    }

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
            _choiceExtensions = null;
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
    /// Published revisions that extend one choice. Every calculation asks this for every offered choice, so the table
    /// is read once (a LIKE pre-filter, then the typed test) and kept in memory. Revisions are insert-only, so the copy
    /// is dropped only when a revision is added and when a transaction rolls back.
    /// </summary>
    public IEnumerable<ContentRevision> ChoiceExtensions(Guid contentId, string choiceId)
    {
        lock (_gate)
        {
            if (_choiceExtensions is null)
            {
                ChoiceExtensionScans++;
                _choiceExtensions = Query<ContentRevision>("SELECT json FROM content_revisions WHERE status = 'Published' AND json LIKE '%\"extendsChoice\"%' ORDER BY rowid;")
                    .Where(r => r.ExtendsChoice is not null)
                    .GroupBy(r => r.ExtendsChoice!)
                    .ToDictionary(g => g.Key, g => g.ToArray());
            }
            return _choiceExtensions.TryGetValue(new(contentId, choiceId), out var extensions) ? extensions : [];
        }
    }

    /// <summary>Test seam: how often <see cref="ChoiceExtensions"/> read the table.</summary>
    internal int ChoiceExtensionScans { get; private set; }

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

    /// <summary>
    /// A complete copy of the database at <paramref name="path"/> (SQLite online backup, so data still in the WAL is
    /// included). M2.1: taken before a library restore replaces anything.
    /// </summary>
    public void BackupTo(string path)
    {
        lock (_gate)
        {
            using var target = new SqliteConnection($"Data Source={path};Pooling=False");
            _connection.BackupDatabase(target);
        }
    }

    public IReadOnlyList<Attachment> ListAttachments() => Attachments("ORDER BY created_at, attachment_id");

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

    /// <summary>
    /// Migration v8 data step (M6 slice 1). Runs inside the migration's transaction. A source counts as import-derived when
    /// it has a PDF now, had one before migration v3, or has an import job. The bundled SRD sources are never marked:
    /// their content is this build's own.
    /// </summary>
    private void BackfillImportDerived()
    {
        var rows = new List<(string Id, string Json)>();
        var evidence = new HashSet<string>(StringComparer.Ordinal);
        lock (_gate)
        {
            using (var command = Command("SELECT id, json, attachment_id IS NOT NULL OR legacy_pdf_ref IS NOT NULL FROM sources;", []))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add((reader.GetString(0), reader.GetString(1)));
                    if (reader.GetInt64(2) != 0)
                        evidence.Add(reader.GetString(0));
                }
            }
            using (var command = Command("SELECT DISTINCT source_id FROM import_jobs;", []))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    evidence.Add(reader.GetString(0));
            }
        }
        var bundled = TomeStackApp.BundledSourceIds;
        foreach (var (id, json) in rows.Where(r => evidence.Contains(r.Id)))
        {
            var source = JsonSerializer.Deserialize<SourceRecord>(json, RulesJson.Compact)!;
            if (bundled.Contains(source.Id))
                continue;
            Execute(
                "UPDATE sources SET json = $json, import_derived = 1 WHERE id = $id;",
                ("$json", Serialize(source with { ImportDerived = true, Redistributable = false, ShareConfirmedAt = null })),
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
