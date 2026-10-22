namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 3 (D43), task 3.3: the turn planner on the turns pack. Iron stance (primary, 3 rounds, upkeep 2 breath),
/// Flare overlay (allowed with Iron only; upkeep 1 breath that ends it if short, and 1 strain on a counting-up pool of 3),
/// Quick Guard (an allowance refreshed at the start of a turn), Second Wind Rhythm (regains 1 breath at the end of a turn).
/// </summary>
public class TurnPlannerTests
{
    // Flare is pinned before Iron on purpose: the planner, not the content order, puts primaries first.
    private static readonly ContentReference[] Pins =
        [Fixtures.SteadyDiscipline, Fixtures.FlareOverlay, Fixtures.IronStance, Fixtures.QuickGuard, Fixtures.SecondWindRhythm];

    private static Guid Breath => Fixtures.SteadyDiscipline.ContentId;
    private static Guid Strain => Fixtures.FlareOverlay.ContentId;
    private static Guid Guard => Fixtures.QuickGuard.ContentId;
    private static string IronUpkeep => $"upkeep:{Fixtures.IronStance.ContentId:D}:iron-upkeep";
    private static string FlareBreath => $"upkeep:{Fixtures.FlareOverlay.ContentId:D}:flare-breath";
    private static string FlareStrain => $"upkeep:{Fixtures.FlareOverlay.ContentId:D}:flare-strain";
    private static string GuardRefresh => $"upkeep:{Fixtures.QuickGuard.ContentId:D}:guard-refresh";
    private static string IronShort => $"short:{Fixtures.IronStance.ContentId:D}:iron-upkeep";

    internal static Character InEncounter(
        int round = 1, bool iron = false, bool flare = false, int? ironLeft = null, int breathSpent = 0, int strainSpent = 0, int guardSpent = 0, bool encounter = true)
    {
        List<ActiveToggle> toggles = [];
        if (iron)
            toggles.Add(new(Fixtures.IronStance.ContentId, "iron", ironLeft));
        if (flare)
            toggles.Add(new(Fixtures.FlareOverlay.ContentId, "flare"));
        var play = new PlayState { Toggles = toggles, Encounter = encounter ? new EncounterState(round) : null };
        play = play.WithSpent(Breath, "breath", breathSpent).WithSpent(Strain, "strain", strainSpent).WithSpent(Guard, "guard", guardSpent);
        var character = Fixtures.Srd521Character();
        return character with { Pins = [.. character.Pins, .. Pins], Play = play };
    }

