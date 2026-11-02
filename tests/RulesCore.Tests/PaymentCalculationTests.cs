using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (MECH-X item 1, T2): a roll, a feature attack and a toggle that pay hit points or a spell slot show their
/// payment and whether it can be made now (R40: <c>Payable</c>, never <c>Available</c>); a roll that casts a spell shows the
/// spell's numbers (R38); spell entries carry tags. Original fixtures: "Fixture Payment Rite" and "Fixture Rite Spark".
/// </summary>
public class PaymentCalculationTests
{
    private const string RiteId = "6a7e0500-0000-4000-8000-000000000001";
    private const string RiteRevision = "6a7e0500-0000-4000-8000-000000000101";
    private const string SparkId = "6a7e0500-0000-4000-8000-0000000000a1";
    private const string SparkRevision = "6a7e0500-0000-4000-8000-0000000001a1";

    // Int 16 (+3), Cha 14 (+2). Level 5: proficiency +3, so spell attack +6 and save DC 14.
    private static readonly AbilityScores Scores = new(10, 12, 14, 16, 10, 14);

    private static readonly string SourceId = Fixtures.SpellPack().Sources[0].Id.ToString();

    private const string Effects =
        "{\"type\":\"resource\",\"id\":\"pool\",\"resourceId\":\"pool\",\"label\":\"Pool\",\"maximum\":\"2\"},"
        + "{\"type\":\"roll\",\"id\":\"hp\",\"rollId\":\"hp\",\"label\":\"Blood Rite\",\"dice\":\"1d6\",\"cost\":\"3\",\"payment\":{\"kind\":\"hitPoints\"}},"
        + "{\"type\":\"roll\",\"id\":\"hpz\",\"rollId\":\"hpz\",\"label\":\"Last Breath\",\"dice\":\"1d6\",\"cost\":\"3\",\"payment\":{\"kind\":\"hitPoints\",\"mayDropToZero\":true}},"
        + "{\"type\":\"roll\",\"id\":\"cast\",\"rollId\":\"cast\",\"label\":\"Cast Spark\",\"castsSpell\":\"" + SparkId + "\"},"
        + "{\"type\":\"roll\",\"id\":\"plain\",\"rollId\":\"plain\",\"label\":\"Plain\",\"dice\":\"1d6\",\"resourceId\":\"pool\",\"cost\":\"2\",\"payment\":{\"kind\":\"resource\"}},"
        + "{\"type\":\"toggle\",\"id\":\"ward\",\"toggleId\":\"ward\",\"label\":\"Warded\",\"payment\":{\"kind\":\"slot\",\"minSlotLevel\":2}},"
        + "{\"type\":\"attack\",\"id\":\"bolt\",\"attackId\":\"bolt\",\"label\":\"Bolt\",\"kind\":\"ranged\",\"abilities\":[\"int\"],\"damage\":{\"count\":\"1\",\"die\":\"6\"},\"damageType\":\"fire\",\"cost\":\"2\",\"payment\":{\"kind\":\"hitPoints\"}}";

    private static ContentRevision Rite(int version, string effects = Effects) =>
        RevisionHashProperties.Read(
            "{\"contentId\":\"" + RiteId + "\",\"revisionId\":\"" + RiteRevision + "\",\"schemaVersion\":" + version
            + ",\"kind\":\"feature\",\"name\":\"Fixture Payment Rite\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"" + SourceId
            + "\",\"page\":{\"start\":1}},\"status\":\"published\",\"effects\":[" + effects + "]}");

    private static ContentRevision Spark(int version = 12, string family = "srd-5.2.1") =>
        RevisionHashProperties.Read(
            "{\"contentId\":\"" + SparkId + "\",\"revisionId\":\"" + SparkRevision + "\",\"schemaVersion\":" + version
            + ",\"kind\":\"spell\",\"name\":\"Fixture Rite Spark\",\"rulesFamilies\":[\"" + family + "\"],\"provenance\":{\"sourceId\":\"" + SourceId
            + "\",\"page\":{\"start\":2}},\"status\":\"published\",\"effects\":[{\"type\":\"spell\",\"id\":\"s\",\"level\":2,\"lists\":[\"fixture-arcane\"],"
            + "\"attack\":\"none\",\"save\":\"dex\",\"dice\":\"2d6\",\"tags\":[\"ritualLike\"]}]}");

    private static InMemoryContentCatalog Catalog(params ContentRevision[] extra)
    {
        var (m0, m1, spells) = (Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack());
        return new([.. m0.Sources, .. m1.Sources, .. spells.Sources], [.. m0.Revisions, .. m1.Revisions, .. spells.Revisions, .. extra]);
    }

