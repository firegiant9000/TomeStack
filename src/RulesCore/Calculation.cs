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

/// <summary>
/// SPEC C-01: one choice an active revision offers (and whose level is reached), what was selected for it, and whether
/// it is resolved. The sheet flags unresolved choices; the builder (M2) will answer them.
/// </summary>
public sealed record ChoiceStatus(
    ContentReference Source,
    string SourceName,
    string ChoiceId,
    string? Text,
    int Count,
    IReadOnlyList<ContentReference> Options,
    IReadOnlyList<ContentReference> Selected,
    bool Resolved);

public sealed record CharacterSheet(
    Guid CharacterId,
    string RulesFamily,
    IReadOnlyList<DerivedValue> Fields,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<ChoiceStatus>? Choices = null)
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

    public const string Stealth = "stealth";

    // Declared before Specs: static initializers run in textual order and BuildSpecs reads this.
    private static readonly Dictionary<Ability, string> AbilityNames = new()
    {
        [Ability.Str] = "Strength", [Ability.Dex] = "Dexterity", [Ability.Con] = "Constitution",
        [Ability.Int] = "Intelligence", [Ability.Wis] = "Wisdom", [Ability.Cha] = "Charisma",
    };

    /// <summary>The 18 skills of both SRDs and the ability each uses. Field ids are <c>skill.&lt;key&gt;</c>. Before Specs, like AbilityNames.</summary>
    public static IReadOnlyList<(string Key, string Label, Ability Ability)> Skills { get; } =
    [
        ("acrobatics", "Acrobatics", Ability.Dex), ("animalHandling", "Animal Handling", Ability.Wis), ("arcana", "Arcana", Ability.Int),
        ("athletics", "Athletics", Ability.Str), ("deception", "Deception", Ability.Cha), ("history", "History", Ability.Int),
        ("insight", "Insight", Ability.Wis), ("intimidation", "Intimidation", Ability.Cha), ("investigation", "Investigation", Ability.Int),
        ("medicine", "Medicine", Ability.Wis), ("nature", "Nature", Ability.Int), ("perception", "Perception", Ability.Wis),
        ("performance", "Performance", Ability.Cha), ("persuasion", "Persuasion", Ability.Cha), ("religion", "Religion", Ability.Int),
        ("sleightOfHand", "Sleight of Hand", Ability.Dex), (Stealth, "Stealth", Ability.Dex), ("survival", "Survival", Ability.Wis),
    ];

    private static readonly IReadOnlyList<FieldSpec> Specs = BuildSpecs();

    private static readonly IReadOnlyDictionary<string, int> SpecIndex =
        Specs.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);

    public static IEnumerable<string> Fields => Specs.Select(s => s.Id);

    public static bool IsField(string field) => SpecIndex.ContainsKey(field);

    /// <summary>
    /// Content validation (M1 item 3): the dependency cycles this revision's own modifiers would create with the base
    /// field graph, as <c>effect.dependency-cycle</c> diagnostics. Effects that do not parse or target no field are
    /// reported by <see cref="ContentValidator"/> instead, and are skipped here.
    /// </summary>
    public static IReadOnlyList<Diagnostic> DependencyCycles(ContentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        var source = new SourceRecord
        {
            Id = revision.Provenance.SourceId, Title = "", Publisher = "", RulesFamilies = revision.RulesFamilies,
            EditionVersion = "", License = "", Redistributable = false,
        };
        var content = new ActiveContent(revision, source);
        var modifiers = new List<Modifier>();
        foreach (var effect in revision.Effects.OfType<ModifierEffect>())
        {
            if (SpecIndex.ContainsKey(effect.Target) && Formula.TryParse(effect.Value, out var formula, out _))
                modifiers.Add(new(content, effect, formula!, [.. formula!.Identifiers.Select(FormulaIdentifiers.FieldFor).OfType<string>().Distinct(StringComparer.Ordinal)]));
        }
        var diagnostics = new List<Diagnostic>();
        RemoveCycles(modifiers, diagnostics, Specs.ToDictionary(s => s.Id, _ => new List<Diagnostic>(), StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        return diagnostics;
    }

    /// <summary>
    /// Calculates the sheet. Content with <c>restriction</c> effects (prerequisites such as "Strength 13 or higher") is
    /// checked first against the sheet calculated without that content, so content cannot qualify itself (a feat that
    /// raises Strength does not meet its own Strength prerequisite). Content whose prerequisite is not met is left out,
    /// with a <c>restriction.unmet</c> diagnostic scoped to it; everything else still calculates (SPEC C-03). Checks use
    /// the displayed values, so a user override counts (SPEC C-06), and the trace shows it.
    /// </summary>
    public static CharacterSheet Calculate(Character character, IContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(catalog);

        var first = Calculate(character, catalog, excluded: new HashSet<ContentReference>());
        var unmet = new List<(ContentReference Content, Diagnostic Diagnostic)>();
        foreach (var restricted in first.Active.Where(a => a.Revision.Effects.OfType<RestrictionEffect>().Any(IsEvaluated)))
        {
            var without = Calculate(character, catalog, excluded: new HashSet<ContentReference> { restricted.Revision.Reference });
            foreach (var restriction in restricted.Revision.Effects.OfType<RestrictionEffect>().Where(IsEvaluated))
            {
                if (!SpecIndex.TryGetValue(restriction.Field, out var index))
                {
                    unmet.Add((restricted.Revision.Reference, new(
                        "effect.unknown-target",
                        $"'{restricted.Revision.Name}' restriction '{restriction.Id}' checks '{restriction.Field}', which is not a calculated field; the content is not applied.",
                        restricted.Revision.Reference, restriction.Id)));
                    continue;
                }
                var actual = without.Sheet.Field(restriction.Field).Value;
                if (actual < restriction.Minimum)
                {
                    unmet.Add((restricted.Revision.Reference, new(
                        "restriction.unmet",
                        $"'{restricted.Revision.Name}' requires {Specs[index].Label} {restriction.Minimum} or higher; this character has {actual} without it. Its effects are not applied.",
                        restricted.Revision.Reference, restriction.Id)));
                }
            }
        }
        if (unmet.Count == 0)
            return first.Sheet;

        var final = Calculate(character, catalog, excluded: unmet.Select(u => u.Content).ToHashSet());
        return final.Sheet with { Diagnostics = [.. unmet.Select(u => u.Diagnostic), .. final.Sheet.Diagnostics] };
    }

    private static bool IsEvaluated(RestrictionEffect restriction) =>
        restriction.Automation == AutomationStatus.Automatic && restriction.Timing == EffectTiming.Always;

    private sealed record Calculation(CharacterSheet Sheet, IReadOnlyList<ActiveContent> Active);

    /// <param name="excluded">Revisions left out (restriction checks); anything they would grant or offer is left out with them.</param>
    private static Calculation Calculate(Character character, IContentCatalog catalog, IReadOnlySet<ContentReference> excluded)
    {
        var policy = RulesFamilies.Get(character.RulesFamily);
        var family = character.RulesFamily;
        var diagnostics = new List<Diagnostic>();
        var resolved = ResolveActiveContent(character, catalog, policy, diagnostics, excluded);
        var active = resolved.Active;
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

        // Fields with an effect the calculator could not apply (not automatic, invalid or disabled): the user may need to
        // account for it by hand, so the field and its dependents are only assisted.
        var manual = new HashSet<string>(StringComparer.Ordinal);
        var modifiers = CollectModifiers(active, policy, diagnostics, warnings, manual);
        var proficiencies = CollectProficiencies(active, diagnostics, manual, content => GateLevel(content, character, resolved.ClassLevels));
        RemoveCycles(modifiers, diagnostics, warnings, manual);
        var order = TopologicalOrder(modifiers);

        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        var ownSteps = new Dictionary<string, List<Step>>(StringComparer.Ordinal);
        var results = new Dictionary<string, (int Value, int Computed, FieldOverride? Override)>(StringComparer.Ordinal);
        foreach (var id in order)
        {
            var spec = Specs[SpecIndex[id]];
            var steps = new List<Step>();
            var context = new BaseContext(character, family, values, proficiencies, resolved.Classes, warnings[id], manual);
            var value = spec.Base(context, steps);
            value = ApplyModifiers(id, value, modifiers.Where(m => m.Effect.Target == id).ToList(), character, resolved.ClassLevels, values, family, steps, warnings[id], manual);

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
                closure.Any(manual.Contains) ? AutomationStatus.Assisted : AutomationStatus.Automatic,
                results[spec.Id].Override,
                spec.Units);
        }).ToList();

        return new(new CharacterSheet(character.Id, family, fields, diagnostics, resolved.Choices), active);
    }

    // ---- content resolution ---------------------------------------------------------------------------------

    /// <param name="GrantedBy">Set when a <c>grant</c> effect of another active revision brought this one in.</param>
    /// <param name="ClassRoot">
    /// The class this content belongs to: itself for a class, or the class that granted it or offered the choice it was
    /// chosen from. It gives <c>CLASS_LEVEL</c> and the level that gates its grants and choices.
    /// </param>
    /// <param name="ChosenFrom">Set when the character picked this revision for a choice offered by another active revision.</param>
    private sealed record ActiveContent(
        ContentRevision Revision, SourceRecord Source, ContentRevision? GrantedBy = null, ContentReference? ClassRoot = null, ContentRevision? ChosenFrom = null)
    {
        /// <summary>Pins, classes and chosen content are roots: their grants are followed (one level).</summary>
        public bool IsRoot => GrantedBy is null;
    }

    /// <summary>A class the character has levels in, with its hit die when the class declares one.</summary>
    private sealed record ClassInfo(ActiveContent Content, int Level, HitDieEffect? Die);

    private sealed record ResolvedContent(
        List<ActiveContent> Active, List<ClassInfo> Classes, Dictionary<ContentReference, int> ClassLevels, List<ChoiceStatus> Choices);

    /// <summary>
    /// Pins and classes first. Then, in a worklist: content granted by a root (one level deep only: granted content's own
    /// content grants are not followed, so user content cannot create chains or loops), and the character's selections
    /// for every choice an active revision offers. Chosen content is a root, like a pin. A grant or choice with a
    /// <c>level</c> applies from that class level (character level outside a class). Every revision is admitted once, so
    /// selections cannot loop. Cross-family use needs a recorded exception (B06), which only pins can have.
    /// </summary>
    private static ResolvedContent ResolveActiveContent(
        Character character, IContentCatalog catalog, RulesFamilyPolicy policy, List<Diagnostic> diagnostics, IReadOnlySet<ContentReference> excluded)
    {
        var active = new List<ActiveContent>();
        var seen = new HashSet<ContentReference>();
        var classLevels = new Dictionary<ContentReference, int>();
        foreach (var entry in character.Classes)
            classLevels.TryAdd(entry.Class, entry.Level);
        var choices = new List<ChoiceStatus>();
        var answered = new HashSet<(ContentReference, string)>();

        ActiveContent? Admit(ContentReference reference, ContentRevision? grantedBy, ContentReference? classRoot = null, ContentRevision? chosenFrom = null)
        {
            if (excluded.Contains(reference))
                return null; // left out by a restriction check; the caller reports why
            var prefix = grantedBy is not null ? $"Granted by '{grantedBy.Name}': " : chosenFrom is not null ? $"Chosen from '{chosenFrom.Name}': " : "";
            var revision = catalog.FindRevision(reference);
            if (revision is null)
            {
                diagnostics.Add(new("content.missing", $"{prefix}Revision {reference.RevisionId} of content {reference.ContentId} is not available.", reference));
                return null;
            }
            if (revision.SchemaVersion is < 1 or > ContentRevision.CurrentSchemaVersion)
            {
                diagnostics.Add(new("content.schema-unsupported", $"{prefix}'{revision.Name}' uses content schema v{revision.SchemaVersion}; this version supports up to v{ContentRevision.CurrentSchemaVersion}. It is not applied.", reference));
                return null;
            }
            if (revision.Status != RevisionStatus.Published)
            {
                diagnostics.Add(new("content.unpublished", $"{prefix}'{revision.Name}' is a {revision.Status.ToString().ToLowerInvariant()} revision and is not active until it is reviewed and published.", reference));
                return null;
            }
            if (!revision.RulesFamilies.Contains(character.RulesFamily))
            {
                var exception = grantedBy is null && chosenFrom is null ? character.CrossFamilyExceptions.LastOrDefault(e => e.Content == reference) : null;
                if (exception is null)
                {
                    diagnostics.Add(new("content.rules-family-mismatch", $"{prefix}'{revision.Name}' supports {string.Join(", ", revision.RulesFamilies)}, not {character.RulesFamily}; it is not applied.", reference));
                    return null;
                }
                diagnostics.Add(new(
                    "content.cross-family-exception",
                    $"'{revision.Name}' is written for {string.Join(", ", revision.RulesFamilies)} but is used under {character.RulesFamily} by a recorded exception: {exception.Reason}. It follows {policy.DisplayName} rules.",
                    reference));
            }
            var source = catalog.FindSource(revision.Provenance.SourceId);
            if (source is null)
            {
                diagnostics.Add(new("content.source-missing", $"{prefix}'{revision.Name}' has no known source record; it is not applied.", reference));
                return null;
            }
            return new(revision, source, grantedBy, revision.Kind == ContentKind.Class ? reference : classRoot, chosenFrom);
        }

        foreach (var pin in character.Pins.Concat(character.Classes.Select(c => c.Class)))
        {
            if (seen.Add(pin) && Admit(pin, null) is { } item)
                active.Add(item);
        }

        var classes = new List<ClassInfo>();
        foreach (var entry in character.Classes)
        {
            var item = active.FirstOrDefault(a => a.Revision.Reference == entry.Class);
            if (item is null)
                continue; // already explained by Admit (missing, draft, wrong family, ...)
            if (item.Revision.Kind != ContentKind.Class)
            {
                diagnostics.Add(new("character.class-not-a-class", $"'{item.Revision.Name}' is recorded as a class level but is {item.Revision.Kind.ToString().ToLowerInvariant()} content; it gives no class levels.", entry.Class));
                continue;
            }
            if (classes.Any(c => c.Content.Revision.Reference == entry.Class))
                continue;
            classes.Add(new(item, entry.Level, item.Revision.Effects.OfType<HitDieEffect>().FirstOrDefault(d => d.Automation == AutomationStatus.Automatic)));
        }
        foreach (var pinnedClass in active.Where(a => a.Revision.Kind == ContentKind.Class && !classLevels.ContainsKey(a.Revision.Reference)))
            diagnostics.Add(new("character.class-without-levels", $"'{pinnedClass.Revision.Name}' is pinned as content but no levels are recorded in it, so its level features and hit points do not apply.", pinnedClass.Revision.Reference));

        foreach (var exception in character.CrossFamilyExceptions.Where(e => !character.Pins.Contains(e.Content)))
            diagnostics.Add(new("character.exception-unused", $"A cross-family exception is recorded for revision {exception.Content.RevisionId}, which this character does not pin.", exception.Content));

        var pending = new Queue<ActiveContent>(active);
        while (pending.Count > 0)
        {
            var item = pending.Dequeue();
            var revision = item.Revision;
            if (item.IsRoot)
            {
                foreach (var grant in revision.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content))
                {
                    if (grant.Automation != AutomationStatus.Automatic || grant.Timing != EffectTiming.Always)
                        continue;
                    if (grant.Level is { } needed && GateLevel(item, character, classLevels) < needed)
                        continue; // not reached yet: a level-3 feature at class level 2 is simply not there
                    if (grant.Content is not { } reference)
                    {
                        diagnostics.Add(new("effect.grant-content-missing", $"'{revision.Name}' effect '{grant.Id}' grants content but names none; it is ignored.", revision.Reference, grant.Id));
                        continue;
                    }
                    // Only feats are restricted: a 2014 background may still grant other content, such as its feature.
                    if (revision.Kind == ContentKind.Background && !policy.BackgroundGrantsFeat && catalog.FindRevision(reference)?.Kind == ContentKind.Feat)
                    {
                        diagnostics.Add(new(
                            "policy.background-feat",
                            $"'{revision.Name}' (background) cannot grant a feat under {policy.DisplayName}; the grant is ignored.",
                            revision.Reference,
                            grant.Id));
                        continue;
                    }
                    if (seen.Add(reference) && Admit(reference, revision, item.ClassRoot) is { } granted)
                    {
                        active.Add(granted);
                        pending.Enqueue(granted);
                        foreach (var nested in granted.Revision.Effects.OfType<GrantEffect>().Where(g => g.Grant == GrantKind.Content))
                            diagnostics.Add(new("grant.nested-ignored", $"'{granted.Revision.Name}' was granted by '{revision.Name}'; its own content grant '{nested.Id}' is not followed (grants are one level deep).", granted.Revision.Reference, nested.Id));
                    }
                }
            }

            foreach (var choice in revision.Effects.OfType<ChoiceEffect>())
            {
                if (choice.Level is { } needed && GateLevel(item, character, classLevels) < needed)
                    continue; // not offered yet, so not unresolved either
                var key = (revision.Reference, choice.ChoiceId);
                if (!answered.Add(key))
                    continue;
                var selected = character.Choices.LastOrDefault(c => c.Source == revision.Reference && c.ChoiceId == choice.ChoiceId)?.Selected ?? [];
                var applied = new List<ContentReference>();
                foreach (var option in selected.Distinct())
                {
                    if (!choice.Options.Contains(option))
                    {
                        diagnostics.Add(new("choice.invalid-option", $"'{revision.Name}' choice '{choice.ChoiceId}': revision {option.RevisionId} is not one of its options; it is not applied.", option, choice.Id));
                        continue;
                    }
                    if (applied.Count == choice.Count)
                    {
                        diagnostics.Add(new("choice.too-many", $"'{revision.Name}' choice '{choice.ChoiceId}' allows {choice.Count} selection(s); the extra selection {option.RevisionId} is not applied.", option, choice.Id));
                        continue;
                    }
                    // Only an option that is actually active answers the choice: one refused by Admit (wrong family,
                    // missing, draft) leaves it unresolved, with Admit's diagnostic saying why.
                    if (seen.Add(option))
                    {
                        if (Admit(option, null, item.ClassRoot, revision) is not { } chosen)
                            continue;
                        active.Add(chosen);
                        pending.Enqueue(chosen);
                    }
                    else if (!active.Any(a => a.Revision.Reference == option))
                    {
                        continue;
                    }
                    applied.Add(option);
                }
                var resolved = applied.Count >= choice.Count;
                if (!resolved)
                {
                    diagnostics.Add(new(
                        "choice.unresolved",
                        $"'{revision.Name}' offers a choice ('{choice.ChoiceId}') of {choice.Count}; {applied.Count} selected.{(choice.Text is { } text ? $" {text}" : "")}",
                        revision.Reference,
                        choice.Id));
                }
                choices.Add(new(revision.Reference, revision.Name, choice.ChoiceId, choice.Text, choice.Count, choice.Options, applied, resolved));
            }
        }

        foreach (var orphan in character.Choices.Where(c => !answered.Contains((c.Source, c.ChoiceId))))
            diagnostics.Add(new("choice.orphaned", $"A selection is recorded for choice '{orphan.ChoiceId}' of revision {orphan.Source.RevisionId}, which is not active or does not offer that choice (yet); it is not applied.", orphan.Source));
        return new(active, classes, classLevels, choices);
    }

    /// <summary>The level that gates <paramref name="content"/>'s grants: its class's level, or the character level.</summary>
    private static int GateLevel(ActiveContent content, Character character, Dictionary<ContentReference, int> classLevels) =>
        content.ClassRoot is { } root ? classLevels.GetValueOrDefault(root) : character.TotalLevel;

    // ---- effects --------------------------------------------------------------------------------------------

    private sealed record Modifier(ActiveContent Content, ModifierEffect Effect, Formula Formula, IReadOnlyList<string> ReadsFields);

    private static List<Modifier> CollectModifiers(
        List<ActiveContent> active, RulesFamilyPolicy policy, List<Diagnostic> diagnostics, Dictionary<string, List<Diagnostic>> warnings,
        HashSet<string> manual)
    {
        var modifiers = new List<Modifier>();
        foreach (var item in active)
        {
            var revision = item.Revision;
            foreach (var effect in revision.Effects.OfType<ModifierEffect>())
            {
                if (effect.Automation != AutomationStatus.Automatic || effect.Timing != EffectTiming.Always)
                {
                    if (SpecIndex.ContainsKey(effect.Target))
                        manual.Add(effect.Target);
                    continue;
                }
                if (!SpecIndex.ContainsKey(effect.Target))
                {
                    diagnostics.Add(new("effect.unknown-target", $"'{revision.Name}' effect '{effect.Id}' targets '{effect.Target}', which is not a calculated field; it is ignored.", revision.Reference, effect.Id));
                    continue;
                }
                if (OriginKind(item) is { } origin && IsAbilityScore(effect) && origin != policy.AbilityIncreaseSource)
                {
                    var via = origin == revision.Kind ? "" : $", from {origin.ToString().ToLowerInvariant()} '{(item.ChosenFrom ?? item.GrantedBy)!.Name}'";
                    warnings[effect.Target].Add(new(
                        "policy.ability-increase-source",
                        $"'{revision.Name}' ({revision.Kind}{via}) cannot change ability scores under {policy.DisplayName}; only {policy.AbilityIncreaseSource} content can. The effect is ignored.",
                        revision.Reference,
                        effect.Id));
                    continue;
                }
                if (effect.Stacking == StackingRule.HighestInGroup && string.IsNullOrWhiteSpace(effect.StackGroup))
                {
                    warnings[effect.Target].Add(new("effect.stack-group-missing", $"'{revision.Name}' effect '{effect.Id}' uses highest-in-group stacking without a stackGroup; it is ignored.", revision.Reference, effect.Id));
                    manual.Add(effect.Target);
                    continue;
                }
                if (!Formula.TryParse(effect.Value, out var formula, out var error))
                {
                    warnings[effect.Target].Add(InvalidFormula(revision, effect, error!));
                    manual.Add(effect.Target);
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
    /// family. Feats and class features may raise scores in both families, so they are not restricted here. A feature
    /// chosen from or granted by origin content counts as that origin (a background's "+2 Str, +1 Con" option must not
    /// bypass the policy by being a separate feature). Every operation counts, so <c>set</c> or <c>replace</c> cannot
    /// bypass it either.
    /// </summary>
    private static ContentKind? OriginKind(ActiveContent content) =>
        content.Revision.Kind is ContentKind.Species or ContentKind.Background ? content.Revision.Kind
        : content.Revision.Kind == ContentKind.Feature && (content.ChosenFrom ?? content.GrantedBy)?.Kind is { } parent
            && parent is ContentKind.Species or ContentKind.Background ? parent
        : null;

    private static bool IsAbilityScore(ModifierEffect effect) =>
        effect.Target.StartsWith("ability.", StringComparison.Ordinal) && effect.Target.EndsWith(".score", StringComparison.Ordinal);

    private sealed record Proficiency(GrantKind Grant, ActiveContent Content, GrantEffect Effect);

    private static Dictionary<string, Proficiency> CollectProficiencies(
        List<ActiveContent> active, List<Diagnostic> diagnostics, HashSet<string> manual, Func<ActiveContent, int> gateLevel)
    {
        var best = new Dictionary<string, Proficiency>(StringComparer.Ordinal);
        foreach (var item in active)
        {
            foreach (var grant in item.Revision.Effects.OfType<GrantEffect>())
            {
                if (grant.Grant == GrantKind.Content)
                    continue;
                if (grant.Level is { } needed && gateLevel(item) < needed)
                    continue;
                if (grant.Automation != AutomationStatus.Automatic || grant.Timing != EffectTiming.Always)
                {
                    if (grant.Target is { } pending && SpecIndex.ContainsKey(pending))
                        manual.Add(pending);
                    continue;
                }
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
    /// every effect whose edge lies inside a strongly connected component (or reads its own target), except an edge
    /// that base edges already imply (for example a Dex modifier effect reading the Dex score): it adds no
    /// reachability, so it cannot close a cycle, and the graph stays acyclic without it.
    /// </summary>
    private static void RemoveCycles(List<Modifier> modifiers, List<Diagnostic> diagnostics, Dictionary<string, List<Diagnostic>> warnings, HashSet<string> manual)
    {
        var edges = Edges(modifiers).ToList();
        var adjacency = Specs.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var (from, to, _) in edges)
            adjacency[from].Add(to);

        var component = StronglyConnectedComponents(adjacency);
        var cyclic = edges
            .Where(e => e.Via is not null && (e.From == e.To || (component[e.From] == component[e.To] && ComponentSize(component, component[e.From]) > 1 && !BaseReaches(e.From, e.To))))
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
            manual.Add(modifier.Effect.Target);
            modifiers.Remove(modifier);
        }
    }

    private static int ComponentSize(Dictionary<string, int> component, int id) => component.Count(c => c.Value == id);

    /// <summary>True if <paramref name="to"/> depends on <paramref name="from"/> through base inputs alone (a path of length ≥ 1).</summary>
    private static bool BaseReaches(string from, string to)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([from]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var dependent in Specs.Where(s => s.Reads.Contains(current, StringComparer.Ordinal)).Select(s => s.Id))
            {
                if (dependent == to)
                    return true;
                if (seen.Add(dependent))
                    pending.Push(dependent);
            }
        }
        return false;
    }

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
        string field, int value, List<Modifier> modifiers, Character character, Dictionary<ContentReference, int> classLevels,
        Dictionary<string, int> values, string family, List<Step> steps, List<Diagnostic> warnings, HashSet<string> manual)
    {
        var evaluated = new List<(Modifier Modifier, int Amount, List<TraceInput> Inputs)>();
        foreach (var modifier in modifiers)
        {
            var inputs = new List<TraceInput>();
            int? Resolve(string identifier)
            {
                int? resolved = identifier switch
                {
                    FormulaIdentifiers.Level => character.TotalLevel,
                    // CLASS_LEVEL: the level in the class this content belongs to; unavailable outside a class.
                    FormulaIdentifiers.ClassLevel => modifier.Content.ClassRoot is { } root && classLevels.TryGetValue(root, out var level) ? level : null,
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
            {
                warnings.Add(InvalidFormula(modifier.Content.Revision, modifier.Effect, error!));
                manual.Add(field);
            }
        }

        TraceOrigin Origin(Modifier m) => ContentOrigin(family, m.Content, m.Effect);
        string Name(Modifier m) => Describe(m.Content);
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
            value = (int)next;
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

    private static string Describe(ActiveContent content)
    {
        var revision = content.Revision;
        var from = content.GrantedBy is { } by ? $" (granted by {by.Kind.ToString().ToLowerInvariant()} '{by.Name}')"
            : content.ChosenFrom is { } chooser ? $" (chosen from {chooser.Kind.ToString().ToLowerInvariant()} '{chooser.Name}')"
            : "";
        return $"{revision.Kind.ToString().ToLowerInvariant()} '{revision.Name}'{from}";
    }

    private static Diagnostic InvalidFormula(ContentRevision revision, ModifierEffect effect, FormulaError error) =>
        new("effect.invalid-formula", $"'{revision.Name}' effect '{effect.Id}' is disabled: {error.Message} ({error.Code})", revision.Reference, effect.Id);

    private static TraceOrigin ContentOrigin(string family, ActiveContent content, Effect effect) =>
        new(TraceOriginKind.Content, family, content.Revision.Reference, content.Revision.Name, effect.Id, content.Source.Id, content.Source.Title, content.Revision.Provenance.Page);

    // ---- field definitions ----------------------------------------------------------------------------------

    /// <param name="Warnings">This field's warnings; a base derivation may add to them.</param>
    /// <param name="Manual">Fields that are only assisted; a base derivation that cannot complete adds its field.</param>
    private sealed record BaseContext(
        Character Character, string Family, Dictionary<string, int> Values, Dictionary<string, Proficiency> Proficiencies,
        IReadOnlyList<ClassInfo> Classes, List<Diagnostic> Warnings, HashSet<string> Manual);

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
            var level = c.Character.TotalLevel;
            var value = 2 + ((level - 1) / 4);
            steps.Add(new(FieldIds.ProficiencyBonus, "derive", $"Proficiency bonus by character level {level} = 2 + floor((level - 1) / 4)", level, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(FormulaIdentifiers.Level, level)]));
            return value;
        }));
        foreach (var ability in Enum.GetValues<Ability>())
            specs.Add(Proficient(FieldIds.Save(ability), $"{AbilityNames[ability]} saving throw", ability));
        foreach (var (key, label, ability) in Skills)
            specs.Add(Proficient(FieldIds.Skill(key), label, ability));
        specs.Add(new(FieldIds.Initiative, "Initiative", "modifier", [FieldIds.Modifier(Ability.Dex)], (c, steps) =>
        {
            var value = c.Values[FieldIds.Modifier(Ability.Dex)];
            steps.Add(new(FieldIds.Initiative, "base", "Initiative starts at the Dexterity modifier", value, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(FieldIds.Modifier(Ability.Dex), value)]));
            return value;
        }));
        specs.Add(new(FieldIds.ArmorClass, "Armor Class", "score", [FieldIds.Modifier(Ability.Dex)], (c, steps) =>
        {
            var dex = c.Values[FieldIds.Modifier(Ability.Dex)];
            var value = 10 + dex;
            // Armor and shields are not modeled yet (M2 equipment): this is the unarmored base. Alternatives such as
            // Unarmored Defense are content `replace` effects; the highest replacement wins (ADR-003).
            steps.Add(new(FieldIds.ArmorClass, "base", "Armor Class without armor = 10 + Dexterity modifier (armor is not modeled yet)", 10, value, new(TraceOriginKind.RulesPolicy, c.Family), [new(FieldIds.Modifier(Ability.Dex), dex)]));
            return value;
        }));
        specs.Add(new(FieldIds.HitPoints, "Hit point maximum", "score", [FieldIds.Modifier(Ability.Con)], HitPoints));
        return specs;
    }

    /// <summary>
    /// SRD hit points: the starting class's hit die maximum at level 1, the fixed value (half the die plus 1) for every
    /// other class level, and the Constitution modifier once per character level. Rolled hit points are recorded as an
    /// override. Without a class there is nothing to derive, so the field is assisted with a warning.
    /// </summary>
    private static int HitPoints(BaseContext c, List<Step> steps)
    {
        const string field = FieldIds.HitPoints;
        var policy = new TraceOrigin(TraceOriginKind.RulesPolicy, c.Family);
        if (c.Classes.Count == 0)
        {
            steps.Add(new(field, "base", "No class levels are recorded, so hit points cannot be derived; record a class or override the value", 0, 0, policy));
            c.Warnings.Add(new("hit-points.no-class", "Hit points need at least one class level. Record the character's class, or override the value."));
            c.Manual.Add(field);
            return 0;
        }

        var value = 0;
        for (var i = 0; i < c.Classes.Count; i++)
        {
            var (content, level, hitDie) = c.Classes[i];
            var name = content.Revision.Name;
            if (hitDie is null)
            {
                c.Warnings.Add(new("class.hit-die-missing", $"'{name}' declares no hit die, so its {level} level(s) add no hit points.", content.Revision.Reference));
                c.Manual.Add(field);
                continue;
            }
            var die = hitDie.Die;
            var origin = ContentOrigin(c.Family, content, hitDie);
            var fixedValue = (die / 2) + 1;
            var fromLevel = 1;
            if (i == 0)
            {
                value += die;
                steps.Add(new(field, "add", $"{name} level 1: the hit die maximum (d{die})", die, value, origin));
                fromLevel = 2;
            }
            var remaining = level - fromLevel + 1;
            if (remaining > 0)
            {
                var amount = remaining * fixedValue;
                value += amount;
                var levels = remaining == 1 ? $"level {fromLevel}" : $"levels {fromLevel}–{level}";
                steps.Add(new(field, "add", $"{name} {levels}: {remaining} × {fixedValue} (fixed value for d{die})", amount, value, origin));
            }
        }

        var con = c.Values[FieldIds.Modifier(Ability.Con)];
        var total = c.Character.TotalLevel;
        value += con * total;
        steps.Add(new(field, "add", $"Constitution modifier ({(con >= 0 ? "+" : "")}{con}) × {total} character level(s)", con * total, value, policy, [new(FieldIds.Modifier(Ability.Con), con), new(FormulaIdentifiers.Level, total)]));
        return value;
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
                steps.Add(new(id, "add", $"{what} from {Describe(proficiency.Content)}", amount, value, ContentOrigin(c.Family, proficiency.Content, proficiency.Effect), [new(FieldIds.ProficiencyBonus, pb)]));
            }
            return value;
        });
    }
}
