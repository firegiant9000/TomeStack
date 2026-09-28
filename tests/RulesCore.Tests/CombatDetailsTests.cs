using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// M2.2 (content schema v8): the mechanics a Fighter needs, on original test content. The attack count (highest Extra
/// Attack wins), the critical range, a modifier while armored, armor training, the armor Strength requirement and
/// Stealth disadvantage, and a roll bonus formula. The untrained-shield difference is a policy field, tested side by side.
/// </summary>
public class CombatDetailsTests
{
    private static readonly string[] Both = [RulesFamilies.Srd51, RulesFamilies.Srd521];

    private static ContentReference Ref(int n) => new(Guid.Parse($"5fadc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5fade000-0000-4000-8000-{n:D12}"));

    private static ContentRevision Revision(int n, ContentKind kind, string name, params Effect[] effects) => new()
    {
        ContentId = Ref(n).ContentId,
        RevisionId = Ref(n).RevisionId,
        SchemaVersion = ContentRevision.CombatDetailsSchemaVersion,
        Kind = kind,
        Name = name,
        RulesFamilies = Both,
        Provenance = new(Fixtures.SourceShared, new(1)),
        Status = RevisionStatus.Published,
        Effects = effects,
    };

    private static GrantEffect Feature(string id, ContentReference content, int level) => new() { Id = id, Grant = GrantKind.Content, Content = content, Level = level };

    private static GrantEffect Training(string key, ClassEntry? onlyAs = null) => new() { Id = $"armor-{key}", Grant = GrantKind.Proficiency, Target = $"armor.{key}", OnlyAs = onlyAs };

    private static readonly ContentRevision ExtraSwing = Revision(11, ContentKind.Feature, "Test Extra Swing",
        new ModifierEffect { Id = "attacks", Operation = ModifierOperation.Set, Target = FieldIds.Attacks, Value = "2 + floor((CLASS_LEVEL + 1) / 12) + floor(CLASS_LEVEL / 20)" });

    private static readonly ContentRevision KeenEdge = Revision(12, ContentKind.Feature, "Test Keen Edge",
        new ModifierEffect { Id = "critical", Operation = ModifierOperation.Bonus, Target = FieldIds.CriticalRange, Value = "-1" });

    private static readonly ContentRevision KeenerEdge = Revision(13, ContentKind.Feature, "Test Keener Edge",
        new ModifierEffect { Id = "critical", Operation = ModifierOperation.Bonus, Target = FieldIds.CriticalRange, Value = "-1" });

    private static readonly ContentRevision GuardStyle = Revision(14, ContentKind.Feature, "Test Guard Style",
        new ModifierEffect { Id = "guard", Operation = ModifierOperation.Bonus, Target = FieldIds.ArmorClass, Value = "1", WhileArmored = true });

    private static readonly ContentRevision Rally = Revision(15, ContentKind.Feature, "Test Rally",
        new ResourceEffect { Id = "rally", ResourceId = "rally", Label = "Rally", Maximum = "1" },
        new RollEffect { Id = "rally-heal", RollId = "rally-heal", Label = "Rally healing", Dice = "1d10", Bonus = "CLASS_LEVEL", ResourceId = "rally" });

    private static readonly ContentRevision Vanguard = Revision(1, ContentKind.Class, "Test Vanguard",
        new HitDieEffect { Id = "hit-die", Die = 10 },
        Training("light"), Training("medium"), Training("shield"), Training("heavy", ClassEntry.StartingClass),
        Feature("guard", GuardStyle.Reference, 1), Feature("rally", Rally.Reference, 1), Feature("keen", KeenEdge.Reference, 3),
        Feature("extra", ExtraSwing.Reference, 5), Feature("keener", KeenerEdge.Reference, 15));

    // A second class with its own Extra Attack (set to 2): the two do not add together.
    private static readonly ContentRevision SkirmisherSwing = Revision(21, ContentKind.Feature, "Test Skirmisher Swing",
        new ModifierEffect { Id = "attacks", Operation = ModifierOperation.Set, Target = FieldIds.Attacks, Value = "2" });

    private static readonly ContentRevision Skirmisher = Revision(2, ContentKind.Class, "Test Skirmisher",
        new HitDieEffect { Id = "hit-die", Die = 8 }, Feature("swing", SkirmisherSwing.Reference, 5));

    private static readonly ContentRevision Hauberk = Revision(31, ContentKind.Item, "Test Heavy Hauberk",
        new ArmorEffect { Id = "armor", Category = ArmorCategory.Heavy, ArmorClass = 16, Strength = 13, StealthDisadvantage = true });

    private static readonly ContentRevision Coat = Revision(32, ContentKind.Item, "Test Leather Coat",
        new ArmorEffect { Id = "armor", Category = ArmorCategory.Light, ArmorClass = 11 });

    private static readonly ContentRevision Buckler = Revision(33, ContentKind.Item, "Test Buckler",
        new ArmorEffect { Id = "shield", Category = ArmorCategory.Shield, ArmorClass = 2 });

