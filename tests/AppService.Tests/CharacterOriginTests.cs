using Microsoft.Data.Sqlite;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Wave 3 (D95, migration v12): <c>characters.origin</c> marks the bundled sample character; invented rows only.</summary>
public class CharacterOriginTests
{
    private static Character Courier(string name = "Test Courier") =>
        TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { Id = Guid.NewGuid(), Name = name };

    private static GapNote Note(Guid characterId) => new()
    {
        Id = Guid.NewGuid(),
        CharacterId = characterId,
        Target = new GapTarget(GapTargetKind.Field, FieldId: "fixture"),
        Text = "Fixture note",
        CreatedAt = TempApp.Now,
        UpdatedAt = TempApp.Now,
    };

    [Fact]
    public void A_v11_database_upgrades_to_12_with_origin_null_on_existing_characters()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        var hero = Courier();
        using (var old = new SqliteStore(database, SqliteStore.Migrations[..11]))
        {
            Assert.Equal(11, old.SchemaVersion);
            old.SaveCharacter(hero);
        }

        using var migrated = new SqliteStore(database);

        Assert.Equal(SqliteStore.LatestSchemaVersion, migrated.SchemaVersion);
        Assert.NotNull(migrated.FindCharacter(hero.Id));
        Assert.Null(migrated.CharacterOrigin(hero.Id));
    }

    [Fact]
    public void Saving_a_sample_again_keeps_its_origin()
    {
        using var temp = new TempApp();
        var store = temp.App.Store;
        var sample = Courier("Test Sample");
        store.SaveCharacter(sample);
        store.SetCharacterOrigin(sample.Id, "sample");

        store.SaveCharacter(sample with { Name = "Test Sample Renamed" });

        Assert.Equal("sample", store.CharacterOrigin(sample.Id));
        Assert.Equal("Test Sample Renamed", store.FindCharacter(sample.Id)!.Name);
    }

    [Fact]
    public void DeleteSampleCharacter_removes_a_sample_and_its_gap_notes()
    {
        using var temp = new TempApp();
        var store = temp.App.Store;
        var sample = Courier("Test Sample");
        store.SaveCharacter(sample);
        store.SetCharacterOrigin(sample.Id, "sample");
        store.SaveGapNote(Note(sample.Id));

        Assert.True(store.DeleteSampleCharacter(sample.Id));

        Assert.Null(store.FindCharacter(sample.Id));
        Assert.Empty(store.ListGapNotes(sample.Id));
    }

    [Fact]
    public void DeleteSampleCharacter_leaves_a_normal_character_and_its_gap_notes()
    {
        using var temp = new TempApp();
        var store = temp.App.Store;
        var normal = Courier();
        store.SaveCharacter(normal);
        store.SaveGapNote(Note(normal.Id));

        Assert.False(store.DeleteSampleCharacter(normal.Id));
        Assert.False(store.DeleteSampleCharacter(Guid.NewGuid()));

        Assert.NotNull(store.FindCharacter(normal.Id));
        Assert.Single(store.ListGapNotes(normal.Id));
    }

    [Fact]
    public void DeleteSampleCharacter_refuses_a_sample_with_snapshots_and_removes_nothing()
    {
        using var temp = new TempApp();
        var store = temp.App.Store;
        var sample = Courier("Test Sample");
        store.SaveCharacter(sample);
        store.SetCharacterOrigin(sample.Id, "sample");
        store.SaveGapNote(Note(sample.Id));
        store.InTransaction(() => store.AddSnapshot(new CharacterSnapshot(Guid.NewGuid(), sample.Id, TempApp.Now, SnapshotReason.Manual, null, sample)));

        Assert.Throws<SampleHasSnapshotsException>(() => store.DeleteSampleCharacter(sample.Id));

        Assert.NotNull(store.FindCharacter(sample.Id));
        Assert.Single(store.ListGapNotes(sample.Id));
        Assert.Equal(1, store.SnapshotCount(sample.Id));
    }

    [Fact]
    public void SetCharacterOrigin_returns_false_for_an_unknown_id_and_true_for_a_stored_one()
    {
        using var temp = new TempApp();
        var store = temp.App.Store;
        var sample = Courier("Test Sample");
        store.SaveCharacter(sample);

        Assert.False(store.SetCharacterOrigin(Guid.NewGuid(), "sample"));
        Assert.True(store.SetCharacterOrigin(sample.Id, "sample"));
    }

    [Fact]
    public void SnapshotCount_counts_one_characters_snapshots()
    {
        using var temp = new TempApp();
        var store = temp.App.Store;
        var first = Courier("Test First");
        var second = Courier("Test Second");
        store.SaveCharacter(first);
        store.SaveCharacter(second);
        Assert.Equal(0, store.SnapshotCount(first.Id));

        store.InTransaction(() =>
        {
            store.AddSnapshot(new CharacterSnapshot(Guid.NewGuid(), first.Id, TempApp.Now, SnapshotReason.Manual, null, first));
            store.AddSnapshot(new CharacterSnapshot(Guid.NewGuid(), first.Id, TempApp.Now, SnapshotReason.Manual, "Fixture", first));
            store.AddSnapshot(new CharacterSnapshot(Guid.NewGuid(), second.Id, TempApp.Now, SnapshotReason.Manual, null, second));
        });

        Assert.Equal(2, store.SnapshotCount(first.Id));
        Assert.Equal(1, store.SnapshotCount(second.Id));
    }
}
