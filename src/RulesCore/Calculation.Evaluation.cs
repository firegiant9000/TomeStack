namespace TomeStack.RulesCore;

public static partial class CharacterCalculator
{
    // ---- evaluation -----------------------------------------------------------------------------------------

    private sealed record Step(string Field, string Operation, string Description, int? Amount, int Result, TraceOrigin Origin, IReadOnlyList<TraceInput>? Inputs = null);

    /// <summary>ADR-003 order: highest replace, then bonuses (stack / highest in group), then highest set.</summary>
    private static int ApplyModifiers(
        string field, int value, List<Modifier> modifiers, Character character, ClassLevelMap classLevels,
        Dictionary<string, int> values, string family, List<Step> steps, List<Diagnostic> warnings, HashSet<string> manual)
    {
        var evaluated = new List<(Modifier Modifier, int Amount, List<TraceInput> Inputs)>();
        var skipped = new List<Modifier>();
        foreach (var modifier in modifiers)
        {
            if (modifier.SkipReason is not null)
            {
                skipped.Add(modifier);
                continue;
            }
            var inputs = new List<TraceInput>();
            if (modifier.Formula.TryEvaluate(id => Resolve(id, modifier.Content, character, classLevels, values, inputs), out var amount, out var error))
                evaluated.Add((modifier, amount, inputs));
            else
            {
                warnings.Add(InvalidFormula(modifier.Content.Revision, modifier.Effect, error!));
                manual.Add(field);
            }
        }

        TraceOrigin Origin(Modifier m) => ContentOrigin(family, m.Content, m.Effect);
        string Name(Modifier m) => m.FollowedToggle is { } followed ? $"{Describe(m.Content)} while {followed} is on" : Describe(m.Content);
        IReadOnlyList<TraceInput>? Inputs(List<TraceInput> i) => i.Count > 0 ? i : null;

        var replacements = evaluated.Where(e => e.Modifier.Effect.Operation == ModifierOperation.Replace).ToList();
        if (replacements.Count > 0)
        {
            var best = replacements.MaxBy(r => r.Amount);
            value = best.Amount;
            steps.Add(new(field, "replace", $"Base replaced by {Name(best.Modifier)}", best.Amount, value, Origin(best.Modifier), Inputs(best.Inputs)));
            foreach (var other in replacements.Where(r => r != best))
                steps.Add(new(field, "ignored", $"Replacement from {Name(other.Modifier)} not used; the highest replacement applies", other.Amount, value, Origin(other.Modifier), Inputs(other.Inputs)));
        }
        foreach (var modifier in skipped)
            steps.Add(new(field, "ignored", $"{Name(modifier)} not used: {modifier.SkipReason}", null, value, Origin(modifier)));

        // Ability scores: increases first (capped at 20 below), then penalties, so the result does not depend on the
        // order of the content (19 + 2 - 2 is 18 either way). A stable sort keeps content order within each group.
        var bonuses = evaluated.Where(e => e.Modifier.Effect.Operation == ModifierOperation.Bonus)
            .OrderBy(e => IsAbilityScoreField(field) && e.Amount < 0)
            .ToList();
        var winners = bonuses
            .Where(b => b.Modifier.Effect.Stacking == StackingRule.HighestInGroup)
            .GroupBy(b => b.Modifier.Effect.StackGroup, StringComparer.Ordinal)
            .Select(g => g.MaxBy(b => b.Amount).Modifier)
            .ToHashSet();
        foreach (var bonus in bonuses)
        {
            if (bonus.Modifier.Effect.Stacking == StackingRule.HighestInGroup && !winners.Contains(bonus.Modifier))
            {
                steps.Add(new(field, "ignored", $"Bonus from {Name(bonus.Modifier)} does not stack with a higher '{bonus.Modifier.Effect.StackGroup}' bonus", bonus.Amount, value, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
                continue;
            }
            // Each formula is bounded, but their number is not: bound the running value too, so it cannot overflow.
            var next = (long)value + bonus.Amount;
            if (Math.Abs(next) > FormulaLimits.MaxMagnitude)
            {
                var revision = bonus.Modifier.Content.Revision;
                warnings.Add(new(
                    "effect.out-of-range",
                    $"'{revision.Name}' effect '{bonus.Modifier.Effect.Id}' is disabled: it would take {field} outside ±{FormulaLimits.MaxMagnitude:0}.",
                    revision.Reference,
                    bonus.Modifier.Effect.Id));
                manual.Add(field);
                steps.Add(new(field, "ignored", $"Bonus from {Name(bonus.Modifier)} not applied; the result would be out of range", bonus.Amount, value, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
                continue;
            }
            // SRD (both families, owner decision 2026-09-27): increases cannot raise an ability score above 20. A bonus
            // stops at 20 (or at the score it started from, if that was already higher); set effects and overrides may
            // exceed it. A higher content-declared maximum (for example 24) is not modeled yet.
            if (IsAbilityScoreField(field) && bonus.Amount > 0 && next > Math.Max(AbilityScoreIncreaseCap, value))
            {
                var capped = Math.Max(AbilityScoreIncreaseCap, value);
                steps.Add(new(field, "add", $"Bonus from {Name(bonus.Modifier)}, capped: increases cannot raise an ability score above {AbilityScoreIncreaseCap} (+{bonus.Amount} would give {next})", capped - value, capped, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
                value = capped;
                continue;
            }
            value = (int)next;
            steps.Add(new(field, "add", bonus.Modifier.ModBonus && !string.IsNullOrEmpty(bonus.Modifier.Effect.Text) ? bonus.Modifier.Effect.Text : $"Bonus from {Name(bonus.Modifier)}", bonus.Amount, value, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
        }

        var sets = evaluated.Where(e => e.Modifier.Effect.Operation == ModifierOperation.Set).ToList();
        if (sets.Count > 0)
        {
            var best = sets.MaxBy(s => s.Amount);
            value = best.Amount;
            steps.Add(new(field, "set", $"Set by {Name(best.Modifier)}", best.Amount, value, Origin(best.Modifier), Inputs(best.Inputs)));
            foreach (var other in sets.Where(s => s != best))
                steps.Add(new(field, "ignored", $"Set from {Name(other.Modifier)} not used; the highest set applies", other.Amount, value, Origin(other.Modifier), Inputs(other.Inputs)));
        }
        return value;
    }

    /// <summary>
    /// The field plus every field it transitively reads (base inputs and effect formulas). <paramref name="actualReads"/>
    /// narrows a spec's static inputs for this character: the spell fields statically read every ability modifier (for
    /// ordering), but depend only on the proficiency bonus and the primary caster's ability.
    /// </summary>
    private static HashSet<string> Closure(string field, List<Modifier> modifiers, IReadOnlyDictionary<string, IReadOnlyList<string>> actualReads)
    {
        var reads = Specs.ToDictionary(
            s => s.Id,
            s => new HashSet<string>(actualReads.TryGetValue(s.Id, out var actual) ? actual : s.Reads, StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var modifier in modifiers)
            reads[modifier.Effect.Target].UnionWith(modifier.ReadsFields);

        var needed = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([field]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (needed.Add(current))
            {
                foreach (var dependency in reads[current])
                    pending.Push(dependency);
            }
        }
        return needed;
    }

    /// <summary>A field's trace: the own steps of every field in its closure, in dependency order, ending with its own.</summary>
    private static List<TraceEntry> Flatten(HashSet<string> needed, Dictionary<string, List<Step>> ownSteps, List<string> order)
    {
        var entries = new List<TraceEntry>();
        foreach (var id in order.Where(needed.Contains))
        {
            foreach (var step in ownSteps[id])
                entries.Add(new(entries.Count + 1, step.Operation, step.Description, step.Amount, step.Result, step.Origin, step.Field, step.Inputs));
        }
        return entries;
    }

    private static string Describe(ActiveContent content)
    {
        var revision = content.Revision;
        var from = content.GrantedBy is { } by ? $" (granted by {by.Kind.ToString().ToLowerInvariant()} '{by.Name}')"
            : content.ChosenFrom is { } chooser ? $" (chosen from {chooser.Kind.ToString().ToLowerInvariant()} '{chooser.Name}')"
            : "";
        return $"{revision.Kind.ToString().ToLowerInvariant()} '{revision.Name}'{from}";
    }

    private static Diagnostic InvalidFormula(ContentRevision revision, Effect effect, FormulaError error) =>
        new("effect.invalid-formula", $"'{revision.Name}' effect '{effect.Id}' is disabled: {error.Message} ({error.Code})", revision.Reference, effect.Id);

    private static TraceOrigin ContentOrigin(string family, ActiveContent content, Effect effect) =>
        new(TraceOriginKind.Content, family, content.Revision.Reference, content.Revision.Name, effect.Id, content.Source.Id, content.Source.Title, content.Revision.Provenance.Page);
}