    private static Character Caster(PlayState? play = null, params ContentReference[] spells) =>
        Fixtures.Load("srd521-ash-m1.json") with
        {
            Pins = [Rite(12).Reference],
            Classes = [new ClassLevel(Fixtures.Arcanist, 5)],
            Level = 5,
            BaseAbilities = Scores,
            Spells = [.. spells.Select(s => new KnownSpell(Fixtures.Arcanist.ContentId, s))],
            Play = play ?? new PlayState(),
        };

    private static CharacterSheet Sheet(Character character, ContentRevision rite, params ContentRevision[] more) =>
        CharacterCalculator.Calculate(character, Catalog([rite, .. more]));

    private static FeatureEffect Roll(CharacterSheet sheet, string id) => sheet.Features!.SelectMany(f => f.Effects).Single(e => e.Id == id);

    [Fact]
    public void A_hit_point_roll_shows_its_payment_and_cost()
    {
        var sheet = Sheet(Caster(new PlayState { CurrentHitPoints = 10 }), Rite(12));
        Assert.True(sheet.HitPoints!.Maximum >= 10);

        var roll = Roll(sheet, "hp");
        Assert.Equal((3, null), (roll.Cost, roll.ResourceId));
        Assert.Equal(new PaymentValue(PaymentKind.HitPoints, true), roll.Payment);
        Assert.True(roll.Available);
    }

    [Fact]
    public void A_hit_point_roll_is_not_payable_at_the_edge_but_stays_available()
    {
        var atCost = Roll(Sheet(Caster(new PlayState { CurrentHitPoints = 3 }), Rite(12)), "hp");
        var below = Roll(Sheet(Caster(new PlayState { CurrentHitPoints = 2 }), Rite(12)), "hp");

        Assert.Equal(new PaymentValue(PaymentKind.HitPoints, false, "Needs more than 3 current hit points."), atCost.Payment);
        Assert.Equal((false, "Needs more than 3 current hit points."), (below.Payment!.Payable, below.Payment.Reason));
        Assert.True(atCost.Available);
        Assert.Null(atCost.UnavailableReason);
    }

    [Fact]
    public void A_roll_that_may_drop_to_zero_is_payable_at_exactly_its_cost()
    {
        var atCost = Roll(Sheet(Caster(new PlayState { CurrentHitPoints = 3 }), Rite(12)), "hpz");
        var below = Roll(Sheet(Caster(new PlayState { CurrentHitPoints = 2 }), Rite(12)), "hpz");

        Assert.Equal(new PaymentValue(PaymentKind.HitPoints, true, null, MayDropToZero: true), atCost.Payment);
        Assert.Equal(new PaymentValue(PaymentKind.HitPoints, false, "Needs at least 3 current hit points.", MayDropToZero: true), below.Payment);
        Assert.True(below.Available);
    }

    [Fact]
    public void Temporary_hit_points_do_not_pay()
    {
        var roll = Roll(Sheet(Caster(new PlayState { CurrentHitPoints = 3, TemporaryHitPoints = 20 }), Rite(12)), "hp");

        Assert.False(roll.Payment!.Payable);
    }

    [Fact]
    public void A_resource_payment_is_the_pool_as_before()
    {
        var plain = Roll(Sheet(Caster(), Rite(12)), "plain");

        Assert.Null(plain.Payment);
        Assert.Equal(("pool", 2), (plain.ResourceId, plain.Cost));
    }

    [Fact]
    public void A_slot_toggle_shows_the_slot_it_would_take()
    {
        // Arcanist 5: 4 / 3 / 1 slots. minSlotLevel 2 skips level 1.
        var toggle = Assert.Single(Sheet(Caster(), Rite(12)).Toggles!, t => t.ToggleId == "ward");

        Assert.Equal(new PaymentValue(PaymentKind.Slot, true, MinSlotLevel: 2, Slot: new SlotChoice(2, false)), toggle.Payment);
        Assert.Null(toggle.Cost);
        Assert.True(toggle.Available);
    }

    [Fact]
    public void A_slot_toggle_with_no_slot_left_is_not_payable_but_stays_available()
    {
        var play = new PlayState { SpellSlotsSpent = [new(1, 0), new(2, 3), new(3, 1)] };
        var toggle = Assert.Single(Sheet(Caster(play), Rite(12)).Toggles!, t => t.ToggleId == "ward");

        Assert.Equal(new PaymentValue(PaymentKind.Slot, false, "No spell slot of level 2 or higher is left.", MinSlotLevel: 2), toggle.Payment);
        Assert.True(toggle.Available);
        Assert.Null(toggle.UnavailableReason);
    }

    [Fact]
    public void A_hit_point_attack()
    {
        var sheet = Sheet(Caster(new PlayState { CurrentHitPoints = 10 }), Rite(12));
        var attack = Assert.Single(sheet.Attacks!, a => a.EffectId == "bolt");

        Assert.Equal((2, null), (attack.Cost, attack.ResourceId));
        Assert.Equal(new PaymentValue(PaymentKind.HitPoints, true), attack.Payment);

        var low = Assert.Single(Sheet(Caster(new PlayState { CurrentHitPoints = 2 }), Rite(12)).Attacks!, a => a.EffectId == "bolt");
        Assert.Equal((false, "Needs more than 2 current hit points."), (low.Payment!.Payable, low.Payment.Reason));
        Assert.True(low.Available);
    }

