using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TomeStack.AppService.Diagnostics;

/// <summary>
/// Roadmap T6, the recovery drill (docs/features/restore-drill-procedure.md): counts what a data folder holds, and a
/// digest per table, so a library and its restore on another machine can be compared without exposing anything in it.
/// The report holds only counts, byte totals and SHA-256 digests: no title, name, text, file name or path, because the
/// drill record is public.
/// <para>
/// Read-only by construction: the database (and its <c>-wal</c>/<c>-shm</c>, if a crash left them) is copied to a temporary
/// folder and opened there with <c>Mode=ReadOnly</c>; the data folder itself is only listed and read. It is refused while
/// TomeStack holds the folder (<see cref="DataFolderLock"/>), because the copy could catch a half-written state.
/// </para>
/// Shipped in AppService for its tests, but only the dev-only DevHost runs it (<c>--drill-report</c>); the app never does.
/// </summary>
public static class RestoreDrill
{
    public const int FormatVersion = 1;

    /// <summary>Tables compared by digest; the import tables are machine-local and listed by count only.</summary>
    private static readonly (string Table, string Sql)[] Digests =
    [
        ("content_revisions", "SELECT revision_id || ':' || sha256 FROM content_revisions"),
        ("sources", "SELECT id, json FROM sources"),
        ("characters", "SELECT id, json FROM characters"),
        ("campaigns", "SELECT id, json FROM campaigns"),
        ("gap_notes", "SELECT id, json FROM gap_notes"),
        // A linked PDF's path and the record's time are machine-local; the file's identity is its hash, mode and size.
        ("attachments", "SELECT attachment_id || ':' || ifnull(sha256, '') || ':' || mode || ':' || byte_length FROM attachments"),
        // Grants and on/off never travel (ADR-011); the file is the identity.
        ("extensions", "SELECT id || ':' || sha256 FROM extensions"),
        ("character_snapshots", "SELECT id, json FROM character_snapshots"),
    ];

    public sealed record TableDigest(string Table, long Rows, string Digest);

    public sealed record Counts(
        long RevisionsPublished, long RevisionsDraft, long RevisionsBundledSrd, long RevisionsOwn,
        long Sources, long SourcesLocal, long SourcesReceived, long SourcesUnknownOrigin, long SourcesImportDerived, long SourcesBundledSrd,
        long Characters, long CharactersArchived, long Campaigns, long GapNotes,
        long Attachments, long AttachmentsManaged, long AttachmentsLinked,
        long ManagedFiles, long ManagedFileBytes, long ManagedFilesMissing,
        long Extensions, long ExtensionsEnabled, long ExtensionFiles, long Snapshots, long ImportJobs);

    public sealed record Report(string Tool, int FormatVersion, DateTimeOffset TakenAt, int DatabaseVersion, Counts Counts, IReadOnlyList<TableDigest> Tables);

    /// <summary>A difference between two reports, and whether the drill expects it (docs/features/restore-drill-procedure.md).</summary>
    public sealed record Difference(string Item, string Before, string After, string? Expected);

