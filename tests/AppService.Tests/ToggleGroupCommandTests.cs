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
        var temp = new TempApp(time: new TickingTime(TempApp.Now)); // each write moves the stamp (D43 S2)
        var pack = temp.AddPack("fixture-pack-forms.json");
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json"); // PB 2 at level 3: Focus is 12
        var pins = pack.Revisions.Select(r => r.Reference).ToList();
        pins.Add(new(Stance, Guid.Parse("5f8de000-0000-4000-8000-000000000001")));
        return (temp, temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, .. pins] }).Character.Id);
    }

    /// <summary>A replace switch carries the stamp of the copy the page holds (here: the stored one), as the UI does.</summary>
    private static CharacterView On(TempApp temp, Guid id, int content, string toggle, bool replace = false) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Content(content), ToggleId: toggle, Replace: replace,
            ExpectedUpdatedAt: replace ? temp.App.GetCharacter(id).Character.UpdatedAt : null));

    private static PlayCommand Switch(Guid id, DateTimeOffset? stamp) =>
        new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Content(3), ToggleId: "tide", Replace: true, ExpectedUpdatedAt: stamp);

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
    public void A_replace_switch_without_the_stamp_is_refused_and_nothing_changes()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 2, "ember");
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(Switch(id, null)));

        Assert.Equal("play.basis-required", refused.Problems[0].Code);
        Assert.Equal("This change needs the character's current state. Reload it and try again.", refused.Problems[0].Message);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_replace_switch_with_a_stale_stamp_is_refused_and_nothing_changes()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 2, "ember");
        var stale = temp.App.GetCharacter(id).Character.UpdatedAt;
        temp.App.Play(new(id, PlayActionKind.Damage, Confirm: true, Amount: 1)); // another window moved the stamp
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var refused = Assert.Throws<AppValidationException>(() => temp.App.Play(Switch(id, stale)));

        Assert.Equal("character.stale", refused.Problems[0].Code);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_double_sent_replace_switch_is_refused_the_second_time()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 2, "ember");
        var stamp = temp.App.GetCharacter(id).Character.UpdatedAt;

        var first = temp.App.Play(Switch(id, stamp));
        var focus = Focus(first);
        var second = Assert.Throws<AppValidationException>(() => temp.App.Play(Switch(id, stamp)));

        Assert.Equal("character.stale", second.Problems[0].Code);
        Assert.Equal(focus, Focus(temp.App.GetCharacter(id))); // the cost was paid once
    }

    [Fact]
    public void A_second_switch_to_the_same_form_with_the_fresh_stamp_pays_nothing()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 2, "ember");
        var first = temp.App.Play(Switch(id, temp.App.GetCharacter(id).Character.UpdatedAt));
        var fresh = temp.App.GetCharacter(id);
        var focus = Focus(first);
        var label = fresh.UndoLabel;
        var before = TempApp.Json(fresh.Character);

        // The sheet's double click: the second press runs with the stamp the first one produced.
        var second = Assert.Throws<AppValidationException>(() => temp.App.Play(Switch(id, fresh.Character.UpdatedAt)));

        Assert.Equal("toggle.already-on", second.Problems[0].Code);
        var after = temp.App.GetCharacter(id);
        Assert.Equal(focus, Focus(after));
        Assert.Equal(label, after.UndoLabel);
        Assert.NotNull(label);
        Assert.Equal(before, TempApp.Json(after.Character));
    }

    [Fact]
    public void A_simple_toggle_does_not_need_the_stamp()
    {
        var (temp, id) = Forms();
        using var _ = temp;

        Assert.True(IsOn(temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Content(2), ToggleId: "ember")), "ember"));
        Assert.False(IsOn(temp.App.Play(new(id, PlayActionKind.ToggleOff, Confirm: true, ContentId: Content(2), ToggleId: "ember")), "ember"));
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
    public void A_refused_replace_keeps_the_overlays_that_were_on()
    {
        var (temp, id) = Forms();
        using var _ = temp;
        On(temp, id, 3, "tide");          // 5 of 12
        On(temp, id, 5, "bound");         // free overlay that needs tide
        temp.App.Play(new(id, PlayActionKind.Spend, Confirm: true, Amount: 5, ContentId: Discipline, ResourceId: "focus"));
        Assert.Equal(2, Focus(temp.App.GetCharacter(id))); // ember costs 3
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        Assert.Equal("resource.insufficient", Refusal(() => On(temp, id, 2, "ember", replace: true)));

        var after = temp.App.GetCharacter(id);
        Assert.Equal(before, TempApp.Json(after.Character));
        Assert.Contains(after.Character.Play.Toggles, t => t.ToggleId == "bound");
        Assert.True(IsOn(after, "tide"));
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

    private static ContentRevision Extra(int n, string name, params Effect[] effects) => new()
    {
        ContentId = Guid.Parse($"6f0dc000-0000-4000-8000-0000000001{n:D2}"), RevisionId = Guid.Parse($"6f0de000-0000-4000-8000-0000000001{n:D2}"),
        Kind = ContentKind.Feature, Name = name, RulesFamilies = [RulesFamilies.Srd521], Status = RevisionStatus.Published, SchemaVersion = 11,
        Provenance = new(Guid.Parse("6f0d5000-0000-4000-8000-000000000001"), new(1)), Effects = effects,
    };

    private static (TempApp Temp, Guid Id) With(IReadOnlyList<ContentRevision> revisions)
    {
        var temp = new TempApp();
        temp.AddPack("fixture-pack-forms.json");
        foreach (var revision in revisions)
            temp.App.Store.AddRevision(revision);
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        return (temp, temp.App.SaveCharacter(brenna with { Pins = [.. brenna.Pins, .. revisions.Select(r => r.Reference)] }).Character.Id);
    }

    private static CharacterView OnOf(TempApp temp, Guid id, ContentRevision revision, string toggle) =>
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: revision.ContentId, ToggleId: toggle));

    [Fact]
    public void Without_the_group_content_two_primaries_are_a_group_of_one_and_nothing_is_refused_or_removed()
    {
        var absent = Guid.Parse("6f0dc000-0000-4000-8000-0000000001ee");
        ToggleEffect Form(string id) => new() { Id = id, ToggleId = id, Label = id, Group = "forms", GroupContent = absent };
        var one = Extra(1, "Test Solo One", Form("one"));
        var two = Extra(2, "Test Solo Two", Form("two"));
        var (temp, id) = With([one, two]);
        using var _ = temp;

        OnOf(temp, id, one, "one");
        var both = OnOf(temp, id, two, "two");

        Assert.True(IsOn(both, "one"));
        Assert.True(IsOn(both, "two"));
        Assert.Equal(2, both.Character.Play.Toggles.Count);
        // They show as plain toggles, so the sheet gives the UI no group to put them in.
        Assert.All(both.Sheet.Toggles!.Where(t => t.ToggleId is "one" or "two"), t => Assert.Null(t.Group));
    }

    [Fact]
    public void A_cost_formula_that_fails_refuses_and_writes_nothing()
    {
        var broken = Extra(3, "Test Broken Cost",
            new ResourceEffect { Id = "pool", ResourceId = "pool", Label = "Pool", Maximum = "5" },
            new ToggleEffect { Id = "bad", ToggleId = "bad", Label = "Bad", Cost = "NOT_A_NAME + 1", ResourceId = "pool" });
        var (temp, id) = With([broken]);
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        Assert.Equal("effect.invalid-formula", Refusal(() => OnOf(temp, id, broken, "bad")));

        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    [Fact]
    public void A_toggle_with_a_pool_in_another_content_and_no_cost_spends_one_from_that_pool()
    {
        var pool = Extra(4, "Test Pool Owner", new ResourceEffect { Id = "pool", ResourceId = "pool", Label = "Pool", Maximum = "5" });
        var user = Extra(5, "Test Pool User", new ToggleEffect { Id = "use", ToggleId = "use", Label = "Use", ResourceId = "pool", ResourceContent = pool.ContentId });
        var (temp, id) = With([pool, user]);
        using var _ = temp;

        var on = OnOf(temp, id, user, "use");

        Assert.Equal(4, on.Sheet.Resources!.Single(r => r.ResourceId == "pool").Current!.Value);
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
