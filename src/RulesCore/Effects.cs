using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>Derived-field identifiers that effects can target and formulas can read (ADR-003).</summary>
public static class FieldIds
{
    public const string Initiative = "initiative";
    public const string ProficiencyBonus = "proficiencyBonus";

    public static string Score(Ability ability) => $"ability.{Key(ability)}.score";

    public static string Modifier(Ability ability) => $"ability.{Key(ability)}.mod";

    public static string Save(Ability ability) => $"save.{Key(ability)}";

    public static string Skill(string skill) => $"skill.{skill}";

    public static string Key(Ability ability) => ability.ToString().ToLowerInvariant();
}

/// <summary>How a <see cref="ModifierEffect"/> combines with the field (ADR-003 "Stacking and order").</summary>
public enum ModifierOperation
{
    /// <summary>Adds the value. Subject to <see cref="StackingRule"/>.</summary>
    Bonus,

    /// <summary>The field becomes the value, after bonuses. Among several sets, the highest wins.</summary>
    Set,

    /// <summary>Replaces the field's base value before bonuses. Among several replacements, the highest wins.</summary>
    Replace,
}

/// <summary>Bonuses stack unless they share a group, in which case only the highest in the group applies.</summary>
public enum StackingRule { Stack, HighestInGroup }

/// <summary>When an effect applies. Derived fields use only <see cref="Always"/>; the rest belong to commands and rolls.</summary>
public enum EffectTiming { Always, WhileActive, OnRoll, OnShortRest, OnLongRest }

public enum GrantKind { Proficiency, Expertise, Content }

public enum RestPeriod { ShortRest, LongRest }

/// <summary>
/// A declarative, typed effect (ADR-003). Effects never contain code. Values are bounded formulas held as source
/// text and parsed by the rules core. Unknown fields round-trip through <see cref="Extensions"/>, and unknown effect
/// types round-trip unchanged as <see cref="UnknownEffect"/>.
/// </summary>
[JsonConverter(typeof(EffectJsonConverter))]
public abstract record Effect
{
    /// <summary>Discriminator; written first, followed by <see cref="Id"/>, by <see cref="EffectJsonConverter"/>.</summary>
    public abstract string Type { get; }

    public required string Id { get; init; }

    public AutomationStatus Automation { get; init; } = AutomationStatus.Automatic;

    public EffectTiming Timing { get; init; } = EffectTiming.Always;

