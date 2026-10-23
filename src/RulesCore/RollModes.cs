namespace TomeStack.RulesCore;

/// <summary>
/// Content schema v11 (D43): one reason a d20 roll has advantage or disadvantage. <paramref name="Source"/> is the content's name.
/// A reason with <paramref name="Against"/> (condition keys) applies only against such a creature, which the player confirms, so it
/// is listed and never changes <see cref="RollModeSummary.Automatic"/>.
/// </summary>
public sealed record RollModeReason(RollMode Mode, string Source, ContentReference Content, IReadOnlyList<string>? Against);

/// <summary>The roll mode the sheet calculates for a field, and every reason listed for it (sheet content order).</summary>
public sealed record RollModeSummary(RollMode Automatic, IReadOnlyList<RollModeReason> Reasons);

public static class RollModes
{
    /// <summary>
    /// The 5e rule: any advantage with any disadvantage cancel to normal however many there are of each; only one kind present gives
    /// that kind; none (or only normal) gives normal.
    /// </summary>
    public static RollMode Combine(IEnumerable<RollMode> modes)
    {
        var advantage = false;
        var disadvantage = false;
        foreach (var mode in modes)
        {
            advantage |= mode == RollMode.Advantage;
            disadvantage |= mode == RollMode.Disadvantage;
        }
        return advantage == disadvantage ? RollMode.Normal : advantage ? RollMode.Advantage : RollMode.Disadvantage;
    }
}
