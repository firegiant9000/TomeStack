using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 item 2, SPEC C-05: play state changes only through the confirmed <c>character.play</c> command. Uses the M1
/// acceptance character Brenna (SRD 5.2.1 Barbarian 3: Rage 3 uses, Stonecunning PB uses, 35 hit points).
/// </summary>
public class PlayCommandTests
{
    private static readonly Guid RageContent = Guid.Parse("52c00000-0000-4000-8000-000000000012");
    private static readonly Guid DwarfContent = Guid.Parse("52c00000-0000-4000-8000-000000000001");

    private static (TempApp Temp, Guid Id) Brenna()
    {
        var temp = new TempApp();
        var id = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json")).Character.Id;
        return (temp, id);
    }

    private static JsonElement Dispatch(TempApp temp, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new { id = "1", command = "character.play", payload }, RulesJson.Compact))).RootElement;

    private static string Code(JsonElement response) =>
        response.GetProperty("error").GetProperty("diagnostics").EnumerateArray().First().GetProperty("code").GetString()!;

    [Fact]
    public void The_sheet_shows_SRD_resources_with_calculated_maximums_and_their_recoveries()
    {
        var (temp, id) = Brenna();
        using var _ = temp;

        var sheet = temp.App.GetCharacter(id).Sheet;

        var rage = Assert.Single(sheet.Resources!, r => r.ResourceId == "rage");
        Assert.Equal(3, rage.Maximum); // min(2 + floor(3 / 3), 4)
        Assert.Contains(rage.Trace.Single().Inputs!, i => i.Name == "CLASS_LEVEL" && i.Value == 3);
        Assert.Equal([RestPeriod.ShortRest, RestPeriod.LongRest], rage.Recoveries.Select(r => r.On));
        var stonecunning = Assert.Single(sheet.Resources!, r => r.ResourceId == "stonecunning");
        Assert.Equal((2, AutomationStatus.Assisted), (stonecunning.Maximum, stonecunning.Automation));
        Assert.Equal(new HitPointState(35, 35, 0), sheet.HitPoints);
        Assert.Contains(sheet.Features!, f => f.Name == "Frenzy" && f.Effects.Any(e => e.Type == "roll" && e.Dice == "2d6"));
    }

    [Fact]
    public void Nothing_changes_without_confirmation()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var response = Dispatch(temp, new { characterId = id, action = "spend", contentId = RageContent, resourceId = "rage" });

        Assert.Equal("play.confirmation-required", Code(response));
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void Saving_the_character_never_changes_its_play_state()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var stale = temp.App.GetCharacter(id).Character; // for example, the copy an open screen holds
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 10));
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, ContentId: RageContent, ResourceId: "rage"));

        // A save for another reason (a name, an override, equipment) with a stale or edited play state keeps the stored one.
        var saved = temp.App.SaveCharacter(stale with { Name = "Brenna Stonefist", Play = new PlayState { CurrentHitPoints = 1, Exhaustion = 6 } });

        Assert.Equal("Brenna Stonefist", saved.Character.Name);
        Assert.Equal((25, 0), (saved.Character.Play.CurrentHitPoints, saved.Character.Play.Exhaustion));
        Assert.Equal(2, saved.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
        Assert.Equal(TempApp.Json(saved.Character.Play), TempApp.Json(temp.App.GetCharacter(id).Character.Play));
    }

    [Fact]
    public void Spending_and_regaining_uses_is_bounded_by_the_calculated_maximum()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        PlayCommand Rage(PlayActionKind action, int amount = 1) => new(id, action, Confirm: true, Amount: amount, ContentId: RageContent, ResourceId: "rage");

        var view = temp.App.Play(Rage(PlayActionKind.Spend, 2));
        Assert.Equal(1, view.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
        Assert.Equal("resource.insufficient", Assert.Throws<AppValidationException>(() => temp.App.Play(Rage(PlayActionKind.Spend, 2))).Problems[0].Code);

        view = temp.App.Play(Rage(PlayActionKind.Regain, 5));
        Assert.Equal(3, view.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
        Assert.Empty(view.Character.Play.Resources);
        Assert.Equal("resource.nothing-spent", Assert.Throws<AppValidationException>(() => temp.App.Play(Rage(PlayActionKind.Regain))).Problems[0].Code);
        Assert.Equal("resource.not-found", Assert.Throws<AppValidationException>(() => temp.App.Play(Rage(PlayActionKind.Spend) with { ContentId = DwarfContent })).Problems[0].Code);
    }

    [Fact]
    public void Damage_uses_temporary_hit_points_first_and_healing_stops_at_the_maximum()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        PlayCommand Hp(PlayActionKind action, int amount) => new(id, action, Confirm: true, Amount: amount);

        temp.App.Play(Hp(PlayActionKind.SetTemporaryHitPoints, 5));
        var view = temp.App.Play(Hp(PlayActionKind.Damage, 12));
        Assert.Equal(new HitPointState(35, 28, 0), view.Sheet.HitPoints);

        view = temp.App.Play(Hp(PlayActionKind.Damage, 100));
        Assert.Equal(0, view.Sheet.HitPoints!.Current);

        view = temp.App.Play(Hp(PlayActionKind.Heal, 100));
        Assert.Equal(35, view.Sheet.HitPoints!.Current);
        Assert.Null(view.Character.Play.CurrentHitPoints); // at the maximum, so it follows a later level-up

        Assert.Equal("play.hit-points-above-maximum", Assert.Throws<AppValidationException>(() => temp.App.Play(Hp(PlayActionKind.SetHitPoints, 36))).Problems[0].Code);
    }

    [Fact]
    public void Conditions_and_exhaustion_are_stored_and_survive_reopening_and_a_package_round_trip()
    {
        var (temp, id) = Brenna();
        using var _ = temp;

        temp.App.Play(new(id, PlayActionKind.AddCondition, Confirm: true, Condition: "poisoned"));
        temp.App.Play(new(id, PlayActionKind.SetExhaustion, Confirm: true, Amount: 2));
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, ContentId: RageContent, ResourceId: "rage"));
        Assert.Equal("play.condition-unknown", Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, PlayActionKind.AddCondition, Confirm: true, Condition: "sleepy"))).Problems[0].Code);
        temp.Reopen();

        var stored = temp.App.GetCharacter(id);
        Assert.Equal(["poisoned"], stored.Character.Play.Conditions);
        Assert.Equal(2, stored.Character.Play.Exhaustion);
        Assert.Equal(4, stored.Character.SchemaVersion);

        using var destination = new TempApp();
        destination.App.ApplyImport(temp.App.ExportCharacters([id]).Content);
        var imported = destination.App.GetCharacter(id);
        Assert.Equal(TempApp.Json(stored.Character.Play), TempApp.Json(imported.Character.Play));
        Assert.Equal(2, imported.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
    }
}
