using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D43 Slice 1 (Task 1.4): <c>toggleOn</c> with <c>replace</c> switches a form group atomically, a toggle's cost is paid from its
/// shared pool, and overlays that need the old form go with it. A refusal leaves the stored character untouched.
/// </summary>
public class ToggleGroupCommandTests
{
    private static readonly Guid Discipline = Guid.Parse("6f0dc000-0000-4000-8000-000000000001");
    private static readonly Guid Stance = Guid.Parse("5f8dc000-0000-4000-8000-000000000001");

    private static Guid Content(int n) => Guid.Parse($"6f0dc000-0000-4000-8000-00000000000{n}");

    private static (TempApp Temp, Guid Id) Forms()
    {
        var temp = new TempApp();
        var pack = temp.AddPack("fixture-pack-forms.json");
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json"); // PB 2 at level 3: Focus is 12
        var pins = pack.Revisions.Select(r => r.Reference).ToList();
        pins.Add(new(Stance, Guid.Parse("5f8de000-0000-4000-8000-000000000001")));
        return (temp, temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, .. pins] }).Character.Id);
    }

    private static CharacterView On(TempApp temp, Guid id, int content, string toggle, bool replace = false) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Content(content), ToggleId: toggle, Replace: replace));

    private static CharacterView Off(TempApp temp, Guid id, int content, string toggle) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: Content(content), ToggleId: toggle));

    private static int Focus(CharacterView view) => view.Sheet.Resources!.Single(r => r.ResourceId == "focus").Current!.Value;

    private static bool IsOn(CharacterView view, string toggle) => view.Sheet.Toggles!.Single(t => t.ToggleId == toggle).On;

    private static string Refusal(Action act) => Assert.Throws<AppValidationException>(act).Problems[0].Code;

    [Fact]
    public void Turning_on_a_second_primary_is_refused_unless_replace()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        Assert.Equal(12, Focus(temp.App.GetCharacter(id)));
        On(temp, id, 2, "ember");

        var refused = Assert.Throws<AppValidationException>(() => On(temp, id, 3, "tide"));
        Assert.Equal("toggle.group-occupied", refused.Problems[0].Code);
        Assert.Contains("Fixture Ember Form", refused.Problems[0].Message, StringComparison.Ordinal);

        var replaced = On(temp, id, 3, "tide", replace: true);
        Assert.True(IsOn(replaced, "tide"));
        Assert.False(IsOn(replaced, "ember"));
        Assert.Equal(12 - 3 - 5, Focus(replaced));
    }

    [Fact]
    public void A_replace_switch_without_enough_pool_changes_nothing()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 2, "ember");
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: 5, ContentId: Discipline, ResourceId: "focus"));
        Assert.Equal(4, Focus(temp.App.GetCharacter(id)));
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        Assert.Equal("resource.insufficient", Refusal(() => On(temp, id, 3, "tide", replace: true)));

        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void Replacing_a_primary_turns_off_overlays_it_does_not_allow()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 3, "tide");
        var both = On(temp, id, 4, "surge");
        Assert.True(IsOn(both, "surge"));

        var replaced = On(temp, id, 2, "ember", replace: true);

        Assert.True(IsOn(replaced, "ember"));
        Assert.False(IsOn(replaced, "surge"));
        Assert.DoesNotContain(replaced.Character.Play.Toggles, t => t.ToggleId == "surge");
    }

    [Fact]
    public void Turning_a_primary_off_turns_off_overlays_that_need_it()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 3, "tide");
        On(temp, id, 5, "bound");
        On(temp, id, 4, "surge");

        var off = Off(temp, id, 3, "tide");

        Assert.False(IsOn(off, "bound"));
        Assert.DoesNotContain(off.Character.Play.Toggles, t => t.ToggleId == "bound");
        Assert.True(IsOn(off, "surge")); // allowed in the base character
    }

    [Fact]
    public void An_overlay_not_allowed_now_is_refused()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 2, "ember");
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        Assert.Equal("toggle.overlay-not-allowed", Refusal(() => On(temp, id, 5, "bound")));

        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_toggle_below_its_level_is_not_found()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        Assert.Equal("toggle.not-found", Refusal(() => On(temp, id, 6, "deep")));
    }

    [Fact]
    public void A_v6_toggle_still_spends_one_use_of_its_own_resource()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        var before = temp.App.GetCharacter(id).Sheet.Resources!.Single(r => r.ResourceId == "radiance").Current!.Value;

        var on = temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Stance, ToggleId: "stance"));

        Assert.Equal(before - 1, on.Sheet.Resources!.Single(r => r.ResourceId == "radiance").Current!.Value);
    }

    [Fact]
    public void Turning_on_a_primary_turns_off_base_only_overlays()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        Assert.True(IsOn(On(temp, id, 7, "calm"), "calm"));

        var tide = On(temp, id, 3, "tide");

        Assert.False(IsOn(tide, "calm"));
        Assert.DoesNotContain(tide.Character.Play.Toggles, t => t.ToggleId == "calm");
    }

    [Fact]
    public void A_combined_overlay_needs_its_primary()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        Assert.Equal("toggle.overlay-not-allowed", Refusal(() => On(temp, id, 8, "calm-tide")));
        On(temp, id, 3, "tide");
        Assert.True(IsOn(On(temp, id, 8, "calm-tide"), "calm-tide"));
    }

    [Fact]
    public void A_cost_of_zero_spends_nothing_and_a_second_source_toggle_joins_the_group()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 3, "tide");
        var bound = On(temp, id, 5, "bound"); // costs nothing
        Assert.Equal(12 - 5, Focus(bound));
        Assert.Equal("toggle.group-occupied", Refusal(() => On(temp, id, 9, "lineage")));
    }
}
