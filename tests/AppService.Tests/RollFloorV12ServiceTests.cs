using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Content v12 (G9): a field roll applies the sheet's roll floor; nothing else does. Original fixtures
/// (<c>tests/RulesFixtures/fixture-pack-roll-floor.json</c>) on the combat pack the development hosts seed.
/// </summary>
public class RollFloorV12ServiceTests
{
    private static ContentReference V12(int n) => new(Guid.Parse($"6f13c000-0000-4000-8000-{n:D12}"), Guid.Parse($"6f13e000-0000-4000-8000-{n:D12}"));

    private static readonly Guid PackSource = Guid.Parse("6f135000-0000-4000-8000-000000001401");
    private static readonly ContentReference PractisedCalm = V12(1401);
    private static readonly ContentReference BedrockGrip = V12(1402);
    private static readonly ContentReference QuietStudy = V12(1403);
    private static readonly ContentReference ShadowHabit = V12(1404);

    private static readonly ContentReference Longblade = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000001"), Guid.Parse("5f6de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Duelist = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000011"), Guid.Parse("5f6de000-0000-4000-8000-000000000011"));

    /// <summary>Hands out the scripted faces in order (1-based faces, as the real source does).</summary>
    private sealed class ScriptedRandom(params int[] faces) : IRandomSource
    {
        private int _next;

        public int Next(int sides) => faces[_next++];
    }

    private static Character Hero(params ContentReference[] features) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Testy Floor Roller",
        RulesFamily = RulesFamilies.Srd521,
        Level = 1,
        Classes = [new(Duelist, 1)],
        BaseAbilities = new(8, 16, 12, 10, 10, 10),
        Equipment = [new(Longblade, Equipped: true)],
        Pins = [Duelist, .. features],
    };

    private static (TempApp Temp, Guid Id) Start(Character character, params int[] faces)
    {
        var temp = new TempApp();
        temp.App.Random = new ScriptedRandom(faces);
        temp.AddPack("fixture-pack-roll-floor.json");
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    [Fact]
    public void Proficient_skill_roll_raises_a_low_d20_to_10()
    {
        var (temp, id) = Start(Hero(PractisedCalm, QuietStudy), 3);
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: "skill.stealth"));

        var die = Assert.Single(record.Dice);
        Assert.Equal((10, 3), (die.Value, die.Rolled));
        Assert.Equal(15, record.Total); // 10 + Dexterity 3 + proficiency 2
        Assert.Equal(10, record.Floor!.Face);
        Assert.Null(record.Floor.Total);
        Assert.Equal(["Fixture: Practised Calm"], record.Floor.Reasons.Select(r => r.Source));
    }

    [Fact]
    public void Non_proficient_skill_roll_is_not_floored()
    {
        var (temp, id) = Start(Hero(PractisedCalm, QuietStudy), 3);
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: "skill.arcana"));

        var die = Assert.Single(record.Dice);
        Assert.Equal((3, (int?)null), (die.Value, die.Rolled));
        Assert.Equal(3, record.Total);
        Assert.Null(record.Floor);
        Assert.Null(record.Unfloored);
    }

    [Fact]
    public void Strength_check_total_is_raised_to_the_score()
    {
        var (temp, id) = Start(Hero(BedrockGrip), 2);
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: "ability.str.mod"));

        Assert.Equal(8, record.Total); // 2 - 1 = 1, raised to the Strength score of 8
        Assert.Equal(1, record.Unfloored);
        Assert.Equal(8, record.Floor!.Total);
        Assert.Null(Assert.Single(record.Dice).Rolled);
    }

    [Fact]
    public void Floored_field_roll_modifier_cites_the_value_step_not_the_floor()
    {
        var (temp, id) = Start(Hero(BedrockGrip), 2);
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: "ability.str.mod"));

        // The floor step is listed after the field's own steps (and so is in the explanation) ...
        Assert.Equal("floor", record.Explain![^1].Operation);
        // ... but the roll modifier still cites the step that set the value.
        var modifier = Assert.Single(record.Modifiers);
        Assert.NotNull(modifier.Origin);
        Assert.NotEqual(BedrockGrip, modifier.Origin!.Content);
        Assert.Equal(record.Explain.Last(t => t.Operation != "floor").Origin, modifier.Origin);
    }

    [Fact]
    public void Save_attack_damage_death_save_roll_effect_and_custom_dice_carry_no_floor()
    {
        var (temp, id) = Start(Hero(PractisedCalm, QuietStudy, BedrockGrip), 1, 1, 1, 1, 1, 1, 1);
        using var _ = temp;

        var records = new[]
        {
            temp.App.Roll(new RollCommand(id, Field: "save.str")),
            temp.App.Roll(new RollCommand(id, Weapon: Longblade)),
            temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true)),
            temp.App.Roll(new RollCommand(id, DeathSave: true)),
            temp.App.Roll(new RollCommand(id, Content: PractisedCalm, EffectId: "surge")),
            temp.App.RollDice(new RollDiceCommand("1d20")),
        };

        Assert.All(records, r => Assert.Null(r.Floor));
        Assert.All(records, r => Assert.Null(r.Unfloored));
        Assert.All(records.SelectMany(r => r.Dice), d => Assert.Null(d.Rolled));
    }

    [Fact]
    public void Field_roll_still_takes_its_automatic_mode()
    {
        var (temp, id) = Start(Hero(PractisedCalm, QuietStudy, ShadowHabit), 3, 15);
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: "skill.stealth"));

        Assert.Equal(RollMode.Advantage, record.AutomaticMode);
        Assert.Equal(2, record.Dice.Count);
        Assert.Equal([10, 15], record.Dice.Select(d => d.Value));
        Assert.Equal([3, (int?)null], record.Dice.Select(d => d.Rolled));
        Assert.Equal(20, record.Total);
    }

    [Fact]
    public void Fixture_features_are_counted_automatic()
    {
        var (temp, _) = Start(Hero(PractisedCalm), 1);
        using var __ = temp;

        var report = temp.App.Coverage(new(PackSource));

        var totals = Assert.Single(report.Sources).Totals;
        Assert.True(totals.Automatic >= 4);
        Assert.Equal((0, 0, 0), (totals.Assisted, totals.Reference, totals.Unsupported));
    }
}
