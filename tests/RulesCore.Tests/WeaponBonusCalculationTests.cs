using TomeStack.RulesCore.Tests.Properties;

namespace TomeStack.RulesCore.Tests;

/// <summary>
/// Content schema v12 (MECH-X item 0, R21): a weapon's own bonus adds to the attack's to-hit and damage after the proficiency
/// bonus, with two traced steps that end "(weapon)". Original fixture: "Fixture Bonus Blade".
/// </summary>
public class WeaponBonusCalculationTests
{
    private const string Name = "Fixture Bonus Blade";

    private static readonly AbilityScores Nimble = new(14, 16, 12, 10, 10, 10);

    private static readonly string SourceId = Fixtures.CombatPack().Sources[0].Id.ToString();

    private const string Rider = ",{\"type\":\"attackRider\",\"id\":\"edge\",\"tags\":[\"weapon\"],\"toHit\":\"1\",\"damageBonus\":\"1\"}";

    /// <summary>A finesse melee 1d8 (versatile 1d10) weapon, as a stored revision of <paramref name="version"/>; <paramref name="extra"/> sits among the weapon's keys.</summary>
    private static ContentRevision Blade(int version, string extra = "", bool rider = false) =>
        RevisionHashProperties.Read(
            "{\"contentId\":\"6a7e0200-0000-4000-8000-000000000001\",\"revisionId\":\"6a7e0200-0000-4000-8000-000000000101\",\"schemaVersion\":" + version
            + ",\"kind\":\"item\",\"name\":\"" + Name + "\",\"rulesFamilies\":[\"srd-5.2.1\"],\"provenance\":{\"sourceId\":\"" + SourceId + "\",\"page\":{\"start\":1}},\"status\":\"published\",\"effects\":["
            + "{\"type\":\"weapon\",\"id\":\"blade\",\"category\":\"simple\",\"attack\":\"melee\"," + extra
            + "\"damage\":\"1d8\",\"versatile\":\"1d10\",\"damageType\":\"slashing\",\"properties\":[\"finesse\"],\"weaponKey\":\"blade\"}"
            + (rider ? Rider : "") + "]}");

    private static CharacterSheet Sheet(ContentRevision blade)
    {
        var packs = new[] { Fixtures.Pack(), Fixtures.M1Pack(), Fixtures.SpellPack(), Fixtures.CombatPack(), Fixtures.EffectsPack(), Fixtures.MulticlassPack() };
        var catalog = new InMemoryContentCatalog([.. packs.SelectMany(p => p.Sources)], [.. packs.SelectMany(p => p.Revisions), blade]);
        var character = Fixtures.Load("srd521-ash-m1.json") with
        {
            Pins = [],
            Classes = [new ClassLevel(Fixtures.Duelist, 1)],
            Level = 1,
            BaseAbilities = Nimble,
            Equipment = [new(blade.Reference, Equipped: true)],
        };
        return CharacterCalculator.Calculate(character, catalog);
    }

    private static AttackEntry Attack(ContentRevision blade) => Assert.Single(Sheet(blade).Attacks!, a => a.Name == Name);

    private static IEnumerable<(string, int?, int)> Steps(AttackEntry attack) => attack.Trace.Select(t => (t.Description, t.Amount, t.Result));

    [Fact]
    public void A_weapon_bonus_adds_to_hit_and_damage()
    {
        // Dex +3 (finesse beats Str +2), PB +2, weapon +2.
        var attack = Attack(Blade(12, "\"bonus\":2,"));

        Assert.Equal((7, "1d8+5", "1d10+5"), (attack.ToHit, attack.Damage, attack.VersatileDamage));
        Assert.Equal([1, 2, 3, 4], attack.Trace.Select(t => t.Order));
        Assert.StartsWith("Proficiency bonus", attack.Trace[1].Description, StringComparison.Ordinal);
        var toHit = attack.Trace[2];
        var damage = attack.Trace[3];
        Assert.Equal(($"{Name}: +2 to hit (weapon)", "add", 2, 7), (toHit.Description, toHit.Operation, toHit.Amount, toHit.Result));
        Assert.Equal(($"{Name}: +2 damage (weapon)", "add", 2, 7), (damage.Description, damage.Operation, damage.Amount, damage.Result));
        Assert.Equal(attack.ToHit, attack.Trace[^1].Result);
    }

    [Fact]
    public void A_weapon_bonus_comes_before_riders()
    {
        var attack = Attack(Blade(12, "\"bonus\":2,", rider: true));

        Assert.Equal((8, "1d8+6", "1d10+6"), (attack.ToHit, attack.Damage, attack.VersatileDamage));
        var descriptions = attack.Trace.Select(t => t.Description).ToList();
        Assert.Equal(
            [$"{Name}: +2 to hit (weapon)", $"{Name}: +2 damage (weapon)", $"{Name}: +1 to hit (attack rider)", $"{Name}: +1 damage (attack rider)"],
            descriptions.Skip(2));
        Assert.Equal(attack.ToHit, attack.Trace[^1].Result);
    }

    [Fact]
    public void A_bonus_below_v12_does_nothing()
    {
        var without = Attack(Blade(11));
        var with = Attack(Blade(11, "\"bonus\":2,"));

        Assert.Equal((without.ToHit, without.Damage, without.VersatileDamage), (with.ToHit, with.Damage, with.VersatileDamage));
        Assert.Equal(Steps(without), Steps(with));
        Assert.DoesNotContain(with.Trace, t => t.Description.EndsWith("(weapon)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_revision_built_in_code_at_an_older_version_gets_nothing_from_a_set_bonus()
    {
        var typed = Blade(12, "\"bonus\":2,");
        Assert.Equal(2, Assert.IsType<WeaponEffect>(typed.Effects[0]).Bonus);

        var attack = Attack(typed with { SchemaVersion = 11 });

        Assert.Equal((5, "1d8+3"), (attack.ToHit, attack.Damage));
        Assert.DoesNotContain(attack.Trace, t => t.Description.EndsWith("(weapon)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"bonus\":0,")]
    public void No_bonus_no_steps(string extra)
    {
        var attack = Attack(Blade(12, extra));

        Assert.Equal((5, "1d8+3"), (attack.ToHit, attack.Damage));
        Assert.Equal(2, attack.Trace.Count);
        Assert.DoesNotContain(attack.Trace, t => t.Description.EndsWith("(weapon)", StringComparison.Ordinal));
    }
}
