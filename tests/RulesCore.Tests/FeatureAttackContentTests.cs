using System.Reflection;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Play mechanics slice 4, Task 4.1 (D43, content schema v11): the attack and attackRider effects, the cross-entry toggle
/// keys on modifier and roll, and the resource.&lt;id&gt; modifier target: the version gate that keeps older revisions byte
/// for byte, validation, and the fixture pack.
/// </summary>
public class FeatureAttackContentTests
{
    private static readonly Guid Absent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee");

    /// <summary>Capability evidence (ADR-020): an attack rider is delivered automatically, adding to the attacks that carry its tags.</summary>
    [Fact]
    public void The_capability_registry_ceiling_for_attackRider_is_automatic()
    {
        var bolt = new AttackEffect
        {
            Id = "bolt", AttackId = "bolt", Label = "Bolt", Kind = WeaponAttack.Melee, Abilities = [Ability.Str],
            Damage = new("1", "6"), DamageType = "fire", Tags = ["spark"],
        };
        var rider = new AttackRiderEffect { Id = "edge", Tags = ["spark"], ToHit = "2" };
        var alone = MechanicsInventoryTests.Holder("Test Bolt Alone", 11, bolt);
        var boosted = MechanicsInventoryTests.Holder("Test Bolt Boosted", 11, bolt, rider);

        var (_, aloneSheet) = MechanicsInventoryTests.Pinned(alone);
        var (report, boostedSheet) = MechanicsInventoryTests.Pinned(boosted);

        CapabilityRegistryTests.AssertAutomaticallyDelivered(report, boosted, AttackRiderEffect.TypeName, "edge");
        Assert.Equal(aloneSheet.Attacks!.Single(a => a.EffectId == "bolt").ToHit + 2, boostedSheet.Attacks!.Single(a => a.EffectId == "bolt").ToHit);
    }

    private static ValidationReport Validate(ContentRevision revision) => ContentValidator.Validate(revision, Fixtures.PlayMechanicsCatalog());

    private static ContentReference[] PackReferences =>
        [Fixtures.OpenHandAdept, Fixtures.OpenHandStrike, Fixtures.KindledMode, Fixtures.BlazingMode, Fixtures.KeenSense, Fixtures.EmberFist];

    // ---- the version gate ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(6)]
    public void A_v10_modifier_carrying_toggleContent_stays_byte_for_byte_and_untyped(int version)
    {
        var json = """{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1","stacking":"stack","toggle":"t","automation":"automatic","timing":"whileActive","toggleContent":"6f0dc000-0000-4000-8000-000000000042"}""";
        var modifier = Assert.IsType<ModifierEffect>(Fixtures.RevisionWithEffect(json, version).Effects.Single());

        Assert.Null(modifier.ToggleContent);
        Assert.Equal(json, Fixtures.EffectJson(modifier));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(9)]
    [InlineData(6)]
    public void A_v10_roll_carrying_whileToggle_stays_byte_for_byte_and_untyped(int version)
    {
        var json = """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","automation":"automatic","timing":"always","whileToggle":{"toggleId":"t"}}""";
        var roll = Assert.IsType<RollEffect>(Fixtures.RevisionWithEffect(json, version).Effects.Single());

        Assert.Null(roll.WhileToggle);
        Assert.Equal(json, Fixtures.EffectJson(roll));
    }

    [Fact]
    public void A_v11_modifier_and_roll_type_their_cross_entry_toggle_and_write_it_back_unchanged()
    {
        var modifierJson = """{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1","stacking":"stack","toggle":"t","toggleContent":"6f0dc000-0000-4000-8000-000000000042","automation":"automatic","timing":"whileActive"}""";
        var modifier = Assert.IsType<ModifierEffect>(Fixtures.RevisionWithEffect(modifierJson, 11).Effects.Single());
        Assert.Equal(Fixtures.KindledMode.ContentId, modifier.ToggleContent);
        Assert.Null(modifier.Extensions);
        Assert.Equal(modifierJson, Fixtures.EffectJson(modifier));

        var rollJson = """{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","whileToggle":{"contentId":"6f0dc000-0000-4000-8000-000000000042","toggleId":"kindled"},"automation":"automatic","timing":"always"}""";
        var roll = Assert.IsType<RollEffect>(Fixtures.RevisionWithEffect(rollJson, 11).Effects.Single());
        Assert.Equal(new ToggleRef(Fixtures.KindledMode.ContentId, "kindled"), roll.WhileToggle);
        Assert.Null(roll.Extensions);
        Assert.Equal(rollJson, Fixtures.EffectJson(roll));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(6)]
    public void An_attack_or_rider_in_an_older_revision_stays_an_unknown_effect_byte_for_byte(int version)
    {
        var attack = """{"type":"attack","id":"a","attackId":"a","label":"A","kind":"melee","abilities":["str"],"damage":{"count":"1","die":"6"},"damageType":"fire"}""";
        var rider = """{"type":"attackRider","id":"r","tags":["all"],"dice":"1d8"}""";
        foreach (var json in new[] { attack, rider })
        {
            var effect = Fixtures.RevisionWithEffect(json, version).Effects.Single();
            Assert.IsType<UnknownEffect>(effect);
            Assert.Equal(json, Fixtures.EffectJson(effect));
        }
    }