    /// <exception cref="DataFolderInUseException">TomeStack has the folder open.</exception>
    /// <exception cref="FileNotFoundException">The folder has no database.</exception>
    public static Report Count(string dataDirectory, TimeProvider? time = null)
    {
        var database = Path.Combine(dataDirectory, TomeStackApp.DatabaseFileName);
        if (!File.Exists(database))
            throw new FileNotFoundException("The folder has no TomeStack database.");
        ThrowIfInUse(dataDirectory);

        var copy = Path.Combine(Path.GetTempPath(), "tomestack-drill", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        try
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(database + suffix))
                    File.Copy(database + suffix, Path.Combine(copy, TomeStackApp.DatabaseFileName + suffix));
            }
            // A crash can leave committed data in the -wal; replaying it needs a writable -shm, which only the copy has.
            var copied = Path.Combine(copy, TomeStackApp.DatabaseFileName);
            var mode = File.Exists(copied + "-wal") ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly;
            Report report;
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copied, Mode = mode, Pooling = false }.ToString()))
            {
                connection.Open();
                report = Build(connection, dataDirectory, (time ?? TimeProvider.System).GetUtcNow());
            }
            return report with { Tables = [.. report.Tables, Sheets(copied)] };
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(copy, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a temporary copy left behind is harmless.
            }
        }
    }

    private static void ThrowIfInUse(string dataDirectory)
    {
        var lockFile = Path.Combine(dataDirectory, DataFolderLock.FileName);
        if (!File.Exists(lockFile))
            return;
        try
        {
            // Only opened for reading: a running TomeStack holds it without sharing, so this fails, and nothing is written.
            using var probe = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            throw new DataFolderInUseException(ex);
        }
    }

    private static Report Build(SqliteConnection connection, string dataDirectory, DateTimeOffset now)
    {
        var tables = Tables(connection);
        long Scalar(string sql) => tables.Contains(TableOf(sql)) ? Convert.ToInt64(Execute(connection, sql), System.Globalization.CultureInfo.InvariantCulture) : 0;

        var bundled = TomeStackApp.BundledSourceIds.Select(id => id.ToString("D")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long published = 0, draft = 0, bundledRevisions = 0;
        foreach (var (status, json) in Rows(connection, "SELECT status, json FROM content_revisions"))
        {
            if (string.Equals(status, "Published", StringComparison.OrdinalIgnoreCase))
                published++;
            else
                draft++;
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("provenance", out var provenance) && provenance.TryGetProperty("sourceId", out var source)
                && bundled.Contains(source.GetString() ?? ""))
                bundledRevisions++;
        }

        long sources = 0, local = 0, received = 0, unknown = 0, importDerived = 0, bundledSources = 0;
        foreach (var (id, json) in Rows(connection, "SELECT id, json FROM sources"))
        {
            sources++;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var origin = root.TryGetProperty("origin", out var o) ? o.GetString() : null;
            if (bundled.Contains(id))
                bundledSources++;
            else if (string.Equals(origin, "local", StringComparison.OrdinalIgnoreCase))
                local++;
            else if (string.Equals(origin, "received", StringComparison.OrdinalIgnoreCase))
                received++;
            else
                unknown++;
            if (root.TryGetProperty("importDerived", out var derived) && derived.ValueKind == JsonValueKind.True)
                importDerived++;
        }

        long characters = 0, archived = 0;
        foreach (var (_, json) in Rows(connection, "SELECT id, json FROM characters"))
        {
            characters++;
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("archivedAt", out var at) && at.ValueKind == JsonValueKind.String)
                archived++;
        }

        long managed = 0, linked = 0, files = 0, bytes = 0, missing = 0;
        var attachmentsFolder = Path.Combine(dataDirectory, "attachments");
        if (tables.Contains("attachments"))
        {
            foreach (var (mode, sha256) in Rows(connection, "SELECT mode, ifnull(sha256, '') FROM attachments"))
            {
                if (!string.Equals(mode, "managed", StringComparison.OrdinalIgnoreCase))
                {
                    linked++;
                    continue;
                }
                managed++;
                var file = new FileInfo(Path.Combine(attachmentsFolder, $"{sha256}.pdf"));
                if (file.Exists)
                {
                    files++;
                    bytes += file.Length;
                }
                else
                {
                    missing++;
                }
            }
        }

        long extensions = 0, enabled = 0;
        if (tables.Contains("extensions"))
        {
            foreach (var (_, json) in Rows(connection, "SELECT id, json FROM extensions"))
            {
                extensions++;
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.True)
                    enabled++;
            }
        }
        var extensionFolder = Path.Combine(dataDirectory, "extensions");
        var extensionFiles = Directory.Exists(extensionFolder) ? Directory.GetFiles(extensionFolder, "*.zip").LongLength : 0;

        var counts = new Counts(
            published, draft, bundledRevisions, published + draft - bundledRevisions,
            sources, local, received, unknown, importDerived, bundledSources,
            characters, archived, Scalar("SELECT COUNT(*) FROM campaigns"), Scalar("SELECT COUNT(*) FROM gap_notes"),
            managed + linked, managed, linked, files, bytes, missing,
            extensions, enabled, extensionFiles, Scalar("SELECT COUNT(*) FROM character_snapshots"), Scalar("SELECT COUNT(*) FROM import_jobs"));

        var digests = Digests.Where(d => tables.Contains(d.Table)).Select(d => Digest(connection, d.Table, d.Sql)).ToList();
        var version = Convert.ToInt32(Execute(connection, "PRAGMA user_version;"), System.Globalization.CultureInfo.InvariantCulture);
        return new("tomestack.restoreDrill", FormatVersion, now, version, counts, digests);
    }

    /// <summary>
    /// The calculated sheet of every character, as the digest of <c>id:SHA-256(sheet JSON)</c>, so the drill compares what
    /// the player sees and not only what is stored. Calculated from the temporary copy through the store, as the app does;
    /// two machines on the same build must agree. Only a database at this build's version can be read this way; an older
    /// or newer one gets no sheet digest.
    /// </summary>
    private static TableDigest Sheets(string copiedDatabase)
    {
        if (ReadVersion(copiedDatabase) != Persistence.SqliteStore.LatestSchemaVersion)
            return new("sheets", 0, "not calculated: another database version");
        using var store = new Persistence.SqliteStore(copiedDatabase);
        var lines = store.ListCharacters()
            .Select(c => $"{c.Id:D}:{Sha256(JsonSerializer.Serialize(RulesCore.CharacterCalculator.Calculate(c, store), RulesCore.RulesJson.Compact))}")
            .Order(StringComparer.Ordinal)
            .ToList();
        return new("sheets", lines.Count, Sha256(string.Join('\n', lines)));
    }

    private static int ReadVersion(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return Convert.ToInt32(Execute(connection, "PRAGMA user_version;"), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>SHA-256 over the sorted lines <c>id:hash</c>; for a JSON row the hash is SHA-256 of the stored JSON.</summary>
    private static TableDigest Digest(SqliteConnection connection, string table, string sql)
    {
        var lines = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                lines.Add(reader.FieldCount == 1 ? reader.GetString(0) : $"{reader.GetString(0)}:{Sha256(reader.GetString(1))}");
        }
        lines.Sort(StringComparer.Ordinal);
        return new(table, lines.Count, Sha256(string.Join('\n', lines)));
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static HashSet<string> Tables(SqliteConnection connection) =>
        [.. Rows(connection, "SELECT name, type FROM sqlite_master WHERE type = 'table'").Select(r => r.Item1)];

    private static string TableOf(string countSql) => countSql[(countSql.LastIndexOf(' ') + 1)..];

    private static object? Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static List<(string, string)> Rows(SqliteConnection connection, string sql)
    {
        var rows = new List<(string, string)>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) // no such table: an older database version
        {
        }
        return rows;
    }

    /// <summary>
    /// Every count and digest that differs, each marked with why the drill expects it, or null for a real difference.
    /// Expected: snapshots are not in backups (LIVING_SPECS D14); extensions come back off and ungranted (ADR-011);
    /// the bundled SRD sources and revisions are seeded by the build itself; import jobs are machine-local.
    /// </summary>
    public static IReadOnlyList<Difference> Compare(Report before, Report after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        string? Why(string item) => item switch
        {
            "Snapshots" or "table character_snapshots" => "Snapshots are not in a full backup (LIVING_SPECS D14)",
            "ExtensionsEnabled" => "A restored extension comes back turned off with no grants (ADR-011)",
            "ImportJobs" => "Import jobs and extracted text are machine-local, never in a backup (ADR-009)",
            "RevisionsBundledSrd" or "SourcesBundledSrd" => "Seeded by the installed build, not restored",
            "DatabaseVersion" => "The restoring build's database version",
            _ => null,
        };
        var differences = new List<Difference>();
        if (before.DatabaseVersion != after.DatabaseVersion)
            differences.Add(new("DatabaseVersion", $"{before.DatabaseVersion}", $"{after.DatabaseVersion}", Why("DatabaseVersion")));
        foreach (var property in typeof(Counts).GetProperties())
        {
            var a = property.GetValue(before.Counts)!.ToString()!;
            var b = property.GetValue(after.Counts)!.ToString()!;
            if (a != b)
                differences.Add(new(property.Name, a, b, Why(property.Name)));
        }
        foreach (var table in before.Tables.Select(t => t.Table).Union(after.Tables.Select(t => t.Table)))
        {
            var a = before.Tables.FirstOrDefault(t => t.Table == table);
            var b = after.Tables.FirstOrDefault(t => t.Table == table);
            if (a?.Digest != b?.Digest)
            {
                // The revisions digest includes the bundled SRD, so a build with other SRD revisions differs there too.
                var expected = Why($"table {table}")
                    ?? (table == "content_revisions" && before.Counts.RevisionsOwn == after.Counts.RevisionsOwn && before.Counts.RevisionsBundledSrd != after.Counts.RevisionsBundledSrd
                        ? "Only the bundled SRD revisions differ (another build's packs)" : null);
                differences.Add(new($"table {table}", a is null ? "absent" : $"{a.Rows} rows, {a.Digest[..12]}", b is null ? "absent" : $"{b.Rows} rows, {b.Digest[..12]}", expected));
            }
        }
        return differences;
    }

    /// <summary>A readable table of a report, for the console.</summary>
    public static string Format(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.AppendLine($"TomeStack restore drill report (format {report.FormatVersion}), database v{report.DatabaseVersion}, taken {report.TakenAt:yyyy-MM-dd HH:mm} UTC");
        foreach (var property in typeof(Counts).GetProperties())
            text.AppendLine($"  {property.Name,-24} {property.GetValue(report.Counts),12}");
        text.AppendLine("  Table digests (SHA-256 over sorted id:hash):");
        foreach (var table in report.Tables)
            text.AppendLine($"  {table.Table,-24} {table.Rows,8} rows  {table.Digest}");
        return text.ToString();
    }
}
