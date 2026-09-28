namespace TomeStack.RulesCore;

/// <summary>
/// One mechanic of a character's active content, as MVP definition of done 3 and the M3 gate count them: an effect of a
/// feature (or a feature with no effects: pure text), how far TomeStack automates it, and what the player does by hand
/// when it is not automatic. <paramref name="ManualStep"/> is the effect's text, else the feature's summary; null means the
/// content gives no instruction, which the inventory reports as a gap.
/// </summary>
public sealed record Mechanic(
    ContentReference Content,
    string FeatureName,
    ContentKind Kind,
    string? EffectId,
    string EffectType,
    AutomationStatus Automation,
    string? ManualStep,
    Guid? SourceId,
    PageRef? Page)
{
    /// <summary>A mechanic TomeStack does not fully apply, with nothing telling the player what to do.</summary>
    public bool MissingManualStep => Automation != AutomationStatus.Automatic && string.IsNullOrWhiteSpace(ManualStep);
}

/// <summary>What <see cref="MechanicsInventory.Of"/> found for one source's content on a character.</summary>
/// <param name="Unsupported">Effects of a type this build does not know (reference only): the gaps to design (M3 B2).</param>
public sealed record MechanicsReport(IReadOnlyList<Mechanic> Mechanics, IReadOnlyList<Mechanic> Unsupported)
{
    public int Count(AutomationStatus automation) => Mechanics.Count(m => m.Automation == automation);

    /// <summary>DoD 3: an automatic modifier is used.</summary>
    public bool HasModifier => Mechanics.Any(m => m.EffectType == ModifierEffect.TypeName && m.Automation == AutomationStatus.Automatic);

    /// <summary>DoD 3: a class resource (a tracked resource).</summary>
    public bool HasResource => Mechanics.Any(m => m.EffectType == ResourceEffect.TypeName && m.Automation != AutomationStatus.Reference);

    /// <summary>DoD 3: a limited-use action (a roll that names the resource it uses).</summary>
    public bool HasLimitedUseAction { get; init; }

    /// <summary>DoD 3: a reference-only feature (its text, not calculated).</summary>
    public bool HasReferenceOnlyFeature { get; init; }
}

/// <summary>
/// M3 B1 (the Stardust Guardian acceptance) and SPEC I-05: every mechanic a character gets from given sources, in the
/// order the sheet lists its features. Pure: it reads the calculated sheet, so it states exactly what TomeStack does.
/// </summary>
public static class MechanicsInventory
{
    public static MechanicsReport Of(CharacterSheet sheet, IReadOnlySet<Guid> sources)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(sources);
        var mechanics = new List<Mechanic>();
        var limitedUse = false;
        var referenceOnly = false;
        foreach (var feature in (sheet.Features ?? []).Where(f => f.Origin.SourceId is { } s && sources.Contains(s)))
        {
            if (feature.Automation == AutomationStatus.Reference && feature.Effects.All(e => e.Automation == AutomationStatus.Reference))
                referenceOnly = true;
            if (feature.Effects.Count == 0)
            {
                mechanics.Add(new(feature.Content, feature.Name, feature.Kind, null, "text", AutomationStatus.Reference, feature.Summary, feature.Origin.SourceId, feature.Origin.Page));
                continue;
            }
            foreach (var effect in feature.Effects)
            {
                // A diagnostic on the effect (a formula that fails, a cycle) means it is not applied as written.
                var automation = effect.Automation == AutomationStatus.Automatic && feature.Diagnostics.Any(d => d.EffectId == effect.Id)
                    ? AutomationStatus.Assisted
                    : effect.Automation;
                limitedUse |= effect.Type == RollEffect.TypeName && effect.ResourceId is not null;
                mechanics.Add(new(
                    feature.Content, feature.Name, feature.Kind, effect.Id, effect.Type, automation,
                    string.IsNullOrWhiteSpace(effect.Text) ? feature.Summary : effect.Text, feature.Origin.SourceId, feature.Origin.Page));
            }
        }
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            ModifierEffect.TypeName, GrantEffect.TypeName, ResourceEffect.TypeName, ChoiceEffect.TypeName, RestrictionEffect.TypeName,
            RecoveryEffect.TypeName, RollEffect.TypeName, HitDieEffect.TypeName, ArmorEffect.TypeName, SpellcastingEffect.TypeName,
            SpellEffect.TypeName, WeaponEffect.TypeName, "text",
        };
        return new(mechanics, [.. mechanics.Where(m => !known.Contains(m.EffectType))])
        {
            HasLimitedUseAction = limitedUse,
            HasReferenceOnlyFeature = referenceOnly,
        };
    }
}
