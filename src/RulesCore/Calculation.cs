namespace TomeStack.RulesCore;

/// <summary>Read-only access to content revisions and sources. Implemented by persistence, not by the rules core.</summary>
public interface IContentCatalog
{
    ContentRevision? FindRevision(ContentReference reference);
    SourceRecord? FindSource(Guid sourceId);
}

public sealed record Diagnostic(string Code, string Message, ContentReference? Content = null, string? EffectId = null);

public enum TraceOriginKind { CharacterChoice, Content, RulesPolicy, Override }

/// <summary>Where a trace step came from: the character's own choice, a rules-family policy, pinned content, or a user override.</summary>
public sealed record TraceOrigin(
    TraceOriginKind Kind,
    string RulesFamily,
    ContentReference? Content = null,
    string? ContentName = null,
    string? EffectId = null,
    Guid? SourceId = null,
    string? SourceTitle = null,
    PageRef? Page = null);

/// <summary>A value a step read: a field id or formula identifier, and its value at that point.</summary>
public sealed record TraceInput(string Name, int Value);

/// <param name="Amount">The value this step contributes (for <c>add</c>/<c>set</c>) or its input, when meaningful.</param>
/// <param name="Result">The running value of <paramref name="Field"/> after this step.</param>
/// <param name="Field">The field this step belongs to. A field's trace includes the steps of the fields it reads.</param>
/// <param name="Inputs">Values the step read (ARCHITECTURE "Rules execution" step 5).</param>
public sealed record TraceEntry(
    int Order,
    string Operation,
    string Description,
    int? Amount,
    int Result,
    TraceOrigin Origin,
    string? Field = null,
    IReadOnlyList<TraceInput>? Inputs = null);

/// <summary>ARCHITECTURE "Rules execution" step 5: value, units, trace, warnings and automation status for one field.</summary>
public sealed record DerivedValue(
    string Field,
    string Label,
    int Value,
    int ComputedValue,
    IReadOnlyList<TraceEntry> Trace,
    IReadOnlyList<Diagnostic> Warnings,
    AutomationStatus Automation,
    FieldOverride? Override,
    string Units = "");

public sealed record CharacterSheet(
    Guid CharacterId,
    string RulesFamily,
    IReadOnlyList<DerivedValue> Fields,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public DerivedValue Field(string field) => Fields.Single(f => f.Field == field);
}

/// <summary>
/// Pure, dependency-ordered calculation of derived character values (ARCHITECTURE "Rules execution"; ADR-003).
/// Fields form a graph: each field's base inputs, plus an edge for every identifier an effect formula reads.
/// Effects that would create a cycle are disabled with a diagnostic, and the rest of the sheet still calculates.
/// There are no persistence, UI or Windows dependencies. Invalid content is isolated (SPEC C-03).
/// </summary>
public static class CharacterCalculator
{
    public const string InitiativeField = FieldIds.Initiative;

    /// <summary>The one skill modeled in M1 groundwork.</summary>
    public const string Stealth = "stealth";

    // Declared before Specs: static initializers run in textual order and BuildSpecs reads this.
    private static readonly Dictionary<Ability, string> AbilityNames = new()
    {
        [Ability.Str] = "Strength", [Ability.Dex] = "Dexterity", [Ability.Con] = "Constitution",
        [Ability.Int] = "Intelligence", [Ability.Wis] = "Wisdom", [Ability.Cha] = "Charisma",
    };

    private static readonly IReadOnlyList<FieldSpec> Specs = BuildSpecs();

