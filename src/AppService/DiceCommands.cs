using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// D32: a roll that belongs to no character (the builder's ability scores, before anything is saved). The formula obeys
/// the dice limits (SPEC Q-02); <paramref name="KeepHighest"/> keeps that many highest dice of a single term; the label
/// goes into the record's provenance so the UI can say what the roll was for. It carries no modifiers. Writes nothing.
/// </summary>
public sealed record RollDiceCommand(string Formula, int? KeepHighest = null, string? Label = null)
{
    public const int MaxLabelLength = 80;
}

public sealed partial class TomeStackApp
{
    public RollRecord RollDice(RollDiceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var formula = command.Formula?.Trim() ?? "";
        var label = string.IsNullOrWhiteSpace(command.Label) ? formula : command.Label.Trim();
        if (label.Length > RollDiceCommand.MaxLabelLength)
            throw new AppValidationException([new("dice.label-too-long", $"A roll label is at most {RollDiceCommand.MaxLabelLength} characters.")]);
        var request = new RollRequest(formula, Provenance: new RollProvenance("dice", label), KeepHighest: command.KeepHighest);
        if (!DiceRoller.TryRoll(request, Random, out var record, out var error))
            throw new AppValidationException([new(error!.Code, error.Message)]);
        return record!;
    }
}
