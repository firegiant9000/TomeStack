using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// MECH-X item 5: a target mark is a player-set toggle, and its riders apply only while it is on. These tests go through the service
/// (<c>tests/RulesFixtures/fixture-pack-target-marks.json</c>, <c>characters/pm-marker-l5.json</c>); no production code is involved.
/// </summary>
public class TargetMarkServiceTests
{
    private static readonly ContentReference Longblade = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000001"), Guid.Parse("5f6de000-0000-4000-8000-000000000001"));
    private static readonly Guid SigilMark = Guid.Parse("6f0dc000-0000-4000-8000-000000000090");

    private static (TempApp Temp, Guid Id) Marker()
    {
        var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(5);
        temp.AddPack("fixture-pack-feature-attacks.json");
        temp.AddPack("fixture-pack-target-marks.json");
        return (temp, temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/pm-marker-l5.json")).Character.Id);
    }

    private static int Sigils(CharacterView view) => view.Sheet.Resources!.Single(r => r.ResourceId == "sigil").Current!.Value;

    private static PlayCommand MarkOn(Guid id) => new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: SigilMark, ToggleId: "marked");

    private static RollRecord Damage(TempApp temp, Guid id, bool critical = false) => temp.App.Roll(new(id, Weapon: Longblade, Damage: true, Critical: critical));

    [Fact]
    public void Marking_spends_a_sigil_and_the_next_rolls_carry_the_mark()
    {
        var (temp, id) = Marker();
        using var _ = temp;
        var toHitBefore = temp.App.Roll(new(id, Weapon: Longblade)).Modifiers.Sum(m => m.Amount);
        Assert.DoesNotContain("1d6", Damage(temp, id).Formula);
        Assert.Equal(3, Sigils(temp.App.GetCharacter(id)));

        var on = temp.App.Play(MarkOn(id));

        Assert.Equal(2, Sigils(on));
        var rolls = TempApp.Json(on.Character);
        Assert.Equal(toHitBefore + 1, temp.App.Roll(new(id, Weapon: Longblade)).Modifiers.Sum(m => m.Amount));
        var damage = Damage(temp, id);
        Assert.Contains("+1d6", damage.Formula);
        Assert.Contains(damage.Explain!, s => s.Description == "Fixture Sigil Mark: +1d6 force damage (attack rider)");
        var critical = Damage(temp, id, critical: true);
        Assert.True(critical.Critical);
        Assert.Contains("+1d6", critical.Formula);
        Assert.Equal(4, critical.Dice.Count);
        Assert.Equal(2, critical.Dice.Count(d => d.FromCritical));
        Assert.Equal(2, critical.Dice.Count(d => d.Sides == 6));
        var toggle = Assert.Single(on.Character.Play.Toggles);
        Assert.Null(toggle.RoundsLeft); // an untimed toggle: it lasts until switched off (R30)
        Assert.Equal(rolls, TempApp.Json(temp.App.GetCharacter(id).Character)); // rolling writes nothing
    }

    [Fact]
    public void Undo_after_marking_gives_the_sigil_back_and_the_mark_goes_off()
    {
        var (temp, id) = Marker();
        using var _ = temp;
        var on = temp.App.Play(MarkOn(id));
        Assert.Equal(2, Sigils(on));
        Assert.Contains("1d6", Damage(temp, id).Formula);

        // The sheet's own undo of a plain switch on (src/Ui/src/undo.ts:101-110): toggleOff, then regain the cost (1 when none is named) to the pool's content.
        temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: SigilMark, ToggleId: "marked"));
        var undone = temp.App.Play(new(id, PlayActionKind.Regain, Confirm: true, Amount: 1, ContentId: SigilMark, ResourceId: "sigil"));

        Assert.Empty(undone.Character.Play.Toggles);
        Assert.Equal(3, Sigils(undone));
        Assert.DoesNotContain("1d6", Damage(temp, id).Formula);
    }

    [Fact]
    public void Marking_with_no_sigil_left_is_refused_and_the_rider_stays_off()
    {
        var (temp, id) = Marker();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: 3, ContentId: SigilMark, ResourceId: "sigil"));
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(MarkOn(id)));

        Assert.Equal("resource.insufficient", refused.Problems[0].Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
        Assert.Empty(temp.App.GetCharacter(id).Character.Play.Toggles);
        Assert.DoesNotContain("1d6", Damage(temp, id).Formula);
    }
}