    private static readonly IReadOnlyDictionary<string, int> SpecIndex =
        Specs.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);

    public static IEnumerable<string> Fields => Specs.Select(s => s.Id);

    public static CharacterSheet Calculate(Character character, IContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(catalog);

        var policy = RulesFamilies.Get(character.RulesFamily);
        var family = character.RulesFamily;
        var diagnostics = new List<Diagnostic>();
        var active = ResolveActiveContent(character, catalog, diagnostics);
        var warnings = Specs.ToDictionary(s => s.Id, _ => new List<Diagnostic>(), StringComparer.Ordinal);

        foreach (var item in active)
        {
            foreach (var effect in item.Revision.Effects.OfType<UnknownEffect>())
            {
                diagnostics.Add(new(
                    "effect.unsupported",
                    $"'{item.Revision.Name}' effect '{effect.Id}' of type '{effect.Type}' is not automated; its text is kept for reference.",
                    item.Revision.Reference,
                    effect.Id));
            }
        }

        var modifiers = CollectModifiers(active, policy, diagnostics, warnings);
        var proficiencies = CollectProficiencies(active, diagnostics);
        RemoveCycles(modifiers, diagnostics, warnings);
        var order = TopologicalOrder(modifiers);

        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        var ownSteps = new Dictionary<string, List<Step>>(StringComparer.Ordinal);
        var results = new Dictionary<string, (int Value, int Computed, FieldOverride? Override)>(StringComparer.Ordinal);
        foreach (var id in order)
        {
            var spec = Specs[SpecIndex[id]];
            var steps = new List<Step>();
            var context = new BaseContext(character, family, values, proficiencies);
            var value = spec.Base(context, steps);
            value = ApplyModifiers(id, value, modifiers.Where(m => m.Effect.Target == id).ToList(), character, values, family, steps, warnings[id]);

            var computed = value;
            var fieldOverride = character.Overrides.LastOrDefault(o => o.Field == id);
            if (fieldOverride is not null)
            {
                value = fieldOverride.Value;
                var reason = string.IsNullOrWhiteSpace(fieldOverride.Reason) ? "" : $": {fieldOverride.Reason}";
                steps.Add(new(id, "override", $"User override (computed value {computed}){reason}", fieldOverride.Value, value, new(TraceOriginKind.Override, family)));
            }
            values[id] = value;
            ownSteps[id] = steps;
            results[id] = (value, computed, fieldOverride);
        }

        var fields = Specs.Select(spec =>
        {
            var closure = Closure(spec.Id, modifiers);
            return new DerivedValue(
                spec.Id,
                spec.Label,
                results[spec.Id].Value,
                results[spec.Id].Computed,
                Flatten(closure, ownSteps, order),
                // A field explains itself with its inputs' warnings too, e.g. an ignored Dex increase explains initiative.
                [.. order.Where(closure.Contains).SelectMany(id => warnings[id]).Distinct()],
                AutomationStatus.Automatic,
                results[spec.Id].Override,
                spec.Units);
        }).ToList();

        return new CharacterSheet(character.Id, family, fields, diagnostics);
    }

    // ---- content resolution ---------------------------------------------------------------------------------

    private sealed record ActiveContent(ContentRevision Revision, SourceRecord Source);

    private static List<ActiveContent> ResolveActiveContent(Character character, IContentCatalog catalog, List<Diagnostic> diagnostics)
    {
        var active = new List<ActiveContent>();
        foreach (var pin in character.Pins)
        {
            var revision = catalog.FindRevision(pin);
            if (revision is null)
            {
                diagnostics.Add(new("content.missing", $"Pinned revision {pin.RevisionId} of content {pin.ContentId} is not available.", pin));
                continue;
            }
            if (revision.SchemaVersion is < 1 or > ContentRevision.CurrentSchemaVersion)
            {
                diagnostics.Add(new("content.schema-unsupported", $"'{revision.Name}' uses content schema v{revision.SchemaVersion}; this version supports up to v{ContentRevision.CurrentSchemaVersion}. It is not applied.", pin));
                continue;
            }
            if (revision.Status != RevisionStatus.Published)
            {
                diagnostics.Add(new("content.unpublished", $"'{revision.Name}' is a {revision.Status.ToString().ToLowerInvariant()} revision and is not active until it is reviewed and published.", pin));
                continue;
            }
            if (!revision.RulesFamilies.Contains(character.RulesFamily))
            {
                diagnostics.Add(new("content.rules-family-mismatch", $"'{revision.Name}' supports {string.Join(", ", revision.RulesFamilies)}, not {character.RulesFamily}; it is not applied.", pin));
                continue;
            }
            var source = catalog.FindSource(revision.Provenance.SourceId);
            if (source is null)
            {
                diagnostics.Add(new("content.source-missing", $"'{revision.Name}' has no known source record; it is not applied.", pin));
                continue;
            }
            active.Add(new(revision, source));
        }
        return active;
    }

    // ---- effects --------------------------------------------------------------------------------------------

    private sealed record Modifier(ActiveContent Content, ModifierEffect Effect, Formula Formula, IReadOnlyList<string> ReadsFields);

    private static List<Modifier> CollectModifiers(
        List<ActiveContent> active, RulesFamilyPolicy policy, List<Diagnostic> diagnostics, Dictionary<string, List<Diagnostic>> warnings)
    {
        var modifiers = new List<Modifier>();
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var effect in revision.Effects.OfType<ModifierEffect>())
            {
                if (effect.Automation != AutomationStatus.Automatic || effect.Timing != EffectTiming.Always)
                    continue;
                if (!SpecIndex.ContainsKey(effect.Target))
                {
                    diagnostics.Add(new("effect.unknown-target", $"'{revision.Name}' effect '{effect.Id}' targets '{effect.Target}', which is not a calculated field; it is ignored.", revision.Reference, effect.Id));
                    continue;
                }
                if (IsOriginAbilityIncrease(revision, effect) && revision.Kind != policy.AbilityIncreaseSource)
                {
                    warnings[effect.Target].Add(new(
                        "policy.ability-increase-source",
                        $"'{revision.Name}' ({revision.Kind}) cannot grant ability score increases under {policy.DisplayName}; only {policy.AbilityIncreaseSource} content can. The increase is ignored.",
                        revision.Reference,
                        effect.Id));
                    continue;
                }
                if (effect.Stacking == StackingRule.HighestInGroup && string.IsNullOrWhiteSpace(effect.StackGroup))
                {
                    warnings[effect.Target].Add(new("effect.stack-group-missing", $"'{revision.Name}' effect '{effect.Id}' uses highest-in-group stacking without a stackGroup; it is ignored.", revision.Reference, effect.Id));
                    continue;
                }
                if (!Formula.TryParse(effect.Value, out var formula, out var error))
                {
                    warnings[effect.Target].Add(InvalidFormula(revision, effect, error!));
                    continue;
                }
                var reads = formula!.Identifiers.Select(FormulaIdentifiers.FieldFor).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
                modifiers.Add(new(item, effect, formula, reads));
            }
        }
        return modifiers;
    }

    /// <summary>
    /// SPEC C-01 / RulesFamilyPolicy: which *origin* content (species or background) may raise ability scores differs by
    /// family. Feats and class features may raise scores in both families, so they are not restricted here.
    /// </summary>
    private static bool IsOriginAbilityIncrease(ContentRevision revision, ModifierEffect effect) =>
        effect.Operation == ModifierOperation.Bonus
        && revision.Kind is ContentKind.Species or ContentKind.Background
        && effect.Target.StartsWith("ability.", StringComparison.Ordinal)
        && effect.Target.EndsWith(".score", StringComparison.Ordinal);

    private sealed record Proficiency(GrantKind Grant, ActiveContent Content, GrantEffect Effect);

    private static Dictionary<string, Proficiency> CollectProficiencies(List<ActiveContent> active, List<Diagnostic> diagnostics)
    {
        var best = new Dictionary<string, Proficiency>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            foreach (var grant in item.Revision.Effects.OfType<GrantEffect>())
            {
                if (grant.Automation != AutomationStatus.Automatic || grant.Timing != EffectTiming.Always || grant.Grant == GrantKind.Content)
                    continue;
                var target = grant.Target ?? "";
                if (!SpecIndex.ContainsKey(target) || !(target.StartsWith("save.", StringComparison.Ordinal) || target.StartsWith("skill.", StringComparison.Ordinal)))
                {
                    diagnostics.Add(new("effect.unknown-target", $"'{item.Revision.Name}' effect '{grant.Id}' grants {grant.Grant.ToString().ToLowerInvariant()} in '{target}', which is not a calculated saving throw or skill; it is ignored.", item.Revision.Reference, grant.Id));
                    continue;
                }
                if (grant.Grant == GrantKind.Expertise && target.StartsWith("save.", StringComparison.Ordinal))
                {
                    diagnostics.Add(new("effect.expertise-on-save", $"'{item.Revision.Name}' effect '{grant.Id}' grants expertise in a saving throw, which is not supported; proficiency is applied instead.", item.Revision.Reference, grant.Id));
                }
                var kind = target.StartsWith("save.", StringComparison.Ordinal) ? GrantKind.Proficiency : grant.Grant;
                if (!best.TryGetValue(target, out var current) || (kind == GrantKind.Expertise && current.Grant != GrantKind.Expertise))
                    best[target] = new(kind, item, grant);
            }
        }
        return best;
    }

    // ---- graph ----------------------------------------------------------------------------------------------

    private static IEnumerable<(string From, string To, Modifier? Via)> Edges(IEnumerable<Modifier> modifiers)
    {
        foreach (var spec in Specs)
        {
            foreach (var dependency in spec.Reads)
                yield return (dependency, spec.Id, null);
        }
        foreach (var modifier in modifiers)
        {
            foreach (var read in modifier.ReadsFields)
                yield return (read, modifier.Effect.Target, modifier);
        }
    }

    /// <summary>
    /// Tarjan SCC. Base edges are acyclic by construction, so every cycle runs through at least one effect edge. Disable
    /// every effect whose edge lies inside a strongly connected component (or reads its own target).
    /// </summary>
    private static void RemoveCycles(List<Modifier> modifiers, List<Diagnostic> diagnostics, Dictionary<string, List<Diagnostic>> warnings)
    {
        var edges = Edges(modifiers).ToList();
        var adjacency = Specs.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to, _) in edges)
            adjacency[from].Add(to);

        var component = StronglyConnectedComponents(adjacency);
        var cyclic = edges
            .Where(e => e.Via is not null && (e.From == e.To || (component[e.From] == component[e.To] && ComponentSize(component, component[e.From]) > 1)))
            .Select(e => e.Via!)
            .Distinct()
            .ToList();

        foreach (var modifier in cyclic)
        {
            var members = component.Where(c => c.Value == component[modifier.Effect.Target]).Select(c => c.Key).OrderBy(f => SpecIndex[f]).ToList();
            var cycle = members.Count > 1 ? string.Join(" → ", [.. members, members[0]]) : $"{modifier.Effect.Target} → {modifier.Effect.Target}";
            var revision = modifier.Content.Revision;
            var diagnostic = new Diagnostic(
                "effect.dependency-cycle",
                $"'{revision.Name}' effect '{modifier.Effect.Id}' is disabled: its formula '{modifier.Formula.Source}' reads {string.Join(", ", modifier.ReadsFields)}, which depends on {modifier.Effect.Target} (cycle: {cycle}).",
                revision.Reference,
                modifier.Effect.Id);
            diagnostics.Add(diagnostic);
            warnings[modifier.Effect.Target].Add(diagnostic);
            modifiers.Remove(modifier);
        }
    }

    private static int ComponentSize(Dictionary<string, int> component, int id) => component.Count(c => c.Value == id);

    private static Dictionary<string, int> StronglyConnectedComponents(Dictionary<string, List<string>> adjacency)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var component = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;

        // Recursion depth is bounded by the fixed field count (not by user content).
        void Visit(string node)
        {
            indices[node] = lowLinks[node] = index++;
            stack.Push(node);
            onStack.Add(node);
            foreach (var target in adjacency[node])
            {
                if (!indices.ContainsKey(target))
                {
                    Visit(target);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indices[target]);
                }
            }
            if (lowLinks[node] != indices[node])
                return;
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component[member] = next;
            }
            while (member != node);
            next++;
        }

        foreach (var node in adjacency.Keys)
        {
            if (!indices.ContainsKey(node))
                Visit(node);
        }
        return component;
    }

    /// <summary>Kahn's algorithm; ties break by declaration order, so the order is deterministic.</summary>
    private static List<string> TopologicalOrder(IEnumerable<Modifier> modifiers)
    {
        var incoming = Specs.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        var outgoing = Specs.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to, _) in Edges(modifiers))
        {
            outgoing[from].Add(to);
            incoming[to]++;
        }
        var ready = new SortedSet<int>(Specs.Where(s => incoming[s.Id] == 0).Select(s => SpecIndex[s.Id]));
        var order = new List<string>();
        while (ready.Count > 0)
        {
            var id = Specs[ready.Min].Id;
            ready.Remove(ready.Min);
            order.Add(id);
            foreach (var target in outgoing[id])
            {
                if (--incoming[target] == 0)
                    ready.Add(SpecIndex[target]);
            }
        }
        return order.Count == Specs.Count
            ? order
            : throw new InvalidOperationException("Dependency cycle survived cycle removal."); // unreachable by construction
    }

    // ---- evaluation -----------------------------------------------------------------------------------------

    private sealed record Step(string Field, string Operation, string Description, int? Amount, int Result, TraceOrigin Origin, IReadOnlyList<TraceInput>? Inputs = null);

    /// <summary>ADR-003 order: highest replace, then bonuses (stack / highest in group), then highest set.</summary>
    private static int ApplyModifiers(
        string field, int value, List<Modifier> modifiers, Character character, Dictionary<string, int> values,
        string family, List<Step> steps, List<Diagnostic> warnings)
    {
        var evaluated = new List<(Modifier Modifier, int Amount, List<TraceInput> Inputs)>();
        foreach (var modifier in modifiers)
        {
            var inputs = new List<TraceInput>();
            int? Resolve(string identifier)
            {
                int? resolved = identifier switch
                {
                    FormulaIdentifiers.Level => character.Level,
                    _ when FormulaIdentifiers.FieldFor(identifier) is { } read && values.TryGetValue(read, out var v) => v,
                    _ => null,
                };
                if (resolved is { } r)
                    inputs.Add(new(identifier, r));
                return resolved;
            }
            if (modifier.Formula.TryEvaluate(Resolve, out var amount, out var error))
                evaluated.Add((modifier, amount, inputs));
            else
                warnings.Add(InvalidFormula(modifier.Content.Revision, modifier.Effect, error!));
        }

        TraceOrigin Origin(Modifier m) => ContentOrigin(family, m.Content, m.Effect);
        string Name(Modifier m) => $"{m.Content.Revision.Kind.ToString().ToLowerInvariant()} '{m.Content.Revision.Name}'";
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

        var bonuses = evaluated.Where(e => e.Modifier.Effect.Operation == ModifierOperation.Bonus).ToList();
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
            value += bonus.Amount;
            steps.Add(new(field, "add", $"Bonus from {Name(bonus.Modifier)}", bonus.Amount, value, Origin(bonus.Modifier), Inputs(bonus.Inputs)));
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

    /// <summary>The field plus every field it transitively reads (base inputs and effect formulas).</summary>
    private static HashSet<string> Closure(string field, List<Modifier> modifiers)
    {
        var reads = Specs.ToDictionary(s => s.Id, s => new HashSet<string>(s.Reads, StringComparer.Ordinal), StringComparer.Ordinal);
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

    private static Diagnostic InvalidFormula(ContentRevision revision, ModifierEffect effect, FormulaError error) =>
        new("effect.invalid-formula", $"'{revision.Name}' effect '{effect.Id}' is disabled: {error.Message} ({error.Code})", revision.Reference, effect.Id);

    private static TraceOrigin ContentOrigin(string family, ActiveContent content, Effect effect) =>
        new(TraceOriginKind.Content, family, content.Revision.Reference, content.Revision.Name, effect.Id, content.Source.Id, content.Source.Title, content.Revision.Provenance.Page);

    // ---- field definitions ----------------------------------------------------------------------------------

    private sealed record BaseContext(Character Character, string Family, Dictionary<string, int> Values, Dictionary<string, Proficiency> Proficiencies);

    private sealed record FieldSpec(string Id, string Label, string Units, IReadOnlyList<string> Reads, Func<BaseContext, List<Step>, int> Base);

    private static List<FieldSpec> BuildSpecs()
    {
        var specs = new List<FieldSpec>();
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var name = AbilityNames[ability];
            var score = FieldIds.Score(ability);
            specs.Add(new(score, $"{name} score", "score", [], (c, steps) =>
            {
                var value = c.Character.BaseAbilities.Get(ability);
                steps.Add(new(score, "base", $"{name} score (character choice)", value, value, new(TraceOriginKind.CharacterChoice, c.Family)));
                return value;
            }));
        }
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var name = AbilityNames[ability];
            var score = FieldIds.Score(ability);
            var modifier = FieldIds.Modifier(ability);
            specs.Add(new(modifier, $"{name} modifier", "modifier", [score], (c, steps) =>
            {
                var input = c.Values[score];
                var value = (int)Math.Floor((input - 10) / 2.0);
                steps.Add(new(modifier, "derive", $"{name} modifier = floor((score - 10) / 2)", input, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(score, input)]));
                return value;
            }));
        }
        specs.Add(new(FieldIds.ProficiencyBonus, "Proficiency bonus", "bonus", [], (c, steps) =>
        {
            var level = c.Character.Level;
            var value = 2 + ((level - 1) / 4);
            steps.Add(new(FieldIds.ProficiencyBonus, "derive", $"Proficiency bonus by character level {level} = 2 + floor((level - 1) / 4)", level, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(FormulaIdentifiers.Level, level)]));
            return value;
        }));
        foreach (var ability in Enum.GetValues<Ability>())
            specs.Add(Proficient(FieldIds.Save(ability), $"{AbilityNames[ability]} saving throw", ability));
        specs.Add(Proficient(FieldIds.Skill(Stealth), "Stealth", Ability.Dex));
        specs.Add(new(FieldIds.Initiative, "Initiative", "modifier", [FieldIds.Modifier(Ability.Dex)], (c, steps) =>
        {
            var value = c.Values[FieldIds.Modifier(Ability.Dex)];
            steps.Add(new(FieldIds.Initiative, "base", "Initiative starts at the Dexterity modifier", value, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(FieldIds.Modifier(Ability.Dex), value)]));
            return value;
        }));
        return specs;
    }

    /// <summary>A saving throw or skill: ability modifier, plus the proficiency bonus (doubled for expertise) if granted.</summary>
    private static FieldSpec Proficient(string id, string label, Ability ability)
    {
        var modifier = FieldIds.Modifier(ability);
        return new(id, label, "modifier", [modifier, FieldIds.ProficiencyBonus], (c, steps) =>
        {
            var value = c.Values[modifier];
            steps.Add(new(id, "base", $"{label} starts at the {AbilityNames[ability]} modifier", value, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(modifier, value)]));
            if (c.Proficiencies.TryGetValue(id, out var proficiency))
            {
                var pb = c.Values[FieldIds.ProficiencyBonus];
                var amount = proficiency.Grant == GrantKind.Expertise ? pb * 2 : pb;
                value += amount;
                var what = proficiency.Grant == GrantKind.Expertise ? "Expertise (twice the proficiency bonus)" : "Proficiency bonus";
                steps.Add(new(id, "add", $"{what} from {proficiency.Content.Revision.Kind.ToString().ToLowerInvariant()} '{proficiency.Content.Revision.Name}'", amount, value, ContentOrigin(c.Family, proficiency.Content, proficiency.Effect), [new(FieldIds.ProficiencyBonus, pb)]));
            }
            return value;
        });
    }
}
