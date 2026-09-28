using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// D01 follow-up (owner, 2026-09-27: SRD rules, previewed): hit dice pool per die size, a short rest spends the dice the
/// player rolled, and a long rest regains them under each family's rule. Original fixtures: "Fixture Warden" (d10) and
/// "Fixture Scholar" (d6) from <c>fixture-pack-m1.json</c>.
/// </summary>
public class ShortRestAndHitDiceTests
{
    private static Character Ash(string family, PlayState play, params ClassLevel[] classes) =>
        Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = classes,
            Level = classes.Sum(c => c.Level),
            Play = play,
        };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.M1Catalog());

    private static readonly ClassLevel[] WardenAndScholar = [new(Fixtures.Warden, 5), new(Fixtures.Scholar, 3)];

    [Fact]
    public void Hit_dice_pool_per_die_size_largest_first()
    {
        var sheet = Sheet(Ash(RulesFamilies.Srd521, new PlayState().WithHitDiceSpent(10, 2), WardenAndScholar));

        Assert.Equal(
            [new HitDiceValue(10, 5, 2, 3, ["Fixture Warden"]), new HitDiceValue(6, 3, 0, 3, ["Fixture Scholar"])],
            sheet.HitDice!.Select(h => h with { Classes = [.. h.Classes] }),
            new HitDiceComparer());
    }

    [Fact]
    public void Long_rest_hit_dice_half_the_total_under_2014_and_all_under_2024_side_by_side()
    {
        // 8 hit dice in all, 7 spent (5 d10, 2 d6). 2014: half of 8 = 4 back, largest first. 2024: all 7 back.
        var tired = new PlayState { CurrentHitPoints = 1 }.WithHitDiceSpent(10, 5).WithHitDiceSpent(6, 2);
        RestPlan Plan(string family)
        {
            var character = Ash(family, tired, WardenAndScholar);
            return RestPlanner.LongRest(character, Sheet(character));
        }

        var dice2014 = Plan(RulesFamilies.Srd51).Changes.Where(c => c.Kind == RestChangeKind.HitDice).ToList();
        var dice2024 = Plan(RulesFamilies.Srd521).Changes.Where(c => c.Kind == RestChangeKind.HitDice).ToList();

        var d10 = Assert.Single(dice2014);
        Assert.Equal((10, 0, 4, 1), (d10.Die!.Value, d10.From, d10.To, d10.SpentAfter!.Value));
        Assert.Contains("choose which hit dice", d10.Condition, StringComparison.Ordinal);
        Assert.Equal([(10, 0, 5, 0), (6, 1, 3, 0)], dice2024.Select(c => (c.Die!.Value, c.From, c.To, c.SpentAfter!.Value)));
        Assert.All(dice2024, c => Assert.Null(c.Condition));
        Assert.Equal(HitDiceRecovery.HalfTotal, RulesFamilies.Get(RulesFamilies.Srd51).LongRestHitDice);
        Assert.Equal(HitDiceRecovery.All, RulesFamilies.Get(RulesFamilies.Srd521).LongRestHitDice);
    }

    [Fact]
    public void Under_2014_rules_a_long_rest_gives_back_at_least_one_hit_die()
    {
        var character = Ash(RulesFamilies.Srd51, new PlayState().WithHitDiceSpent(10, 1), new ClassLevel(Fixtures.Warden, 1));

        var change = Assert.Single(RestPlanner.LongRest(character, Sheet(character)).Changes, c => c.Kind == RestChangeKind.HitDice);

        Assert.Equal((0, 1), (change.From, change.To)); // half of 1 is 0, but the minimum is one die
    }

    [Fact]
    public void A_short_rest_heals_each_rolled_die_plus_con_and_the_minimum_differs_side_by_side()
    {
        var play = new PlayState { CurrentHitPoints = 3 };
        (RestPlan Plan, int Con, int Max) For(string family, params HitDieRoll[] rolls)
        {
            var character = Ash(family, play, WardenAndScholar) with { BaseAbilities = Fixtures.Load("srd521-ash-m1.json").BaseAbilities with { Con = 6 } };
            var sheet = Sheet(character);
            Assert.Empty(RestPlanner.CheckHitDice(sheet, rolls));
            return (RestPlanner.ShortRest(character, sheet, rolls), sheet.Field(FieldIds.Modifier(Ability.Con)).Value, sheet.HitPoints!.Maximum);
        }

        // Con 6 is a -2 modifier: a d6 showing 1 gives -1, so 0 hit points under 2014 and 1 under 2024.
        var (plan2014, con, _) = For(RulesFamilies.Srd51, new HitDieRoll(10, 7), new HitDieRoll(6, 1));
        var (plan2024, _, _) = For(RulesFamilies.Srd521, new HitDieRoll(10, 7), new HitDieRoll(6, 1));

        Assert.Equal(-2, con);
        Assert.Equal([(3, 8, 5), (8, 8, 0)], plan2014.Changes.Select(c => (c.From, c.To, c.Amount!.Value)));
        Assert.Equal([(3, 8, 5), (8, 9, 1)], plan2024.Changes.Select(c => (c.From, c.To, c.Amount!.Value)));
        Assert.Contains("(at least 1)", plan2024.Changes[1].Reason, StringComparison.Ordinal);
        Assert.Equal(0, RulesFamilies.Get(RulesFamilies.Srd51).HitDieHealingMinimum);
        Assert.Equal(1, RulesFamilies.Get(RulesFamilies.Srd521).HitDieHealingMinimum);
    }

    [Fact]
    public void Applying_a_short_rest_spends_only_the_ticked_dice_and_never_passes_the_maximum()
    {
        var character = Ash(RulesFamilies.Srd521, new PlayState { CurrentHitPoints = 3 }, WardenAndScholar);
        var sheet = Sheet(character);
        var maximum = sheet.HitPoints!.Maximum;
        var plan = RestPlanner.ShortRest(character, sheet, [new(10, 10), new(10, 10), new(10, 10), new(6, 6), new(10, 10)]);

        var play = RestPlanner.Apply(character.Play, plan, new HashSet<string> { "hitDie:1" }, maximum);

        Assert.Equal(3, play.HitDiceSpentOf(10)); // four d10s rolled, one unticked
        Assert.Equal(1, play.HitDiceSpentOf(6));
        var expected = Math.Min(3 + (10 + Con(sheet)) * 3 + Math.Max(6 + Con(sheet), 1), maximum);
        Assert.Equal(expected >= maximum ? null : expected, play.CurrentHitPoints);
        Assert.Equal(3, character.Play.CurrentHitPoints); // the plan never changes the character itself
    }

    private static int Con(CharacterSheet sheet) => sheet.Field(FieldIds.Modifier(Ability.Con)).Value;

    [Fact]
    public void Hit_dice_the_character_does_not_have_are_refused()
    {
        var sheet = Sheet(Ash(RulesFamilies.Srd521, new PlayState().WithHitDiceSpent(6, 3), WardenAndScholar));

        Assert.Equal(["rest.hit-die-unknown"], RestPlanner.CheckHitDice(sheet, [new(12, 5)]).Select(d => d.Code));
        Assert.Equal(["rest.hit-dice-insufficient"], RestPlanner.CheckHitDice(sheet, [new(6, 2)]).Select(d => d.Code));
        Assert.Equal(["rest.hit-die-roll-invalid"], RestPlanner.CheckHitDice(sheet, [new(10, 11)]).Select(d => d.Code));
        Assert.Equal(["rest.hit-die-roll-invalid"], RestPlanner.CheckHitDice(sheet, [new(10, 0)]).Select(d => d.Code));
    }

    [Fact]
    public void Regaining_hit_points_on_a_rest_resets_death_saves()
    {
        var dying = new PlayState { CurrentHitPoints = 0, DeathSaves = new(1, 2) };
        var character = Ash(RulesFamilies.Srd51, dying, WardenAndScholar); // 2014: a short rest at 0 hit points is allowed
        var sheet = Sheet(character);
        Assert.Null(RestPlanner.CannotRest(character, sheet, RestPeriod.ShortRest));

        var plan = RestPlanner.ShortRest(character, sheet, [new(10, 9)]);
        Assert.Equal(RestChangeKind.DeathSaves, plan.Changes[^1].Kind);
        Assert.Equal(new DeathSaves(), RestPlanner.Apply(dying, plan, new HashSet<string>(), sheet.HitPoints!.Maximum).DeathSaves);
        // Unticking the hit die keeps the saves: no hit points were regained.
        Assert.Equal(new DeathSaves(1, 2), RestPlanner.Apply(dying, plan, new HashSet<string> { "hitDie:0" }, sheet.HitPoints.Maximum).DeathSaves);
    }

    [Fact]
    public void Resting_at_0_hit_points_follows_each_family_side_by_side()
    {
        var dying = new PlayState { CurrentHitPoints = 0 };
        var old = Ash(RulesFamilies.Srd51, dying, WardenAndScholar);
        var current = Ash(RulesFamilies.Srd521, dying, WardenAndScholar);

        Assert.Null(RestPlanner.CannotRest(old, Sheet(old), RestPeriod.ShortRest));
        Assert.Equal("rest.needs-hit-points", RestPlanner.CannotRest(current, Sheet(current), RestPeriod.ShortRest)?.Code);
        Assert.Equal("rest.needs-hit-points", RestPlanner.CannotRest(old, Sheet(old), RestPeriod.LongRest)?.Code);
        Assert.Equal("rest.needs-hit-points", RestPlanner.CannotRest(current, Sheet(current), RestPeriod.LongRest)?.Code);
    }

    [Theory]
    [InlineData(20, 0, 0, true)]
    [InlineData(1, 0, 2, false)]
    [InlineData(10, 1, 0, false)]
    [InlineData(9, 0, 1, false)]
    public void Death_save_outcomes_follow_the_srd(int d20, int successes, int failures, bool regains)
    {
        var outcome = DeathSaves.Outcome(d20);

        Assert.Equal((successes, failures, regains), (outcome.Successes, outcome.Failures, outcome.RegainsOneHitPoint));
        Assert.Equal(new DeathSaves(2, 3), new DeathSaves(2, 2).After(DeathSaves.Outcome(1))); // stops at 3
    }

    [Fact]
    public void Invalid_play_state_is_reported()
    {
        var play = new PlayState { DeathSaves = new(4, 0), HitDiceSpent = [new(7, 1), new(10, 1), new(10, 2)] };

        Assert.Equal(
            ["play.hit-dice-invalid", "play.hit-dice-duplicate", "play.death-saves-out-of-range"],
            play.Validate().Select(d => d.Code));
    }

    private sealed class HitDiceComparer : IEqualityComparer<HitDiceValue>
    {
        public bool Equals(HitDiceValue? x, HitDiceValue? y) =>
            x is not null && y is not null && (x.Die, x.Total, x.Spent, x.Remaining) == (y.Die, y.Total, y.Spent, y.Remaining) && x.Classes.SequenceEqual(y.Classes);

        public int GetHashCode(HitDiceValue obj) => obj.Die;
    }
}
