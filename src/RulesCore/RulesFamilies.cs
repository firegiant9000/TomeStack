namespace TomeStack.RulesCore;

/// <summary>
/// Rules-family identifiers. SRD 5.1 (2014 rules) and SRD 5.2.1 (2024 rules) are distinct
/// families; content declares compatibility explicitly and is never matched by name (SPEC S-02, C-01).
/// </summary>
public static class RulesFamilies
{
    public const string Srd51 = "srd-5.1";
    public const string Srd521 = "srd-5.2.1";

    /// <summary>
    /// The Multiclass Spellcaster table (spell slots of levels 1–9 at caster levels 1–20). The same numbers in SRD 5.1
    /// (p. 58) and SRD 5.2.1 (p. 26); each was parsed twice from its PDF and matches the SRD Wizard's table
    /// (<c>docs/licensing/srd-pack-review.md</c>). Game numbers, no rules text.
    /// </summary>
    private static readonly IReadOnlyList<IReadOnlyList<int>> SrdMulticlassSpellSlots =
    [
        [2], [3], [4, 2], [4, 3], [4, 3, 2], [4, 3, 3], [4, 3, 3, 1], [4, 3, 3, 2], [4, 3, 3, 3, 1], [4, 3, 3, 3, 2],
        [4, 3, 3, 3, 2, 1], [4, 3, 3, 3, 2, 1], [4, 3, 3, 3, 2, 1, 1], [4, 3, 3, 3, 2, 1, 1], [4, 3, 3, 3, 2, 1, 1, 1],
        [4, 3, 3, 3, 2, 1, 1, 1], [4, 3, 3, 3, 2, 1, 1, 1, 1], [4, 3, 3, 3, 3, 1, 1, 1, 1], [4, 3, 3, 3, 3, 2, 1, 1, 1],
        [4, 3, 3, 3, 3, 2, 2, 1, 1],
    ];

    public static IReadOnlyList<RulesFamilyPolicy> All { get; } =
    [
        new(Srd51, "SRD 5.1 (2014 rules)", AbilityIncreaseSource: ContentKind.Species, BackgroundGrantsFeat: false, LongRestExhaustionNeedsFoodAndDrink: true,
            LongRestHitDice: HitDiceRecovery.HalfTotal, HitDieHealingMinimum: 0, ShortRestNeedsOneHitPoint: false,
            HalfCasterLevels: CasterLevelRounding.Down, ThirdCasterLevels: CasterLevelRounding.Down, MulticlassSpellSlots: SrdMulticlassSpellSlots,
            UntrainedShieldGivesArmorClass: true),
        new(Srd521, "SRD 5.2.1 (2024 rules)", AbilityIncreaseSource: ContentKind.Background, BackgroundGrantsFeat: true, LongRestExhaustionNeedsFoodAndDrink: false,
            LongRestHitDice: HitDiceRecovery.All, HitDieHealingMinimum: 1, ShortRestNeedsOneHitPoint: true,
            HalfCasterLevels: CasterLevelRounding.Up, ThirdCasterLevels: CasterLevelRounding.Down, MulticlassSpellSlots: SrdMulticlassSpellSlots,
            UntrainedShieldGivesArmorClass: false, ConcentrationSaveMaximumDc: 30),
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
/// <param name="LongRestHitDice">
/// How many spent hit dice a long rest gives back: up to half the character's total number of hit dice, at least one,
/// under 2014 rules (SRD 5.1 p. 87); all of them under 2024 rules (SRD 5.2.1 p. 185, "Regain All HP").
/// </param>
/// <param name="HitDieHealingMinimum">
/// The fewest hit points one spent hit die restores (roll + Constitution modifier): 1 under 2024 rules (SRD 5.2.1 p. 187,
/// "minimum of 1 Hit Point"). SRD 5.1 (p. 87) states no minimum; TomeStack uses 0, so a hit die never takes hit points away.
/// </param>
/// <param name="ShortRestNeedsOneHitPoint">
/// Whether a short rest needs at least 1 hit point to start: yes under 2024 rules (SRD 5.2.1 p. 187), not stated under 2014
/// rules (SRD 5.1 p. 87). A long rest needs 1 hit point under both.
/// </param>
/// <param name="HalfCasterLevels">
/// How a half caster's class levels count toward the Multiclass Spellcaster table (<see cref="MulticlassCaster.Half"/>):
/// half, rounded down under 2014 rules (SRD 5.1 p. 58), rounded up under 2024 rules (SRD 5.2.1 p. 25).
/// </param>
/// <param name="ThirdCasterLevels">
/// How a third caster's class levels count (<see cref="MulticlassCaster.Third"/>). Neither SRD has a third caster, so this
/// is TomeStack's choice for homebrew: rounded down under both.
/// </param>
/// <param name="MulticlassSpellSlots">
/// The Multiclass Spellcaster table: 20 rows (caster levels 1–20), each the slots of spell levels 1, 2, … (trailing zeros
/// omitted). Used once a character has ordinary spell slots from two or more casters.
/// </param>
/// <param name="UntrainedShieldGivesArmorClass">
/// Whether a shield adds to Armor Class when the character lacks proficiency (training) with shields. Yes under 2014 rules:
/// SRD 5.1 (p. 62) gives untrained armor only disadvantage and no spellcasting. No under 2024 rules: "You gain the Armor
/// Class benefit of a Shield only if you have training with it" (SRD 5.2.1 p. 92). Checked only for a character whose
/// content records armor training at all (M2.2); older classes record none, and nothing changes for them.
/// </param>
/// <param name="ConcentrationSaveMaximumDc">
/// The highest DC of the Constitution saving throw that damage asks of a concentrating character: 30 under 2024 rules
/// (SRD 5.2.1, Concentration: "up to a maximum DC of 30"); none under 2014 rules (SRD 5.1 sets no ceiling).
/// </param>
public sealed record RulesFamilyPolicy(
    string Id,
    string DisplayName,
    ContentKind AbilityIncreaseSource,
    bool BackgroundGrantsFeat,
    bool LongRestExhaustionNeedsFoodAndDrink,
    HitDiceRecovery LongRestHitDice,
    int HitDieHealingMinimum,
    bool ShortRestNeedsOneHitPoint,
    CasterLevelRounding HalfCasterLevels,
    CasterLevelRounding ThirdCasterLevels,
    IReadOnlyList<IReadOnlyList<int>> MulticlassSpellSlots,
    bool UntrainedShieldGivesArmorClass,
    int? ConcentrationSaveMaximumDc = null);

/// <summary>How many spent hit dice a long rest gives back (<see cref="RulesFamilyPolicy.LongRestHitDice"/>).</summary>
public enum HitDiceRecovery { HalfTotal, All }

/// <summary>How a fraction of class levels rounds for the Multiclass Spellcaster table.</summary>
public enum CasterLevelRounding { Down, Up }
