using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// D04 on the bundled SRD content: the SRD casters (srd-&lt;family&gt;-classes.json) calculate spellcasting from their
/// tables, record SRD spells, and follow the multiclass rules. Values are the SRD tables' (docs/licensing/srd-pack-review.md).
/// </summary>
public class SrdCasterTests
{
    private static readonly ContentPack Classes521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-classes.json");
    private static readonly ContentPack Spells521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-spells.json");

    private static ContentRevision Named(ContentPack pack, string name, ContentKind kind) => pack.Revisions.Single(r => r.Name == name && r.Kind == kind);

    private static ContentReference Spell(ContentPack pack, string name) => Named(pack, name, ContentKind.Spell).Reference;

    /// <summary>The revision carrying the class's spellcasting effect (its Spellcasting or Pact Magic feature).</summary>
    private static Guid Caster(ContentPack pack, string className) =>
        pack.Revisions.Single(r => r.Effects.OfType<SpellcastingEffect>().Any() && r.Effects.OfType<SpellcastingEffect>().Single().SpellList == className.ToLowerInvariant()).ContentId;

    [Fact]
    public void An_SRD_5_2_1_Wizard_5_prepares_SRD_spells_with_the_tables_slots()
    {
        using var temp = new TempApp();
        var wizard = Named(Classes521, "Wizard", ContentKind.Class);
        var caster = Caster(Classes521, "Wizard");
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Test SRD Wizard", RulesFamily = RulesFamilies.Srd521, Level = 5,
            Classes = [new(wizard.Reference, 5)],
            BaseAbilities = new(8, 14, 14, 16, 12, 10),
            Spells = [new(caster, Spell(Spells521, "Fire Bolt")), new(caster, Spell(Spells521, "Magic Missile")), new(caster, Spell(Spells521, "Fireball"))],
        };

        var sheet = temp.App.SaveCharacter(character).Sheet;