    internal static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.PlayMechanicsCatalog());

    private static TurnPlan Plan(Character character, TurnMoment at = TurnMoment.TurnStart, TurnSelection? selection = null) =>
        TurnPlanner.Plan(character, Sheet(character), at, selection ?? TurnSelection.None);

    private static PlayState Turn(
        Character character, TurnMoment at = TurnMoment.TurnStart, IEnumerable<string>? skip = null, IReadOnlyDictionary<string, TurnChoiceOption>? choices = null) =>
        TurnPlanner.Apply(character.Play, Plan(character, at, new TurnSelection(new HashSet<string>(skip ?? []), choices ?? new Dictionary<string, TurnChoiceOption>())));

    private static bool IsOn(PlayState play, string toggleId) => play.Toggles.Any(t => t.ToggleId == toggleId);

    [Fact]
    public void Turn_start_ticks_round_duration_then_spends_primaries_before_overlays()
    {
        var plan = Plan(InEncounter(round: 1, iron: true, flare: true, ironLeft: 3, guardSpent: 1));

        Assert.Equal(
            [TurnChangeKind.Round, TurnChangeKind.Duration, TurnChangeKind.Spend, TurnChangeKind.Spend, TurnChangeKind.Regain, TurnChangeKind.Spend],
            plan.Changes.Select(c => c.Kind));
        Assert.Equal(("round", 1, 2), (plan.Changes[0].Id, plan.Changes[0].From, plan.Changes[0].To));
        Assert.Equal(($"duration:{Fixtures.IronStance.ContentId:D}:iron", 3, 2), (plan.Changes[1].Id, plan.Changes[1].From, plan.Changes[1].To));
        Assert.Equal((IronUpkeep, Breath, "breath", 0, 2), (plan.Changes[2].Id, plan.Changes[2].ContentId, plan.Changes[2].ResourceId, plan.Changes[2].From, plan.Changes[2].To));
        Assert.Equal((FlareBreath, 2, 3), (plan.Changes[3].Id, plan.Changes[3].From, plan.Changes[3].To));
        Assert.Equal((GuardRefresh, 1, 0), (plan.Changes[4].Id, plan.Changes[4].From, plan.Changes[4].To));
        Assert.Equal((FlareStrain, "strain", 0, 1), (plan.Changes[5].Id, plan.Changes[5].ResourceId, plan.Changes[5].From, plan.Changes[5].To));
        Assert.Empty(plan.Choices);
        Assert.Empty(plan.Manual);

        var after = Turn(InEncounter(round: 1, iron: true, flare: true, ironLeft: 3, guardSpent: 1));
        Assert.Equal(2, after.Encounter!.Round);
        Assert.Equal(2, after.Toggles.Single(t => t.ToggleId == "iron").RoundsLeft);
        Assert.Equal(3, after.SpentOf(Breath, "breath"));
        Assert.Equal(1, after.SpentOf(Strain, "strain"));
        Assert.Equal(0, after.SpentOf(Guard, "guard"));
    }

    [Fact]
    public void Upkeep_spends_primaries_before_overlays_from_one_pool()
    {
        // 2 breath left: Iron (2) is paid first although Flare's content comes first, so Flare cannot pay its 1.
        var plan = Plan(InEncounter(iron: true, flare: true, breathSpent: 5));

        var spend = Assert.Single(plan.Changes, c => c.Kind == TurnChangeKind.Spend);
        Assert.Equal(IronUpkeep, spend.Id);
        var end = Assert.Single(plan.Changes, c => c.Kind == TurnChangeKind.ToggleEnd);
        Assert.Equal("Flare overlay (needs 1, has 0)", end.Label);
        Assert.True(end.Required);
        Assert.Equal("flare", end.ToggleId);
        Assert.DoesNotContain(plan.Changes, c => c.Id == FlareStrain);
        Assert.Empty(plan.Choices);
    }

    [Fact]
    public void A_spend_that_cannot_be_paid_asks()
    {
        var character = InEncounter(iron: true, flare: true, breathSpent: 6);
        var plan = Plan(character);

        var prompt = Assert.Single(plan.Choices);
        Assert.Equal("Iron stance needs 2, has 1", prompt.Label);
        Assert.Equal((IronShort, Fixtures.IronStance.ContentId, "iron", 2, 1), (prompt.Id, prompt.ContentId, prompt.ToggleId, prompt.Needs, prompt.Has));
        // With no choice given the plan shows the preselected End outcome: Iron ends, and Flare ends with it.
        Assert.Equal(["iron", "flare"], plan.Changes.Where(c => c.Kind == TurnChangeKind.ToggleEnd).Select(c => c.ToggleId));
        Assert.All(plan.Changes.Where(c => c.Kind == TurnChangeKind.ToggleEnd), c => Assert.True(c.Required));
        Assert.Empty(Turn(character).Toggles);
    }

    [Fact]
    public void Choosing_end_ends_the_form_and_its_overlays()
    {
        var after = Turn(InEncounter(iron: true, flare: true, breathSpent: 6), choices: new Dictionary<string, TurnChoiceOption> { [IronShort] = TurnChoiceOption.End });

        Assert.False(IsOn(after, "iron"));
        Assert.False(IsOn(after, "flare"));
        Assert.Equal(6, after.SpentOf(Breath, "breath"));
        Assert.Equal(0, after.SpentOf(Strain, "strain")); // the strain spend is absent
    }

    [Fact]
    public void Choosing_keep_spends_what_is_left_and_adds_a_manual_line()
    {
        var character = InEncounter(iron: true, breathSpent: 6);
        var choices = new Dictionary<string, TurnChoiceOption> { [IronShort] = TurnChoiceOption.Keep };
        var plan = Plan(character, selection: new TurnSelection(new HashSet<string>(), choices));

        Assert.Equal("Iron stance: short 1 breath, settle by hand", Assert.Single(plan.Manual).Message);
        var after = TurnPlanner.Apply(character.Play, plan);
        Assert.Equal(7, after.SpentOf(Breath, "breath"));
        Assert.True(IsOn(after, "iron"));
    }

    [Fact]
    public void IfShort_end_ends_the_form_without_asking()
    {
        // Flare is only on beside Iron (it is not allowed in the base character), so Iron is paid and Flare is the one that is short.
        var plan = Plan(InEncounter(iron: true, flare: true, breathSpent: 5));

        Assert.Empty(plan.Choices);
        Assert.Empty(plan.Manual);
        var end = Assert.Single(plan.Changes, c => c.Kind == TurnChangeKind.ToggleEnd);
        Assert.True(end.Required);
        Assert.Equal($"end:{Fixtures.FlareOverlay.ContentId:D}:flare", end.Id);
    }

    [Fact]
    public void Unticking_an_upkeep_spend_ends_the_form_instead()
    {
        var character = InEncounter(iron: true, flare: true);
        var after = Turn(character, skip: [IronUpkeep]);

        Assert.False(IsOn(after, "iron"));
        Assert.False(IsOn(after, "flare"));
        Assert.Equal(0, after.SpentOf(Breath, "breath"));
        Assert.Equal(0, after.SpentOf(Strain, "strain"));
    }

    [Fact]
    public void Ending_a_primary_drops_its_overlays_upkeep()
    {
        var plan = Plan(InEncounter(iron: true, flare: true, ironLeft: 1));

        Assert.Equal([TurnChangeKind.Round, TurnChangeKind.Duration, TurnChangeKind.ToggleEnd, TurnChangeKind.ToggleEnd], plan.Changes.Select(c => c.Kind));
        Assert.Equal(["iron", "flare"], plan.Changes.Where(c => c.Kind == TurnChangeKind.ToggleEnd).Select(c => c.ToggleId));
        Assert.DoesNotContain(plan.Changes, c => c.Kind == TurnChangeKind.Spend);
    }

    [Fact]
    public void An_allowance_refreshes_at_turn_start()
    {
        var character = InEncounter(guardSpent: 1);

        var start = Plan(character);
        var regain = Assert.Single(start.Changes, c => c.Kind == TurnChangeKind.Regain);
        Assert.Equal((Guard, "guard", 1, 0), (regain.ContentId, regain.ResourceId, regain.From, regain.To));
        Assert.Equal(0, TurnPlanner.Apply(character.Play, start).SpentOf(Guard, "guard"));

        Assert.DoesNotContain(Plan(character, TurnMoment.TurnEnd).Changes, c => c.ResourceId == "guard");
    }

    [Fact]
    public void A_counting_up_pool_fills_and_prompts_when_full()
    {
        var filling = Plan(InEncounter(iron: true, flare: true, strainSpent: 2));
        var spend = Assert.Single(filling.Changes, c => c.ResourceId == "strain");
        Assert.Equal((2, 3), (spend.From, spend.To));
        Assert.Equal("Flare ends; take a level of exhaustion", Assert.Single(filling.Prompts).Text);

        var full = Plan(InEncounter(iron: true, flare: true, strainSpent: 3));
        var capped = Assert.Single(full.Changes, c => c.ResourceId == "strain");
        Assert.Equal((3, 3), (capped.From, capped.To));
        Assert.Contains("already full", capped.Reason);
        Assert.Equal(3, TurnPlanner.Apply(InEncounter(iron: true, flare: true, strainSpent: 3).Play, full).SpentOf(Strain, "strain"));
    }

    [Fact]
    public void Turn_end_runs_only_turn_end_upkeep()
    {
        var plan = Plan(InEncounter(round: 4, iron: true, flare: true, ironLeft: 2, breathSpent: 3, guardSpent: 1), TurnMoment.TurnEnd);

        var change = Assert.Single(plan.Changes);
        Assert.Equal((TurnChangeKind.Regain, Breath, "breath", 3, 2), (change.Kind, change.ContentId, change.ResourceId, change.From, change.To));
        Assert.Equal($"upkeep:{Fixtures.SecondWindRhythm.ContentId:D}:rhythm", change.Id);
    }

    [Fact]
    public void The_first_turn_after_starting_moves_round_0_to_1()
    {
        var plan = Plan(InEncounter(round: 0));
        var round = Assert.Single(plan.Changes);
        Assert.Equal(("round", 0, 1), (round.Id, round.From, round.To));
        Assert.Equal(1, Turn(InEncounter(round: 0)).Encounter!.Round);
    }

    [Fact]
    public void Outside_an_encounter_a_turn_is_refused()
    {
        Assert.Equal("turn.not-in-encounter", TurnPlanner.CannotTakeTurn(InEncounter(encounter: false))!.Code);
        Assert.Null(TurnPlanner.CannotTakeTurn(InEncounter()));
    }

    [Fact]
    public void A_toggle_with_no_rounds_left_does_not_tick()
    {
        // "Track by hand": on with a Duration but no RoundsLeft.
        var plan = Plan(InEncounter(iron: true, ironLeft: null));
        Assert.DoesNotContain(plan.Changes, c => c.Kind == TurnChangeKind.Duration);
    }

    [Fact]
    public void Unticking_a_duration_keeps_the_rounds_left()
    {
        var character = InEncounter(iron: true, ironLeft: 3);
        var after = Turn(character, skip: [$"duration:{Fixtures.IronStance.ContentId:D}:iron"]);
        Assert.Equal(3, after.Toggles.Single(t => t.ToggleId == "iron").RoundsLeft);
    }

    // ---- ToggleGroups.EndingWith: the one rule for "overlays that are no longer allowed once a primary is off" ----------

    [Fact]
    public void EndingWith_names_the_overlays_that_need_the_ending_primary()
    {
        var toggles = Sheet(InEncounter(iron: true, flare: true)).Toggles!;
        var iron = toggles.Single(t => t.ToggleId == "iron");

        Assert.Equal(["flare"], ToggleGroups.EndingWith(iron, toggles).Select(t => t.ToggleId));
        // An overlay is never "ending with" itself, and an ungrouped or overlay toggle takes nothing with it.
        Assert.Empty(ToggleGroups.EndingWith(toggles.Single(t => t.ToggleId == "flare"), toggles));
    }

    [Fact]
    public void EndingWith_keeps_an_overlay_that_the_base_character_allows()
    {
        var character = ToggleGroupTests.Forms(new ActiveToggle(Fixtures.TideForm.ContentId, "tide"), new ActiveToggle(Fixtures.SurgeOverlay.ContentId, "surge"), new ActiveToggle(Fixtures.BoundOverlay.ContentId, "bound"));
        var toggles = ToggleGroupTests.Sheet(character).Toggles!;

        // Surge is allowed in the base character; Bound is allowed only with Tide.
        Assert.Equal(["bound"], ToggleGroups.EndingWith(toggles.Single(t => t.ToggleId == "tide"), toggles).Select(t => t.ToggleId));
    }
}