    /// <summary>Original or summarized text, kept so unhandled mechanics stay readable (SPEC I-05).</summary>
    public string? Text { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>bonus / set / replace on one derived field.</summary>
public sealed record ModifierEffect : Effect
{
    public const string TypeName = "modifier";

    public override string Type => TypeName;

    public required ModifierOperation Operation { get; init; }

    /// <summary>A <see cref="FieldIds"/> value, e.g. <c>initiative</c> or <c>ability.dex.score</c>.</summary>
    public required string Target { get; init; }

    /// <summary>Bounded formula source, e.g. <c>2</c> or <c>PB + CON.MOD</c>.</summary>
    public required string Value { get; init; }

    public StackingRule Stacking { get; init; } = StackingRule.Stack;

    /// <summary>Required with <see cref="StackingRule.HighestInGroup"/>.</summary>
    public string? StackGroup { get; init; }
}

/// <summary>Grants a proficiency or expertise in a field (<c>save.dex</c>, <c>skill.stealth</c>), or another content revision.</summary>
public sealed record GrantEffect : Effect
{
    public const string TypeName = "grant";

    public override string Type => TypeName;

    public required GrantKind Grant { get; init; }

    public string? Target { get; init; }

    public ContentReference? Content { get; init; }
}

/// <summary>A limited-use resource with a formula maximum (for example, uses per rest).</summary>
public sealed record ResourceEffect : Effect
{
    public const string TypeName = "resource";

    public override string Type => TypeName;

    public required string ResourceId { get; init; }

    public required string Label { get; init; }

    public required string Maximum { get; init; }
}

/// <summary>A choice the player makes (for example, pick one of these content revisions).</summary>
public sealed record ChoiceEffect : Effect
{
    public const string TypeName = "choice";

    public override string Type => TypeName;

    public required string ChoiceId { get; init; }

    public int Count { get; init; } = 1;

    public IReadOnlyList<ContentReference> Options { get; init; } = [];
}

/// <summary>A prerequisite or limitation, for example a minimum ability score.</summary>
public sealed record RestrictionEffect : Effect
{
    public const string TypeName = "restriction";

    public override string Type => TypeName;

    public required string Field { get; init; }

    public required int Minimum { get; init; }
}

/// <summary>Restores a resource on a rest. Previewed and confirmed by a command, never applied by calculation.</summary>
public sealed record RecoveryEffect : Effect
{
    public const string TypeName = "recovery";

    public override string Type => TypeName;

    public required string ResourceId { get; init; }

    public required RestPeriod On { get; init; }

    /// <summary>Formula, or <c>all</c>.</summary>
    public required string Amount { get; init; }
}

/// <summary>A roll the sheet offers. Rolling never consumes a resource by itself (ARCHITECTURE "commands vs calculation").</summary>
public sealed record RollEffect : Effect
{
    public const string TypeName = "roll";

    public override string Type => TypeName;

    public required string RollId { get; init; }

    public required string Label { get; init; }

    /// <summary>Dice expression, e.g. <c>1d8+2</c>.</summary>
    public required string Dice { get; init; }

    /// <summary>Optional resource the associated action spends; only an explicit action command spends it.</summary>
    public string? ResourceId { get; init; }
}

/// <summary>
/// An effect type this build does not know. The original JSON is kept byte-for-byte and written back unchanged, and
/// it is never automated.
/// </summary>
public sealed record UnknownEffect : Effect
{
    public override string Type => DeclaredType;

    /// <summary>The <c>type</c> string from the JSON (empty if absent).</summary>
    public string DeclaredType { get; private init; } = "";

    public required JsonElement Raw { get; init; }

    internal static UnknownEffect From(JsonElement raw)
    {
        var id = raw.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString()! : "";
        var type = raw.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
        var text = raw.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
        return new UnknownEffect { Id = id, DeclaredType = type, Raw = raw.Clone(), Automation = AutomationStatus.Reference, Text = text };
    }
}

/// <summary>
/// Reads the <c>type</c> discriminator. Maps the schemaVersion 1 types (<c>abilityScoreIncrease</c>, <c>initiativeBonus</c>)
/// to <see cref="ModifierEffect"/> (ADR-003 migration). Anything unknown or malformed becomes <see cref="UnknownEffect"/>
/// instead of failing the whole revision.
/// </summary>
public sealed class EffectJsonConverter : JsonConverter<Effect>
{
    public const string LegacyAbilityScoreIncrease = "abilityScoreIncrease";
    public const string LegacyInitiativeBonus = "initiativeBonus";

    public override Effect? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("An effect must be a JSON object.");
        var type = element.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        try
        {
            return type switch
            {
                ModifierEffect.TypeName => (Effect?)element.Deserialize<ModifierEffect>(options),
                GrantEffect.TypeName => element.Deserialize<GrantEffect>(options),
                ResourceEffect.TypeName => element.Deserialize<ResourceEffect>(options),
                ChoiceEffect.TypeName => element.Deserialize<ChoiceEffect>(options),
                RestrictionEffect.TypeName => element.Deserialize<RestrictionEffect>(options),
                RecoveryEffect.TypeName => element.Deserialize<RecoveryEffect>(options),
                RollEffect.TypeName => element.Deserialize<RollEffect>(options),
                LegacyAbilityScoreIncrease or LegacyInitiativeBonus => FromSchemaVersion1(element, type, options),
                _ => UnknownEffect.From(element),
            } ?? UnknownEffect.From(element);
        }
        catch (JsonException)
        {
            // A known type with a malformed body stays reference-only and visible rather than breaking the revision.
            return UnknownEffect.From(element);
        }
    }

    public override void Write(Utf8JsonWriter writer, Effect value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value is UnknownEffect unknown)
        {
            unknown.Raw.WriteTo(writer);
            return;
        }
        // Deterministic layout (revision hashes depend on it): type, id, then the rest in serializer order.
        var body = JsonSerializer.SerializeToElement(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        writer.WriteString("id", value.Id);
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name is not ("type" or "id"))
                property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    /// <summary>schemaVersion 1 <c>{ id, type, ability?, amount?, automation?, text? }</c> to a typed bonus.</summary>
    private static ModifierEffect FromSchemaVersion1(JsonElement element, string type, JsonSerializerOptions options)
    {
        var id = element.GetProperty("id").GetString() ?? throw new JsonException("Effect id is required.");
        var automation = element.TryGetProperty("automation", out var a)
            ? a.Deserialize<AutomationStatus>(options)
            : AutomationStatus.Automatic;
        var text = element.TryGetProperty("text", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
        var amount = element.TryGetProperty("amount", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetRawText() : "";
        var target = type == LegacyInitiativeBonus
            ? FieldIds.Initiative
            : element.TryGetProperty("ability", out var ab) && ab.ValueKind == JsonValueKind.String
                ? $"ability.{ab.GetString()}.score"
                : "";

        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is not ("id" or "type" or "ability" or "amount" or "automation" or "text"))
                extensions[property.Name] = property.Value.Clone();
        }

        return new ModifierEffect
        {
            Id = id,
            Operation = ModifierOperation.Bonus,
            Target = target,
            Value = amount,
            Automation = automation,
            Text = text,
            Extensions = extensions.Count > 0 ? extensions : null,
        };
    }
}
