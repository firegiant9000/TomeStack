using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// SPEC C-02, C-04: an equipped weapon's attack and damage roll through the <c>roll</c> command and change nothing.
/// Original fixtures seeded by development hosts (<c>tests/RulesFixtures/fixture-pack-m2-combat.json</c>).
/// </summary>
public class AttackCommandTests
{
    private static ContentReference Ref(int n) => new(Guid.Parse($"5f6dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f6de000-0000-4000-8000-{n:D12}"));

    private static readonly ContentReference Longblade = Ref(1);
    private static readonly ContentReference Needle = Ref(2);
    private static readonly ContentReference Duelist = Ref(11);

    [Fact]
    public void Weapon_attack_and_damage_rolls_use_the_sheet_and_change_nothing()
    {
        using var temp = new TempApp();
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Test Duelist",
            RulesFamily = RulesFamilies.Srd51,
            Level = 1,
            Classes = [new(Duelist, 1)],
            BaseAbilities = new(14, 16, 12, 10, 10, 10),
            Equipment = [new(Longblade, Equipped: true), new(Needle, Equipped: false)],
        };
        var id = temp.App.SaveCharacter(character).Character.Id;
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var attack = temp.App.Roll(new(id, Weapon: Longblade, Mode: RollMode.Advantage));
        var damage = temp.App.Roll(new(id, Weapon: Longblade, Damage: true, Critical: true));
        var twoHanded = temp.App.Roll(new(id, Weapon: Longblade, Damage: true, Versatile: true));

