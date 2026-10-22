using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// D43 slice 4 (content v11): attacks and attack riders that features give, on the sheet. Original fixtures: the adept pack
/// (<c>fixture-pack-feature-attacks.json</c>) and <c>characters/pm-adept-l5.json</c> (level 5: Str 14, Dex 16, Wis 12, PB 3,
/// handDie 8). Variants are built in memory; the fixture files are never edited.
/// </summary>
public class FeatureAttackTests
{
    private static readonly ActiveToggle Kindled = new(Fixtures.KindledMode.ContentId, "kindled");
    private static readonly ActiveToggle Blazing = new(Fixtures.BlazingMode.ContentId, "blazing");
    private static readonly ActiveToggle Fist = new(Fixtures.EmberFist.ContentId, "fist");

    private static Character Adept(params ActiveToggle[] on) =>
        Fixtures.Load("pm-adept-l5.json") with { Play = new PlayState { Toggles = on } };

    private static CharacterSheet Sheet(Character character, InMemoryContentCatalog? catalog = null) =>
        CharacterCalculator.Calculate(character, catalog ?? Fixtures.PlayMechanicsCatalog());

    private static AttackEntry Strike(CharacterSheet sheet) => sheet.Attacks!.Single(a => a.EffectId == "strike");

    private static AttackEntry Bolt(CharacterSheet sheet) => sheet.Attacks!.Single(a => a.EffectId == "bolt");

