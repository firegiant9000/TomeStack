using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// What to roll: a content roll effect (<paramref name="Content"/> and <paramref name="EffectId"/>), or a sheet field as
/// a d20 test (<paramref name="Field"/>: an ability modifier, saving throw, skill or initiative).
/// </summary>
public sealed record RollCommand(
    Guid CharacterId,
    ContentReference? Content = null,
    string? EffectId = null,
    string? Field = null,
    RollMode Mode = RollMode.Normal,
    bool Critical = false);

public sealed partial class TomeStackApp
{
    /// <summary>Dice for play: the OS CSPRNG. Tests replace it with a seeded source.</summary>
    internal IRandomSource Random { get; set; } = SystemRandomSource.Instance;

    /// <summary>
    /// SPEC C-04, M1 item 7: rolls and returns a <see cref="RollRecord"/> with its formula, dice, modifiers and
    /// provenance. It **never** changes the character: nothing is saved, and a roll linked to a resource only names
    /// that resource (spending is a separate, confirmed command, M2; ARCHITECTURE "commands vs calculation").
    /// </summary>
    public RollRecord Roll(RollCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var character = _store.FindCharacter(command.CharacterId)
            ?? throw new AppValidationException([new("character.not-found", $"Character {command.CharacterId} does not exist.")]);
        var sheet = CharacterCalculator.Calculate(character, _store);

        RollRequest request;
        if (command.Content is { } reference)
        {
            if (command.Field is not null)
                throw new AppValidationException([new("roll.ambiguous", "Roll either a content effect or a field, not both.")]);
            if (sheet.Active?.Contains(reference) != true)
                throw new AppValidationException([new("roll.content-inactive", $"Revision {reference.RevisionId} does not apply to this character, so its rolls are not available.", reference)]);
            var revision = _store.FindRevision(reference)!;
            var effect = revision.Effects.OfType<RollEffect>().FirstOrDefault(e => e.Id == command.EffectId)
                ?? throw new AppValidationException([new("roll.effect-not-found", $"'{revision.Name}' has no roll effect '{command.EffectId}'.", reference, command.EffectId)]);
            request = DiceRoller.FromEffect(effect, revision, _store.FindSource(revision.Provenance.SourceId), command.Mode, command.Critical);
        }
        else if (command.Field is { } field)
        {
            if (!IsD20Field(field))
                throw new AppValidationException([new("roll.field-not-rollable", $"'{field}' is not an ability check, saving throw, skill or initiative.")]);
            var value = sheet.Field(field);
            // The modifier cites the step that set the displayed value: the override if there is one, else the last step.
            var origin = value.Trace.Count > 0 ? value.Trace[^1].Origin : null;
            request = new RollRequest("1d20", command.Mode, command.Critical, [new RollModifier(value.Label, value.Value, origin)], new RollProvenance(field, $"{value.Label} (d20 test)"));
        }
        else
        {
            throw new AppValidationException([new("roll.target-required", "Name a content roll effect or a field to roll.")]);
        }

        if (!DiceRoller.TryRoll(request, Random, out var record, out var error))
            throw new AppValidationException([new(error!.Code, error.Message, command.Content, command.EffectId)]);
        return record!;
    }

    private static bool IsD20Field(string field) =>
        CharacterCalculator.IsField(field)
        && (field == FieldIds.Initiative
            || field.StartsWith("save.", StringComparison.Ordinal)
            || field.StartsWith("skill.", StringComparison.Ordinal)
            || (field.StartsWith("ability.", StringComparison.Ordinal) && field.EndsWith(".mod", StringComparison.Ordinal)));
}
