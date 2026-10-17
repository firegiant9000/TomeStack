using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Character schema v9 (Wave 2, D42) at the service: the version each writer writes is computed from the data (R73), so a
/// character that uses no v9 field is stored, exported, backed up, snapshotted and hashed exactly as 0.5.0 did, and the largest
/// v9 character is still refused by the one size guard that keeps every saved character backup-able.
/// </summary>
public class CharacterSchemaV9PersistenceTests
{
    private static Character V8() => TempApp.LoadFixture<Character>("characters/srd521-courier-v8.json");

    private static Character V9() => TempApp.LoadFixture<Character>("characters/srd521-courier-v9.json");

    private static string StoredJson(TempApp temp, string table, Guid id)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(temp.Directory, TomeStackApp.DatabaseFileName)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT json FROM {table} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return (string)command.ExecuteScalar()!;
    }

    private static IEnumerable<(string Path, string Text)> CharacterEntries(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("characters/", StringComparison.Ordinal)).ToList())
        {
            using var reader = new StreamReader(entry.Open());
            yield return (entry.FullName, reader.ReadToEnd());
        }
    }

    /// <summary>A build that pins <c>CurrentSchemaVersion = 8</c> reads a character by its v8 schema; the entry must pass it.</summary>
    private static void AssertReadableByAV8Build(string path, string text)
    {
        Assert.Contains("\"schemaVersion\": 8", text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);
        var errors = SchemaTests.ValidateFile("character.v8.schema.json", document.RootElement);
        Assert.True(errors.Length == 0, $"{path} does not match the v8 schema: {errors}");
    }

    [Fact]
    public void The_store_writes_schema_8_for_a_character_with_no_v9_field_and_9_for_one_with_a_v9_field()
    {
        using var temp = new TempApp();
        var plain = temp.App.SaveCharacter(V8()).Character;
        var v9 = temp.App.SaveCharacter(V9()).Character;

        Assert.Contains("\"schemaVersion\":8", StoredJson(temp, "characters", plain.Id), StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":9", StoredJson(temp, "characters", v9.Id), StringComparison.Ordinal);
        Assert.Equal(8, temp.App.Store.FindCharacter(plain.Id)!.SchemaVersion);
        Assert.Equal(9, temp.App.Store.FindCharacter(v9.Id)!.SchemaVersion);
    }

    [Fact]
    public void Clearing_the_last_v9_value_and_saving_makes_the_stored_character_v8_again()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(V8() with { Xp = 100 }).Character;
        Assert.Contains("\"schemaVersion\":9", StoredJson(temp, "characters", saved.Id), StringComparison.Ordinal);

        temp.App.SaveCharacter(saved with { Xp = null });

        var json = StoredJson(temp, "characters", saved.Id);
        Assert.Contains("\"schemaVersion\":8", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"xp\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExportPurpose.Backup)]
    [InlineData(ExportPurpose.Share)]
    public void A_character_package_of_v8_characters_writes_schema_8_and_passes_the_v8_schema(ExportPurpose purpose)
    {
        using var temp = new TempApp();
        var id = temp.App.SaveCharacter(V8()).Character.Id;

        var entries = CharacterEntries(temp.App.ExportCharacters([id], purpose).Content).ToList();

        var entry = Assert.Single(entries);
        AssertReadableByAV8Build(entry.Path, entry.Text);
    }

    [Fact]
    public void A_character_package_with_a_v9_field_writes_schema_9_and_imports_into_this_build()
    {
        using var origin = new TempApp();
        var saved = origin.App.SaveCharacter(V9());
        var package = origin.App.ExportCharacters([saved.Character.Id]).Content;

        var entry = Assert.Single(CharacterEntries(package));
        Assert.Contains("\"schemaVersion\": 9", entry.Text, StringComparison.Ordinal);
        using (var document = JsonDocument.Parse(entry.Text))
            Assert.Equal("", SchemaTests.ValidateFile("character.v9.schema.json", document.RootElement));

        using var destination = new TempApp();
        destination.App.ApplyImport(package);
        Assert.Equal(TempApp.Json(saved.Character), TempApp.Json(destination.App.Store.FindCharacter(saved.Character.Id)!));
    }

    [Fact]
    public void A_library_backup_of_v8_characters_writes_schema_8_and_passes_the_v8_schema()
    {
        using var temp = new TempApp();
        temp.App.SaveCharacter(V8());
        temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json"));
        using var buffer = new MemoryStream();

        temp.App.WriteLibraryBackup(buffer);

        var entries = CharacterEntries(buffer.ToArray()).ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => AssertReadableByAV8Build(e.Path, e.Text));
    }

    [Fact]
    public void A_library_backup_with_a_v9_character_writes_it_as_schema_9()
    {
        using var temp = new TempApp();
        temp.App.SaveCharacter(V9());
        using var buffer = new MemoryStream();

        temp.App.WriteLibraryBackup(buffer);

        Assert.Contains("\"schemaVersion\": 9", Assert.Single(CharacterEntries(buffer.ToArray())).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_of_a_v8_character_holds_schema_8()
    {
        using var temp = new TempApp();
        var plain = temp.App.SaveCharacter(V8()).Character;
        var v9 = temp.App.SaveCharacter(V9()).Character;

        var plainSnapshot = temp.App.Snapshot(new(plain.Id));
        var v9Snapshot = temp.App.Snapshot(new(v9.Id));

        var plainJson = StoredJson(temp, "character_snapshots", plainSnapshot.Id);
        Assert.Contains("\"schemaVersion\":8", plainJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"schemaVersion\":9", plainJson, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":9", StoredJson(temp, "character_snapshots", v9Snapshot.Id), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rest_token_hashes_a_v8_character_as_schema_8()
    {
        var plan = new RestPlan(RestPeriod.LongRest, [], []);

        Assert.Contains("\"schemaVersion\":8", TomeStackApp.BasisText(V8(), plan), StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":9", TomeStackApp.BasisText(V9(), plan), StringComparison.Ordinal);
    }

    [Fact]
    public void A_v9_character_calculates_like_the_same_character_without_the_v9_fields()
    {
        using var temp = new TempApp();
        var v9 = V9();
        var bare = v9 with { Details = null, CustomItems = null, CustomAttacks = null, DamageDefences = null, Companions = null, Xp = null, Play = new PlayState() };

        var withV9 = temp.App.SaveCharacter(v9).Sheet;
        var without = temp.App.SaveCharacter(bare).Sheet;

        Assert.Equal(TempApp.Json(without.Fields), TempApp.Json(withV9.Fields)); // nothing in v9 is calculated
    }

    [Fact]
    public void Removing_a_companion_in_a_save_takes_its_recorded_hit_points_with_it_instead_of_being_refused()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(V9()).Character;
        Assert.Single(saved.Play.Companions!);

        // A companion that stays keeps its hit points: a save never changes play state otherwise.
        var kept = temp.App.SaveCharacter(saved with { Play = new PlayState() }).Character;
        Assert.Equal(7, Assert.Single(kept.Play.Companions!).Current);

        var without = temp.App.SaveCharacter(saved with { Companions = null }).Character;

        Assert.Null(without.Play.Companions);
        Assert.Null(temp.App.Store.FindCharacter(saved.Id)!.Play.Companions);
    }

    // ---- the size guard ----------------------------------------------------------------------------------------------

    private static string Wide(int length) => new('€', length); // 3 bytes in UTF-8 and 6 once the JSON encoder escapes it

    /// <summary>Every v9 maximum, in a script the encoder escapes, plus <paramref name="notes"/> session notes of the longest length.</summary>
    private static Character WorstCase(int notes)
    {
        Guid Id(int n) => Guid.Parse($"00000000-0000-4000-8000-{n:D12}");
        CustomAttack Attack(int n) => new(Id(n), Wide(Character.MaxName), Character.MaxToHit, "8d100+1000", Wide(Character.MaxDamageTypeLength), Wide(Character.MaxShortNote));
        var companions = Enumerable.Range(0, Character.MaxCompanions)
            .Select(i => new Companion(Id(100 + i), Wide(Character.MaxName), Wide(Character.MaxCompanionTextLength), Character.MaxCompanionArmorClass, PlayState.MaxHitPoints, Wide(Character.MaxCompanionSpeedLength),
                [.. Enumerable.Range(0, Character.MaxCompanionAttacks).Select(a => Attack(1_000 + (i * 100) + a))], Wide(Character.MaxShortNote)))
            .ToList();
        return V8() with
        {
            Details = new(
                Wide(Character.MaxDetailText), Wide(Character.MaxDetailText), Wide(Character.MaxDetailText), Wide(Character.MaxDetailText), Wide(Character.MaxDetailText),
                new(Wide(Character.MaxDetailText), Wide(Character.MaxDetailText), Wide(Character.MaxDetailText), Wide(Character.MaxDetailText), Wide(Character.MaxDetailText), Wide(Character.MaxDetailText)),
                Wide(Character.MaxDetailText), Wide(Character.MaxDetailText),
                [.. Enumerable.Repeat(Wide(Character.MaxDetailListEntryLength), Character.MaxDetailListEntries)], [.. Enumerable.Repeat(Wide(Character.MaxDetailListEntryLength), Character.MaxDetailListEntries)]),
            CustomItems = [.. Enumerable.Range(0, Character.MaxCustomItems).Select(i => new CustomItem(Id(i + 1), Wide(Character.MaxName), EquipmentEntry.MaxQuantity, Wide(Character.MaxShortNote), true))],
            CustomAttacks = [.. Enumerable.Range(0, Character.MaxCustomAttacks).Select(i => Attack(500 + i))],
            DamageDefences = [.. DamageTypes.All.SelectMany(t => new[] { DamageDefenceKind.Resistance, DamageDefenceKind.Immunity, DamageDefenceKind.Vulnerability }.Select(k => new DamageDefence(t, k)))],
            Companions = companions,
            Xp = Character.MaxXp,
            Play = V8().Play with { Companions = [.. companions.Select(c => new CompanionPlay(c.Id, PlayState.MaxHitPoints, PlayState.MaxHitPoints))] },
            Notes = [.. Enumerable.Range(0, notes).Select(i => new SessionNote(Id(10_000 + i), new DateOnly(2026, 1, 1), Wide(SessionNote.MaxText), DateTimeOffset.UnixEpoch))],
        };
    }

    private static long EntryBytes(Character character) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(character, RulesJson.Options)) + 1;

    [Fact]
    public void The_worst_case_v9_character_with_the_most_session_notes_is_refused_at_save_by_the_size_guard()
    {
        using var temp = new TempApp();
        var worst = WorstCase(Character.MaxNotes);
        Assert.Empty(worst.Validate()); // within every bound, so only the size can refuse it
        Assert.True(EntryBytes(worst) > PackageService.MaxEntryBytes, $"the worst case is {EntryBytes(worst)} bytes");

        var ex = Assert.Throws<AppValidationException>(() => temp.App.SaveCharacter(worst));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal("character.too-large", problem.Code);
        Assert.Contains("MB", problem.Message, StringComparison.Ordinal); // the limit, never the text
        Assert.DoesNotContain("€", problem.Message, StringComparison.Ordinal);
        Assert.Empty(temp.App.Store.ListCharacters());
    }

    [Fact]
    public void Every_character_that_saves_also_backs_up_even_at_the_largest_size_the_guard_allows()
    {
        using var temp = new TempApp();
        // The most notes that keep the worst-case v9 character within one package entry.
        var notes = Enumerable.Range(0, Character.MaxNotes + 1).Reverse().First(n => EntryBytes(WorstCase(n)) <= PackageService.MaxEntryBytes);
        var largest = WorstCase(notes);
        Assert.True(notes < Character.MaxNotes && EntryBytes(WorstCase(notes + 1)) > PackageService.MaxEntryBytes, "one more note would be refused");

        var saved = temp.App.SaveCharacter(largest).Character;

        Assert.Equal(9, saved.SchemaVersion);
        Assert.Single(CharacterEntries(temp.App.ExportCharacters([saved.Id]).Content)); // a backup holds it
        using var buffer = new MemoryStream();
        temp.App.WriteLibraryBackup(buffer);
        Assert.Single(CharacterEntries(buffer.ToArray()));
        using var restored = new TempApp();
        restored.App.ApplyImport(temp.App.ExportCharacters([saved.Id]).Content); // and it restores
        Assert.NotNull(restored.App.Store.FindCharacter(saved.Id));
    }

    [Fact]
    public void The_entry_too_large_message_names_the_general_cause_and_no_text()
    {
        var ex = Assert.Throws<PackageException>(() => PackageService.RequireEntriesWithinLimit([("characters/00000000-0000-4000-8000-000000000001.json", PackageService.MaxEntryBytes + 1)]));

        var problem = Assert.Single(ex.Errors);
        Assert.Equal("package.entry-too-large", problem.Code);
        Assert.DoesNotContain("session notes", problem.Message, StringComparison.OrdinalIgnoreCase); // v9 text counts too
        Assert.Contains("long text", problem.Message, StringComparison.Ordinal);
    }
}
