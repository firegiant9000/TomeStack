using System.Text.Json;
using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Data.Sqlite;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>
/// T3 property 6 (ARCHITECTURE "numbered, forward-only migrations", ADR-002 insert-only revisions): a database written at
/// any older version (v1 to v8) and opened by this build migrates to the latest version, keeps every row and every revision
/// hash (v1's are rewritten once by the ADR-003 migration to v2, to the hash of the upcast form), and leaves exactly one
/// backup, at the version it was opened with.
/// </summary>
public class MigrationProperties
{
    /// <summary>An old database: its version and what it holds, written only with that version's tables.</summary>
    public sealed record OldDatabase(int Version, IReadOnlyList<SourceRecord> Sources, IReadOnlyList<string> Revisions, IReadOnlyList<Character> Characters, IReadOnlyList<Campaign> Campaigns, IReadOnlyList<GapNote> Notes)
    {
        public override string ToString() =>
            $"v{Version}: {Sources.Count} sources, {Revisions.Count} revisions, {Characters.Count} characters, {Campaigns.Count} campaigns, {Notes.Count} gap notes";
    }

    private static readonly DateTimeOffset Then = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static Gen<SourceRecord> Sources { get; } =
        from id in Generators.Guids
        from title in Generators.Names
        from families in Generators.ContentFamilies
        from redistributable in Gen.Elements(true, false)
        select new SourceRecord
        {
            Id = id, Title = title, Publisher = "Test homebrew", RulesFamilies = families, EditionVersion = "1",
            License = "Test license", Redistributable = redistributable,
        };

    public static Gen<OldDatabase> Databases { get; } =
        from version in Gen.Choose(1, SqliteStore.LatestSchemaVersion - 1)
        from sources in Sources.ListOf().Select(s => s.Take(3).ToList())
        // A v1 database holds v1 revisions (the build that wrote it knew no other); later ones hold any version from v2.
        from revisions in (version == 1 ? Generators.Revision(1) : Gen.Choose(2, ContentRevision.CurrentSchemaVersion).SelectMany(Generators.Revision))
            .Select(r => r.ToJsonString()).ListOf().Select(r => r.Take(10).ToList())
        from characters in Generators.Characters.ListOf().Select(c => c.Take(3).ToList())
        from campaignName in Generators.Names
        from campaignFamily in Gen.Elements(Generators.Families)
        from noteText in Generators.Names
        let campaigns = version >= 4 ? new List<Campaign> { new() { Id = Guid.NewGuid(), Name = campaignName, RulesFamily = campaignFamily, AllowedSources = [.. sources.Select(s => s.Id)], UpdatedAt = Then } } : []
        let notes = version >= 5 ? characters.Select(c => new GapNote
        {
            Id = Guid.NewGuid(), CharacterId = c.Id, Target = new(GapTargetKind.Field, FieldId: FieldIds.Initiative, Label: "Initiative"),
            Text = "Test note: " + noteText, CreatedAt = Then, UpdatedAt = Then,
        }).ToList() : []
        select new OldDatabase(version, sources, revisions, characters, campaigns, notes);

    private static long Count(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> Hashes(string database)
    {
        using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision_id, sha256 FROM content_revisions;";
        using var reader = command.ExecuteReader();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
            hashes[reader.GetString(0)] = reader.GetString(1);
        return hashes;
    }

    /// <summary>Writes the database as a build of that version did: its migrations only, and only its tables.</summary>
    private static void Write(string database, OldDatabase old)
    {
        using (var store = new SqliteStore(database, SqliteStore.Migrations[..old.Version]))
        {
            store.InTransaction(() =>
            {
                if (old.Version >= ContentRevision.TypedEffectsSchemaVersion)
                {
                    foreach (var json in old.Revisions)
                        store.AddRevision(JsonSerializer.Deserialize<ContentRevision>(json, RulesJson.Compact)!);
                }
                foreach (var character in old.Characters)
                    store.SaveCharacter(character);
                foreach (var campaign in old.Campaigns)
                    store.SaveCampaign(campaign);
                foreach (var note in old.Notes)
                    store.SaveGapNote(note);
            });
        }

        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Insert(string sql, params (string Name, string Value)[] values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in values)
                command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }
        // sources gained columns in v3 and v8 (UpsertSource writes them); (id, json) is what every version accepts.
        foreach (var source in old.Sources)
            Insert("INSERT INTO sources (id, json) VALUES ($id, $json);", ("$id", source.Id.ToString("D")), ("$json", SqliteStore.Serialize(source)));
        if (old.Version == 1)
        {
            // As a v1 build stored them: the v1 JSON as written, hashed as written.
            foreach (var json in old.Revisions)
            {
                var node = JsonNode.Parse(json)!;
                Insert(
                    "INSERT OR IGNORE INTO content_revisions (revision_id, content_id, status, sha256, json) VALUES ($rid, $cid, $status, $hash, $json);",
                    ("$rid", (string)node["revisionId"]!), ("$cid", (string)node["contentId"]!),
                    ("$status", (string)node["status"]! == "published" ? nameof(RevisionStatus.Published) : nameof(RevisionStatus.Draft)),
                    ("$hash", SqliteStore.Sha256(json)), ("$json", json));
            }
        }
        transaction.Commit();
    }

    private static readonly string[] Tables = ["sources", "content_revisions", "characters", "campaigns", "gap_notes"];

    private static Dictionary<string, long> Counts(string database, int version) =>
        Tables.Where(t => t switch { "campaigns" => version >= 4, "gap_notes" => version >= 5, _ => true })
            .ToDictionary(t => t, t => Count(database, $"SELECT COUNT(*) FROM {t};"));

    [Property(MaxTest = 60)]
    public Property A_database_of_any_older_version_migrates_keeping_its_rows_and_hashes_and_one_backup() =>
        Prop.ForAll(Databases.ToArbitrary(), old =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
                Write(database, old);
                var counts = Counts(database, old.Version);
                var hashes = Hashes(database);
                var characters = old.Characters.ToDictionary(c => c.Id, c => TempApp.Json(c));

                using (var store = new SqliteStore(database))
                {
                    Assert.Equal(SqliteStore.LatestSchemaVersion, store.SchemaVersion);
                    foreach (var (id, json) in characters)
                        Assert.Equal(json, TempApp.Json(store.FindCharacter(id)));
                    foreach (var revision in store.ListRevisionsInOrder())
                    {
                        var stored = store.RevisionHash(revision.RevisionId);
                        // Every stored hash is the hash of what the store reads back, so the insert-only check still works.
                        Assert.Equal(SqliteStore.Sha256(SqliteStore.Serialize(revision)), stored);
                        if (old.Version >= ContentRevision.TypedEffectsSchemaVersion)
                            Assert.Equal(hashes[revision.RevisionId.ToString("D")], stored);
                        else
                            Assert.Equal(ContentRevision.TypedEffectsSchemaVersion, revision.SchemaVersion);
                    }
                }
                Assert.Equal(counts, Counts(database, old.Version));

                var backups = Directory.GetFiles(directory, "*.bak");
                Assert.Equal([SqliteStore.BackupPath(database, old.Version)], backups);
                Assert.Equal(old.Version, Count(backups[0], "PRAGMA user_version;"));
                Assert.Equal(counts, Counts(backups[0], old.Version));
                Assert.Equal(hashes, Hashes(backups[0]));

                // Opening the migrated database again writes no second backup.
                using (new SqliteStore(database)) { }
                Assert.Single(Directory.GetFiles(directory, "*.bak"));
                return true;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(directory, recursive: true);
            }
        });
}
