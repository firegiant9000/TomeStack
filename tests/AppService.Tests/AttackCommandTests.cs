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
}
