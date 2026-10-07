using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// SPEC C-08 (audit 2026-09-28): a character is archived, not deleted. Archiving needs the confirmation that follows the
/// preview, removes nothing the character or a backup depends on, and a full library backup carries archived characters.
/// </summary>
public class ArchiveTests
{
    private static (TempApp Temp, Guid Id) Setup()
    {
        var temp = new TempApp();
        var character = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd51-quickfoot.json")).Character;
        temp.App.AddGapNote(new(character.Id, new(GapTargetKind.Field, FieldId: FieldIds.ArmorClass), "Test note: cover bonus."));
        temp.App.Play(new(character.Id, PlayActionKind.Damage, Confirm: true, Amount: 3));
        return (temp, character.Id);
    }

    [Fact]
    public void Archiving_needs_the_confirmation_and_the_preview_says_what_stays()
    {
        var (temp, id) = Setup();
        using var _t = temp;
        var before = temp.App.Store.FindCharacter(id)!;

        var preview = temp.App.PreviewArchive(id);
        Assert.Equal((id, before.Name, false, 1), (preview.CharacterId, preview.Name, preview.AlreadyArchived, preview.GapNotes));

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Archive(new(id)));
        Assert.Equal("character.archive-confirmation-required", Assert.Single(refused.Problems).Code);
        Assert.Null(temp.App.Store.FindCharacter(id)!.ArchivedAt); // the preview and the refusal wrote nothing
    }

    [Fact]
    public void An_archived_character_keeps_everything_and_comes_back_as_it_was()
    {
        var (temp, id) = Setup();
        using var _t = temp;
        temp.App.SaveCharacter(temp.App.Store.FindCharacter(id)! with { Level = 5 }); // a level above the default of 1
        var before = temp.App.Store.FindCharacter(id)!;
        var sheet = TempApp.Json(temp.App.GetCharacter(id).Sheet);

        var archived = temp.App.Archive(new(id, Confirm: true));

        Assert.NotNull(archived.ArchivedAt);
        Assert.Equal(archived.ArchivedAt, Assert.Single(temp.App.ListCharacters(), c => c.Id == id).ArchivedAt);
        Assert.Equal((5, 5), (archived.Level, Assert.Single(temp.App.ListCharacters(), c => c.Id == id).Level)); // D25
        var stored = temp.App.Store.FindCharacter(id)!;
        Assert.Equal(TempApp.Json(before with { ArchivedAt = stored.ArchivedAt }), TempApp.Json(stored)); // play state, choices and UpdatedAt unchanged
        Assert.Single(temp.App.ListGapNotes(id));
        Assert.Equal(sheet, TempApp.Json(temp.App.GetCharacter(id).Sheet)); // still opens, same sheet
        Assert.Equal("character.already-archived", Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.Archive(new(id, Confirm: true))).Problems).Code);

        var back = temp.App.Unarchive(id);

        Assert.Null(back.ArchivedAt);
        Assert.Equal(TempApp.Json(before), TempApp.Json(temp.App.Store.FindCharacter(id)!));
        Assert.Equal("character.not-archived", Assert.Single(Assert.Throws<AppValidationException>(() => temp.App.Unarchive(id)).Problems).Code);
    }

    [Fact]
    public void Saving_a_character_never_changes_its_archive_mark()
    {
        var (temp, id) = Setup();
        using var _t = temp;
        var stale = temp.App.Store.FindCharacter(id)!;
        temp.App.Archive(new(id, Confirm: true));

        temp.App.SaveCharacter(stale with { Name = "Renamed" }); // a stale copy without the mark
        Assert.NotNull(temp.App.Store.FindCharacter(id)!.ArchivedAt);

        temp.App.Unarchive(id);
        temp.App.SaveCharacter(stale with { ArchivedAt = DateTimeOffset.UnixEpoch }); // a payload cannot archive either
        Assert.Null(temp.App.Store.FindCharacter(id)!.ArchivedAt);

        // Dual review: nor can a save create an archived character.
        var created = temp.App.SaveCharacter(stale with { Id = Guid.NewGuid(), ArchivedAt = DateTimeOffset.UnixEpoch }).Character;
        Assert.Null(temp.App.Store.FindCharacter(created.Id)!.ArchivedAt);
    }

    [Fact]
    public void A_character_package_never_carries_or_changes_the_archive_mark()
    {
        // Dual review: archiving is local library organisation. Only a full library restore brings it back.
        var (temp, id) = Setup();
        using var _t = temp;
        var beforeArchive = temp.App.ExportCharacters([id]).Content; // an older export, made while it was active
        temp.App.Archive(new(id, Confirm: true));

        foreach (var purpose in new[] { ExportPurpose.Backup, ExportPurpose.Share })
        {
            var package = temp.App.ExportCharacters([id], purpose).Content;
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(package));
            using var character = JsonDocument.Parse(new StreamReader(zip.GetEntry($"characters/{id:D}.json")!.Open()).ReadToEnd());
            Assert.False(character.RootElement.TryGetProperty("archivedAt", out _), purpose.ToString());

            // A friend who imports it gets an active character.
            using var friend = new TempApp();
            friend.App.ApplyImport(package);
            Assert.Null(Assert.Single(friend.App.ListCharacters()).ArchivedAt);
        }

        // Re-importing the older export replaces the character but keeps it archived here.
        temp.App.ApplyImport(beforeArchive);
        Assert.NotNull(temp.App.Store.FindCharacter(id)!.ArchivedAt);
    }

    [Fact]
    public void The_archive_mark_is_written_only_when_set_and_matches_the_character_schema()
    {
        var (temp, id) = Setup();
        using var _t = temp;
        using (var active = JsonDocument.Parse(JsonSerializer.Serialize(temp.App.Store.FindCharacter(id)!, RulesJson.Compact)))
            Assert.False(active.RootElement.TryGetProperty("archivedAt", out _)); // unchanged JSON for every character not archived

        temp.App.Archive(new(id, Confirm: true));
        using var archived = JsonDocument.Parse(JsonSerializer.Serialize(temp.App.Store.FindCharacter(id)!, RulesJson.Compact));
        Assert.True(archived.RootElement.TryGetProperty("archivedAt", out _));
        Assert.Equal("", SchemaTests.Validate("character", archived.RootElement));
    }

    [Fact]
    public void A_full_library_backup_includes_archived_characters_and_restores_them_archived()
    {
        var (temp, id) = Setup();
        using var _t = temp;
        temp.App.Archive(new(id, Confirm: true));
        var path = Path.Combine(Path.GetTempPath(), "tomestack-tests", $"{Guid.NewGuid():N}.tomestack.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (var file = File.Create(path))
            {
                var contents = temp.App.WriteLibraryBackup(file).Contents;
                Assert.Equal((1, 1), (contents.Characters, contents.GapNotes));
            }

            using var clean = new TempApp();
            clean.App.ApplyLibraryRestore(path);

            Assert.Equal(TempApp.Json(temp.App.Store.FindCharacter(id)!), TempApp.Json(clean.App.Store.FindCharacter(id)!));
            Assert.NotNull(Assert.Single(clean.App.ListCharacters()).ArchivedAt);
            Assert.Single(clean.App.ListGapNotes(id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_archive_commands_are_transport_neutral_json()
    {
        var (temp, id) = Setup();
        using var _t = temp;
        var dispatcher = new CommandDispatcher(temp.App);
        JsonElement Send(string command, object payload) =>
            JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "a", command, payload }, RulesJson.Compact))).RootElement;

        Assert.Equal(1, Send("character.archivePreview", new { characterId = id }).GetProperty("result").GetProperty("gapNotes").GetInt32());
        Assert.False(Send("character.archive", new { characterId = id }).GetProperty("ok").GetBoolean()); // no confirm
        Assert.True(Send("character.archive", new { characterId = id, confirm = true }).GetProperty("result").TryGetProperty("archivedAt", out _));
        var back = Send("character.unarchive", new { characterId = id });
        Assert.True(back.GetProperty("ok").GetBoolean(), back.ToString());
        Assert.Contains("character.archive", CommandDispatcher.Commands);
    }
}