    [Fact]
    public void A_v11_attack_and_rider_are_typed_and_write_every_field_back_unchanged()
    {
        var attackJson = """{"type":"attack","id":"a","attackId":"a","label":"A","kind":"ranged","abilities":["wis","dex"],"proficient":false,"damage":{"count":"2","die":"SCALE.handDie"},"damageBonus":"WIS.MOD","damageType":"force","range":"60 ft","tags":["bolt","all"],"replacesAttack":true,"activation":"bonusAction","resourceId":"focus","resourceContent":"6f0dc000-0000-4000-8000-000000000040","cost":"1","whileToggle":{"toggleId":"kindled"},"automation":"automatic","timing":"always"}""";
        var attack = Assert.IsType<AttackEffect>(Fixtures.RevisionWithEffect(attackJson, 11).Effects.Single());
        Assert.Equal(WeaponAttack.Ranged, attack.Kind);
        Assert.Equal([Ability.Wis, Ability.Dex], attack.Abilities);
        Assert.False(attack.Proficient);
        Assert.Equal(new AttackDamage("2", "SCALE.handDie"), attack.Damage);
        Assert.Equal(Activation.BonusAction, attack.Activation);
        Assert.Equal("1", attack.Cost);
        Assert.True(attack.ReplacesAttack);
        Assert.Equal(attackJson, Fixtures.EffectJson(attack));

        var riderJson = """{"type":"attackRider","id":"r","tags":["unarmed"],"toHit":"1","dice":"3d8","damageBonus":"2","damageType":"fire","toggle":"kindled","toggleContent":"6f0dc000-0000-4000-8000-000000000042","stackGroup":"modeRider","automation":"automatic","timing":"always"}""";
        var rider = Assert.IsType<AttackRiderEffect>(Fixtures.RevisionWithEffect(riderJson, 11).Effects.Single());
        Assert.Equal(["unarmed"], rider.Tags);
        Assert.Equal(Fixtures.KindledMode.ContentId, rider.ToggleContent);
        Assert.Equal("modeRider", rider.StackGroup);
        Assert.Equal(riderJson, Fixtures.EffectJson(rider));
    }

