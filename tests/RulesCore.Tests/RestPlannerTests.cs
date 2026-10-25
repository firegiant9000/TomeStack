using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M2 item 3, SPEC C-05, D01 (owner, 2026-09-27: long rest only in M2): a long rest is planned from the sheet and applied
/// only by a confirmed command. The plan is pure and never changes the character.
/// </summary>
public class RestPlannerTests
{
    private static readonly ContentReference Feat = new(Guid.Parse("5f3dc000-0000-4000-8000-000000000001"), Guid.Parse("5f3de000-0000-4000-8000-000000000001"));

    private static (Character Character, InMemoryContentCatalog Catalog) Setup(string family, PlayState play)
    {
        var pack = Fixtures.Pack();
        var feat = new ContentRevision
        {
            ContentId = Feat.ContentId,
            RevisionId = Feat.RevisionId,
            Kind = ContentKind.Feat,
            Name = "Test Rest Feat",
            RulesFamilies = [RulesFamilies.Srd51, RulesFamilies.Srd521],
            Provenance = new(Fixtures.SourceShared),
            Status = RevisionStatus.Published,
            Effects =
            [
                new ResourceEffect { Id = "all", ResourceId = "all", Label = "All back", Maximum = "3" },
                new RecoveryEffect { Id = "all-long", ResourceId = "all", On = RestPeriod.LongRest, Amount = "all", Timing = EffectTiming.OnLongRest },
                new ResourceEffect { Id = "some", ResourceId = "some", Label = "Some back", Maximum = "4" },
                new RecoveryEffect { Id = "some-long", ResourceId = "some", On = RestPeriod.LongRest, Amount = "PB - 1", Timing = EffectTiming.OnLongRest },
                new ResourceEffect { Id = "short", ResourceId = "short", Label = "Short only", Maximum = "2" },
                new RecoveryEffect { Id = "short-rest", ResourceId = "short", On = RestPeriod.ShortRest, Amount = "all", Timing = EffectTiming.OnShortRest },
                new ResourceEffect { Id = "hand", ResourceId = "hand", Label = "By hand", Maximum = "2" },
                new RecoveryEffect { Id = "hand-long", ResourceId = "hand", On = RestPeriod.LongRest, Amount = "PB +", Timing = EffectTiming.OnLongRest },
                new ResourceEffect { Id = "none", ResourceId = "none", Label = "No rule", Maximum = "1" },
            ],
        };
        var fixture = family == RulesFamilies.Srd51 ? Fixtures.Srd51Character() : Fixtures.Srd521Character();
        return (fixture with { Pins = [.. fixture.Pins, Feat], Play = play }, new InMemoryContentCatalog(pack.Sources, [.. pack.Revisions, feat]));
    }

    private static PlayState Tired() => new PlayState { CurrentHitPoints = 1, TemporaryHitPoints = 3, Exhaustion = 2 }
        .WithSpent(Feat.ContentId, "all", 5) // more than the maximum (3), for example after an update lowered it
        .WithSpent(Feat.ContentId, "some", 3)
        .WithSpent(Feat.ContentId, "short", 2)
        .WithSpent(Feat.ContentId, "hand", 1)
        .WithSpent(Feat.ContentId, "none", 1);

    private static RestPlan Plan(string family, PlayState play)
    {
        var (character, catalog) = Setup(family, play);
        return RestPlanner.LongRest(character, CharacterCalculator.Calculate(character, catalog));
    }

    [Fact]
    public void A_long_rest_that_ends_a_form_takes_the_overlay_that_needs_it_even_when_the_overlay_is_unticked()
    {
        var tide = new ActiveToggle(Fixtures.TideForm.ContentId, "tide");
        var bound = new ActiveToggle(Fixtures.BoundOverlay.ContentId, "bound"); // allowed only with Tide
        var character = ToggleGroupTests.Forms(tide, bound);
        var sheet = ToggleGroupTests.Sheet(character);
        Assert.True(sheet.Toggles!.Single(t => t.ToggleId == "bound").On);
        var plan = RestPlanner.LongRest(character, sheet);
        var boundChange = Assert.Single(plan.Changes, c => c.Kind == RestChangeKind.Toggle && c.ResourceId == "bound");

        // The overlay's own switch-off is unticked and the form's is ticked: the overlay still goes with the form.
        var play = RestPlanner.Apply(character.Play, plan, new HashSet<string> { boundChange.Id }, sheetToggles: sheet.Toggles);
        Assert.Empty(play.Toggles);

        // Without the sheet's toggles the form alone goes, as before (callers that do not know the groups).
        Assert.Equal(["bound"], RestPlanner.Apply(character.Play, plan, new HashSet<string> { boundChange.Id }).Toggles.Select(t => t.ToggleId));

        // Switching the form on again does not bring the overlay back.
        var again = ToggleGroupTests.Sheet(character with { Play = play with { Toggles = [tide] } });
        Assert.DoesNotContain(again.Toggles!, t => t.ToggleId == "bound" && t.On);

        // Keeping the form (its change unticked) keeps it on; the overlay's own change still applies.
        var tideChange = Assert.Single(plan.Changes, c => c.Kind == RestChangeKind.Toggle && c.ResourceId == "tide");
        Assert.Equal(["tide"], RestPlanner.Apply(character.Play, plan, new HashSet<string> { tideChange.Id }, sheetToggles: sheet.Toggles).Toggles.Select(t => t.ToggleId));
    }

