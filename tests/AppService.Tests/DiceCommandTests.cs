using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>D32: a labelled roll with no character, for the builder's ability scores. Writes nothing.</summary>
public class DiceCommandTests
{
    [Fact]
    public void Rolls_a_formula_with_keep_highest_and_the_label_in_the_provenance()
    {
        using var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(7);

        var record = temp.App.RollDice(new RollDiceCommand("4d6", KeepHighest: 3, Label: "Ability score roll 1"));

        var expected = new SeededRandomSource(7);
        var values = Enumerable.Range(0, 4).Select(_ => expected.Next(6)).ToList();
        Assert.Equal(values, record.Dice.Select(d => d.Value));
        Assert.Equal(1, record.Dice.Count(d => !d.Kept));
        Assert.Equal(values.Sum() - values.Min(), record.Total);
        Assert.Equal(("dice", "Ability score roll 1"), (record.Provenance!.RollId, record.Provenance.Label));
    }

    /// <summary>Hands out the scripted faces in order (1-based faces, as the real source does).</summary>
    private sealed class ScriptedRandom(params int[] faces) : IRandomSource
    {
        private int _next;

        public int Next(int sides) => faces[_next++];
    }

    [Theory]
    [InlineData(new[] { 3, 6, 1, 4 }, new[] { true, true, false, true })] // the lowest die is the one dropped
    [InlineData(new[] { 2, 5, 2, 6 }, new[] { false, true, true, true })] // tied lowest: the earliest is dropped
    [InlineData(new[] { 6, 6, 6, 6 }, new[] { false, true, true, true })] // all tied: still exactly one dropped, the earliest
    [InlineData(new[] { 1, 4, 3, 1 }, new[] { false, true, true, true })] // tied lowest at both ends: the earliest
    public void Keep_highest_drops_the_lowest_die_and_the_earliest_of_a_tie(int[] faces, bool[] kept)
    {
        using var temp = new TempApp();
        temp.App.Random = new ScriptedRandom(faces);

        var record = temp.App.RollDice(new RollDiceCommand("4d6", KeepHighest: 3));

        Assert.Equal(faces, record.Dice.Select(d => d.Value));
        Assert.Equal(kept, record.Dice.Select(d => d.Kept));
        Assert.Equal(faces.Sum() - faces.Min(), record.Total);
    }

    [Fact]
    public void Keep_highest_over_the_bridge_serialises_the_kept_flags()
    {
        using var temp = new TempApp();
        temp.App.Random = new ScriptedRandom(3, 6, 1, 4);
        var dispatcher = new CommandDispatcher(temp.App);

        using var response = JsonDocument.Parse(dispatcher.Dispatch("""{"id":"3","command":"dice.roll","payload":{"formula":"4d6","keepHighest":3,"label":"Ability score roll 3"}}"""));

        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        var result = response.RootElement.GetProperty("result");
        var dice = result.GetProperty("dice").EnumerateArray().ToList();
        Assert.Equal(new[] { 3, 6, 1, 4 }, dice.Select(d => d.GetProperty("value").GetInt32()));
        Assert.Equal(new[] { true, true, false, true }, dice.Select(d => d.GetProperty("kept").GetBoolean()));
        Assert.Equal(13, result.GetProperty("total").GetInt32());
    }

    [Fact]
    public void Labels_default_to_the_formula_and_are_bounded()
    {
        using var temp = new TempApp();
        Assert.Equal("2d6", temp.App.RollDice(new RollDiceCommand(" 2d6 ")).Provenance!.Label);
        Assert.Equal("2d6", temp.App.RollDice(new RollDiceCommand("2d6", Label: "   ")).Provenance!.Label);
        Assert.Equal(new string('x', 80), temp.App.RollDice(new RollDiceCommand("1d4", Label: new string('x', 80))).Provenance!.Label);
        // A long formula with no label reports the formula's own dice error, not the label length.
        var longFormula = Assert.Throws<AppValidationException>(() => temp.App.RollDice(new RollDiceCommand("1d6+" + new string('1', 90))));
        Assert.StartsWith("dice.", Assert.Single(longFormula.Problems).Code);
        Assert.NotEqual("dice.label-too-long", longFormula.Problems[0].Code);
        var tooLong = Assert.Throws<AppValidationException>(() => temp.App.RollDice(new RollDiceCommand("1d4", Label: new string('x', 81))));
        Assert.Equal("dice.label-too-long", Assert.Single(tooLong.Problems).Code);
    }

    [Theory]
    [InlineData("4d6+1d4", 3, "dice.keep-requires-single-term")]
    [InlineData("4d6", 4, "dice.keep-out-of-range")]
    [InlineData("1d2000", null, "dice.sides-out-of-range")]
    [InlineData(null, null, "dice.empty")]
    [InlineData("", null, "dice.empty")]
    [InlineData("   ", null, "dice.empty")]
    public void Dice_errors_come_back_with_their_codes(string? formula, int? keep, string code)
    {
        using var temp = new TempApp();
        var error = Assert.Throws<AppValidationException>(() => temp.App.RollDice(new RollDiceCommand(formula!, keep)));
        Assert.Equal(code, Assert.Single(error.Problems).Code);
    }

    [Fact]
    public void A_missing_formula_over_the_bridge_is_a_validation_error_not_an_internal_one()
    {
        using var temp = new TempApp();
        var dispatcher = new CommandDispatcher(temp.App);
        using var response = JsonDocument.Parse(dispatcher.Dispatch("""{"id":"1","command":"dice.roll","payload":{"keepHighest":3}}"""));
        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        var error = response.RootElement.GetProperty("error");
        Assert.Equal("validation", error.GetProperty("code").GetString());
        Assert.Equal("dice.empty", error.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public void An_explicit_null_formula_over_the_bridge_is_a_bad_request_not_a_dice_error()
    {
        // Unlike an absent formula (validation + dice.empty above), an explicit null is refused while the payload is bound.
        using var temp = new TempApp();
        var dispatcher = new CommandDispatcher(temp.App);
        using var response = JsonDocument.Parse(dispatcher.Dispatch("""{"id":"4","command":"dice.roll","payload":{"formula":null,"keepHighest":3}}"""));
        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("bad-request", response.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void The_command_is_on_the_bridge_allowlist_and_writes_nothing()
    {
        using var temp = new TempApp();
        Assert.Contains("dice.roll", CommandDispatcher.Commands);
        // Same pattern as RollCommandTests: the stored character as JSON before and after (plus the list count).
        var id = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-ash-m1.json") with { Pins = [] }).Character.Id;
        var before = TempApp.Json(temp.App.Store.FindCharacter(id));
        var count = temp.App.ListCharacters().Count;
        temp.App.RollDice(new RollDiceCommand("1d20"));
        Assert.Equal(before, TempApp.Json(temp.App.Store.FindCharacter(id)));
        Assert.Equal(count, temp.App.ListCharacters().Count);

        var dispatcher = new CommandDispatcher(temp.App);
        using var response = JsonDocument.Parse(dispatcher.Dispatch("""{"id":"2","command":"dice.roll","payload":{"formula":"4d6","keepHighest":3,"label":"Ability score roll 2"}}"""));
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Ability score roll 2", response.RootElement.GetProperty("result").GetProperty("provenance").GetProperty("label").GetString());
    }
}
