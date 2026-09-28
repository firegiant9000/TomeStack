using System.Text.Json;
using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// D04 (M2 spellcasting, content schema v5, character schema v6): one caster calculates its spell attack bonus, save DC,
/// slots by class level and its prepared or known spells; a second caster is calculated separately and its slots are a
/// manual step. Original fixtures (<c>fixture-pack-m2-spells.json</c>) with invented tables.
/// </summary>
public class SpellcastingTests
{
    // Int 16 (+3), Cha 14 (+2), Wis 10.
    private static readonly AbilityScores Scores = new(10, 12, 14, 16, 10, 14);

    private static Character Caster(string family, IReadOnlyList<KnownSpell>? spells = null, PlayState? play = null, params ClassLevel[] classes) =>
        Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = classes,
            Level = classes.Sum(c => c.Level),
            BaseAbilities = Scores,
            Spells = spells ?? [],
            Play = play ?? new PlayState(),
        };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Fixtures.SpellCatalog());

    private static KnownSpell Arcane(ContentReference spell, bool prepared = true) => new(Fixtures.Arcanist.ContentId, spell, prepared);

    [Fact]
    public void A_prepared_caster_has_attack_save_dc_slots_and_spells_with_traces()
    {
        var sheet = Sheet(Caster(RulesFamilies.Srd521, [Arcane(Fixtures.Spark), Arcane(Fixtures.FrostRing), Arcane(Fixtures.Veil, prepared: false), Arcane(Fixtures.EmberWave)], null, new ClassLevel(Fixtures.Arcanist, 5)));

        // PB 3 at level 5, Int +3.
        var attack = sheet.Field(FieldIds.SpellAttack);
        Assert.Equal((6, AutomationStatus.Automatic), (attack.Value, attack.Automation));
        Assert.Equal(14, sheet.Field(FieldIds.SpellSaveDc).Value);
        var step = attack.Trace.Single(t => t.Field == FieldIds.SpellAttack);
        Assert.Equal((Fixtures.Arcanist, "TomeStack Fixtures: Spellcasting", new PageRef(1)), (step.Origin.Content!, step.Origin.SourceTitle!, step.Origin.Page!));
        Assert.Contains(step.Inputs!, i => i.Name == "ability.int.mod" && i.Value == 3);

        // Invented table at class level 5: 4 / 3 / 1.
        Assert.Equal([4, 3, 1, 0, 0, 0, 0, 0, 0], Enumerable.Range(1, 9).Select(l => sheet.Field(FieldIds.SpellSlots(l)).Value));
        Assert.Equal([(1, 4, 4), (2, 3, 3), (3, 1, 1)], sheet.SpellSlots!.Select(s => (s.Level, s.Maximum, s.Remaining)));
        Assert.Null(sheet.PactSlots);

        var entry = Assert.Single(sheet.Spellcasting!);
        Assert.Equal((true, 5, 4, 8), (entry.Primary, entry.ClassLevel, entry.CantripsAllowed!.Value, entry.SpellsAllowed!.Value)); // max(1, 3 + 5)
        Assert.Equal(
            [("Fixture Spark", 0, true), ("Fixture Frost Ring", 1, true), ("Fixture Veil", 2, false), ("Fixture Ember Wave", 3, true)],
            entry.Spells.Select(s => (s.Name, s.Level, s.Prepared)));
        Assert.Empty(entry.Warnings);
        Assert.Equal((SpellAttackKind.Ranged, "1d10"), (entry.Spells[0].Attack, entry.Spells[0].Dice));
    }

    [Fact]
    public void A_non_caster_has_no_spellcasting_and_its_spell_fields_are_zero_and_automatic()
    {
        var sheet = Sheet(Caster(RulesFamilies.Srd521, null, null, new ClassLevel(Fixtures.Warden, 3)));

        Assert.Empty(sheet.Spellcasting!);
        Assert.Empty(sheet.SpellSlots!);
        Assert.All(new[] { FieldIds.SpellAttack, FieldIds.SpellSaveDc, FieldIds.SpellSlots(1), FieldIds.PactSlots }, f =>
            Assert.Equal((0, AutomationStatus.Automatic), (sheet.Field(f).Value, sheet.Field(f).Automation)));
    }

    [Fact]
    public void Spells_off_the_list_above_the_slot_level_or_beyond_the_counts_are_flagged_not_dropped()
    {
        var sheet = Sheet(Caster(
            RulesFamilies.Srd521,
            [Arcane(Fixtures.Spark), Arcane(Fixtures.MendingWord), Arcane(Fixtures.EmberWave), Arcane(Fixtures.FrostRing)],
            null,
            new ClassLevel(Fixtures.Arcanist, 1)));

        var entry = Assert.Single(sheet.Spellcasting!);
        Assert.Equal(4, entry.Spells.Count); // every recorded spell stays visible
        Assert.Contains(entry.Warnings, w => w.Code == "spells.not-on-list" && w.Content == Fixtures.MendingWord);
        Assert.Contains(entry.Warnings, w => w.Code == "spells.level-too-high" && w.Content == Fixtures.EmberWave);
        Assert.DoesNotContain(entry.Warnings, w => w.Code == "spells.too-many"); // max(1, 3 + 1) = 4 prepared; 3 recorded

        // Int 8 at class level 1: max(1, -1 + 1) = 1 prepared spell, but 2 are prepared (the unprepared one does not count).
        var weak = Caster(RulesFamilies.Srd521, [Arcane(Fixtures.FrostRing), Arcane(Fixtures.Veil), Arcane(Fixtures.EmberWave, prepared: false)], null, new ClassLevel(Fixtures.Arcanist, 1)) with
        {
            BaseAbilities = Scores with { Int = 8 },
        };
        var tooMany = Assert.Single(Sheet(weak).Spellcasting!).Warnings.Single(w => w.Code == "spells.too-many");
        Assert.Contains("prepares 1 spell(s) at level 1; 2 are recorded", tooMany.Message, StringComparison.Ordinal);

        // 4 cantrips where 3 are allowed at level 1 (the same fixture cantrip recorded 4 times, which validation refuses).
        var cantrips = Sheet(Caster(RulesFamilies.Srd521, [.. Enumerable.Range(0, 4).Select(_ => Arcane(Fixtures.Spark))], null, new ClassLevel(Fixtures.Arcanist, 1)));
        Assert.Contains(cantrips.Spellcasting!.Single().Warnings, w => w.Code == "spells.too-many-cantrips");
    }

    [Fact]
    public void A_second_caster_is_calculated_separately_and_combined_slots_are_a_manual_step()
    {
        var character = Caster(
            RulesFamilies.Srd521,
            [Arcane(Fixtures.FrostRing), new(Fixtures.Chanter.ContentId, Fixtures.MendingWord)],
            null,
            new ClassLevel(Fixtures.Arcanist, 3), new ClassLevel(Fixtures.Chanter, 2));
        var sheet = Sheet(character);

        Assert.Equal(["Fixture Arcanist", "Fixture Chanter"], sheet.Spellcasting!.Select(s => s.Name));
        var chanter = sheet.Spellcasting![1];
        Assert.Equal((false, 3 + 2, 8 + 3 + 2), (chanter.Primary, chanter.AttackBonus, chanter.SaveDc)); // PB 3 (total level 5) + Cha 2
        var slots = sheet.Field(FieldIds.SpellSlots(1));
        Assert.Equal((4, AutomationStatus.Assisted), (slots.Value, slots.Automation)); // the Arcanist's 4, not combined
        Assert.Contains(slots.Warnings, w => w.Code == "spellcasting.multiclass-slots");
        Assert.Contains(sheet.Field(FieldIds.SpellAttack).Warnings, w => w.Code == "spellcasting.multiclass");

        // The manual step: the total recorded as an override (SPEC C-06); play uses it.
        var overridden = Sheet(character with { Overrides = [new(FieldIds.SpellSlots(1), 6, "Multiclass spellcaster table")] });
        Assert.Equal(6, overridden.SpellSlots!.Single(s => s.Level == 1).Maximum);
    }

    [Fact]
    public void Pact_magic_slots_are_their_own_pool_of_one_level()
    {
        var sheet = Sheet(Caster(RulesFamilies.Srd51, [new(Fixtures.Oathbinder.ContentId, Fixtures.Spark)], null, new ClassLevel(Fixtures.Oathbinder, 3), new ClassLevel(Fixtures.Arcanist, 1)));

        Assert.Equal((2, 2), (sheet.PactSlots!.Maximum, sheet.PactSlots.Level)); // invented: 2 slots of level 2 at 3
        Assert.Equal((2, AutomationStatus.Automatic), (sheet.Field(FieldIds.SpellSlots(1)).Value, sheet.Field(FieldIds.SpellSlots(1)).Automation));
        Assert.Equal("Fixture Oathbinder", sheet.Spellcasting![0].Name); // the first class taken is the primary caster
        Assert.Equal(2 + 2, sheet.Field(FieldIds.SpellAttack).Value); // PB 2 + Cha 2
    }

    [Fact]
    public void The_same_caster_calculates_the_same_under_both_families_side_by_side()
    {
        // No rules-family policy field is involved: 2014/2024 spellcasting differences are content (each SRD class
        // revision has its own tables), so identical content gives identical results.
        var spells = new[] { Arcane(Fixtures.Spark), Arcane(Fixtures.FrostRing) };
        var old = Sheet(Caster(RulesFamilies.Srd51, spells, null, new ClassLevel(Fixtures.Arcanist, 7)));
        var current = Sheet(Caster(RulesFamilies.Srd521, spells, null, new ClassLevel(Fixtures.Arcanist, 7)));

        string[] fields = [FieldIds.SpellAttack, FieldIds.SpellSaveDc, .. Enumerable.Range(1, 9).Select(FieldIds.SpellSlots)];
        Assert.Equal(fields.Select(f => old.Field(f).Value), fields.Select(f => current.Field(f).Value));
    }

    [Fact]
    public void Rests_restore_slots_long_rest_all_short_rest_pact_only()
    {
        var play = new PlayState().WithSlotsSpent(1, 2).WithSlotsSpent(2, 1) with { PactSlotsSpent = 1 };
        var character = Caster(RulesFamilies.Srd521, null, play, new ClassLevel(Fixtures.Arcanist, 3), new ClassLevel(Fixtures.Oathbinder, 2));
        var sheet = Sheet(character);

        var longRest = RestPlanner.LongRest(character, sheet);
        Assert.Equal(
            [(RestChangeKind.SpellSlots, 2, 4), (RestChangeKind.SpellSlots, 0, 1), (RestChangeKind.PactSlots, 1, 2)],
            longRest.Changes.Where(c => c.Kind is RestChangeKind.SpellSlots or RestChangeKind.PactSlots).Select(c => (c.Kind, c.From, c.To)));
        var rested = RestPlanner.Apply(play, longRest, new HashSet<string>());
        Assert.Equal((0, 0, 0), (rested.SlotsSpentOf(1), rested.SlotsSpentOf(2), rested.PactSlotsSpent));

        var shortRest = RestPlanner.ShortRest(character, sheet, []);
        var pact = Assert.Single(shortRest.Changes);
        Assert.Equal(RestChangeKind.PactSlots, pact.Kind);
    }

    [Fact]
    public void A_spellcasting_effect_in_a_revision_older_than_v5_stays_unknown_and_byte_for_byte()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RulesFixtures", "fixture-pack-m2-spells.json"));
        var pack = JsonSerializer.Deserialize<ContentPack>(json, RulesJson.Options)!;
        var arcanist = pack.Revisions.Single(r => r.Reference == Fixtures.Arcanist);
        Assert.IsType<SpellcastingEffect>(arcanist.Effects[1]);

        var v4 = JsonSerializer.Serialize(arcanist, RulesJson.Options).Replace("\"schemaVersion\": 5", "\"schemaVersion\": 4", StringComparison.Ordinal);
        var old = JsonSerializer.Deserialize<ContentRevision>(v4, RulesJson.Options)!;

        var unknown = Assert.IsType<UnknownEffect>(old.Effects[1]);
        Assert.Equal(AutomationStatus.Reference, unknown.Automation);
        Assert.Equal(v4, JsonSerializer.Serialize(old, RulesJson.Options));
    }

    [Fact]
    public void Validation_refuses_bad_tables_and_spellcasting_below_v5()
    {
        var catalog = Fixtures.SpellCatalog();
        var arcanist = catalog.FindRevision(Fixtures.Arcanist)!;
        var spellcasting = arcanist.Effects.OfType<SpellcastingEffect>().Single();

        var shortTable = arcanist with { RevisionId = Guid.NewGuid(), Effects = [arcanist.Effects[0], spellcasting with { Slots = spellcasting.Slots.Take(5).ToList() }] };
        var pactTwoLevels = arcanist with { RevisionId = Guid.NewGuid(), Effects = [arcanist.Effects[0], spellcasting with { SlotKind = SpellSlotKind.PactMagic }] };
        var old = arcanist with { RevisionId = Guid.NewGuid(), SchemaVersion = 4 };

        Assert.Contains(ContentValidator.Validate(shortTable, catalog).Errors, e => e.Code == "validate.spellcasting");
        Assert.Contains(ContentValidator.Validate(pactTwoLevels, catalog).Errors, e => e.Code == "validate.spellcasting");
        Assert.Contains(ContentValidator.Validate(old, catalog).Errors, e => e.Code == "validate.requires-v5");
        Assert.True(ContentValidator.Validate(arcanist, catalog).CanPublish);
        Assert.True(ContentValidator.Validate(catalog.FindRevision(Fixtures.EmberWave)!, catalog).CanPublish);
    }

    [Fact]
    public void Spells_are_references_of_the_character_but_never_active_content()
    {
        var character = Caster(RulesFamilies.Srd521, [Arcane(Fixtures.FrostRing)], null, new ClassLevel(Fixtures.Arcanist, 1));

        Assert.Contains(Fixtures.FrostRing, character.AllReferences());
        Assert.DoesNotContain(Fixtures.FrostRing, Sheet(character).Active!);
        Assert.Empty(character.Validate());
        Assert.Contains(
            (character with { Spells = [Arcane(Fixtures.FrostRing), Arcane(Fixtures.FrostRing)] }).Validate(),
            d => d.Code == "character.spell-duplicate");
    }
}
