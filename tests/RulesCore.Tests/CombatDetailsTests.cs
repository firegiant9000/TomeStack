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
        new HitDieEffect { Id = "hit-die", Die = 8 }, Training("light"), Feature("swing", SkirmisherSwing.Reference, 5));

    // A class that records no armor training (as every class before content v8), and one that records it has none.
    private static readonly ContentRevision Drifter = Revision(4, ContentKind.Class, "Test Drifter", new HitDieEffect { Id = "hit-die", Die = 8 });

    private static readonly ContentRevision Scholar = Revision(5, ContentKind.Class, "Test Scholar", new HitDieEffect { Id = "hit-die", Die = 6 }, Training(CharacterCalculator.NoArmorTrainingKey));

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
            [.. Fixtures.Pack().Revisions, .. m1.Revisions, Vanguard, ExtraSwing, KeenEdge, KeenerEdge, GuardStyle, Rally, Skirmisher, SkirmisherSwing, Drifter, Scholar, Hauberk, Coat, Buckler, .. extra]);
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
    public void Missing_armor_training_warns_only_when_every_class_records_its_training()
    {
        // A later-class Vanguard has no heavy armor training (onlyAs startingClass).
        var later = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk)], new ClassLevel(Skirmisher.Reference, 1), new ClassLevel(Vanguard.Reference, 1)));
        Assert.Contains(later.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained" && w.Message.Contains("heavy armor", StringComparison.Ordinal));

        var starting = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk)], new ClassLevel(Vanguard.Reference, 1), new ClassLevel(Skirmisher.Reference, 1)));
        Assert.DoesNotContain(starting.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained");

        // A class that records no armor training at all (every class before M2.2) is never flagged.
        var unknown = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk), Worn(Buckler)], new ClassLevel(Drifter.Reference, 1)));
        Assert.DoesNotContain(unknown.Field(FieldIds.ArmorClass).Warnings, w => w.Code is "equipment.armor-untrained" or "equipment.shield-untrained");
        Assert.Equal(16 + 2, unknown.Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void One_class_without_an_armor_record_turns_the_training_check_off()
    {
        // The review's case: a Paladin (written before v8) who took a level of Fighter. The Fighter's own training has
        // no heavy armor as a later class, but the first class's training is unknown, so nothing is flagged.
        var sheet = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk), Worn(Buckler)], new ClassLevel(Drifter.Reference, 5), new ClassLevel(Vanguard.Reference, 1)));
        var ac = sheet.Field(FieldIds.ArmorClass);

        Assert.DoesNotContain(ac.Warnings, w => w.Code is "equipment.armor-untrained" or "equipment.shield-untrained");
        Assert.Equal(16 + 2 + 1, ac.Value); // hauberk, buckler, Guard Style
    }

    [Fact]
    public void A_class_that_records_no_armor_training_keeps_the_check_on()
    {
        // armor.none (the SRD Wizard): the class states it gives none, so a later Vanguard's missing heavy training shows.
        var sheet = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Hauberk)], new ClassLevel(Scholar.Reference, 1), new ClassLevel(Vanguard.Reference, 1)));
        Assert.Contains(sheet.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained" && w.Message.Contains("heavy armor", StringComparison.Ordinal));

        var alone = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Coat)], new ClassLevel(Scholar.Reference, 1)));
        Assert.Contains(alone.Field(FieldIds.ArmorClass).Warnings, w => w.Code == "equipment.armor-untrained" && w.Message.Contains("light armor", StringComparison.Ordinal));
        Assert.DoesNotContain(alone.Diagnostics, d => d.Code == "effect.unknown-target");
    }

    [Fact]
    public void Light_armor_from_a_feat_does_not_turn_the_check_on_for_a_class_without_a_record()
    {
        // The review's second case: under 5.2.1 an untrained shield adds nothing, so a feat granting only light armor
        // must not make a shield-trained class (written before v8) lose its shield.
        var feat = Revision(41, ContentKind.Feat, "Test Light Armor Feat", Training("light"));
        var character = Fighter(RulesFamilies.Srd521, [Worn(Coat), Worn(Buckler)], new ClassLevel(Drifter.Reference, 1)) with { Pins = [feat.Reference] };
        var ac = CharacterCalculator.Calculate(character, Catalog(feat)).Field(FieldIds.ArmorClass);

        Assert.Equal(11 + 2 + 2, ac.Value);
        Assert.DoesNotContain(ac.Warnings, w => w.Code is "equipment.armor-untrained" or "equipment.shield-untrained");
    }

    [Fact]
    public void A_class_that_does_not_resolve_turns_the_training_check_off()
    {
        // Dual review: a starting class that is missing (left out of a share package, or removed) has unknown training.
        // Without it, the check would run on the Scholar alone and take the shield away under 5.2.1.
        var missing = new ClassLevel(new(Guid.NewGuid(), Guid.NewGuid()), 5);
        var sheet = Sheet(Fighter(RulesFamilies.Srd521, [Worn(Buckler)], missing, new ClassLevel(Scholar.Reference, 1)));
        var ac = sheet.Field(FieldIds.ArmorClass);

        Assert.Contains(sheet.Diagnostics, d => d.Code == "content.missing");
        Assert.Equal(10 + 2 + 2, ac.Value);
        Assert.DoesNotContain(ac.Warnings, w => w.Code is "equipment.armor-untrained" or "equipment.shield-untrained");
    }

    [Fact]
    public void A_class_whose_armor_grants_are_not_automatic_has_unknown_training()
    {
        // Dual review: an assisted grant gives no training the calculator can see, so it must not count as a record.
        var assisted = Revision(6, ContentKind.Class, "Test Assisted Class", new HitDieEffect { Id = "hit-die", Die = 8 },
            Training("shield") with { Automation = AutomationStatus.Assisted });
        var character = Fighter(RulesFamilies.Srd521, [Worn(Buckler)], new ClassLevel(assisted.Reference, 1));
        var ac = CharacterCalculator.Calculate(character, Catalog(assisted)).Field(FieldIds.ArmorClass);

        Assert.Equal(10 + 2 + 2, ac.Value);
        Assert.DoesNotContain(ac.Warnings, w => w.Code == "equipment.shield-untrained");
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

    private static ContentRevision BonusRoll(int n, string bonus) => Revision(n, ContentKind.Feat, $"Test Bonus Roll {n}",
        new RollEffect { Id = "hit", RollId = "hit", Label = "Test hit", Dice = "1d6", Bonus = bonus });

    private static FeatureEffect RollOf(CharacterSheet sheet, ContentRevision feat) =>
        sheet.Features!.Single(f => f.Content == feat.Reference).Effects.Single(e => e.Id == "hit");

    [Theory]
    [InlineData("-1", 12, -1)]
    [InlineData("STR.MOD", 8, -1)] // Strength 8: the bonus is -1, not 0
    [InlineData("STR.MOD", 16, 3)]
    public void A_roll_bonus_keeps_its_sign(string bonus, int strength, int expected)
    {
        var feat = BonusRoll(42, bonus);
        var character = Fighter(RulesFamilies.Srd51, null, new ClassLevel(Vanguard.Reference, 1)) with { Pins = [feat.Reference], BaseAbilities = new(strength, 14, 14, 10, 10, 10) };
        var sheet = CharacterCalculator.Calculate(character, Catalog(feat));

        Assert.Equal(expected, RollOf(sheet, feat).Bonus);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Content == feat.Reference);
    }

    [Fact]
    public void A_roll_bonus_that_cannot_be_evaluated_is_a_content_diagnostic()
    {
        // CLASS_LEVEL parses, but a feat belongs to no class, so it has no value for this character.
        var feat = BonusRoll(43, "CLASS_LEVEL");
        var character = Fighter(RulesFamilies.Srd51, null, new ClassLevel(Vanguard.Reference, 1)) with { Pins = [feat.Reference] };
        var sheet = CharacterCalculator.Calculate(character, Catalog(feat));

        Assert.Null(RollOf(sheet, feat).Bonus);
        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Content == feat.Reference);
        Assert.Equal(("effect.invalid-formula", "hit"), (diagnostic.Code, diagnostic.EffectId));
        Assert.Equal(AutomationStatus.Assisted, sheet.Features!.Single(f => f.Content == feat.Reference).Automation);
    }

    [Fact]
    public void The_critical_range_is_bounded_to_a_d20_roll()
    {
        var feat = Revision(44, ContentKind.Feat, "Test Absurd Edge", new ModifierEffect { Id = "critical", Operation = ModifierOperation.Bonus, Target = FieldIds.CriticalRange, Value = "-25" });
        var character = Fighter(RulesFamilies.Srd51, null, new ClassLevel(Vanguard.Reference, 1)) with { Pins = [feat.Reference] };
        var field = CharacterCalculator.Calculate(character, Catalog(feat)).Field(FieldIds.CriticalRange);

        Assert.Equal(1, field.Value);
        Assert.Contains(field.Trace, t => t.Operation == "bound" && t.Result == 1);
        var warning = Assert.Single(field.Warnings, w => w.Code == "effect.out-of-range");
        Assert.Equal((feat.Reference, "critical"), (warning.Content, warning.EffectId));
    }

    [Fact]
    public void The_attack_count_is_at_least_one()
    {
        var feat = Revision(45, ContentKind.Feat, "Test Clumsy", new ModifierEffect { Id = "attacks", Operation = ModifierOperation.Bonus, Target = FieldIds.Attacks, Value = "-3" });
        var character = Fighter(RulesFamilies.Srd51, null, new ClassLevel(Vanguard.Reference, 1)) with { Pins = [feat.Reference] };
        var field = CharacterCalculator.Calculate(character, Catalog(feat)).Field(FieldIds.Attacks);

        Assert.Equal(1, field.Value);
        Assert.Contains(field.Trace, t => t.Operation == "bound" && t.Result == 1);
        Assert.Contains(field.Warnings, w => w.Code == "effect.out-of-range" && w.Content == feat.Reference);

        // In range, nothing is bounded.
        var fine = Sheet(Fighter(RulesFamilies.Srd51, null, new ClassLevel(Vanguard.Reference, 5))).Field(FieldIds.Attacks);
        Assert.DoesNotContain(fine.Trace, t => t.Operation == "bound");
        Assert.DoesNotContain(fine.Warnings, w => w.Code == "effect.out-of-range");
    }

    [Fact]
    public void Below_content_v8_the_calculator_ignores_the_v8_fields()
    {
        // The validator refuses these, but an imported or hand-edited v7 revision can carry them; an older build ignores them.
        var guard = GuardStyle with { SchemaVersion = 7 };
        var rally = BonusRoll(46, "5") with { SchemaVersion = 7 };
        var hauberk = Hauberk with { SchemaVersion = 7 };
        var trained = Revision(47, ContentKind.Class, "Test Old Class", new HitDieEffect { Id = "hit-die", Die = 8 }, Training("light")) with { SchemaVersion = 7 };
        var catalog = new InMemoryContentCatalog(
            [.. Fixtures.Pack().Sources, .. Fixtures.M1Pack().Sources],
            [.. Fixtures.Pack().Revisions, .. Fixtures.M1Pack().Revisions, guard, rally, hauberk, trained]);
        var character = Fighter(RulesFamilies.Srd521, [Worn(hauberk)], new ClassLevel(trained.Reference, 1)) with { Pins = [guard.Reference, rally.Reference] };
        var sheet = CharacterCalculator.Calculate(character, catalog);
        var ac = sheet.Field(FieldIds.ArmorClass);

        Assert.Equal(16 + 1, ac.Value); // the guard bonus applies (whileArmored ignored; armor is worn anyway)
        Assert.DoesNotContain(ac.Warnings, w => w.Code is "equipment.armor-strength" or "equipment.armor-untrained"); // Str 12 < 13, heavy untrained: both ignored
        Assert.DoesNotContain(sheet.Field(FieldIds.Skill(CharacterCalculator.Stealth)).Warnings, w => w.Code == "equipment.stealth-disadvantage");
        Assert.Null(RollOf(sheet, rally).Bonus);
        Assert.Equal(4, sheet.Diagnostics.Count(d => d.Code == "effect.schema-field-ignored")); // whileArmored, armor requirements, bonus, training

        // Without armor, an ignored whileArmored modifier applies as an always-on bonus, as in a v7 build.
        var bare = CharacterCalculator.Calculate(character with { Equipment = [] }, catalog).Field(FieldIds.ArmorClass);
        Assert.Equal(10 + 2 + 1, bare.Value);
    }

    [Fact]
    public void Below_content_v8_the_attacks_and_critical_range_fields_do_not_exist()
    {
        // Dual review: a v7 build does not know these fields, so a v7 (or v1 or v2, imported with warnings only) revision
        // that changes them changes nothing, and a restriction on them keeps the content out, as an unknown field does.
        var edge = KeenEdge with { SchemaVersion = 7, Effects = [new ModifierEffect { Id = "critical", Operation = ModifierOperation.Set, Target = FieldIds.CriticalRange, Value = "2" }] };
        var swing = ExtraSwing with { SchemaVersion = 7 };
        var gated = Revision(49, ContentKind.Feat, "Test Gated Feat",
            new RestrictionEffect { Id = "needs-attacks", Field = FieldIds.Attacks, Minimum = 1 },
            new ModifierEffect { Id = "init", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "5" }) with { SchemaVersion = 7 };
        var catalog = new InMemoryContentCatalog(
            [.. Fixtures.Pack().Sources, .. Fixtures.M1Pack().Sources],
            [.. Fixtures.Pack().Revisions, .. Fixtures.M1Pack().Revisions, edge, swing, gated]);
        var sheet = CharacterCalculator.Calculate(Fighter(RulesFamilies.Srd51, null) with { Pins = [edge.Reference, swing.Reference, gated.Reference] }, catalog);

        Assert.Equal((20, 1), (sheet.Field(FieldIds.CriticalRange).Value, sheet.Field(FieldIds.Attacks).Value));
        Assert.Equal(2, sheet.Diagnostics.Count(d => d.Code == "effect.schema-field-ignored" && d.Content != gated.Reference));
        Assert.Contains(sheet.Diagnostics, d => d.Code == "effect.schema-field-ignored" && d.Content == gated.Reference && d.EffectId == "needs-attacks");
        Assert.DoesNotContain(gated.Reference, sheet.Active!);
    }

    [Fact]
    public void The_validator_rejects_set_and_replace_on_the_critical_range()
    {
        var catalog = Catalog();
        foreach (var operation in new[] { ModifierOperation.Set, ModifierOperation.Replace })
        {
            var edge = KeenEdge with { Effects = [new ModifierEffect { Id = "critical", Operation = operation, Target = FieldIds.CriticalRange, Value = "19" }] };
            Assert.Contains(ContentValidator.Validate(edge, catalog).Errors, e => e.Code == "validate.critical-range-operation" && e.EffectId == "critical");
        }
        Assert.DoesNotContain(ContentValidator.Validate(KeenEdge, catalog).Errors, e => e.Code == "validate.critical-range-operation");
    }

    [Fact]
    public void The_validator_rejects_an_armor_class_replacement_that_applies_only_while_armored()
    {
        var catalog = Catalog();
        var never = GuardStyle with { Effects = [new ModifierEffect { Id = "guard", Operation = ModifierOperation.Replace, Target = FieldIds.ArmorClass, Value = "15", WhileArmored = true }] };
        Assert.Contains(ContentValidator.Validate(never, catalog).Errors, e => e.Code == "validate.while-armored-replace" && e.EffectId == "guard");

        // A bonus while armored is fine, and so is an unarmored replacement.
        Assert.True(ContentValidator.Validate(GuardStyle, catalog).CanPublish);
        var unarmored = GuardStyle with { Effects = [new ModifierEffect { Id = "guard", Operation = ModifierOperation.Replace, Target = FieldIds.ArmorClass, Value = "13 + DEX.MOD" }] };
        Assert.DoesNotContain(ContentValidator.Validate(unarmored, catalog).Errors, e => e.Code == "validate.while-armored-replace");
    }

    [Fact]
    public void The_validator_reports_the_lowest_schema_version_a_revision_needs()
    {
        var catalog = Catalog();
        ContentRevision Feat(params Effect[] effects) => Revision(48, ContentKind.Feat, "Test Needs", effects);

        Assert.Equal(3, ContentValidator.Validate(Feat(new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.Initiative, Value = "1" }), catalog).RequiredSchemaVersion);
        Assert.Equal(4, ContentValidator.Validate(Coat, catalog).RequiredSchemaVersion);
        Assert.Equal(5, ContentValidator.Validate(Feat(new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = FieldIds.SpellSaveDc, Value = "1" }), catalog).RequiredSchemaVersion);
        Assert.Equal(6, ContentValidator.Validate(Feat(new ToggleEffect { Id = "t", ToggleId = "t", Label = "T" }), catalog).RequiredSchemaVersion);
        foreach (var revision in new[] { ExtraSwing, KeenEdge, GuardStyle, Rally, Hauberk, Vanguard, Scholar })
            Assert.Equal(8, ContentValidator.Validate(revision, catalog).RequiredSchemaVersion);
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
