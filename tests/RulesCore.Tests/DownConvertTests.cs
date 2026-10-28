using TomeStack.RulesCore;

namespace TomeStack.RulesCore.Tests;

public class DownConvertTests
{
    private static readonly Guid ContentId = Guid.Parse("6f0dc000-0000-4000-8000-0000000000b1");

    private static Character Base() => Fixtures.Srd521Character() with { Name = "Compat Hero" };

    private static readonly CustomItem Rope = new(Guid.Parse("6f0dc000-0000-4000-8000-0000000000b2"), "Rope", 1, null, false);

    private static Character V9() => Base() with { Xp = 300 };

    [Fact]
    public void Character_below_9_blocks_a_running_encounter()
    {
        var input = Base() with { Play = Base().Play with { Encounter = new EncounterState(3) } };

        var result = DownConvert.Below(input, 9);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new BelowChange("play.encounter", BelowAction.Blocks, "below.encounter-running", "'Compat Hero': an encounter is running. End it, then export."), change);
        Assert.True(result.Blocked);
        Assert.Same(input, result.Character);
    }

    [Fact]
    public void Character_below_9_drops_rounds_left_and_writes_v9()
    {
        var input = V9() with { Play = Base().Play with { Toggles = [new ActiveToggle(ContentId, "tideStance", RoundsLeft: 2)] } };
        Assert.Equal(10, input.SchemaVersion);

        var result = DownConvert.Below(input, 9);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new BelowChange("play.toggles[0].roundsLeft", BelowAction.Dropped, "below.rounds-left", "'Compat Hero': the rounds left on 'tideStance'."), change);
        Assert.False(result.Blocked);
        Assert.Equal(9, result.Character.SchemaVersion);
        var toggle = Assert.Single(result.Character.Play.Toggles);
        Assert.Null(toggle.RoundsLeft);
        Assert.Equal("tideStance", toggle.ToggleId);
    }

    public static TheoryData<string, string, string, Func<Character, Character>> V9Fields => new()
    {
        { "details", "details", "below.details", c => c with { Details = new CharacterDetails(Backstory: "Raised on the tide.") } },
        { "customItems", "customItems", "below.custom-items", c => c with { CustomItems = [Rope] } },
        { "customAttacks", "customAttacks", "below.custom-attacks", c => c with { CustomAttacks = [new CustomAttack(Rope.Id, "Cudgel", 3, "1d6", "bludgeoning", null)] } },
        { "damageDefences", "damageDefences", "below.damage-defences", c => c with { DamageDefences = [new DamageDefence("fire", DamageDefenceKind.Resistance)] } },
        { "companions", "companions", "below.companions", c => c with { Companions = [new Companion(Rope.Id, "Gull", null, null, 4, null, null, null)] } },
        { "play.companions", "play.companions", "below.companions", c => c with { Play = c.Play with { Companions = [new CompanionPlay(Rope.Id, 2, 0)] } } },
        { "xp", "xp", "below.xp", c => c with { Xp = 300 } },
        { "equipment attuned", "equipment[0].attuned", "below.equipment-state", c => c with { Equipment = [new EquipmentEntry(Fixtures.Bracers, Attuned: true)] } },
        { "equipment chargesSpent", "equipment[0].chargesSpent", "below.equipment-state", c => c with { Equipment = [new EquipmentEntry(Fixtures.Bracers, ChargesSpent: 2)] } },
    };

    [Theory]
    [MemberData(nameof(V9Fields))]
    public void Character_below_8_drops_each_v9_field(string label, string path, string code, Func<Character, Character> set)
    {
        var input = set(Base());
        Assert.Equal(9, input.SchemaVersion);

        var result = DownConvert.Below(input, 8);

        var change = Assert.Single(result.Changes);
        Assert.Equal(path, change.Path);
        Assert.Equal(BelowAction.Dropped, change.Action);
        Assert.Equal(code, change.Code);
        Assert.StartsWith("'Compat Hero': ", change.Message);
        Assert.False(result.Blocked);
        Assert.Equal(8, result.Character.SchemaVersion);
        Assert.NotEmpty(label);
    }

    [Fact]
    public void Character_below_8_clears_the_equipment_id_when_attuned()
    {
        var input = Base() with { Equipment = [new EquipmentEntry(Fixtures.Bracers, Attuned: true, Id: Rope.Id)] };

        var result = DownConvert.Below(input, 8);

        Assert.Equal("equipment[0].attuned", Assert.Single(result.Changes).Path);
        Assert.Null(result.Character.Equipment[0].Id);
        Assert.Null(result.Character.Equipment[0].Attuned);
    }

    [Fact]
    public void Character_below_8_silently_drops_an_equipment_id_alone()
    {
        var input = Base() with { Equipment = [new EquipmentEntry(Fixtures.Bracers, Id: Rope.Id)] };
        Assert.Equal(9, input.SchemaVersion);

        var result = DownConvert.Below(input, 8);

        var change = Assert.Single(result.Changes);
        Assert.Equal("equipment[0].id", change.Path);
        Assert.Equal(BelowAction.Silent, change.Action);
        Assert.Equal("below.equipment-state", change.Code);
        Assert.False(result.Blocked);
        Assert.Equal(8, result.Character.SchemaVersion);
        Assert.Null(result.Character.Equipment[0].Id);
    }

    [Fact]
    public void Character_below_8_blocks_a_vulnerability_entry()
    {
        var input = Base() with { DamageDefences = [new DamageDefence("fire", DamageDefenceKind.Vulnerability)] };

        var result = DownConvert.Below(input, 8);

        var change = Assert.Single(result.Changes);
        Assert.Equal("damageDefences[0]", change.Path);
        Assert.Equal(BelowAction.Blocks, change.Action);
        Assert.Equal("below.vulnerability", change.Code);
        Assert.True(result.Blocked);
        Assert.Same(input, result.Character);
    }

    [Fact]
    public void Character_below_8_lists_a_vulnerability_with_the_other_drops_and_returns_the_input()
    {
        var input = Base() with
        {
            Xp = 10,
            DamageDefences = [new DamageDefence("cold", DamageDefenceKind.Resistance), new DamageDefence("fire", DamageDefenceKind.Vulnerability)],
        };

        var result = DownConvert.Below(input, 8);

        Assert.Contains(result.Changes, c => c.Path == "xp" && c.Action == BelowAction.Dropped);
        Assert.Contains(result.Changes, c => c.Path == "damageDefences[1]" && c.Action == BelowAction.Blocks);
        Assert.DoesNotContain(result.Changes, c => c.Code == "below.damage-defences");
        Assert.Same(input, result.Character);
    }

    [Fact]
    public void Character_below_returns_same_instance_when_nothing_is_above()
    {
        var v8 = Base();
        Assert.Equal(8, v8.SchemaVersion);
        var low = DownConvert.Below(v8, 8);
        Assert.Same(v8, low.Character);
        Assert.Empty(low.Changes);

        var v10 = Base() with { Play = Base().Play with { Encounter = new EncounterState(1) } };
        var top = DownConvert.Below(v10, Character.CurrentSchemaVersion);
        Assert.Same(v10, top.Character);
        Assert.Empty(top.Changes);
    }

    [Fact]
    public void Character_below_rejects_out_of_range_target()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DownConvert.Below(Base(), 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => DownConvert.Below(Base(), Character.CurrentSchemaVersion + 1));
        Assert.Throws<ArgumentNullException>(() => DownConvert.Below(null!, 8));
    }

    [Fact]
    public void Character_below_8_lists_both_steps_for_a_v10_character()
    {
        var input = Base() with
        {
            Xp = 300,
            Play = Base().Play with { Toggles = [new ActiveToggle(ContentId, "tideStance", RoundsLeft: 2)] },
        };
        Assert.Equal(10, input.SchemaVersion);

        var result = DownConvert.Below(input, 8);

        Assert.Equal(["play.toggles[0].roundsLeft", "xp"], result.Changes.Select(c => c.Path).ToArray());
        Assert.Equal(8, result.Character.SchemaVersion);
        Assert.Null(result.Character.Xp);
        Assert.Null(Assert.Single(result.Character.Play.Toggles).RoundsLeft);
    }
}
