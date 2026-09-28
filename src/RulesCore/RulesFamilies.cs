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
        new(Srd51, "SRD 5.1 (2014 rules)", AbilityIncreaseSource: ContentKind.Species, BackgroundGrantsFeat: false, LongRestExhaustionNeedsFoodAndDrink: true),
        new(Srd521, "SRD 5.2.1 (2024 rules)", AbilityIncreaseSource: ContentKind.Background, BackgroundGrantsFeat: true, LongRestExhaustionNeedsFoodAndDrink: false),
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
/// Which origin content kind may grant ability score increases: species under 2014 rules, background under 2024 rules.
/// Applies to every modifier operation on an ability score, so origin content cannot bypass it with <c>set</c> or <c>replace</c>.
/// </param>
/// <param name="BackgroundGrantsFeat">
/// Whether a background may grant a feat (a <c>grant</c> effect of kind <c>content</c>): no under 2014 rules, yes under 2024 rules.
/// </param>
/// <param name="LongRestExhaustionNeedsFoodAndDrink">
/// Whether a long rest removes an exhaustion level only if the character has had food and drink: yes under 2014 rules
/// (SRD 5.1, exhaustion), no under 2024 rules (SRD 5.2.1 removes one level per long rest). The rest preview proposes the
/// reduction either way and, where this is true, says it depends on food and drink so the player can untick it.
/// </param>
public sealed record RulesFamilyPolicy(
    string Id, string DisplayName, ContentKind AbilityIncreaseSource, bool BackgroundGrantsFeat, bool LongRestExhaustionNeedsFoodAndDrink);
