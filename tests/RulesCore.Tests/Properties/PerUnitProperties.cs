using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// Play mechanics slice 5 (D43, Task 5.3): the dice a per-unit roll shows are the base dice plus one per-unit group for every
/// unit spent above the base cost, whatever the base, the cap and the amount.
/// </summary>
public class PerUnitProperties
{
    private static Gen<(int Base, int MaxUnits, int Amount)> Spends { get; } =
        from baseCost in Gen.Choose(0, 5)
        from maxUnits in Gen.Choose(1, 10)
        from extra in Gen.Choose(0, maxUnits)
        select (baseCost, maxUnits, baseCost + extra);

    [Property(MaxTest = 200)]
    public Property Dice_count_extra_equals_amount_minus_base() =>
        Prop.ForAll(Spends.ToArbitrary(), spend =>
        {
            const int baseCount = 4;
            const int perUnitCount = 2;
            var formula = DiceRoller.PerUnitFormula($"{baseCount}d10", $"{perUnitCount}d6", spend.Base, spend.Amount);
            var request = new RollRequest(formula);
            var rolled = DiceRoller.TryRoll(request, new SeededRandomSource(1), out var record, out _);
            return (rolled && record!.Dice.Count == baseCount + (spend.Amount - spend.Base) * perUnitCount)
                .Label($"{formula}: {record?.Dice.Count}");
        });

    [Fact]
    public void The_extra_dice_are_summed_into_one_term_and_nothing_is_added_at_the_base()
    {
        Assert.Equal("4d10+2d10", DiceRoller.PerUnitFormula("4d10", "1d10", 2, 4));
        Assert.Equal("4d10", DiceRoller.PerUnitFormula("4d10", "1d10", 2, 2));
    }
}
