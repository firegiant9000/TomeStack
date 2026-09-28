using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>
/// SPEC C-05, character schema v4: what changes during play. Never derived and never changed by calculation; only an
/// explicit, confirmed command writes it (ARCHITECTURE "commands vs calculation"). The defaults mean "fresh": at the hit
/// point maximum, no temporary hit points, nothing spent, no conditions.
/// </summary>
public sealed record PlayState
{
    public const int MaxHitPoints = 10_000;
    public const int MaxExhaustion = 6;

    /// <summary>Current hit points; <c>null</c> means at the maximum, so a character keeps full hit points when it levels up.</summary>
    public int? CurrentHitPoints { get; init; }

    public int TemporaryHitPoints { get; init; }

    /// <summary>Spent uses per resource, keyed by content id (not revision), so an update to a new revision keeps them.</summary>
    public IReadOnlyList<ResourceUse> Resources { get; init; } = [];

    /// <summary>Condition keys from <see cref="ConditionKeys.All"/>, without exhaustion (see <see cref="Exhaustion"/>).</summary>
    public IReadOnlyList<string> Conditions { get; init; } = [];

    /// <summary>Exhaustion level, 0–6 in both SRDs.</summary>
    public int Exhaustion { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    public int SpentOf(Guid contentId, string resourceId) =>
        Resources.LastOrDefault(r => r.ContentId == contentId && r.ResourceId == resourceId)?.Spent ?? 0;

    /// <summary>The same state with <paramref name="spent"/> recorded for one resource (0 removes the entry).</summary>
    public PlayState WithSpent(Guid contentId, string resourceId, int spent) => this with
    {
        Resources =
        [
            .. Resources.Where(r => !(r.ContentId == contentId && r.ResourceId == resourceId)),
            .. spent > 0 ? [new ResourceUse(contentId, resourceId, spent)] : Array.Empty<ResourceUse>(),
        ],
    };

    public IEnumerable<Diagnostic> Validate()
    {
        if (CurrentHitPoints is < 0 or > MaxHitPoints)
            yield return new("play.hit-points-out-of-range", $"Current hit points {CurrentHitPoints} must be between 0 and {MaxHitPoints}.");
        if (TemporaryHitPoints is < 0 or > MaxHitPoints)
            yield return new("play.hit-points-out-of-range", $"Temporary hit points {TemporaryHitPoints} must be between 0 and {MaxHitPoints}.");
        if (Exhaustion is < 0 or > MaxExhaustion)
            yield return new("play.exhaustion-out-of-range", $"Exhaustion level {Exhaustion} must be between 0 and {MaxExhaustion}.");
        foreach (var use in Resources.Where(r => r.Spent is < 0 or > MaxHitPoints || string.IsNullOrWhiteSpace(r.ResourceId)))
            yield return new("play.resource-invalid", $"Spent uses of resource '{use.ResourceId}' must be between 0 and {MaxHitPoints}, with a resource id.");
        foreach (var duplicate in Resources.GroupBy(r => (r.ContentId, r.ResourceId)).Where(g => g.Count() > 1))
            yield return new("play.resource-duplicate", $"Resource '{duplicate.Key.ResourceId}' is recorded more than once.");
        foreach (var condition in Conditions.Where(c => !ConditionKeys.All.Contains(c)))
            yield return new("play.condition-unknown", $"'{condition}' is not a condition TomeStack knows.");
        if (Conditions.Distinct().Count() != Conditions.Count)
            yield return new("play.condition-duplicate", "A condition is recorded more than once.");
    }
}

/// <summary>How many uses of one resource are spent. <paramref name="ContentId"/> is the content that defines it.</summary>
public sealed record ResourceUse(Guid ContentId, string ResourceId, int Spent);

/// <summary>
/// The conditions both SRDs define (exhaustion is a level, <see cref="PlayState.Exhaustion"/>). Keys only: the rules text
/// of each condition is not bundled, and conditions do not change calculated fields yet (they are reminders).
/// </summary>
public static class ConditionKeys
{
    public static IReadOnlyList<string> All { get; } =
    [
        "blinded", "charmed", "deafened", "frightened", "grappled", "incapacitated", "invisible", "paralyzed",
        "petrified", "poisoned", "prone", "restrained", "stunned", "unconscious",
    ];
}
