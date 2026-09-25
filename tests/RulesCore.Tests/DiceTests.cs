using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>Item 13: dice engine skeleton (SPEC C-04; ARCHITECTURE "commands vs calculation").</summary>
public class DiceTests
{
    /// <summary>Returns the queued values in order, so expected dice are explicit.</summary>
    private sealed class ScriptedRandom(params int[] values) : IRandomSource
    {
        private int _next;

        public List<int> SidesAsked { get; } = [];

        public int Next(int sides)
        {
            SidesAsked.Add(sides);
            return values[_next++];
        }
    }

    private static RollRecord Roll(RollRequest request, IRandomSource random)
    {
        Assert.True(DiceRoller.TryRoll(request, random, out var record, out var error), error?.Message);
        return record!;
    }

    [Theory]
    [InlineData("1d20+5", 1, 20, 5)]
    [InlineData("d20", 1, 20, 0)]
    [InlineData("2d6", 2, 6, 0)]
    [InlineData("1D8 - 1", 1, 8, -1)]
    [InlineData(" 3d4 + 10 ", 3, 4, 10)]
    public void Parses_NdM_plus_K(string source, int count, int sides, int constant)
    {
        Assert.True(DiceExpression.TryParse(source, out var expression, out _));

        var dice = expression!.Terms.Single(t => t.IsDice);
        Assert.Equal((count, sides), (dice.Count, dice.Sides));
        Assert.Equal(constant, expression.Terms.Where(t => !t.IsDice).Sum(t => t.Sign * t.Constant));
    }

    [Fact]
    public void Rolls_record_every_die_and_sum_dice_constants_and_modifiers()
    {
        var random = new ScriptedRandom(4, 6, 3);
        var request = new RollRequest("1d8+2d6+2", Modifiers: [new("Strength modifier", 3), new("Magic weapon", 1)]);

        var record = Roll(request, random);

        Assert.Equal([8, 6, 6], random.SidesAsked);
        Assert.Equal([(0, 8, 4), (1, 6, 6), (1, 6, 3)], record.Dice.Select(d => (d.Term, d.Sides, d.Value)));
        Assert.Equal((13, 2, 4 + 6 + 3 + 2 + 3 + 1), (record.DiceTotal, record.ExpressionConstant, record.Total));
        Assert.Equal("1d8+2d6+2", record.Formula);
    }

    [Theory]
    [InlineData(RollMode.Advantage, 7, 15, 15)]
    [InlineData(RollMode.Advantage, 15, 7, 15)]
    [InlineData(RollMode.Disadvantage, 7, 15, 7)]
    [InlineData(RollMode.Disadvantage, 15, 7, 7)]
    public void Advantage_and_disadvantage_roll_two_d20s_and_record_which_was_kept(RollMode mode, int first, int second, int kept)
    {
        var record = Roll(new RollRequest("1d20+4", mode), new ScriptedRandom(first, second));

        Assert.Equal([(first, first == kept), (second, second == kept && first != kept)], record.Dice.Select(d => (d.Value, d.Kept)));
        Assert.Equal(kept + 4, record.Total);
        Assert.Equal(mode, record.Mode);
    }

    [Fact]
    public void Advantage_only_applies_to_a_single_d20()
    {
        Assert.False(DiceRoller.TryRoll(new RollRequest("2d6+1", RollMode.Advantage), new SeededRandomSource(1), out _, out var error));
        Assert.Equal("dice.advantage-requires-d20", error!.Code);
        Assert.False(DiceRoller.TryRoll(new RollRequest("2d20", RollMode.Disadvantage), new SeededRandomSource(1), out _, out error));
        Assert.Equal("dice.advantage-requires-d20", error!.Code);
    }

    [Fact]
    public void Critical_doubles_the_dice_but_not_the_modifiers()
    {
        var record = Roll(new RollRequest("1d8+2d6+3", Critical: true, Modifiers: [new("Strength modifier", 2)]), new ScriptedRandom(5, 1, 6, 6, 2, 3, 4));

        Assert.Equal(2 + 4, record.Dice.Count);
        Assert.Equal([false, true, false, false, true, true], record.Dice.Select(d => d.FromCritical));
        Assert.Equal(5 + 1 + 6 + 6 + 2 + 3, record.DiceTotal);
        Assert.Equal(record.DiceTotal + 3 + 2, record.Total);
        Assert.True(record.Critical);
    }

    [Fact]
    public void Seeded_rolls_are_reproducible_and_stay_in_range()
    {
        var a = Roll(new RollRequest("10d6+1"), new SeededRandomSource(42));
        var b = Roll(new RollRequest("10d6+1"), new SeededRandomSource(42));
        var c = Roll(new RollRequest("10d6+1"), new SeededRandomSource(43));

        Assert.Equal(a.Dice, b.Dice);
        Assert.NotEqual(a.Dice, c.Dice);

        var random = new SeededRandomSource(7);
        var counts = new int[21];
        for (var i = 0; i < 20_000; i++)
            counts[random.Next(20)]++;
        Assert.Equal(0, counts[0]);
        Assert.All(counts.Skip(1), n => Assert.InRange(n, 800, 1_200)); // uniform-ish: about 1,000 per face
    }