    [Fact]
    public void Casts_a_spell()
    {
        var cast = Roll(Sheet(Caster(), Rite(12), Spark()), "cast").CastsSpell;

        Assert.Equal(new CastSpellValue(Spark().Reference, "Fixture Rite Spark", 2, SpellAttackKind.None, Ability.Dex, "2d6", 6, 14), cast);
    }

    [Fact]
    public void Casts_a_spell_without_a_caster_has_no_numbers()
    {
        var character = Caster() with { Classes = [new ClassLevel(Fixtures.Warden, 3)], Level = 3 };
        var cast = Roll(Sheet(character, Rite(12), Spark()), "cast").CastsSpell!;

        Assert.Equal(("Fixture Rite Spark", 2, null, null), (cast.Name, cast.Level, cast.AttackBonus, cast.SaveDc));
    }

    [Fact]
    public void Casts_the_pinned_revision_when_the_character_pins_one()
    {
        var older = Spark() with { RevisionId = Guid.Parse("6a7e0500-0000-4000-8000-0000000001a0"), Name = "Fixture Rite Spark Old" };
        var sheet = Sheet(Caster(null, older.Reference), Rite(12), older, Spark());

        Assert.Equal(older.Reference, Roll(sheet, "cast").CastsSpell!.Spell);
    }

    [Fact]
    public void A_spell_of_the_other_rules_family_is_not_cast_and_says_why()
    {
        var sheet = Sheet(Caster(), Rite(12), Spark(family: "srd-5.1"));

        Assert.Null(Roll(sheet, "cast").CastsSpell);
        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "spells.unusable");
        Assert.Contains("supports srd-5.1, not srd-5.2.1", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Rite(12).Reference, diagnostic.Content);
        Assert.Equal("cast", diagnostic.EffectId);
    }

    [Fact]
    public void A_missing_spell_is_not_cast()
    {
        var sheet = Sheet(Caster(), Rite(12));

        Assert.Null(Roll(sheet, "cast").CastsSpell);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "spells.unusable" && d.Message.Contains("is not available", StringComparison.Ordinal));
    }

    [Fact]
    public void Spell_entries_carry_tags()
    {
        var spark = Spark();
        var sheet = Sheet(Caster(null, spark.Reference), Rite(12), spark);

        var entry = Assert.Single(sheet.Spellcasting![0].Spells, s => s.Name == "Fixture Rite Spark");
        Assert.Equal(["ritualLike"], entry.Tags);
        Assert.All(sheet.Spellcasting[0].Spells.Where(s => s.Name != "Fixture Rite Spark"), s => Assert.Null(s.Tags));
    }

    [Fact]
    public void Below_v12_nothing_changes()
    {
        // The v11 build read each key as extension data: the pool cost still applies and no payment, cast or tag shows.
        var v11 = Rite(11, Effects.Replace("\"cost\":\"3\",\"payment\":{\"kind\":\"hitPoints\"}", "\"resourceId\":\"pool\",\"cost\":\"1\",\"payment\":{\"kind\":\"hitPoints\"}", StringComparison.Ordinal));
        var spark = Spark(11);
        var sheet = Sheet(Caster(null, spark.Reference), v11, spark);

        var roll = Roll(sheet, "hp");
        Assert.Equal((null, "pool", 1), (roll.Payment, roll.ResourceId, roll.Cost));
        Assert.All(sheet.Features!.SelectMany(f => f.Effects), e => Assert.Null(e.Payment));
        Assert.Null(Roll(sheet, "cast").CastsSpell);
        Assert.All(sheet.Toggles!, t => Assert.Null(t.Payment));
        Assert.All(sheet.Attacks!, a => Assert.Null(a.Payment));
        Assert.All(sheet.Spellcasting![0].Spells, s => Assert.Null(s.Tags));
    }

    [Fact]
    public void A_revision_built_in_code_at_an_older_version_gets_nothing_from_set_properties()
    {
        var typed = Rite(12) with { SchemaVersion = 11 };
        var spark = Spark() with { SchemaVersion = 11 };
        var sheet = Sheet(Caster(null, spark.Reference), typed, spark);

        Assert.All(sheet.Features!.SelectMany(f => f.Effects), e => Assert.Null(e.Payment));
        Assert.Null(Roll(sheet, "cast").CastsSpell);
        Assert.All(sheet.Toggles!, t => Assert.Null(t.Payment));
        Assert.All(sheet.Attacks!, a => Assert.Null(a.Payment));
        Assert.All(sheet.Spellcasting![0].Spells, s => Assert.Null(s.Tags));
    }
}
