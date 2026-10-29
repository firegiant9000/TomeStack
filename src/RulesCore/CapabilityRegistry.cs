using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace TomeStack.RulesCore;

/// <summary>
/// ADR-020, D66: what the engine can do with one effect type (<paramref name="Field"/> null) or with one member of it. The
/// <paramref name="Ceiling"/> is the highest automation the engine delivers; a field row narrows it when that member is
/// present. <paramref name="Evidence"/> names the test class that proves it, which a test checks exists.
/// </summary>
public sealed record Capability(string EffectType, string? Field, AutomationStatus Ceiling, string Reason, string Evidence);

/// <summary>
/// The automation an effect really gets: the author's value lowered by the engine's ceiling (and by a diagnostic).
/// <paramref name="Unsupported"/> is the fourth state, outside <see cref="AutomationStatus"/>: the type is not in the registry,
/// so <paramref name="Status"/> and the rest are null.
/// </summary>
public sealed record EffectiveAutomation(AutomationStatus? Status, bool Unsupported, AutomationStatus? Ceiling, string? Reason, string? Evidence);

/// <summary>
/// ADR-020 (D66): the one list of effect types the engine knows, with the most it does for each. The engine sets the ceiling;
/// the author may only lower it. Pure code: no stored change, no hash change, and the calculator still reads the author's
/// value (only the mechanics report and the coverage view read the effective one).
/// </summary>
public static class CapabilityRegistry
{
    /// <summary>The pseudo-type of a feature with no effects: its text only.</summary>
    public const string TextType = "text";

    public static IReadOnlyList<Capability> Entries { get; } =
    [
        new(ModifierEffect.TypeName, null, AutomationStatus.Automatic, "Applied by the calculator: bonus, set and replace on a derived field", "MechanicsInventoryTests"),
        new(GrantEffect.TypeName, null, AutomationStatus.Automatic, "Proficiencies, expertise, content and damage defences calculate", "DamageDefenceTests"),
        new(HitDieEffect.TypeName, null, AutomationStatus.Automatic, "Hit points per class level calculate", "ClassLevelTests"),
        new(ResourceEffect.TypeName, null, AutomationStatus.Automatic, "The maximum is calculated and the player spends it", "ResourceAndFeatureTests"),
        new(UpkeepEffect.TypeName, null, AutomationStatus.Automatic, "Previewed and confirmed by a turn command, never applied by calculation", "UpkeepContentTests"),
        new(AttackEffect.TypeName, null, AutomationStatus.Automatic, "To hit and damage calculate for the character", "FeatureAttackTests"),
        new(AttackRiderEffect.TypeName, null, AutomationStatus.Automatic, "Adds to the attacks that carry its tags", "FeatureAttackContentTests"),
        new(ChoiceEffect.TypeName, null, AutomationStatus.Automatic, "Picked by the player; the selection applies", "ChoiceTests"),
        new(RestrictionEffect.TypeName, null, AutomationStatus.Automatic, "A prerequisite the calculator validates", "RestrictionTests"),
        new(RecoveryEffect.TypeName, null, AutomationStatus.Automatic, "Previewed and confirmed by a rest command", "RestPlannerTests"),
        new(RollEffect.TypeName, null, AutomationStatus.Automatic, "Rolled by the sheet; a named resource is spent by the action command", "ChargingContentTests"),
        new(ValueEffect.TypeName, null, AutomationStatus.Automatic, "A named formula other formulas read", "NamedValueTests"),
        new(RollModeEffect.TypeName, null, AutomationStatus.Automatic, "Advantage or disadvantage on a save, skill or ability check", "RollModeTests"),
        new(RollModeEffect.TypeName, "against", AutomationStatus.Assisted, "Listed with its conditions; the player applies it and it never changes the automatic mode", "RollModeTests"),
        new(ResponseEffect.TypeName, null, AutomationStatus.Automatic, "Offered when damage is taken; the player chooses it and the outcomes apply", "DamageResponseTests"),
        new(ToggleEffect.TypeName, null, AutomationStatus.Automatic, "Gates its own effects", "ToggleAndCostTests"),
        new(ToggleGroupEffect.TypeName, null, AutomationStatus.Automatic, "Enforces which toggles may be on together", "ToggleGroupTests"),
        new(ScaleEffect.TypeName, null, AutomationStatus.Automatic, "A class table column that formulas read", "MechanicsInventoryTests"),
        new(ItemEffect.TypeName, null, AutomationStatus.Automatic, "Charges and attunement are tracked per equipment entry", "MechanicsInventoryTests"),
        new(WeaponEffect.TypeName, null, AutomationStatus.Automatic, "Attack and damage calculate", "MulticlassAndAttackTests"),
        new(ArmorEffect.TypeName, null, AutomationStatus.Automatic, "Armor class calculates", "ClassLevelTests"),
        new(SpellcastingEffect.TypeName, null, AutomationStatus.Automatic, "Spell attack, save DC and slots calculate", "SpellcastingTests"),
        new(SpellEffect.TypeName, null, AutomationStatus.Automatic, "Known and prepared spells are listed", "SpellcastingTests"),
        new(TextType, null, AutomationStatus.Reference, "Text only: nothing to calculate", "ResourceAndFeatureTests"),
    ];

