namespace TomeStack.RulesCore;

/// <summary>
/// Content schema v11 (D43): one reason a d20 roll has advantage or disadvantage. <paramref name="Source"/> is the content's name.
/// A reason with <paramref name="Against"/> (condition keys) applies only against such a creature, which the player confirms, so it
/// is listed and never changes <see cref="RollModeSummary.Automatic"/>.
/// </summary>
public sealed record RollModeReason(
    RollMode Mode,
    string Source,
    ContentReference Content,
    IReadOnlyList<string>? Against,
    [property: System.Text.Json.Serialization.JsonIgnore] AutomationStatus Automation = AutomationStatus.Automatic,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Prevent = false)
{
    /// <summary>True when the reason feeds <see cref="RollModeSummary.Automatic"/>: an automatic effect that needs no confirmation (no <c>against</c>).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("automatic")]
    public bool IsAutomatic => Automation == AutomationStatus.Automatic && Against is not { Count: > 0 };
}

/// <summary>The roll mode the sheet calculates for a field, and every reason listed for it (sheet content order).</summary>
public sealed record RollModeSummary(RollMode Automatic, IReadOnlyList<RollModeReason> Reasons);

/// <summary>Content v12 (G9): one roll floor that reaches a check field: a d20 below <paramref name="Face"/> counts as it, or a total below <paramref name="Total"/> counts as it (one of the two is set).</summary>
public sealed record RollFloorReason(string Source, ContentReference Reference, int? Face, int? Total);

/// <summary>Content v12 (G9): the roll floor of a check field: the highest face and the highest total (each null when no reason gives one), and every reason (sheet content order).</summary>
public sealed record RollFloorSummary(int? Face, int? Total, IReadOnlyList<RollFloorReason> Reasons);

/// <summary>Content v12 (G7): an <c>attack</c> roll mode with the attack tags it reaches and the ability the attack must use (null: any).</summary>
public sealed record AttackModeValue(RollModeReason Reason, IReadOnlyList<string> Tags, Ability? Ability);

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

    /// <summary>
    /// Content v12 (G7): the mode of attack rolls against the character. An automatic <c>prevent</c> reason removes every mode it names
    /// (it contributes no mode itself); the rest combine as <see cref="Combine"/>.
    /// </summary>
    public static RollMode CombineAgainst(IEnumerable<RollModeReason> reasons)
    {
        var list = reasons.Where(r => r.IsAutomatic).ToList();
        var prevented = list.Where(r => r.Prevent).Select(r => r.Mode).ToHashSet();
        return Combine(list.Where(r => !r.Prevent && !prevented.Contains(r.Mode)).Select(r => r.Mode));
    }

    /// <summary>
    /// Content v12 (G7): the attack-roll modes that reach one attack. A mode matches when its tags hold <c>all</c> or any of
    /// <paramref name="attackTags"/>, and it names no ability or the attack's. Null when none match.
    /// </summary>
    public static RollModeSummary? ForAttack(IReadOnlyList<AttackModeValue>? modes, IReadOnlyList<string> attackTags, Ability ability)
    {
        var matching = (modes ?? [])
            .Where(m => (m.Ability is null || m.Ability == ability) && (m.Tags.Contains(AttackRiderEffect.AllTag) || m.Tags.Any(attackTags.Contains)))
            .Select(m => m.Reason)
            .ToList();
        return matching.Count == 0 ? null : new RollModeSummary(Combine(matching.Where(r => r.IsAutomatic).Select(r => r.Mode)), matching);
    }
}
