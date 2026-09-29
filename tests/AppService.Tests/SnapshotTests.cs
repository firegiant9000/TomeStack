using System.IO.Compression;
using Microsoft.Data.Sqlite;
using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M5 slice 8 (B08; owner decision LIVING_SPECS D14: by hand only, not in backups): character snapshots. Insert-only
/// (database migration v7), restored after a preview with a one-use token, with an undo snapshot in the same
/// transaction. All content is original.
/// </summary>
public class SnapshotTests
{
    private static ContentRevision Draft(SourceRecord source, Guid contentId, string name, string initiative) => new()
    {
        ContentId = contentId,
        RevisionId = Guid.Empty,
        Kind = ContentKind.Class,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(source.Id),
        Status = RevisionStatus.Draft,
        Effects = [new HitDieEffect { Id = "hd", Die = 8 }, new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = initiative }],
    };

    private static (TempApp Temp, Character Hero, ContentReference Class) Setup(string label = "Test Snapshot Source")
    {
        var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new(label, [RulesFamilies.Srd521]));
        var cls = temp.App.Publish(temp.App.SaveDraft(Draft(source, Guid.NewGuid(), "Test Snapshot Class", "CLASS_LEVEL"))).Published;
        var hero = temp.App.CreateCharacter(new("Test Snapshot Hero", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null, [new(cls, 2)])).Character;
        return (temp, hero, cls);
    }

    [Fact]
    public void A_snapshot_is_restored_after_a_preview_with_an_undo_snapshot_and_a_one_use_token()
    {
        var (temp, hero, cls) = Setup();
        using var _ = temp;
        var app = temp.App;
        var taken = app.Snapshot(new(hero.Id, "  Before level 5  "));
        Assert.Equal(("Before level 5", SnapshotReason.Manual, 2), (taken.Label, taken.Reason, taken.Level));

        app.SaveCharacter(app.GetCharacter(hero.Id).Character with { Classes = [new(cls, 5)] });
        var preview = app.PreviewRestore(new(hero.Id, taken.Id));
        var change = Assert.Single(preview.Fields, f => f.Field == FieldIds.Initiative);
        Assert.Equal((5, 2), (change.Before, change.After));
        Assert.Equal(5, app.GetCharacter(hero.Id).Sheet.Field(FieldIds.Initiative).Value); // a preview changes nothing

        var restored = app.RestoreSnapshot(new(preview.Token, Confirm: true));
        Assert.Equal([new ClassLevel(cls, 2)], restored.View.Character.Classes);
        Assert.Equal(hero.Id, restored.View.Character.Id);
        Assert.Equal(SnapshotReason.BeforeRestore, restored.Undo.Reason);
        Assert.Equal(5, restored.Undo.Level); // the undo snapshot holds the state the restore replaced

        // The same token again does nothing.
        var before = TempApp.Json(new { characters = app.Store.ListCharacters(), snapshots = app.Store.ListSnapshots(hero.Id) });
        Assert.Equal("snapshot.token-unknown", Assert.Throws<AppValidationException>(() => app.RestoreSnapshot(new(preview.Token, Confirm: true))).Problems.Single().Code);
        Assert.Equal(before, TempApp.Json(new { characters = app.Store.ListCharacters(), snapshots = app.Store.ListSnapshots(hero.Id) }));
        Assert.Equal([restored.Undo.Id, taken.Id], app.Snapshots(hero.Id).Select(s => s.Id)); // newest first

        // The undo snapshot can itself be restored.
        var back = app.RestoreSnapshot(new(app.PreviewRestore(new(hero.Id, restored.Undo.Id)).Token, Confirm: true));
        Assert.Equal([new ClassLevel(cls, 5)], back.View.Character.Classes);
    }

    [Fact]
    public void A_restore_needs_confirmation_and_the_character_as_it_was_at_the_preview()
    {
        var (temp, hero, cls) = Setup();
        using var _ = temp;
        var app = temp.App;
        var taken = app.Snapshot(new(hero.Id));
        var preview = app.PreviewRestore(new(hero.Id, taken.Id));

        Assert.Equal("snapshot.confirm-required", Assert.Throws<AppValidationException>(() => app.RestoreSnapshot(new(preview.Token))).Problems.Single().Code);
        app.SaveCharacter(app.GetCharacter(hero.Id).Character with { Classes = [new(cls, 3)] });
        Assert.Equal("snapshot.character-changed", Assert.Throws<AppValidationException>(() => app.RestoreSnapshot(new(preview.Token, Confirm: true))).Problems.Single().Code);
        Assert.Equal([new ClassLevel(cls, 3)], app.GetCharacter(hero.Id).Character.Classes);
        Assert.Single(app.Snapshots(hero.Id)); // no undo snapshot: nothing was restored

        var other = app.CreateCharacter(new("Test Other", RulesFamilies.Srd521, new(10, 10, 10, 10, 10, 10), null)).Character;
        Assert.Equal("snapshot.not-found", Assert.Throws<AppValidationException>(() => app.PreviewRestore(new(other.Id, taken.Id))).Problems.Single().Code);
        Assert.Equal("snapshot.label-too-long", Assert.Throws<AppValidationException>(() => app.Snapshot(new(hero.Id, new string('x', 201)))).Problems.Single().Code);
    }

    [Fact]
    public void A_snapshot_never_carries_the_archive_mark_and_a_restore_never_archives_or_unarchives()
    {
        var (temp, hero, _) = Setup();
        using var __ = temp;
        var app = temp.App;
        app.Archive(new(hero.Id, Confirm: true));
        var ofArchived = app.Snapshot(new(hero.Id));
        Assert.Null(app.Store.FindSnapshot(ofArchived.Id)!.Character.ArchivedAt);

        app.Unarchive(hero.Id);
        app.RestoreSnapshot(new(app.PreviewRestore(new(hero.Id, ofArchived.Id)).Token, Confirm: true));
        Assert.Null(app.GetCharacter(hero.Id).Character.ArchivedAt); // still unarchived

        var ofActive = app.Snapshot(new(hero.Id));
        app.Archive(new(hero.Id, Confirm: true));
        app.RestoreSnapshot(new(app.PreviewRestore(new(hero.Id, ofActive.Id)).Token, Confirm: true));
        Assert.NotNull(app.GetCharacter(hero.Id).Character.ArchivedAt); // still archived
    }

    [Fact]
    public void A_restore_keeps_the_campaign_membership_and_shows_a_name_it_brings_back()
    {
        var (temp, hero, _) = Setup();
        using var __ = temp;
        var app = temp.App;
        var campaign = app.SaveCampaign(new Campaign { Id = Guid.Empty, Name = "Test Table", RulesFamily = RulesFamilies.Srd521 });
        app.SaveCharacter(app.GetCharacter(hero.Id).Character with { CampaignId = campaign.Id });
        var inCampaign = app.Snapshot(new(hero.Id));

        // The character leaves the campaign, which is then deleted, and is renamed.
        app.SaveCharacter(app.GetCharacter(hero.Id).Character with { CampaignId = null, Name = "Test Renamed Hero" });
        app.DeleteCampaign(campaign.Id);

        var preview = app.PreviewRestore(new(hero.Id, inCampaign.Id));
        Assert.Equal("Test Snapshot Hero", preview.NameAfter);
        var restored = app.RestoreSnapshot(new(preview.Token, Confirm: true)).View.Character;
        Assert.Null(restored.CampaignId); // never back into a deleted campaign, or out of the current one
        Assert.Equal("Test Snapshot Hero", restored.Name);
    }

    [Fact]
    public void A_restore_that_fails_leaves_no_undo_snapshot_and_a_newer_preview_replaces_an_older_token()
    {
        var (temp, hero, cls) = Setup();
        using var __ = temp;
        var app = temp.App;
        var tooMany = app.GetCharacter(hero.Id).Character with
        {
            Spells = [.. Enumerable.Range(0, Character.MaxSpells + 1).Select(_ => new KnownSpell(cls.ContentId, new(Guid.NewGuid(), Guid.NewGuid())))],
        };
        var broken = new CharacterSnapshot(Guid.NewGuid(), hero.Id, TempApp.Now, SnapshotReason.Manual, "Too many spells", tooMany);
        app.Store.InTransaction(() => app.Store.AddSnapshot(broken));
        var before = TempApp.Json(new { character = app.Store.FindCharacter(hero.Id), snapshots = app.Store.ListSnapshots(hero.Id) });

        Assert.Throws<AppValidationException>(() => app.RestoreSnapshot(new(app.PreviewRestore(new(hero.Id, broken.Id)).Token, Confirm: true)));
        Assert.Equal(before, TempApp.Json(new { character = app.Store.FindCharacter(hero.Id), snapshots = app.Store.ListSnapshots(hero.Id) })); // the undo row rolled back

        var good = app.Snapshot(new(hero.Id));
        var first = app.PreviewRestore(new(hero.Id, good.Id));
        var second = app.PreviewRestore(new(hero.Id, good.Id));
        Assert.Equal("snapshot.token-unknown", Assert.Throws<AppValidationException>(() => app.RestoreSnapshot(new(first.Token, Confirm: true))).Problems.Single().Code);
        app.RestoreSnapshot(new(second.Token, Confirm: true));
    }

    [Fact]
    public void A_snapshot_pinning_content_that_is_not_installed_restores_with_content_missing()
    {
        var (temp, hero, _) = Setup();
        using var __ = temp;
        var app = temp.App;
        var gone = new ContentReference(Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new CharacterSnapshot(Guid.NewGuid(), hero.Id, TempApp.Now, SnapshotReason.Manual, "From elsewhere", app.GetCharacter(hero.Id).Character with { Pins = [gone] });
        app.Store.InTransaction(() => app.Store.AddSnapshot(snapshot));

        var preview = app.PreviewRestore(new(hero.Id, snapshot.Id));
        Assert.Contains(preview.NewDiagnostics, d => d.Code == "content.missing" && d.Content == gone);
        Assert.Contains(gone, preview.Added);

        var restored = app.RestoreSnapshot(new(preview.Token, Confirm: true));
        Assert.Contains(restored.View.Sheet.Diagnostics, d => d.Code == "content.missing" && d.Content == gone);
    }

    [Fact]
    public void Snapshots_are_insert_only_and_in_no_package_or_library_backup()
    {
        var (temp, hero, _) = Setup();
        using var __ = temp;
        var app = temp.App;
        const string label = "Test-snapshot-label-5fd5";
        app.Snapshot(new(hero.Id, label));

        using (var connection = new SqliteConnection($"Data Source={Path.Combine(temp.Directory, TomeStackApp.DatabaseFileName)};Pooling=False"))
        {
            connection.Open();
            foreach (var sql in new[] { "UPDATE character_snapshots SET json = '{}';", "DELETE FROM character_snapshots;" })
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                Assert.Contains("insert-only", Assert.Throws<SqliteException>(() => command.ExecuteNonQuery()).Message, StringComparison.Ordinal);
            }
        }
        Assert.Single(app.Snapshots(hero.Id));

        static string Text(byte[] zip)
        {
            using var archive = new ZipArchive(new MemoryStream(zip));
            return string.Concat(archive.Entries.Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
        }
        Assert.DoesNotContain(label, Text(app.ExportCharacters([hero.Id], ExportPurpose.Backup).Content), StringComparison.Ordinal);
        Assert.DoesNotContain(label, Text(app.ExportCharacters([hero.Id], ExportPurpose.Share).Content), StringComparison.Ordinal);
        using var backup = new MemoryStream();
        app.WriteLibraryBackup(backup);
        Assert.DoesNotContain(label, Text(backup.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_6_data_folder_upgrades_to_version_7_after_a_backup_and_keeps_its_characters()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tomestack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, TomeStackApp.DatabaseFileName);
        try
        {
            // A character saved by a v6 build (review fix: the upgrade must keep existing rows, not only work on new ones).
            var old = new Character { Id = Guid.NewGuid(), Name = "Test From v6", RulesFamily = RulesFamilies.Srd521, BaseAbilities = new(10, 10, 10, 10, 10, 10), Level = 3 };
            using (var v6 = new SqliteStore(database, SqliteStore.Migrations[..6]))
            {
                Assert.Equal(6, v6.SchemaVersion);
                v6.InTransaction(() => v6.SaveCharacter(old));
            }

            using (var app = TomeStackApp.Open(directory, new FixedTime(TempApp.Now), syncRoots: []))
            {
                Assert.Equal(7, app.GetInfo().SchemaVersion);
                Assert.Equal(TempApp.Json(old), TempApp.Json(app.Store.FindCharacter(old.Id)));
                Assert.Single([app.Snapshot(new(old.Id))]);
            }
            var backup = SqliteStore.BackupPath(database, 6);
            using var check = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
            check.Open();
            using var count = check.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM characters;";
            Assert.Equal(1L, count.ExecuteScalar()); // the v6 copy holds the character too
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }
}
