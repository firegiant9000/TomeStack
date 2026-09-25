using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>
/// Stored character choices and play state. Derived values are never stored here; they are
/// recalculated from <see cref="BaseAbilities"/>, <see cref="Pins"/> and <see cref="Overrides"/>.
/// </summary>
public sealed record Character
{
    public const int CurrentSchemaVersion = 1;

    public required Guid Id { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Name { get; init; }
    public required string RulesFamily { get; init; }
    public Guid? CampaignId { get; init; }
    public required AbilityScores BaseAbilities { get; init; }
    public IReadOnlyList<ContentReference> Pins { get; init; } = [];
    public IReadOnlyList<FieldOverride> Overrides { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    public IReadOnlyList<Diagnostic> Validate()
    {
        var problems = new List<Diagnostic>();
        if (string.IsNullOrWhiteSpace(Name))
            problems.Add(new("character.name-required", "Character name is required."));
        if (!RulesFamilies.IsKnown(RulesFamily))
            problems.Add(new("character.rules-family-unknown", $"Rules family '{RulesFamily}' is not supported."));
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var score = BaseAbilities.Get(ability);
            if (score is < 1 or > 30)
                problems.Add(new("character.ability-out-of-range", $"{ability} score {score} must be between 1 and 30."));
        }
        return problems;
    }
}

public sealed record AbilityScores(int Str, int Dex, int Con, int Int, int Wis, int Cha)
{
    public int Get(Ability ability) => ability switch
    {
        Ability.Str => Str,
        Ability.Dex => Dex,
        Ability.Con => Con,
        Ability.Int => Int,
        Ability.Wis => Wis,
        Ability.Cha => Cha,
        _ => throw new ArgumentOutOfRangeException(nameof(ability)),
    };
}

/// <summary>SPEC C-06. A labeled user override applied as the final display layer.</summary>
public sealed record FieldOverride(string Field, int Value, string? Reason = null);
