using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// F0: a weapon's mastery property shows on the sheet only for a character with a Weapon Mastery feature
/// (SRD 5.2.1 packs; characters built in memory).
/// </summary>
public class WeaponMasteryDisplayTests
{
    private static ContentPack Pack(string name) => TomeStackApp.LoadBundledPack($"TomeStack.Content.srd-5.2.1-{name}.json");

    private static ContentReference Named(ContentPack pack, string name) => pack.Revisions.Last(r => r.Name == name).Reference;

    private static string? MasteryOf(ContentPack classes, string className, string weaponName, out string? expected, AbilityScores? scores = null)
    {
        var equipment = Pack("equipment");
        var weapon = equipment.Revisions.Last(r => r.Name == weaponName);
        expected = weapon.Effects.OfType<WeaponEffect>().Single().Mastery;
        var cls = Named(classes, className);
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Test Mastery", RulesFamily = RulesFamilies.Srd521, Level = 1,
            Classes = [new(cls, 1)],
            BaseAbilities = scores ?? new(15, 14, 13, 12, 10, 8),
            Equipment = [new(weapon.Reference, Equipped: true)],
        };
        using var temp = new TempApp();
        var sheet = temp.App.SaveCharacter(character).Sheet;
        return sheet.Attacks!.Single(a => a.Item == weapon.Reference).Mastery;
    }

    [Fact]
    public void A_wizard_with_a_dagger_sees_no_mastery()
    {
        var mastery = MasteryOf(Pack("classes"), "Wizard", "Dagger", out var expected);

        Assert.NotNull(expected);
        Assert.Null(mastery);
    }

    [Fact]
    public void A_level_1_fighter_sees_the_weapons_mastery()
    {
        var mastery = MasteryOf(Pack("fighter"), "Fighter", "Dagger", out var expected);

        Assert.NotNull(expected);
        Assert.Equal(expected, mastery);
    }

    [Fact]
    public void A_level_1_barbarian_sees_the_weapons_mastery()
    {
        var mastery = MasteryOf(Pack("classes"), "Barbarian", "Greataxe", out var expected);

        Assert.NotNull(expected);
        Assert.Equal(expected, mastery);
    }
}
