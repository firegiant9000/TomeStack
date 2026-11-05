using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 slice G1 (Task 3): an attack rider's or roll's dice from formulas, and critical / once-per-turn riders staying out
/// of the entry's damage. Riders sit on the original adept fixtures (class level 5: handDie 8) and on an original blade item.
/// </summary>
public class ScaledDiceV12SheetTests
{
    private static readonly ActiveToggle Kindled = new(Fixtures.KindledMode.ContentId, "kindled");
    private static readonly ActiveToggle Blazing = new(Fixtures.BlazingMode.ContentId, "blazing");

    internal static Character Adept(int level, params ActiveToggle[] on) =>
        Fixtures.Load("pm-adept-l5.json") with
        {
            Level = level,
            Classes = [new ClassLevel(Fixtures.OpenHandAdept, level)],
            Play = new PlayState { Toggles = on },
        };

    internal static InMemoryContentCatalog With(ContentReference reference, Func<Effect, Effect> change)
    {
        ContentPack[] packs = [Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack(), Fixtures.FormsPack(), Fixtures.TurnsPack(), Fixtures.FeatureAttacksPack()];
        var revisions = packs.SelectMany(p => p.Revisions)
            .Select(r => r.Reference == reference ? r with { SchemaVersion = Math.Max(r.SchemaVersion, 12), Effects = [.. r.Effects.Select(change)] } : r)
            .ToList();
        return new([.. packs.SelectMany(p => p.Sources)], [.. revisions]);
    }

    /// <summary>The kindled rider, rewritten: scaled dice (no literal dice, no stack group) on unarmed attacks.</summary>
    internal static InMemoryContentCatalog ScaledRider(ScaledDice scaled, Func<AttackRiderEffect, AttackRiderEffect>? more = null) =>
        With(Fixtures.KindledMode, e => e is AttackRiderEffect r
            ? (more ?? (x => x))(r with { Dice = null, StackGroup = null, Tags = ["unarmed"], ScaledDice = scaled })
            : e);

    private static CharacterSheet Sheet(Character character, InMemoryContentCatalog catalog) => CharacterCalculator.Calculate(character, catalog);

    private static AttackEntry Strike(CharacterSheet sheet) => sheet.Attacks!.Single(a => a.EffectId == "strike");

