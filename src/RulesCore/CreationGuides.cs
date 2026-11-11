using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>Content schema v13 (ADR-024): names one piece of content by its id (any revision of it), as a guide line, a pack line or a guide's subject.</summary>
public sealed record GuideTarget(Guid ContentId)
{
    /// <summary>Unknown members, kept so a later build's additions round-trip.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>
/// Content schema v13 (ADR-024): the effect of a <see cref="ContentKind.CreationGuide"/> revision that names the class or
/// background the guide advises on. Typed only in a v13 revision: below v13 it stays an <see cref="UnknownEffect"/>, byte for byte.
/// </summary>
public sealed record GuideForEffect : Effect
{
    public const string TypeName = "guideFor";

    public const int SchemaVersion = ContentRevision.CreationGuideSchemaVersion;

    public override string Type => TypeName;

    [property: JsonRequired]
    public required GuideTarget Content { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<GuideForEffect>(unknown);
}

/// <summary>Content schema v13 (ADR-024): the abilities a guide recommends putting the highest scores in, by ability key (<c>str</c> to <c>cha</c>). Typed only in a v13 revision.</summary>
public sealed record PrimaryAbilitiesEffect : Effect
{
    public const string TypeName = "primaryAbilities";

    public const int SchemaVersion = ContentRevision.CreationGuideSchemaVersion;

    public override string Type => TypeName;

    [property: JsonRequired]
    public required IReadOnlyList<string> Abilities { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<PrimaryAbilitiesEffect>(unknown);
}

/// <summary>Content schema v13 (ADR-024): a guide's starting equipment, as picks that each offer options. Typed only in a v13 revision.</summary>
public sealed record StartingEquipmentEffect : Effect
{
    public const string TypeName = "startingEquipment";

    public const int SchemaVersion = ContentRevision.CreationGuideSchemaVersion;

    public override string Type => TypeName;

    [property: JsonRequired]
    public required IReadOnlyList<KitPick> Picks { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<StartingEquipmentEffect>(unknown);
}

/// <summary>One choice in a kit: pick one of its <see cref="Options"/>.</summary>
public sealed record KitPick(string Id, string? Label, IReadOnlyList<KitOption> Options)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>One option of a <see cref="KitPick"/>: its <see cref="Lines"/>, offered only to a character with <see cref="RequiresProficiency"/> when that is set.</summary>
public sealed record KitOption(string Id, IReadOnlyList<KitLine> Lines, string? RequiresProficiency = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>
/// One line of a kit option; exactly one of <see cref="Item"/> (with an optional <see cref="Quantity"/>), <see cref="AnyOf"/> (a
/// proficiency key such as <c>weapon.simple</c>), <see cref="Coins"/> or <see cref="Pack"/> is set (validated by a later task).
/// </summary>
public sealed record KitLine(GuideTarget? Item = null, int? Quantity = null, string? AnyOf = null,
    [property: JsonConverter(typeof(SparseCurrencyConverter))] Currency? Coins = null, GuideTarget? Pack = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Content schema v13 (ADR-024): one line of a pack, <see cref="ItemEffect.Contains"/>.</summary>
public sealed record PackLine(GuideTarget Item, int Quantity = 1)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>
/// Reads and writes coins as the members present only (<c>{"gp":50}</c>), so a kit line round-trips as written. The default
/// <see cref="Currency"/> form always writes all five denominations.
/// </summary>
internal sealed class SparseCurrencyConverter : JsonConverter<Currency>
{
    private static readonly string[] Denominations = ["cp", "sp", "ep", "gp", "pp"];

    public override Currency? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Coins must be an object.");
        var counts = new int[5];
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var member = reader.GetString();
            var index = Array.FindIndex(Denominations, d => string.Equals(d, member, StringComparison.OrdinalIgnoreCase));
            reader.Read();
            if (index < 0)
                throw new JsonException("Unknown coin kind.");
            counts[index] = reader.GetInt32();
        }
        return new Currency(counts[0], counts[1], counts[2], counts[3], counts[4]);
    }

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        int[] counts = [value.Cp, value.Sp, value.Ep, value.Gp, value.Pp];
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] != 0)
                writer.WriteNumber(Denominations[i], counts[i]);
        }
        writer.WriteEndObject();
    }
}
