using Microsoft.Data.Sqlite;
using TomeStack.AppService.Persistence;
using TomeStack.ImportWorker;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Wave 0/1 minor (R82, owner-approved migration v11): <c>import_candidates</c> gains indexed <c>kind</c>, <c>page</c>, <c>page_end</c>,
/// <c>name</c> and <c>confidence</c> columns (with the existing <c>status</c>) so a candidate page turn reads only its page. The JSON
/// stays authoritative; every writer sets both. Invented rows only.
/// </summary>
public class ImportCandidateColumnsTests
{
    private static readonly Guid Job = Guid.Parse("00000000-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherJob = Guid.Parse("00000000-0000-4000-8000-0000000000a2");

    private static string NewDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, TomeStackApp.DatabaseFileName);
    }

    private static DraftCandidate Draft(Guid id, int page, int? end, ContentKind kind, string name, double confidence) =>
        new(id, Guid.Empty, new PageRef(page, end), "Fixture excerpt", kind, name, [], [], confidence, []);

    private static StoredCandidate Stored(Guid job, int n, int page, int? end, ContentKind kind, string name, double confidence, CandidateStatus status = CandidateStatus.Pending, DraftCandidate? edited = null)
    {
        var id = new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);
        return new StoredCandidate { Id = id, JobId = job, Candidate = Draft(id, page, end, kind, name, confidence), Edited = edited, Status = status, UpdatedAt = TempApp.Now };
    }

    private static List<List<object?>> Rows(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<List<object?>>();
        while (reader.Read())
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i))]);
        return rows;
    }

    /// <summary>A database at the version before this migration, with candidate rows written by raw SQL (as 0.5.0 stored them: no columns beyond status).</summary>
    private static string PreMigrationDatabase(params StoredCandidate[] rows)
    {
        var database = NewDatabase();
        using (new SqliteStore(database, SqliteStore.Migrations[..10])) { }
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            foreach (var row in rows)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO import_candidates (id, job_id, status, json) VALUES ($id, $job, $status, $json);";
                command.Parameters.AddWithValue("$id", row.Id.ToString("D"));
                command.Parameters.AddWithValue("$job", row.JobId.ToString("D"));
                command.Parameters.AddWithValue("$status", row.Status.ToString());
                command.Parameters.AddWithValue("$json", SqliteStore.Serialize(row));
                command.ExecuteNonQuery();
            }
        }
        return database;
    }

    [Fact]
    public void The_migration_is_the_next_version_and_adds_the_columns_and_the_index()
    {
        Assert.Equal(11, SqliteStore.LatestSchemaVersion);
        var database = PreMigrationDatabase();
        Assert.DoesNotContain(Rows(database, "SELECT name FROM pragma_table_info('import_candidates')"), r => (string?)r[0] is "kind" or "page");

        using (var migrated = new SqliteStore(database))
            Assert.Equal(11, migrated.SchemaVersion);

        var columns = Rows(database, "SELECT name FROM pragma_table_info('import_candidates')").Select(r => (string)r[0]!).ToList();
        Assert.Contains("kind", columns);
        Assert.Contains("page", columns);
        Assert.Contains("page_end", columns);
        Assert.Contains("name", columns);
        Assert.Contains("confidence", columns);
        Assert.Contains("status", columns);
        Assert.Contains(Rows(database, "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'import_candidates'"), r => (string?)r[0] == "ix_import_candidates_page");
        // R124: the index covers the page-turn query (filters, order and the columns it returns), so a turn reads the index alone.
        Assert.Equal(["job_id", "status", "kind", "page", "page_end", "name", "confidence"],
            Rows(database, "SELECT name FROM pragma_index_info('ix_import_candidates_page') ORDER BY seqno").Select(r => (string)r[0]!).Take(7));
        var plan = string.Join(" | ", Rows(database,
            "EXPLAIN QUERY PLAN SELECT id, page, name FROM import_candidates WHERE job_id = 'x' AND status = 'Pending' AND kind = 'Spell' AND page_end >= 2 AND page <= 9 AND confidence >= 0.5 ORDER BY rowid")
            .Select(r => (string)r[3]!));
        Assert.Contains("USING COVERING INDEX ix_import_candidates_page", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void The_backfill_reads_the_columns_from_the_stored_json_using_the_edited_version_when_there_is_one()
    {
        var plain = Stored(Job, 1, 4, null, ContentKind.Spell, "Fixture Plain", 0.85);
        var spanning = Stored(Job, 2, 6, 8, ContentKind.Item, "Fixture Span", 0.5, CandidateStatus.Accepted);
        var edited = Stored(Job, 3, 2, null, ContentKind.Feat, "Fixture Before", 0.9, edited: Draft(new Guid(3, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), 9, 11, ContentKind.Class, "Fixture After", 0.9));
        var ignored = Stored(OtherJob, 4, 1, null, ContentKind.Feature, "Fixture Ignored", 0.25, CandidateStatus.Ignored);
        var database = PreMigrationDatabase(plain, spanning, edited, ignored);

        using (new SqliteStore(database)) { }

        var rows = Rows(database, "SELECT id, status, kind, page, page_end, name, confidence FROM import_candidates ORDER BY id")
            .ToDictionary(r => Guid.Parse((string)r[0]!), r => (Status: (string)r[1]!, Kind: (string)r[2]!, Page: (long)r[3]!, End: (long)r[4]!, Name: (string)r[5]!, Confidence: (double)r[6]!));
        Assert.Equal(("Pending", "Spell", 4L, 4L, "Fixture Plain", 0.85), rows[plain.Id]);
        Assert.Equal(("Accepted", "Item", 6L, 8L, "Fixture Span", 0.5), rows[spanning.Id]);
        Assert.Equal(("Pending", "Class", 9L, 11L, "Fixture After", 0.9), rows[edited.Id]); // the edited version decides, as Current does
        Assert.Equal(("Ignored", "Feature", 1L, 1L, "Fixture Ignored", 0.25), rows[ignored.Id]);
    }

    [Fact]
    public void A_row_whose_json_is_null_or_malformed_keeps_the_defaults_and_the_migration_still_completes()
    {
        var good = Stored(Job, 1, 4, null, ContentKind.Spell, "Fixture Good", 0.85);
        var database = PreMigrationDatabase(good);
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            // Task 8a fix round 1 (M3): also a row whose candidate is null or whose name reads as null (not NULL-able in the column).
            var nameless = SqliteStore.Serialize(Stored(Job, 9, 4, null, ContentKind.Spell, "Fixture Nameless", 0.85));
            Assert.Contains("\"proposedName\":\"Fixture Nameless\"", nameless, StringComparison.Ordinal);
            nameless = nameless.Replace("\"proposedName\":\"Fixture Nameless\"", "\"proposedName\":null", StringComparison.Ordinal);
            var kindless = SqliteStore.Serialize(Stored(Job, 10, 4, null, ContentKind.Spell, "Fixture Kindless", 0.85));
            Assert.Contains("\"proposedKind\":\"spell\"", kindless, StringComparison.Ordinal);
            kindless = kindless.Replace("\"proposedKind\":\"spell\"", "\"proposedKind\":null", StringComparison.Ordinal);
            foreach (var (id, json) in new[]
            {
                ("00000000-0000-4000-8000-0000000000b1", "null"), ("00000000-0000-4000-8000-0000000000b2", "{not json"),
                ("00000000-0000-4000-8000-0000000000b3", $"{{\"id\":\"{Guid.NewGuid()}\",\"jobId\":\"{Job}\",\"candidate\":null}}"), ("00000000-0000-4000-8000-0000000000b4", nameless),
                ("00000000-0000-4000-8000-0000000000b5", kindless), ("00000000-0000-4000-8000-0000000000b6", "{}"),
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO import_candidates (id, job_id, status, json) VALUES ($id, $job, 'Pending', $json);";
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$job", Job.ToString("D"));
                command.Parameters.AddWithValue("$json", json);
                command.ExecuteNonQuery();
            }
        }

        using (var migrated = new SqliteStore(database))
            Assert.Equal(11, migrated.SchemaVersion);

        var rows = Rows(database, "SELECT id, kind, page, name FROM import_candidates ORDER BY id");
        Assert.Equal(7, rows.Count);
        var goodRow = rows.Single(r => (string?)r[0] == good.Id.ToString("D"));
        Assert.Equal(("Spell", 4L, "Fixture Good"), ((string)goodRow[1]!, (long)goodRow[2]!, (string)goodRow[3]!));
        var defaults = rows.Where(r => (string?)r[0] != good.Id.ToString("D")).ToList();
        Assert.Equal(6, defaults.Count);
        Assert.All(defaults, r => Assert.Equal(("", 0L, ""), ((string)r[1]!, (long)r[2]!, (string)r[3]!))); // untouched: the column defaults
    }

    [Fact]
    public void Every_writer_sets_the_columns_with_the_json_and_a_resave_moves_them()
    {
        var database = NewDatabase();
        using var store = new SqliteStore(database);
        var row = Stored(Job, 7, 3, 5, ContentKind.Spell, "Fixture Row", 0.75);
        store.SaveImportCandidate(row);
        Assert.Equal(("Pending", "Spell", 3L, 5L, "Fixture Row", 0.75), Columns(database, row.Id));

        store.SaveImportCandidate(row with { Status = CandidateStatus.Ignored, Edited = Draft(row.Id, 12, null, ContentKind.Feat, "Fixture Renamed", 0.75) });

        Assert.Equal(("Ignored", "Feat", 12L, 12L, "Fixture Renamed", 0.75), Columns(database, row.Id));
        Assert.Equal("Fixture Renamed", store.FindImportCandidate(row.Id)!.Current.ProposedName); // the JSON agrees
    }

    private static (string, string, long, long, string, double) Columns(string database, Guid id)
    {
        // Read through a second connection while the store is open: WAL allows it.
        var r = Rows(database.Replace("\\", "/"), $"SELECT status, kind, page, page_end, name, confidence FROM import_candidates WHERE id = '{id:D}'").Single();
        return ((string)r[0]!, (string)r[1]!, (long)r[2]!, (long)r[3]!, (string)r[4]!, (double)r[5]!);
    }

    /// <summary>The old in-memory filter, verbatim, over every row of the job: the reference the SQL page must equal.</summary>
    private static List<Guid> Reference(SqliteStore store, Guid job, ImportCandidateFilter f) =>
        [.. store.ListImportCandidates(job)
            .Where(c => f.FirstPage is not { } from || (c.Current.Page.End ?? c.Current.Page.Start) >= from)
            .Where(c => f.LastPage is not { } to || c.Current.Page.Start <= to)
            .Where(c => f.Kind is not { } kind || c.Current.ProposedKind == kind)
            .Where(c => f.MinConfidence is not { } min || c.Current.Confidence >= min)
            .Where(c => f.MaxConfidence is not { } max || c.Current.Confidence <= max)
            .Where(c => f.Status is not { } status || c.Status == status)
            .OrderBy(c => c.Current.Page.Start).ThenBy(c => c.Current.ProposedName, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => c.Id)];

    private static SqliteStore SeededStore(out int count)
    {
        var store = new SqliteStore(NewDatabase());
        var kinds = new[] { ContentKind.Spell, ContentKind.Feat, ContentKind.Item, ContentKind.Class, ContentKind.Feature };
        var names = new[] { "fixture alder", "Fixture Alder", "Fixture alder", "FIXTURE BIRCH", "Fixture Élan", "Fixture Zed", "fixture Cedar" };
        var statuses = new[] { CandidateStatus.Pending, CandidateStatus.Pending, CandidateStatus.Accepted, CandidateStatus.Ignored, CandidateStatus.AcceptedAsReference };
        var n = 0;
        for (var i = 0; i < 90; i++)
        {
            n++;
            var page = 1 + i % 12;
            var edited = i % 7 == 3 ? Draft(new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), page + 2, page + 4, kinds[(i + 2) % kinds.Length], names[(i + 3) % names.Length], 0.1 * (i % 10)) : null;
            store.SaveImportCandidate(Stored(Job, n, page, i % 5 == 0 ? page + 1 : null, kinds[i % kinds.Length], names[i % names.Length], Math.Round(0.1 * (i % 10), 1), statuses[i % statuses.Length], edited));
        }
        store.SaveImportCandidate(Stored(OtherJob, ++n, 3, null, ContentKind.Spell, "Fixture Elsewhere", 0.5)); // another job never leaks in
        count = n - 1;
        return store;
    }

    public static TheoryData<int> FilterCases() => [.. Enumerable.Range(0, 12)];

    private static ImportCandidateFilter Case(int n) => n switch
    {
        0 => new(),
        1 => new(Status: CandidateStatus.Pending),
        2 => new(Kind: ContentKind.Spell),
        3 => new(FirstPage: 5),
        4 => new(LastPage: 4),
        5 => new(FirstPage: 3, LastPage: 3),
        6 => new(MinConfidence: 0.7),
        7 => new(MaxConfidence: 0.3),
        8 => new(Kind: ContentKind.Class, Status: CandidateStatus.Pending, FirstPage: 2, LastPage: 9),
        9 => new(MinConfidence: 0.2, MaxConfidence: 0.6, Status: CandidateStatus.Accepted),
        10 => new(FirstPage: 99),
        _ => new(Kind: ContentKind.Item, MaxConfidence: 0.0),
    };

    [Theory]
    [MemberData(nameof(FilterCases))]
    public void A_page_equals_the_old_in_memory_filter_in_order_and_in_total(int filterCase)
    {
        using var store = SeededStore(out _);
        var filter = Case(filterCase);
        var everything = Reference(store, Job, filter);

        var whole = store.QueryImportCandidates(Job, filter, 0, null);
        Assert.Equal(everything, whole.Items.Select(c => c.Id));
        Assert.Equal(everything.Count, whole.Total);

        foreach (var (offset, limit) in new[] { (0, 7), (7, 7), (14, 50), (everything.Count, 5), (Math.Max(0, everything.Count - 3), 200) })
        {
            var page = store.QueryImportCandidates(Job, filter, offset, limit);
            Assert.Equal(everything.Skip(offset).Take(limit), page.Items.Select(c => c.Id));
            Assert.Equal(everything.Count, page.Total);
            Assert.All(page.Items, c => Assert.False(string.IsNullOrEmpty(c.Etag)));
        }
    }

    [Fact]
    public void A_page_turn_reads_the_json_of_its_page_only()
    {
        using var store = SeededStore(out var count);
        Assert.True(count >= 60);
        var before = store.CandidateJsonRowsRead;

        var page = store.QueryImportCandidates(Job, new(), 20, 10);

        Assert.Equal(10, page.Items.Count);
        Assert.Equal(count, page.Total);
        Assert.Equal(10, store.CandidateJsonRowsRead - before);
    }

    [Fact]
    public void A_migrated_database_pages_the_same_as_one_written_by_the_new_code()
    {
        var rows = Enumerable.Range(1, 40).Select(i => Stored(Job, i, 1 + i % 6, i % 4 == 0 ? 2 + i % 6 : null, i % 2 == 0 ? ContentKind.Spell : ContentKind.Item, $"Fixture Row {i % 9}", 0.1 * (i % 10), i % 3 == 0 ? CandidateStatus.Ignored : CandidateStatus.Pending)).ToArray();
        var database = PreMigrationDatabase(rows);
        using var store = new SqliteStore(database);

        foreach (var filter in new ImportCandidateFilter[] { new(), new(Kind: ContentKind.Spell, Status: CandidateStatus.Pending), new(FirstPage: 3, LastPage: 5), new(MinConfidence: 0.4) })
            Assert.Equal(Reference(store, Job, filter), store.QueryImportCandidates(Job, filter, 0, null).Items.Select(c => c.Id));
    }
}