    private static InMemoryContentCatalog With(ContentReference reference, Func<Effect, Effect> change)
    {
        ContentPack[] packs = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack(), Fixtures.FeatureAttacksPack()];
        var revisions = packs.SelectMany(p => p.Revisions)
            .Select(r => r.Reference == reference ? r with { Effects = [.. r.Effects.Select(change)] } : r)
            .ToList();
        return new([.. packs.SelectMany(p => p.Sources)], [.. revisions]);
    }

    private static InMemoryContentCatalog WithStrike(Func<AttackEffect, AttackEffect> change) =>
        With(Fixtures.OpenHandStrike, e => e is AttackEffect { AttackId: "strike" } a ? change(a) : e);

    [Fact]
    public void A_feature_attack_uses_the_best_listed_ability_and_a_scale_die()
    {
        var strike = Strike(Sheet(Adept()));

        Assert.Equal((AttackSource.Feature, Fixtures.OpenHandStrike, "strike", "Open hand strike"), (strike.Source, strike.Item, strike.EffectId, strike.Name));
        Assert.Equal((WeaponAttack.Melee, (WeaponCategory?)null, Ability.Dex), (strike.Attack, strike.Category, strike.Ability)); // Dex +3 beats Str +2
        Assert.Equal((6, "1d8+3", "bludgeoning", true, true), (strike.ToHit, strike.Damage, strike.DamageType, strike.Proficient, strike.Available)); // +3 and PB 3; handDie 8 at level 5
        Assert.Contains(strike.Trace, t => t.Description.Contains("best of Str, Dex", StringComparison.Ordinal));
        Assert.Contains(strike.Trace, t => t.Description.Contains("Fixture Open Hand Adept handDie", StringComparison.Ordinal));
        Assert.Equal(AutomationStatus.Automatic, strike.Automation);
        Assert.Empty(strike.Warnings);
    }

    [Fact]
    public void A_single_listed_ability_is_not_called_a_best_of()
    {
        var bolt = Bolt(Sheet(Adept()));

        Assert.Equal((Ability.Wis, 4, "2d6+1"), (bolt.Ability, bolt.ToHit, bolt.Damage)); // Wis +1 and PB 3
        Assert.DoesNotContain(bolt.Trace, t => t.Description.Contains("best of", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_tie_between_listed_abilities_goes_to_the_first_listed()
    {
        var catalog = WithStrike(a => a with { Abilities = [Ability.Con, Ability.Wis] }); // Con 12 and Wis 12: both +1

        Assert.Equal(Ability.Con, Strike(Sheet(Adept(), catalog)).Ability);
    }

    [Fact]
    public void A_feature_attack_without_proficiency_omits_the_proficiency_bonus()
    {
        var strike = Strike(Sheet(Adept(), WithStrike(a => a with { Proficient = false })));

        Assert.Equal((3, false), (strike.ToHit, strike.Proficient));
        Assert.DoesNotContain(strike.Trace, t => t.Description.Contains("Proficiency", StringComparison.Ordinal));
    }

    [Fact]
    public void A_feature_attack_carries_its_pool_cost_range_and_tags()
    {
        var bolt = Bolt(Sheet(Adept()));

        Assert.Equal(("focus", 1, (Guid?)Fixtures.OpenHandAdept.ContentId), (bolt.ResourceId, bolt.Cost, bolt.CostResourceContent));
        Assert.Equal(("60 ft", WeaponAttack.Ranged, false, "force"), (bolt.Range, bolt.Attack, bolt.ReplacesAttack, bolt.DamageType));
        Assert.Equal(["bolt", "singleTarget"], bolt.Tags);
        Assert.Null(bolt.Activation);
        Assert.Null(Strike(Sheet(Adept())).ResourceId);
    }

    [Fact]
    public void A_pool_without_a_declared_content_is_the_declaring_features_own_and_a_cost_formula_is_evaluated()
    {
        var catalog = With(Fixtures.OpenHandStrike, e => e is AttackEffect { AttackId: "bolt" } a
            ? a with { ResourceContent = null, Cost = "LEVEL - 3", ReplacesAttack = true, Activation = Activation.BonusAction }
            : e);

        var bolt = Bolt(Sheet(Adept(), catalog));

        Assert.Equal((Fixtures.OpenHandStrike.ContentId, 2, true, Activation.BonusAction), (bolt.CostResourceContent, bolt.Cost, bolt.ReplacesAttack, bolt.Activation));
    }

    [Fact]
    public void Two_attack_effects_with_the_same_attackId_are_two_rows()
    {
        var catalog = With(Fixtures.OpenHandStrike, e => e is AttackEffect { AttackId: "bolt" } a ? a with { AttackId = "strike" } : e);

        var attacks = Sheet(Adept(), catalog).Attacks!;

        Assert.Equal(["strike", "bolt"], attacks.Select(a => a.EffectId));
        Assert.Equal(["Open hand strike", "Focus bolt"], attacks.Select(a => a.Name));
    }

    [Fact]
    public void A_rider_adds_dice_only_to_matching_tags_while_its_toggle_is_on()
    {
        var withLongblade = Adept(Kindled) with { Equipment = [new(Fixtures.Longblade, Equipped: true)] };
        var on = Sheet(withLongblade);

        Assert.Equal("1d8+3d8+3", Strike(on).Damage);
        Assert.Equal("2d6+3d8+1", Bolt(on).Damage);
        var rider = Assert.Single(Strike(on).Riders!);
        Assert.Equal((Fixtures.KindledMode, "kindled-rider", "3d8", 0, 0), (rider.Content, rider.EffectId, rider.Dice, rider.ToHit, rider.DamageBonus));
        Assert.Contains(Strike(on).Trace, t => t.Description.Contains("Fixture Kindled Mode", StringComparison.Ordinal) && t.Description.Contains("3d8", StringComparison.Ordinal));
        // a weapon attack never gets the kindled rider (tags unarmed and singleTarget)
        var blade = on.Attacks!.Single(a => a.Source == AttackSource.Weapon);
        Assert.Equal(("1d8+2", (IReadOnlyList<RiderValue>?)null), (blade.Damage, blade.Riders));

        var off = Sheet(withLongblade with { Play = new PlayState() });
        Assert.Equal(("1d8+3", "2d6+1"), (Strike(off).Damage, Bolt(off).Damage));
        Assert.Null(Strike(off).Riders);
    }

    [Fact]
    public void A_rider_with_to_hit_a_damage_bonus_and_a_damage_type_adds_all_three()
    {
        var catalog = With(Fixtures.KindledMode, e => e is AttackRiderEffect r ? r with { ToHit = "1", DamageBonus = "2", DamageType = "fire" } : e);

        var strike = Strike(Sheet(Adept(Kindled), catalog));

        Assert.Equal((7, "1d8+3d8+5"), (strike.ToHit, strike.Damage));
        var rider = Assert.Single(strike.Riders!);
        Assert.Equal((Fixtures.KindledMode, "kindled-rider", "3d8", 1, 2, "fire"), (rider.Content, rider.EffectId, rider.Dice, rider.ToHit, rider.DamageBonus, rider.DamageType));
        Assert.Equal(strike.ToHit, strike.Trace[^1].Result);
        Assert.Contains(strike.Trace, t => t.Description.Contains("Fixture Kindled Mode", StringComparison.Ordinal) && t.Amount == 1);
    }

    [Fact]
    public void A_flat_rider_on_all_applies_to_weapons_too()
    {
        var withLongblade = Adept(Blazing) with { Equipment = [new(Fixtures.Longblade, Equipped: true)] };

        var sheet = Sheet(withLongblade);

        var blade = sheet.Attacks!.Single(a => a.Source == AttackSource.Weapon);
        Assert.Equal("1d8+4", blade.Damage); // Str +2, and 2 more from the all rider
        Assert.Equal("blazing-bonus", Assert.Single(blade.Riders!).EffectId);
        Assert.Contains(blade.Trace, t => t.Description.Contains("Fixture Blazing Mode", StringComparison.Ordinal));
        Assert.Equal(2, blade.ToHit); // to hit unchanged: Str +2, and no weapon proficiency is recorded (the adept class lists none)
    }

    [Fact]
    public void Highest_rider_in_a_stack_group_applies()
    {
        var strike = Strike(Sheet(Adept(Kindled, Fist)));

        Assert.Equal("1d8+3d8+3", strike.Damage); // 3d8 (13.5) over 1d8 (4.5)
        Assert.Equal("kindled-rider", Assert.Single(strike.Riders!).EffectId);
        var note = Assert.Single(strike.Trace, t => t.Operation == "rider.not-applied");
        Assert.Contains("Fixture Ember Fist", note.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(Strike(Sheet(Adept(Kindled))).Trace, t => t.Operation == "rider.not-applied");
        Assert.DoesNotContain(strike.Warnings, w => w.Code == "rider.not-applied");
    }

    [Fact]
    public void A_lone_rider_of_a_stack_group_applies()
    {
        var strike = Strike(Sheet(Adept(Fist)));

        Assert.Equal("1d8+1d8+3", strike.Damage);
        Assert.Equal("fist-rider", Assert.Single(strike.Riders!).EffectId);
    }

    [Fact]
    public void Two_forms_never_both_add_riders()
    {
        var strike = Strike(Sheet(Adept(Kindled, Blazing))); // blazing is stored last, so it replaces kindled in the group

        Assert.Contains("4d8", strike.Damage, StringComparison.Ordinal);
        Assert.DoesNotContain("3d8", strike.Damage, StringComparison.Ordinal);
        Assert.Equal(["blazing-rider", "blazing-bonus"], strike.Riders!.Select(r => r.EffectId));
    }

    [Fact]
    public void A_scale_die_unarmed_attack_shows_3d8_in_one_form_and_4d8_in_another_never_both()
    {
        var kindled = Strike(Sheet(Adept(Kindled)));
        Assert.Contains("3d8", kindled.Damage, StringComparison.Ordinal);
        Assert.DoesNotContain("4d8", kindled.Damage, StringComparison.Ordinal);

        var blazing = Strike(Sheet(Adept(Kindled, Blazing)));
        Assert.Contains("4d8", blazing.Damage, StringComparison.Ordinal);
        Assert.DoesNotContain("3d8", blazing.Damage, StringComparison.Ordinal);
        Assert.Equal("1d8+4d8+5", blazing.Damage); // the all rider adds 2 to the flat 3
    }

    [Fact]
    public void An_attack_die_that_is_not_a_die_is_unavailable()
    {
        var strike = Strike(Sheet(Adept(Kindled), WithStrike(a => a with { Damage = new("1", "7") })));

        Assert.False(strike.Available);
        Assert.Contains("d7", strike.UnavailableReason, StringComparison.Ordinal);
        Assert.DoesNotContain("d7", strike.Damage, StringComparison.Ordinal);
        Assert.Null(strike.Riders);
    }

    [Theory]
    [InlineData("1", "7", "d7")]
    [InlineData("1", "0", "d0")]
    [InlineData("1", "3", "d3")]
    [InlineData("1", "SCALE.handDie - 1", "d7")]
    [InlineData("0", "8", "0")]
    [InlineData("101", "8", "101")]
    [InlineData("1", "SCALE.nonesuch", "die")]
    [InlineData("1 +", "8", "count")]
    public void A_bad_die_or_count_lists_the_attack_as_unavailable_with_a_diagnostic(string count, string die, string reason)
    {
        var sheet = Sheet(Adept(), WithStrike(a => a with { Damage = new(count, die) }));

        var strike = Strike(sheet);
        Assert.False(strike.Available);
        Assert.Contains(reason, strike.UnavailableReason, StringComparison.Ordinal);
        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "attack.die-invalid");
        Assert.Equal((Fixtures.OpenHandStrike, "strike"), (diagnostic.Content, diagnostic.EffectId));
        Assert.Contains(sheet.Attacks!, a => a.EffectId == "bolt" && a.Available); // the other attack is untouched
    }

    [Fact]
    public void A_valid_attack_has_no_die_diagnostic_and_the_sheet_lists_the_attacks_in_order()
    {
        var sheet = Sheet(Adept());

        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code == "attack.die-invalid");
        Assert.Equal(["strike", "bolt"], sheet.Attacks!.Select(a => a.EffectId));
    }

    [Fact]
    public void A_weapon_keeps_its_numbers_and_has_the_weapon_source()
    {
        var sheet = Sheet(Adept() with { Equipment = [new(Fixtures.Longblade, Equipped: true)] });

        var blade = sheet.Attacks!.Single(a => a.Source == AttackSource.Weapon);
        Assert.Equal((WeaponCategory.Martial, "1d8+2", true, (string?)null, (int?)null), (blade.Category, blade.Damage, blade.Available, blade.UnavailableReason, blade.Cost));
        Assert.Null(blade.Riders);
        Assert.False(blade.ReplacesAttack);
    }
}
