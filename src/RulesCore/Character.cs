using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>
/// Stored character choices and play state. Derived values are never stored here; they are
/// recalculated from <see cref="BaseAbilities"/>, <see cref="Pins"/> and <see cref="Overrides"/>.
/// </summary>
public sealed record Character : IJsonOnDeserialized
{
    /// <summary>v2 adds <see cref="Level"/>. v1 is upcast on read (level 1).</summary>
    public const int CurrentSchemaVersion = 2;

    public const int MinLevel = 1;
    public const int MaxLevel = 20;

    private int _schemaVersion = CurrentSchemaVersion;

    public required Guid Id { get; init; }

    public int SchemaVersion { get => _schemaVersion; init => _schemaVersion = value; }

    public required string Name { get; init; }
    public required string RulesFamily { get; init; }

    /// <summary>Total character level (1–20). Drives the proficiency bonus.</summary>
    public int Level { get; init; } = MinLevel;
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
        if (SchemaVersion is < 1 or > CurrentSchemaVersion)
            problems.Add(new("character.schema-unsupported", $"Character data uses schema v{SchemaVersion}; this version of TomeStack supports v1 to v{CurrentSchemaVersion}. Update TomeStack to open it."));
        if (string.IsNullOrWhiteSpace(Name))
            problems.Add(new("character.name-required", "Character name is required."));
        if (!RulesFamilies.IsKnown(RulesFamily))
            problems.Add(new("character.rules-family-unknown", $"Rules family '{RulesFamily}' is not supported."));
        if (Level is < MinLevel or > MaxLevel)
            problems.Add(new("character.level-out-of-range", $"Level {Level} must be between {MinLevel} and {MaxLevel}."));
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var score = BaseAbilities.Get(ability);
            if (score is < 1 or > 30)
                problems.Add(new("character.ability-out-of-range", $"{ability} score {score} must be between 1 and 30."));
        }
        return problems;
    }

    /// <summary>v1 has no level; the default (level 1) is exactly its meaning.</summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (_schemaVersion is >= 1 and < CurrentSchemaVersion)
            _schemaVersion = CurrentSchemaVersion;
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
