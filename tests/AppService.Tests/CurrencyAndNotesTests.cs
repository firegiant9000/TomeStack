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

    [Fact]
    public void A_snapshot_keeps_no_session_notes_and_a_restore_still_keeps_the_current_ones()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json") with { Notes = [Note("Private fixture note.")] }).Character;
        var snapshot = temp.App.Snapshot(new(saved.Id, "with notes"));

        Assert.Empty(temp.App.Store.FindSnapshot(snapshot.Id)!.Character.Notes); // the journal is never copied into a snapshot

        var preview = temp.App.PreviewRestore(new(saved.Id, snapshot.Id));
        var result = temp.App.RestoreSnapshot(new(preview.Token, Confirm: true));
        Assert.Equal("Private fixture note.", Assert.Single(result.View.Character.Notes).Text);
        Assert.Empty(temp.App.Store.FindSnapshot(result.Undo.Id)!.Character.Notes); // nor into the undo snapshot a restore takes
    }

    [Fact]
    public void The_restore_preview_names_the_detail_fields_that_differ_and_never_quotes_their_text()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json")
            with { Details = new(Alignment: "Fixture-old-alignment", Languages: ["Fixture-old-tongue"]) }).Character;
        var snapshot = temp.App.Snapshot(new(saved.Id, "with details"));

        var same = temp.App.PreviewRestore(new(saved.Id, snapshot.Id));
        Assert.Null(same.DetailsChange);

        temp.App.SaveCharacter(saved with { Details = new(Alignment: "Fixture-new-alignment", Appearance: new(Hair: "Fixture-new-hair"), Languages: ["Fixture-old-tongue"]) });
        var changed = temp.App.PreviewRestore(new(saved.Id, snapshot.Id));
        Assert.Equal(["alignment", "hair"], changed.DetailsChange);
        Assert.DoesNotContain("Fixture-", string.Join(' ', changed.DetailsChange!), StringComparison.Ordinal);

        temp.App.SaveCharacter(saved with { Details = null });
        Assert.Equal(["alignment", "languages"], temp.App.PreviewRestore(new(saved.Id, snapshot.Id)).DetailsChange);
        var restored = temp.App.RestoreSnapshot(new(temp.App.PreviewRestore(new(saved.Id, snapshot.Id)).Token, Confirm: true)).View.Character.Details;
        Assert.Equal(JsonSerializer.Serialize(saved.Details, RulesJson.Options), JsonSerializer.Serialize(restored, RulesJson.Options)); // semantics unchanged: details roll back
    }

    [Fact]
    public void The_restore_preview_names_the_coin_change_and_stays_quiet_when_coins_are_the_same()
    {
        using var temp = new TempApp();
        var saved = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json") with { Currency = new(Sp: 12, Gp: 3) }).Character;
        var snapshot = temp.App.Snapshot(new(saved.Id, "three gold"));

        var same = temp.App.PreviewRestore(new(saved.Id, snapshot.Id));
        Assert.Null(same.CurrencyAfter);
        Assert.Null(same.CurrencyNow);

        temp.App.SaveCharacter(saved with { Currency = new(Gp: 40) });
        var changed = temp.App.PreviewRestore(new(saved.Id, snapshot.Id));
        Assert.Equal(new Currency(Sp: 12, Gp: 3), changed.CurrencyAfter);
        Assert.Equal(new Currency(Gp: 40), changed.CurrencyNow);
        Assert.Equal(new Currency(Sp: 12, Gp: 3), temp.App.RestoreSnapshot(new(changed.Token, Confirm: true)).View.Character.Currency); // semantics unchanged: coins roll back
    }
}
