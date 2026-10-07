using System.IO.Compression;
using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Character schema v8 (D21, D22): currency and session notes are saved with the character, never shared, kept across a restore.</summary>
public class CurrencyAndNotesTests
{
    private static SessionNote Note(string text) => new(Guid.NewGuid(), new DateOnly(2026, 10, 6), text, DateTimeOffset.Parse("2026-10-06T20:00:00Z"));

    private static Character ReadPackageCharacter(byte[] package, Guid id)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry($"characters/{id:D}.json")!.Open());
        return JsonSerializer.Deserialize<Character>(reader.ReadToEnd(), RulesJson.Options)!;
    }

    [Fact]
    public void Save_keeps_play_state_and_takes_the_payloads_currency_and_notes()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json")).Character;
        var full = temp.App.GetCharacter(saved.Id).Sheet.HitPoints!.Current;
        temp.App.Play(new(saved.Id, PlayActionKind.Damage, Confirm: true, Amount: 5));

        var view = temp.App.SaveCharacter(saved with { Currency = new(Gp: 7), Notes = [Note("Fixture session one.")] });

        Assert.Equal(full - 5, view.Sheet.HitPoints!.Current); // the stale payload did not undo the damage
        Assert.Equal(new Currency(Gp: 7), view.Character.Currency);
        Assert.Equal("Fixture session one.", Assert.Single(view.Character.Notes).Text);
    }

    [Fact]
    public void A_share_package_carries_no_session_notes_and_a_backup_does()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json") with { Notes = [Note("Private fixture note.")], Currency = new(Gp: 7) }).Character;

        var share = ReadPackageCharacter(temp.App.ExportCharacters([saved.Id], ExportPurpose.Share).Content, saved.Id);
        var backup = ReadPackageCharacter(temp.App.ExportCharacters([saved.Id], ExportPurpose.Backup).Content, saved.Id);

        Assert.Empty(share.Notes);
        Assert.Equal(new Currency(Gp: 7), share.Currency); // coins are not private
        Assert.Single(backup.Notes);
    }

    [Fact]
    public void Importing_a_share_keeps_the_local_notes_and_importing_a_backup_takes_the_packages()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json") with { Notes = [Note("Backup-time note.")] }).Character;
        var share = temp.App.ExportCharacters([saved.Id], ExportPurpose.Share).Content;
        var backup = temp.App.ExportCharacters([saved.Id], ExportPurpose.Backup).Content;

        temp.App.SaveCharacter(saved with { Notes = [Note("Local note written later.")] });
        temp.App.ApplyImport(share);
        Assert.Equal("Local note written later.", Assert.Single(temp.App.GetCharacter(saved.Id).Character.Notes).Text);

        temp.App.ApplyImport(backup);
        Assert.Equal("Backup-time note.", Assert.Single(temp.App.GetCharacter(saved.Id).Character.Notes).Text);
    }

    [Fact]
    public void Restoring_a_snapshot_keeps_the_current_session_notes()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json")).Character;
        var snapshot = temp.App.Snapshot(new(saved.Id, "before notes"));
        temp.App.SaveCharacter(saved with { Notes = [Note("Written after the snapshot.")] });

        var preview = temp.App.PreviewRestore(new(saved.Id, snapshot.Id));
        var restored = temp.App.RestoreSnapshot(new(preview.Token, Confirm: true)).View.Character;

        Assert.Equal("Written after the snapshot.", Assert.Single(restored.Notes).Text);
    }
}
