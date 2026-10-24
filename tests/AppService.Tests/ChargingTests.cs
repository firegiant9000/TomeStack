using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D43 slice 8 (Task 8.2): <c>useAction</c> (a roll's own spend and its alsoChange applied together or not at all), the draw-down
/// amount checks of the roll command, and <c>endEncounter</c> with <c>clear</c>, on the charging pack and the Fixture Charge Adept
/// (focus 9, a counting-up charge pool of 4). The one concurrency token is <c>Character.UpdatedAt</c>.
/// </summary>
public class ChargingTests
{
    private static readonly Guid Class = Guid.Parse("6f0dc000-0000-4000-8000-000000000080");
    private static readonly Guid Beam = Guid.Parse("6f0dc000-0000-4000-8000-000000000081");
    private static readonly ContentReference BeamRef = new(Beam, Guid.Parse("6f0de000-0000-4000-8000-000000000081"));

    /// <summary>The Charge Adept with the given uses of focus spent and charge built up; stamps move on every write.</summary>
    private static (TempApp Temp, Guid Id) Charger(int focusSpent = 0, int charge = 0, Func<ContentRevision, ContentRevision>? adjust = null)
    {
        var temp = new TempApp(time: new TickingTime(TempApp.Now));
        temp.AddPack("fixture-pack-charging.json", adjust);
        var character = TempApp.LoadFixture<Character>("characters/pm-charger-l5.json");
        character = character with { Play = character.Play.WithSpent(Class, "focus", focusSpent).WithSpent(Class, "charge", charge) };
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    private static DateTimeOffset Stamp(TempApp temp, Guid id) => temp.App.GetCharacter(id).Character.UpdatedAt;

    private static string Stored(TempApp temp, Guid id) => TempApp.Json(temp.App.Store.FindCharacter(id));

    private static string StoredPlay(TempApp temp, Guid id) => TempApp.Json(temp.App.Store.FindCharacter(id)!.Play);

    private static int Left(CharacterView view, string resource) => view.Sheet.Resources!.Single(r => r.ResourceId == resource).Current!.Value;

    private static int Built(CharacterView view, string resource) => view.Sheet.Resources!.Single(r => r.ResourceId == resource).Spent;

    private static AppValidationException Refused(Action act) => Assert.Throws<AppValidationException>(act);

    private static CharacterView Use(TempApp temp, Guid id, string? rollId, DateTimeOffset? stamp, int amount = 1, Guid? content = null) =>
        temp.App.Play(new(id, PlayActionKind.UseAction, Confirm: true, Amount: amount, ContentId: content ?? Beam, RollId: rollId, ExpectedUpdatedAt: stamp));

    private static CharacterView StartEncounter(TempApp temp, Guid id) =>
        temp.App.Play(new(id, PlayActionKind.StartEncounter, Confirm: true, ExpectedUpdatedAt: Stamp(temp, id)));

    private static CharacterView EndEncounter(TempApp temp, Guid id, DateTimeOffset stamp, params string[]? clear) =>
        temp.App.Play(new(id, PlayActionKind.EndEncounter, Confirm: true, ExpectedUpdatedAt: stamp, Clear: clear));

    private static string Pool(string resource, Guid? content = null) => $"{content ?? Class:D}/{resource}";

    [Fact]
    public void Gather_spends_focus_and_builds_charge_in_one_change()
    {
        var (temp, id) = Charger();
        using var _ = temp;

        var view = Use(temp, id, "gather", Stamp(temp, id));

        Assert.Equal(8, Left(view, "focus"));
        Assert.Equal(1, Built(view, "charge"));
        Assert.Equal("use Gather", view.UndoLabel);
        Assert.Equal("use Gather", temp.App.GetCharacter(id).UndoLabel);
        var stored = temp.App.Store.FindCharacter(id)!.Play;
        Assert.Equal(1, stored.SpentOf(Class, "focus"));
        Assert.Equal(1, stored.SpentOf(Class, "charge"));
    }

    [Theory]
    [InlineData(9, 0, "use.unaffordable")]
    [InlineData(0, 4, "use.pool-full")]
    public void A_use_that_cannot_apply_every_change_changes_nothing(int focusSpent, int charge, string code)
    {
        var (temp, id) = Charger(focusSpent, charge);
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Use(temp, id, "gather", Stamp(temp, id)));

        Assert.Equal(code, refused.Code);
        Assert.Equal(code, refused.Problems[0].Code);
        Assert.Equal(before, Stored(temp, id));
        Assert.Null(temp.App.GetCharacter(id).UndoLabel);
    }

    [Fact]
    public void A_double_sent_use_is_applied_once()
    {
        var (temp, id) = Charger();
        using var _ = temp;
        var stamp = Stamp(temp, id);
        Use(temp, id, "gather", stamp);
        var after = Stored(temp, id);

        var second = Refused(() => Use(temp, id, "gather", stamp));

        Assert.Equal("character.stale", second.Code);
        Assert.Equal(after, Stored(temp, id));
        Assert.Equal(1, Built(temp.App.GetCharacter(id), "charge"));
    }

    [Fact]
    public void A_use_without_a_stamp_is_refused()
    {
        var (temp, id) = Charger();
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Use(temp, id, "gather", null));

        Assert.Equal("play.basis-required", refused.Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void Undo_of_a_use_restores_every_pool()
    {
        var (temp, id) = Charger(focusSpent: 2, charge: 1);
        using var _ = temp;
        var before = StoredPlay(temp, id);
        var used = Use(temp, id, "gather", Stamp(temp, id));
        Assert.NotEqual(before, StoredPlay(temp, id));

        var undone = temp.App.UndoPlay(new(id, used.Character.UpdatedAt, true));

        Assert.Equal(before, StoredPlay(temp, id));
        Assert.Equal(7, Left(undone, "focus"));
        Assert.Equal(1, Built(undone, "charge"));
        Assert.Null(undone.UndoLabel);
    }

    [Fact]
    public void A_use_of_an_unknown_roll_or_one_that_is_unavailable_is_refused()
    {
        var (temp, id) = Charger();
        using var _ = temp;
        var before = Stored(temp, id);

        Assert.Equal("use.not-found", Refused(() => Use(temp, id, "nope", Stamp(temp, id))).Code);
        Assert.Equal("use.not-found", Refused(() => Use(temp, id, null, Stamp(temp, id))).Code);
        Assert.Equal("use.not-found", Refused(() => Use(temp, id, "gather", Stamp(temp, id), content: Guid.NewGuid())).Code);
        Assert.Equal("use.not-found", Refused(() => Use(temp, id, "gather", Stamp(temp, id), content: Class)).Code); // the class has no such roll
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void A_use_of_a_roll_whose_form_is_off_is_refused()
    {
        var (temp, id) = Charger(adjust: Gated);
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Use(temp, id, "gather", Stamp(temp, id)));

        Assert.Equal("use.unavailable", refused.Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void An_amount_that_could_not_be_calculated_makes_the_use_unavailable()
    {
        var (temp, id) = Charger(adjust: r => r.ContentId == Beam
            ? r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "gather" } roll ? roll with { AlsoChange = [new AlsoChange("charge", Class, UpkeepChange.Spend, "1 + SCALE.nonesuch")] } : e)] }
            : r);
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Use(temp, id, "gather", Stamp(temp, id)));

        Assert.Equal("use.unavailable", refused.Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void A_cost_that_cannot_be_calculated_refuses_the_use()
    {
        var (temp, id) = Charger(adjust: r => r.ContentId == Beam
            ? r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "gather" } roll ? roll with { Cost = "1 + SCALE.nonesuch" } : e)] }
            : r);
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Use(temp, id, "gather", Stamp(temp, id)));

        Assert.Equal("effect.invalid-formula", refused.Code);
        Assert.Equal("effect.invalid-formula", refused.Problems[0].Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void A_use_that_changes_nothing_is_refused_and_keeps_the_undo_slot()
    {
        var (temp, id) = Charger();
        using var _ = temp;
        var gathered = Use(temp, id, "gather", Stamp(temp, id));
        var before = Stored(temp, id);

        var refused = Refused(() => Use(temp, id, "release", Stamp(temp, id), amount: 0)); // base cost 0, nothing drawn down

        Assert.Equal("use.unavailable", refused.Code);
        Assert.Equal(before, Stored(temp, id));
        Assert.Equal("use Gather", temp.App.GetCharacter(id).UndoLabel);
        Assert.Equal(gathered.Character.UpdatedAt, Stamp(temp, id));
    }

    [Fact]
    public void A_regain_with_nothing_spent_changes_nothing_and_is_refused()
    {
        var (temp, id) = Charger(adjust: r => r.ContentId == Beam
            ? r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "gather" } roll
                ? roll with { ResourceId = null, ResourceContent = null, Cost = null, AlsoChange = [new AlsoChange("focus", Class, UpkeepChange.Regain, "1")] }
                : e)] }
            : r);
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Use(temp, id, "gather", Stamp(temp, id)));

        Assert.Equal("use.unavailable", refused.Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void A_roll_that_changes_no_pool_cannot_be_used()
    {
        var (temp, id) = Charger(adjust: r => r.ContentId == Beam
            ? r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "gather" } roll ? roll with { ResourceId = null, ResourceContent = null, Cost = null, AlsoChange = null, Dice = "1d4" } : e)] }
            : r);
        using var _ = temp;

        Assert.Equal("use.unavailable", Refused(() => Use(temp, id, "gather", Stamp(temp, id))).Code);
    }

    [Fact]
    public void A_blank_label_names_the_undo_by_the_roll_id()
    {
        var (temp, id) = Charger(adjust: r => r.ContentId == Beam
            ? r with { Effects = [.. r.Effects.Select(e => e is RollEffect { Id: "gather" } roll ? roll with { Label = " " } : e)] }
            : r);
        using var _ = temp;

        var view = Use(temp, id, "gather", Stamp(temp, id));

        Assert.Equal("use gather", view.UndoLabel);
    }

    [Fact]
    public void A_roll_id_two_rolls_share_is_not_found_rather_than_the_first_one()
    {
        var (temp, id) = Charger(adjust: r => r.ContentId == Beam
            ? r with { Effects = [.. r.Effects, new RollEffect { Id = "gather-twin", RollId = "gather", Label = "Twin", Dice = "1d4" }] }
            : r);
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => Use(temp, id, "gather", Stamp(temp, id)));

        Assert.Equal("use.not-found", refused.Code);
        Assert.Equal(before, Stored(temp, id));
    }

    private static ContentRevision Gated(ContentRevision revision) => revision.ContentId != Beam
        ? revision
        : revision with
        {
            Effects =
            [
                .. revision.Effects.Select(e => e is RollEffect { Id: "gather" } roll ? roll with { WhileToggle = new ToggleRef(null, "form") } : e),
                new ToggleEffect { Id = "form", ToggleId = "form", Label = "Form" },
            ],
        };

    [Fact]
    public void Release_rolls_extra_dice_for_the_charge_it_uses()
    {
        var (temp, id) = Charger(charge: 3);
        using var _ = temp;
        var before = Stored(temp, id);

        var record = temp.App.Roll(new RollCommand(id, BeamRef, "release", Amount: 3));

        Assert.Equal("2d8+3d8", record.Formula);
        Assert.Equal(5, record.Dice.Count);
        Assert.Equal(new RollSpend(Class, "charge", 3, true), record.Spend);
        Assert.True(record.Spend!.DrawsDown);
        Assert.Equal(before, Stored(temp, id)); // rolling spends nothing

        var released = Use(temp, id, "release", Stamp(temp, id), amount: 3);

        Assert.Equal(0, Built(released, "charge"));
        Assert.Equal("use Release", released.UndoLabel);
    }

    [Fact]
    public void A_release_above_the_built_up_charge_is_refused()
    {
        var (temp, id) = Charger(charge: 2);
        using var _ = temp;
        var before = Stored(temp, id);

        var rolled = Refused(() => temp.App.Roll(new RollCommand(id, BeamRef, "release", Amount: 3)));
        var used = Refused(() => Use(temp, id, "release", Stamp(temp, id), amount: 3));

        Assert.Equal("roll.amount-unaffordable", rolled.Code);
        Assert.Equal("use.unaffordable", used.Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void A_release_keeps_the_per_unit_bounds()
    {
        var (temp, id) = Charger(charge: 4);
        using var _ = temp;
        var before = Stored(temp, id);

        Assert.Equal("roll.amount-out-of-range", Refused(() => temp.App.Roll(new RollCommand(id, BeamRef, "release", Amount: 5))).Code);
        Assert.Equal("roll.amount-out-of-range", Refused(() => Use(temp, id, "release", Stamp(temp, id), amount: 5)).Code);
        Assert.Equal(before, Stored(temp, id));
    }

    [Fact]
    public void Clearing_only_named_counting_up_pools()
    {
        var (temp, id) = Charger(focusSpent: 1, charge: 2);
        using var _ = temp;
        StartEncounter(temp, id);
        var started = StoredPlay(temp, id);
        var before = Stored(temp, id);

        foreach (var bad in new[] { Pool("focus"), Pool("nope"), Pool("charge", Guid.NewGuid()), "garbage", "", $"{Class:D}/" })
        {
            var refused = Refused(() => EndEncounter(temp, id, Stamp(temp, id), bad));
            Assert.Equal("play.clear-invalid", refused.Code);
            Assert.Equal(before, Stored(temp, id));
        }
        Assert.Equal("play.clear-invalid", Refused(() => EndEncounter(temp, id, Stamp(temp, id), Enumerable.Repeat(Pool("charge"), 65).ToArray())).Code);
        Assert.Equal(before, Stored(temp, id));

        var ended = EndEncounter(temp, id, Stamp(temp, id), Pool("charge"));

        Assert.Equal(0, Built(ended, "charge"));
        Assert.Equal(8, Left(ended, "focus"));
        Assert.Null(temp.App.Store.FindCharacter(id)!.Play.Encounter);
        Assert.Equal("end encounter", ended.UndoLabel);

        var undone = temp.App.UndoPlay(new(id, ended.Character.UpdatedAt, true));

        Assert.Equal(started, StoredPlay(temp, id));
        Assert.Equal(2, Built(undone, "charge"));
        Assert.NotNull(temp.App.Store.FindCharacter(id)!.Play.Encounter);
    }

    [Fact]
    public void Duplicate_clear_entries_are_harmless_and_no_clear_keeps_the_pool()
    {
        var (temp, id) = Charger(charge: 2);
        using var _ = temp;
        StartEncounter(temp, id);

        var kept = EndEncounter(temp, id, Stamp(temp, id));

        Assert.Equal(2, Built(kept, "charge"));

        StartEncounter(temp, id);
        var cleared = EndEncounter(temp, id, Stamp(temp, id), Pool("charge"), Pool("charge"));

        Assert.Equal(0, Built(cleared, "charge"));
    }

    [Fact]
    public void Clear_on_any_other_action_is_refused()
    {
        var (temp, id) = Charger(charge: 2);
        using var _ = temp;
        var before = Stored(temp, id);

        var refused = Refused(() => temp.App.Play(new(id, PlayActionKind.StartEncounter, Confirm: true, ExpectedUpdatedAt: Stamp(temp, id), Clear: [Pool("charge")])));
        var heal = Refused(() => temp.App.Play(new(id, PlayActionKind.Heal, Confirm: true, Amount: 1, Clear: [Pool("charge")])));

        Assert.Equal("play.clear-invalid", refused.Code);
        Assert.Equal("play.clear-invalid", heal.Code);
        Assert.Equal(before, Stored(temp, id));
        temp.App.Play(new(id, PlayActionKind.Heal, Confirm: true, Amount: 1, Clear: [])); // an empty list is no clear
    }

    [Fact]
    public void A_use_that_fills_charge_adds_the_full_notice()
    {
        var (temp, id) = Charger(charge: 3, adjust: r => r.ContentId == Class
            ? r with { Effects = [.. r.Effects.Select(e => e is ResourceEffect { ResourceId: "charge" } pool ? pool with { ResourceFull = new ResourceFullPrompt("The charge is full.") } : e)] }
            : r);
        using var _ = temp;

        var view = Use(temp, id, "gather", Stamp(temp, id));

        Assert.Equal(4, Built(view, "charge"));
        var notice = Assert.Single(view.Notices!);
        Assert.Equal(("resource.full", "The charge is full."), (notice.Code, notice.Message));
    }

    [Fact]
    public void A_use_that_leaves_charge_below_full_adds_no_notice()
    {
        var (temp, id) = Charger(charge: 2, adjust: r => r.ContentId == Class
            ? r with { Effects = [.. r.Effects.Select(e => e is ResourceEffect { ResourceId: "charge" } pool ? pool with { ResourceFull = new ResourceFullPrompt("The charge is full.") } : e)] }
            : r);
        using var _ = temp;

        Assert.Null(Use(temp, id, "gather", Stamp(temp, id)).Notices);
    }

    [Fact]
    public void A_long_rest_proposes_clearing_charge()
    {
        var (temp, id) = Charger(focusSpent: 3, charge: 2);
        using var _ = temp;

        var preview = temp.App.PreviewRest(id, RestPeriod.LongRest);

        Assert.Contains(preview.Changes, c => c.Kind == RestChangeKind.Resource && c.ContentId == Class && c.ResourceId == "charge" && c.SpentAfter == 0);
        Assert.Contains(preview.Changes, c => c.Kind == RestChangeKind.Resource && c.ContentId == Class && c.ResourceId == "focus" && c.SpentAfter == 0);
    }
}
