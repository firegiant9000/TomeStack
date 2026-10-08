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
