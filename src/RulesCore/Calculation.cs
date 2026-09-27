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

/// <param name="Amount">The value this step contributes (for <c>add</c>/<c>set</c>) or its input, when meaningful.</param>
/// <param name="Result">The running value after this step.</param>
public sealed record TraceEntry(int Order, string Operation, string Description, int? Amount, int Result, TraceOrigin Origin);

/// <summary>ARCHITECTURE "Rules execution" step 5: value, trace, warnings and automation status for one field.</summary>
public sealed record DerivedValue(
    string Field,
    string Label,
    int Value,
    int ComputedValue,
    IReadOnlyList<TraceEntry> Trace,
    IReadOnlyList<Diagnostic> Warnings,
    AutomationStatus Automation,
    FieldOverride? Override);

public sealed record CharacterSheet(
    Guid CharacterId,
    string RulesFamily,
    IReadOnlyList<DerivedValue> Fields,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public DerivedValue Field(string field) => Fields.Single(f => f.Field == field);
}

/// <summary>
/// Pure calculation of derived character values. No persistence, UI or Windows dependencies.
/// Invalid content is isolated with a diagnostic instead of failing the whole sheet (SPEC C-03).
/// </summary>
public static class CharacterCalculator
{
    public const string InitiativeField = "initiative";

    private static readonly HashSet<string> SupportedEffectTypes = [Effect.AbilityScoreIncrease, Effect.InitiativeBonus];

    public static CharacterSheet Calculate(Character character, IContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(catalog);

        var policy = RulesFamilies.Get(character.RulesFamily);
        var diagnostics = new List<Diagnostic>();
        var active = ResolveActiveContent(character, catalog, diagnostics);

        foreach (var (revision, _) in active)
        {
            foreach (var effect in revision.Effects.Where(e => !SupportedEffectTypes.Contains(e.Type)))
            {
                diagnostics.Add(new(
                    "effect.unsupported",
                    $"'{revision.Name}' effect '{effect.Id}' of type '{effect.Type}' is not automated; its text is kept for reference.",
                    revision.Reference,
                    effect.Id));
            }
        }

        var initiative = CalculateInitiative(character, policy, active);
        return new CharacterSheet(character.Id, character.RulesFamily, [initiative], diagnostics);
    }

    private static List<(ContentRevision Revision, SourceRecord Source)> ResolveActiveContent(
        Character character, IContentCatalog catalog, List<Diagnostic> diagnostics)
    {
        var active = new List<(ContentRevision, SourceRecord)>();
        foreach (var pin in character.Pins)
        {
            var revision = catalog.FindRevision(pin);
            if (revision is null)
            {
                diagnostics.Add(new("content.missing", $"Pinned revision {pin.RevisionId} of content {pin.ContentId} is not available.", pin));
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
            active.Add((revision, source));
        }
        return active;
    }

    private static DerivedValue CalculateInitiative(
        Character character, RulesFamilyPolicy policy, List<(ContentRevision Revision, SourceRecord Source)> active)
    {
        var trace = new List<TraceEntry>();
        var warnings = new List<Diagnostic>();
        var order = 1;
        var family = character.RulesFamily;

        var dex = character.BaseAbilities.Dex;
        trace.Add(new(order++, "base", "Dexterity score (character choice)", dex, dex, new(TraceOriginKind.CharacterChoice, family)));

        foreach (var (revision, source, effect) in Effects(active, Effect.AbilityScoreIncrease))
        {
            if (effect.Ability != Ability.Dex)
                continue;
            if (revision.Kind != policy.AbilityIncreaseSource)
            {
                warnings.Add(new(
                    "policy.ability-increase-source",
                    $"'{revision.Name}' ({revision.Kind}) cannot grant ability score increases under {policy.DisplayName}; only {policy.AbilityIncreaseSource} content can. The increase is ignored.",
                    revision.Reference,
                    effect.Id));
                continue;
            }
            if (!TryAmount(revision, effect, warnings, out var amount))
                continue;
            dex += amount;
            trace.Add(new(order++, "add", $"Dexterity increase from {revision.Kind.ToString().ToLowerInvariant()} '{revision.Name}'", amount, dex, ContentOrigin(family, revision, source, effect)));
        }

        var modifier = (int)Math.Floor((dex - 10) / 2.0);
        trace.Add(new(order++, "derive", "Dexterity modifier = floor((score - 10) / 2)", dex, modifier, new(TraceOriginKind.RulesPolicy, family)));

        var value = modifier;
        foreach (var (revision, source, effect) in Effects(active, Effect.InitiativeBonus))
        {
            if (!TryAmount(revision, effect, warnings, out var amount))
                continue;
            value += amount;
            trace.Add(new(order++, "add", $"Initiative bonus from '{revision.Name}'", amount, value, ContentOrigin(family, revision, source, effect)));
        }

        var computed = value;
        var fieldOverride = character.Overrides.LastOrDefault(o => o.Field == InitiativeField);
        if (fieldOverride is not null)
        {
            value = fieldOverride.Value;
            var reason = string.IsNullOrWhiteSpace(fieldOverride.Reason) ? "" : $": {fieldOverride.Reason}";
            trace.Add(new(order, "override", $"User override (computed value {computed}){reason}", fieldOverride.Value, value, new(TraceOriginKind.Override, family)));
        }

        return new DerivedValue(InitiativeField, "Initiative", value, computed, trace, warnings, AutomationStatus.Automatic, fieldOverride);
    }

    private static IEnumerable<(ContentRevision Revision, SourceRecord Source, Effect Effect)> Effects(
        List<(ContentRevision Revision, SourceRecord Source)> active, string type) =>
        from item in active
        from effect in item.Revision.Effects
        where effect.Type == type && effect.Automation == AutomationStatus.Automatic
        select (item.Revision, item.Source, effect);

    private static bool TryAmount(ContentRevision revision, Effect effect, List<Diagnostic> warnings, out int amount)
    {
        if (effect.Amount is { } value and >= -10 and <= 10)
        {
            amount = value;
            return true;
        }
        warnings.Add(new("effect.invalid-amount", $"'{revision.Name}' effect '{effect.Id}' needs an amount between -10 and 10; it is ignored.", revision.Reference, effect.Id));
        amount = 0;
        return false;
    }

    private static TraceOrigin ContentOrigin(string family, ContentRevision revision, SourceRecord source, Effect effect) =>
        new(TraceOriginKind.Content, family, revision.Reference, revision.Name, effect.Id, source.Id, source.Title, revision.Provenance.Page);
}
