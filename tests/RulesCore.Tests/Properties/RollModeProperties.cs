using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>D43 S6 task 6.2: combining roll modes is order independent, and any advantage with any disadvantage cancels.</summary>
public class RollModeProperties
{
    private static Gen<RollMode[]> Modes { get; } =
        Gen.Elements(RollMode.Normal, RollMode.Advantage, RollMode.Disadvantage).ListOf().Select(l => l.ToArray());

    /// <summary>A seeded Fisher-Yates permutation.</summary>
    private static RollMode[] Shuffle(RollMode[] modes, int seed)
    {
        var copy = (RollMode[])modes.Clone();
        var random = new Random(seed);
        for (var i = copy.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    [Property(MaxTest = 300)]
    public Property Combine_is_order_independent_and_cancels() =>
        Prop.ForAll(Modes.ToArbitrary(), Gen.Choose(0, 1000).ToArbitrary(), (modes, seed) =>
        {
            var shuffled = Shuffle(modes, seed);
            var combined = RollModes.Combine(modes);
            var order = combined == RollModes.Combine(shuffled);
            var hasAdv = modes.Contains(RollMode.Advantage);
            var hasDis = modes.Contains(RollMode.Disadvantage);
            var expected = hasAdv == hasDis ? RollMode.Normal : hasAdv ? RollMode.Advantage : RollMode.Disadvantage;
            return order && combined == expected;
        });
}