    [Theory]
    [InlineData("""{"type":"attack","id":"a","attackId":"a","label":"A","kind":"sideways","abilities":["str"],"damage":{"count":"1","die":"6"},"damageType":"fire"}""")]
    [InlineData("""{"type":"attack","id":"a","attackId":"a","label":"A","kind":"melee","abilities":["str"],"damage":{"count":"1","die":"6"},"damageType":"fire","activation":"free"}""")]
    [InlineData("""{"type":"attack","id":"a","attackId":"a","label":"A","kind":"melee","abilities":["wizard"],"damage":{"count":"1","die":"6"},"damageType":"fire"}""")]
    [InlineData("""{"type":"attack","id":"a","attackId":"a","label":"A","kind":"melee","abilities":["str, dex"],"damage":{"count":"1","die":"6"},"damageType":"fire"}""")]
    [InlineData("""{"type":"attack","id":"a","attackId":"a","label":"A","kind":" melee","abilities":["str"],"damage":{"count":"1","die":"6"},"damageType":"fire"}""")]
    [InlineData("""{"type":"attack","id":"a","attackId":"a","label":"A","kind":"melee","abilities":["str"],"damageType":"fire"}""")]
    [InlineData("""{"type":"attackRider","id":"r","dice":"1d8"}""")]
    public void A_malformed_v11_attack_or_rider_stays_untyped_reports_malformed_and_requires_v11(string json)
    {
        var revision = Fixtures.RevisionWithEffect(json, 11);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Theory]
    [InlineData("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1","timing":"whileActive","toggle":"t","toggleContent":"not-a-guid"}""")]
    [InlineData("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","whileToggle":{"contentId":"not-a-guid","toggleId":"t"}}""")]
    public void A_modifier_or_roll_with_an_unreadable_v11_key_reports_malformed_and_requires_v11(string json)
    {
        var revision = Fixtures.RevisionWithEffect(json, 11);
        Assert.IsType<UnknownEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.effect-malformed");
        Assert.Equal(11, report.RequiredSchemaVersion);
    }

    [Fact]
    public void A_resource_target_below_v11_is_an_unknown_target()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"resource.pool","value":"1"}""", 10);

        var report = Validate(revision);

        var error = Assert.Single(report.Errors, e => e.Code == "validate.unknown-target");
        Assert.Equal("m", error.EffectId);
        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.requires-v11");
    }

    [Fact]
    public void The_modifier_and_roll_v11_key_lists_match_the_properties_the_build_added()
    {
        string[] common = ["Id", "Automation", "Timing", "Text", "Extensions"];
        AssertKeys<ModifierEffect>([.. common, "Operation", "Target", "Value", "Stacking", "StackGroup", "Toggle", "WhileArmored"], Properties.RevisionHashProperties.ModifierV11Keys);
        AssertKeys<RollEffect>([.. common, "RollId", "Label", "Dice", "ResourceId", "Activation", "ResourceContent", "Cost", "VariableCost", "Bonus"], Properties.RevisionHashProperties.RollV11Keys);

        static void AssertKeys<T>(string[] pinned, string[] hashTestCopy)
        {
            var added = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } && !pinned.Contains(p.Name))
                .Select(p => p.Name)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var field = typeof(T).GetField("V11Keys", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            var production = ((IEnumerable<string>)field.GetValue(null)!).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

            Assert.NotEmpty(production);
            Assert.Equal(production, added, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(production, hashTestCopy.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        }
    }

    // ---- validation: errors --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("attack-blank-label", "validate.attack-incomplete", "strike")]
    [InlineData("attack-bad-id", "validate.attack-incomplete", "strike")]
    [InlineData("attack-blank-damage-type", "validate.attack-incomplete", "strike")]
    [InlineData("attack-blank-die", "validate.attack-incomplete", "strike")]
    [InlineData("attack-blank-count", "validate.attack-incomplete", "strike")]
    [InlineData("attack-no-abilities", "validate.attack-abilities", "strike")]
    [InlineData("attack-die-invalid", "validate.attack-damage", "strike")]
    [InlineData("attack-count-invalid", "validate.attack-damage", "strike")]
    [InlineData("attack-bonus-invalid", "validate.attack-damage", "strike")]
    [InlineData("attack-damage-type", "validate.grant-damage-type", "strike")]
    [InlineData("attack-tag-invalid", "validate.tag-invalid", "strike")]
    [InlineData("attack-cost-without-resource", "validate.attack-resource", "bolt")]
    [InlineData("attack-resource-own-lacks", "validate.attack-resource", "bolt")]
    [InlineData("attack-resource-content-lacks", "validate.attack-resource", "bolt")]
    [InlineData("attack-resource-content-not-installed", "validate.attack-resource", "bolt")]
    [InlineData("attack-cost-invalid", "validate.formula-invalid", "bolt")]
    [InlineData("attack-while-toggle-unknown", "validate.while-toggle-unknown", "strike")]
    [InlineData("attack-while-toggle-content-lacks", "validate.while-toggle-unknown", "strike")]
    [InlineData("rider-no-tags", "validate.rider-incomplete", "fist-rider")]
    [InlineData("rider-no-effect", "validate.rider-incomplete", "fist-rider")]
    [InlineData("rider-dice-bad-die", "validate.rider-dice", "fist-rider")]
    [InlineData("rider-dice-zero", "validate.rider-dice", "fist-rider")]
    [InlineData("rider-dice-too-many", "validate.rider-dice", "fist-rider")]
    [InlineData("rider-dice-with-bonus", "validate.rider-dice", "fist-rider")]
    [InlineData("rider-dice-no-count", "validate.rider-dice", "fist-rider")]
    [InlineData("rider-dice-padded", "validate.rider-dice", "fist-rider")]
    [InlineData("rider-to-hit-invalid", "validate.formula-invalid", "fist-rider")]
    [InlineData("rider-bonus-invalid", "validate.formula-invalid", "fist-rider")]
    [InlineData("rider-damage-type", "validate.grant-damage-type", "fist-rider")]
    [InlineData("rider-tag-invalid", "validate.tag-invalid", "fist-rider")]
    [InlineData("rider-toggle-unknown", "validate.rider-toggle-unknown", "fist-rider")]
    [InlineData("rider-toggle-content-without-toggle", "validate.rider-toggle-unknown", "fist-rider")]
    [InlineData("rider-toggle-content-lacks", "validate.rider-toggle-unknown", "fist-rider")]
    [InlineData("roll-while-toggle-blank-not-installed", "validate.while-toggle-unknown", "flash")]
    [InlineData("attack-while-toggle-blank-not-installed", "validate.while-toggle-unknown", "strike")]
    [InlineData("rider-toggle-blank-not-installed", "validate.rider-toggle-unknown", "fist-rider")]
    [InlineData("modifier-toggle-blank-not-installed", "validate.toggle-unknown", "keen-initiative")]
    [InlineData("restriction-resource-target", "validate.unknown-target", "r")]
    [InlineData("roll-while-toggle-unknown", "validate.while-toggle-unknown", "flash")]
    [InlineData("roll-while-toggle-content-lacks", "validate.while-toggle-unknown", "flash")]
    [InlineData("modifier-own-toggle-unknown", "validate.toggle-unknown", "keen-initiative")]
    [InlineData("modifier-toggle-content-lacks", "validate.toggle-unknown", "keen-initiative")]
    [InlineData("modifier-toggle-content-without-toggle", "validate.toggle-unknown", "keen-initiative")]
    [InlineData("resource-target-unknown", "validate.resource-target", "reflex-bonus")]
    [InlineData("resource-target-empty", "validate.resource-target", "reflex-bonus")]
    [InlineData("resource-target-other-revision", "validate.resource-target", "keen-initiative")]
    [InlineData("resource-target-set", "validate.resource-target-operation", "reflex-bonus")]
    [InlineData("resource-target-replace", "validate.resource-target-operation", "reflex-bonus")]
    [InlineData("attack-below-v11", "validate.requires-v11", null)]
    [InlineData("rider-below-v11", "validate.requires-v11", null)]
    public void Feature_attack_mistakes_are_errors(string variant, string code, string? effectId)
    {
        var errors = Validate(FeatureAttackVariants.Make(variant)).Errors.Where(e => e.Code == code).ToList();

        Assert.NotEmpty(errors);
        if (effectId is not null)
            Assert.Contains(errors, e => e.EffectId == effectId);
    }

    [Fact]
    public void A_whileToggle_without_a_toggleId_reads_but_is_an_error()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","whileToggle":{"contentId":"6f0dc000-0000-4000-8000-000000000042"}}""", 11);
        Assert.IsType<RollEffect>(revision.Effects.Single());

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.while-toggle-unknown" && e.EffectId == "r");
    }

    [Fact]
    public void A_whileToggle_without_a_toggleId_naming_uninstalled_content_is_an_error_not_a_warning()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"roll","id":"r","rollId":"r","label":"R","dice":"1d6","whileToggle":{"contentId":"6f0dc000-0000-4000-8000-0000000000ee"}}""", 11);

        var report = Validate(revision);

        Assert.Contains(report.Errors, e => e.Code == "validate.while-toggle-unknown" && e.EffectId == "r");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-content-missing");
    }

    [Fact]
    public void A_blank_toggle_name_is_not_reported_as_unchecked_content()
    {
        var report = Validate(FeatureAttackVariants.Make("rider-toggle-blank-not-installed"));

        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-content-missing");
    }

    [Fact]
    public void A_non_overlay_toggle_with_allowedWith_gets_the_existing_error_and_no_not_primary_warning()
    {
        var report = Validate(FeatureAttackVariants.Make("overlay-with-allowed-with-not-overlay"));

        Assert.Contains(report.Errors, e => e.Code == "validate.toggle-allowed-with" && e.EffectId == "surge");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-allowed-with-not-primary");
    }

    [Fact]
    public void A_toggle_name_that_resolves_nowhere_is_one_error_with_the_effects_own_code()
    {
        var report = Validate(FeatureAttackVariants.Make("rider-toggle-unknown"));

        Assert.Single(report.Errors, e => e.EffectId == "fist-rider" && e.Code == "validate.rider-toggle-unknown");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-content-missing");
    }

    [Fact]
    public void An_attack_naming_content_that_is_not_installed_is_an_error_without_a_missing_content_warning()
    {
        var report = Validate(FeatureAttackVariants.Make("attack-resource-content-not-installed"));

        Assert.Contains(report.Errors, e => e.Code == "validate.attack-resource");
        Assert.DoesNotContain(report.Warnings, w => w.Code.EndsWith("-content-missing", StringComparison.Ordinal));
    }

    [Fact]
    public void An_attack_pool_defined_by_a_later_revision_of_the_holder_content_is_found()
    {
        // The store lists a content's revisions oldest first, so only the newest may define the pool.
        var holder = Guid.Parse("6f0dc000-0000-4000-8000-0000000000e1");
        var adept = Fixtures.PlayMechanicsCatalog().FindRevision(Fixtures.OpenHandAdept)!;
        var first = adept with { ContentId = holder, RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000e1"), Effects = [] };
        var second = adept with { ContentId = holder, RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000e2"), Effects = [.. adept.Effects.Where(e => e is ResourceEffect)] };
        var strike = FeatureAttackVariants.Make("attack-resource-content-not-installed");
        strike = strike with { Effects = [.. strike.Effects.Select(e => e is AttackEffect { Id: "bolt" } a ? a with { ResourceContent = holder } : e)] };

        var report = ContentValidator.Validate(strike, Fixtures.PlayMechanicsCatalog(), [first, second]);

        Assert.DoesNotContain(report.Errors, e => e.Code == "validate.attack-resource");
    }

    // ---- validation: warnings ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("modifier-toggle-content-not-installed", "keen-initiative")]
    [InlineData("rider-toggle-content-not-installed", "fist-rider")]
    [InlineData("roll-while-toggle-not-installed", "flash")]
    [InlineData("attack-while-toggle-not-installed", "strike")]
    public void A_toggle_of_content_that_is_not_installed_is_a_warning_and_does_not_block(string variant, string effectId)
    {
        var report = Validate(FeatureAttackVariants.Make(variant));

        var warning = Assert.Single(report.Warnings, w => w.Code == "validate.toggle-content-missing");
        Assert.Equal(effectId, warning.EffectId);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void A_toggle_of_content_present_only_in_the_batch_is_found_without_a_warning_or_error()
    {
        var holder = Fixtures.RevisionWithEffect("""{"type":"toggle","id":"warm","toggleId":"warm","label":"Warm"}""", 11)
            with { ContentId = Absent, RevisionId = Guid.Parse("6f0de000-0000-4000-8000-0000000000ee") };
        var rider = FeatureAttackVariants.Make("rider-toggle-content-not-installed");
        rider = rider with { Effects = [.. rider.Effects.Select(e => e is AttackRiderEffect r ? r with { Toggle = "warm" } : e)] };

        var report = ContentValidator.Validate(rider, Fixtures.PlayMechanicsCatalog(), [rider, holder]);

        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-content-missing");
        Assert.Empty(report.Errors);
    }

    [Theory]
    [InlineData("allowed-with-overlay")]
    [InlineData("allowed-with-other-group")]
    [InlineData("allowed-with-ungrouped")]
    public void An_overlay_allowed_with_a_toggle_that_is_not_a_primary_of_its_group_warns(string variant)
    {
        var report = Validate(FeatureAttackVariants.Make(variant));

        var warning = Assert.Single(report.Warnings, w => w.Code == "validate.toggle-allowed-with-not-primary");
        Assert.Equal("surge", warning.EffectId);
        Assert.DoesNotContain(report.Errors, e => e.Code.StartsWith("validate.toggle-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("allowed-with-primary")]
    [InlineData("allowed-with-primary-of-another-source")]
    public void An_overlay_allowed_with_a_primary_of_its_own_group_does_not_warn(string variant)
    {
        var report = Validate(FeatureAttackVariants.Make(variant));

        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-allowed-with-not-primary");
        Assert.Empty(report.Errors);
    }

    private static ContentRevision Declaring(Guid contentId, ToggleEffect toggle) =>
        Fixtures.RevisionWithEffect("""{"type":"modifier","id":"seed","operation":"bonus","target":"initiative","value":"1"}""", 11)
            with { ContentId = contentId, RevisionId = Guid.NewGuid(), Effects = [toggle] };

    [Fact]
    public void A_primary_of_the_same_group_id_held_by_other_content_is_not_a_primary_of_this_group()
    {
        var other = Declaring(Absent, new ToggleEffect { Id = "ember2", ToggleId = "ember2", Label = "Ember", Group = "forms", GroupContent = Absent });
        var surge = FeatureAttackVariants.Make("allowed-with-primary");
        surge = surge with { Effects = [.. surge.Effects.Select(e => e is ToggleEffect t ? t with { AllowedWith = [new ToggleRef(Absent, "ember2")] } : e)] };

        var report = ContentValidator.Validate(surge, Fixtures.PlayMechanicsCatalog(), [surge, other]);

        Assert.Single(report.Warnings, w => w.Code == "validate.toggle-allowed-with-not-primary");
    }

    [Fact]
    public void A_primary_that_names_no_group_content_belongs_to_the_group_of_the_content_that_declares_it()
    {
        // The group's holder is the content that declares the toggle when it names none: here the forms discipline itself.
        var holder = Declaring(Fixtures.FormDiscipline.ContentId, new ToggleEffect { Id = "base", ToggleId = "base", Label = "Base", Group = "forms" });
        var surge = FeatureAttackVariants.Make("allowed-with-primary");
        surge = surge with { Effects = [.. surge.Effects.Select(e => e is ToggleEffect t ? t with { AllowedWith = [new ToggleRef(Fixtures.FormDiscipline.ContentId, "base")] } : e)] };

        var report = ContentValidator.Validate(surge, Fixtures.PlayMechanicsCatalog(), [surge, holder]);

        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-allowed-with-not-primary");
    }

    [Fact]
    public void An_allowedWith_naming_an_overlay_on_content_not_installed_still_only_warns_that_the_content_is_missing()
    {
        var report = Validate(FeatureAttackVariants.Make("allowed-with-not-installed"));

        Assert.Contains(report.Warnings, w => w.Code == "validate.toggle-allowed-with-content-missing");
        Assert.DoesNotContain(report.Warnings, w => w.Code == "validate.toggle-allowed-with-not-primary");
    }

    [Fact]
    public void The_forms_pack_pairings_raise_no_not_primary_warning()
    {
        foreach (var revision in Fixtures.FormsPack().Revisions)
            Assert.DoesNotContain(Validate(revision).Warnings, w => w.Code == "validate.toggle-allowed-with-not-primary");
    }

    // ---- versions -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("modifier-toggle-content-only")]
    [InlineData("roll-while-toggle-only")]
    [InlineData("resource-target-only")]
    [InlineData("attack-only")]
    [InlineData("rider-only")]
    public void A_single_v11_key_alone_requires_v11(string variant) =>
        Assert.Equal(11, Validate(FeatureAttackVariants.Make(variant)).RequiredSchemaVersion);

    [Fact]
    public void Content_using_no_v11_field_still_publishes_at_its_old_version()
    {
        var stance = Fixtures.AllCatalog().FindRevision(Fixtures.RadiantStance)!;
        Assert.Equal(6, ContentValidator.Validate(stance with { SchemaVersion = 11 }, Fixtures.AllCatalog()).RequiredSchemaVersion);
    }

    [Fact]
    public void A_modifier_and_a_roll_without_the_new_keys_still_publish_at_their_old_version()
    {
        var revision = Fixtures.RevisionWithEffect("""{"type":"modifier","id":"m","operation":"bonus","target":"initiative","value":"1"}""", 11);
        var withRoll = revision with { Effects = [.. revision.Effects, new RollEffect { Id = "r", RollId = "r", Label = "R", Dice = "1d4" }] };

        Assert.Equal(3, Validate(withRoll).RequiredSchemaVersion);
    }

    // ---- the fixture pack ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_feature_attacks_pack_validates_without_errors_or_warnings_at_v11()
    {
        foreach (var reference in PackReferences)
        {
            var revision = Fixtures.PlayMechanicsCatalog().FindRevision(reference)!;
            var report = Validate(revision);
            Assert.True(report.Errors.Count == 0, $"{revision.Name}: {string.Join("; ", report.Errors.Select(e => $"{e.Code} {e.Message}"))}");
            Assert.True(report.Warnings.Count == 0, $"{revision.Name}: {string.Join("; ", report.Warnings.Select(e => $"{e.Code} {e.Message}"))}");
            Assert.Equal(ContentRevision.PlayMechanicsSchemaVersion, report.RequiredSchemaVersion);
        }
    }

    [Fact]
    public void The_pack_types_every_new_effect_and_key()
    {
        var catalog = Fixtures.PlayMechanicsCatalog();
        var strike = catalog.FindRevision(Fixtures.OpenHandStrike)!;
        Assert.Equal(["strike", "bolt"], strike.Effects.OfType<AttackEffect>().Select(a => a.AttackId));
        Assert.Equal(["kindled-rider"], catalog.FindRevision(Fixtures.KindledMode)!.Effects.OfType<AttackRiderEffect>().Select(r => r.Id));
        Assert.Equal(["blazing-rider", "blazing-bonus"], catalog.FindRevision(Fixtures.BlazingMode)!.Effects.OfType<AttackRiderEffect>().Select(r => r.Id));
        Assert.Equal(Fixtures.KindledMode.ContentId, catalog.FindRevision(Fixtures.KeenSense)!.Effects.OfType<ModifierEffect>().Single().ToggleContent);
        Assert.Equal("kindled", catalog.FindRevision(Fixtures.KindledMode)!.Effects.OfType<RollEffect>().Single().WhileToggle!.ToggleId);
        Assert.All(PackReferences, r => Assert.DoesNotContain(catalog.FindRevision(r)!.Effects, e => e is UnknownEffect));
    }

    [Fact]
    public void The_toggle_labels_are_the_ones_the_sheet_shows()
    {
        var catalog = Fixtures.PlayMechanicsCatalog();
        Assert.Equal("Kindled mode", catalog.FindRevision(Fixtures.KindledMode)!.Effects.OfType<ToggleEffect>().Single().Label);
        Assert.Equal("Blazing mode", catalog.FindRevision(Fixtures.BlazingMode)!.Effects.OfType<ToggleEffect>().Single().Label);
        Assert.Equal("Ember fist", catalog.FindRevision(Fixtures.EmberFist)!.Effects.OfType<ToggleEffect>().Single().Label);
    }

    [Fact]
    public void The_adept_gets_its_granted_features_at_level_5_and_the_pinned_one()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Load("pm-adept-l5.json"), Fixtures.PlayMechanicsCatalog());

        Assert.NotNull(sheet.Active);
        Assert.All(PackReferences, r => Assert.Contains(r, sheet.Active!));
    }

    [Fact]
    public void The_level_3_grant_is_not_active_at_level_2()
    {
        var adept = Fixtures.Load("pm-adept-l5.json");
        var second = adept with { Level = 2, Classes = [adept.Classes[0] with { Level = 2 }] };

        var sheet = CharacterCalculator.Calculate(second, Fixtures.PlayMechanicsCatalog());

        Assert.Contains(Fixtures.OpenHandStrike, sheet.Active!);
        Assert.Contains(Fixtures.KindledMode, sheet.Active!);
        Assert.Contains(Fixtures.KeenSense, sheet.Active!);
        Assert.DoesNotContain(Fixtures.BlazingMode, sheet.Active!);
    }

    [Fact]
    public void The_mechanics_inventory_does_not_list_the_new_effects_as_unsupported()
    {
        var sheet = CharacterCalculator.Calculate(Fixtures.Load("pm-adept-l5.json"), Fixtures.PlayMechanicsCatalog());
        var report = MechanicsInventory.Of(sheet, new HashSet<Guid> { Fixtures.FeatureAttacksPack().Sources.Single().Id });

        Assert.Contains(report.Mechanics, m => m.EffectType == AttackEffect.TypeName);
        Assert.Contains(report.Mechanics, m => m.EffectType == AttackRiderEffect.TypeName);
        Assert.DoesNotContain(report.Unsupported, m => m.EffectType is AttackEffect.TypeName or AttackRiderEffect.TypeName);
    }
}

/// <summary>Revisions from the feature attacks pack (and the forms pack) with one mistake each, for the validation theories.</summary>
internal static class FeatureAttackVariants
{
    private static readonly Guid Absent = Guid.Parse("6f0dc000-0000-4000-8000-0000000000ee");

    private static ContentRevision Pack(ContentReference reference) => Fixtures.PlayMechanicsCatalog().FindRevision(reference)!;

    private static ContentRevision With(ContentReference reference, Func<Effect, Effect> change)
    {
        var revision = Pack(reference);
        return revision with { Effects = [.. revision.Effects.Select(change)] };
    }

    private static ContentRevision WithAttack(string id, Func<AttackEffect, AttackEffect> change) =>
        With(Fixtures.OpenHandStrike, e => e is AttackEffect a && a.Id == id ? change(a) : e);

    private static ContentRevision WithRider(Func<AttackRiderEffect, AttackRiderEffect> change) =>
        With(Fixtures.EmberFist, e => e is AttackRiderEffect r ? change(r) : e);

    private static ContentRevision WithFlash(Func<RollEffect, RollEffect> change) =>
        With(Fixtures.KindledMode, e => e is RollEffect r ? change(r) : e);

    private static ContentRevision WithKeen(Func<ModifierEffect, ModifierEffect> change) =>
        With(Fixtures.KeenSense, e => e is ModifierEffect m ? change(m) : e);

    private static ContentRevision WithReflexBonus(Func<ModifierEffect, ModifierEffect> change) =>
        With(Fixtures.KindledMode, e => e is ModifierEffect m ? change(m) : e);

    private static ContentRevision WithSurge(Func<ToggleEffect, ToggleEffect> change) =>
        With(Fixtures.SurgeOverlay, e => e is ToggleEffect t ? change(t) : e);

    public static ContentRevision Make(string variant) => variant switch
    {
        "attack-blank-label" => WithAttack("strike", a => a with { Label = " " }),
        "attack-bad-id" => WithAttack("strike", a => a with { AttackId = "Bad Id" }),
        "attack-blank-damage-type" => WithAttack("strike", a => a with { DamageType = " " }),
        "attack-blank-die" => WithAttack("strike", a => a with { Damage = new("1", " ") }),
        "attack-blank-count" => WithAttack("strike", a => a with { Damage = new("", "6") }),
        "attack-no-abilities" => WithAttack("strike", a => a with { Abilities = [] }),
        "attack-die-invalid" => WithAttack("strike", a => a with { Damage = new("1", "6 +") }),
        "attack-count-invalid" => WithAttack("strike", a => a with { Damage = new("1 +", "6") }),
        "attack-bonus-invalid" => WithAttack("strike", a => a with { DamageBonus = "2 +" }),
        "attack-damage-type" => WithAttack("strike", a => a with { DamageType = "sparkles" }),
        "attack-tag-invalid" => WithAttack("strike", a => a with { Tags = ["Bad Tag"] }),
        "attack-cost-without-resource" => WithAttack("bolt", a => a with { ResourceId = null, ResourceContent = null }),
        "attack-resource-own-lacks" => WithAttack("bolt", a => a with { ResourceContent = null }),
        "attack-resource-content-lacks" => WithAttack("bolt", a => a with { ResourceContent = Fixtures.KindledMode.ContentId }),
        "attack-resource-content-not-installed" => WithAttack("bolt", a => a with { ResourceContent = Absent }),
        "attack-cost-invalid" => WithAttack("bolt", a => a with { Cost = "1 +" }),
        "attack-while-toggle-unknown" => WithAttack("strike", a => a with { WhileToggle = new(null, "nonesuch") }),
        "attack-while-toggle-content-lacks" => WithAttack("strike", a => a with { WhileToggle = new(Fixtures.EmberFist.ContentId, "kindled") }),
        "attack-while-toggle-not-installed" => WithAttack("strike", a => a with { WhileToggle = new(Absent, "kindled") }),
        "rider-no-tags" => WithRider(r => r with { Tags = [] }),
        "rider-no-effect" => WithRider(r => r with { Dice = null }),
        "rider-dice-bad-die" => WithRider(r => r with { Dice = "3d7" }),
        "rider-dice-zero" => WithRider(r => r with { Dice = "0d8" }),
        "rider-dice-too-many" => WithRider(r => r with { Dice = "101d8" }),
        "rider-dice-with-bonus" => WithRider(r => r with { Dice = "3d8+1" }),
        "rider-dice-no-count" => WithRider(r => r with { Dice = "d8" }),
        "rider-dice-padded" => WithRider(r => r with { Dice = " 3d8" }),
        "rider-to-hit-invalid" => WithRider(r => r with { ToHit = "1 +" }),
        "rider-bonus-invalid" => WithRider(r => r with { DamageBonus = "2 +" }),
        "rider-damage-type" => WithRider(r => r with { DamageType = "sparkles" }),
        "rider-tag-invalid" => WithRider(r => r with { Tags = ["unarmed", "Bad Tag"] }),
        "rider-toggle-unknown" => WithRider(r => r with { Toggle = "nonesuch" }),
        "rider-toggle-content-without-toggle" => WithRider(r => r with { Toggle = null, ToggleContent = Fixtures.KindledMode.ContentId }),
        "rider-toggle-content-lacks" => WithRider(r => r with { ToggleContent = Fixtures.KindledMode.ContentId }),
        "rider-toggle-content-not-installed" => WithRider(r => r with { Toggle = "kindled", ToggleContent = Absent }),
        "roll-while-toggle-unknown" => WithFlash(r => r with { WhileToggle = new(null, "nonesuch") }),
        "roll-while-toggle-content-lacks" => WithFlash(r => r with { WhileToggle = new(Fixtures.EmberFist.ContentId, "kindled") }),
        "roll-while-toggle-blank-not-installed" => WithFlash(r => r with { WhileToggle = new(Absent, " ") }),
        "attack-while-toggle-blank-not-installed" => WithAttack("strike", a => a with { WhileToggle = new(Absent, "") }),
        "rider-toggle-blank-not-installed" => WithRider(r => r with { Toggle = "", ToggleContent = Absent }),
        "modifier-toggle-blank-not-installed" => WithKeen(m => m with { Toggle = " ", ToggleContent = Absent }),
        // A primary (not an overlay) naming an overlay of its own group: the not-primary warning would fire if its overlay-only guard were removed.
        "overlay-with-allowed-with-not-overlay" => WithSurge(t => t with { Layer = ToggleLayer.Primary, AllowedWith = [new ToggleRef(Fixtures.BoundOverlay.ContentId, "bound")] }),
        "restriction-resource-target" => Only(new RestrictionEffect { Id = "r", Field = "resource.reflex", Minimum = 1 }),
        "roll-while-toggle-not-installed" => WithFlash(r => r with { WhileToggle = new(Absent, "kindled") }),
        "modifier-own-toggle-unknown" => WithKeen(m => m with { Toggle = "nonesuch", ToggleContent = null }),
        "modifier-toggle-content-lacks" => WithKeen(m => m with { ToggleContent = Fixtures.EmberFist.ContentId }),
        "modifier-toggle-content-without-toggle" => WithKeen(m => m with { Toggle = null, Timing = EffectTiming.Always }),
        "modifier-toggle-content-not-installed" => WithKeen(m => m with { ToggleContent = Absent }),
        "resource-target-unknown" => WithReflexBonus(m => m with { Target = "resource.nonesuch" }),
        "resource-target-empty" => WithReflexBonus(m => m with { Target = "resource." }),
        "resource-target-other-revision" => WithKeen(m => m with { Target = "resource.reflex" }),
        "resource-target-set" => WithReflexBonus(m => m with { Operation = ModifierOperation.Set }),
        "resource-target-replace" => WithReflexBonus(m => m with { Operation = ModifierOperation.Replace }),
        "attack-below-v11" => Pack(Fixtures.OpenHandStrike) with { SchemaVersion = 10 },
        "rider-below-v11" => Pack(Fixtures.EmberFist) with { SchemaVersion = 10 },
        "modifier-toggle-content-only" => KeenOnly(),
        "roll-while-toggle-only" => Only(new RollEffect { Id = "r", RollId = "r", Label = "R", Dice = "1d6", WhileToggle = new(Fixtures.KindledMode.ContentId, "kindled") }),
        "resource-target-only" => Only(new ResourceEffect { Id = "pool", ResourceId = "pool", Label = "Pool", Maximum = "2" },
            new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = "resource.pool", Value = "1" }),
        "attack-only" => Only(new AttackEffect { Id = "a", AttackId = "a", Label = "A", Kind = WeaponAttack.Melee, Abilities = [Ability.Str], Damage = new("1", "6"), DamageType = "fire" }),
        "rider-only" => Only(new AttackRiderEffect { Id = "r", Tags = ["all"], Dice = "1d8" }),
        "allowed-with-overlay" => WithSurge(t => t with { AllowedWith = [new ToggleRef(Fixtures.BoundOverlay.ContentId, "bound")] }),
        "allowed-with-other-group" => WithSurge(t => t with { AllowedWith = [new ToggleRef(Fixtures.IronStance.ContentId, "iron")] }),
        "allowed-with-ungrouped" => WithSurge(t => t with { AllowedWith = [new ToggleRef(Fixtures.EmberFist.ContentId, "fist")] }),
        "allowed-with-primary" => WithSurge(t => t with { AllowedWith = [new ToggleRef(Fixtures.TideForm.ContentId, "tide"), new ToggleRef(Fixtures.EmberForm.ContentId, "ember")] }),
        "allowed-with-primary-of-another-source" => WithSurge(t => t with { AllowedWith = [new ToggleRef(Fixtures.LineageForm.ContentId, "lineage")] }),
        "allowed-with-not-installed" => WithSurge(t => t with { AllowedWith = [new ToggleRef(Absent, "tide")] }),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "unknown variant"),
    };

    private static ContentRevision KeenOnly() =>
        Only(new ToggleEffect { Id = "t", ToggleId = "t", Label = "T" },
            new ModifierEffect { Id = "m", Operation = ModifierOperation.Bonus, Target = "initiative", Value = "1", Timing = EffectTiming.WhileActive, Toggle = "t", ToggleContent = Fixtures.KindledMode.ContentId });

    private static ContentRevision Only(params Effect[] effects) =>
        Fixtures.RevisionWithEffect("""{"type":"modifier","id":"seed","operation":"bonus","target":"initiative","value":"1"}""", 11) with { Effects = effects };
}
