using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests;

/// <summary>Content v12 (G9): the roller raises d20s to a face floor and the total to a total floor.</summary>
public class RollFloorRollerTests
{
    private sealed class ScriptedRandom(params int[] values) : IRandomSource
    {
        private int _next;

        public int Next(int sides) => values[_next++];
    }

    private static RollFloorSummary Floor(int? face = null, int? total = null) => new(face, total, []);

    private static RollRecord Roll(RollRequest request, IRandomSource random)
    {
        Assert.True(DiceRoller.TryRoll(request, random, out var record, out var error), error?.Message);
        return record!;
    }

    [Fact]
    public void Face_floor_raises_a_low_d20_and_records_the_raw_face()
    {
        var record = Roll(new RollRequest("1d20+3", Floor: Floor(face: 10)), new ScriptedRandom(4));

        var die = Assert.Single(record.Dice);
        Assert.Equal(10, die.Value);
        Assert.Equal(4, die.Rolled);
        Assert.Equal(13, record.Total);
        Assert.Null(record.Unfloored);
        Assert.NotNull(record.Floor);
    }

    [Fact]
    public void Face_floor_leaves_a_high_d20_alone()
    {
        var record = Roll(new RollRequest("1d20", Floor: Floor(face: 10)), new ScriptedRandom(15));

        Assert.Equal(15, record.Dice[0].Value);
        Assert.Null(record.Dice[0].Rolled);
    }

    [Fact]
    public void Advantage_with_face_floor_raises_both_before_keeping()
    {
        var record = Roll(new RollRequest("1d20", RollMode.Advantage, Floor: Floor(face: 10)), new ScriptedRandom(3, 7));

        Assert.Equal([10, 10], record.Dice.Select(d => d.Value));
        Assert.Equal([3, 7], record.Dice.Select(d => d.Rolled));
        Assert.Equal(10, record.Total);
        Assert.Single(record.Dice, d => d.Kept);
    }

    [Fact]
    public void Advantage_with_face_floor_keeps_the_higher_when_one_clears_it()
    {
        var record = Roll(new RollRequest("1d20", RollMode.Advantage, Floor: Floor(face: 10)), new ScriptedRandom(3, 17));

        Assert.Equal(17, record.Total);
        Assert.Equal([10, 17], record.Dice.Select(d => d.Value));
        Assert.Equal([3, null], record.Dice.Select(d => d.Rolled));
    }

    [Fact]
    public void Disadvantage_with_face_floor_raises_both_before_keeping()
    {
        var record = Roll(new RollRequest("1d20", RollMode.Disadvantage, Floor: Floor(face: 10)), new ScriptedRandom(3, 17));

        Assert.Equal(10, record.Total);
        Assert.Equal([10, 17], record.Dice.Select(d => d.Value));
        Assert.Equal([3, null], record.Dice.Select(d => d.Rolled));
    }

    [Fact]
    public void Total_floor_raises_the_total_after_modifiers()
    {
        var record = Roll(
            new RollRequest("1d20+2", Modifiers: [new RollModifier("Bless", 1)], Floor: Floor(total: 15)),
            new ScriptedRandom(5));

        Assert.Equal(15, record.Total);
        Assert.Equal(8, record.Unfloored);
        Assert.Equal(5, record.Dice[0].Value);
    }

    [Fact]
    public void Total_floor_at_or_below_the_total_changes_nothing()
    {
        var record = Roll(new RollRequest("1d20+2", Floor: Floor(total: 9)), new ScriptedRandom(7));

        Assert.Equal(9, record.Total);
        Assert.Null(record.Unfloored);
        Assert.Equal(9, record.Floor!.Total);
    }

    [Fact]
    public void Face_floor_ignores_non_d20_dice()
    {
        var record = Roll(new RollRequest("1d6", Floor: Floor(face: 4)), new ScriptedRandom(2));

        Assert.Equal(2, record.Dice[0].Value);
        Assert.Null(record.Dice[0].Rolled);
        Assert.Equal(2, record.Total);
    }

    [Fact]
    public void Null_floor_changes_nothing()
    {
        var record = Roll(new RollRequest("1d20+1"), new ScriptedRandom(2));

        Assert.Equal(3, record.Total);
        Assert.Null(record.Floor);
        Assert.Null(record.Unfloored);
        Assert.Null(record.Dice[0].Rolled);
    }

    [Fact]
    public void Undoubled_dice_are_never_floored()
    {
        var record = Roll(new RollRequest("1d20", Floor: Floor(face: 10), Undoubled: "1d20"), new ScriptedRandom(2, 3));

        Assert.Equal([10, 3], record.Dice.Select(d => d.Value));
        Assert.Equal([2, null], record.Dice.Select(d => d.Rolled));
    }

    [Property(MaxTest = 500)]
    public Property Advantage_with_a_face_floor_equals_the_max_of_the_floored_faces() =>
        Prop.ForAll(
            Gen.Choose(2, 20).ToArbitrary(),
            Gen.Choose(int.MinValue, int.MaxValue).ToArbitrary(),
            (face, seed) =>
            {
                var raw = new SeededRandomSource((ulong)(uint)seed);
                var a = raw.Next(20);
                var b = raw.Next(20);
                var record = Roll(
                    new RollRequest("1d20", RollMode.Advantage, Floor: Floor(face: face)),
                    new SeededRandomSource((ulong)(uint)seed));
                return record.Total == Math.Max(Math.Max(a, face), Math.Max(b, face));
            });
}