    [Fact]
    public void Long_rest_proposes_ending_the_encounter_once()
    {
        var running = Tired() with { Encounter = new(3), Toggles = [new(Guid.Parse("5f3dc000-0000-4000-8000-0000000000aa"), "stance", RoundsLeft: 2)] };

        var plan = Plan(RulesFamilies.Srd521, running);
        var change = Assert.Single(plan.Changes, c => c.Kind == RestChangeKind.Encounter);
        Assert.Equal(("encounter", "End the encounter"), (change.Id, change.Label));
        Assert.Empty(plan.Changes.Where(c => c.Kind == RestChangeKind.Encounter).Skip(1));

        var applied = RestPlanner.Apply(running, plan, new HashSet<string>());
        Assert.Null(applied.Encounter);
        Assert.All(applied.Toggles, t => Assert.Null(t.RoundsLeft));

        var skipped = RestPlanner.Apply(running, plan, new HashSet<string> { "encounter" });
        Assert.Equal(3, skipped.Encounter!.Round);
        Assert.Equal(2, skipped.Toggles.Single().RoundsLeft);

        Assert.DoesNotContain(Plan(RulesFamilies.Srd521, Tired()).Changes, c => c.Kind == RestChangeKind.Encounter);
        Assert.DoesNotContain(Plan(RulesFamilies.Srd521, applied).Changes, c => c.Kind == RestChangeKind.Encounter);
    }

    [Fact]
    public void A_long_rest_proposes_no_temporary_hit_points_and_long_rest_recoveries()
    {
        // Hit points are covered by RestCommandTests (these fixtures have no class, so their maximum is 0).
        var plan = Plan(RulesFamilies.Srd521, Tired());

        Assert.Contains(plan.Changes, c => c.Kind == RestChangeKind.TemporaryHitPoints && (c.From, c.To) == (3, 0));
        var all = Assert.Single(plan.Changes, c => c.ResourceId == "all");
        Assert.Equal((0, 3, 0), (all.From, all.To, all.SpentAfter));
        Assert.Equal("all-long", all.Origin.EffectId);
        var some = Assert.Single(plan.Changes, c => c.ResourceId == "some");
        Assert.Equal((1, 2, 2), (some.From, some.To, some.SpentAfter)); // PB 2 - 1 = one use back
        Assert.DoesNotContain(plan.Changes, c => c.ResourceId == "short"); // a long rest is not a short rest
        // Never silently skipped: a recovery it cannot calculate, and a resource with no recovery rule, are manual steps.
        Assert.Equal(
            [("rest.recover-by-hand", "hand-long"), ("rest.no-recovery-encoded", "none")],
            plan.Manual.Select(m => (m.Code, m.EffectId!)));
    }

    [Fact]
    public void Exhaustion_needs_food_and_drink_only_under_2014_rules_side_by_side()
    {
        var exhaustion2014 = Assert.Single(Plan(RulesFamilies.Srd51, Tired()).Changes, c => c.Kind == RestChangeKind.Exhaustion);
        var exhaustion2024 = Assert.Single(Plan(RulesFamilies.Srd521, Tired()).Changes, c => c.Kind == RestChangeKind.Exhaustion);

        Assert.Equal((2, 1), (exhaustion2014.From, exhaustion2014.To));
        Assert.Equal((2, 1), (exhaustion2024.From, exhaustion2024.To));
        Assert.Contains("food and drink", exhaustion2014.Condition, StringComparison.Ordinal);
        Assert.Null(exhaustion2024.Condition);
        Assert.True(RulesFamilies.Get(RulesFamilies.Srd51).LongRestExhaustionNeedsFoodAndDrink);
        Assert.False(RulesFamilies.Get(RulesFamilies.Srd521).LongRestExhaustionNeedsFoodAndDrink);
    }

    [Fact]
    public void Applying_skips_unticked_changes_and_a_rested_character_has_nothing_left_to_propose()
    {
        var (character, catalog) = Setup(RulesFamilies.Srd51, Tired());
        var plan = RestPlanner.LongRest(character, CharacterCalculator.Calculate(character, catalog));

        var play = RestPlanner.Apply(character.Play, plan, new HashSet<string> { "exhaustion" });

        Assert.Equal(0, play.TemporaryHitPoints);
        Assert.Equal(2, play.Exhaustion); // unticked
        Assert.Equal(0, play.SpentOf(Feat.ContentId, "all"));
        Assert.Equal(2, play.SpentOf(Feat.ContentId, "some"));
        Assert.Equal(2, play.SpentOf(Feat.ContentId, "short"));
        Assert.Equal(2, character.Play.Exhaustion); // the plan never changes the character itself

        var fresh = character with { Play = new() };
        Assert.Empty(RestPlanner.LongRest(fresh, CharacterCalculator.Calculate(fresh, catalog)).Changes);
    }
}
