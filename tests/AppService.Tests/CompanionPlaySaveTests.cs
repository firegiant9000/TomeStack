using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// MECH-X item 4 (S1): the hit points of a content companion survive every path but an ordinary save. A play action, a rest and a
/// snapshot restore never prune a row; <c>character.save</c> drops a row whose content no longer offers the companion.
/// </summary>
public class CompanionPlaySaveTests
{
    private static readonly Guid MuleId = Guid.Parse("6a7e0cb0-0000-4000-8000-0000000000c1");

    private sealed record Setup(TempApp Temp, Guid Id);

    /// <summary>Stores <c>pm-handler-l3</c> directly, so the content row is there although no content offers the companion.</summary>
    private static Setup Start()
    {
        var temp = new TempApp();
        temp.AddPack("fixture-pack-m2-combat.json"); // the class; the Warden Bond feature is not installed, as when its source is gone
        var character = TempApp.LoadFixture<Character>("characters/pm-handler-l3.json");
        temp.App.Store.InTransaction(() => temp.App.Store.SaveCharacter(character));
        return new(temp, character.Id);
    }

    [Fact]
    public void A_play_action_rest_and_restore_keep_a_content_row_whose_source_is_gone()
    {
        var s = Start();
        using var _ = s.Temp;
        Assert.True(s.Temp.App.Store.FindCharacter(s.Id)!.UsesV11Fields);

        var damaged = s.Temp.App.Play(new(s.Id, PlayActionKind.CompanionDamage, Confirm: true, Amount: 1, CompanionId: MuleId));
        Assert.Equal(0, damaged.Character.Play.Companions!.Single(p => p.ContentId is null).Temporary); // the 1 temporary point absorbs it
        Assert.Single(damaged.Character.Play.Companions!, p => p.ContentId is not null);

        var asking = s.Temp.App.PreviewRest(s.Id, RestPeriod.LongRest);
        var rested = s.Temp.App.Rest(new(s.Id, RestPeriod.LongRest, Confirm: true, Basis: asking.Basis));
        Assert.Single(rested.Character.Play.Companions!, p => p.ContentId is not null);

        var snapshot = s.Temp.App.Snapshot(new(s.Id));
        s.Temp.App.Play(new(s.Id, PlayActionKind.CompanionDamage, Confirm: true, Amount: 1, CompanionId: MuleId));
        var preview = s.Temp.App.PreviewRestore(new(s.Id, snapshot.Id));
        var restored = s.Temp.App.RestoreSnapshot(new(preview.Token, Confirm: true));
        Assert.Single(restored.View.Character.Play.Companions!, p => p.ContentId is not null);
        Assert.True(s.Temp.App.Store.FindCharacter(s.Id)!.UsesV11Fields);
    }

    [Fact]
    public void An_ordinary_save_prunes_a_content_row_whose_content_is_not_active()
    {
        var s = Start();
        using var _ = s.Temp;
        var stored = s.Temp.App.GetCharacter(s.Id).Character;

        var saved = s.Temp.App.SaveCharacter(stored with { Name = "Testy Handler Renamed" });

        var row = Assert.Single(saved.Character.Play.Companions!);
        Assert.Equal(MuleId, row.Id);
        Assert.Null(row.ContentId);
        Assert.Equal(9, s.Temp.App.Store.FindCharacter(s.Id)!.SchemaVersion);
    }
}