    /// <summary>
    /// The first eight d20s from seed 1, computed outside .NET (a Python SplitMix64 with the same rejection bound).
    /// Seeded examples and tests depend on this sequence, so changing the generator must update it on purpose.
    /// </summary>
    [Fact]
    public void Seeded_sequence_is_pinned_so_a_generator_change_cannot_silently_alter_it()
    {
        var random = new SeededRandomSource(1);

        Assert.Equal([6, 20, 11, 16, 2, 9, 6, 14], Enumerable.Range(0, 8).Select(_ => random.Next(20)));
    }

    [Theory]
    [InlineData("", "dice.empty")]
    [InlineData("d", "dice.syntax")]
    [InlineData("1d", "dice.syntax")]
    [InlineData("0d6", "dice.syntax")]
    [InlineData("5", "dice.no-dice")]
    [InlineData("1d20+", "dice.syntax")]
    [InlineData("1d20++2", "dice.syntax")]
    [InlineData("1d20*2", "dice.syntax")]
    [InlineData("1d1", "dice.sides-out-of-range")]
    [InlineData("1d100000", "dice.sides-out-of-range")]
    [InlineData("101d6", "dice.too-many-dice")]
    [InlineData("99999999d6", "dice.too-many-dice")]
    [InlineData("60d6+60d6", "dice.too-many-dice")]
    [InlineData("1d20+5000", "dice.modifier-out-of-range")]
    [InlineData("1d4+1d4+1d4+1d4+1d4+1d4+1d4+1d4+1", "dice.too-many-terms")]
    [InlineData("1d20+PB", "dice.syntax")]
    [InlineData("1d20; rm", "dice.syntax")]
    [InlineData("1d20+1d20+1d20+1d20+1d20+1d20+1d20+1d20+1", "dice.too-long")] // 41 characters
    public void Malformed_dice_are_rejected_with_a_stable_code(string source, string code)
    {
        Assert.False(DiceExpression.TryParse(source, out _, out var error));
        Assert.Equal(code, error!.Code);
    }

    [Fact]
    public void Fuzzed_dice_input_never_throws()
    {
        const string alphabet = "0123456789dD+- *x()PB;";
        var random = new Random(13);
        for (var i = 0; i < 10_000; i++)
        {
            var source = new string([.. Enumerable.Range(0, random.Next(0, 20)).Select(_ => alphabet[random.Next(alphabet.Length)])]);
            if (DiceRoller.TryRoll(new RollRequest(source), new SeededRandomSource((ulong)i), out var record, out var error))
                Assert.InRange(record!.Dice.Count, 1, DiceLimits.MaxDice);
            else
                Assert.StartsWith("dice.", error!.Code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Roll_from_a_content_effect_cites_its_revision_and_source_and_never_consumes_the_linked_resource()
    {
        var strike = new RollEffect { Id = "strike", RollId = "star-strike", Label = "Star Strike", Dice = "1d8+2", ResourceId = "star-focus", Timing = EffectTiming.OnRoll };
        var revision = Fixtures.Pack().Revisions.Single(r => r.Reference == Fixtures.StarSense) with { Effects = [strike] };
        var source = Fixtures.Catalog().FindSource(revision.Provenance.SourceId);
        var character = Fixtures.Srd521Character();
        var before = JsonSerializer.Serialize(character, RulesJson.Compact);

        var initiative = CharacterCalculator.Calculate(character, Fixtures.Catalog()).Field(FieldIds.Initiative);
        var request = DiceRoller.FromEffect(strike, revision, source, critical: true, modifiers: [new("Initiative", initiative.Value, initiative.Trace[^1].Origin)]);
        var record = Roll(request, new SeededRandomSource(3));

        Assert.Equal(("star-strike", "Star Strike", Fixtures.StarSense, "strike"), (record.Provenance!.RollId, record.Provenance.Label, record.Provenance.Content, record.Provenance.EffectId));
        Assert.Equal(("TomeStack Fixtures: Shared", new PageRef(7), "star-focus"), (record.Provenance.SourceTitle, record.Provenance.Page, record.Provenance.LinkedResourceId));
        Assert.Equal(2, record.Dice.Count);
        Assert.Equal(record.DiceTotal + 2 + initiative.Value, record.Total);
        // Rolling is not spending: the character (and any resource state it will hold) is unchanged.
        Assert.Equal(before, JsonSerializer.Serialize(character, RulesJson.Compact));
    }

    [Fact]
    public void Roll_records_serialize_for_the_ui_contract()
    {
        var record = Roll(new RollRequest("1d20+1", RollMode.Advantage, Provenance: new("init", "Initiative")), new ScriptedRandom(3, 18));

        var json = JsonSerializer.Serialize(record, RulesJson.Compact);

        Assert.Contains("\"mode\":\"advantage\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dice\":[{\"term\":0,\"sides\":20,\"value\":3,\"kept\":false,\"fromCritical\":false},{\"term\":0,\"sides\":20,\"value\":18,\"kept\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"total\":19", json, StringComparison.Ordinal);
    }
}
