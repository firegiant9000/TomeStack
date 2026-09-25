namespace TomeStack.RulesCore;

/// <summary>
/// Rules-family identifiers. SRD 5.1 (2014 rules) and SRD 5.2.1 (2024 rules) are distinct
/// families; content declares compatibility explicitly and is never matched by name (SPEC S-02, C-01).
/// </summary>
public static class RulesFamilies
{
    public const string Srd51 = "srd-5.1";
    public const string Srd521 = "srd-5.2.1";

    public static IReadOnlyList<RulesFamilyPolicy> All { get; } =
    [
        new(Srd51, "SRD 5.1 (2014 rules)", AbilityIncreaseSource: ContentKind.Species),
        new(Srd521, "SRD 5.2.1 (2024 rules)", AbilityIncreaseSource: ContentKind.Background),
    ];

    public static bool IsKnown(string? id) => All.Any(p => p.Id == id);

    public static RulesFamilyPolicy Get(string id) =>
        All.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException($"Unknown rules family '{id}'. Expected one of: {string.Join(", ", All.Select(p => p.Id))}.", nameof(id));
}

/// <summary>
/// Explicitly encoded differences between rules families. Each difference is a named field so
/// that it can be fixture-tested side by side rather than inferred.
/// </summary>
/// <param name="AbilityIncreaseSource">
/// Which content kind may grant ability score increases: species under 2014 rules, background under 2024 rules.
/// </param>
public sealed record RulesFamilyPolicy(string Id, string DisplayName, ContentKind AbilityIncreaseSource);
