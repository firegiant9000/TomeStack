using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// Play mechanics slice 8 (D43, Task 8.2): whatever the starting state and whatever sequence of gathers and releases is applied, a
/// pool stays between 0 and its maximum, and a refused step hands back the state it was given.
/// </summary>
public class ChargingProperties
{
    private static readonly Guid Class = Fixtures.ChargeAdept.ContentId;

    private static readonly CharacterSheet Sheet =
        CharacterCalculator.Calculate(Fixtures.Load("pm-charger-l5.json"), Fixtures.PlayMechanicsCatalog());

    private static readonly int FocusMaximum = Sheet.Resources!.Single(r => r.ResourceId == "focus").Maximum!.Value;

    private static readonly int ChargeMaximum = Sheet.Resources!.Single(r => r.ResourceId == "charge").Maximum!.Value;

    private static Gen<IReadOnlyList<PoolChange>> Step { get; } =
        from kind in Gen.Choose(0, 2)
        from amount in Gen.Choose(0, 6)
        select (IReadOnlyList<PoolChange>)(kind switch
        {
            // Gather: one focus and one charge, together.
            0 => [new PoolChange(Class, "focus", UpkeepChange.Spend, 1), new PoolChange(Class, "charge", UpkeepChange.Spend, 1)],
            // Release: draw the charge down by an amount.
            1 => [new PoolChange(Class, "charge", UpkeepChange.Regain, amount)],
            // A stray spend of focus.
            _ => [new PoolChange(Class, "focus", UpkeepChange.Spend, amount)],
        });

    private static Gen<(int Focus, int Charge, List<IReadOnlyList<PoolChange>> Steps)> Runs { get; } =
        from focus in Gen.Choose(0, FocusMaximum)
        from charge in Gen.Choose(0, ChargeMaximum)
        from steps in Gen.ListOf(Step).Select(s => s.ToList())
        select (focus, charge, steps);

    [Property(MaxTest = 300)]
    public Property A_use_never_leaves_a_pool_outside_its_bounds() =>
        Prop.ForAll(Runs.ToArbitrary(), run =>
        {
            var play = new PlayState().WithSpent(Class, "focus", run.Focus).WithSpent(Class, "charge", run.Charge);
            foreach (var step in run.Steps)
            {
                var (next, errors) = PoolChanges.Apply(play, Sheet, step);
                if (errors.Count > 0 && !ReferenceEquals(next, play))
                    return false.Label("a refused step must return the state it was given");
                play = next;
                var focus = play.SpentOf(Class, "focus");
                var charge = play.SpentOf(Class, "charge");
                if (focus < 0 || focus > FocusMaximum || charge < 0 || charge > ChargeMaximum)
                    return false.Label($"focus spent {focus}/{FocusMaximum}, charge built up {charge}/{ChargeMaximum}");
            }
            return true.Label("in bounds");
        });

    [Fact]
    public void A_refused_set_of_changes_applies_none_of_them()
    {
        var play = new PlayState().WithSpent(Class, "charge", ChargeMaximum);

        var (next, errors) = PoolChanges.Apply(play, Sheet, [new PoolChange(Class, "focus", UpkeepChange.Spend, 1), new PoolChange(Class, "charge", UpkeepChange.Spend, 1)]);

        Assert.Same(play, next);
        Assert.Equal(["use.pool-full"], errors.Select(e => e.Code));
    }

    [Fact]
    public void Two_changes_to_one_pool_add_up()
    {
        var play = new PlayState().WithSpent(Class, "focus", FocusMaximum - 1);

        var (next, errors) = PoolChanges.Apply(play, Sheet, [new PoolChange(Class, "focus", UpkeepChange.Spend, 1), new PoolChange(Class, "focus", UpkeepChange.Spend, 1)]);

        Assert.Same(play, next);
        Assert.Equal(["use.unaffordable"], errors.Select(e => e.Code));
    }

    [Fact]
    public void A_regain_on_a_normal_pool_clamps_and_a_regain_on_a_counting_up_pool_cannot_go_below_nothing()
    {
        var play = new PlayState().WithSpent(Class, "focus", 2).WithSpent(Class, "charge", 1);

        var (clamped, none) = PoolChanges.Apply(play, Sheet, [new PoolChange(Class, "focus", UpkeepChange.Regain, 5)]);
        var (kept, below) = PoolChanges.Apply(play, Sheet, [new PoolChange(Class, "charge", UpkeepChange.Regain, 2)]);

        Assert.Empty(none);
        Assert.Equal(0, clamped.SpentOf(Class, "focus"));
        Assert.Same(play, kept);
        Assert.Equal(["use.unaffordable"], below.Select(e => e.Code));
    }

    [Fact]
    public void A_pool_that_is_not_on_the_sheet_is_unavailable()
    {
        var play = new PlayState();

        var (next, errors) = PoolChanges.Apply(play, Sheet, [new PoolChange(Class, "nope", UpkeepChange.Spend, 1), new PoolChange(Guid.NewGuid(), "focus", UpkeepChange.Spend, 1)]);

        Assert.Same(play, next);
        Assert.Equal(["use.unavailable", "use.unavailable"], errors.Select(e => e.Code));
    }
}