    private static readonly Dictionary<string, Capability> TypeWide =
        Entries.Where(e => e.Field is null).ToDictionary(e => e.EffectType, StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<(Type Type, string Field), PropertyInfo?> Members = new();

    private const string UntypedReason = "This build kept the rule untyped (stored below the version that introduced its type, or its body does not fit), so it ignores it: text only";

    public static bool IsRegistered(string effectType) => effectType is not null && TypeWide.ContainsKey(effectType);

    /// <summary>The highest automation the engine delivers for <paramref name="effect"/>, or null when its type is not registered.</summary>
    public static AutomationStatus? Ceiling(Effect effect) => Binding(effect)?.Ceiling;

    /// <summary>The author's value lowered by the ceiling and, when the effect has a diagnostic, to Assisted.</summary>
    public static EffectiveAutomation Effective(Effect effect, AutomationStatus author, bool hasDiagnostic)
    {
        if (Binding(effect) is not { } binding)
            return new(null, true, null, null, null);
        var status = Rank(author) <= Rank(binding.Ceiling) ? author : binding.Ceiling;
        if (hasDiagnostic && status == AutomationStatus.Automatic)
            status = AutomationStatus.Assisted;
        return new(status, false, binding.Ceiling, binding.Reason, binding.Evidence);
    }

    /// <summary>The entry that sets the ceiling: the lowest applicable one, a field row before the type-wide row on a tie.</summary>
    private static Capability? Binding(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (!TypeWide.TryGetValue(effect.Type, out var typeWide))
            return null;
        // R7: a registered type the build kept untyped (a versioned type stored in a revision below its version gate, or a body that
        // does not fit) is ignored by the engine, so nothing beyond its text is delivered, whatever its members say.
        if (effect is UnknownEffect && typeWide.Ceiling != AutomationStatus.Reference)
            return typeWide with { Ceiling = AutomationStatus.Reference, Reason = UntypedReason };
        var binding = typeWide;
        foreach (var entry in Entries)
        {
            if (entry.Field is not null && entry.EffectType == effect.Type && Rank(entry.Ceiling) <= Rank(binding.Ceiling) && IsPresent(effect, entry.Field))
                binding = entry;
        }
        return binding;
    }

    /// <summary>Reference &lt; Assisted &lt; Automatic. The enum's own order is the other way round, so it is not compared directly.</summary>
    private static int Rank(AutomationStatus status) => status switch
    {
        AutomationStatus.Reference => 0,
        AutomationStatus.Assisted => 1,
        _ => 2,
    };

    /// <summary>A member is present when its JSON property is on the effect and not null.</summary>
    private static bool IsPresent(Effect effect, string field)
    {
        var property = Members.GetOrAdd((effect.GetType(), field), key =>
            key.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => string.Equals(JsonNamingPolicy.CamelCase.ConvertName(p.Name), key.Field, StringComparison.Ordinal)));
        return property?.GetValue(effect) is not null;
    }
}