        Assert.Equal(("1d20", 4, RollMode.Advantage), (attack.Formula, attack.Modifiers.Single().Amount, attack.Mode)); // Str +2, PB 2
        Assert.Equal(("1d8+2", true), (damage.Formula, damage.Critical));
        Assert.Equal("1d10+2", twoHanded.Formula);
        Assert.Equal("Fixture Longblade damage (slashing)", damage.Provenance!.Label);
        Assert.Equal("roll.weapon-unknown", Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, Weapon: Needle))).Problems[0].Code); // not equipped
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character));
    }

    // D43 S4 (Task 4.5a): feature attacks (fixture-pack-feature-attacks.json), rolled by content + effect id.
    private static readonly ContentReference Strike = new(Guid.Parse("6f0dc000-0000-4000-8000-000000000041"), Guid.Parse("6f0de000-0000-4000-8000-000000000041"));
    private static readonly Guid BlazingContent = Guid.Parse("6f0dc000-0000-4000-8000-000000000043");

    private static (TempApp Temp, Guid Id) Adept(Func<ContentRevision, ContentRevision>? adjust = null)
    {
        var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(5);
        temp.AddPack("fixture-pack-feature-attacks.json", adjust);
        return (temp, temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/pm-adept-l5.json")).Character.Id);
    }

    [Fact]
    public void An_amount_is_refused_on_any_roll_that_is_not_a_content_roll_effect()
    {
        var (temp, id) = Adept();
        using var _ = temp;

        var problem = Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, Content: Strike, EffectId: "strike", Amount: 2)));

        Assert.Equal("roll.amount-not-allowed", problem.Problems[0].Code);
        Assert.Equal("roll.amount-not-allowed", Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, Field: "initiative", Amount: 1))).Problems[0].Code);
    }

    [Fact]
    public void A_feature_attack_rolls_to_hit_with_its_traced_bonus()
    {
        var (temp, id) = Adept();
        using var _ = temp;
        var entry = temp.App.GetCharacter(id).Sheet.Attacks!.Single(a => a.Source == AttackSource.Feature && a.Item == Strike && a.EffectId == "strike");
        var before = TempApp.Json(temp.App.GetCharacter(id).Character);

        var record = temp.App.Roll(new(id, Content: Strike, EffectId: "strike", Mode: RollMode.Advantage));

        var expected = new SeededRandomSource(5);
        Assert.Equal(("1d20", RollMode.Advantage), (record.Formula, record.Mode));
        var modifier = record.Modifiers.Single();
        Assert.Equal(entry.ToHit, modifier.Amount);
        Assert.Equal($"{entry.Name} to hit", modifier.Label);
        Assert.Equal(entry.Origin, modifier.Origin);
        Assert.Equal(2, record.Dice.Count);
        Assert.Equal(expected.Next(20), record.Dice[0].Value);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(id).Character)); // a roll writes nothing

        // A pool-costing attack names its pool on the attack roll only, so the record offers "Spend" once.
        var bolt = temp.App.Roll(new(id, Content: Strike, EffectId: "bolt")).Provenance!;
        var boltDamage = temp.App.Roll(new(id, Content: Strike, EffectId: "bolt", Damage: true)).Provenance!;
        Assert.Equal(("focus", Guid.Parse("6f0dc000-0000-4000-8000-000000000040")), (bolt.LinkedResourceId, bolt.LinkedResourceContent));
        Assert.Null(boltDamage.LinkedResourceId);
    }

    private static readonly Guid KindledContent = Guid.Parse("6f0dc000-0000-4000-8000-000000000042");

    [Fact]
    public void An_attack_roll_and_its_damage_roll_both_carry_the_attack_trace()
    {
        var (temp, id) = Adept();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: KindledContent, ToggleId: "kindled"));
        var entry = temp.App.GetCharacter(id).Sheet.Attacks!.Single(a => a.Source == AttackSource.Feature && a.Item == Strike && a.EffectId == "strike");

        var toHit = temp.App.Roll(new(id, Content: Strike, EffectId: "strike"));
        var damage = temp.App.Roll(new(id, Content: Strike, EffectId: "strike", Damage: true));

        Assert.Equal(TempApp.Json(entry.Trace), TempApp.Json(toHit.Explain));
        Assert.Equal(entry.ToHit, toHit.Explain![^1].Result);
        Assert.Equal(toHit.Modifiers.Sum(m => m.Amount), toHit.Explain[^1].Result);
        Assert.Equal(TempApp.Json(entry.Trace), TempApp.Json(damage.Explain));
        // R6: the rider's dice step has no amount and its text ends "damage (attack rider)"; the damage step is a "set" with no amount.
        Assert.Contains(damage.Explain!, s => s.Amount is null && s.Description.EndsWith("damage (attack rider)", StringComparison.Ordinal));
        Assert.Contains(damage.Explain!, s => s.Operation == "set" && s.Amount is null && s.Description.StartsWith("Damage ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_losing_stack_group_rider_keeps_the_running_to_hit()
    {
        var (temp, id) = Adept();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: KindledContent, ToggleId: "kindled"));
        // Kindled and Blazing are one toggle group, so the second rider that shares stack group "modeRider" comes from the ungrouped Ember Fist.
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: Guid.Parse("6f0dc000-0000-4000-8000-000000000045"), ToggleId: "fist"));

        var record = temp.App.Roll(new(id, Content: Strike, EffectId: "strike"));

        Assert.Contains(record.Explain!, s => s.Operation == "rider.not-applied");
        Assert.Equal(record.Modifiers.Sum(m => m.Amount), record.Explain![^1].Result);
    }

    [Fact]
    public void A_weapon_attack_and_damage_carry_the_attack_trace()
    {
        using var temp = new TempApp();
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Test Duelist",
            RulesFamily = RulesFamilies.Srd51,
            Level = 1,
            Classes = [new(Duelist, 1)],
            BaseAbilities = new(14, 16, 12, 10, 10, 10),
            Equipment = [new(Longblade, Equipped: true)],
        };
        var id = temp.App.SaveCharacter(character).Character.Id;
        var entry = temp.App.GetCharacter(id).Sheet.Attacks!.Single(a => a.Source == AttackSource.Weapon && a.Item == Longblade);

        var toHit = temp.App.Roll(new(id, Weapon: Longblade));
        var damage = temp.App.Roll(new(id, Weapon: Longblade, Damage: true));

        Assert.Equal(TempApp.Json(entry.Trace), TempApp.Json(toHit.Explain));
        Assert.Equal(toHit.Modifiers.Sum(m => m.Amount), toHit.Explain![^1].Result);
        Assert.Equal(TempApp.Json(entry.Trace), TempApp.Json(damage.Explain));
    }

    [Fact]
    public void Rider_dice_double_on_a_critical_hit_and_flat_bonuses_do_not()
    {
        var (temp, id) = Adept();
        using var _ = temp;
        temp.App.Play(new(id, PlayActionKind.ToggleOn, Confirm: true, ContentId: BlazingContent, ToggleId: "blazing"));

        var record = temp.App.Roll(new(id, Content: Strike, EffectId: "strike", Damage: true, Critical: true));

        Assert.Equal("1d8+4d8+5", record.Formula); // Dex +3 and the +2 rider are one flat 5
        Assert.Equal(10, record.Dice.Count); // 5 dice, each doubled
        Assert.Equal(5, record.Dice.Count(d => d.FromCritical));
        Assert.Equal(record.Dice.Sum(d => d.Value) + 5, record.Total); // the constant counts once
    }

    /// <summary>MECH-X item 0: a +2 copy of the Longblade (v12), equipped by a Str +2, PB +2 Duelist.</summary>
    private static (TempApp Temp, Guid Id, ContentReference Blade) BonusBlade()
    {
        var temp = new TempApp();
        var original = temp.App.Store.FindRevision(Longblade)!;
        var blade = original with
        {
            ContentId = Guid.Parse("6a7e0300-0000-4000-8000-000000000001"),
            RevisionId = Guid.Parse("6a7e0300-0000-4000-8000-000000000101"),
            Name = "Fixture Bonus Longblade",
            SchemaVersion = 12,
            Effects = [.. original.Effects.Select(e => e is WeaponEffect w ? w with { Bonus = 2 } : e)],
        };
        temp.App.Store.AddRevision(blade);
        var character = new Character
        {
            Id = Guid.NewGuid(),
            Name = "Test Duelist",
            RulesFamily = RulesFamilies.Srd51,
            Level = 1,
            Classes = [new(Duelist, 1)],
            BaseAbilities = new(14, 16, 12, 10, 10, 10),
            Equipment = [new(blade.Reference, Equipped: true)],
        };
        return (temp, temp.App.SaveCharacter(character).Character.Id, blade.Reference);
    }

    [Fact]
    public void A_weapon_bonus_is_in_the_to_hit_and_damage_rolls_and_their_explanation()
    {
        var (temp, id, blade) = BonusBlade();
        using var _ = temp;
        var entry = temp.App.GetCharacter(id).Sheet.Attacks!.Single(a => a.Source == AttackSource.Weapon && a.Item == blade);

        var toHit = temp.App.Roll(new(id, Weapon: blade));
        var damage = temp.App.Roll(new(id, Weapon: blade, Damage: true));

        Assert.Equal(6, entry.ToHit); // Str +2, PB +2, weapon +2
        Assert.Equal(6, toHit.Modifiers.Sum(m => m.Amount));
        Assert.Equal("1d8+4", damage.Formula);
        Assert.Equal(TempApp.Json(entry.Trace), TempApp.Json(toHit.Explain));
        Assert.Equal(TempApp.Json(entry.Trace), TempApp.Json(damage.Explain));
        Assert.Contains(damage.Explain!, s => s.Description == "Fixture Bonus Longblade: +2 damage (weapon)" && s.Amount == 2);
        Assert.Equal(entry.ToHit, toHit.Explain![^1].Result);
    }

    [Fact]
    public void A_weapon_bonus_is_flat_so_a_critical_hit_doubles_the_dice_and_not_the_bonus()
    {
        var (temp, id, blade) = BonusBlade();
        using var _ = temp;

        var normal = temp.App.Roll(new(id, Weapon: blade, Damage: true));
        var critical = temp.App.Roll(new(id, Weapon: blade, Damage: true, Critical: true));

        Assert.Equal(("1d8+4", 1, 0), (normal.Formula, normal.Dice.Count, normal.Dice.Count(d => d.FromCritical)));
        Assert.Equal(("1d8+4", 2, 1), (critical.Formula, critical.Dice.Count, critical.Dice.Count(d => d.FromCritical))); // one die doubled
        Assert.Equal(critical.Dice.Sum(d => d.Value) + 4, critical.Total); // Str +2 and the weapon +2 count once
    }

    [Fact]
    public void An_attack_with_an_invalid_die_is_refused()
    {
        var (temp, id) = Adept(r => r.ContentId == Strike.ContentId
            ? r with { Effects = [.. r.Effects.Select(e => e is AttackEffect { AttackId: "strike" } a ? a with { Damage = new("1", "0") } : e)] }
            : r);
        using var _ = temp;

        var refusal = Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, Content: Strike, EffectId: "strike", Damage: true)));

        Assert.Equal("attack.die-invalid", refusal.Code);
        Assert.Equal("attack.die-invalid", refusal.Problems[0].Code);
    }

    [Fact]
    public void An_attack_outside_its_form_is_not_refused_and_rolls()
    {
        // The strike is available only while Kindled mode is on; the toggle is off, so the attack is unavailable but still calculated.
        var kindled = new ToggleRef(Guid.Parse("6f0dc000-0000-4000-8000-000000000042"), "kindled");
        var (temp, id) = Adept(r => r.ContentId == Strike.ContentId
            ? r with { Effects = [.. r.Effects.Select(e => e is AttackEffect { AttackId: "strike" } a ? a with { WhileToggle = kindled } : e)] }
            : r);
        using var _ = temp;
        var entry = temp.App.GetCharacter(id).Sheet.Attacks!.Single(a => a.Source == AttackSource.Feature && a.Item == Strike && a.EffectId == "strike");
        Assert.False(entry.Available);

        var hit = temp.App.Roll(new(id, Content: Strike, EffectId: "strike"));
        var damage = temp.App.Roll(new(id, Content: Strike, EffectId: "strike", Damage: true));

        Assert.Equal("1d20", hit.Formula);
        Assert.StartsWith("1d", damage.Formula, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attack_whose_damage_bonus_fails_is_refused()
    {
        var (temp, id) = Adept(r => r.ContentId == Strike.ContentId
            ? r with { Effects = [.. r.Effects.Select(e => e is AttackEffect { AttackId: "strike" } a ? a with { DamageBonus = "1 +" } : e)] }
            : r);
        using var _ = temp;

        var refusal = Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, Content: Strike, EffectId: "strike", Damage: true)));

        Assert.Equal("effect.invalid-formula", refusal.Code);
        Assert.Equal("effect.invalid-formula", refusal.Problems[0].Code);
    }

    [Fact]
    public void The_to_hit_roll_of_an_attack_with_an_invalid_die_is_refused()
    {
        var (temp, id) = Adept(r => r.ContentId == Strike.ContentId
            ? r with { Effects = [.. r.Effects.Select(e => e is AttackEffect { AttackId: "strike" } a ? a with { Damage = new("1", "0") } : e)] }
            : r);
        using var _ = temp;

        var refusal = Assert.Throws<AppValidationException>(() => temp.App.Roll(new(id, Content: Strike, EffectId: "strike")));

        Assert.Equal("attack.die-invalid", refusal.Code);
        Assert.Equal("attack.die-invalid", refusal.Problems[0].Code);
    }

    [Fact]
    public void A_weapon_roll_never_picks_a_feature_attack_row_of_the_same_item()
    {
        // An item revision that declares both a weapon and a feature attack: the weapon roll must take the weapon row.
        var (temp, id) = Adept(r => r.ContentId == Strike.ContentId
            ? r with
            {
                Kind = ContentKind.Item,
                // As an item the revision has no class, so the feature attack's die is a plain number instead of a scale.
                Effects =
                [
                    new WeaponEffect { Id = "blade", Category = WeaponCategory.Martial, Attack = WeaponAttack.Melee, Damage = "1d12", DamageType = "slashing", WeaponKey = "blade" },
                    .. r.Effects.Select(e => e is AttackEffect a ? a with { Damage = a.Damage with { Die = "8" } } : e),
                ],
            }
            : r);
        using var _ = temp;
        var character = temp.App.GetCharacter(id).Character;
        temp.App.SaveCharacter(character with { Equipment = [new(Strike, Equipped: true)] });

        var weapon = temp.App.Roll(new(id, Weapon: Strike, Damage: true));
        var feature = temp.App.Roll(new(id, Content: Strike, EffectId: "strike", Damage: true));

        Assert.StartsWith("1d12", weapon.Formula, StringComparison.Ordinal);
        Assert.Equal("weapon.damage", weapon.Provenance!.RollId);
        Assert.StartsWith("1d8", feature.Formula, StringComparison.Ordinal);
    }
}