        // PB 3 + Int 3; SRD 5.2.1 Wizard Features table at level 5: 4 cantrips, 9 prepared, slots 4/3/2.
        Assert.Equal((6, 14), (sheet.Field(FieldIds.SpellAttack).Value, sheet.Field(FieldIds.SpellSaveDc).Value));
        Assert.Equal([(1, 4), (2, 3), (3, 2)], sheet.SpellSlots!.Select(s => (s.Level, s.Maximum)));
        var entry = Assert.Single(sheet.Spellcasting!);
        Assert.Equal((4, 9), (entry.CantripsAllowed!.Value, entry.SpellsAllowed!.Value));
        Assert.Empty(entry.Warnings); // all three are on the wizard list and castable
        Assert.Equal(["Fire Bolt", "Magic Missile", "Fireball"], entry.Spells.Select(s => s.Name));
        Assert.Contains(sheet.Resources!, r => r.Label == "Arcane Recovery" && r.Maximum == 1);
        Assert.Contains(sheet.Features!, f => f.Name == "Evoker" || f.Name == "Ritual Adept");
        Assert.Contains(sheet.Choices!, c => c.ChoiceId == "wizard-subclass" && !c.Resolved);
    }

    [Fact]
    public void Every_SRD_5_2_1_caster_reaches_its_tables_at_level_20()
    {
        using var temp = new TempApp();
        // Full casters reach 9th-level slots; half casters stop at 5th; Pact Magic is 4 slots of level 5.
        var expected = new Dictionary<string, int[]>
        {
            ["Bard"] = [4, 3, 3, 3, 3, 2, 2, 1, 1], ["Cleric"] = [4, 3, 3, 3, 3, 2, 2, 1, 1], ["Druid"] = [4, 3, 3, 3, 3, 2, 2, 1, 1],
            ["Sorcerer"] = [4, 3, 3, 3, 3, 2, 2, 1, 1], ["Wizard"] = [4, 3, 3, 3, 3, 2, 2, 1, 1], ["Paladin"] = [4, 3, 3, 3, 2], ["Ranger"] = [4, 3, 3, 3, 2],
        };
        foreach (var name in expected.Keys.Append("Warlock"))
        {
            var character = new Character
            {
                Id = Guid.NewGuid(), Name = $"Test {name} 20", RulesFamily = RulesFamilies.Srd521, Level = 20,
                Classes = [new(Named(Classes521, name, ContentKind.Class).Reference, 20)], BaseAbilities = new(13, 13, 13, 13, 13, 13),
            };
            var sheet = temp.App.SaveCharacter(character).Sheet;
            if (name == "Warlock")
                Assert.Equal((4, 5), (sheet.PactSlots!.Maximum, sheet.PactSlots.Level));
            else
                Assert.Equal(expected[name], sheet.SpellSlots!.Select(s => s.Maximum));
            Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("spellcasting.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SRD_5_2_1_Paladin_and_Ranger_have_two_slots_at_level_1_and_Druid_two_cantrips()
    {
        // The two table values the review found mis-transcribed or at risk (docs/licensing/srd-pack-review.md).
        SpellcastingEffect Of(string name) => Classes521.Revisions.SelectMany(r => r.Effects.OfType<SpellcastingEffect>()).Single(s => s.SpellList == name);

        Assert.Equal([2], Of("paladin").Slots[0]);
        Assert.Equal([2], Of("ranger").Slots[0]);
        Assert.Equal([2, 2, 2, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4], Of("druid").Cantrips);
        Assert.Equal(4, Of("warlock").Cantrips![9]); // 4 cantrips at Warlock level 10
    }

    private static readonly ContentPack Classes51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-classes.json");
    private static readonly ContentPack Spells51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-spells.json");

    private static CharacterSheet Single(TempApp temp, string family, ContentPack classes, string className, int level, AbilityScores scores, params KnownSpell[] spells) =>
        temp.App.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = $"Test {className} {level}", RulesFamily = family, Level = level,
            Classes = [new(Named(classes, className, ContentKind.Class).Reference, level)], BaseAbilities = scores, Spells = spells,
        }).Sheet;

    [Fact]
    public void Paladins_cast_from_level_2_under_2014_rules_and_level_1_under_2024_side_by_side()
    {
        using var temp = new TempApp();
        var scores = new AbilityScores(15, 10, 14, 8, 10, 16);

        var old1 = Single(temp, RulesFamilies.Srd51, Classes51, "Paladin", 1, scores);
        var new1 = Single(temp, RulesFamilies.Srd521, Classes521, "Paladin", 1, scores);
        var old2 = Single(temp, RulesFamilies.Srd51, Classes51, "Paladin", 2, scores);

        Assert.Empty(old1.Spellcasting!); // SRD 5.1: Spellcasting at 2nd level
        Assert.Empty(old1.SpellSlots!);
        Assert.Equal([(1, 2)], new1.SpellSlots!.Select(s => (s.Level, s.Maximum))); // SRD 5.2.1: 2 slots at level 1
        Assert.Equal(2, new1.Spellcasting!.Single().SpellsAllowed); // the Prepared Spells column
        Assert.Equal([(1, 2)], old2.SpellSlots!.Select(s => (s.Level, s.Maximum)));
        Assert.Equal(Math.Max(1, 3 + 1), old2.Spellcasting!.Single().SpellsAllowed); // 2014: Cha mod + half the paladin level
    }

    [Fact]
    public void A_2014_Wizard_prepares_by_formula_and_a_2024_Wizard_by_table_side_by_side()
    {
        using var temp = new TempApp();
        var scores = new AbilityScores(8, 14, 14, 16, 12, 10);

        var old5 = Single(temp, RulesFamilies.Srd51, Classes51, "Wizard", 5, scores, new KnownSpell(Caster(Classes51, "Wizard"), Spell(Spells51, "Fireball")));
        var new5 = Single(temp, RulesFamilies.Srd521, Classes521, "Wizard", 5, scores, new KnownSpell(Caster(Classes521, "Wizard"), Spell(Spells521, "Fireball")));

        Assert.Equal((8, 9), (old5.Spellcasting!.Single().SpellsAllowed!.Value, new5.Spellcasting!.Single().SpellsAllowed!.Value)); // Int 3 + 5, vs the table's 9
        Assert.Equal(old5.SpellSlots!.Select(s => s.Maximum), new5.SpellSlots!.Select(s => s.Maximum)); // full caster slots match (4/3/2)
        Assert.Empty(old5.Spellcasting!.Single().Warnings);
        Assert.Equal("3rd-level evocation", Named(Spells51, "Fireball", ContentKind.Spell).Summary);
    }

    [Fact]
    public void Known_casters_have_fixed_spell_counts_under_2014_rules()
    {
        using var temp = new TempApp();
        var bard = Single(temp, RulesFamilies.Srd51, Classes51, "Bard", 10, new(8, 14, 12, 10, 10, 16)).Spellcasting!.Single();
        var sorcerer = Single(temp, RulesFamilies.Srd51, Classes51, "Sorcerer", 1, new(8, 14, 14, 10, 10, 16)).Spellcasting!.Single();

        Assert.Equal((SpellPreparation.Known, 4, 14), (bard.Preparation, bard.CantripsAllowed!.Value, bard.SpellsAllowed!.Value)); // the Bard table at 10th level
        Assert.Equal((4, 2), (sorcerer.CantripsAllowed!.Value, sorcerer.SpellsAllowed!.Value));
    }

    [Fact]
    public void Every_SRD_5_1_caster_reaches_its_tables_at_level_20()
    {
        using var temp = new TempApp();
        foreach (var name in new[] { "Bard", "Cleric", "Druid", "Sorcerer", "Wizard", "Paladin", "Ranger", "Warlock" })
        {
            var sheet = Single(temp, RulesFamilies.Srd51, Classes51, name, 20, new(13, 13, 13, 13, 13, 13));
            if (name == "Warlock")
                Assert.Equal((4, 5), (sheet.PactSlots!.Maximum, sheet.PactSlots.Level));
            else
                Assert.Equal(name is "Paladin" or "Ranger" ? [4, 3, 3, 3, 2] : new[] { 4, 3, 3, 3, 3, 2, 2, 1, 1 }, sheet.SpellSlots!.Select(s => s.Maximum));
            Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("spellcasting.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_2014_Wizard_is_proficient_with_daggers_and_a_2024_Wizard_with_simple_weapons()
    {
        using var temp = new TempApp();
        var eq51 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.1-equipment.json");
        var eq521 = TomeStackApp.LoadBundledPack("TomeStack.Content.srd-5.2.1-equipment.json");
        CharacterSheet With(string family, ContentPack classes, ContentPack equipment, string weapon) => temp.App.SaveCharacter(new Character
        {
            Id = Guid.NewGuid(), Name = "Test Armed Wizard", RulesFamily = family, Level = 1,
            Classes = [new(Named(classes, "Wizard", ContentKind.Class).Reference, 1)], BaseAbilities = new(10, 14, 12, 16, 10, 10),
            Equipment = [new(Named(equipment, weapon, ContentKind.Item).Reference, Equipped: true)],
        }).Sheet;

        Assert.True(With(RulesFamilies.Srd51, Classes51, eq51, "Dagger").Attacks!.Single().Proficient); // "Daggers, darts, slings, quarterstaffs, light crossbows"
        Assert.False(With(RulesFamilies.Srd51, Classes51, eq51, "Mace").Attacks!.Single().Proficient);
        Assert.True(With(RulesFamilies.Srd521, Classes521, eq521, "Mace").Attacks!.Single().Proficient); // 2024: Simple weapons
    }

    [Fact]
    public void An_SRD_multiclass_needs_the_primary_abilities_and_takes_only_the_multiclass_subset()
    {
        using var temp = new TempApp();
        var sorcerer = Named(Classes521, "Sorcerer", ContentKind.Class).Reference;
        var paladin = Named(Classes521, "Paladin", ContentKind.Class).Reference;
        Character Make(AbilityScores scores) => new()
        {
            Id = Guid.NewGuid(), Name = "Test Sorcerer Paladin", RulesFamily = RulesFamilies.Srd521, Level = 5,
            Classes = [new(sorcerer, 3), new(paladin, 2)], BaseAbilities = scores,
        };

        var weak = temp.App.SaveCharacter(Make(new(10, 12, 14, 10, 10, 16))).Sheet; // Str 10: Paladin needs Str 13 and Cha 13
        var strong = temp.App.SaveCharacter(Make(new(13, 12, 14, 10, 10, 16))).Sheet;

        Assert.Contains(weak.Diagnostics, d => d.Code == "restriction.multiclass-unmet" && d.Content == paladin);
        Assert.DoesNotContain(strong.Diagnostics, d => d.Code == "restriction.multiclass-unmet");
        // Saving throws come from the starting class only (Sorcerer: Con and Cha), and the slots of two casters are a manual step.
        Assert.Equal(2 + 3, strong.Field(FieldIds.Save(Ability.Con)).Value); // Con +2, PB 3 (Sorcerer save)
        Assert.Equal(0, strong.Field(FieldIds.Save(Ability.Wis)).Value); // Wis +0: no Paladin save proficiency as a later class
        Assert.Equal(AutomationStatus.Assisted, strong.Field(FieldIds.SpellSlots(1)).Automation);
        Assert.Equal(["Spellcasting", "Spellcasting"], strong.Spellcasting!.Select(s => s.Name));
    }
}
