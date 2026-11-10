using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>Content v12 (G1): a constant <c>scaledDice</c> rider is the same as the literal <c>dice</c> rider, in the damage, the rider values and the stack-group winner.</summary>
public class ScaledDiceProperties
{
    private static readonly int[] Dice = [4, 6, 8, 10, 12];

    private static Gen<(int Count, int Die)> Rolls { get; } =
        from count in Gen.Choose(1, 10)
        from die in Gen.Elements(Dice)
        select (count, die);

    private static (string Damage, IReadOnlyList<(string Effect, string? Dice)> Riders) Run(Func<AttackRiderEffect, AttackRiderEffect> change)
    {
        // Kindled (the changed rider) and blazing (4d8, same stack group) both on: the higher expected value wins.
        var catalog = ScaledDiceV12SheetTests.With(Fixtures.KindledMode, e => e is AttackRiderEffect r ? change(r) : e);
        var character = ScaledDiceV12SheetTests.Adept(
            5, new ActiveToggle(Fixtures.KindledMode.ContentId, "kindled"), new ActiveToggle(Fixtures.BlazingMode.ContentId, "blazing"));
        var strike = CharacterCalculator.Calculate(character, catalog).Attacks!.Single(a => a.EffectId == "strike");
        return (strike.Damage, [.. strike.Riders!.Select(r => (r.EffectId, r.Dice))]);
    }

    [Property(MaxTest = 60)]
    public Property Constant_scaled_rider_equals_literal_dice() =>
        Prop.ForAll(Rolls.ToArbitrary(), roll =>
        {
            var literal = Run(r => r with { Dice = $"{roll.Count}d{roll.Die}" });
            var scaled = Run(r => r with { Dice = null, ScaledDice = new($"{roll.Count}", $"{roll.Die}") });
            return (literal.Damage == scaled.Damage && literal.Riders.SequenceEqual(scaled.Riders))
                .Label($"{roll.Count}d{roll.Die}: {literal.Damage} / {scaled.Damage}");
        });
}
