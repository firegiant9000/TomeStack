using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M3 C3 (D04's M3 part; content schema v7): the spell slots of several casters combine on the SRD Multiclass Spellcaster
/// table when each caster says how its levels count (<see cref="SpellcastingEffect.MulticlassCaster"/>). The rounding is
/// rules-family policy; Pact Magic stays separate. Original fixtures (<c>fixture-pack-m3-multiclass.json</c>): their own
/// tables are invented and unlike the SRD table, so a match proves the policy table was used.
/// </summary>
public class MulticlassSpellSlotTests
{
    private static Character Caster(string family, params ClassLevel[] classes) =>
        Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = classes,
            Level = classes.Sum(c => c.Level),
            BaseAbilities = new(10, 12, 14, 16, 14, 14),
            Spells = [],
        };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.AllCatalog());

    private static int[] Slots(CharacterSheet sheet) => [.. Enumerable.Range(1, 9).Select(l => sheet.Field(FieldIds.SpellSlots(l)).Value)];

    [Fact]
    public void A_full_and_a_half_caster_combine_with_the_familys_rounding_side_by_side()
    {
        ClassLevel[] classes = [new(Fixtures.Loremaster, 3), new(Fixtures.Wayfinder, 5)];

        var old = Sheet(Caster(RulesFamilies.Srd51, classes));
        var current = Sheet(Caster(RulesFamilies.Srd521, classes));

        // 3 + floor(5 / 2) = 5 under 2014 rules; 3 + ceil(5 / 2) = 6 under 2024 rules (Multiclass Spellcaster table).
        Assert.Equal([4, 3, 2, 0, 0, 0, 0, 0, 0], Slots(old));
        Assert.Equal([4, 3, 3, 0, 0, 0, 0, 0, 0], Slots(current));
        foreach (var sheet in new[] { old, current })
        {
            var field = sheet.Field(FieldIds.SpellSlots(1));
            Assert.Equal(AutomationStatus.Automatic, field.Automation);
            Assert.Empty(field.Warnings);
            var steps = field.Trace.Where(t => t.Field == FieldIds.SpellSlots(1)).ToList();
            Assert.Equal([Fixtures.Loremaster, Fixtures.Wayfinder], steps.Where(t => t.Origin.Kind == TraceOriginKind.Content).Select(t => t.Origin.Content!));
            Assert.Equal(TraceOriginKind.RulesPolicy, steps[^1].Origin.Kind);
        }
        Assert.Equal(5, old.Field(FieldIds.SpellSlots(1)).Trace.Last(t => t.Field == FieldIds.SpellSlots(1)).Amount);
        Assert.Equal(6, current.Field(FieldIds.SpellSlots(1)).Trace.Last(t => t.Field == FieldIds.SpellSlots(1)).Amount);
    }

    [Fact]
    public void A_third_caster_counts_a_third_rounded_down_under_both_families()
    {
        foreach (var family in new[] { RulesFamilies.Srd51, RulesFamilies.Srd521 })
        {
            // floor(7 / 3) = 2, + 1 = caster level 3: four level 1 slots and two level 2 slots.
            var sheet = Sheet(Caster(family, new(Fixtures.Runeblade, 7), new(Fixtures.Loremaster, 1)));
            Assert.Equal([4, 2, 0, 0, 0, 0, 0, 0, 0], Slots(sheet));
        }
    }

    [Fact]
    public void Two_half_casters_at_level_1_have_no_slots_under_2014_rules_and_a_caster_level_of_2_under_2024()
    {
        ClassLevel[] classes = [new(Fixtures.Wayfinder, 1), new(Fixtures.Runeblade, 2)];

        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0], Slots(Sheet(Caster(RulesFamilies.Srd51, classes)))); // 0 + 0
        // 2024: ceil(1 / 2) = 1, plus floor(2 / 3) = 0: caster level 1, two level 1 slots.
        Assert.Equal([2, 0, 0, 0, 0, 0, 0, 0, 0], Slots(Sheet(Caster(RulesFamilies.Srd521, classes))));
    }

    [Fact]
    public void A_single_caster_keeps_its_own_table_and_Pact_Magic_is_never_combined()
    {
        var single = Sheet(Caster(RulesFamilies.Srd521, new ClassLevel(Fixtures.Loremaster, 4)));
        Assert.Equal([5, 0, 0, 0, 0, 0, 0, 0, 0], Slots(single)); // its invented table, not the multiclass one

        var withPact = Sheet(Caster(RulesFamilies.Srd521, new(Fixtures.Hexwright, 3), new(Fixtures.Loremaster, 2), new(Fixtures.Wayfinder, 2)));
        Assert.Equal((2, 2), (withPact.PactSlots!.Maximum, withPact.PactSlots.Level)); // the Hexwright's own pool
        Assert.Equal([4, 2, 0, 0, 0, 0, 0, 0, 0], Slots(withPact)); // 2 + ceil(2 / 2) = 3; the Hexwright adds nothing
    }

    [Fact]
    public void A_caster_that_does_not_say_how_it_combines_keeps_the_manual_step()
    {
        var sheet = Sheet(Caster(RulesFamilies.Srd521, new(Fixtures.Arcanist, 3), new(Fixtures.Loremaster, 2)));

        var slots = sheet.Field(FieldIds.SpellSlots(1));
        Assert.Equal((4, AutomationStatus.Assisted), (slots.Value, slots.Automation)); // the Arcanist's own table
        Assert.Contains(slots.Warnings, w => w.Code == "spellcasting.multiclass-slots");
    }

    [Fact]
    public void Validation_refuses_multiclassCaster_below_v7_and_on_Pact_Magic()
    {
        var pack = Fixtures.MulticlassPack();
        var catalog = Fixtures.AllCatalog();
        var loremaster = pack.Revisions.Single(r => r.Reference == Fixtures.Loremaster);

        Assert.Empty(ContentValidator.Validate(loremaster, catalog).Errors);
        Assert.Contains(ContentValidator.Validate(loremaster with { SchemaVersion = 6 }, catalog).Errors, p => p.Code == "validate.requires-v7");

        var hexwright = pack.Revisions.Single(r => r.Reference == Fixtures.Hexwright);
        var pactWithKind = hexwright with
        {
            Effects = [.. hexwright.Effects.Select(e => e is SpellcastingEffect s ? s with { MulticlassCaster = MulticlassCaster.Full } : e)],
        };
        Assert.Contains(ContentValidator.Validate(pactWithKind, catalog).Errors, p => p.Code == "validate.spellcasting-multiclass-pact");
    }

    [Fact]
    public void An_older_spellcasting_revision_serializes_without_the_new_field()
    {
        // The field is nullable and absent by default (WhenWritingNull), so no stored v5 or v6 revision changes its hash.
        var arcanist = Fixtures.SpellPack().Revisions.Single(r => r.Reference == Fixtures.Arcanist);
        Assert.DoesNotContain("multiclassCaster", JsonSerializer.Serialize(arcanist, RulesJson.Options), StringComparison.Ordinal);
        Assert.Contains("\"multiclassCaster\":\"full\"", JsonSerializer.Serialize(Fixtures.MulticlassPack().Revisions[0], RulesJson.Compact), StringComparison.Ordinal);
    }
}
