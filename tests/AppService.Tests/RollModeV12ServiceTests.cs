using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// Content v12 (G7, Task 5): the sheet's automatic attack-roll mode reaches the weapon, feature and spell attack rolls (and initiative)
/// exactly as it reaches a field roll. Original fixtures (<c>tests/RulesFixtures/fixture-pack-roll-modes-v12.json</c>) on the combat and
/// spell packs the development hosts seed.
/// </summary>
public class RollModeV12ServiceTests
{
    private static ContentReference V12(int n) => new(Guid.Parse($"6f0dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"6f0de000-0000-4000-8000-{n:D12}"));

    private static readonly Guid PackSource = Guid.Parse("6f0d5000-0000-4000-8000-000000001301");
    private static readonly ContentReference WildSwing = V12(1301);
    private static readonly ContentReference FirstToMove = V12(1302);
    private static readonly ContentReference HardToPin = V12(1303);
    private static readonly ContentReference BraceAndAim = V12(1304);
    private static readonly ContentReference BarbedLash = V12(1305);
    private static readonly ContentReference SpellSense = V12(1306);

    private static readonly ContentReference Longblade = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000001"), Guid.Parse("5f6de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Duelist = new(Guid.Parse("5f6dc000-0000-4000-8000-000000000011"), Guid.Parse("5f6de000-0000-4000-8000-000000000011"));
    private static readonly ContentReference Arcanist = new(Guid.Parse("5f5dc000-0000-4000-8000-000000000001"), Guid.Parse("5f5de000-0000-4000-8000-000000000001"));
    private static readonly ContentReference Spark = new(Guid.Parse("5f5dc000-0000-4000-8000-000000000011"), Guid.Parse("5f5de000-0000-4000-8000-000000000011"));

    private static readonly Guid RopeId = Guid.Parse("6f0dca00-0000-4000-8000-000000001301");
    private static readonly Guid HoundId = Guid.Parse("6f0dca00-0000-4000-8000-000000001302");
    private static readonly Guid BiteId = Guid.Parse("6f0dca00-0000-4000-8000-000000001303");

    private static Character Duellist(IEnumerable<ContentReference> features, IEnumerable<ContentReference>? on = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Testy Roll Moder",
        RulesFamily = RulesFamilies.Srd521,
        Level = 1,
        Classes = [new(Duelist, 1)],
        BaseAbilities = new(14, 16, 12, 10, 10, 10),
        Equipment = [new(Longblade, Equipped: true)],
        Pins = [Duelist, .. features],
        Play = new PlayState { Toggles = [.. (on ?? []).Select(f => new ActiveToggle(f.ContentId, f == BraceAndAim ? "brace" : "swing"))] },
    };

    private static Character Caster(IEnumerable<ContentReference> features, IEnumerable<ContentReference>? on = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Testy Spell Moder",
        RulesFamily = RulesFamilies.Srd521,
        Level = 3,
        Classes = [new(Arcanist, 3)],
        BaseAbilities = new(8, 14, 12, 16, 10, 10),
        Spells = [new(Arcanist.ContentId, Spark)],
        Pins = [Arcanist, .. features],
        Play = new PlayState { Toggles = [.. (on ?? []).Select(f => new ActiveToggle(f.ContentId, f == BraceAndAim ? "brace" : "swing"))] },
    };

    private static (TempApp Temp, Guid Id) Start(Character character)
    {
        var temp = new TempApp();
        temp.App.Random = new SeededRandomSource(7);
        temp.AddPack("fixture-pack-roll-modes-v12.json");
        return (temp, temp.App.SaveCharacter(character).Character.Id);
    }

    [Fact]
    public void Weapon_roll_takes_the_automatic_attack_mode()
    {
        var (temp, id) = Start(Duellist([WildSwing], [WildSwing]));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Weapon: Longblade));

        Assert.Equal(RollMode.Advantage, record.Mode);
        Assert.Equal(2, record.Dice.Count);
        Assert.Equal(RollMode.Advantage, record.AutomaticMode);
        Assert.Equal(["Fixture: Wild Swing"], record.ModeSources);
        // The damage roll takes no mode.
        var damage = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Damage: true));
        Assert.Null(damage.AutomaticMode);

        // The toggle off: the mode is left out and the record says nothing about an automatic mode.
        var (off, offId) = Start(Duellist([WildSwing]));
        using var __ = off;
        var plain = off.App.Roll(new RollCommand(offId, Weapon: Longblade));
        Assert.Equal((RollMode.Normal, 1), (plain.Mode, plain.Dice.Count));
        Assert.Null(plain.AutomaticMode);
        Assert.Null(plain.ModeSources);
    }

    [Fact]
    public void Picking_disadvantage_against_automatic_advantage_rolls_normal()
    {
        var (temp, id) = Start(Duellist([WildSwing], [WildSwing]));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Weapon: Longblade, Mode: RollMode.Disadvantage));

        Assert.Equal((RollMode.Normal, 1), (record.Mode, record.Dice.Count));
        Assert.Equal(RollMode.Advantage, record.AutomaticMode);
        Assert.Equal(["Fixture: Wild Swing"], record.ModeSources);
    }

    [Fact]
    public void Feature_attack_roll_takes_the_mode()
    {
        var (temp, id) = Start(Duellist([WildSwing, BraceAndAim, BarbedLash], [WildSwing, BraceAndAim]));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Content: BarbedLash, EffectId: "lash"));

        // Wild Swing wants Strength; the lash uses Dexterity, so only the all-tags Brace reaches it.
        Assert.Equal((RollMode.Advantage, 2), (record.Mode, record.Dice.Count));
        Assert.Equal(RollMode.Advantage, record.AutomaticMode);
        Assert.Equal(["Fixture: Brace and Aim"], record.ModeSources);
        Assert.Null(temp.App.Roll(new RollCommand(id, Content: BarbedLash, EffectId: "lash", Damage: true)).AutomaticMode);
    }

    [Fact]
    public void Spell_attack_roll_matches_spell_or_all_tags()
    {
        // A spell-tagged mode reaches the spell attack; the melee Strength one does not.
        var (tagged, taggedId) = Start(Caster([SpellSense, WildSwing], [WildSwing]));
        using var _ = tagged;
        var sense = tagged.App.Roll(new RollCommand(taggedId, Spell: Spark, SpellAttack: true));
        Assert.Equal((RollMode.Advantage, 2), (sense.Mode, sense.Dice.Count));
        Assert.Equal(["Fixture: Spell Sense"], sense.ModeSources);

        // An all-tags mode reaches it too.
        var (all, allId) = Start(Caster([BraceAndAim], [BraceAndAim]));
        using var __ = all;
        var brace = all.App.Roll(new RollCommand(allId, Spell: Spark, SpellAttack: true));
        Assert.Equal(RollMode.Advantage, brace.AutomaticMode);
        Assert.Equal(["Fixture: Brace and Aim"], brace.ModeSources);

        // Only a melee Strength mode on: nothing reaches a ranged spell attack.
        var (none, noneId) = Start(Caster([WildSwing], [WildSwing]));
        using var ___ = none;
        var plain = none.App.Roll(new RollCommand(noneId, Spell: Spark, SpellAttack: true));
        Assert.Equal(RollMode.Normal, plain.Mode);
        Assert.Null(plain.AutomaticMode);
    }

    [Fact]
    public void Companion_and_custom_rolls_take_no_automatic_mode()
    {
        var character = Duellist([WildSwing, BraceAndAim], [WildSwing, BraceAndAim]) with
        {
            CustomAttacks = [new CustomAttack(RopeId, "Testy Rope", 3, "1d4", "bludgeoning", null)],
            Companions = [new Companion(HoundId, "Testy Hound", null, null, 12, null, [new CustomAttack(BiteId, "Testy Bite", 4, "1d6", "piercing", null)], null)],
        };
        var (temp, id) = Start(character);
        using var _ = temp;

        var custom = temp.App.Roll(new RollCommand(id, CustomAttack: RopeId));
        var companion = temp.App.Roll(new RollCommand(id, CompanionAttack: new CompanionAttackTarget(HoundId, BiteId)));

        Assert.All(new[] { custom, companion }, r =>
        {
            Assert.Equal((RollMode.Normal, 1), (r.Mode, r.Dice.Count));
            Assert.Null(r.AutomaticMode);
            Assert.Null(r.ModeSources);
        });
        Assert.All(temp.App.GetCharacter(id).Sheet.Attacks!.Where(a => a.CompanionId is not null), a => Assert.Null(a.Mode));
    }

    [Fact]
    public void Initiative_roll_has_advantage()
    {
        var (temp, id) = Start(Duellist([FirstToMove]));
        using var _ = temp;

        var record = temp.App.Roll(new RollCommand(id, Field: FieldIds.Initiative));

        Assert.Equal((RollMode.Advantage, 2), (record.Mode, record.Dice.Count));
        Assert.Equal(RollMode.Advantage, record.AutomaticMode);
        Assert.Equal(["Fixture: First to Move"], record.ModeSources);
    }

    [Fact]
    public void Fixture_features_calculate_and_are_Automatic()
    {
        var all = new[] { WildSwing, FirstToMove, HardToPin, BraceAndAim, BarbedLash, SpellSense };
        var (temp, id) = Start(Duellist(all, [WildSwing, BraceAndAim]));
        using var _ = temp;
        var sheet = temp.App.GetCharacter(id).Sheet;

        foreach (var feature in all)
            Assert.Equal(AutomationStatus.Automatic, Assert.Single(sheet.Features!, f => f.Content == feature).Automation);
        Assert.DoesNotContain(sheet.Diagnostics, d => d.Content is { } c && all.Contains(c));

        // Attacks against the character: the stance's advantage and the defence's prevent cancel; both are listed.
        Assert.Equal(RollMode.Normal, sheet.AttacksAgainst!.Automatic);
        Assert.Equal(["Fixture: Hard to Pin", "Fixture: Wild Swing"], sheet.AttacksAgainst.Reasons.Select(r => r.Source).Order());
        Assert.Equal(0, sheet.Field(FieldIds.Speed).Value); // Brace sets the speed to 0 while it is on

        // The coverage report counts every effect Automatic: 3 + 1 + 1 + 3 + 1 + 1.
        var report = temp.App.Coverage(new(PackSource));
        Assert.Equal(new CoverageCounts(Automatic: 10, Assisted: 0, Reference: 0, Unsupported: 0), report.Totals);
    }

    [Fact]
    public void Incapacitated_leaves_the_prevent_out_and_the_stance_shows_advantage()
    {
        var character = Duellist([WildSwing, HardToPin], [WildSwing]);
        var (temp, id) = Start(character with { Play = character.Play with { Conditions = ["incapacitated"] } });
        using var _ = temp;

        var against = temp.App.GetCharacter(id).Sheet.AttacksAgainst!;

        Assert.Equal(RollMode.Advantage, against.Automatic);
        Assert.Equal(["Fixture: Wild Swing"], against.Reasons.Select(r => r.Source));
    }
}