    private static InMemoryContentCatalog Catalog(params ContentRevision[] extra)
    {
        var m1 = Fixtures.M1Pack();
        return new(
            [.. Fixtures.Pack().Sources, .. m1.Sources],
            [.. Fixtures.Pack().Revisions, .. m1.Revisions, Vanguard, ExtraSwing, KeenEdge, KeenerEdge, GuardStyle, Rally, Skirmisher, SkirmisherSwing, Hauberk, Coat, Buckler, .. extra]);
    }

    // Str 12, Dex 14 (+2), Con 14.
    private static Character Fighter(string family, IReadOnlyList<EquipmentEntry>? equipment = null, params ClassLevel[] classes) =>
        Fixtures.Load(family == RulesFamilies.Srd51 ? "srd51-ash-m1.json" : "srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = classes,
            Level = classes.Sum(c => c.Level),
            BaseAbilities = new(12, 14, 14, 10, 10, 10),
            Equipment = equipment ?? [],
            Spells = [],
        };

    private static CharacterSheet Sheet(Character character) => CharacterCalculator.Calculate(character, Catalog());

    private static EquipmentEntry Worn(ContentRevision item) => new(item.Reference, Equipped: true);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(19, 3)]
    [InlineData(20, 4)]
    public void Extra_attack_sets_the_attack_count_by_class_level(int level, int attacks)
    {
        var field = Sheet(Fighter(RulesFamilies.Srd521, null, new ClassLevel(Vanguard.Reference, level))).Field(FieldIds.Attacks);

        Assert.Equal(attacks, field.Value);
        Assert.Equal(AutomationStatus.Automatic, field.Automation);
        if (level >= 5)
            Assert.Contains(field.Trace, t => t.Operation == "set" && t.Origin.Content == ExtraSwing.Reference);
    }

    [Fact]
    public void Extra_attack_from_two_classes_does_not_add_together()
    {
        // SRD 5.1 p. 57 / SRD 5.2.1 p. 25: the highest applies. Vanguard 11 gives 3; the Skirmisher's 2 is not used.
        var sheet = Sheet(Fighter(RulesFamilies.Srd521, null, new ClassLevel(Vanguard.Reference, 11), new ClassLevel(Skirmisher.Reference, 5)));
        var field = sheet.Field(FieldIds.Attacks);

        Assert.Equal(3, field.Value);
        Assert.Contains(field.Trace, t => t.Operation == "ignored" && t.Origin.Content == SkirmisherSwing.Reference);
        Assert.Equal(2, Sheet(Fighter(RulesFamilies.Srd521, null, new ClassLevel(Vanguard.Reference, 4), new ClassLevel(Skirmisher.Reference, 5))).Field(FieldIds.Attacks).Value);
    }

    [Theory]
    [InlineData(2, 20)]
    [InlineData(3, 19)]
    [InlineData(14, 19)]
    [InlineData(15, 18)]
    public void The_critical_range_drops_with_each_feature(int level, int critical)
    {
        var field = Sheet(Fighter(RulesFamilies.Srd51, null, new ClassLevel(Vanguard.Reference, level))).Field(FieldIds.CriticalRange);
        Assert.Equal(critical, field.Value);
        Assert.Equal(1 + (20 - critical), field.Trace.Count(t => t.Field == FieldIds.CriticalRange)); // base, then one step each
    }

    [Fact]
    public void A_while_armored_modifier_needs_body_armor_not_just_a_shield()
    {
        var level1 = new ClassLevel(Vanguard.Reference, 1);
        var bare = Sheet(Fighter(RulesFamilies.Srd521, null, level1)).Field(FieldIds.ArmorClass);
        var shieldOnly = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Buckler)], level1)).Field(FieldIds.ArmorClass);
        var coat = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Coat)], level1)).Field(FieldIds.ArmorClass);

        Assert.Equal(10 + 2, bare.Value); // 10 + Dex, no Guard Style
        Assert.Contains(bare.Trace, t => t.Operation == "ignored" && t.Origin.Content == GuardStyle.Reference && t.Description.Contains("only while armor is worn", StringComparison.Ordinal));
        Assert.Equal(10 + 2 + 2, shieldOnly.Value); // a shield is not armor for this
        Assert.Equal(11 + 2 + 1, coat.Value); // light armor + Dex, and Guard Style
        Assert.Contains(coat.Trace, t => t.Operation == "add" && t.Origin.Content == GuardStyle.Reference);
    }

    [Fact]
    public void Armor_strength_and_stealth_warn_and_armor_class_still_counts()
    {
        var sheet = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk)], new ClassLevel(Vanguard.Reference, 1)));

        var ac = sheet.Field(FieldIds.ArmorClass);
        Assert.Equal(16 + 1, ac.Value); // heavy: no Dex; Guard Style +1
        var strength = Assert.Single(ac.Warnings, w => w.Code == "equipment.armor-strength");
        Assert.Contains("needs Strength 13; with Strength 12", strength.Message, StringComparison.Ordinal);
        Assert.Contains(sheet.Field(FieldIds.Skill(CharacterCalculator.Stealth)).Warnings, w => w.Code == "equipment.stealth-disadvantage");

        var strong = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk)], new ClassLevel(Vanguard.Reference, 1)) with { BaseAbilities = new(13, 14, 14, 10, 10, 10) });
        Assert.DoesNotContain(strong.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-strength");
    }

    [Fact]
    public void Missing_armor_training_warns_only_when_the_character_records_some()
    {
        // A later-class Vanguard has no heavy armor training (onlyAs startingClass).
        var later = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk)], new ClassLevel(Skirmisher.Reference, 1), new ClassLevel(Vanguard.Reference, 1)));
        Assert.Contains(later.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained" && w.Message.Contains("heavy armor", StringComparison.Ordinal));

        var starting = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk)], new ClassLevel(Vanguard.Reference, 1), new ClassLevel(Skirmisher.Reference, 1)));
        Assert.DoesNotContain(starting.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained");

        // Content that records no armor training at all (every class before M2.2) is never flagged.
        var unknown = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk), Worn(Buckler)], new ClassLevel(Skirmisher.Reference, 1)));
        Assert.DoesNotContain(unknown.Field(FieldIds.ArmorClass).Warnings, w => w.Code is "equipment.armor-untrained" or "equipment.shield-untrained");
        Assert.Equal(16 + 2, unknown.Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void An_untrained_shield_adds_to_armor_class_under_2014_rules_but_not_2024_rules_side_by_side()
    {
        // RulesFamilyPolicy.UntrainedShieldGivesArmorClass: SRD 5.1 p. 62 (disadvantage only) vs SRD 5.2.1 p. 92.
        var untrained = Revision(3, ContentKind.Class, "Test Unshielded", new HitDieEffect { Id = "hit-die", Die = 8 }, Training("light"));
        var catalog = Catalog(untrained);
        CharacterSheet Calc(string family) => CharacterCalculator.Calculate(Fighter(family, [Worn(Coat), Worn(Buckler)], new ClassLevel(untrained.Reference, 1)), catalog);

        var older = Calc(RulesFamilies.Srd51).Field(FieldIds.ArmorClass);
        var newer = Calc(RulesFamilies.Srd521).Field(FieldIds.ArmorClass);

        Assert.Equal((11 + 2 + 2, 11 + 2), (older.Value, newer.Value));
        Assert.Contains(older.Warnings, w => w.Code == "equipment.shield-untrained" && w.Message.Contains("still adds", StringComparison.Ordinal));
        Assert.Contains(newer.Warnings, w => w.Code == "equipment.shield-untrained" && w.Message.Contains("adds nothing", StringComparison.Ordinal));
        Assert.Contains(newer.Trace, t => t.Operation == "ignored" && t.Origin.Content == Buckler.Reference);
        Assert.Equal((true, false), (RulesFamilies.Get(RulesFamilies.Srd51).UntrainedShieldGivesArmorClass, RulesFamilies.Get(RulesFamilies.Srd521).UntrainedShieldGivesArmorClass));
    }

    [Fact]
    public void A_roll_bonus_formula_is_evaluated_for_the_character()
    {
        var sheet = Sheet(Fighter(RulesFamilies.Srd521, null, new ClassLevel(Vanguard.Reference, 7)));
        var roll = sheet.Features!.Single(f => f.Content == Rally.Reference).Effects.Single(e => e.Id == "rally-heal");
        Assert.Equal(("1d10", 7), (roll.Dice, roll.Bonus));
    }

    [Fact]
    public void Combat_details_need_content_schema_v8()
    {
        var catalog = Catalog();
        foreach (var revision in new[] { ExtraSwing, KeenEdge, GuardStyle, Rally, Hauberk, Vanguard })
        {
            Assert.True(ContentValidator.Validate(revision, catalog).CanPublish, $"{revision.Name}: {string.Join("; ", ContentValidator.Validate(revision, catalog).Errors.Select(e => e.Message))}");
            var older = ContentValidator.Validate(revision with { SchemaVersion = 7 }, catalog);
            Assert.Contains(older.Errors, e => e.Code == "validate.requires-v8");
        }
        var badTraining = Vanguard with { Effects = [new GrantEffect { Id = "x", Grant = GrantKind.Proficiency, Target = "armor.mithral" }] };
        Assert.Contains(ContentValidator.Validate(badTraining, catalog).Errors, e => e.Code == "validate.unknown-target");
        var badStrength = Buckler with { Effects = [new ArmorEffect { Id = "shield", Category = ArmorCategory.Shield, ArmorClass = 2, Strength = 13 }] };
        Assert.Contains(ContentValidator.Validate(badStrength, catalog).Errors, e => e.Code == "validate.armor-strength");
    }
}