    [Theory]
    [InlineData(1, "1d6")]
    [InlineData(2, "1d6")]
    [InlineData(5, "3d6")]
    public void Scaled_rider_count_follows_class_level(int level, string dice)
    {
        var sheet = Sheet(Adept(level, Kindled), ScaledRider(new("ceil(CLASS_LEVEL/2)", "6")));

        var strike = Strike(sheet);
        var rider = Assert.Single(strike.Riders!);
        Assert.Equal(dice, rider.Dice);
        Assert.Contains("+" + dice, strike.Damage, StringComparison.Ordinal);
        Assert.Null(rider.VersatileDice);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("rider.", StringComparison.Ordinal));
    }

    [Fact]
    public void Scaled_rider_reads_a_scale()
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRider(new("SCALE.handDie", "6"))); // handDie 8 at level 5

        Assert.Equal("8d6", Assert.Single(Strike(sheet).Riders!).Dice);
        Assert.Equal("1d8+8d6+3", Strike(sheet).Damage);
    }

    [Fact]
    public void Count_zero_adds_no_dice()
    {
        var sheet = Sheet(Adept(1, Kindled), ScaledRider(new("floor(CLASS_LEVEL/3)", "6")));

        var strike = Strike(sheet);
        Assert.Equal("1d6+3", strike.Damage);
        Assert.Null(strike.Riders);
        Assert.Contains(strike.Trace, t => t.Operation == "rider.no-dice");
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Code.StartsWith("rider.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CLASS_LEVEL-6", "6")] // -1
    [InlineData("CLASS_LEVEL+96", "6")] // 101
    [InlineData("2", "7")] // not a die this app rolls
    [InlineData("NOPE", "6")] // a formula that fails
    [InlineData("", "6")] // blank
    public void Count_out_of_range_leaves_the_rider_out(string count, string die)
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRider(new(count, die)));

        var strike = Strike(sheet);
        Assert.Equal("1d8+3", strike.Damage);
        Assert.Null(strike.Riders);
        Assert.True(strike.Available);
        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.Code == "rider.dice-invalid");
        Assert.Equal((Fixtures.KindledMode, "kindled-rider"), (diagnostic.Content, diagnostic.EffectId));
    }

    [Fact]
    public void A_missing_count_leaves_the_rider_out_without_throwing()
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRider(new(null!, "6")));

        Assert.Equal("1d8+3", Strike(sheet).Damage);
        Assert.Single(sheet.Diagnostics, d => d.Code == "rider.dice-invalid");
    }

    [Fact]
    public void Critical_rider_stays_out_of_damage()
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRider(new("2", "6"), r => r with { Critical = true }));

        var strike = Strike(sheet);
        Assert.Equal("1d8+3", strike.Damage);
        var rider = Assert.Single(strike.Riders!);
        Assert.True(rider.Critical);
        Assert.False(rider.OncePerTurn);
        Assert.Equal("2d6", rider.Dice);
        Assert.Contains(strike.Trace, t => t.Description == "Fixture Kindled Mode: +2d6 on a critical hit (attack rider)");
    }

    [Fact]
    public void Once_per_turn_rider_stays_out_of_damage()
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRider(new("2", "6"), r => r with { OncePerTurn = true }));

        var strike = Strike(sheet);
        Assert.Equal("1d8+3", strike.Damage);
        var rider = Assert.Single(strike.Riders!);
        Assert.True(rider.OncePerTurn);
        Assert.False(rider.Critical);
        Assert.Equal("2d6", rider.Dice);
        Assert.Contains(strike.Trace, t => t.Description == "Fixture Kindled Mode: +2d6 once per turn, when you add it (attack rider)");
    }

    [Fact]
    public void A_flag_on_a_literal_dice_rider_works_too()
    {
        var catalog = With(Fixtures.KindledMode, e => e is AttackRiderEffect r ? r with { StackGroup = null, Critical = true } : e);

        var strike = Strike(Sheet(Adept(5, Kindled), catalog));

        Assert.Equal("1d8+3", strike.Damage);
        Assert.Equal(("3d8", true), (Assert.Single(strike.Riders!).Dice, Assert.Single(strike.Riders!).Critical));
    }

    [Fact]
    public void Critical_count_zero_adds_a_note_and_no_rider()
    {
        var sheet = Sheet(Adept(1, Kindled), ScaledRider(new("floor(CLASS_LEVEL/3)", "6"), r => r with { Critical = true }));

        Assert.Null(Strike(sheet).Riders);
        Assert.Contains(Strike(sheet).Trace, t => t.Operation == "rider.no-dice");
    }

    // The blade: a versatile 1d8 / 1d10 weapon with a die-less rider.
    private static readonly string SourceId = Fixtures.CombatPack().Sources[0].Id.ToString();

    private static AttackEntry Blade(string riderJson)
    {
        var blade = RevisionHashProperties.Read(
            "{\"contentId\":\"6a7e0300-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0300-0000-4000-8000-000000000101\",\"schemaVersion\":12"
            + ",\"kind\":\"item\",\"name\":\"Fixture Dice Blade\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"" + SourceId + "\",\"page\":{\"start\":1}},\"status\":\"published\",\"effects\":["
            + "{\"type\":\"weapon\",\"id\":\"blade\",\"category\":\"simple\",\"attack\":\"melee\",\"damage\":\"1d8\",\"versatile\":\"1d10\",\"damageType\":\"slashing\",\"properties\":[],\"weaponKey\":\"blade\"},"
            + riderJson + "]}");
        var packs = new[] { Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack() };
        var catalog = new InMemoryContentCatalog([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions), blade]);
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = [new ClassLevel(Fixtures.Duelist, 1)],
            Level = 1,
            BaseAbilities = new(14, 16, 12, 10, 10, 10),
            Equipment = [new(blade.Reference, Equipped: true)],
        };
        return Assert.Single(CharacterCalculator.Calculate(character, catalog).Attacks!, a => a.Name == "Fixture Dice Blade");
    }

    [Fact]
    public void Dieless_rider_takes_the_weapon_die_and_versatile_die()
    {
        var attack = Blade("{\"type\":\"attackRider\",\"id\":\"edge\",\"tags\":[\"weapon\"],\"scaledDice\":{\"count\":\"2\"}}");

        Assert.Equal(("1d8+2d8+2", "1d10+2d10+2"), (attack.Damage, attack.VersatileDamage));
        var rider = Assert.Single(attack.Riders!);
        Assert.Equal(("2d8", "2d10"), (rider.Dice, rider.VersatileDice));
    }

    [Fact]
    public void Dieless_critical_rider_carries_both_dice_and_leaves_damage_alone()
    {
        var attack = Blade("{\"type\":\"attackRider\",\"id\":\"edge\",\"tags\":[\"weapon\"],\"critical\":true,\"scaledDice\":{\"count\":\"2\"}}");

        Assert.Equal(("1d8+2", "1d10+2"), (attack.Damage, attack.VersatileDamage));
        var rider = Assert.Single(attack.Riders!);
        Assert.Equal(("2d8", "2d10", true), (rider.Dice, rider.VersatileDice, rider.Critical));
    }

    [Fact]
    public void A_scaled_rider_with_its_own_die_has_no_versatile_die()
    {
        var attack = Blade("{\"type\":\"attackRider\",\"id\":\"edge\",\"tags\":[\"weapon\"],\"scaledDice\":{\"count\":\"2\",\"die\":\"6\"}}");

        Assert.Equal("1d10+2d6+2", attack.VersatileDamage);
        Assert.Null(Assert.Single(attack.Riders!).VersatileDice);
    }

    [Fact]
    public void Dieless_rider_on_a_feature_attack_takes_its_evaluated_die()
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRider(new("2"), r => r with { OncePerTurn = true }));

        var rider = Assert.Single(Strike(sheet).Riders!);
        Assert.Equal(("2d8", null), (rider.Dice, rider.VersatileDice)); // handDie 8 at level 5
        var withoutFlag = Sheet(Adept(5, Kindled), ScaledRider(new("2")));
        Assert.Equal("1d8+2d8+3", Strike(withoutFlag).Damage);
        var bolt = withoutFlag.Attacks!.Single(a => a.EffectId == "bolt");
        Assert.Null(bolt.Riders); // the rider is tagged unarmed
    }

    [Fact]
    public void A_dieless_rider_takes_each_attacks_own_die()
    {
        var catalog = ScaledRider(new("2"), r => r with { Tags = ["bolt", "unarmed"] });

        var sheet = Sheet(Adept(5, Kindled), catalog);

        Assert.Equal("2d6+2d6+1", sheet.Attacks!.Single(a => a.EffectId == "bolt").Damage); // the bolt rolls 2d6
        Assert.Equal("1d8+2d8+3", Strike(sheet).Damage);
    }

    private static FeatureEffect Flash(CharacterSheet sheet) =>
        sheet.Features!.Single(f => f.Content == Fixtures.KindledMode).Effects.Single(e => e.Id == "flash");

    private static InMemoryContentCatalog ScaledRoll(ScaledDice scaled) =>
        With(Fixtures.KindledMode, e => e is RollEffect { Id: "flash" } r ? r with { Dice = null, ScaledDice = scaled } : e);

    [Fact]
    public void Scaled_roll_dice_on_feature_effect()
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRoll(new("ceil(CLASS_LEVEL/2)", "6")));

        Assert.Equal("3d6", Flash(sheet).Dice);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.EffectId == "flash");
    }

    [Fact]
    public void A_literal_roll_keeps_its_dice()
    {
        Assert.Equal("1d6", Flash(Sheet(Adept(5, Kindled), With(Fixtures.KindledMode, e => e))).Dice);
    }

    [Fact]
    public void Scaled_roll_count_zero_has_no_dice_and_no_diagnostic()
    {
        var sheet = Sheet(Adept(1, Kindled), ScaledRoll(new("floor(CLASS_LEVEL/3)", "6")));

        Assert.Null(Flash(sheet).Dice);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.EffectId == "flash");
    }

    [Theory]
    [InlineData("NOPE", "6", "effect.invalid-formula")]
    [InlineData("2", "NOPE", "effect.invalid-formula")]
    [InlineData("", "6", "effect.invalid-formula")]
    [InlineData("CLASS_LEVEL-6", "6", "roll.dice-invalid")]
    [InlineData("CLASS_LEVEL+96", "6", "roll.dice-invalid")]
    [InlineData("2", "7", "roll.dice-invalid")]
    public void Scaled_roll_that_fails_has_no_dice_and_a_diagnostic(string count, string die, string code)
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRoll(new(count, die)));

        Assert.Null(Flash(sheet).Dice);
        var diagnostic = Assert.Single(sheet.Diagnostics, d => d.EffectId == "flash");
        Assert.Equal((code, Fixtures.KindledMode), (diagnostic.Code, diagnostic.Content));
    }

    [Fact]
    public void A_missing_roll_die_has_no_dice_and_no_throw()
    {
        var sheet = Sheet(Adept(5, Kindled), ScaledRoll(new("2", null)));

        Assert.Null(Flash(sheet).Dice);
        Assert.Single(sheet.Diagnostics, d => d.EffectId == "flash");
    }

    [Fact]
    public void V11_sheet_is_unchanged()
    {
        var sheet = Sheet(Adept(5, Kindled, Blazing), Fixtures.PlayMechanicsCatalog());
        var riders = Strike(sheet).Riders!;
        Assert.NotEmpty(riders);

        var json = JsonSerializer.Serialize(Strike(sheet), RulesJson.Options);

        foreach (var rider in JsonNode.Parse(json)!["riders"]!.AsArray())
        {
            var names = rider!.AsObject().Select(p => p.Key).ToList();
            Assert.DoesNotContain("critical", names);
            Assert.DoesNotContain("oncePerTurn", names);
            Assert.DoesNotContain("versatileDice", names);
        }
        var dice = JsonNode.Parse(json)!["riders"]!.AsArray().Single(r => r!["effectId"]!.GetValue<string>() == "blazing-rider")!.AsObject();
        Assert.Equal(["content", "effectId", "dice", "toHit", "damageBonus"], dice.Select(p => p.Key).ToList());
        Assert.Equal("4d8", riders.Single(r => r.EffectId == "blazing-rider").Dice); // kindled and blazing share a group: the higher wins
    }
}
