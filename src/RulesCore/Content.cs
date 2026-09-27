using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

public enum Ability { Str, Dex, Con, Int, Wis, Cha }

public enum ContentKind { Species, Background, Class, Subclass, Feature, Feat, Spell, Item }

/// <summary>Only <see cref="Published"/> revisions can affect calculations (SPEC I-01, I-06).</summary>
public enum RevisionStatus { Draft, Published }

/// <summary>SPEC I-05. Unhandled mechanics keep their text and are marked <see cref="Reference"/>.</summary>
public enum AutomationStatus { Automatic, Assisted, Reference }

/// <summary>SPEC S-01. A source of rules content and its rights metadata.</summary>
public sealed record SourceRecord
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Publisher { get; init; }
    public required IReadOnlyList<string> RulesFamilies { get; init; }
    public required string EditionVersion { get; init; }
    public required string License { get; init; }
    public required bool Redistributable { get; init; }
    public string? Attribution { get; init; }
    public DateTimeOffset? ImportedAt { get; init; }
    public string? Sha256 { get; init; }
    public string? PdfRef { get; init; }
}

public sealed record PageRef(int Start, int? End = null)
{
    public override string ToString() => End is { } end && end != Start ? $"pp. {Start}-{end}" : $"p. {Start}";
}

public sealed record Provenance(Guid SourceId, PageRef? Page = null);

/// <summary>An exact pin to one immutable content revision (ARCHITECTURE: ContentReference).</summary>
public sealed record ContentReference(Guid ContentId, Guid RevisionId);

/// <summary>
/// One immutable revision of a content entity. Identity is <see cref="ContentId"/>/<see cref="RevisionId"/>,
/// never <see cref="Name"/>. Unknown JSON fields round-trip through <see cref="Extensions"/>.
/// </summary>
public sealed record ContentRevision
{
    public const int CurrentSchemaVersion = 1;

    public required Guid ContentId { get; init; }
    public required Guid RevisionId { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required ContentKind Kind { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> RulesFamilies { get; init; }
    public required Provenance Provenance { get; init; }
    public required RevisionStatus Status { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<Effect> Effects { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    [JsonIgnore]
    public ContentReference Reference => new(ContentId, RevisionId);
}

/// <summary>
/// M0 declarative effect. <see cref="Type"/> is an open string so unknown effects survive
/// round trips and degrade to reference-only diagnostics. M1 replaces this with the typed effect AST (ADR-003).
/// </summary>
public sealed record Effect
{
    public const string AbilityScoreIncrease = "abilityScoreIncrease";
    public const string InitiativeBonus = "initiativeBonus";

    public required string Id { get; init; }
    public required string Type { get; init; }
    public Ability? Ability { get; init; }
    public int? Amount { get; init; }
    public AutomationStatus Automation { get; init; } = AutomationStatus.Automatic;
    public string? Text { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
