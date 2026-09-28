using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 item 3, SPEC C-05, D01: <c>character.restPreview</c> proposes and writes nothing; <c>character.rest</c> applies the
/// same proposal only with confirmation. Brenna (SRD 5.2.1 Barbarian 3) after a fight: 2 Rages spent, 23 of 35 hit points.
/// The short rest and hit dice follow D01 (owner, 2026-09-27: SRD rules, previewed).
/// </summary>
public class RestCommandTests
{
    private static readonly Guid RageContent = Guid.Parse("52c00000-0000-4000-8000-000000000012");

    private static (TempApp Temp, Guid Id) AfterAFight()
    {
        var temp = new TempApp();
        var id = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json")).Character.Id;
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: 2, ContentId: RageContent, ResourceId: "rage"));
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 12));
        temp.App.Play(new(id, PlayActionKind.SetTemporaryHitPoints, Confirm: true, Amount: 4));
        temp.App.Play(new(id, PlayActionKind.SetExhaustion, Confirm: true, Amount: 1));
        return (temp, id);
    }

    private static JsonElement Dispatch(TempApp temp, string command, object payload) =>
        JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch(JsonSerializer.Serialize(new { id = "1", command, payload }, RulesJson.Compact))).RootElement;

    private static string Code(JsonElement response) =>
        response.GetProperty("error").GetProperty("diagnostics").EnumerateArray().First().GetProperty("code").GetString()!;

    [Fact]
    public void The_preview_proposes_every_long_rest_change_and_writes_nothing()
    {
        var (temp, id) = AfterAFight();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var response = Dispatch(temp, "character.restPreview", new { characterId = id, kind = "longRest" });

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var preview = response.GetProperty("result").Deserialize<RestPreview>(RulesJson.Compact)!;
        Assert.Equal((23, 35), Single(preview, RestChangeKind.HitPoints));
        Assert.Equal((4, 0), Single(preview, RestChangeKind.TemporaryHitPoints));
        Assert.Equal((1, 3), Single(preview, RestChangeKind.Resource)); // Rage: regain all on a long rest
        Assert.Equal((1, 0), Single(preview, RestChangeKind.Exhaustion));
        Assert.Null(preview.Changes.Single(c => c.Kind == RestChangeKind.Exhaustion).Condition); // 2024 rules
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    private static (int From, int To) Single(RestPreview preview, RestChangeKind kind)
    {
        var change = Assert.Single(preview.Changes, c => c.Kind == kind);
        return (change.From, change.To);
    }

    [Fact]
    public void A_rest_needs_confirmation_and_the_current_preview()
    {
        var (temp, id) = AfterAFight();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);
        var preview = temp.App.PreviewRest(id, RestPeriod.LongRest);

        Assert.Equal("rest.confirmation-required", Code(Dispatch(temp, "character.rest", new { characterId = id, kind = "longRest", basis = preview.Basis })));
        Assert.Equal("rest.preview-stale", Code(Dispatch(temp, "character.rest", new { characterId = id, kind = "longRest", confirm = true, basis = "0" })));
        Assert.Equal("rest.hit-dice-long-rest", Code(Dispatch(temp, "character.restPreview", new { characterId = id, kind = "longRest", hitDice = new[] { new { die = 12, roll = 5 } } })));
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));

        // A change after the preview (another hit) makes it stale: the player must see the new proposal.
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1));
        Assert.Equal("rest.preview-stale", Assert.Throws<AppValidationException>(() => temp.App.Rest(new(id, Confirm: true, Basis: preview.Basis))).Problems[0].Code);
    }

    [Fact]
    public void Confirming_applies_the_previewed_changes_except_the_unticked_ones()
    {
        var (temp, id) = AfterAFight();
        using var _ = temp;
        var preview = temp.App.PreviewRest(id, RestPeriod.LongRest);

        var view = temp.App.Rest(new(id, Confirm: true, Basis: preview.Basis, Skip: ["exhaustion"]));

        Assert.Equal(new HitPointState(35, 35, 0), view.Sheet.HitPoints);
        Assert.Equal(3, view.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
        Assert.Equal(1, view.Character.Play.Exhaustion);
        Assert.Equal(TempApp.Json(view.Character), TempApp.Json(temp.App.GetCharacter(id).Character));

        var next = temp.App.PreviewRest(id, RestPeriod.LongRest);
        Assert.Equal(RestChangeKind.Exhaustion, Assert.Single(next.Changes).Kind);
        Assert.Empty(next.Manual);
    }

    [Fact]
    public void A_short_rest_spends_the_rolled_hit_dice_and_recovers_one_rage_then_the_long_rest_gives_the_dice_back()
    {
        // Brenna: Barbarian 3 (3 d12 hit dice), Con +2. The SRD 5.2.1 Rage regains one use on a short rest.
        var (temp, id) = AfterAFight();
        using var _ = temp;
        var hitDice = new[] { new { die = 12, roll = 5 }, new { die = 12, roll = 1 } };

        var response = Dispatch(temp, "character.restPreview", new { characterId = id, kind = "shortRest", hitDice });

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var preview = response.GetProperty("result").Deserialize<RestPreview>(RulesJson.Compact)!;
        Assert.Equal(
            [(RestChangeKind.HitDie, 23, 30), (RestChangeKind.HitDie, 30, 33), (RestChangeKind.Resource, 1, 2)],
            preview.Changes.Select(c => (c.Kind, c.From, c.To)));
        Assert.DoesNotContain(preview.Changes, c => c.Kind is RestChangeKind.TemporaryHitPoints or RestChangeKind.Exhaustion); // long rest only

        // The confirmation must name the same dice: other rolls are a different proposal.
        Assert.Equal("rest.preview-stale", Code(Dispatch(temp, "character.rest", new { characterId = id, kind = "shortRest", confirm = true, basis = preview.Basis, hitDice = new[] { new { die = 12, roll = 6 } } })));
        var rested = Dispatch(temp, "character.rest", new { characterId = id, kind = "shortRest", confirm = true, basis = preview.Basis, hitDice });
        Assert.True(rested.GetProperty("ok").GetBoolean(), rested.ToString());

        var view = temp.App.GetCharacter(id);
        Assert.Equal(33, view.Sheet.HitPoints!.Current);
        Assert.Equal(2, view.Sheet.Resources!.Single(r => r.ResourceId == "rage").Current);
        Assert.Equal(new HitDiceValue(12, 3, 2, 1, ["Barbarian"]).Remaining, Assert.Single(view.Sheet.HitDice!).Remaining);

        // Only one d12 is left.
        Assert.Equal("rest.hit-dice-insufficient", Code(Dispatch(temp, "character.restPreview", new { characterId = id, kind = "shortRest", hitDice })));

        // 2024 rules: the long rest gives back every spent hit die.
        var longRest = temp.App.PreviewRest(id, RestPeriod.LongRest);
        var dice = Assert.Single(longRest.Changes, c => c.Kind == RestChangeKind.HitDice);
        Assert.Equal((1, 3), (dice.From, dice.To));
        Assert.Equal(3, Assert.Single(temp.App.Rest(new(id, Confirm: true, Basis: longRest.Basis)).Sheet.HitDice!).Remaining);
    }

    [Fact]
    public void A_short_rest_at_0_hit_points_is_refused_under_2024_rules()
    {
        var (temp, id) = AfterAFight();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.SetHitPoints, Confirm: true, Amount: 0));

        Assert.Equal("rest.needs-hit-points", Code(Dispatch(temp, "character.restPreview", new { characterId = id, kind = "shortRest" })));
        Assert.Equal("rest.needs-hit-points", Code(Dispatch(temp, "character.restPreview", new { characterId = id, kind = "longRest" })));
    }

    [Fact]
    public void An_assisted_resource_with_an_encoded_recovery_is_proposed_too()
    {
        // The SRD 5.2.1 Dwarf's Stonecunning (assisted: the player decides when to use it) recovers all on a long rest.
        var (temp, id) = AfterAFight();
        using var _ = temp;
        var dwarf = Guid.Parse("52c00000-0000-4000-8000-000000000001");
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, ContentId: dwarf, ResourceId: "stonecunning"));

        var preview = temp.App.PreviewRest(id, RestPeriod.LongRest);

        var change = Assert.Single(preview.Changes, c => c.ResourceId == "stonecunning");
        Assert.Equal((1, 2), (change.From, change.To));
        Assert.Empty(preview.Manual);
    }
}
