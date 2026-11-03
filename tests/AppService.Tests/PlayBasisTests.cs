using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D43 Slice 2 (Task 2.1): a play command may carry the stamp (<c>Character.UpdatedAt</c>) of the copy the page acted on. A stale
/// stamp is refused with the save's own <c>character.stale</c>, and nothing is written. Without a stamp a simple action behaves as
/// before (last write wins). Tests that need a write to move the stamp use a clock that ticks.
/// </summary>
public class PlayBasisTests
{
    private static readonly Guid RageContent = Guid.Parse("52c00000-0000-4000-8000-000000000012");

    private static (TempApp Temp, Guid Id) Brenna()
    {
        var temp = new TempApp(time: new TickingTime(TempApp.Now));
        var id = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json")).Character.Id;
        return (temp, id);
    }

    private static PlayCommand Spend(Guid id, DateTimeOffset? stamp = null) =>
        new(id, PlayActionKind.Spend, Confirm: true, ContentId: RageContent, ResourceId: "rage", ExpectedUpdatedAt: stamp);

    [Fact]
    public void A_stale_expected_stamp_is_refused_and_nothing_changes()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var old = temp.App.GetCharacter(id);
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));
        var after = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(Spend(id, old.Character.UpdatedAt)));

        Assert.Equal("character.stale", refused.Problems[0].Code);
        Assert.Equal(after, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_stale_stamp_is_refused_for_every_kind_of_play_action()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var old = temp.App.GetCharacter(id).Character.UpdatedAt;
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));
        var after = TempApp.Json(temp.App.GetCharacter(id).Character);

        foreach (var action in new[] { PlayActionKind.Heal, PlayActionKind.SpendCharge, PlayActionKind.CompanionDamage, PlayActionKind.ToggleOn })
        {
            var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(new(id, action, Confirm: true, ExpectedUpdatedAt: old)));
            Assert.Equal("character.stale", refused.Problems[0].Code);
        }
        Assert.Equal(after, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_current_expected_stamp_is_accepted()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var current = temp.App.GetCharacter(id);

        var spent = temp.App.Play(Spend(id, current.Character.UpdatedAt));

        Assert.Equal(current.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current!.Value - 1, spent.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current!.Value);
    }

    [Fact]
    public void A_command_without_expected_stamp_behaves_as_before()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));

        var spent = temp.App.Play(Spend(id)); // no stamp: last write wins

        Assert.True(spent.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current!.Value < 3);
    }

    [Fact]
    public void A_stale_save_and_a_stale_play_report_the_same_code()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var page = temp.App.GetCharacter(id).Character; // the copy a stale page holds
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));

        var save = Assert.Throws<AppValidationException>(() => temp.App.SaveCharacter(page));
        var play = Assert.Throws<AppValidationException>(() => temp.App.Play(Spend(id, page.UpdatedAt)));

        Assert.Equal("character.stale", save.Problems[0].Code);
        Assert.Equal(save.Problems[0].Code, play.Problems[0].Code);
        Assert.Equal(save.Problems[0].Message, play.Problems[0].Message);
    }

    [Fact]
    public void The_stamp_in_a_play_result_is_the_stamp_the_next_read_returns()
    {
        var (temp, id) = Brenna();
        using var _ = temp;

        var played = temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));

        Assert.Equal(played.Character.UpdatedAt, temp.App.GetCharacter(id).Character.UpdatedAt);
        temp.App.Play(Spend(id, played.Character.UpdatedAt)); // and it is accepted as the next command's stamp
    }

    [Fact]
    public void The_stamp_round_trips_through_the_dispatcher_payload()
    {
        var (temp, id) = Brenna();
        using var _ = temp;
        var old = temp.App.GetCharacter(id).Character.UpdatedAt;
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));
        var dispatcher = new CommandDispatcher(temp.App);

        string Send(DateTimeOffset stamp) => dispatcher.Dispatch(System.Text.Json.JsonSerializer.Serialize(
            new { id = "1", command = "character.play", payload = new { characterId = id, action = "heal", confirm = true, expectedUpdatedAt = stamp.ToString("O") } }, RulesJson.Compact));

        Assert.Contains("character.stale", Send(old), StringComparison.Ordinal);
        var accepted = System.Text.Json.JsonDocument.Parse(Send(temp.App.GetCharacter(id).Character.UpdatedAt)).RootElement;
        Assert.True(accepted.GetProperty("ok").GetBoolean(), accepted.ToString());
    }
}
