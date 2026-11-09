using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <param name="Code"><c>ability.set-by-content</c>, <c>ability.cap-ambiguous</c>, <c>ability.out-of-range</c> or <c>ability.not-read</c>.</param>
/// <param name="Message">Names TomeStack content only, never a sheet value.</param>
public sealed record AbilityNote(Ability Ability, string Code, string Message);

/// <param name="ProposedBase">The base scores that reproduce the sheet's final scores where they can be solved.</param>
public sealed record AbilityPlan(AbilityScores ProposedBase, IReadOnlyList<AbilityNote> Notes);

/// <summary>
/// The ability back-solve (<c>features/ddb-pdf-import.md</c> "Ability scores"). The sheet shows final scores; TomeStack
/// stores the base, which absorbs everything TomeStack does not model. One preview at base 1 gives each score's trace:
/// a <c>replace</c> or <c>set</c> step means the base cannot be solved (the sheet's score is proposed and noted);
/// otherwise the <c>add</c> steps are summed into increases <c>P</c> and penalties <c>N</c> (refused modifiers are
/// <c>ignored</c> steps and do not count). With <c>T = S − N</c>: below 20 the base is <c>T − P</c>; at 20 it is
/// <c>20 − P</c>, the smallest base consistent with the cap (noted); above 20 plain increases add nothing, so it is <c>T</c>, unless a capped increase (v12) reproduces the sheet: then the bases
/// from <c>T − P</c> up to <c>T</c> are previewed and the first that gives the sheet's score is used (noted when the next base gives it too).
/// During those previews the other abilities stay at the probe base, so a formula that reads another ability can miss; then <c>T</c> stands.
/// The base is clamped to 1–30. The caller previews again with the result and shows any remaining difference.
/// </summary>
internal static class AbilitySolver
{
    public const int MinBase = 1;
    public const int MaxBase = 30;

    /// <summary>The base a score the sheet does not give keeps.</summary>
    public const int DefaultBase = 10;

    private static readonly AbilityScores Probe = new(MinBase, MinBase, MinBase, MinBase, MinBase, MinBase);

    private static readonly Dictionary<Ability, string> Names = new()
    {
        [Ability.Str] = "Strength", [Ability.Dex] = "Dexterity", [Ability.Con] = "Constitution",
        [Ability.Int] = "Intelligence", [Ability.Wis] = "Wisdom", [Ability.Cha] = "Charisma",
    };

    private static AbilityScores WithBase(Ability ability, int value) => ability switch
    {
        Ability.Str => Probe with { Str = value },
        Ability.Dex => Probe with { Dex = value },
        Ability.Con => Probe with { Con = value },
        Ability.Int => Probe with { Int = value },
        Ability.Wis => Probe with { Wis = value },
        _ => Probe with { Cha = value },
    };

    private static int Score(CharacterSheet sheet, Ability ability) => sheet.Field(FieldIds.Score(ability)).Value;

    public static AbilityPlan Solve(Func<AbilityScores, CharacterSheet> preview, IReadOnlyDictionary<Ability, int> sheetScores)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(sheetScores);
        var probe = preview(Probe);
        var notes = new List<AbilityNote>();
        var bases = new Dictionary<Ability, int>();
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var label = Names[ability];
            if (!sheetScores.TryGetValue(ability, out var score))
            {
                bases[ability] = DefaultBase;
                notes.Add(new(ability, "ability.not-read", $"The sheet's {label} was not read, so its base stays {DefaultBase}."));
                continue;
            }
            var field = FieldIds.Score(ability);
            var steps = probe.Field(field).Trace.Where(t => t.Field is null || t.Field == field).ToList();
            if (steps.FirstOrDefault(t => t.Operation is "replace" or "set") is { } fixedBy)
            {
                bases[ability] = Math.Clamp(score, MinBase, MaxBase);
                notes.Add(new(ability, "ability.set-by-content", $"{label} is set by {fixedBy.Origin.ContentName ?? "content"}, so its base cannot be solved; the sheet's score is proposed."));
                continue;
            }
            var adds = steps.Where(t => t.Operation == "add" && t.Amount is not null).Select(t => t.Amount!.Value).ToList();
            var increases = adds.Where(a => a > 0).Sum();
            var penalties = adds.Where(a => a < 0).Sum();
            var target = score - penalties;
            int solved;
            if (target < CharacterCalculator.AbilityScoreIncreaseCap)
                solved = target - increases;
            else if (target == CharacterCalculator.AbilityScoreIncreaseCap)
            {
                solved = target - increases;
                if (increases > 0)
                    notes.Add(new(ability, "ability.cap-ambiguous", $"{label} reaches the cap of {CharacterCalculator.AbilityScoreIncreaseCap}, so its base may be higher than proposed; the smallest base that fits is used."));
            }
            else
            {
                solved = target;
                if (increases > 0)
                {
                    // Capped increases (content v12) can lift a base above 20, and a cap may be only partly used. Scan the bases from
                    // target - increases up to target (at most increases + 2 previews) and take the first that reproduces the sheet.
                    var high = Math.Min(MaxBase, target);
                    var match = -1;
                    for (var candidate = Math.Max(MinBase, target - increases); candidate <= high; candidate++)
                    {
                        if (Score(preview(WithBase(ability, candidate)), ability) == score)
                        {
                            match = candidate;
                            break;
                        }
                    }
                    if (match >= 0)
                    {
                        solved = match;
                        if (match < target && match + 1 <= high && Score(preview(WithBase(ability, match + 1)), ability) == score)
                            notes.Add(new(ability, "ability.cap-ambiguous", $"{label} reaches a content maximum of {score}, so its base may be higher than proposed; the smallest base that fits is used."));
                    }
                }
            }
            if (solved is < MinBase or > MaxBase)
                notes.Add(new(ability, "ability.out-of-range", $"{label}'s solved base is outside {MinBase} to {MaxBase}, so it is clamped and the score will differ."));
            bases[ability] = Math.Clamp(solved, MinBase, MaxBase);
        }
        return new AbilityPlan(new AbilityScores(bases[Ability.Str], bases[Ability.Dex], bases[Ability.Con], bases[Ability.Int], bases[Ability.Wis], bases[Ability.Cha]), notes);
    }
}
