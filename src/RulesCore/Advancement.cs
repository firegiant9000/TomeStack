namespace TomeStack.RulesCore;

/// <summary>
/// Wave 3 item 27 (R162, R177): experience points. Both bundled SRDs print the same table, so it is one shared rules constant, like
/// the proficiency bonus by total level, not a <see cref="RulesFamilyPolicy"/> field (a policy field encodes a difference; there is none).
/// It is display only: reaching a threshold never changes a character's level.
/// </summary>
public static class Advancement
{
    /// <summary>The experience points needed to reach each character level, indexed by level - 1 (level 1 needs 0, level 20 needs 355,000).</summary>
    public static readonly IReadOnlyList<int> XpThresholds =
    [
        0, 300, 900, 2_700, 6_500, 14_000, 23_000, 34_000, 48_000, 64_000,
        85_000, 100_000, 120_000, 140_000, 165_000, 195_000, 225_000, 265_000, 305_000, 355_000,
    ];

    /// <summary>The total needed for the level after <paramref name="level"/>; null below level 1 and at level 20 and above, where there is no next level.</summary>
    public static int? XpForNextLevel(int level) => level >= 1 && level < XpThresholds.Count ? XpThresholds[level] : null;
}
