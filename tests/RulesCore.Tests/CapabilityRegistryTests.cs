using System.Reflection;
using System.Text.Json;

namespace TomeStack.RulesCore.Tests;

/// <summary>ADR-020, D66: the capability registry lists every effect type the engine knows, and the author can only lower a ceiling.</summary>
public class CapabilityRegistryTests
{
    /// <summary>Every effect type the deserializer can produce, read from the code: the versioned ones, the directly typed ones, and text.</summary>
    private static IEnumerable<string> AllEffectTypes()
    {
        var core = typeof(Effect).Assembly;
        var versioned = (System.Collections.IDictionary)core.GetType("TomeStack.RulesCore.VersionedEffects")!
            .GetField("ByName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var fromKeys = versioned.Keys.Cast<string>();
        // Each typed effect record names itself with a TypeName constant, so a new record is found here even before it joins ByName.
        var fromRecords = core.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Effect)) && !t.IsAbstract)
            .Select(t => t.GetField("TypeName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string)
            .OfType<string>();
        return fromKeys.Concat(fromRecords).Append("text").Distinct(StringComparer.Ordinal);
    }

    private static Effect Read(string json) => JsonSerializer.Deserialize<Effect>(json)!;

    [Fact]
    public void Every_typed_effect_type_is_registered()
    {
        var types = AllEffectTypes().ToList();
        Assert.True(types.Count >= 23, $"expected the 22 effect types and text, found {types.Count}");
        Assert.All(types, t => Assert.True(CapabilityRegistry.IsRegistered(t), $"effect type '{t}' has no row in CapabilityRegistry"));
    }

    [Fact]
    public void Every_registered_type_is_a_type_the_engine_can_produce()
    {
        var known = AllEffectTypes().ToHashSet(StringComparer.Ordinal);
        Assert.All(CapabilityRegistry.Entries.Select(e => e.EffectType).Distinct(), t => Assert.Contains(t, known));
    }

    [Fact]
    public void Field_entries_name_real_json_members()
    {
        var records = typeof(Effect).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Effect)) && !t.IsAbstract && t.GetField("TypeName", BindingFlags.Public | BindingFlags.Static) is not null)
            .ToDictionary(t => (string)t.GetField("TypeName", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!, StringComparer.Ordinal);
        foreach (var entry in CapabilityRegistry.Entries.Where(e => e.Field is not null))
        {
            var record = Assert.Contains(entry.EffectType, records);
            Assert.Contains(record.GetProperties(), p => JsonNamingPolicy.CamelCase.ConvertName(p.Name) == entry.Field);
        }
    }

    [Fact]
    public void Each_type_has_exactly_one_type_wide_entry_and_no_blank_reason()
    {
        Assert.Equal(CapabilityRegistry.Entries.Count(e => e.Field is null), CapabilityRegistry.Entries.Where(e => e.Field is null).Select(e => e.EffectType).Distinct().Count());
        Assert.All(CapabilityRegistry.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Reason));
            Assert.False(string.IsNullOrWhiteSpace(e.Evidence));
        });
    }

    private static readonly ModifierEffect Modifier = new() { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" };

    [Theory]
    [InlineData(AutomationStatus.Automatic, false, AutomationStatus.Automatic)]
    [InlineData(AutomationStatus.Assisted, false, AutomationStatus.Assisted)]
    [InlineData(AutomationStatus.Reference, false, AutomationStatus.Reference)]
    [InlineData(AutomationStatus.Automatic, true, AutomationStatus.Assisted)]
    [InlineData(AutomationStatus.Reference, true, AutomationStatus.Reference)]
    public void Effective_is_the_minimum_of_author_and_ceilings(AutomationStatus author, bool diagnostic, AutomationStatus expected)
    {
        var effective = CapabilityRegistry.Effective(Modifier, author, diagnostic);

        Assert.Equal((expected, false), (effective.Status, effective.Unsupported));
        Assert.Equal(AutomationStatus.Automatic, effective.Ceiling);
    }

    [Fact]
    public void A_field_that_is_present_lowers_the_ceiling_and_the_author_cannot_lower_it_further_than_they_choose()
    {
        var plain = new RollModeEffect { Id = "r", Target = "save.wis", Mode = RollMode.Advantage };
        var against = plain with { Against = ["frightened"] };

        Assert.Equal(AutomationStatus.Automatic, CapabilityRegistry.Ceiling(plain));
        Assert.Equal(AutomationStatus.Assisted, CapabilityRegistry.Ceiling(against));
        var effective = CapabilityRegistry.Effective(against, AutomationStatus.Automatic, hasDiagnostic: false);
        Assert.Equal((AutomationStatus.Assisted, "RollModeTests"), (effective.Status, effective.Evidence));
        Assert.Equal(AutomationStatus.Reference, CapabilityRegistry.Effective(against, AutomationStatus.Reference, false).Status);
    }

    [Fact]
    public void A_registered_type_the_build_kept_untyped_is_reference_only_whatever_its_members()
    {
        // R7: a rollMode in a revision below v11 stays an UnknownEffect, which the engine ignores. Its type is registered (so it is
        // not unsupported), but nothing is delivered: the ceiling is Reference, with or without a member that would lower a typed one.
        var withAgainst = Read("""{"type":"rollMode","id":"r","target":"save.wis","mode":"advantage","against":["frightened"]}""");
        var plain = Read("""{"type":"rollMode","id":"r","target":"save.wis","mode":"advantage","against":null}""");

        Assert.IsType<UnknownEffect>(withAgainst);
        Assert.IsType<UnknownEffect>(plain);
        foreach (var untyped in new[] { withAgainst, plain })
        {
            Assert.Equal(AutomationStatus.Reference, CapabilityRegistry.Ceiling(untyped));
            var effective = CapabilityRegistry.Effective(untyped, AutomationStatus.Automatic, hasDiagnostic: false);
            Assert.Equal((AutomationStatus.Reference, false, AutomationStatus.Reference), (effective.Status, effective.Unsupported, effective.Ceiling));
        }
    }

    [Fact]
    public void A_roll_that_names_a_resource_is_not_lowered_because_the_action_command_spends_it()
    {
        // D43 slice 5: the sheet's action command spends a roll's resource (RollDetailsOf gives it a cost), so design 4.4's
        // "roll + resourceId = Assisted" row does not describe this build.
        var roll = new RollEffect { Id = "r", RollId = "r", Label = "R", Dice = "1d6", ResourceId = "pool" };

        Assert.Equal(AutomationStatus.Automatic, CapabilityRegistry.Effective(roll, AutomationStatus.Automatic, false).Status);
    }

    [Fact]
    public void An_unregistered_type_is_unsupported_with_no_status()
    {
        var unknown = Read("""{"type":"fixture-unknown","id":"u"}""");

        var effective = CapabilityRegistry.Effective(unknown, AutomationStatus.Automatic, hasDiagnostic: false);

        Assert.True(effective.Unsupported);
        Assert.Null(effective.Status);
        Assert.Null(effective.Ceiling);
        Assert.Null(CapabilityRegistry.Ceiling(unknown));
        Assert.False(CapabilityRegistry.IsRegistered("fixture-unknown"));
    }

    [Fact]
    public void Author_cannot_raise_above_the_ceiling()
    {
        var text = Read("""{"type":"text","id":"t"}""");

        Assert.Equal(AutomationStatus.Reference, CapabilityRegistry.Effective(text, AutomationStatus.Automatic, false).Status);
        Assert.True(CapabilityRegistry.IsRegistered("text"));
    }

    // ---- helpers the per-type evidence tests share ---------------------------------------------------------------------

    /// <summary>
    /// What each evidence test for a type with no other ceiling test asserts: the sheet lists a mechanic of this type as Automatic,
    /// it is not reported unsupported, and the registry's ceiling for the content's own effect is Automatic.
    /// </summary>
    internal static void AssertAutomaticallyDelivered(MechanicsReport report, ContentRevision content, string effectType, string effectId)
    {
        Assert.Empty(report.Unsupported);
        var mechanic = Assert.Single(report.Mechanics, m => m.EffectType == effectType && m.EffectId == effectId);
        Assert.Equal(AutomationStatus.Automatic, mechanic.Automation);
        var effect = Assert.Single(content.Effects, e => e.Type == effectType && e.Id == effectId);
        Assert.Equal(AutomationStatus.Automatic, CapabilityRegistry.Ceiling(effect));
    }
}
